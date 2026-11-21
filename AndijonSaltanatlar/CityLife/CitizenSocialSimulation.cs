using System;
using System.Collections.Generic;
using AndijonSaltanatlar.AdvancedEconomy;
using AndijonSaltanatlar.Buildings;
using AndijonSaltanatlar.Core;
using AndijonSaltanatlar.Population;
using AndijonSaltanatlar.Units;
using AndijonSaltanatlar.World;
using Microsoft.Xna.Framework;

namespace AndijonSaltanatlar.CityLife
{
    public enum HouseholdHardshipEvent { None, Complaint, RevoltStarted, Recovered }

    /// <summary>Ko'rinadigan fuqarolarning suhbat, savdo, bino ziyorati va xalq arzi harakati.</summary>
    public sealed class CitizenSocialSimulation
    {
        private enum BehaviorMode { GoingToBuilding, InsideBuilding, GoingToPalace, Gathering, Talking, Returning }

        private sealed class Behavior
        {
            public CitizenUnit Unit;
            public BehaviorMode Mode;
            public List<Point> Path;
            public int PathIndex;
            public Vector2 Destination;
            public float Timer;
            public CitizenUnit Partner;
        }

        private readonly Dictionary<int, Behavior> _behaviors = new Dictionary<int, Behavior>();
        private readonly Dictionary<int, float> _cooldowns = new Dictionary<int, float>();
        private readonly Random _random = new Random(GameConfig.WorldSeed + 103);
        private float _scanTimer;
        private int _lastProcessedDay = -1;
        private int _lastTradeNoticeDay = -1;

        public static HouseholdHardshipEvent AdvanceHardship(Household household)
        {
            if (household == null) return HouseholdHardshipEvent.None;
            if (household.SharedMoney > 0.01f)
            {
                bool recovered = household.IsRevolting;
                household.HardshipDays = 0;
                household.IsRevolting = false;
                return recovered ? HouseholdHardshipEvent.Recovered : HouseholdHardshipEvent.None;
            }

            household.HardshipDays = Math.Min(3, household.HardshipDays + 1);
            if (household.HardshipDays == 1) return HouseholdHardshipEvent.Complaint;
            if (household.HardshipDays >= 3 && !household.IsRevolting)
            {
                household.IsRevolting = true;
                return HouseholdHardshipEvent.RevoltStarted;
            }
            return HouseholdHardshipEvent.None;
        }

        public void DayTick(GameStateManager state)
        {
            if (state == null || state.Day == _lastProcessedDay) return;
            _lastProcessedDay = state.Day;

            int complaints = 0, revolts = 0, recoveries = 0;
            foreach (Household household in state.Pop.Households)
            {
                switch (AdvanceHardship(household))
                {
                    case HouseholdHardshipEvent.Complaint:
                        complaints++;
                        break;
                    case HouseholdHardshipEvent.RevoltStarted:
                        revolts++;
                        break;
                    case HouseholdHardshipEvent.Recovered:
                        recoveries++;
                        break;
                }
            }

            if (revolts > 0)
            {
                state.World.PlayerKingdom.Stability = Math.Max(0, state.World.PlayerKingdom.Stability - revolts * 8);
                string text = revolts + " oila saroyga yig'ildi — pul yo'qligi sabab qo'zg'olon boshlandi!";
                state.Life.RecordEvent("citizen_revolt", state.Day, text);
                state.ShowMessage(text, isError: true);
            }
            else if (complaints > 0)
            {
                string text = complaints + " oila puldan qiynalmoqda va shoh huzuriga shikoyatga yo'l oldi.";
                state.Life.RecordEvent("citizen_complaint", state.Day, text);
                state.ShowMessage(text, isError: true);
            }
            else if (recoveries > 0)
            {
                state.World.PlayerKingdom.Stability = Math.Min(100, state.World.PlayerKingdom.Stability + recoveries * 4);
                state.ShowMessage("Xalq arzi tinchidi — " + recoveries + " oila yana oyoqqa turdi.");
            }
        }

