using AndijonSaltanatlar.Units;
using Microsoft.Xna.Framework;

namespace AndijonSaltanatlar.Military
{
    /// <summary>
    /// Qo'shin turi (UnitType) → sahnadagi jangchi: klass (xatti-harakat), ko'rinish (sprayt oilasi), zirh, masofa va qism.
    /// Qamal (SiegeMode), parad (MarchOutParade) va boshqa joylar shu bitta jadvaldan foydalanadi — yangi tur bir joyda qo'shiladi.
    /// </summary>
    public static class UnitLooks
    {
        /// <summary>Saf qismi: "otliq", "piyoda", "nayza", "kamon".</summary>
        public static string GroupOf(UnitType t)
        {
            UnitDef d = UnitCatalog.Get(t);
            if (d.Role == UnitRole.Otliq) return "otliq";
            if (d.Role == UnitRole.Masofaviy) return "kamon";
            return t == UnitType.Nayzador || t == UnitType.SaroyQoriqchisi || t == UnitType.Qoriqchi ? "nayza" : "piyoda";
        }

        /// <summary>Masofaviy jangchining o'q yetadigan masofasi (tile).</summary>
        public static float ReachOf(UnitType t) => t switch
        {
            UnitType.Zambarakchi => 7.5f,   // dala to'pi: eng uzoq
            UnitType.Tufangchi => 6.5f,
            UnitType.Mergan => 5.5f,
            UnitType.Razvedkachi => 4f,
            _ => 4.5f
        };

        /// <summary>Sahna jangchisi: o'z klassi, kiyimi, zirhi va (kamonchi/tufangchi bo'lsa) masofasi bilan.</summary>
        public static Unit Create(UnitType t, Vector2 pos, Direction facing = Direction.North)
        {
            UnitDef d = UnitCatalog.Get(t);
            Vector2[] here = { pos };
            Unit u = t switch
            {
                UnitType.Otliq or UnitType.ElitaOtliq or UnitType.Razvedkachi => new CavalryUnit(here),
                UnitType.Nayzador or UnitType.SaroyQoriqchisi or UnitType.Qoriqchi => new SpearmanUnit(pos, facing),
                UnitType.Qalqonchi or UnitType.OgirZirhli => new GuardUnit(pos, facing),
                UnitType.Qilichboz or UnitType.Boltador => new SwordsmanUnit(here),
                _ => new PatrolUnit(here),
            };
            u.SetLook(d.IconKey);
            u.Armor = d.Armor;
            u.LineType = t;
            if (d.Role == UnitRole.Masofaviy) u.MakeArcher(ReachOf(t));
            return u;
        }

        /// <summary>Sahnadagi jangchidan qo'shin turi (asir askarlar qo'shinga qo'shilganda).</summary>
        public static UnitType TypeOf(Unit u)
        {
            if (u.LineType.HasValue) return u.LineType.Value;
            return u switch
            {
                CavalryUnit _ => UnitType.Otliq,
                Unit a when a.IsArcher => UnitType.Kamonchi,
                SpearmanUnit _ => UnitType.Nayzador,
                GuardUnit _ => UnitType.Qalqonchi,
                SwordsmanUnit _ => UnitType.Qilichboz,
                _ => UnitType.Piyoda
            };
        }

        /// <summary>
        /// Jangdagi haqiqiy zarar: nayzachi otliqqa 1.5 barobar kuchli uradi (tarixiy "otliqqa qarshi nayza"),
        /// so'ng nishonning zirhi zarbaning Armor% ini yutadi. Kamida 1.
        /// </summary>
        public static int Damage(Unit attacker, Unit victim, int dmg)
        {
            float f = dmg;
            if (attacker is SpearmanUnit && victim is CavalryUnit) f *= 1.5f;
            int armor = System.Math.Clamp(victim.Armor, 0, 60);
            if (attacker.LineType == UnitType.Boltador) armor /= 2;   // jangovar bolta zirhning yarmini yorib o'tadi
            if (armor > 0) f *= (100 - armor) / 100f;
            return System.Math.Max(1, (int)System.MathF.Round(f));
        }
    }
}
