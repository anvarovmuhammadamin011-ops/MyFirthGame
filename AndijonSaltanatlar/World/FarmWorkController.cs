using System;
using System.Collections.Generic;
using System.Linq;
using AndijonSaltanatlar.Buildings;
using AndijonSaltanatlar.Core;
using AndijonSaltanatlar.Core.Graphics;
using AndijonSaltanatlar.Units;
using Microsoft.Xna.Framework;

namespace AndijonSaltanatlar.World
{
    /// <summary>
    /// Dehqonlar o'z fermasida ishlaydi (eng ko'pi MaxWorkers ta dala): qo'lida ketmon bilan
    ///   1) dalaga boradi, 2) yerni chopadi va urug' ekadi, 3) ekin o'sguncha chopiq/sug'orish qiladi (ekin ko'z oldida
    ///   kattalashadi), 4) pishgach o'radi, 5) hosilni bozorga olib borib sotadi (tanga xazinaga), 6) dalasiga qaytadi.
    /// Bozor bo'lmasa hosil saroy omboriga (oziq-ovqat) topshiriladi. Dehqoni bor dalani kunlik avtomatik hisob boshqarmaydi.
    /// </summary>
    public sealed class FarmWorkController
    {
        public const int MaxWorkers = 6;
        private enum Phase { ToField, Work, ToMarket, Sell }

        private sealed class Job
        {
            public Farm Farm;
            public CitizenUnit Worker;
            public Phase Phase;
            public Vector2 Target;
            public float Timer, TalkTimer;
            public int Carry;
            public Building Market;
            public bool WaterMode;      // hozir sug'oryapti (aks holda ketmon bilan chopyapti)
            public float ModeTimer;
            public float Wet;           // sug'orilganlik (0..1) — ekin tezroq o'sadi
            public int Tier;            // jihoz darajasi (0 oddiy ketmon → 2 yaxshi asbob): tezroq ishlaydi
            public int Earned;          // jami topilgan tanga (jihoz yangilash uchun)
        }

        private readonly Dictionary<int, Job> _jobs = new Dictionary<int, Job>();
        private UnitManager _owner;
        private MapManager _map;
        private float _syncTimer;
        private readonly Random _rng = new Random();

        public int WorkerCount => _jobs.Count;
        /// <summary>Qop ko'tarib ketayotgan birinchi dehqon (sinov kamerasi uchun).</summary>
        public Unit FirstCarrier { get { foreach (Job j in _jobs.Values) if (j.Worker.CarryKey != null) return j.Worker; return null; } }

        /// <summary>Bu odam dala dehqonimi (boshqa tizimlar — cho'milish va h.k. — uni olib ketmasin).</summary>
        public bool IsWorker(Unit u)
        {
            foreach (Job j in _jobs.Values) if (j.Worker == u) return true;
            return false;
        }

        public void Update(float dt, MapManager map, BuildingManager buildings, UnitManager units, GameStateManager state)
        {
            if (!ReferenceEquals(units, _owner)) { _jobs.Clear(); _owner = units; _syncTimer = 0f; }
            _map = map;
            _syncTimer -= dt;
            if (_syncTimer <= 0f) { _syncTimer = 2f; Sync(buildings, units); }

            // O'yin soati: ekin o'sishi va ish tezligi o'yin tezligiga bog'liq
            float gameDt = state.Paused ? 0f : dt * state.Speed;
            float hours = gameDt / Math.Max(1f, state.Clock.SecondsPerDay) * 24f;
            foreach (Job j in _jobs.Values) Step(j, dt, gameDt, hours, buildings, state);
        }

        // ------------------------------------------------------------------ dehqonlarni dalalarga biriktirish
        private void Sync(BuildingManager buildings, UnitManager units)
        {
            Building hall = buildings.Buildings.FirstOrDefault(b => b.Type == BuildingType.MainHall);
            Vector2 center = hall != null ? new Vector2(hall.Position.X, hall.Position.Y) : Vector2.Zero;
            var farms = buildings.Buildings.OfType<Farm>().Where(f => f.BuildProgress >= 1f)
                .OrderBy(f => Vector2.DistanceSquared(new Vector2(f.Position.X, f.Position.Y), center))
                .Take(MaxWorkers).ToList();
            var keep = new HashSet<int>(farms.Select(f => f.Id));
            foreach (Farm f in buildings.Buildings.OfType<Farm>()) f.Tended = keep.Contains(f.Id) && _jobs.ContainsKey(f.Id);

            foreach (int id in _jobs.Keys.ToList())
            {
                Job j = _jobs[id];
                bool gone = !keep.Contains(id) || !buildings.Buildings.Contains(j.Farm) || !units.Units.Contains(j.Worker);
                if (!gone) continue;
                units.Remove(j.Worker);
                j.Farm.Tended = false;
                _jobs.Remove(id);
            }

            foreach (Farm f in farms)
            {
                if (_jobs.ContainsKey(f.Id)) continue;
                Vector2 start = FieldFront(f);
                var worker = units.Add(new CitizenUnit(new[] { start, start + new Vector2(0.3f, 0f) }, CitizenArt.Farmer, 0.8f));
                worker.PauseTime = 0f;
                var job = new Job { Farm = f, Worker = worker, Phase = Phase.ToField, TalkTimer = 2f + (float)_rng.NextDouble() * 4f };
                _jobs[f.Id] = job;
                f.Tended = true;
                GoTo(job, Phase.ToField, start);
            }
        }