        public void Update(float dt, MapManager map, BuildingManager buildings, UnitManager units,
                           GameStateManager state, Unit possessed = null)
        {
            if (dt <= 0f || map == null || buildings == null || units == null || state == null) return;

            foreach (int id in new List<int>(_cooldowns.Keys))
            {
                float remaining = _cooldowns[id] - dt;
                if (remaining <= 0f) _cooldowns.Remove(id);
                else _cooldowns[id] = remaining;
            }

            foreach (Behavior behavior in new List<Behavior>(_behaviors.Values))
            {
                if (behavior.Unit == possessed)
                {
                    Cancel(behavior, preserveExternalControl: true);
                    continue;
                }
                if (!units.Units.Contains(behavior.Unit))
                {
                    _behaviors.Remove(behavior.Unit.Id);
                    continue;
                }
                UpdateBehavior(behavior, dt, map, state);
            }

            _scanTimer -= dt;
            if (_scanTimer > 0f) return;
            _scanTimer = 1.8f;

            StartGatherings(map, buildings, units, state, possessed);
            StartConversations(map, buildings, units, state, possessed);
            StartBuildingVisits(map, buildings, units, state, possessed);
        }

        private void UpdateBehavior(Behavior behavior, float dt, MapManager map, GameStateManager state)
        {
            CitizenUnit unit = behavior.Unit;
            switch (behavior.Mode)
            {
                case BehaviorMode.GoingToBuilding:
                case BehaviorMode.GoingToPalace:
                case BehaviorMode.Returning:
                    if (MoveAlongPath(behavior, dt))
                    {
                        if (behavior.Mode == BehaviorMode.GoingToBuilding)
                        {
                            unit.IsInsideBuilding = true;
                            Citizen citizen = state.Pop.GetById(unit.CitizenId);
                            int choice = citizen == null ? 0 : Math.Abs(citizen.Id + state.Day) % 3;
                            unit.SetActivity(choice switch
                            {
                                1 => CitizenActivityIcon.Sitting,
                                2 => CitizenActivityIcon.Eating,
                                _ => CitizenActivityIcon.Visiting
                            });
                            behavior.Mode = BehaviorMode.InsideBuilding;
                            behavior.Timer = 3.5f + (float)_random.NextDouble() * 3f;
                        }
                        else if (behavior.Mode == BehaviorMode.GoingToPalace)
                        {
                            behavior.Mode = BehaviorMode.Gathering;
                            unit.DriveIdle();
                        }
                        else
                        {
                            Release(behavior);
                        }
                    }
                    break;
                case BehaviorMode.InsideBuilding:
                    behavior.Timer -= dt;
                    unit.DriveIdle();
                    if (behavior.Timer <= 0f)
                    {
                        unit.IsInsideBuilding = false;
                        BeginReturn(behavior, map);
                    }
                    break;
                case BehaviorMode.Gathering:
                    Household household = HouseholdOf(unit, state);
                    if (household == null || household.HardshipDays == 0)
                    {
                        BeginReturn(behavior, map);
                        break;
                    }
                    unit.SetActivity(household.IsRevolting ? CitizenActivityIcon.Revolting : CitizenActivityIcon.Complaining);
                    unit.DriveIdle();
                    // Arz-shikoyat: qo'l silkitib gapiradi (suhbat jestining o'zi, davomli)
                    if (unit.LifeActionTime <= 0.12f)
                        unit.PlayAction(Unit.ActionTalk, 0.9f + (float)_random.NextDouble() * 0.5f);
                    if (behavior.Destination != Vector2.Zero)
                        unit.SetFacing(behavior.Destination - unit.Position);
                    break;
                case BehaviorMode.Talking:
                    behavior.Timer -= dt;
                    unit.DriveIdle();
                    // Suhbat/savdo jesti: qo'l silkitib, yengil chayqalish — tugaganda o'zini yangilab, suhbat davomida takrorlanadi
                    if (unit.LifeActionTime <= 0.12f)
                        unit.PlayAction(Unit.ActionTalk, 1.0f + (float)_random.NextDouble() * 0.6f);
                    if (behavior.Partner != null)
                    {
                        unit.SetFacing(behavior.Partner.Position - unit.Position);
                    }
                    if (behavior.Timer <= 0f)
                    {
                        Citizen citizen = state.Pop.GetById(unit.CitizenId);
                        if (citizen != null) citizen.Happiness = Math.Min(100f, citizen.Happiness + 1f);
                        Release(behavior);
                    }
                    break;
            }
        }

        private bool MoveAlongPath(Behavior behavior, float dt)
        {
            CitizenUnit unit = behavior.Unit;
            float step = unit.Speed * dt;
            while (behavior.PathIndex < behavior.Path.Count)
            {
                Point tile = behavior.Path[behavior.PathIndex];
                Vector2 target = new Vector2(tile.X + 0.5f, tile.Y + 0.5f);
                Vector2 delta = target - unit.Position;
                float distance = delta.Length();
                if (distance <= Math.Max(0.03f, step))
                {
                    unit.Position = target;
                    behavior.PathIndex++;
                    continue;
                }

                unit.SetFacing(delta);
                unit.DriveMove(delta, unit.Speed);
                unit.Position += delta / distance * step;
                return false;
            }
            unit.DriveIdle();
            return true;
        }

