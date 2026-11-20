using System;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AndijonSaltanatlar.Server.Data;
using AndijonSaltanatlar.Shared.DTOs;

namespace AndijonSaltanatlar.Server.Controllers
{
    /// <summary>
    /// Bulutdagi saqlash: o'yinchining shahri serverda saqlanadi va istalgan qurilmada qolgan joyidan davom etadi.
    /// Versiya nazorati: client oxirgi ko'rgan versiyani yuboradi; boshqa qurilma yangiroq saqlagan bo'lsa 409 —
    /// eski qurilma yangi progress ustidan yozib yubormaydi.
    /// </summary>
    [ApiController]
    [Route("api/save")]
    [Authorize]
    [RequestSizeLimit(MaxBytes)]
    public class SaveController : ControllerBase
    {
        /// <summary>
        /// Bitta saqlash so'rovining eng katta hajmi. Rivojlangan saltanat saqlanishi ~9 MB ga yetdi va 8 MB chegarada
        /// serverga yuklanmay qolardi (413) — zaxira bilan 32 MB.
        /// </summary>
        public const int MaxBytes = 32 * 1024 * 1024;
        private readonly GameDbContext _db;

        public SaveController(GameDbContext db) { _db = db; }

        private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");

        [HttpGet]
        public IActionResult Get()
        {
            CloudSaveEntity s = _db.CloudSaves.FirstOrDefault(x => x.UserId == UserId);
            if (s == null) return NotFound(new { messageUz = "Saqlangan o'yin yo'q — yangi saltanat boshlanadi." });
            return Ok(new CloudSaveDto { Json = s.Json, Version = s.Version, UpdatedAt = s.UpdatedAt, Device = s.Device });
        }

        [HttpPut]
        public IActionResult Put([FromBody] CloudSaveUploadRequest req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.Json)) return BadRequest(new { messageUz = "Saqlash ma'lumoti bo'sh." });
            if (!_db.Users.Any(u => u.Id == UserId)) return Unauthorized(new { messageUz = "Hisob topilmadi." });

            CloudSaveEntity s = _db.CloudSaves.FirstOrDefault(x => x.UserId == UserId);
            if (s == null)
            {
                s = new CloudSaveEntity { UserId = UserId };
                _db.CloudSaves.Add(s);
            }
            else if (!req.Force && req.BaseVersion != s.Version)
            {
                return Conflict(new { messageUz = "O'yin boshqa qurilmada davom ettirilgan.", version = s.Version });
            }

            s.Json = req.Json;
            s.Version++;
            s.UpdatedAt = DateTime.UtcNow;
            s.Device = (req.Device ?? "").Length > 40 ? req.Device.Substring(0, 40) : (req.Device ?? "");
            _db.SaveChanges();
            return Ok(new CloudSaveDto { Version = s.Version, UpdatedAt = s.UpdatedAt, Device = s.Device });
        }
    }
}
