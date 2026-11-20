using System;
using System.Collections.Generic;
using System.Linq;
using AndijonSaltanatlar.Buildings;
using AndijonSaltanatlar.Core;
using AndijonSaltanatlar.Military;
using AndijonSaltanatlar.NPC;
using AndijonSaltanatlar.Population;
using AndijonSaltanatlar.Units;
using Microsoft.Xna.Framework;

namespace AndijonSaltanatlar.World
{
    /// <summary>
    /// SHAHAR HAYOTI: har kasb egasi o'z ish joyiga boradi va kasbiga xos ishlaydi. Ekranda har binodan haqiqatan o'sha
    /// binoda ishlaydigan fuqarolardan (PopulationManager) eng ko'pi PerBuilding tasi ko'rinadi, butun shaharda MaxWorkers ta:
    ///  - ertalab uyidan asta chiqadi (Presence), ko'cha bo'ylab (A*) ish joyiga boradi;
    ///  - ish joyida kasbiga xos harakat (WorkStyle): hunarmand bolg'a uradi, konchi qazadi, baliqchi suv bo'yida qarmoq tashlaydi,
    ///    amaldor/olim yozadi, savdogar savdolashadi, tabib o't teradi; vaqti-vaqti bilan gapiradi;
    ///  - mahsulot (ma'dan, baliq, buyum) va karvon yuki bozorga yelkada olib boriladi, so'ng qaytadi;
    ///  - kechqurun uyiga qaytib, asta ichkariga kiradi; tongda yana chiqadi.
    /// Qo'shinxonalarda esa askarlar (qo'shindagi turlar ko'rinishida) qo'g'irchoq oldida mashg'ulot o'tkazadi.
    /// Faqat ko'rinish: iqtisodiy hissa kasblar soni bo'yicha EconomyManager'da (ikki marta hisoblanmaydi).
    /// Dehqonlar alohida — FarmWorkController (dala ishi va hosil).
    /// </summary>
    public sealed class WorkplaceLifeController
    {
        public const int MaxWorkers = 18;
        public const int PerBuilding = 2;
        public const int DrillPerBarracks = 3;
        private enum Phase { Leaving, ToWork, Working, Carrying, Returning, ToHome, AtHome }

        private sealed class Job
        {
            public string Key;
            public Building Work;
            public CitizenProfession Prof;
            public CitizenUnit W;
            public Phase Phase;
            public Vector2 Target, Spot, Home, Face;
            public float Timer, TalkTimer, WorkFor;
            public Building Market;
        }

        private sealed class Drill
        {
            public string Key;
            public Building Barracks;
            public Unit U;
            public float Timer;
            public int Swings;
        }

        private readonly Dictionary<string, Job> _jobs = new Dictionary<string, Job>();
        private readonly Dictionary<string, Drill> _drills = new Dictionary<string, Drill>();
        private UnitManager _owner;
        private MapManager _map;
        private float _syncTimer;
        private readonly Random _rng = new Random(4242);

        public int WorkerCount => _jobs.Count;
        public int DrillCount => _drills.Count;

        /// <summary>Bu odam shu tizimning ishchisi/askarimi (cho'milish, komendant soati va h.k. uni olib ketmasin).</summary>
        public bool IsWorker(Unit u)
        {
            foreach (Job j in _jobs.Values) if (j.W == u) return true;
            foreach (Drill d in _drills.Values) if (d.U == u) return true;
            return false;
        }

        /// <summary>Shu kasbdagi birinchi ko'rinadigan ishchi (sinov kamerasi uchun).</summary>
        public Unit FirstOf(CitizenProfession p) => _jobs.Values.FirstOrDefault(j => j.Prof == p)?.W;

        public void Update(float dt, MapManager map, BuildingManager buildings, UnitManager units, GameStateManager state)
        {
            if (!ReferenceEquals(units, _owner)) { _jobs.Clear(); _drills.Clear(); _owner = units; _syncTimer = 0f; }
            _map = map;
            _syncTimer -= dt;
            if (_syncTimer <= 0f) { _syncTimer = 3f; RefreshMarkets(buildings); Sync(buildings, units, state); }
            int hour = state.Clock.Hour;
            bool night = hour >= 20 || hour < 6;
            foreach (Job j in _jobs.Values) Step(j, dt, night);
            foreach (Drill d in _drills.Values) StepDrill(d, dt, night);
        }