        private void StartGatherings(MapManager map, BuildingManager buildings, UnitManager units,
                                     GameStateManager state, Unit possessed)
        {
            Building palace = null;
            foreach (Building building in buildings.Buildings)
                if (building.Type == BuildingType.MainHall && building.BuildProgress >= 1f) { palace = building; break; }
            if (palace == null) return;

            var citizens = CitizenUnits(units);
            foreach (CitizenUnit unit in citizens)
            {
                if (unit == possessed || IsBusy(unit) || unit.IsInsideBuilding || unit.ExternallyControlled) continue;
                Household household = HouseholdOf(unit, state);
                if (household == null || household.HardshipDays <= 0) continue;
                if (!TryRouteToBuilding(unit, palace, map, BehaviorMode.GoingToPalace, CitizenActivityIcon.Complaining,
                        household.Id, out Behavior behavior)) continue;
                _behaviors[unit.Id] = behavior;
                _cooldowns[unit.Id] = 4f;
            }
        }

        private void StartConversations(MapManager map, BuildingManager buildings, UnitManager units,
                                        GameStateManager state, Unit possessed)
        {
            var citizens = CitizenUnits(units);
            int started = 0;
            for (int i = 0; i < citizens.Count && started < 2; i++)
            {
                CitizenUnit first = citizens[i];
                if (!CanInteract(first, state, possessed)) continue;

                for (int j = i + 1; j < citizens.Count; j++)
                {
                    CitizenUnit second = citizens[j];
                    if (!CanInteract(second, state, possessed) || Vector2.Distance(first.Position, second.Position) > 1.15f) continue;

                    bool traded = TryTrade(first, second, buildings, state);
                    StartConversation(first, second, traded ? CitizenActivityIcon.Trading : CitizenActivityIcon.Talking);
                    started++;
                    break;
                }
            }
        }

        private void StartBuildingVisits(MapManager map, BuildingManager buildings, UnitManager units,
                                         GameStateManager state, Unit possessed)
        {
            foreach (CitizenUnit unit in CitizenUnits(units))
            {
                if (unit == possessed || IsBusy(unit) || unit.IsInsideBuilding || unit.ExternallyControlled
                    || IsDistressed(unit, state) || _cooldowns.ContainsKey(unit.Id)) continue;
                if (_random.NextDouble() > 0.42) continue;
                Building target = ChooseBuilding(unit, buildings, state);
                if (target == null || !TryRouteToBuilding(unit, target, map, BehaviorMode.GoingToBuilding,
                        CitizenActivityIcon.Visiting, 0, out Behavior behavior)) continue;
                _behaviors[unit.Id] = behavior;
                _cooldowns[unit.Id] = 14f + (float)_random.NextDouble() * 12f;
            }
        }

        private Building ChooseBuilding(CitizenUnit unit, BuildingManager buildings, GameStateManager state)
        {
            Citizen citizen = state.Pop.GetById(unit.CitizenId);
            if (citizen == null) return null;

            BuildingType[] preferred = citizen.Profession switch
            {
                CitizenProfession.Savdogar => new[] { BuildingType.Market, BuildingType.Caravanserai },
                CitizenProfession.Soqchi => new[] { BuildingType.Barracks, BuildingType.Mosque },
                CitizenProfession.Muallim => new[] { BuildingType.Madrasa, BuildingType.Mosque },
                _ => new[] { BuildingType.House, BuildingType.Market, BuildingType.Mosque, BuildingType.Madrasa, BuildingType.Caravanserai }
            };

            var choices = new List<Building>();
            foreach (Building building in buildings.Buildings)
            {
                if (building.BuildProgress < 1f || building.Type == BuildingType.MainHall) continue;
                foreach (BuildingType type in preferred)
                    if (building.Type == type) { choices.Add(building); break; }
            }
            if (choices.Count == 0) return null;
            int index = Math.Abs(citizen.Id + state.Day) % choices.Count;
            return choices[index];
        }

