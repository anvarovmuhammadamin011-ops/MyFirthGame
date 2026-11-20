using AndijonSaltanatlar.Buildings;
using System;
using System.Collections.Generic;
using AndijonSaltanatlar.Core;
using AndijonSaltanatlar.Core.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace AndijonSaltanatlar.Units
{
    /// <summary>Barcha jangchilarni saqlaydi, yangilaydi va sichqoncha ostidagisini topadi.</summary>
    public sealed class UnitManager
    {
        private readonly AssetManager _assets;
        private int _nextId = 1;

        public List<Unit> Units { get; } = new List<Unit>();

        public UnitManager(AssetManager assets)
        {
            _assets = assets;
        }

        public T Add<T>(T unit) where T : Unit
        {
            unit.Id = _nextId++;
            LoadFrames(unit.SpriteKey, out var frames, out var back);
            unit.SetFrames(frames);
            unit.BackFrames = back;
            unit.WorkFrames = LoadSet(unit.SpriteKey + "_h", 4);
            unit.BackWorkFrames = LoadSet(unit.SpriteKey + "_bh", 4);
            var attack = new Texture2D[UnitArt.AttackKinds * UnitArt.AttackPhases];
            bool any = false;
            for (int i = 0; i < attack.Length; i++)
            {
                attack[i] = _assets.TryGet(AssetManager.AttackKey(unit.SpriteKey, i / UnitArt.AttackPhases, i % UnitArt.AttackPhases));
                any |= attack[i] != null;
            }
            unit.AttackFrames = any ? attack : null;
            Units.Add(unit);
            return unit;
        }

        /// <summary>Jangchini xos navkarga aylantiradi (ko'rinishi va zarba kadrlari bilan).</summary>
        public void MakeElite(Unit unit)
        {
            if (unit == null || unit.IsElite) return;
            LoadFrames(SpriteKeys.UnitElite, out var frames, out var eliteBack);
            var attack = new Texture2D[UnitArt.AttackKinds * UnitArt.AttackPhases];
            for (int i = 0; i < attack.Length; i++)
                attack[i] = _assets.TryGet(AssetManager.AttackKey(SpriteKeys.UnitElite, i / UnitArt.AttackPhases, i % UnitArt.AttackPhases));
            unit.MakeElite(frames, attack);
            unit.BackFrames = eliteBack;
        }

        public const int WalkFrames = 8;

        /// <summary>prefix0..prefix{n-1} — hammasi bo'lsa qaytadi, aks holda null.</summary>
        private Texture2D[] LoadSet(string prefix, int n)
        {
            var a = new Texture2D[n];
            for (int i = 0; i < n; i++) { a[i] = _assets.TryGet(prefix + i); if (a[i] == null) return null; }
            return a;
        }

        /// <summary>
        /// Kadrlar: [turish, yurish...]. 3D modeldan 8 kadrli yurish sikli (key_w0..w7, orqa: key_bw0..bw7) bo'lsa — shu,
        /// aks holda eski 3 kadr. Orqa kadrlar old kadrlar bilan bir xil sonda bo'lmasa null.
        /// </summary>
        public void LoadFrames(string key, out Texture2D[] frames, out Texture2D[] back)
        {
            _assets.EnsureFamily(key);   // yangi kasb/askar: 3D-render kadrlaridan birinchi marta yasaladi
            if (_assets.TryGet(key + "_w0") != null && _assets.TryGet(key + "_w" + (WalkFrames - 1)) != null)
            {
                frames = new Texture2D[1 + WalkFrames];
                frames[0] = _assets.Get(key + "_0");
                for (int i = 0; i < WalkFrames; i++) frames[1 + i] = _assets.TryGet(key + "_w" + i) ?? frames[0];
                back = null;
                Texture2D b0 = _assets.TryGet(key + "_b0");
                if (b0 != null && _assets.TryGet(key + "_bw0") != null)
                {
                    back = new Texture2D[1 + WalkFrames];
                    back[0] = b0;
                    for (int i = 0; i < WalkFrames; i++) back[1 + i] = _assets.TryGet(key + "_bw" + i) ?? b0;
                }
                return;
            }
            frames = new Texture2D[SpriteKeys.UnitFrames];
            for (int i = 0; i < frames.Length; i++) frames[i] = _assets.Get(key + "_" + i);
            back = LoadBack(key);
        }

        /// <summary>Orqa tomon kadrlari (key_b0..b2) — hammasi bo'lsa qaytadi, aks holda null.</summary>
        private Texture2D[] LoadBack(string key)
        {
            var back = new Texture2D[SpriteKeys.UnitFrames];
            for (int i = 0; i < back.Length; i++)
            {
                back[i] = _assets.TryGet(key + "_b" + i);
                if (back[i] == null) return null;
            }
            return back;
        }

        /// <summary>Unitni dunyodan olib tashlaydi (faqat vaqtinchalik unitlar — masalan bosqinchilar — uchun).</summary>
        public bool Remove(Unit unit) => Units.Remove(unit);

        public void Update(float dt)
        {
            foreach (var u in Units) u.Update(dt);
        }

        private readonly Dictionary<Unit, Vector2> _lastValid = new Dictionary<Unit, Vector2>();
        private readonly Dictionary<long, List<Unit>> _grid = new Dictionary<long, List<Unit>>();

        /// <summary>
        /// Oddiy fizika: odamlar bir-birining ichidan o'tmaydi (yaqin kelsa yumshoq itarishadi) va bino devoridan o'tib
        /// ketmaydi (oxirgi to'g'ri joyiga qaytadi). Ferma ustida yurish mumkin; uy ichidagi/yashirin odamlarga tegmaydi.
        /// </summary>
        public void ApplyPhysics(World.MapManager map, Unit possessed, float dt)
        {
            const float R = 0.32f;              // tana radiusi (tile)
            _grid.Clear();
            foreach (Unit u in Units)
            {
                if (u.IsDown || u.Hidden || (u is CitizenUnit c && c.IsInsideBuilding)) continue;
                long key = ((long)(int)MathF.Floor(u.Position.X) << 32) ^ (uint)(int)MathF.Floor(u.Position.Y);
                if (!_grid.TryGetValue(key, out var l)) _grid[key] = l = new List<Unit>(4);
                l.Add(u);
            }
            foreach (Unit a in Units)
            {
                if (a.IsDown || a.Hidden || (a is CitizenUnit ca && ca.IsInsideBuilding)) continue;
                int tx = (int)MathF.Floor(a.Position.X), ty = (int)MathF.Floor(a.Position.Y);
                Vector2 push = Vector2.Zero;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        long key = ((long)(tx + dx) << 32) ^ (uint)(ty + dy);
                        if (!_grid.TryGetValue(key, out var l)) continue;
                        foreach (Unit b in l)
                        {
                            if (b == a) continue;
                            Vector2 d = a.Position - b.Position;
                            float dist = d.Length();
                            if (dist >= R * 2f) continue;
                            if (dist < 0.001f) d = new Vector2(((a.Id * 7) % 5) - 2f, ((a.Id * 3) % 5) - 2f) * 0.01f + new Vector2(0.01f, 0f);
                            else d /= dist;
                            push += d * (R * 2f - dist) * 0.5f;
                        }
                    }
                // Boshqarilayotgan odamni o'zgalar kamroq suradi (o'yinchi o'zi yo'l topadi)
                if (a == possessed) push *= 0.4f;
                if (push != Vector2.Zero)
                {
                    float max = 1.6f * MathF.Max(dt, 0.001f);
                    if (push.Length() > max) push = Vector2.Normalize(push) * max;
                    Vector2 np = a.Position + push;
                    if (map.IsPassable((int)MathF.Floor(np.X), (int)MathF.Floor(np.Y)) || OnFarm(map, np)) a.Position = np;
                }
                // Bino ichiga kirib qolgan bo'lsa — oxirgi to'g'ri joyiga qaytadi
                int px = (int)MathF.Floor(a.Position.X), py = (int)MathF.Floor(a.Position.Y);
                bool ok = map.IsPassable(px, py) || OnFarm(map, a.Position);
                if (ok) _lastValid[a] = a.Position;
                else if (_lastValid.TryGetValue(a, out Vector2 back)) a.Position = back;
            }
        }

        private static bool OnFarm(World.MapManager map, Vector2 p) =>
            map.GetOccupant((int)MathF.Floor(p.X), (int)MathF.Floor(p.Y)) is Building b && b.Type == BuildingType.Farm;

        /// <summary>World nuqtasi ostidagi unit (eng oldinda turgani).</summary>
        public Unit GetAt(Vector2 world)
        {
            Unit best = null;
            foreach (var u in Units)
            {
                if (u.Hidden) continue;
                Vector2 feet = IsoMath.TileToWorld(u.Position);
                if (MathF.Abs(world.X - feet.X) > 11f) continue;
                if (world.Y > feet.Y + 5f || world.Y < feet.Y - 48f) continue;
                if (best == null || u.Depth > best.Depth) best = u;
            }
            return best;
        }

        /// <summary>Berilgan tile to'rtburchagi ichida unit bormi (qurilishni tekshirish uchun).</summary>
        public bool AnyInArea(Point pos, int size)
        {
            foreach (var u in Units)
            {
                if (u.Position.X >= pos.X && u.Position.X < pos.X + size &&
                    u.Position.Y >= pos.Y && u.Position.Y < pos.Y + size) return true;
            }
            return false;
        }
    }
}
