using AndijonSaltanatlar.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace AndijonSaltanatlar.World
{
    public enum DecorationType
    {
        TreePoplar, TreeChinor, TreeApricot, TreeCypress, Bush, Flowers,
        Stall, Hovuz, Well, Brazier, Banner, Crates, Dummy, WeaponRack, Tandir, Sori, Bell
    }

    /// <summary>
    /// Shahar muhiti bezagi: daraxt, buta, rasta, hovuz va h.k. Bino emas, lekin tile egallaydi.
    /// </summary>
    public sealed class Decoration : IWorldObject
    {
        public int Id { get; set; }
        public DecorationType Type { get; }
        public string Name { get; }
        public string Description { get; }
        public Point Position { get; }
        public int Size { get; }
        public string SpriteKey { get; }
        /// <summary>Yurishga to'sqinlik qiladimi.</summary>
        public bool BlocksMovement { get; }
        /// <summary>Qurilish paytida avtomatik olib tashlanadimi (daraxt, buta, gul).</summary>
        public bool Removable { get; }

        public Texture2D Texture { get; set; }
        public Vector2 DrawPosition { get; set; }
        public bool Flipped { get; set; }
        public float Depth => Position.X + Position.Y + Size;
        public int SortLayer => 0;

        public Decoration(DecorationType type, Point position, int variant = 0)
        {
            Type = type;
            Position = position;
            Size = 1;
            BlocksMovement = true;
            switch (type)
            {
                case DecorationType.TreePoplar:
                    Name = "Terak"; SpriteKey = SpriteKeys.TreePoplar; Removable = true;
                    Description = "Ariq bo‘yida o‘sadigan baland terak."; break;
                case DecorationType.TreeChinor:
                    Name = "Chinor"; SpriteKey = SpriteKeys.TreeChinor; Removable = true;
                    Description = "Qalin soya beradigan ko‘p yillik chinor."; break;
                case DecorationType.TreeApricot:
                    Name = "O‘rik daraxti"; SpriteKey = SpriteKeys.TreeApricot; Removable = true;
                    Description = "Bahorda oppoq gullaydigan o‘rik."; break;
                case DecorationType.TreeCypress:
                    Name = "Archa"; SpriteKey = SpriteKeys.TreeCypress; Removable = true;
                    Description = "Doim yashil archa."; break;
                case DecorationType.Bush:
                    Name = "Buta"; SpriteKey = SpriteKeys.Bush; Removable = true; BlocksMovement = false;
                    Description = "Dekorativ buta."; break;
                case DecorationType.Flowers:
                    Name = "Gullar"; SpriteKey = SpriteKeys.Flowers; Removable = true; BlocksMovement = false;
                    Description = "Lola va rayhonlar."; break;
                case DecorationType.Stall:
                    Name = "Rasta"; SpriteKey = SpriteKeys.Stall + "_" + (variant % SpriteKeys.StallVariants);
                    Description = "Bozor rastasi: qovun, anor, non va matolar sotiladi."; break;
                case DecorationType.Hovuz:
                    Name = "Hovuz"; SpriteKey = SpriteKeys.Hovuz; Size = 2;
                    Description = "Markaziy maydondagi favvorali hovuz."; break;
                case DecorationType.Well:
                    Name = "Quduq"; SpriteKey = SpriteKeys.Well;
                    Description = "Mahalla qudug‘i."; break;
                case DecorationType.Brazier:
                    Name = "Mash‘ala"; SpriteKey = SpriteKeys.Brazier;
                    Description = "Qorovullar tunda yoqadigan mash‘ala."; break;
                case DecorationType.Banner:
                    Name = "Bayroq"; SpriteKey = SpriteKeys.Banner;
                    Description = "Saltanat bayrog‘i."; break;
                case DecorationType.Crates:
                    Name = "Yuklar"; SpriteKey = SpriteKeys.Crates;
                    Description = "Karvon olib kelgan sandiq va qoplar."; break;
                case DecorationType.Dummy:
                    Name = "Mashq qo‘g‘irchog‘i"; SpriteKey = SpriteKeys.Dummy;
                    Description = "Jangchilar qilichbozlikni shu yerda mashq qiladi."; break;
                case DecorationType.WeaponRack:
                    Name = "Qurol tokchasi"; SpriteKey = SpriteKeys.WeaponRack;
                    Description = "Nayza va qalqonlar saqlanadigan tokcha."; break;
                case DecorationType.Tandir:
                    Name = "Tandir"; SpriteKey = SpriteKeys.Tandir;
                    Description = "Issiq non yopiladigan tandir."; break;
                case DecorationType.Sori:
                    Name = "So‘ri"; SpriteKey = SpriteKeys.Sori;
                    Description = "Choyxona so‘risi — ko‘k choy va suhbat joyi."; break;
                case DecorationType.Bell:
                    Name = "Ogohlantirish qo‘ng‘irog‘i"; SpriteKey = SpriteKeys.Bell; BlocksMovement = false;
                    Description = "Shahar markazidagi qo‘ng‘iroq — chalinsa, xalq uylariga kiradi."; break;
            }
        }
    }
}
