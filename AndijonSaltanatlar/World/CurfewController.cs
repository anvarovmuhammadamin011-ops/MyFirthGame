using System;
using System.Collections.Generic;
using System.Linq;
using AndijonSaltanatlar.Buildings;
using AndijonSaltanatlar.Core;
using AndijonSaltanatlar.Units;
using Microsoft.Xna.Framework;

namespace AndijonSaltanatlar.World
{
    /// <summary>
    /// QO'NG'IROQ VA KECH: shahar markazidagi ogohlantirish qo'ng'irog'i chalinsa (yoki kech tushsa) aholi
    /// uylariga kirib ketadi (ko'rinmas bo'ladi), tong otib/qo'ng'iroq tinchigach yana tashqariga chiqadi.
    /// Boshqa tizim band qilgan (dehqon, cho'milayotgan, boshqariladigan) odamlar tegilmaydi.
    /// </summary>
    public sealed class CurfewController
    {
        private sealed class Hidden
        {
            public List<Vector2> Saved;
            public float SavedPause;
            public int Phase;     // 0 — uyga ketyapti, 1 — ichkarida (ko'rinmas)
            public Vector2 Door;
        }

        private readonly Dictionary<CitizenUnit, Hidden> _managed = new Dictionary<CitizenUnit, Hidden>();
        private UnitManager _owner;
        private float _bellTimer;      // qo'ng'iroq chalingan — qolgan majburiy vaqt
        private bool _placed;
        private float _retryTimer;

        public bool Active => _bellTimer > 0f;

        /// <summary>Qo'ng'iroqni chaladi — belgilangan vaqtga xalq uyga kiradi.</summary>
        public void Ring(GameStateManager state)
        {
            _bellTimer = 25f;
            state?.ShowMessage("Qo'ng'iroq chalindi! Xalq uylariga kirmoqda.");
        }

        public void Update(float dt, MapManager map, BuildingManager buildings, UnitManager units, float nightAmount, Unit possessed, Func<Unit, bool> busy)
        {
            if (!ReferenceEquals(units, _owner)) { _managed.Clear(); _owner = units; _placed = false; }
            if (_bellTimer > 0f) _bellTimer -= dt;
            EnsureBell(map, buildings);

            bool night = nightAmount > 0.55f;
            bool curfew = _bellTimer > 0f || night;

            // Boshqarilayotganlarni yuritish (uyga ketish → ichkariga kirish)
            foreach (var kv in _managed.ToList())
            {
                CitizenUnit c = kv.Key; Hidden h = kv.Value;
                if (!units.Units.Contains(c)) { _managed.Remove(c); continue; }
                if (h.Phase == 0 && Vector2.Distance(c.Position, h.Door) < 0.9f)
                {
                    h.Phase = 1;
                    c.Hidden = true;                 // uy ichida — ko'rinmaydi
                    c.PatrolPoints.Clear();
                    c.DriveIdle();
                }
            }

            if (curfew)
            {
                // Bo'sh fuqarolarni uyga jo'natish (har kadr bir nechta — og'ir bo'lmasin)
                int budget = 4;
                foreach (Unit u in units.Units)
                {
                    if (budget <= 0) break;
                    if (!(u is CitizenUnit c) || c.IsDown || c.Hidden || c == possessed || c.ExternallyControlled) continue;
                    if (_managed.ContainsKey(c) || (busy?.Invoke(c) ?? false)) continue;
                    if (c.PatrolPoints.Count < 1) continue;
                    Vector2? door = NearestHouseDoor(buildings, c.Position);
                    if (door == null) continue;
                    var h = new Hidden { Saved = new List<Vector2>(c.PatrolPoints), SavedPause = c.PauseTime, Phase = 0, Door = door.Value };
                    _managed[c] = h;
                    c.PatrolPoints.Clear();
                    c.PatrolPoints.Add(c.Position);
                    c.PatrolPoints.Add(door.Value);
                    PatrolController.RouteUnit(map, c);
                    c.PauseTime = 0f;
                    c.ResetRoute();
                    budget--;
                }
            }
            else if (_managed.Count > 0)
            {
                // Tong otdi / qo'ng'iroq tinchidi — hamma tashqariga chiqadi
                foreach (var kv in _managed.ToList())
                {
                    CitizenUnit c = kv.Key; Hidden h = kv.Value;
                    _managed.Remove(c);
                    if (!units.Units.Contains(c)) continue;
                    c.Hidden = false;
                    c.PatrolPoints.Clear();
                    c.PatrolPoints.AddRange(h.Saved);
                    c.PauseTime = h.SavedPause;
                    c.ResetRoute();
                }
            }
        }

        private void EnsureBell(MapManager map, BuildingManager buildings)
        {
            if (_placed) return;
            _retryTimer -= 0.016f;
            Building hall = buildings.Buildings.FirstOrDefault(b => b.Type == BuildingType.MainHall && b.BuildProgress >= 1f);
            if (hall == null) return;
            // Saroy oldidagi maydonga qo'ng'iroq
            var p = new Point(hall.Position.X + hall.Size.X / 2, hall.Position.Y + hall.Size.Y + 1);
            if (map.AddDecoration(DecorationType.Bell, p.X, p.Y) != null) { _placed = true; return; }
            for (int r = 1; r <= 3 && !_placed; r++)
                for (int dx = -r; dx <= r && !_placed; dx++)
                    for (int dy = -r; dy <= r && !_placed; dy++)
                        if (map.AddDecoration(DecorationType.Bell, p.X + dx, p.Y + dy) != null) _placed = true;
        }

        private static Vector2? NearestHouseDoor(BuildingManager buildings, Vector2 from)
        {
            Building best = null; float bd = float.MaxValue;
            foreach (Building b in buildings.Buildings)
            {
                if (b.Type != BuildingType.House || b.BuildProgress < 1f) continue;
                float d = Vector2.DistanceSquared(new Vector2(b.Position.X, b.Position.Y), from);
                if (d < bd) { bd = d; best = b; }
            }
            if (best == null) return null;
            return new Vector2(best.FrontEntranceTile.X + 0.5f, best.FrontEntranceTile.Y + 0.5f);
        }
    }
}
