using System;
using AndijonSaltanatlar.Core;
using Microsoft.Xna.Framework;

namespace AndijonSaltanatlar.Buildings
{
    public enum FarmCropStage
    {
        Empty,
        Growing,
        Mature
    }

    public sealed class Farm : Building
    {
        public const int BuildCost = 180;
        public const int CropGrowthDays = 3;
        public const int CropYield = 12;

        public FarmCropStage CropStage { get; private set; }
        public int GrowthDays { get; private set; }
        public int StoredProduce { get; private set; }
        public bool IsPlayerControlled { get; set; }

        /// <summary>Dalaga o'z dehqoni biriktirilgan: ekish, o'rish va bozorga olib borishni dehqon o'zi qiladi.</summary>
        public bool Tended { get; set; }

        /// <summary>Ekish jarayoni (0..1): dehqon jo'yaklar bo'ylab yurib qator-qator ekadi. 1 ga yetganda Growing bo'ladi.</summary>
        public float PlantProgress { get; private set; }
        public bool IsPlanting => CropStage == FarmCropStage.Empty && PlantProgress > 0f && PlantProgress < 1f;

        /// <summary>Ekishni bir oz davom ettiradi (qator-qator). To'lgach ekin o'sa boshlaydi.</summary>
        public void AdvancePlant(float amount)
        {
            if (CropStage != FarmCropStage.Empty || amount <= 0f) return;
            PlantProgress = Math.Min(1f, PlantProgress + amount);
            if (PlantProgress >= 1f) { PlantCrop(); }
        }

        /// <summary>Dehqon parvarishida o'sish (o'yin soati bo'yicha): shuncha soatda pishadi.</summary>
        public const float TendedGrowHours = 8f;
        private float _growth;

        /// <summary>Ekin o'sishi 0..1 (ko'rinish uchun: nihol -> yosh -> yashil -> oltin).</summary>
        public float Growth => CropStage == FarmCropStage.Mature ? 1f
            : CropStage == FarmCropStage.Growing ? Math.Max(_growth, GrowthDays / (float)CropGrowthDays) : 0f;

        /// <summary>Parvarishdagi dalada ekinni o'stiradi (ulush). Pishsa Mature bo'ladi.</summary>
        public void AdvanceGrowth(float amount)
        {
            if (CropStage != FarmCropStage.Growing || amount <= 0f) return;
            _growth = Math.Max(_growth, GrowthDays / (float)CropGrowthDays) + amount;
            GrowthDays = Math.Min(CropGrowthDays - 1, (int)(_growth * CropGrowthDays));
            if (_growth >= 1f) { _growth = 1f; CropStage = FarmCropStage.Mature; GrowthDays = 0; }
        }

        public Farm(Point position) : base(position)
        {
            Type = BuildingType.Farm;
            Name = "Ferma";
            Description = "Dehqonlar ekin ekib, oziq-ovqat yetishtiradigan xo'jalik.";
            Size = new Point(4, 4);
            Category = BuildingCategory.Nature;
            Cost = BuildCost;
            SetHitPoints(220);
            SpriteKey = SpriteKeys.Farm;
        }

        public bool PlantCrop()
        {
            if (CropStage != FarmCropStage.Empty) return false;
            CropStage = FarmCropStage.Growing;
            GrowthDays = 0;
            _growth = 0f;
            return true;
        }

        public int HarvestCrop()
        {
            if (CropStage != FarmCropStage.Mature) return 0;
            CropStage = FarmCropStage.Empty;
            GrowthDays = 0;
            _growth = 0f;
            PlantProgress = 0f; // keyingi mavsum qator-qator qaytadan ekiladi
            StoredProduce += CropYield;
            return CropYield;
        }

        public int WorkAutonomously(bool hasFarmer)
        {
            if (!hasFarmer) return 0;
            if (IsPlayerControlled && CropStage != FarmCropStage.Growing) return 0;
            switch (CropStage)
            {
                case FarmCropStage.Empty:
                    PlantCrop();
                    return 0;
                case FarmCropStage.Growing:
                    GrowthDays++;
                    if (GrowthDays >= CropGrowthDays) CropStage = FarmCropStage.Mature;
                    return 0;
                case FarmCropStage.Mature:
                    return HarvestCrop();
                default:
                    return 0;
            }
        }

        public int SellStoredProduce()
        {
            int amount = StoredProduce;
            StoredProduce = 0;
            return amount;
        }

        public void RestoreCropState(int stage, int growthDays, int storedProduce)
        {
            CropStage = (FarmCropStage)Math.Clamp(stage, (int)FarmCropStage.Empty, (int)FarmCropStage.Mature);
            GrowthDays = CropStage == FarmCropStage.Growing
                ? Math.Clamp(growthDays, 0, CropGrowthDays - 1)
                : 0;
            StoredProduce = Math.Max(0, storedProduce);
        }
    }
}
