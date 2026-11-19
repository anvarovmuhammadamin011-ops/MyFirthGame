using System;
using System.IO;
using System.Text.Json;
using Microsoft.Xna.Framework;

namespace AndijonSaltanatlar.Core
{
    /// <summary>
    /// SHOH QIYOFASI: yangi o'yinchi birinchi kirishda o'z shohini sozlaydi — ismi, tana tuzilishi
    /// (ingichka / o'rta / semiz) va bo'yi (past / o'rta / baland). Shu qurilmada (ma'lumotlar papkasida) saqlanadi.
    /// </summary>
    public static class ShahLook
    {
        public static readonly string[] BodyNames = { "Ingichka", "O'rta", "Semiz" };
        public static readonly string[] HeightNames = { "Past", "O'rta", "Baland" };
        private static readonly float[] Girths = { 0.8f, 1f, 1.26f };
        private static readonly float[] Heights = { 0.92f, 1f, 1.08f };

        public const string DefaultName = "Zahiriddin";
        public const int NameMax = 18;

        private sealed class Data
        {
            public string Name { get; set; }
            public int Body { get; set; } = 1;
            public int Height { get; set; } = 1;
        }

        private static bool _loaded;
        private static string _name = DefaultName;
        private static int _body = 1, _height = 1;

        /// <summary>Qiyofa saqlanganmi (aks holda yangi o'yinchiga sozlash ekrani ko'rsatiladi).</summary>
        public static bool Configured { get { EnsureLoaded(); return _configured; } }
        private static bool _configured;

        public static string Name { get { EnsureLoaded(); return _name; } }
        public static int Body { get { EnsureLoaded(); return _body; } }
        public static int Height { get { EnsureLoaded(); return _height; } }

        /// <summary>Chizish masshtabi: X — tana kengligi, Y — bo'y.</summary>
        public static Vector2 Scale => ScaleOf(Body, Height);

        public static Vector2 ScaleOf(int body, int height) =>
            new Vector2(Girths[Math.Clamp(body, 0, 2)], Heights[Math.Clamp(height, 0, 2)]);

        public static string CleanName(string name)
        {
            string n = (name ?? "").Trim();
            if (n.Length > NameMax) n = n.Substring(0, NameMax).Trim();
            return n.Length == 0 ? DefaultName : n;
        }

        private static string FilePath => AppPaths.File("shah.json");

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                if (!File.Exists(FilePath)) return;
                Data d = JsonSerializer.Deserialize<Data>(File.ReadAllText(FilePath));
                if (d == null) return;
                _name = CleanName(d.Name);
                _body = Math.Clamp(d.Body, 0, 2);
                _height = Math.Clamp(d.Height, 0, 2);
                _configured = true;
            }
            catch { }
        }

        /// <summary>Qiyofani o'rnatadi va saqlaydi.</summary>
        public static void Set(string name, int body, int height, bool save = true)
        {
            _loaded = true;
            _name = CleanName(name);
            _body = Math.Clamp(body, 0, 2);
            _height = Math.Clamp(height, 0, 2);
            _configured = true;
            if (!save) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, JsonSerializer.Serialize(new Data { Name = _name, Body = _body, Height = _height }));
            }
            catch { }
        }

        /// <summary>Sinovlar uchun: holatni tozalaydi (fayl qayta o'qiladi).</summary>
        public static void ResetForTests()
        {
            _loaded = false; _configured = false;
            _name = DefaultName; _body = 1; _height = 1;
        }
    }
}
