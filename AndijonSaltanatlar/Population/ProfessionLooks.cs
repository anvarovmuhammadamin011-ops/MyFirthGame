using AndijonSaltanatlar.Core.Graphics;

namespace AndijonSaltanatlar.Population
{
    /// <summary>Kasbga xos ish harakati (renderer tana pozasini shu bo'yicha qimirlatadi).</summary>
    public enum WorkStyle
    {
        None,
        Hammer,  // hunarmand, quruvchi: bolg'a/tesha urish
        Dig,     // konchi: cho'kich bilan qazish (chuqur egilish)
        Write,   // amaldor, soliqchi, olim, ustoz, me'mor: daftar/kitob ustida bosh qimirlatish
        Trade,   // savdogar, bozor egasi: xaridor bilan gaplashib, yonga chayqalish
        Fish,    // baliqchi: suvga engashib, vaqti-vaqti bilan qarmoqni tortish
        Tend,    // tabib, chorvador: o't/hayvon ustiga egilish
        Carry    // karvonchi, omborchi: qop tashish (yelkada yuk)
    }

    /// <summary>Kasb → ko'rinadigan figura (kiyim turi), ish harakati va gaplari. Yagona jadval.</summary>
    public static class ProfessionLooks
    {
        public static int KindOf(CitizenProfession p) => p switch
        {
            CitizenProfession.Dehqon or CitizenProfession.Bogbon => CitizenArt.Farmer,
            CitizenProfession.Savdogar => CitizenArt.Merchant,
            CitizenProfession.Hunarmand => CitizenArt.Craftsman,
            CitizenProfession.Quruvchi => CitizenArt.Builder,
            CitizenProfession.Tabib => CitizenArt.Healer,
            CitizenProfession.Muallim => CitizenArt.Teacher,
            CitizenProfession.Amaldor => CitizenArt.Official,
            CitizenProfession.Tashuvchi => CitizenArt.Caravaneer,
            CitizenProfession.Chorvador => CitizenArt.Shepherd,
            CitizenProfession.Konchi => CitizenArt.Miner,
            CitizenProfession.Baliqchi => CitizenArt.Fisher,
            CitizenProfession.Omborchi => CitizenArt.Storekeeper,
            CitizenProfession.SoliqYiguvchi => CitizenArt.TaxCollector,
            CitizenProfession.BozorEgasi => CitizenArt.BazaarOwner,
            CitizenProfession.Olim => CitizenArt.Scholar,
            CitizenProfession.Memor => CitizenArt.Architect,
            _ => CitizenArt.Citizen
        };

        /// <summary>Figura turidan kasb (ko'rinadigan figura shu kasbdagi fuqaroga bog'lanadi). Noaniq bo'lsa None.</summary>
        public static CitizenProfession ProfessionOf(int kind) => kind switch
        {
            CitizenArt.Merchant => CitizenProfession.Savdogar,
            CitizenArt.Craftsman => CitizenProfession.Hunarmand,
            CitizenArt.Farmer => CitizenProfession.Dehqon,
            CitizenArt.Shepherd => CitizenProfession.Chorvador,
            CitizenArt.Miner => CitizenProfession.Konchi,
            CitizenArt.Fisher => CitizenProfession.Baliqchi,
            CitizenArt.Storekeeper => CitizenProfession.Omborchi,
            CitizenArt.TaxCollector => CitizenProfession.SoliqYiguvchi,
            CitizenArt.BazaarOwner => CitizenProfession.BozorEgasi,
            CitizenArt.Scholar => CitizenProfession.Olim,
            CitizenArt.Healer => CitizenProfession.Tabib,
            CitizenArt.Architect => CitizenProfession.Memor,
            CitizenArt.Builder => CitizenProfession.Quruvchi,
            CitizenArt.Teacher => CitizenProfession.Muallim,
            CitizenArt.Official => CitizenProfession.Amaldor,
            CitizenArt.Caravaneer => CitizenProfession.Tashuvchi,
            _ => CitizenProfession.None
        };

