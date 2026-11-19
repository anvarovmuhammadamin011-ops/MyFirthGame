using System;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using AndijonSaltanatlar.Server.Authentication;
using AndijonSaltanatlar.Server.Data;
using AndijonSaltanatlar.Server.Hubs;
using AndijonSaltanatlar.Server.Services;
using AndijonSaltanatlar.Server.Simulation;

var builder = WebApplication.CreateBuilder(args);

// Mahalliy maxfiy sozlamalar (Google ClientId/ClientSecret): server papkasidagi google.local.json.
// Fayl git'ga qo'shilmaydi (.gitignore). Buyruq qatori argumentlari baribir ustun turadi.
builder.Configuration.AddJsonFile("google.local.json", optional: true, reloadOnChange: false);
builder.Configuration.AddCommandLine(args);

// --- Ma'lumotlar bazasi (PostgreSQL / EF Core) ---
// Provider: "Postgres" (production / docker-compose) yoki "Sqlite" (PostgreSQL o'rnatilmagan lokal LAN sinovi).
// Ikkalasida ham ma'lumot diskda saqlanadi — server qayta ishga tushganda yo'qolmaydi.
string provider = builder.Configuration["Database:Provider"] ?? "Postgres";
if (string.Equals(provider, "Sqlite", StringComparison.OrdinalIgnoreCase))
{
    string sqlite = builder.Configuration["Database:Sqlite"] ?? "Data Source=andijon_server.db";
    builder.Services.AddDbContext<GameDbContext>(opt => opt.UseSqlite(sqlite));
}
else
{
    string connStr = builder.Configuration.GetConnectionString("Default")
        ?? "Host=localhost;Port=5432;Database=andijon;Username=andijon;Password=andijon";
    builder.Services.AddDbContext<GameDbContext>(opt => opt.UseNpgsql(connStr));
}

// --- Parol hashlash (ASP.NET Identity) ---
builder.Services.AddScoped<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();

