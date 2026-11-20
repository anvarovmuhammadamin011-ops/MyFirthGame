using System;
using AndijonSaltanatlar.Units;
using Microsoft.Xna.Framework;
using Xunit;

namespace AndijonSaltanatlar.Tests
{
    /// <summary>Jang animatsiyasi: zarba kadrlari, chayqalish va yiqilish.</summary>
    public class CombatAnimationTests
    {
        [Fact]
        public void HitReaction_LeansAwayThenRecovers()
        {
            var u = new RaiderUnit(new Vector2(5, 5), false);
            u.TakeHitVisual(new Vector2(1, 0));
            Assert.True(u.FlashTimer > 0f && u.HitReactTimer > 0f && u.HurtTimer > 0f);
            u.HitReactTimer = Unit.HitReactTime * 0.75f;     // erta bosqich: orqaga og'gan
            u.GetHitPose(out Vector2 off, out float rot);
            Assert.True(off.X > 0f && rot > 0f);
            u.HitReactTimer = 0f;                               // tugadi: joyida
            u.GetHitPose(out off, out rot);
            Assert.Equal(Vector2.Zero, off);
            Assert.Equal(0f, rot);
        }

        [Fact]
        public void Fall_RotatesTo90Degrees_WithSmallBounce()
        {
            var u = new RaiderUnit(new Vector2(5, 5), false);
            u.HitPoints = 0;
            float prev = 0f, max = 0f;
            for (int i = 0; i < 40; i++)
            {
                u.Update(0.016f);
                float a = u.FallAngle;
                Assert.InRange(a, 0f, MathHelper.PiOver2 + 0.0001f);
                max = Math.Max(max, a);
                prev = a;
            }
            Assert.Equal(MathHelper.PiOver2, prev, 3);          // oxirida to'liq yotadi
            u.Revive(1f);
            u.Update(0.016f);
            Assert.Equal(0f, u.FallAngle);                      // o'rnidan turdi
        }

        [Fact]
        public void Facing_NearBoundary_DoesNotFlicker()
        {
            var u = new RaiderUnit(new Vector2(5, 5), false);
            u.SetFacing(new Vector2(1, 0));
            Assert.Equal(Direction.East, u.Direction);
            // East/South chegarasi (45°) atrofida tebranish (jangda nishonga qarash): sprayt chap-o'ng ag'darilmaydi
            for (int i = 0; i < 20; i++)
            {
                float a = MathF.PI / 4f + (i % 2 == 0 ? 0.12f : -0.12f);
                u.SetFacing(new Vector2(MathF.Cos(a), MathF.Sin(a)));
                Assert.Equal(Direction.East, u.Direction);
            }
            // Chegaradan aniq o'tsa — buriladi
            float b = MathF.PI / 4f + 0.45f;
            u.SetFacing(new Vector2(MathF.Cos(b), MathF.Sin(b)));
            Assert.Equal(Direction.South, u.Direction);
            // Teskari tomonga — darhol
            u.SetFacing(new Vector2(0, -1));
            Assert.Equal(Direction.North, u.Direction);
        }

        [Fact]
        public void Walking_StepsOnlyWhileActuallyMoving()
        {
            var u = new RaiderUnit(new Vector2(5, 5), false) { ExternallyControlled = true };
            u.DriveMove(new Vector2(1, 0), 2f);
            u.Update(0.016f);
            float p0 = u.WalkPhase;
            float step = u.WalkCycleTiles / 10f;
            for (int i = 0; i < 10; i++) { u.Position += new Vector2(step, 0); u.Update(0.016f); }
            Assert.True(u.IsStepping);
            Assert.Equal(p0 + 1f, u.WalkPhase, 3);               // bitta sikl masofasi = bitta to'liq sikl (ikki qadam)
            // Yo'l to'silgan: holat "yurish", lekin siljimaydi — qadam to'xtaydi (oyoq qotmaydi, tana joyida sakramaydi)
            for (int i = 0; i < 15; i++) { u.DriveMove(new Vector2(1, 0), 2f); u.Update(0.016f); }
            Assert.Equal(UnitState.Walking, u.State);
            Assert.False(u.IsStepping);
        }
    }
}
