using System;
using System.Collections.Generic;
using AndijonSaltanatlar.Buildings;
using Microsoft.Xna.Framework;

namespace AndijonSaltanatlar.World
{
    /// <summary>
    /// Shaharni bezash: bino atrofiga rang-barang, tasodifiy bezaklar (gul, buta, daraxt, tandir, so'ri)
    /// qo'yadi — har safar har xil. Uy/bog' qurilganda avtomatik, yoki "BEZASH" tugmasi bilan butun shahar.
    /// Asosan olib tashlanadigan (keyin ustiga qurish mumkin) va yurishga xalaqit bermaydigan bezaklar.
    /// </summary>
    public static class CityBeautifier
    {
        // Og'irlik uchun takroriy elementlar: gul va buta ko'proq, daraxt o'rtacha, tandir/so'ri kam (urg'u).
        private static readonly DecorationType[] HousePalette =
        {
            DecorationType.Flowers, DecorationType.Flowers, DecorationType.Flowers,
            DecorationType.Bush, DecorationType.Bush,
            DecorationType.TreeApricot, DecorationType.TreePoplar,
            DecorationType.Tandir, DecorationType.Sori,
        };
        private static readonly DecorationType[] GardenPalette =
        {
            DecorationType.Flowers, DecorationType.Flowers, DecorationType.Flowers,
            DecorationType.Bush, DecorationType.Bush,
            DecorationType.TreeApricot, DecorationType.TreeApricot, DecorationType.TreeChinor,
        };
        private static readonly DecorationType[] GeneralPalette =
        {
            DecorationType.Flowers, DecorationType.Flowers,
            DecorationType.Bush, DecorationType.Bush,
            DecorationType.TreeChinor, DecorationType.TreePoplar,
        };

        private static DecorationType[] PaletteFor(BuildingType type) =>
            type == BuildingType.Garden ? GardenPalette :
            type == BuildingType.House ? HousePalette : GeneralPalette;

        /// <summary>Bino footprint'i atrofidagi bo'sh kataklarga tasodifiy bezak qo'yadi. Qo'yilgan son qaytadi.</summary>
        public static int DecorateAround(MapManager map, Point origin, int size, BuildingType type, Random rng, int max = 4)
        {
            if (map == null) return 0;
            DecorationType[] palette = PaletteFor(type);
            var ring = new List<Point>();
            for (int x = origin.X - 1; x <= origin.X + size; x++)
            {
                ring.Add(new Point(x, origin.Y - 1));
                ring.Add(new Point(x, origin.Y + size));
            }
            for (int y = origin.Y; y < origin.Y + size; y++)
            {
                ring.Add(new Point(origin.X - 1, y));
                ring.Add(new Point(origin.X + size, y));
            }
            Shuffle(ring, rng);

            int placed = 0;
            int accents = 0; // tandir/so'ri (olib tashlanmaydi) — ko'pi bilan 1 ta
            foreach (Point p in ring)
            {
                if (placed >= max) break;
                if (rng.NextDouble() > 0.5) continue; // har katakka emas — tabiiy tarqoq
                DecorationType t = palette[rng.Next(palette.Length)];
                bool accent = t == DecorationType.Tandir || t == DecorationType.Sori;
                if (accent && accents >= 1) t = DecorationType.Flowers; // urg'uni cheklaymiz
                if (map.AddDecoration(t, p.X, p.Y, rng.Next(4)) != null)
                {
                    placed++;
                    if (accent) accents++;
                }
            }
            return placed;
        }

        /// <summary>Butun shaharni bezaydi: har bino atrofiga oz-ozdan rang-barang bezak. Qo'yilgan umumiy son.</summary>
        public static int Beautify(MapManager map, BuildingManager buildings, Random rng)
        {
            if (map == null || buildings == null) return 0;
            int total = 0;
            foreach (Building b in buildings.Buildings)
            {
                if (b == null) continue;
                // Mudofaa inshootlari atrofi bezatilmaydi (devor/darvoza/minora toza qolsin).
                if (b.Type == BuildingType.Wall || b.Type == BuildingType.Gate || b.Type == BuildingType.Tower) continue;
                total += DecorateAround(map, b.Position, b.Size.X, b.Type, rng, 3);
            }
            return total;
        }

        private static void Shuffle<T>(IList<T> list, Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