        // ------------------------------------------------------------------ kim ko'rinadi
        private void Sync(BuildingManager buildings, UnitManager units, GameStateManager state)
        {
            Building hall = buildings.Buildings.FirstOrDefault(b => b.Type == BuildingType.MainHall);
            Vector2 center = hall != null ? new Vector2(hall.Position.X, hall.Position.Y) : new Vector2(GameConfig.CityCenter, GameConfig.CityCenter);
            var houses = buildings.Buildings.Where(b => b.Type == BuildingType.House && b.BuildProgress >= 1f).ToList();

            // 1) Kerakli ishchilar: (bino, kasb, o'rin) — o'sha binoda haqiqatan ishlaydigan fuqarolar bo'yicha
            var employed = new Dictionary<(int, CitizenProfession), List<Citizen>>();
            foreach (Citizen c in state.Pop.Citizens)
            {
                if (!c.IsEmployed || !c.WorkplaceBuildingId.HasValue) continue;
                var key = (c.WorkplaceBuildingId.Value, c.Profession);
                if (!employed.TryGetValue(key, out var list)) employed[key] = list = new List<Citizen>();
                list.Add(c);
            }
            var want = new List<(string Key, Building B, CitizenProfession P, Citizen C, int Slot)>();
            foreach (Building b in buildings.Buildings.Where(x => x.BuildProgress >= 1f)
                         .OrderBy(x => Vector2.DistanceSquared(new Vector2(x.Position.X, x.Position.Y), center)))
            {
                foreach (var job in PopulationManager.JobsOf(b.Type))
                {
                    // Dehqonlar — FarmWorkController; soqchilar — qo'shinxona mashg'uloti
                    if (job.P == CitizenProfession.Dehqon || job.P == CitizenProfession.Soqchi) continue;
                    if (!employed.TryGetValue((b.Id, job.P), out var people)) continue;
                    for (int s = 0; s < Math.Min(PerBuilding, people.Count); s++)
                        want.Add((b.Id + ":" + (int)job.P + ":" + s, b, job.P, people[s], s));
                }
            }
            // Turli kasblar ko'rinsin: avval har kasbdan bittadan, keyin qolganlari (MaxWorkers gacha)
            var chosen = want.Where(w => w.Slot == 0).Concat(want.Where(w => w.Slot > 0)).Take(MaxWorkers).ToList();
            var keep = new HashSet<string>(chosen.Select(w => w.Key));

            foreach (string k in _jobs.Keys.ToList())
            {
                Job j = _jobs[k];
                if (keep.Contains(k) && buildings.Buildings.Contains(j.Work) && units.Units.Contains(j.W)) continue;
                units.Remove(j.W);
                _jobs.Remove(k);
            }
            foreach (var w in chosen)
            {
                if (_jobs.ContainsKey(w.Key)) continue;
                Vector2 home = HomeOf(w.B, houses, w.Slot);
                var unit = units.Add(new CitizenUnit(new[] { home, home + new Vector2(0.2f, 0f) }, ProfessionLooks.KindOf(w.P), 0.8f));
                unit.BindCitizen(w.C.Id, w.C.Name, NPCInteractionManager.DescribeShort(w.C));
                unit.PauseTime = 0f;
                unit.Presence = 0f;
                var job = new Job
                {
                    Key = w.Key, Work = w.B, Prof = w.P, W = unit, Home = home, Phase = Phase.Leaving,
                    TalkTimer = 4f + (float)_rng.NextDouble() * 8f, WorkFor = 18f + (float)_rng.NextDouble() * 20f
                };
                job.Spot = SpotOf(w.B, w.P, w.Slot, out job.Face);
                _jobs[w.Key] = job;
            }

            // 2) Qo'shinxona mashg'uloti: qo'shindagi eng kuchli turlar ko'rinishida
            var drillKeep = new HashSet<string>();
            var types = state.Military.Units.OrderByDescending(u => u.Strength).Select(u => u.UnitType).Distinct().ToList();
            if (types.Count == 0) types.Add(UnitType.Piyoda);
            foreach (Building b in buildings.Buildings.Where(x => x.Type == BuildingType.Barracks && x.BuildProgress >= 1f).Take(3))
                for (int s = 0; s < DrillPerBarracks; s++)
                {
                    string key = "drill:" + b.Id + ":" + s;
                    drillKeep.Add(key);
                    if (_drills.ContainsKey(key)) continue;
                    Vector2 yard = new Vector2(b.Position.X + 0.6f + s * 0.9f, b.Position.Y + b.Size.Y + 1.4f);
                    Unit u = UnitLooks.Create(types[s % types.Count], NearestWalkable(yard), Direction.North);
                    u.Team = UnitTeam.Player;
                    u.Hold = true;
                    units.Add(u);
                    u.SetFacing(new Vector2(0, -1));
                    _drills[key] = new Drill { Key = key, Barracks = b, U = u, Timer = (float)_rng.NextDouble() * 2f };
                }
            foreach (string k in _drills.Keys.ToList())
            {
                Drill d = _drills[k];
                if (drillKeep.Contains(k) && buildings.Buildings.Contains(d.Barracks) && units.Units.Contains(d.U)) continue;
                units.Remove(d.U);
                _drills.Remove(k);
            }
        }