        private static Vector2 FieldFront(Farm f) => new Vector2(f.FrontEntranceTile.X + 0.5f, f.FrontEntranceTile.Y + 0.5f);

        /// <summary>Dala jo'yaklari bo'ylab ish nuqtalari (dehqon qator bo'ylab yurib chopadi).</summary>
        private static List<Vector2> FieldRows(Farm f)
        {
            var pts = new List<Vector2>();
            for (int row = 0; row < 5; row++)
            {
                float y = f.Position.Y + 0.75f + row * 0.62f;
                bool rev = row % 2 == 1;
                for (int c = 0; c < 4; c++)
                {
                    int cc = rev ? 3 - c : c;
                    pts.Add(new Vector2(f.Position.X + 0.7f + cc * 0.85f, y));
                }
            }
            return pts;
        }

        /// <summary>Yo'lga chiqaradi: ko'cha bo'ylab (A*) borish manziliga.</summary>
        private void GoTo(Job j, Phase phase, Vector2 target)
        {
            j.Phase = phase;
            j.Target = target;
            j.Worker.Working = false;
            j.Worker.PatrolPoints.Clear();
            j.Worker.PatrolPoints.Add(j.Worker.Position);
            j.Worker.PatrolPoints.Add(target);
            if (_map != null) PatrolController.RouteUnit(_map, j.Worker);
            j.Worker.PauseTime = 0f;
            j.Worker.ResetRoute();
        }