        public static WorkStyle StyleOf(CitizenProfession p) => p switch
        {
            CitizenProfession.Hunarmand or CitizenProfession.Quruvchi => WorkStyle.Hammer,
            CitizenProfession.Konchi => WorkStyle.Dig,
            CitizenProfession.Amaldor or CitizenProfession.SoliqYiguvchi or CitizenProfession.Olim
                or CitizenProfession.Muallim or CitizenProfession.Memor => WorkStyle.Write,
            CitizenProfession.Savdogar or CitizenProfession.BozorEgasi => WorkStyle.Trade,
            CitizenProfession.Baliqchi => WorkStyle.Fish,
            CitizenProfession.Tabib or CitizenProfession.Chorvador => WorkStyle.Tend,
            CitizenProfession.Tashuvchi or CitizenProfession.Omborchi => WorkStyle.Carry,
            _ => WorkStyle.None
        };

        /// <summary>Ish paytidagi gaplar (vaqti-vaqti bilan bosh ustida chiqadi).</summary>
        public static string[] LinesOf(CitizenProfession p) => p switch
        {
            CitizenProfession.Hunarmand => new[] { "Bu ko'zani bozorda yaxshi narxda sotamiz", "Temir qizidi — urish vaqti!", "Ustamning naqshini o'rganyapman" },
            CitizenProfession.Quruvchi => new[] { "G'isht teraman, devor tez ko'tariladi", "Ganch qorildi — tezroq!", "Yangi uyga poydevor qo'ydik" },
            CitizenProfession.Konchi => new[] { "Mis tomiri topildi!", "Tosh qattiq, lekin chiday olamiz", "Arava to'ldi — omborga!" },
            CitizenProfession.Baliqchi => new[] { "Bugun baliq ko'p!", "Qarmoq tortildi — katta laqqa!", "To'rni quritish kerak" },
            CitizenProfession.Savdogar => new[] { "Ipak, ziravor — arzon berdim!", "Kel, xaridor, mol yaxshi", "Karvon yangi mol olib keldi" },
            CitizenProfession.BozorEgasi => new[] { "Rastalar tartibli bo'lsin", "Tarozi to'g'ri — insof bilan", "Bozor gavjum, xazina to'ladi" },
            CitizenProfession.Amaldor => new[] { "Devon ishi bitdi", "Farmon yozildi", "Mahallalar ro'yxati tayyor" },
            CitizenProfession.SoliqYiguvchi => new[] { "Soliq daftari to'g'ri", "Xazinaga tushum yozildi", "Bozordan boj olindi" },
            CitizenProfession.Olim => new[] { "Yulduzlar jadvalini hisoblayapman", "Ulug'bek zijini o'qiyapman", "Yangi ilm — yangi kashfiyot" },
            CitizenProfession.Muallim => new[] { "Shogirdlar, xat o'rganamiz!", "Bugun hisob ilmi darsi", "Ilm — eng katta boylik" },
            CitizenProfession.Memor => new[] { "Gumbaz loyihasi tayyor", "Peshtoq koshini moviy bo'ladi", "O'lchov aniq bo'lsin" },
            CitizenProfession.Tabib => new[] { "Bu o't bosh og'rig'iga davo", "Bemorlar sog'aymoqda", "Dorivor o'tlar terildi" },
            CitizenProfession.Chorvador => new[] { "Qo'ylar semirdi", "Sut sog'ib keldim", "Yaylov yaxshi bo'ldi" },
            CitizenProfession.Tashuvchi => new[] { "Karvon yuki bozorga!", "Tuyalar dam olsin", "Ipak yo'lidan mol keldi" },
            CitizenProfession.Omborchi => new[] { "Ombor hisobi to'g'ri", "Don qoplari joylandi", "Zaxira yetarli" },
            _ => new[] { "Ish ko'p, vaqt oz" }
        };
    }
}