        // ------------------------------------------------------------------ joylar
        private Vector2 HomeOf(Building work, List<Building> houses, int slot)
        {
            var near = houses.OrderBy(h => Vector2.DistanceSquared(new Vector2(h.Position.X, h.Position.Y), new Vector2(work.Position.X, work.Position.Y))).Take(6).ToList();
            if (near.Count == 0) return Front(work);
            Building h2 = near[(slot * 3 + work.Id) % near.Count];
            return Front(h2);
        }

        private static Vector2 Front(Building b) => new Vector2(b.FrontEntranceTile.X + 0.5f, b.FrontEntranceTile.Y + 0.5f);

        /// <summary>Ish nuqtasi: bino oldi (o'rin bo'yicha yonma-yon); baliqchi — eng yaqin suv bo'yi.</summary>
        private Vector2 SpotOf(Building b, CitizenProfession p, int slot, out Vector2 face)
        {
            Vector2 front = Front(b);
            face = new Vector2(0, -1);   // binoga qarab
            if (p == CitizenProfession.Baliqchi && _map != null && Shore(b, out Vector2 shore, out Vector2 water))
            {
                face = water - shore;
                return shore + new Vector2(slot * 0.5f, 0);
            }
            return NearestWalkable(front + new Vector2(-0.45f + slot * 0.9f, 0.35f));
        }