        private bool TryTrade(CitizenUnit first, CitizenUnit second, BuildingManager buildings, GameStateManager state)
        {
            Citizen firstCitizen = state.Pop.GetById(first.CitizenId);
            Citizen secondCitizen = state.Pop.GetById(second.CitizenId);
            if (firstCitizen == null || secondCitizen == null) return false;

            Citizen seller = firstCitizen.Profession == CitizenProfession.Savdogar ? firstCitizen
                : secondCitizen.Profession == CitizenProfession.Savdogar ? secondCitizen : null;
            Citizen buyer = seller == firstCitizen ? secondCitizen : seller == secondCitizen ? firstCitizen : null;
            if (seller == null || buyer == null || seller.HouseholdId == buyer.HouseholdId || !seller.WorkplaceBuildingId.HasValue)
                return false;

            Household sellerHousehold = state.Pop.GetHousehold(seller.HouseholdId);
            Household buyerHousehold = state.Pop.GetHousehold(buyer.HouseholdId);
            if (sellerHousehold == null || buyerHousehold == null) return false;
            MarketState market = state.Life.Economy.Merchants.GetByBuilding(seller.WorkplaceBuildingId.Value);
            if (market == null) return false;
            Vector2 marketPosition = new Vector2(market.X + 0.5f, market.Y + 0.5f);
            Vector2 pairPosition = (first.Position + second.Position) * 0.5f;
            if (Vector2.Distance(pairPosition, marketPosition) > 4f) return false;

            MerchantInfo offer = state.Life.Economy.Merchants.GetMerchant(seller, state.Life.Economy.Prices);
            if (offer == null || offer.Available < 1f || market.ThroughputLeft < 1f
                || offer.Price <= 0f || buyerHousehold.SharedMoney < offer.Price) return false;

            string transactionId = "citizen-trade:" + state.Day + ":" + buyer.Id + ":" + seller.Id;
            EconomyTransactionService transactions = state.Life.Economy.Transactions;
            if (!transactions.TryBegin(transactionId)) return false;

            float price = offer.Price;
            market.Stock[(int)offer.Product] -= 1f;
            market.ThroughputLeft -= 1f;
            buyerHousehold.SharedMoney -= price;
            buyerHousehold.LastExpense += price;
            sellerHousehold.SharedMoney += price;
            sellerHousehold.LastIncome += price;
            if (offer.Product == ProductType.Food) buyerHousehold.FoodReserve += 1f;
            transactions.Log(transactionId, state.Day, TransactionType.CitizenTrade, offer.Product, 1f, price,
                buyer.Name + " " + seller.Name + "dan mahsulot xarid qildi.");

            if (_lastTradeNoticeDay != state.Day)
            {
                _lastTradeNoticeDay = state.Day;
                state.ShowMessage("Bozorda " + buyer.Name + " va " + seller.Name + " savdo qildi.");
            }
            return true;
        }

        private void StartConversation(CitizenUnit first, CitizenUnit second, CitizenActivityIcon icon)
        {
            first.ExternallyControlled = true;
            second.ExternallyControlled = true;
            first.SetActivity(icon);
            second.SetActivity(icon);
            first.SetFacing(second.Position - first.Position);
            second.SetFacing(first.Position - second.Position);
            first.DriveIdle();
            second.DriveIdle();
            _behaviors[first.Id] = new Behavior { Unit = first, Mode = BehaviorMode.Talking, Timer = 4f, Partner = second };
            _behaviors[second.Id] = new Behavior { Unit = second, Mode = BehaviorMode.Talking, Timer = 4f, Partner = first };
            _cooldowns[first.Id] = 12f;
            _cooldowns[second.Id] = 12f;
        }

        private bool TryRouteToBuilding(CitizenUnit unit, Building building, MapManager map, BehaviorMode mode,
                                       CitizenActivityIcon icon, int householdId, out Behavior behavior)
        {
            behavior = null;
            List<Point> path = FindPathToBuilding(unit, building, map, householdId);
            if (path == null) return false;
            behavior = new Behavior
            {
                Unit = unit,
                Mode = mode,
                Path = path,
                PathIndex = path.Count > 1 ? 1 : path.Count,
                Destination = path.Count > 0 ? new Vector2(path[path.Count - 1].X + 0.5f, path[path.Count - 1].Y + 0.5f) : unit.Position
            };
            unit.ExternallyControlled = true;
            unit.IsInsideBuilding = false;
            unit.SetActivity(icon);
            unit.DriveIdle();
            return true;
        }

