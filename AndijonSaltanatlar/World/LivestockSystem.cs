using System;
using System.Collections.Generic;
using AndijonSaltanatlar.Buildings;
using AndijonSaltanatlar.Core;
using AndijonSaltanatlar.Core.Graphics;
using AndijonSaltanatlar.Environment;
using AndijonSaltanatlar.Units;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace AndijonSaltanatlar.World
{
    /// <summary>
    /// CHORVA VA PARRANDA: cho'ponlarning qo'y, echki va mollari, cho'pon iti hamda hovlilardagi tovuqlar.
    ///  - Har bir cho'ponning o'z podasi va ITI bor; poda cho'pon ortidan yuradi, o'tlaydi, atrofida sayr qiladi.
    ///    It cho'pon yonida yuradi, podadan uzoqlashgan jonivorni qaytarib haydaydi, dushmanga qarab yuguradi.
    ///    O'yinchi cho'pon ichiga kirsa, poda va it uning ortidan ergashadi.
    ///  - TOVUQLAR uylar yonidagi hovlida don cho'qiydi; odam yaqinlashsa pirillab qochadi.
    ///  - Har o'yin kuni poda KO'PAYADI: kamida ikki katta jonivor bo'lsa, qo'zichoq / uloq / buzoq / jo'ja
    ///    tug'iladi; bolalar 3 kunda katta bo'ladi. Bahorda tez, qishda sekin ko'payadi.
    ///  - Shahar podadan sut-go'sht, tovuqlardan tuxum oladi (oziq-ovqat).
    ///  - Dushman yaqinlashsa jonivorlar qochadi.
    /// </summary>
    public sealed class LivestockSystem
    {
        public enum Kind { Sheep, Goat, Cow, Chicken, Dog, Duck }
        private const int KindCount = 6;

        private const int HerdCapacity = 24;
        private const int CoopCapacity = 8;
        private const int GrowUpDays = 3;
        private const int MaxCoops = 7;

        private sealed class Herd
        {
            public CitizenUnit Shepherd;   // tovuqxonada null
            public Vector2 Center;
            public bool Coop;              // hovlidagi tovuqlar
            public bool Pond;              // suvdagi o'rdaklar
            public readonly List<Animal> Animals = new List<Animal>();
            public int Capacity => Coop ? CoopCapacity : HerdCapacity;
        }

        private sealed class Animal : IWorldObject
        {
            public Kind Kind;
            public Herd Herd;
            public bool Baby;
            public int AgeDays;
            public Vector2 Pos;
            public Vector2 Target;
            public bool Moving;
            public bool Grazing;
            public bool FacingLeft;
            public float Timer;
            public float Anim;
            public float Speed;
            public float Rush = 1f;        // vaqtinchalik tezlik koeffitsienti (it haydaganda, qochganda)
            public Texture2D Tex;

            public float Depth => Pos.X + Pos.Y;
            public int SortLayer => 1;
            public Texture2D Texture => Tex;
            public bool Flipped => FacingLeft;
            public Vector2 DrawPosition
            {
                get
                {
                    Vector2 w = IsoMath.TileToWorld(Pos);
                    return new Vector2(MathF.Round(w.X - Tex.Width / 2f), MathF.Round(w.Y - Tex.Height + 2));
                }
            }
        }

        private readonly MapManager _map;
        private readonly UnitManager _units;
        private readonly Random _rng = new Random(2024);
        private readonly List<Herd> _herds = new List<Herd>();
        private readonly List<IWorldObject> _objects = new List<IWorldObject>();
        // [tur][bola?][kadr: 0 turish, 1-2 yurish, 3 o'tlash / cho'qish / it o'tirishi]
        private readonly Texture2D[,,] _tex = new Texture2D[KindCount, 2, 4];

        public int Sheep { get; private set; }
        public int Goats { get; private set; }
        public int Cows { get; private set; }
        public int Chickens { get; private set; }
        public int Dogs { get; private set; }

        public LivestockSystem(AssetManager assets, MapManager map, UnitManager units, BuildingManager buildings = null)
        {
            _map = map;
            _units = units;
            GraphicsDevice gd = assets.GraphicsDevice;
            for (int k = 0; k < KindCount; k++)
                for (int b = 0; b < 2; b++)
                    for (int f = 0; f < 4; f++)
                        _tex[k, b, f] = LivestockArt.Frame((Kind)k, b == 1, f).ToTexture(gd);

            foreach (Unit u in units.Units)
                if (u is CitizenUnit c && c.Kind == CitizenArt.Shepherd)
                {
                    var herd = new Herd { Shepherd = c, Center = c.Position };
                    _herds.Add(herd);
                    // Boshlang'ich poda: 4 qo'y, 3 echki, 2 sigir va cho'pon iti
                    for (int i = 0; i < 4; i++) AddAnimal(herd, Kind.Sheep, false);
                    for (int i = 0; i < 3; i++) AddAnimal(herd, Kind.Goat, false);
                    for (int i = 0; i < 2; i++) AddAnimal(herd, Kind.Cow, false);
                    AddAnimal(herd, Kind.Dog, false, c.Position);
                }

            if (buildings != null) SpawnCoops(buildings);
            SpawnPonds();
            Recount();
        }

        /// <summary>Suv havzalarida (ko'lmak, daryo) o'rdaklar suzadi.</summary>
        private void SpawnPonds()
        {
            var water = new List<Vector2>();
            for (int y = 0; y < _map.Height; y++)
                for (int x = 0; x < _map.Width; x++)
                    if (_map.GetTile(x, y) == TileType.Water) water.Add(new Vector2(x + 0.5f, y + 0.5f));
            if (water.Count < 3) return;
            int ponds = water.Count > 24 ? 2 : 1;
            for (int p = 0; p < ponds; p++)
            {
                var pond = new Herd { Pond = true, Center = water[_rng.Next(water.Count)] };
                _herds.Add(pond);
                int ducks = 3 + _rng.Next(3);
                for (int i = 0; i < ducks; i++) AddAnimal(pond, Kind.Duck, false);
            }
        }

        /// <summary>Uylar yonidagi hovlilarda tovuqlar (har ikkinchi uyda, ko'pi bilan MaxCoops ta).</summary>
        private void SpawnCoops(BuildingManager buildings)
        {
            int n = 0, index = 0;
            foreach (Building b in buildings.Buildings)
            {
                if (b.Type != BuildingType.House || b.BuildProgress < 1f) continue;
                if (index++ % 2 != 0) continue;
                Vector2? yard = OpenNear(new Vector2(b.FrontEntranceTile.X + 0.5f, b.FrontEntranceTile.Y + 0.8f), 1.2f);
                if (!yard.HasValue) continue;
                var coop = new Herd { Coop = true, Center = yard.Value };
                _herds.Add(coop);
                int hens = 3 + _rng.Next(2);
                for (int i = 0; i < hens; i++) AddAnimal(coop, Kind.Chicken, false);
                if (_rng.Next(2) == 0) AddAnimal(coop, Kind.Chicken, true);
                if (++n >= MaxCoops) break;
            }
        }

        private Animal AddAnimal(Herd herd, Kind kind, bool baby, Vector2? near = null)
        {
            Vector2 p = kind == Kind.Duck
                ? (OpenWaterNear(near ?? herd.Center, 2.4f) ?? herd.Center)
                : (OpenNear(near ?? herd.Center, herd.Coop ? 1.0f : 2.2f) ?? herd.Center);
            var a = new Animal
            {
                Kind = kind, Herd = herd, Baby = baby, Pos = p, Target = p,
                Timer = (float)_rng.NextDouble() * 2f, FacingLeft = _rng.Next(2) == 0,
                Speed = kind switch { Kind.Cow => 0.75f, Kind.Goat => 1.05f, Kind.Chicken => 0.6f, Kind.Dog => 1.5f, Kind.Duck => 0.5f, _ => 0.9f }
            };
            a.Tex = _tex[(int)kind, baby ? 1 : 0, 0];
            herd.Animals.Add(a);
            return a;
        }

        private void Recount()
        {
            int s = 0, g = 0, c = 0, h = 0, d = 0;
            foreach (Herd hd in _herds)
                foreach (Animal a in hd.Animals)
                    switch (a.Kind)
                    {
                        case Kind.Sheep: s++; break;
                        case Kind.Goat: g++; break;
                        case Kind.Cow: c++; break;
                        case Kind.Chicken: h++; break;
                        case Kind.Dog: d++; break;
                        default: break; // Duck — podaga sanalmaydi
                    }
            Sheep = s; Goats = g; Cows = c; Chickens = h; Dogs = d;
        }

        private bool IsOpen(Vector2 p)
        {
            int x = (int)MathF.Floor(p.X), y = (int)MathF.Floor(p.Y);
            return _map.InBounds(x, y) && _map.GetTile(x, y) != TileType.Water && _map.GetOccupant(x, y) == null;
        }

        /// <summary>O'rdaklar uchun: faqat suv kataklari ochiq.</summary>
        private bool IsWaterOpen(Vector2 p)
        {
            int x = (int)MathF.Floor(p.X), y = (int)MathF.Floor(p.Y);
            return _map.InBounds(x, y) && _map.GetTile(x, y) == TileType.Water;
        }

        private bool CanStand(Animal a, Vector2 p) => a.Kind == Kind.Duck ? IsWaterOpen(p) : IsOpen(p);

        private Vector2? OpenWaterNear(Vector2 c, float radius)
        {
            for (int i = 0; i < 28; i++)
            {
                float a = (float)(_rng.NextDouble() * MathF.PI * 2), r = (float)Math.Sqrt(_rng.NextDouble()) * radius;
                Vector2 p = c + new Vector2(MathF.Cos(a), MathF.Sin(a)) * r;
                if (IsWaterOpen(p)) return p;
            }
            return null;
        }

        private Vector2? OpenNear(Vector2 c, float radius)
        {
            for (int i = 0; i < 24; i++)
            {
                float a = (float)(_rng.NextDouble() * MathF.PI * 2), r = (float)Math.Sqrt(_rng.NextDouble()) * radius;
                Vector2 p = c + new Vector2(MathF.Cos(a), MathF.Sin(a)) * r;
                if (IsOpen(p)) return p;
            }
            return null;
        }

        // ==================================================================
        // Ko'payish (har o'yin kuni)
        // ==================================================================

        /// <summary>Fasl ko'payishga ta'siri: bahorda ko'p, qishda kam bola tug'iladi.</summary>
        public static float BirthSeasonFactor(Season s) => s switch
        {
            Season.Bahor => 1.5f,
            Season.Yoz => 1.0f,
            Season.Kuz => 0.7f,
            _ => 0.3f
        };

        /// <returns>Shu kuni tug'ilganlar haqida qisqa xabar (yoki null).</returns>
        public string DayTick(GameStateManager state)
        {
            float seasonFactor = BirthSeasonFactor(state.Season);
            int born = 0;
            string last = null;
            foreach (Herd h in _herds)
            {
                foreach (Animal a in h.Animals)
                    if (a.Baby && ++a.AgeDays >= GrowUpDays) { a.Baby = false; a.AgeDays = 0; }

                Kind[] kinds = h.Coop ? new[] { Kind.Chicken } : new[] { Kind.Sheep, Kind.Goat, Kind.Cow };
                foreach (Kind k in kinds)
                {
                    if (h.Animals.Count >= h.Capacity) break;
                    int adults = 0;
                    Animal mother = null;
                    foreach (Animal a in h.Animals)
                        if (a.Kind == k && !a.Baby) { adults++; mother = a; }
                    if (adults < 2) continue;
                    float chance = k switch { Kind.Cow => 0.25f, Kind.Goat => 0.4f, Kind.Chicken => 0.5f, _ => 0.45f } * seasonFactor;
                    if (_rng.NextDouble() < chance)
                    {
                        AddAnimal(h, k, true, mother.Pos);
                        born++;
                        last = k switch { Kind.Sheep => "qo'zichoq", Kind.Goat => "uloq", Kind.Chicken => "jo'ja", _ => "buzoq" };
                    }
                }
            }

            // Sut, go'sht va tuxum: shahar oziq-ovqati (it hisobga kirmaydi)
            int adultsAll = 0, cows = 0, hens = 0;
            foreach (Herd h in _herds)
                foreach (Animal a in h.Animals)
                {
                    if (a.Baby || a.Kind == Kind.Dog || a.Kind == Kind.Duck) continue;
                    if (a.Kind == Kind.Chicken) { hens++; continue; }
                    adultsAll++;
                    if (a.Kind == Kind.Cow) cows++;
                }
            int food = adultsAll / 3 + cows + hens / 2;
            if (food > 0) state.Food += food;

            Recount();
            if (born == 0) return null;
            string counts = " (Qo'y: " + Sheep + ", echki: " + Goats + ", sigir: " + Cows + ", tovuq: " + Chickens + ")  +" + food + " oziq";
            return born == 1 ? "Chorvada yangi " + last + " tug'ildi!" + counts : "Chorvada " + born + " ta bola tug'ildi!" + counts;
        }

        // ==================================================================
        // Harakat
        // ==================================================================

        public void Update(float dt)
        {
            if (dt <= 0f) return;
            foreach (Herd h in _herds)
            {
                if (h.Shepherd != null && !h.Shepherd.Hidden)
                    h.Center = Vector2.Lerp(h.Center, h.Shepherd.Position, 1f - MathF.Exp(-1.2f * dt));
                foreach (Animal a in h.Animals)
                {
                    if (a.Kind == Kind.Dog) UpdateDog(a, dt);
                    else if (a.Kind == Kind.Chicken) UpdateChicken(a, dt);
                    else if (a.Kind == Kind.Duck) UpdateDuck(a, dt);
                    else UpdateAnimal(a, dt);
                }
                // Jonivorlar bir-biriga urilib, ustma-ust tushib qolmasin — yengil ajralish kuchi
                foreach (Animal a in h.Animals) Separate(h, a);
            }
        }

        /// <summary>Yaqin turgan poda a'zolarini bir-biridan yengil itaradi (ustma-ust tushish/"urishish" bagini oldini oladi).</summary>
        private void Separate(Herd h, Animal a)
        {
            float min = a.Herd.Coop ? 0.3f : 0.42f;
            Vector2 push = Vector2.Zero;
            foreach (Animal o in h.Animals)
            {
                if (o == a) continue;
                Vector2 d = a.Pos - o.Pos;
                float dist = d.Length();
                if (dist > 0.0001f && dist < min) push += d / dist * (min - dist);
                else if (dist <= 0.0001f) push += new Vector2((float)_rng.NextDouble() - 0.5f, (float)_rng.NextDouble() - 0.5f) * min;
            }
            if (push == Vector2.Zero) return;
            Vector2 np = a.Pos + push * 0.6f;
            if (CanStand(a, np)) a.Pos = np;
            else if (CanStand(a, new Vector2(np.X, a.Pos.Y))) a.Pos = new Vector2(np.X, a.Pos.Y);
            else if (CanStand(a, new Vector2(a.Pos.X, np.Y))) a.Pos = new Vector2(a.Pos.X, np.Y);
        }

        private Unit NearestEnemy(Vector2 p, float range)
        {
            Unit best = null;
            float bestD = range * range;
            foreach (Unit u in _units.Units)
            {
                if (u.Team != UnitTeam.Enemy || u.IsDown) continue;
                float d = Vector2.DistanceSquared(u.Position, p);
                if (d < bestD) { bestD = d; best = u; }
            }
            return best;
        }

        private void UpdateAnimal(Animal a, float dt)
        {
            Unit danger = NearestEnemy(a.Pos, 2.5f);
            float speed = a.Speed;
            if (danger != null)
            {
                Vector2 away = a.Pos - danger.Position;
                if (away.LengthSquared() < 0.001f) away = new Vector2(1, 0);
                away.Normalize();
                a.Target = a.Pos + away * 2f;
                a.Moving = true; a.Grazing = false;
                speed *= 2.4f;
            }
            else
            {
                float far = Vector2.Distance(a.Pos, a.Herd.Center);
                if (far > 3.2f)
                {
                    // Poda ortidan qolib ketdi — cho'pon tomon yuradi
                    a.Target = a.Herd.Center + new Vector2((float)_rng.NextDouble() - 0.5f, (float)_rng.NextDouble() - 0.5f) * 2f;
                    a.Moving = true; a.Grazing = false;
                    speed *= far > 6f ? 2.2f : 1.4f;
                }
                else
                {
                    a.Timer -= dt;
                    if (a.Timer <= 0f && !a.Moving)
                    {
                        int act = _rng.Next(10);
                        if (act < 5) { a.Grazing = true; a.Timer = 1.5f + (float)_rng.NextDouble() * 3f; }
                        else if (act < 8)
                        {
                            Vector2? t = OpenNear(a.Herd.Center, 2.6f);
                            if (t.HasValue) { a.Target = t.Value; a.Moving = true; a.Grazing = false; }
                            a.Timer = 1f + (float)_rng.NextDouble() * 2f;
                        }
                        else { a.Grazing = false; a.Timer = 1f + (float)_rng.NextDouble() * 2f; }
                    }
                }
            }
            if (!a.Moving) a.Rush = 1f;
            Step(a, speed * a.Rush, dt, a.Grazing);
        }

        /// <summary>
        /// CHO'PON ITI: cho'pon yonida yuradi; podadan uzoqlashgan jonivorni orqasidan aylanib, poda tomon haydaydi;
        /// dushman yaqinlashsa unga qarab yugurib, oraliqda turib akillaydi (poda shu orada qochadi).
        /// </summary>
        private void UpdateDog(Animal dog, float dt)
        {
            Herd h = dog.Herd;
            float speed = dog.Speed;
            bool sit = false;

            Unit enemy = NearestEnemy(dog.Pos, 5f);
            if (enemy != null)
            {
                Vector2 toEnemy = enemy.Position - dog.Pos;
                float d = toEnemy.Length();
                if (d > 1.1f) { dog.Target = enemy.Position - toEnemy / d * 1.0f; dog.Moving = true; speed *= 1.8f; }
                else { dog.Moving = false; sit = (int)(dog.Anim * 3f) % 2 == 0; dog.Anim += dt * 4f; } // akillash
            }
            else
            {
                dog.Timer -= dt;
                if (dog.Timer <= 0f)
                {
                    dog.Timer = 0.35f;
                    // Eng uzoqlashgan jonivor
                    Animal stray = null;
                    float worst = 2.4f;
                    foreach (Animal a in h.Animals)
                    {
                        if (a == dog) continue;
                        float far = Vector2.Distance(a.Pos, h.Center);
                        if (far > worst) { worst = far; stray = a; }
                    }
                    if (stray != null)
                    {
                        // Jonivorning narigi tomoniga o'tib, uni poda tomon haydaydi
                        Vector2 outDir = stray.Pos - h.Center;
                        if (outDir.LengthSquared() < 0.001f) outDir = new Vector2(1, 0);
                        outDir.Normalize();
                        dog.Target = stray.Pos + outDir * 0.8f;
                        dog.Moving = true;
                        dog.Rush = 1.7f;
                        if (Vector2.Distance(dog.Pos, stray.Pos) < 1.3f)
                        {
                            stray.Target = h.Center + new Vector2((float)_rng.NextDouble() - 0.5f, (float)_rng.NextDouble() - 0.5f);
                            stray.Moving = true; stray.Grazing = false; stray.Rush = 1.8f;
                        }
                    }
                    else if (h.Shepherd != null)
                    {
                        // Cho'pon yonida (biroz yon tomonda) yuradi
                        Vector2 side = h.Shepherd.Position + new Vector2(0.7f, 0.6f);
                        float d = Vector2.Distance(dog.Pos, side);
                        dog.Rush = d > 3f ? 1.6f : 1f;
                        if (d > 1.2f) { dog.Target = side; dog.Moving = true; }
                        else if (!dog.Moving && _rng.Next(6) == 0)
                        {
                            Vector2? t = OpenNear(h.Center, 2.8f); // podani aylanib chiqadi
                            if (t.HasValue) { dog.Target = t.Value; dog.Moving = true; }
                        }
                    }
                }
                if (!dog.Moving) sit = true;
            }
            Step(dog, speed * dog.Rush, dt, sit);
        }

        /// <summary>O'RDAK: suv havzasida sekin suzadi, vaqti-vaqti boshini suvga tiqib ozuqa izlaydi.</summary>
        private void UpdateDuck(Animal a, float dt)
        {
            a.Timer -= dt;
            float far = Vector2.Distance(a.Pos, a.Herd.Center);
            if (!a.Moving && (a.Timer <= 0f || far > 3.2f))
            {
                int act = _rng.Next(10);
                if (act < 4 && far < 3f) { a.Grazing = true; a.Timer = 1.6f + (float)_rng.NextDouble() * 2.6f; } // suvdan ozuqa (bosh suvga)
                else
                {
                    Vector2? t = OpenWaterNear(a.Herd.Center, 2.6f);
                    if (t.HasValue) { a.Target = t.Value; a.Moving = true; a.Grazing = false; }
                    a.Timer = 1.2f + (float)_rng.NextDouble() * 2f;
                }
            }
            Step(a, a.Speed, dt, a.Grazing);
        }

        /// <summary>TOVUQ: hovlida don cho'qiydi, odam yoki jonivor yaqinlashsa pirillab qochadi.</summary>
        private void UpdateChicken(Animal a, float dt)
        {
            float speed = a.Speed;
            Unit scare = null;
            foreach (Unit u in _units.Units)
            {
                if (u.Hidden || u.IsDown) continue;
                float range = u.State == UnitState.Walking ? 1.3f : 0.9f;
                if (Vector2.DistanceSquared(u.Position, a.Pos) < range * range) { scare = u; break; }
            }
            if (scare != null)
            {
                Vector2 away = a.Pos - scare.Position;
                if (away.LengthSquared() < 0.001f) away = new Vector2(1, 0);
                away.Normalize();
                // Biroz yon tomonga ham og'adi — tovuqlar to'g'ri chiziqda qochmaydi
                away = Vector2.Normalize(away + new Vector2(-away.Y, away.X) * ((float)_rng.NextDouble() - 0.5f));
                a.Target = a.Pos + away * 1.4f;
                a.Moving = true; a.Grazing = false;
                speed *= 3.2f;
            }
            else
            {
                float far = Vector2.Distance(a.Pos, a.Herd.Center);
                a.Timer -= dt;
                if (far > 2.4f && !a.Moving)
                {
                    a.Target = OpenNear(a.Herd.Center, 1.0f) ?? a.Herd.Center;
                    a.Moving = true; a.Grazing = false;
                }
                else if (a.Timer <= 0f && !a.Moving)
                {
                    int act = _rng.Next(10);
                    if (act < 6) { a.Grazing = true; a.Timer = 0.6f + (float)_rng.NextDouble() * 1.6f; }
                    else
                    {
                        Vector2? t = OpenNear(a.Herd.Center, 1.6f);
                        if (t.HasValue) { a.Target = t.Value; a.Moving = true; a.Grazing = false; }
                        a.Timer = 0.5f + (float)_rng.NextDouble() * 1.2f;
                    }
                }
            }
            // Cho'qish: bosh tez-tez pastga-yuqoriga
            bool peck = a.Grazing && (int)(a.Timer * 7f) % 2 == 0;
            Step(a, speed, dt, peck);
        }

        /// <summary>Nishon tomon bir qadam va kadr tanlash (frame 3 — o'tlash / cho'qish / o'tirish).</summary>
        private void Step(Animal a, float speed, float dt, bool restPose)
        {
            int k = (int)a.Kind, b = a.Baby ? 1 : 0;
            if (a.Moving)
            {
                Vector2 to = a.Target - a.Pos;
                float d = to.Length();
                if (d < 0.06f) { a.Moving = false; }
                else
                {
                    Vector2 step = to / d * MathF.Min(speed * dt, d);
                    Vector2 np = a.Pos + step;
                    if (CanStand(a, np)) a.Pos = np;
                    else if (CanStand(a, new Vector2(np.X, a.Pos.Y))) a.Pos = new Vector2(np.X, a.Pos.Y);
                    else if (CanStand(a, new Vector2(a.Pos.X, np.Y))) a.Pos = new Vector2(a.Pos.X, np.Y);
                    else a.Moving = false;
                    float sx = IsoMath.TileToWorld(step).X;
                    if (MathF.Abs(sx) > 0.0001f) a.FacingLeft = sx < 0;
                }
                a.Anim += dt * speed * 7f;
                a.Tex = _tex[k, b, 1 + ((int)a.Anim % 2)];
            }
            else a.Tex = _tex[k, b, restPose ? 3 : 0];
        }

        // ==================================================================
        // Chizish
        // ==================================================================

        /// <summary>Izometrik ko'rinish: jonivorlar binolar va odamlar bilan birga saralanadi.</summary>
        public IReadOnlyList<IWorldObject> GroundObjects
        {
            get
            {
                _objects.Clear();
                foreach (Herd h in _herds) foreach (Animal a in h.Animals) _objects.Add(a);
                return _objects;
            }
        }

        /// <summary>Tanlangan yoki boshqarilayotgan cho'ponning podasi haqida kichik panel.</summary>
        public void DrawHerdInfo(SpriteBatch sb, Texture2D px, PixelFont font, Unit focus, int vw)
        {
            if (!(focus is CitizenUnit c) || c.Kind != CitizenArt.Shepherd) return;
            Herd herd = null;
            foreach (Herd h in _herds) if (h.Shepherd == c) herd = h;
            if (herd == null) return;
            int s = 0, g = 0, cw = 0, dogs = 0, babies = 0;
            foreach (Animal a in herd.Animals)
            {
                if (a.Baby) babies++;
                if (a.Kind == Kind.Sheep) s++; else if (a.Kind == Kind.Goat) g++; else if (a.Kind == Kind.Cow) cw++; else if (a.Kind == Kind.Dog) dogs++;
            }
            string t = "PODA:  " + s + " qo'y   " + g + " echki   " + cw + " sigir" + (dogs > 0 ? "   " + dogs + " it" : "")
                     + (babies > 0 ? "   (" + babies + " bola)" : "") + "   /" + HerdCapacity;
            Vector2 size = font.Measure(t, 2);
            var r = new Rectangle(vw / 2 - (int)size.X / 2 - 14, 82, (int)size.X + 28, 28);
            sb.Draw(px, new Rectangle(r.X - 2, r.Y - 2, r.Width + 4, r.Height + 4), new Color(120, 160, 80, 230));
            sb.Draw(px, r, new Color(22, 30, 18, 235));
            font.DrawCentered(sb, t, r.Center.ToVector2(), new Color(214, 240, 170), 2);
        }
    }

    /// <summary>Chorva sprite'lari (o'ngga qarab). Bolalar — kichikroq (0.65 masshtab).</summary>
    public static class LivestockArt
    {
        private static readonly Color Ink = new Color(36, 28, 24);

        /// <param name="frame">0 — turish, 1-2 — yurish, 3 — o'tlash / cho'qish (bosh pastda), itda — o'tirish.</param>
        public static Canvas Frame(LivestockSystem.Kind kind, bool baby, int frame)
        {
            float s = baby ? 0.65f : 1f;
            switch (kind)
            {
                case LivestockSystem.Kind.Goat: return Goat(s, frame);
                case LivestockSystem.Kind.Cow: return Cow(s, frame);
                case LivestockSystem.Kind.Chicken: return baby ? Chick(frame) : Hen(frame);
                case LivestockSystem.Kind.Dog: return Dog(s, frame);
                case LivestockSystem.Kind.Duck: return Duck(frame);
                default: return Sheep(s, frame);
            }
        }

        private static int Sz(float v, float s) => Math.Max(4, (int)MathF.Ceiling(v * s));

        private static void Legs(Canvas c, float s, int frame, float[] xs, float top, float bottom, Color col)
        {
            for (int i = 0; i < xs.Length; i++)
            {
                float off = frame == 1 ? (i % 2 == 0 ? 1.2f : -1.2f) : frame == 2 ? (i % 2 == 0 ? -1.2f : 1.2f) : 0f;
                int x = (int)MathF.Round((xs[i] + off) * s);
                c.FillRect(x, (int)(top * s), Math.Max(1, (int)(2 * s)), Math.Max(2, (int)((bottom - top) * s)), col);
            }
        }

        private static Canvas Sheep(float s, int frame)
        {
            var c = new Canvas(Sz(24, s), Sz(17, s));
            Color wool = new Color(238, 234, 222), woolDark = new Color(212, 206, 192), face = new Color(52, 44, 40);
            c.FillEllipse(12 * s, 16 * s, 8 * s, 1.4f * s, new Color(0, 0, 0, 60));
            Legs(c, s, frame, new[] { 6f, 9f, 14f, 17f }, 11, 16, face);
            // jingalak jun (bir nechta dumaloq)
            c.FillEllipse(11.5f * s, 8.5f * s, 7.5f * s, 4.6f * s, wool, 0.05f);
            for (int i = 0; i < 5; i++) c.FillEllipse((6 + i * 2.6f) * s, (5.8f + (i % 2)) * s, 2.4f * s, 2.2f * s, i % 2 == 0 ? wool : woolDark);
            c.FillEllipse(4.5f * s, 8 * s, 1.8f * s, 1.6f * s, woolDark); // dum
            bool graze = frame == 3;
            float hx = graze ? 20f : 19.5f, hy = graze ? 11.5f : 6.5f;
            c.FillEllipse(hx * s, hy * s, 2.6f * s, 2.2f * s, face);
            c.FillEllipse((hx - 1.5f) * s, (hy - 2f) * s, 1.4f * s, 0.9f * s, face); // quloq
            c.FillRect((int)((hx + 0.5f) * s), (int)((hy - 0.8f) * s), 1, 1, new Color(240, 220, 120));
            c.Outline(Ink, 0.4f);
            return c;
        }

        private static Canvas Goat(float s, int frame)
        {
            var c = new Canvas(Sz(22, s), Sz(19, s));
            Color fur = new Color(124, 86, 56), dark = new Color(70, 48, 34), light = new Color(214, 196, 170);
            c.FillEllipse(11 * s, 18 * s, 7 * s, 1.3f * s, new Color(0, 0, 0, 60));
            Legs(c, s, frame, new[] { 6f, 8.5f, 13f, 15.5f }, 12, 18, dark);
            c.FillEllipse(10.5f * s, 10 * s, 6.5f * s, 3.6f * s, fur, 0.06f);
            c.FillEllipse(10.5f * s, 11.5f * s, 4.5f * s, 1.6f * s, light);     // qorin
            c.FillPolygon(dark, 0f, new Vector2(4 * s, 8 * s), new Vector2(3 * s, 4.5f * s), new Vector2(5.5f * s, 7.5f * s)); // dikka dum
            bool graze = frame == 3;
            float hx = graze ? 18.5f : 17.5f, hy = graze ? 13.5f : 6f;
            c.FillEllipse(hx * s, hy * s, 2.4f * s, 2.1f * s, fur);
            c.Line(new Vector2((hx - 0.5f) * s, (hy - 1.8f) * s), new Vector2((hx - 3f) * s, (hy - 4.5f) * s), new Color(200, 190, 170)); // shoxlar
            c.Line(new Vector2((hx + 0.5f) * s, (hy - 1.8f) * s), new Vector2((hx - 1.5f) * s, (hy - 4.8f) * s), new Color(200, 190, 170));
            c.FillRect((int)((hx + 0.6f) * s), (int)((hy + 1.6f) * s), Math.Max(1, (int)(1.2f * s)), Math.Max(1, (int)(2.4f * s)), light); // soqol
            c.FillRect((int)((hx + 0.5f) * s), (int)((hy - 0.6f) * s), 1, 1, Ink);
            c.Outline(Ink, 0.4f);
            return c;
        }

        private static Canvas Cow(float s, int frame)
        {
            var c = new Canvas(Sz(32, s), Sz(23, s));
            Color body = new Color(236, 230, 220), patch = new Color(120, 72, 44), dark = new Color(60, 40, 30);
            c.FillEllipse(16 * s, 22 * s, 11 * s, 1.6f * s, new Color(0, 0, 0, 60));
            Legs(c, s, frame, new[] { 7f, 10f, 20f, 23f }, 14, 22, dark);
            c.FillEllipse(15 * s, 11 * s, 10 * s, 5.5f * s, body, 0.04f);
            c.FillBlob(11 * s, 9.5f * s, 3.6f * s, 2.8f * s, patch, 0.06f);   // ola dog'lar
            c.FillBlob(18.5f * s, 12 * s, 3f * s, 2.4f * s, patch, 0.06f);
            c.FillEllipse(17 * s, 15.5f * s, 2.2f * s, 1.2f * s, new Color(232, 160, 160)); // yelin
            c.Line(new Vector2(5 * s, 9 * s), new Vector2(4 * s, 15 * s), dark);            // dum
            bool graze = frame == 3;
            float hx = graze ? 27f : 26f, hy = graze ? 16.5f : 8f;
            c.FillEllipse(hx * s, hy * s, 3.4f * s, 2.8f * s, patch);
            c.FillEllipse((hx + 1.8f) * s, (hy + 1f) * s, 1.8f * s, 1.5f * s, new Color(220, 170, 150)); // tumshuq
            c.Line(new Vector2((hx - 1f) * s, (hy - 2.4f) * s), new Vector2((hx - 3f) * s, (hy - 4.2f) * s), new Color(236, 226, 200)); // shoxlar
            c.Line(new Vector2((hx + 1f) * s, (hy - 2.4f) * s), new Vector2((hx + 2.5f) * s, (hy - 4.4f) * s), new Color(236, 226, 200));
            c.FillRect((int)((hx + 0.2f) * s), (int)((hy - 0.8f) * s), 1, 1, Ink);
            c.Outline(Ink, 0.4f);
            return c;
        }

        /// <summary>Tovuq: jigarrang-oq patlar, qizil toj, sariq tumshuq. frame 3 — don cho'qish.</summary>
        private static Canvas Hen(int frame)
        {
            var c = new Canvas(13, 13);
            Color body = new Color(196, 132, 74), wing = new Color(150, 94, 52), light = new Color(236, 214, 176);
            Color comb = new Color(214, 48, 40), beak = new Color(238, 186, 60), leg = new Color(222, 160, 60);
            c.FillEllipse(6.5f, 12.2f, 4f, 0.9f, new Color(0, 0, 0, 60));
            // oyoqlar
            int l1 = frame == 1 ? 1 : frame == 2 ? -1 : 0;
            c.FillRect(5 + l1, 9, 1, 3, leg);
            c.FillRect(7 - l1, 9, 1, 3, leg);
            // dum patlari (yuqoriga ko'tarilgan)
            c.FillPolygon(wing, 0f, new Vector2(1, 3), new Vector2(4, 6), new Vector2(2.5f, 7.5f));
            c.FillEllipse(6f, 7f, 4f, 2.8f, body, 0.06f);
            c.FillEllipse(6.5f, 8f, 2.6f, 1.4f, light);           // ko'krak
            c.FillEllipse(5.2f, 6.6f, 2.2f, 1.4f, wing);           // qanot
            bool peck = frame == 3;
            float hx = peck ? 10.5f : 9.2f, hy = peck ? 9.2f : 4.2f;
            c.FillEllipse(hx, hy, 1.8f, 1.7f, body);
            c.FillRect((int)hx - 1, (int)hy - 3, 2, 1, comb);      // toj
            c.FillRect((int)hx, (int)hy - 2, 1, 1, comb);
            c.FillRect((int)hx + 2, (int)hy, 1, 1, beak);          // tumshuq
            c.FillRect((int)hx + 1, (int)hy + 1, 1, 1, comb);      // soqolcha
            c.FillRect((int)hx, (int)hy - 1, 1, 1, Ink);           // ko'z
            c.Outline(Ink, 0.4f);
            return c;
        }

        /// <summary>O'rdak: suvda suzadi, pastki qismi suv sathida. frame 3 — boshini suvga tiqish.</summary>
        private static Canvas Duck(int frame)
        {
            var c = new Canvas(14, 10);
            Color body = new Color(238, 232, 218), wing = new Color(150, 120, 86), head = new Color(60, 118, 84), beak = new Color(238, 168, 58);
            c.FillEllipse(7f, 8.6f, 5.5f, 1.0f, new Color(40, 96, 128, 90)); // suv aksi
            c.FillEllipse(6.5f, 6f, 5f, 2.5f, body, 0.06f);                   // tana
            c.FillEllipse(4.0f, 5.4f, 2.3f, 1.5f, wing);                      // qanot
            c.FillPolygon(body, 0f, new Vector2(1.4f, 4.8f), new Vector2(3.4f, 6.2f), new Vector2(1.9f, 6.6f)); // dum
            bool feed = frame == 3;
            float hx = feed ? 10.6f : 10.2f, hy = feed ? 6.6f : 2.7f;
            c.Line(new Vector2(9f, 5f), new Vector2(hx, hy + 1f), head, 2);   // bo'yin
            c.FillEllipse(hx, hy, 1.8f, 1.7f, head);                          // bosh
            c.FillRect((int)hx + 1, (int)hy, 2, 1, beak);                     // tumshuq
            c.FillRect((int)hx, (int)hy - 1, 1, 1, Ink);                      // ko'z
            c.Outline(Ink, 0.4f);
            return c;
        }

        /// <summary>Jo'ja: kichkina sariq yumaloq.</summary>
        private static Canvas Chick(int frame)
        {
            var c = new Canvas(8, 7);
            Color fluff = new Color(250, 222, 96), beak = new Color(232, 150, 50);
            c.FillEllipse(4, 6.4f, 2.6f, 0.6f, new Color(0, 0, 0, 55));
            int l1 = frame == 1 ? 1 : frame == 2 ? -1 : 0;
            c.FillRect(3 + l1, 5, 1, 1, beak);
            c.FillRect(5 - l1, 5, 1, 1, beak);
            c.FillEllipse(3.6f, 3.6f, 2.6f, 1.9f, fluff);
            bool peck = frame == 3;
            float hx = peck ? 6f : 5.4f, hy = peck ? 4.2f : 2.2f;
            c.FillEllipse(hx, hy, 1.4f, 1.3f, fluff);
            c.FillRect((int)hx + 1, (int)hy, 1, 1, beak);
            c.FillRect((int)hx, (int)hy - 1, 1, 1, Ink);
            c.Outline(new Color(150, 110, 40), 0.35f);
            return c;
        }

        /// <summary>Cho'pon iti (alabay uslubida): och sarg'ish jun, qoramtir quloq va tumshuq. frame 3 — o'tirish.</summary>
        private static Canvas Dog(float s, int frame)
        {
            var c = new Canvas(Sz(22, s), Sz(16, s));
            Color fur = new Color(222, 196, 150), dark = new Color(96, 70, 48), light = new Color(240, 226, 196);
            c.FillEllipse(11 * s, 15.2f * s, 7 * s, 1.2f * s, new Color(0, 0, 0, 60));
            bool sit = frame == 3;
            if (sit)
            {
                // O'tirgan it: orqa qismi yerda, old oyoqlar tik, bosh baland
                c.FillEllipse(8.5f * s, 11 * s, 4.6f * s, 3.4f * s, fur, 0.05f);
                c.FillRect((int)(12 * s), (int)(10 * s), Math.Max(1, (int)(2 * s)), Math.Max(2, (int)(5 * s)), fur);
                c.FillRect((int)(14 * s), (int)(10 * s), Math.Max(1, (int)(2 * s)), Math.Max(2, (int)(5 * s)), Canvas.Shade(fur, 0.9f));
                c.FillEllipse(13 * s, 8.5f * s, 3 * s, 3 * s, fur);            // ko'krak
                c.Line(new Vector2(4 * s, 12 * s), new Vector2(2 * s, 9.5f * s), fur, Math.Max(1, (int)(2 * s))); // dum
                float hx = 15.5f, hy = 4.6f;
                c.FillEllipse(hx * s, hy * s, 2.8f * s, 2.5f * s, fur);
                c.FillEllipse((hx + 2.4f) * s, (hy + 0.9f) * s, 1.6f * s, 1.2f * s, dark); // tumshuq
                c.FillEllipse((hx - 1.4f) * s, (hy - 1.6f) * s, 1.1f * s, 1.5f * s, dark); // quloq
                c.FillRect((int)((hx + 0.6f) * s), (int)((hy - 0.6f) * s), 1, 1, Ink);
            }
            else
            {
                Legs(c, s, frame, new[] { 5f, 7.5f, 13.5f, 16f }, 10, 15, Canvas.Shade(fur, 0.85f));
                c.FillEllipse(10.5f * s, 8.5f * s, 6.8f * s, 3.2f * s, fur, 0.05f);
                c.FillEllipse(11 * s, 10 * s, 4.5f * s, 1.4f * s, light);     // qorin
                // dum: yurganda halqa bo'lib yuqoriga
                c.Line(new Vector2(4 * s, 7 * s), new Vector2(2 * s, 4 * s), fur, Math.Max(1, (int)(2 * s)));
                c.FillRect((int)(2 * s), (int)(3.5f * s), Math.Max(1, (int)(2 * s)), Math.Max(1, (int)(1.5f * s)), fur);
                float hx = 17.5f, hy = 6f;
                c.FillEllipse(hx * s, hy * s, 2.8f * s, 2.4f * s, fur);
                c.FillEllipse((hx + 2.4f) * s, (hy + 0.8f) * s, 1.6f * s, 1.2f * s, dark); // tumshuq
                c.FillEllipse((hx - 1.4f) * s, (hy - 1.6f) * s, 1.1f * s, 1.5f * s, dark); // quloq
                c.FillRect((int)((hx + 0.6f) * s), (int)((hy - 0.6f) * s), 1, 1, Ink);
            }
            c.Outline(Ink, 0.4f);
            return c;
        }
    }
}