// --- JWT autentifikatsiya ---
string jwtKey = builder.Configuration["Jwt:Key"] ?? Environment.GetEnvironmentVariable("ANDIJON_JWT_KEY") ?? "";
if (jwtKey.Length < 32) jwtKey = "DEV-ONLY-INSECURE-KEY-change-me-in-production-32b"; // faqat lokal dev uchun
var jwtService = new JwtTokenService(jwtKey);
builder.Services.AddSingleton(jwtService);
builder.Services.AddSingleton(new AndijonSaltanatlar.Server.Authentication.SessionTokenService(jwtService));
// Kirish endpoint'larini suiiste'moldan himoya: har IP uchun daqiqasiga 20 so'rov
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddPolicy("auth", ctx => System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "?",
        _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1) }));
});
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt =>
    {
        opt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
        // SignalR: tokenni query-string'dan olish
        opt.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var accessToken = ctx.Request.Query["access_token"];
                var path = ctx.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/gamehub"))
                    ctx.Token = accessToken;
                return System.Threading.Tasks.Task.CompletedTask;
            },
            // Bekor qilingan (chiqilgan) sessiyaning tokeni muddati tugamagan bo'lsa ham rad etiladi
            OnTokenValidated = ctx =>
            {
                string sid = ctx.Principal?.FindFirst("sid")?.Value;
                if (!string.IsNullOrEmpty(sid))
                {
                    var db = ctx.HttpContext.RequestServices.GetRequiredService<GameDbContext>();
                    if (AndijonSaltanatlar.Server.Authentication.SessionTokenService.IsRevoked(db, sid)) ctx.Fail("Sessiya bekor qilingan");
                }
                return System.Threading.Tasks.Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();

// --- Google orqali kirish (yagona kirish usuli) ---
var googleOptions = AndijonSaltanatlar.Server.Authentication.GoogleAuthOptions.From(builder.Configuration);
builder.Services.AddSingleton(googleOptions);
var vipOptions = AndijonSaltanatlar.Server.Authentication.VipOptions.From(builder.Configuration);
builder.Services.AddSingleton(vipOptions);
builder.Services.AddHttpClient("google", c => c.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddSingleton<AndijonSaltanatlar.Server.Authentication.GoogleLoginService>();

builder.Services.AddScoped<BuildingService>();
builder.Services.AddScoped<AiPlanValidationService>();
builder.Services.AddSingleton<IServerClock, SystemClock>();
builder.Services.AddScoped<OnlineGameService>();
builder.Services.AddScoped<KingdomService>();
builder.Services.AddScoped<DiplomacyService>();
builder.Services.AddScoped<WarService>();
builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddHostedService<WorldSimulationService>();

var app = builder.Build();

// --- Bazani yaratish (migratsiyalar o'rniga dev uchun EnsureCreated) ---
// DB mavjud bo'lmasa server ishdan to'xtamaydi — ogohlantirish yoziladi.
try
{
    using IServiceScope scope = app.Services.CreateScope();
    GameDbContext db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
    db.Database.EnsureCreated();
    // Mavjud bazaga yangi jadval/ustunlarni qo'shadi (ma'lumot saqlanadi).
    SchemaUpgrader.Upgrade(db, app.Logger);
    // "Barcha shaharlar AI davlat" rejimi: mavjud dunyolardagi egasiz hududlar viloyat AI davlatlariga beriladi
    WorldSeeder.AiOwnsAllCities = app.Configuration.GetValue<bool>("World:AiOwnsAllCities");
    if (WorldSeeder.AiOwnsAllCities)
    {
        int filled = 0;
        foreach (int wid in db.Worlds.Select(w => w.Id).ToList())
            filled += WorldSeeder.FillNeutralWithAiAsync(db, wid).GetAwaiter().GetResult();
        app.Logger.LogInformation("Barcha shaharlar AI davlat: {Count} ta egasiz hudud AI davlatlarga berildi.", filled);
    }
    app.Logger.LogInformation("Ma'lumotlar bazasiga ulanildi ({Provider}).", provider);
}
catch (Exception ex)
{
    app.Logger.LogWarning(ex, "Ma'lumotlar bazasiga ulanib bo'lmadi. Server ishlaydi, lekin auth/saqlash funksiyalari DB talab qiladi. PostgreSQL'ni ishga tushiring (docker compose up).");
}

if (googleOptions.IsConfigured)
    app.Logger.LogInformation("Google kirish sozlangan (ClientId ...{Tail}).", googleOptions.ClientId.Length > 6 ? googleOptions.ClientId[^6..] : "");
else if (googleOptions.DevLogin)
    app.Logger.LogWarning("Google:DevLogin YOQILGAN — sinov sahifasi haqiqiy Google o'rnida. Production'da o'chiring!");
else
    app.Logger.LogWarning("Google kirish sozlanmagan: Google:ClientId va Google:ClientSecret kiriting (docs/GOOGLE_LOGIN.md).");

// Tunnel (Cloudflare) yoki reverse-proxy ortida: tashqi manzil https bo'lsa — kirish havolalari ham https bo'ladi.
// Faqat shu kompyuterdagi proksi (localhost) sarlavhalariga ishoniladi (standart KnownProxies = loopback).
app.UseForwardedHeaders(new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto });

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/", () => "Andijon: Saltanatlar — Server ishlayapti. API: /api/auth/google, /api/save, /api/game, /api/online, Hub: /gamehub");
app.MapGet("/health", () => Results.Ok(new { ok = true, provider, utc = DateTime.UtcNow }));
// Kirish ekranidagi havolalar (Google OAuth rozilik ekrani uchun ham kerak)
app.MapGet("/privacy", () => Results.Content(AndijonSaltanatlar.Server.Controllers.LoginPages.Message("Maxfiylik siyosati",
    "O'yin Google hisobingizdan faqat identifikator (sub), email va ismni oladi (openid, email, profile). Parolingiz va pochtangiz o'qilmaydi. " +
    "Email faqat hisobingizni tiklash uchun saqlanadi va boshqa o'yinchilarga ko'rsatilmaydi — ular faqat siz tanlagan taxallusni ko'radi. " +
    "Sessiya tokenlari qurilmangizda shifrlangan holda saqlanadi; \"Hisobdan chiqish\" ularni o'chiradi. Ma'lumotlarni o'chirish uchun server administratoriga murojaat qiling.", true), "text/html; charset=utf-8"));
app.MapGet("/terms", () => Results.Content(AndijonSaltanatlar.Server.Controllers.LoginPages.Message("Foydalanish shartlari",
    "Andijon: Saltanatlar — tarixiy strategiya o'yini. Boshqa o'yinchilarni haqoratlamang, xatolardan foydalanib o'yin muvozanatini buzmang, " +
    "bir kishi bir hisobdan foydalansin. Qoidabuzar hisoblar cheklanishi mumkin. O'yin \"boricha\" taqdim etiladi.", true), "text/html; charset=utf-8"));
app.MapControllers();
app.MapHub<GameHub>("/gamehub");

app.Run();