        // ------------------------------------------------------------------ bitta dehqonning ishi
        private void Step(Job j, float dt, float gameDt, float hours, BuildingManager buildings, GameStateManager state)
        {
            Farm f = j.Farm;
            CitizenUnit w = j.Worker;
            // Ekin dehqon parvarishida o'sadi (dehqon dalada bo'lsa tezroq — chopiq va sug'orish)
            // Sug'orilgan dala 1.5 barobar tez o'sadi; namlik asta quriydi
            j.Wet = Math.Max(0f, j.Wet - hours / 3f);
            if (f.CropStage == FarmCropStage.Growing)
                f.AdvanceGrowth(hours / Farm.TendedGrowHours * (j.Phase == Phase.Work ? 1.25f : 0.8f) * (j.Wet > 0f ? 1.5f : 1f) * (1f + j.Tier * 0.2f));

            switch (j.Phase)
            {
                case Phase.ToField:
                    if (Vector2.Distance(w.Position, j.Target) < 0.7f)
                    {
                        // Dalaga yetdi: jo'yaklar bo'ylab ishlash (to'g'ridan-to'g'ri yuradi, har nuqtada to'xtab chopadi)
                        j.Phase = Phase.Work;
                        w.PatrolPoints.Clear();
                        w.PatrolPoints.AddRange(FieldRows(f));
                        w.PauseTime = 2.6f;
                        w.ResetRoute();
                        j.Timer = 0f;
                    }
                    break;

                case Phase.Work:
                    // Chopiq va sug'orish navbatma-navbat (ekin o'sayotganda)
                    if (f.CropStage == FarmCropStage.Growing)
                    {
                        j.ModeTimer += gameDt;
                        if (j.ModeTimer > 10f)
                        {
                            j.ModeTimer = 0f;
                            j.WaterMode = !j.WaterMode;
                            if (j.WaterMode) { j.Wet = 1f; w.Say("Ekinni sug'oryapman", 2.5f); }
                            else w.Say("Chopiq qilyapman", 2.2f);
                        }
                    }
                    else j.WaterMode = false;
                    bool still = w.State != UnitState.Walking;
                    w.Working = !j.WaterMode;
                    w.Watering = j.WaterMode && still;
                    j.Timer += gameDt;
                    if (f.CropStage == FarmCropStage.Empty)
                    {
                        if (f.PlantProgress <= 0f)
                        {
                            // Ekishni boshlash — urug' kerak (yerni chopib bo'lgach)
                            if (j.Timer > 4f)
                            {
                                if (state.Farming.TryConsumeSeed()) { f.AdvancePlant(1f / 45f); w.Say("Urug' ekyapman...", 2.5f); }
                                else if (j.Timer > 12f) { w.Say("Urug' tugadi...", 3f); j.Timer = 4f; }
                            }
                        }
                        else
                        {
                            // Jo'yaklar bo'ylab qator-qator ekish davom etadi (yaxshi jihoz — tezroq)
                            f.AdvancePlant(gameDt / 8f * (1f + j.Tier * 0.5f));
                            w.Working = true;
                        }
                    }
                    else if (f.CropStage == FarmCropStage.Mature)
                    {
                        // O'rim: biroz ishlaydi, keyin hosilni yig'ib bozorga
                        if (j.Timer > 6f)
                        {
                            int harvest = f.HarvestCrop();
                            int total = state.Farming.ApplyYieldBonus(harvest);
                            f.SellStoredProduce();   // dalada saqlanmaydi — dehqon o'zi olib ketadi
                            j.Carry = total;
                            j.Market = NearestMarket(f, buildings);
                            w.CarryKey = "carry_sack";   // hosil qopga solindi — yelkada
                            w.Say("Hosilni qopga soldim (" + total + ") — bozorga!", 3f);
                            Vector2 dest = j.Market != null ? EntranceOf(j.Market) : FieldFront(f);
                            GoTo(j, Phase.ToMarket, dest);
                        }
                    }
                    else j.Timer = Math.Min(j.Timer, 5f);
                    break;

                case Phase.ToMarket:
                    if (j.Market == null || Vector2.Distance(w.Position, j.Target) < 0.8f)
                    {
                        j.Phase = Phase.Sell;
                        j.Timer = 0f;
                        w.PatrolPoints.Clear();
                        w.PatrolPoints.Add(w.Position);
                        w.ResetRoute();
                    }
                    break;

                case Phase.Sell:
                    j.Timer += dt;
                    if (j.Timer > 2f)
                    {
                        if (j.Carry > 0)
                        {
                            bool market = j.Market != null && j.Market.Type == BuildingType.Market;
                            if (market)
                            {
                                int coins = j.Carry * 2;
                                state.Coins += coins;
                                j.Earned += coins;
                                // Jihoz yangilash: pul yiqqach dehqon ketmonini yaxshiroq asbobga almashtiradi (tezroq ishlaydi)
                                if (j.Tier < 2 && j.Earned >= (j.Tier + 1) * 140)
                                {
                                    j.Tier++;
                                    w.Say("Yangi, yaxshiroq ketmon oldim! Ish tezlashdi.", 3.5f);
                                }
                                // Pul ko'rsatkichi faqat dehqon ustida kichik (bilinar-bilinmas) — katta toast emas
                                else w.Say("+" + coins + " tanga", 3f);
                            }
                            else
                            {
                                state.Food += j.Carry;
                                w.Say("Omborga topshirdim", 3f);
                            }
                            j.Carry = 0;
                        }
                        w.CarryKey = null;
                        GoTo(j, Phase.ToField, FieldFront(f));
                    }
                    break;
            }
            if (j.Phase != Phase.Work) { w.Working = false; w.Watering = false; }
            else if (w.State == UnitState.Walking) w.Working = false;

            // Ishlayotganda vaqti-vaqti bilan gapiradi
            j.TalkTimer -= dt;
            if (j.TalkTimer <= 0f && string.IsNullOrEmpty(w.SpeechText))
            {
                j.TalkTimer = 9f + (float)_rng.NextDouble() * 8f;
                string line = j.Phase == Phase.Work
                    ? (f.CropStage == FarmCropStage.Growing ? (f.Growth < 0.5f ? "Yerni chopyapman" : "Ekin yaxshi o'syapti") : f.CropStage == FarmCropStage.Mature ? "O'rim vaqti!" : "Yer haydayapman")
                    : j.Phase == Phase.ToMarket ? "Hosilni bozorga olib ketyapman" : j.Phase == Phase.ToField ? "Dalamga ketyapman" : null;
                if (line != null) w.Say(line, 3f);
            }
        }

        private static Vector2 EntranceOf(Building b) => new Vector2(b.FrontEntranceTile.X + 0.5f, b.FrontEntranceTile.Y + 0.5f);

        private static Building NearestMarket(Farm f, BuildingManager buildings)
        {
            var from = new Vector2(f.Position.X, f.Position.Y);
            return buildings.Buildings.Where(b => b.Type == BuildingType.Market && b.BuildProgress >= 1f)
                       .OrderBy(b => Vector2.DistanceSquared(new Vector2(b.Position.X, b.Position.Y), from)).FirstOrDefault()
                   ?? buildings.Buildings.FirstOrDefault(b => b.Type == BuildingType.MainHall);
        }
    }
}