        /// <summary>Binoga eng yaqin, suv bilan qo'shni yuriladigan katak (10 katak radiusda).</summary>
        private bool Shore(Building b, out Vector2 shore, out Vector2 water)
        {
            shore = water = Vector2.Zero;
            float best = float.MaxValue;
            int cx = b.Position.X + b.Size.X / 2, cy = b.Position.Y + b.Size.Y / 2;
            for (int dy = -10; dy <= 10; dy++)
                for (int dx = -10; dx <= 10; dx++)
                {
                    int x = cx + dx, y = cy + dy;
                    if (!_map.IsWalkable(x, y)) continue;
                    foreach (var (ox, oy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    {
                        if (!_map.IsWater(x + ox, y + oy)) continue;
                        float d = dx * dx + dy * dy;
                        if (d >= best) continue;
                        best = d;
                        shore = new Vector2(x + 0.5f, y + 0.5f);
                        water = new Vector2(x + ox + 0.5f, y + oy + 0.5f);
                    }
                }
            return best < float.MaxValue;
        }

        private Vector2 NearestWalkable(Vector2 p)
        {
            if (_map == null) return p;
            for (int r = 0; r <= 4; r++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        int x = (int)MathF.Floor(p.X) + dx, y = (int)MathF.Floor(p.Y) + dy;
                        if (_map.IsWalkable(x, y)) return new Vector2(x + (p.X - MathF.Floor(p.X)), y + (p.Y - MathF.Floor(p.Y)));
                    }
            return p;
        }

        private static bool Carries(CitizenProfession p) =>
            p == CitizenProfession.Tashuvchi || p == CitizenProfession.Omborchi;

        private static bool SellsGoods(CitizenProfession p) =>
            p == CitizenProfession.Konchi || p == CitizenProfession.Baliqchi || p == CitizenProfession.Hunarmand || p == CitizenProfession.Chorvador;

        private Building NearestMarket(Building from)
        {
            Building best = null; float bd = float.MaxValue;
            if (_marketCache == null) return null;
            foreach (Building m in _marketCache)
            {
                float d = Vector2.DistanceSquared(new Vector2(m.Position.X, m.Position.Y), new Vector2(from.Position.X, from.Position.Y));
                if (d < bd) { bd = d; best = m; }
            }
            return best;
        }
        private List<Building> _marketCache;

        // ------------------------------------------------------------------ bitta ishchining kuni
        private void GoTo(Job j, Phase phase, Vector2 target)
        {
            j.Phase = phase;
            j.Target = target;
            CitizenUnit w = j.W;
            w.Hold = false;
            w.WorkStyle = 0;
            w.PatrolPoints.Clear();
            w.PatrolPoints.Add(w.Position);
            w.PatrolPoints.Add(target);
            if (_map != null) PatrolController.RouteUnit(_map, w);
            w.PauseTime = 0f;
            w.ResetRoute();
        }

        private void Step(Job j, float dt, bool night)
        {
            CitizenUnit w = j.W;
            if (w.ExternallyControlled || w.IsDown) return;   // bosqin/qo'zg'olonda boshqa tizim boshqaradi
            bool arrived = Vector2.Distance(w.Position, j.Target) < 0.7f;
            if (night && j.Phase != Phase.ToHome && j.Phase != Phase.AtHome)
            {
                w.CarryKey = null;
                GoTo(j, Phase.ToHome, j.Home);
                return;
            }
            switch (j.Phase)
            {
                case Phase.Leaving:
                    w.Hidden = false;
                    w.Presence = MathF.Min(1f, w.Presence + dt * 2f);   // eshikdan asta chiqadi
                    if (w.Presence >= 1f) GoTo(j, Phase.ToWork, j.Spot);
                    break;

                case Phase.ToWork:
                case Phase.Returning:
                    if (!arrived) break;
                    j.Phase = Phase.Working;
                    j.Timer = 0f;
                    w.Hold = true;
                    w.SetFacing(j.Face);
                    w.WorkStyle = (int)ProfessionLooks.StyleOf(j.Prof);
                    if (w.WorkStyle == (int)AndijonSaltanatlar.Population.WorkStyle.Carry) w.WorkStyle = (int)AndijonSaltanatlar.Population.WorkStyle.Tend;   // yuk ortish — egilish
                    break;

                case Phase.Working:
                    j.Timer += dt;
                    j.TalkTimer -= dt;
                    if (j.TalkTimer <= 0f)
                    {
                        string[] lines = ProfessionLooks.LinesOf(j.Prof);
                        w.Say(lines[_rng.Next(lines.Length)], 3f);
                        j.TalkTimer = 12f + (float)_rng.NextDouble() * 14f;
                    }
                    if (j.Timer < j.WorkFor) break;
                    j.Timer = 0f;
                    bool carry = Carries(j.Prof) || (SellsGoods(j.Prof) && _rng.NextDouble() < 0.45);
                    j.Market = carry ? NearestMarket(j.Work) : null;
                    if (j.Market != null && j.Market != j.Work)
                    {
                        w.CarryKey = "carry_sack";   // mahsulot / karvon yuki yelkada
                        GoTo(j, Phase.Carrying, Front(j.Market));
                    }
                    break;

                case Phase.Carrying:
                    if (!arrived) break;
                    w.CarryKey = null;
                    w.Say(j.Prof == CitizenProfession.Tashuvchi || j.Prof == CitizenProfession.Omborchi ? "Yuk bozorga yetdi" : "Mahsulot bozorga topshirildi", 2.5f);
                    GoTo(j, Phase.Returning, j.Spot);
                    break;

                case Phase.ToHome:
                    if (!arrived) break;
                    j.Phase = Phase.AtHome;
                    w.Hold = true;
                    break;

                case Phase.AtHome:
                    // Uyga asta kiradi; tongda yana chiqadi
                    if (night)
                    {
                        w.Presence = MathF.Max(0f, w.Presence - dt * 2f);
                        if (w.Presence <= 0f) w.Hidden = true;
                    }
                    else
                    {
                        j.Phase = Phase.Leaving;
                        w.Hidden = false;
                    }
                    break;
            }
        }

        /// <summary>Qo'shinxona mashg'uloti: qo'g'irchoqqa qarab ketma-ket zarbalar, oraliqda buyruq olish (ta'zim).</summary>
        private void StepDrill(Drill d, float dt, bool night)
        {
            Unit u = d.U;
            if (u.ExternallyControlled || u.IsDown) return;   // bosqinda — jangga ketgan
            u.Hidden = night;                                   // kechasi qo'shinxonada dam oladi
            if (night) return;
            d.Timer -= dt;
            if (d.Timer > 0f) return;
            if (d.Swings < 4)
            {
                u.AttackKind = d.Swings % 3;
                u.AttackTimer = Unit.SwingTime;
                d.Swings++;
                d.Timer = 0.9f + (float)_rng.NextDouble() * 0.3f;
            }
            else
            {
                u.PlayAction(Unit.ActionSalute, 0.8f);   // sarkarda buyrug'ini oladi
                d.Swings = 0;
                d.Timer = 2.2f;
            }
        }

        /// <summary>Bozorlar ro'yxatini yangilaydi (Sync dan oldin chaqiriladi).</summary>
        public void RefreshMarkets(BuildingManager buildings) =>
            _marketCache = buildings.Buildings.Where(b => b.Type == BuildingType.Market && b.BuildProgress >= 1f).ToList();
    }
}
