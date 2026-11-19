using AndijonSaltanatlar.Core;
using Microsoft.Xna.Framework;

namespace AndijonSaltanatlar.Units
{
    /// <summary>
    /// Shoh — odatda saroy ichida taxtda o'tiradi (HouseInterior). O'yinchi saroyda shoh yonida F bossa,
    /// shoh shaharga chiqadi: shu unit dunyoga qo'shiladi, o'yinchi uni boshqaradi, soqchilar hamrohlik qiladi.
    /// Boshqaruv qo'yib yuborilsa, shoh saroy eshigiga qaytadi va dunyodan olinadi (yana taxtga o'tiradi).
    /// Shahar qo'shini hisobiga kirmaydi. O'yinchi shohining ismi va qiyofasi ShahLook'dan olinadi.
    /// Qamalda dushman shohi ham shu sinf (Team = Enemy).
    /// </summary>
    public sealed class ShahUnit : Unit
    {
        public ShahUnit(Vector2 position) : base(position)
        {
            Name = "Shoh " + ShahName;
            Description = "Saltanat hukmdori. Zarbof to'n kiygan, boshida oq salla, qo'lida kitob.";
            SpriteKey = SpriteKeys.UnitShah;
            Team = UnitTeam.Player;
            ExternallyControlled = true;
            Attack = 12;
            Speed = 1.2f;
            GaitScale = 0.8f; // shoh — viqor bilan, shoshmay yuradi
            BodyScale = ShahLook.Scale;
            SetHitPoints(220);
        }

        /// <summary>Dushman shohi (qamal): o'z ismi bilan, saroyini himoya qiladi.</summary>
        public ShahUnit(Vector2 position, string name, int hitPoints) : base(position)
        {
            Name = "Shoh " + name;
            Description = "Dushman hukmdori. Uni yengsangiz, saroy va hudud sizniki bo'ladi.";
            SpriteKey = SpriteKeys.UnitShah;
            Team = UnitTeam.Enemy;
            ExternallyControlled = true;
            Attack = 14;
            Speed = 1.2f;
            GaitScale = 0.8f;
            BodyScale = new Vector2(1.08f, 1.04f);
            SetHitPoints(hitPoints);
        }

        /// <summary>O'yinchi shohining ismi (birinchi kirishda tanlanadi).</summary>
        public static string ShahName => ShahLook.Name;

        protected override void UpdateBehavior(float dt)
        {
            State = UnitState.Idle;
        }
    }
}
