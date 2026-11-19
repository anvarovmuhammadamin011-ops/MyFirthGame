using System;
using System.Collections.Generic;
using System.Linq;
using AndijonSaltanatlar.Buildings;
using AndijonSaltanatlar.Core;
using AndijonSaltanatlar.Core.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace AndijonSaltanatlar.World
{
    /// <summary>
    /// OT-ARAVALAR: aravakash fermadan yoki karvonsaroydan yuk (qoplar) ortadi, ko'chalar bo'ylab bozorga
    /// (bozor bo'lmasa saroyga) olib boradi, tushiradi va bo'sh qaytadi. Har yetkazishda shahar ozgina daromad oladi.
    /// </summary>
    public sealed class CartSystem
    {
        private enum State { ToSource, Loading, ToDest, Unloading, Idle }

        private sealed class Cart : IWorldObject
        {
            public Vector2 Pos;
            public List<Vector2> Path;
            public int PathIndex;
            public State State;
            public float Timer;
            public Building Source, Dest;
            public bool Loaded;
            public bool FacingLeft;
            public float Anim;
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
                    return new Vector2(MathF.Round(w.X - Tex.Width / 2f), MathF.Round(w.Y - Tex.Height + 4));
                }
            }
        }

        public const int MaxCarts = 4;
        private const float Speed = 1.35f;          // tile/soniya
        private const int DeliveryIncome = 3;

        private readonly List<Cart> _carts = new List<Cart>();
        private readonly List<IWorldObject> _objects = new List<IWorldObject>();
        private readonly Texture2D[,] _tex = new Texture2D[2, 4]; // [yuklangan][kadr]
        private float _syncTimer;
        private int _nextSource;

        public int Count => _carts.Count;

        public CartSystem(AssetManager assets)
        {
            GraphicsDevice gd = assets.GraphicsDevice;
            for (int l = 0; l < 2; l++)
                for (int f = 0; f < 4; f++)
                    _tex[l, f] = CartArt.Frame(l == 1, f).ToTexture(gd);
        }

        public IReadOnlyList<IWorldObject> Objects
        {
            get
            {
                _objects.Clear();
                foreach (Cart c in _carts) _objects.Add(c);
                return _objects;
            }
        }

        public void Update(float dt, MapManager map, BuildingManager buildings, GameStateManager state)
        {
            if (dt <= 0f) return;
            _syncTimer -= dt;
            if (_syncTimer <= 0f) { _syncTimer = 3f; Sync(map, buildings); }

            foreach (Cart c in _carts)
            {
                switch (c.State)
                {
                    case State.ToSource:
                    case State.ToDest:
                        if (Move(c, dt))
                        {
                            c.State = c.State == State.ToSource ? State.Loading : State.Unloading;
                            c.Timer = 2.2f;
                        }
                        break;
                    case State.Loading:
                        c.Timer -= dt;
                        if (c.Timer <= 0f) { c.Loaded = true; Route(c, map, c.Dest, State.ToDest); }
                        break;
                    case State.Unloading:
                        c.Timer -= dt;
                        if (c.Timer <= 0f)
                        {
                            c.Loaded = false;
                            state.Coins += DeliveryIncome;
                            Route(c, map, c.Source, State.ToSource);
                        }
                        break;
                    case State.Idle:
                        c.Timer -= dt;
                        if (c.Timer <= 0f) Route(c, map, c.Loaded ? c.Dest : c.Source, c.Loaded ? State.ToDest : State.ToSource);
                        break;
                }
                int frame = c.State == State.ToSource || c.State == State.ToDest ? (int)c.Anim % 4 : 0;
                c.Tex = _tex[c.Loaded ? 1 : 0, frame];
            }
        }

        /// <returns>Manzilga yetdimi.</returns>
        private bool Move(Cart c, float dt)
        {
            if (c.Path == null || c.PathIndex >= c.Path.Count) return true;
            float step = Speed * dt;
            while (step > 0f && c.PathIndex < c.Path.Count)
            {
                Vector2 target = c.Path[c.PathIndex];
                Vector2 to = target - c.Pos;
                float d = to.Length();
                if (d <= step) { c.Pos = target; c.PathIndex++; step -= d; continue; }
                Vector2 mv = to / d * step;
                c.Pos += mv;
                float sx = IsoMath.TileToWorld(mv).X;
                if (MathF.Abs(sx) > 0.0001f) c.FacingLeft = sx < 0;
                step = 0f;
            }
            c.Anim += dt * 7f;
            return c.PathIndex >= c.Path.Count;
        }

        private void Route(Cart c, MapManager map, Building to, State next)
        {
            Point? goal = Entrance(map, to);
            var start = new Point((int)MathF.Floor(c.Pos.X), (int)MathF.Floor(c.Pos.Y));
            List<Point> path = goal.HasValue ? Pathfinder.FindPath(map, start, goal.Value) : null;
            if (path == null || path.Count == 0)
            {
                c.State = State.Idle; c.Timer = 4f; c.Path = null;
                return;
            }
            c.Path = path.Select(p => new Vector2(p.X + 0.5f, p.Y + 0.5f)).ToList();
            c.PathIndex = 0;
            c.State = next;
        }

        private void Sync(MapManager map, BuildingManager buildings)
        {
            var sources = buildings.Buildings.Where(b => b.BuildProgress >= 1f && (b.Type == BuildingType.Farm || b.Type == BuildingType.Caravanserai)).ToList();
            var dests = buildings.Buildings.Where(b => b.BuildProgress >= 1f && b.Type == BuildingType.Market).ToList();
            if (dests.Count == 0) dests = buildings.Buildings.Where(b => b.BuildProgress >= 1f && b.Type == BuildingType.MainHall).ToList();

            // Buzilgan bino bilan bog'langan aravalarni olib tashlash
            _carts.RemoveAll(c => !buildings.Buildings.Contains(c.Source) || !buildings.Buildings.Contains(c.Dest));
            if (sources.Count == 0 || dests.Count == 0) { _carts.Clear(); return; }

            int want = Math.Min(MaxCarts, Math.Max(1, (sources.Count + 1) / 2));
            while (_carts.Count < want)
            {
                Building src = sources[_nextSource++ % sources.Count];
                Building dst = dests.OrderBy(d => Vector2.DistanceSquared(new Vector2(d.Position.X, d.Position.Y), new Vector2(src.Position.X, src.Position.Y))).First();
                Point? at = Entrance(map, dst);
                if (!at.HasValue) break;
                var cart = new Cart { Pos = new Vector2(at.Value.X + 0.5f, at.Value.Y + 0.5f), Source = src, Dest = dst, Tex = _tex[0, 0] };
                _carts.Add(cart);
                Route(cart, map, src, State.ToSource);
            }
        }

        /// <summary>Bino oldidagi yuriladigan katak (old kirish, bo'lmasa atrofdagi birinchi ochiq katak).</summary>
        private static Point? Entrance(MapManager map, Building b)
        {
            Point e = b.FrontEntranceTile;
            if (map.IsPassable(e.X, e.Y)) return e;
            for (int r = 1; r <= 3; r++)
                for (int y = b.Position.Y - r; y < b.Position.Y + b.Size.Y + r; y++)
                    for (int x = b.Position.X - r; x < b.Position.X + b.Size.X + r; x++)
                    {
                        bool inside = x >= b.Position.X && x < b.Position.X + b.Size.X && y >= b.Position.Y && y < b.Position.Y + b.Size.Y;
                        if (!inside && map.IsPassable(x, y)) return new Point(x, y);
                    }
            return null;
        }
    }

    /// <summary>Ot-arava sprite'i (o'ngga qarab): ot, ikki g'ildirakli arava, aravakash va (yuklangan bo'lsa) qoplar.</summary>
    public static class CartArt
    {
        private static readonly Color Ink = new Color(36, 28, 24);

        /// <param name="frame">0..3 — otning oyoqlari va g'ildirak parraklari aylanishi.</param>
        public static Canvas Frame(bool loaded, int frame)
        {
            var c = new Canvas(46, 32);
            Color wood = new Color(146, 100, 58), woodD = new Color(104, 70, 40);
            Color horse = new Color(132, 84, 50), horseD = new Color(86, 54, 32), mane = new Color(52, 34, 22);

            c.FillEllipse(24, 29.5f, 18, 2.2f, new Color(0, 0, 0, 60)); // soya

            // Ot oyoqlari (yurish)
            float[] legX = { 28f, 30.5f, 35.5f, 38f };
            for (int i = 0; i < legX.Length; i++)
            {
                float off = frame == 1 || frame == 3 ? (i % 2 == 0 ? 1.4f : -1.4f) : frame == 2 ? (i % 2 == 0 ? -1.4f : 1.4f) : 0f;
                c.FillRect((int)MathF.Round(legX[i] + off), 19, 2, 9, horseD);
            }
            // Ot tanasi, dumi, bo'yni, boshi, yoli
            c.Line(new Vector2(26.5f, 13), new Vector2(25, 21), mane, 2);
            c.FillEllipse(33, 16, 7.5f, 4.4f, horse, 0.05f);
            c.FillPolygon(horse, 0f, new Vector2(37, 14), new Vector2(40.5f, 6.5f), new Vector2(43, 8), new Vector2(40, 16));
            c.FillEllipse(42, 8.5f, 3.2f, 2.3f, horse);
            c.FillRect(37, 7, 3, 7, mane);
            c.FillRect(43, 8, 1, 1, Ink); // ko'z

            // Shotilar (aravani otga bog'laydi)
            c.Line(new Vector2(20, 18), new Vector2(29, 16), woodD, 1);

            // Arava kuzovi
            c.FillRect(2, 13, 19, 7, wood);
            c.FillRect(2, 13, 19, 2, Canvas.Shade(wood, 1.15f));
            c.FillRect(2, 19, 19, 1, woodD);

            // Yuk (qoplar)
            if (loaded)
            {
                Color sack = new Color(222, 200, 150), sackD = new Color(184, 160, 112);
                c.FillEllipse(6, 11, 3.6f, 3f, sack);
                c.FillEllipse(11, 10.5f, 3.6f, 3.2f, sackD);
                c.FillEllipse(8.5f, 7.5f, 3.2f, 2.8f, sack);
                c.FillRect(8, 5, 1, 2, woodD);
            }

            // Aravakash (o'rindiqda)
            c.FillRect(15, 7, 5, 6, new Color(52, 98, 120));   // to'n
            c.FillEllipse(17.5f, 5, 2.3f, 2.3f, new Color(220, 176, 130)); // yuz
            c.FillRect(15, 2, 5, 2, new Color(40, 40, 46));    // do'ppi
            c.Line(new Vector2(20, 9), new Vector2(29, 13), woodD, 1); // jilov

            // G'ildirak (parraklar aylanadi)
            c.FillEllipse(11, 23, 6, 6, woodD);
            c.FillEllipse(11, 23, 4.4f, 4.4f, wood);
            float a = frame * MathF.PI / 4f;
            for (int s = 0; s < 4; s++)
            {
                float ang = a + s * MathF.PI / 4f;
                var d = new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * 4.2f;
                c.Line(new Vector2(11, 23) - d, new Vector2(11, 23) + d, woodD, 1);
            }
            c.FillEllipse(11, 23, 1.3f, 1.3f, Ink);

            c.Outline(Ink, 0.4f);
            return c;
        }
    }
}
