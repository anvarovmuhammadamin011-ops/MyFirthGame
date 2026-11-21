using System;
using System.Collections.Generic;
using System.Linq;
using AndijonSaltanatlar.Buildings;
using AndijonSaltanatlar.Core;
using AndijonSaltanatlar.Core.Graphics;
using AndijonSaltanatlar.Population;
using AndijonSaltanatlar.Units;
using Microsoft.Xna.Framework;

namespace AndijonSaltanatlar.World
{
    /// <summary>
    /// QURUVCHILAR QURILISH MAYDONIDA: hali qurilayotgan binolar (0 &lt; BuildProgress &lt; 1) oldida 1-2 quruvchi
    /// turib bolg'a uradi (Hammer ish pozasi), vaqti-vaqti gapiradi. Bino bitgach yoki olib tashlangach yo'qoladi.
    /// Dekorativ (haqiqiy fuqaroga bog'lanmaydi) — shuning uchun ish-joy/ehtiyoj simulyatsiyasiga ta'sir qilmaydi.
    /// </summary>
    public sealed class ConstructionLifeController
    {
        public const int MaxSites = 5;        // bir vaqtda ko'rsatiladigan qurilish maydonlari
        private const int PerSite = 2;        // har maydonda quruvchilar

        private sealed class Builder
        {
            public CitizenUnit Unit;
            public Building Site;
            public float TalkTimer;
        }

        private readonly Dictionary<string, Builder> _builders = new Dictionary<string, Builder>();
        private UnitManager _owner;
        private MapManager _map;
        private float _syncTimer;
        private readonly Random _rng = new Random();

        public bool IsWorker(Unit u)
        {
            foreach (Builder b in _builders.Values) if (b.Unit == u) return true;
            return false;
        }

        public void Update(float dt, MapManager map, BuildingManager buildings, UnitManager units, GameStateManager state)
        {
            if (!ReferenceEquals(units, _owner)) { _builders.Clear(); _owner = units; _syncTimer = 0f; }
            _map = map;
            _syncTimer -= dt;
            if (_syncTimer <= 0f) { _syncTimer = 1.5f; Sync(buildings, units); }

            foreach (Builder b in _builders.Values)
            {
                CitizenUnit w = b.Unit;
                w.Hold = true;                 // joyida turadi, bolg'a uradi
                b.TalkTimer -= dt;
                if (b.TalkTimer <= 0f && string.IsNullOrEmpty(w.SpeechText))
                {
                    string[] lines = ProfessionLooks.LinesOf(CitizenProfession.Quruvchi);
                    w.Say(lines[_rng.Next(lines.Length)], 3f);
                    b.TalkTimer = 10f + (float)_rng.NextDouble() * 10f;
                }
            }
        }

        private void Sync(BuildingManager buildings, UnitManager units)
        {
            // Hali qurilayotgan binolar (boshlangan, lekin bitmagan)
            Building hall = buildings.Buildings.FirstOrDefault(b => b.Type == BuildingType.MainHall);
            Vector2 center = hall != null ? new Vector2(hall.Position.X, hall.Position.Y) : Vector2.Zero;
            var sites = buildings.Buildings
                .Where(b => b.BuildProgress < 1f)
                .OrderBy(b => Vector2.DistanceSquared(new Vector2(b.Position.X, b.Position.Y), center))
                .Take(MaxSites).ToList();
            var keep = new HashSet<string>();

            foreach (Building site in sites)
                for (int s = 0; s < PerSite; s++)
                {
                    string key = "build:" + site.Id + ":" + s;
                    keep.Add(key);
                    if (_builders.ContainsKey(key)) continue;
                    Vector2 spot = SpotOf(site, s);
                    var unit = units.Add(new CitizenUnit(new[] { spot, spot + new Vector2(0.2f, 0f) }, CitizenArt.Builder, 0.8f));
                    unit.PauseTime = 0f;
                    unit.Hold = true;
                    unit.WorkStyle = (int)WorkStyle.Hammer;
                    unit.SetFacing(new Vector2(site.Position.X + site.Size.X * 0.5f, site.Position.Y + site.Size.Y * 0.5f) - spot);
                    _builders[key] = new Builder { Unit = unit, Site = site, TalkTimer = 2f + (float)_rng.NextDouble() * 6f };
                }

            foreach (string k in _builders.Keys.ToList())
            {
                Builder b = _builders[k];
                bool gone = !keep.Contains(k) || !buildings.Buildings.Contains(b.Site)
                            || b.Site.BuildProgress >= 1f || !units.Units.Contains(b.Unit);
                if (!gone) continue;
                units.Remove(b.Unit);
                _builders.Remove(k);
            }
        }

        /// <summary>Bino oldidagi ish nuqtasi (o'rin bo'yicha yonma-yon), yurib bo'ladigan katakka yaqin.</summary>
        private Vector2 SpotOf(Building site, int slot)
        {
            Vector2 front = new Vector2(site.FrontEntranceTile.X + 0.5f, site.FrontEntranceTile.Y + 0.5f);
            Vector2 want = front + new Vector2(-0.5f + slot * 1.0f, 0.4f);
            if (_map == null) return want;
            for (int r = 0; r < 4; r++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        int tx = (int)want.X + dx, ty = (int)want.Y + dy;
                        if (_map.IsWalkable(tx, ty)) return new Vector2(tx + 0.5f, ty + 0.5f);
                    }
            return want;
        }
    }
}
