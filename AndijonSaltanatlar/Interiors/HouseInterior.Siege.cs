using System;
using System.Collections.Generic;
using AndijonSaltanatlar.Buildings;
using AndijonSaltanatlar.Core;
using AndijonSaltanatlar.Core.Graphics;
using AndijonSaltanatlar.Units;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace AndijonSaltanatlar.Interiors
{
    /// <summary>Qamaldagi saroy himoyachisi (shoh, vazir, soqchi). Jon miqdori saroyga qayta kirilganda ham saqlanadi.</summary>
    public sealed class SiegeFoe
    {
        public string Name = "";
        public string Short = "";
        public int Hp, MaxHp, Attack;
        public bool IsShah, IsVizier;
        public bool Down => Hp <= 0;
    }

    /// <summary>
    /// QAMAL: dushman saroyi ichidagi jang. Taxt zalida shoh, vazir va saroy soqchilari — ular sizga hujum qiladi.
    /// SPACE / sichqoncha — zarba. Shoh va vazir yiqilgach, eshikdan chiqsangiz saroy olinadi.
    /// Jangchingiz yiqilsa — saroydan chiqariladi (boshqa jangchi bilan qayta kirish mumkin).
    /// </summary>
    public sealed partial class HouseInterior
    {
        private bool _fight;
        private float _avatarCd;
        private int _combo;
        private readonly List<(Vector2 Tile, string Text, Color Color, float T)> _fightFloats = new List<(Vector2, string, Color, float)>();

        /// <summary>Hozir saroyda qamal jangi ketyaptimi.</summary>
        public bool FightMode => _fight;
        public bool FightShahDown { get; private set; }
        public bool FightVizierDown { get; private set; }
        /// <summary>Saroyga kirgan jangchi yiqildi (u tashqariga chiqarildi).</summary>
        public bool FightAvatarDown { get; private set; }

        /// <summary>Qamal: dushman saroyiga jang bilan kirish. roster — shoh, vazir, soqchilar (HP saqlanadi).</summary>
        public void EnterSiege(Building palace, Unit avatar, IList<SiegeFoe> roster, string shahName)
        {
            Enter(palace, avatar);
            if (!Active) return;
            _fight = true;
            FightAvatarDown = false;
            _avatarCd = 0f;
            _fightFloats.Clear();
            _residents.Clear();
            _title = "DUSHMAN SAROYI";
            _subtitle = "Shoh " + shahName + " va vaziri shu yerda — ularni yenging!";
            Texture2D guard = _assets.Get(SpriteKeys.UnitSpearman + "_0");
            Texture2D vizier = _assets.Get(SpriteKeys.UnitCitizen + CitizenArt.Merchant + "_0");
            Texture2D shah = _assets.Get(SpriteKeys.UnitShah + "_0");
            int guardIdx = 0;
            foreach (SiegeFoe f in roster)
            {
                var r = new Resident { Name = f.Name, Short = f.Short, Foe = f, Info = "", Line = "" };
                if (f.IsShah) { r.Tex = shah; r.IsShah = true; r.Pos = ThroneSeat; r.Lift = f.Hp < f.MaxHp ? 0 : 16; if (r.Lift == 0) r.Pos = ThroneSeat + new Vector2(0, 1.3f); }
                else if (f.IsVizier) { r.Tex = vizier; r.Pos = new Vector2(2.4f, 4.6f); }
                else { r.Tex = guard; r.Pos = guardIdx++ % 2 == 0 ? new Vector2(2.4f, 2.6f) : new Vector2(5.6f, 2.6f); }
                _residents.Add(r);
            }
            UpdateFightFlags();
            _state.ShowMessage("Dushman saroyiga kirdingiz! Shoh va vazirni yenging (SPACE — zarba)", true);
        }

        /// <summary>Qamal tugadi — oddiy rejimga qaytish.</summary>
        public void EndSiege()
        {
            _fight = false;
            FightShahDown = FightVizierDown = FightAvatarDown = false;
        }

        private void UpdateFightFlags()
        {
            bool shah = true, viz = true;
            foreach (Resident r in _residents)
            {
                if (r.Foe == null) continue;
                if (r.Foe.IsShah && !r.Foe.Down) shah = false;
                if (r.Foe.IsVizier && !r.Foe.Down) viz = false;
            }
            FightShahDown = shah;
            FightVizierDown = viz;
        }

        private void UpdateFight(float dt, InputManager input)
        {
            for (int i = _fightFloats.Count - 1; i >= 0; i--)
            {
                var f = _fightFloats[i];
                f.T += dt;
                if (f.T > 1f) _fightFloats.RemoveAt(i); else _fightFloats[i] = f;
            }
            if (_avatarCd > 0f) _avatarCd -= dt;

            // --- Dushmanlar: yaqinlashib uradi ---
            foreach (Resident r in _residents)
            {
                SiegeFoe f = r.Foe;
                if (f == null || f.Down) continue;
                if (r.Hurt > 0f) r.Hurt -= dt;
                if (r.Lunge > 0f) r.Lunge -= dt;
                if (r.Cooldown > 0f) r.Cooldown -= dt;
                float d = Vector2.Distance(r.Pos, _pos);
                if (f.IsShah && r.Lift > 0f)
                {
                    // Shoh taxtdan turadi: yaqinlashsangiz yoki zarba yesa
                    if (d < 3.2f || f.Hp < f.MaxHp) { r.Lift = 0f; r.Pos = ThroneSeat + new Vector2(0, 1.3f); r.ChatText = "Soqchilar! Bosqinchini ushlang!"; r.ChatTime = 2.5f; }
                    else continue;
                }
                if (d > 0.85f)
                {
                    float speed = f.IsShah ? 1.35f : f.IsVizier ? 1.2f : 1.7f;
                    Vector2 dir = (_pos - r.Pos) / d;
                    Vector2 np = r.Pos + dir * speed * dt;
                    if (FoeCanStand(np)) r.Pos = np;
                    else if (FoeCanStand(new Vector2(np.X, r.Pos.Y))) r.Pos = new Vector2(np.X, r.Pos.Y);
                    else if (FoeCanStand(new Vector2(r.Pos.X, np.Y))) r.Pos = new Vector2(r.Pos.X, np.Y);
                }
                else if (r.Cooldown <= 0f && !_avatar.IsDown)
                {
                    r.Cooldown = 1.1f + (float)_brng.NextDouble() * 0.4f;
                    r.Lunge = 0.25f;
                    int dmg = Math.Max(2, f.Attack + _brng.Next(-2, 3));
                    _avatar.HitPoints = Math.Max(0, _avatar.HitPoints - dmg);
                    _avatar.TakeHitVisual(IsoMath.TileToWorld(_pos) - IsoMath.TileToWorld(r.Pos));
                    _fightFloats.Add((_pos, dmg.ToString(), new Color(255, 120, 110), 0f));
                }
            }
            // Dushmanlar bir-birining ustiga chiqmasin
            for (int i = 0; i < _residents.Count; i++)
                for (int j = i + 1; j < _residents.Count; j++)
                {
                    Resident a = _residents[i], b = _residents[j];
                    if (a.Foe == null || b.Foe == null || a.Foe.Down || b.Foe.Down || a.Lift > 0f || b.Lift > 0f) continue;
                    Vector2 dv = b.Pos - a.Pos;
                    float l = dv.Length();
                    if (l >= 0.55f || l < 0.0001f) continue;
                    Vector2 push = dv / l * (0.55f - l) * 0.5f;
                    if (FoeCanStand(a.Pos - push)) a.Pos -= push;
                    if (FoeCanStand(b.Pos + push)) b.Pos += push;
                }

            if (_avatar.IsDown)
            {
                FightAvatarDown = true;
                _state.ShowMessage(_avatar.Name + " saroyda yiqildi! Boshqa jangchi bilan qayta kiring", true);
                Exit();
                return;
            }
            if (input == null) { _avatar.DriveIdle(); return; }

            // --- O'yinchi: zarba ---
            bool attack = input.KeyPressed(Keys.Space) || (input.LeftPressed && !input.MouseOverUI);
            if (attack && _avatarCd <= 0f)
            {
                _avatarCd = 0.45f;
                _combo = (_combo + 1) % 3;
                _avatar.AttackKind = _combo;
                _avatar.AttackTimer = Unit.SwingTime;
                Resident target = null;
                float best = 1.45f;
                foreach (Resident r in _residents)
                {
                    if (r.Foe == null || r.Foe.Down || r.Lift > 0f) continue;
                    float d = Vector2.Distance(r.Pos, _pos);
                    if (d < best) { best = d; target = r; }
                }
                if (target != null)
                {
                    _avatar.SetFacing(target.Pos - _pos);
                    int dmg = (int)(Math.Max(6, _avatar.Attack) * (_combo == 2 ? 2.1f : 1.6f)) + _brng.Next(0, 5);
                    target.Foe.Hp = Math.Max(0, target.Foe.Hp - dmg);
                    target.Hurt = 0.3f;
                    _fightFloats.Add((target.Pos, dmg.ToString(), new Color(255, 236, 120), 0f));
                    Vector2 away = target.Pos - _pos;
                    if (away.LengthSquared() > 0.001f) { away.Normalize(); Vector2 kp = target.Pos + away * 0.25f; if (FoeCanStand(kp)) target.Pos = kp; }
                    if (target.Foe.Down)
                    {
                        target.DownTime = 0f;
                        _fightFloats.Add((target.Pos + new Vector2(-0.3f, -0.3f), target.Foe.IsShah ? "SHOH YIQILDI!" : target.Foe.IsVizier ? "VAZIR YIQILDI!" : "YIQITDINGIZ!", new Color(255, 214, 90), 0f));
                        UpdateFightFlags();
                        if (target.Foe.IsShah || target.Foe.IsVizier)
                            _state.ShowMessage(FightShahDown && FightVizierDown ? "Shoh va vazir yengildi! Endi saroydan chiqing (eshik)" : target.Foe.Name + " yiqildi!");
                    }
                }
            }

            // --- Yurish (devorlar va eshik — oddiy rejimdagidek) ---
            Vector2 screen = Vector2.Zero;
            if (input.KeyDown(Keys.A) || input.KeyDown(Keys.Left)) screen.X -= 1;
            if (input.KeyDown(Keys.D) || input.KeyDown(Keys.Right)) screen.X += 1;
            if (input.KeyDown(Keys.W) || input.KeyDown(Keys.Up)) screen.Y -= 1;
            if (input.KeyDown(Keys.S) || input.KeyDown(Keys.Down)) screen.Y += 1;
            if (screen != Vector2.Zero && dt > 0f)
            {
                screen.Normalize();
                Vector2 dir = IsoMath.WorldToTileF(screen * 32f);
                dir.Normalize();
                float speed = input.KeyDown(Keys.LeftShift) || input.KeyDown(Keys.RightShift) ? RunSpeed : WalkSpeed;
                _avatar.DriveMove(dir, speed);
                Vector2 step = dir * speed * dt;
                if (!TryMove(_pos + step))
                    if (!TryMove(new Vector2(_pos.X + step.X, _pos.Y)))
                        TryMove(new Vector2(_pos.X, _pos.Y + step.Y));
            }
            else _avatar.DriveIdle();

            if (input.KeyPressed(Keys.Escape)) { Exit(); return; }
            Point tile = new Point((int)MathF.Floor(_pos.X), (int)MathF.Floor(_pos.Y));
            bool inDoorTile = tile.Y == N - 1 && tile.X >= _doorX0 && tile.X <= _doorX1;
            if (!inDoorTile) _leftDoor = true;
            else if (_leftDoor && _pos.Y > N - 0.45f) Exit();
        }

        private bool FoeCanStand(Vector2 p)
        {
            if (p.X < 0.3f || p.Y < 0.3f || p.X > N - 0.3f || p.Y > N - 0.4f) return false;
            int tx = (int)MathF.Floor(p.X), ty = (int)MathF.Floor(p.Y);
            return !_blocked[tx, ty];
        }

        /// <summary>Saroy aholisini chizish: jangda — zarba yeganda qizarish, hamla, yiqilib yotish.</summary>
        private void DrawResident(SpriteBatch sb, Resident r, bool left)
        {
            if (r.Foe == null) { DrawPerson(sb, r.Tex, r.Pos, left, r.Scale, r.Lift); return; }
            Texture2D tex = r.Tex;
            Vector2 feet = IsoMath.TileToWorld(r.Pos) + new Vector2(0, -r.Lift);
            float k = r.Scale * AssetManager.ScaleOf(tex);
            var origin = new Vector2(tex.Width / 2f, tex.Height - AssetManager.FootOf(tex));
            SpriteEffects fx = left ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
            if (r.Foe.Down)
            {
                r.DownTime += 1f / 60f;
                float lie = MathHelper.Clamp(r.DownTime / 0.4f, 0f, 1f);
                sb.Draw(tex, new Vector2(MathF.Round(feet.X), MathF.Round(feet.Y)), null, Color.Lerp(Color.White, new Color(150, 140, 135), lie), (left ? -1f : 1f) * MathHelper.PiOver2 * 0.95f * lie, origin, k, fx, 0f);
                return;
            }
            if (r.Lunge > 0f)
            {
                Vector2 to = IsoMath.TileToWorld(_pos) - IsoMath.TileToWorld(r.Pos);
                if (to.LengthSquared() > 0.01f) { to.Normalize(); feet += to * 6f * (r.Lunge / 0.25f); }
            }
            Color tint = r.Hurt > 0f ? Color.Lerp(Color.White, new Color(255, 110, 100), r.Hurt / 0.3f) : Color.White;
            sb.Draw(tex, new Vector2(MathF.Round(feet.X), MathF.Round(feet.Y)), null, tint, 0f, origin, k, fx, 0f);
        }

        /// <summary>Jang qatlami (ekran): HP chiziqlari va zarba raqamlari.</summary>
        private void DrawFightOverlay(SpriteBatch sb, Texture2D px, PixelFont font, Matrix m)
        {
            Vector2 ToScreen(Vector2 tile, float lift) => Vector2.Transform(IsoMath.TileToWorld(tile) + new Vector2(0, -lift), m);
            void Bar(Vector2 c, float f, Color col)
            {
                int w = 44, h = 6;
                sb.Draw(px, new Rectangle((int)c.X - w / 2 - 1, (int)c.Y - 1, w + 2, h + 2), new Color(10, 10, 12, 220));
                sb.Draw(px, new Rectangle((int)c.X - w / 2, (int)c.Y, (int)(w * MathHelper.Clamp(f, 0f, 1f)), h), col);
            }
            foreach (Resident r in _residents)
            {
                if (r.Foe == null || r.Foe.Down) continue;
                Bar(ToScreen(r.Pos, 70 + r.Lift), r.Foe.Hp / (float)Math.Max(1, r.Foe.MaxHp), r.Foe.IsShah ? new Color(255, 170, 40) : new Color(220, 60, 50));
            }
            if (_avatar != null && _avatar.MaxHitPoints > 0)
                Bar(ToScreen(_pos, 70), _avatar.HitPoints / (float)_avatar.MaxHitPoints, new Color(90, 200, 110));
            foreach (var f in _fightFloats)
            {
                Vector2 p = ToScreen(f.Tile, 60 + 30f * f.T);
                float a = f.T < 0.7f ? 1f : 1f - (f.T - 0.7f) / 0.3f;
                Vector2 s = font.Measure(f.Text, 2);
                font.Draw(sb, f.Text, new Vector2(p.X - s.X / 2 + 1, p.Y + 1), Color.Black * (0.7f * a), 2);
                font.Draw(sb, f.Text, new Vector2(p.X - s.X / 2, p.Y), f.Color * a, 2);
            }
        }
    }
}