        private List<Point> FindPathToBuilding(CitizenUnit unit, Building building, MapManager map, int householdId)
        {
            var goals = new List<Point>();
            for (int y = building.Position.Y - 1; y <= building.Position.Y + building.Size.Y; y++)
                for (int x = building.Position.X - 1; x <= building.Position.X + building.Size.X; x++)
                {
                    bool edge = x == building.Position.X - 1 || x == building.Position.X + building.Size.X
                        || y == building.Position.Y - 1 || y == building.Position.Y + building.Size.Y;
                    if (edge && map.IsWalkable(x, y)) goals.Add(new Point(x, y));
                }
            if (goals.Count == 0) return null;

            Point start = new Point((int)MathF.Floor(unit.Position.X), (int)MathF.Floor(unit.Position.Y));
            OrderEntranceGoals(building, start, goals);
            int offset = building.Type == BuildingType.MainHall
                ? Math.Abs(unit.CitizenId + householdId) % goals.Count
                : 0;
            for (int i = 0; i < goals.Count; i++)
            {
                Point goal = goals[(i + offset) % goals.Count];
                if (building.Type == BuildingType.MainHall && IsRallySpotTaken(goal)) continue;
                List<Point> path = Pathfinder.FindPath(map, start, goal, 6000);
                if (path != null) return path;
            }
            return null;
        }

        internal static void OrderEntranceGoals(Building building, Point start, List<Point> goals)
        {
            goals.Sort((a, b) =>
            {
                if (building.Type != BuildingType.MainHall)
                {
                    bool aIsEntrance = a == building.FrontEntranceTile;
                    bool bIsEntrance = b == building.FrontEntranceTile;
                    if (aIsEntrance != bIsEntrance) return aIsEntrance ? -1 : 1;
                }
                return Manhattan(start, a).CompareTo(Manhattan(start, b));
            });
        }

        private bool IsRallySpotTaken(Point point)
        {
            foreach (Behavior behavior in _behaviors.Values)
                if ((behavior.Mode == BehaviorMode.GoingToPalace || behavior.Mode == BehaviorMode.Gathering)
                    && behavior.Destination == new Vector2(point.X + 0.5f, point.Y + 0.5f)) return true;
            return false;
        }

        private void BeginReturn(Behavior behavior, MapManager map)
        {
            CitizenUnit unit = behavior.Unit;
            unit.IsInsideBuilding = false;
            Vector2 resume = unit is PatrolUnit patrol ? patrol.NextPatrolPoint : unit.Position;
            Point start = new Point((int)MathF.Floor(unit.Position.X), (int)MathF.Floor(unit.Position.Y));
            Point goal = new Point((int)MathF.Floor(resume.X), (int)MathF.Floor(resume.Y));
            List<Point> path = Pathfinder.FindPath(map, start, goal, 6000);
            if (path == null || path.Count <= 1)
            {
                Release(behavior);
                return;
            }

            behavior.Mode = BehaviorMode.Returning;
            behavior.Path = path;
            behavior.PathIndex = 1;
            unit.SetActivity(CitizenActivityIcon.None);
            unit.DriveIdle();
        }

        private static bool CanInteract(CitizenUnit unit, GameStateManager state, Unit possessed)
        {
            return unit != possessed && unit.IsBound && !unit.IsDown && !unit.IsInsideBuilding
                && !unit.ExternallyControlled && unit.ActivityIcon == CitizenActivityIcon.None
                && HouseholdOf(unit, state)?.HardshipDays == 0;
        }

        private static bool IsDistressed(CitizenUnit unit, GameStateManager state)
            => HouseholdOf(unit, state)?.HardshipDays > 0;

        private static Household HouseholdOf(CitizenUnit unit, GameStateManager state)
        {
            Citizen citizen = state.Pop.GetById(unit.CitizenId);
            return citizen == null ? null : state.Pop.GetHousehold(citizen.HouseholdId);
        }

        private static List<CitizenUnit> CitizenUnits(UnitManager units)
        {
            var citizens = new List<CitizenUnit>();
            foreach (Unit unit in units.Units)
                if (unit is CitizenUnit citizen && citizen.IsBound && !citizen.IsInsideBuilding) citizens.Add(citizen);
            return citizens;
        }

        private bool IsBusy(CitizenUnit unit) => _behaviors.ContainsKey(unit.Id);

        private void Release(Behavior behavior)
        {
            CitizenUnit unit = behavior.Unit;
            _behaviors.Remove(unit.Id);
            unit.IsInsideBuilding = false;
            unit.SetActivity(CitizenActivityIcon.None);
            unit.DriveIdle();
            unit.ExternallyControlled = false;
        }

        private void Cancel(Behavior behavior, bool preserveExternalControl)
        {
            _behaviors.Remove(behavior.Unit.Id);
            behavior.Unit.IsInsideBuilding = false;
            behavior.Unit.SetActivity(CitizenActivityIcon.None);
            behavior.Unit.DriveIdle();
            if (!preserveExternalControl) behavior.Unit.ExternallyControlled = false;
        }

        private static int Manhattan(Point a, Point b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
    }
}
