namespace WorldCup2026.Helpers;

/// <summary>
/// Single source of truth mapping FIFA codes and English team names to Chinese names.
/// Used both for cross-source team-identity matching (DataServiceAggregator) and
/// for Chinese display translation (LocalizationService).
/// </summary>
public static class TeamNameMap
{
    public static readonly Dictionary<string, string> CodeOrNameToChinese = new(StringComparer.OrdinalIgnoreCase)
    {
        {"MEX","墨西哥"},{"RSA","南非"},{"KOR","韩国"},{"CZE","捷克"},{"CAN","加拿大"},{"BIH","波黑"},
        {"QAT","卡塔尔"},{"SUI","瑞士"},{"BRA","巴西"},{"MAR","摩洛哥"},{"HAI","海地"},{"SCO","苏格兰"},
        {"USA","美国"},{"PAR","巴拉圭"},{"AUS","澳大利亚"},{"TUR","土耳其"},{"GER","德国"},{"CUW","库拉索"},
        {"CIV","科特迪瓦"},{"ECU","厄瓜多尔"},{"NED","荷兰"},{"JPN","日本"},{"SWE","瑞典"},{"TUN","突尼斯"},
        {"BEL","比利时"},{"EGY","埃及"},{"IRN","伊朗"},{"NZL","新西兰"},{"ESP","西班牙"},{"CPV","佛得角"},
        {"KSA","沙特"},{"URU","乌拉圭"},{"FRA","法国"},{"SEN","塞内加尔"},{"IRQ","伊拉克"},{"NOR","挪威"},
        {"ARG","阿根廷"},{"ALG","阿尔及利亚"},{"AUT","奥地利"},{"JOR","约旦"},{"POR","葡萄牙"},{"COD","民主刚果"},
        {"UZB","乌兹别克斯坦"},{"COL","哥伦比亚"},{"ENG","英格兰"},{"CRO","克罗地亚"},{"GHA","加纳"},{"PAN","巴拿马"},
        // English name fallbacks (API may return full English names)
        {"Mexico","墨西哥"},{"South Africa","南非"},{"South Korea","韩国"},{"Czech Republic","捷克"},
        {"Canada","加拿大"},{"Bosnia and Herzegovina","波黑"},{"Qatar","卡塔尔"},{"Switzerland","瑞士"},
        {"Brazil","巴西"},{"Morocco","摩洛哥"},{"Haiti","海地"},{"Scotland","苏格兰"},
        {"United States","美国"},{"Paraguay","巴拉圭"},{"Australia","澳大利亚"},{"Turkey","土耳其"},
        {"Germany","德国"},{"Curaçao","库拉索"},{"Ivory Coast","科特迪瓦"},{"Ecuador","厄瓜多尔"},
        {"Netherlands","荷兰"},{"Japan","日本"},{"Sweden","瑞典"},{"Tunisia","突尼斯"},
        {"Belgium","比利时"},{"Egypt","埃及"},{"Iran","伊朗"},{"New Zealand","新西兰"},
        {"Spain","西班牙"},{"Cape Verde","佛得角"},{"Saudi Arabia","沙特"},{"Uruguay","乌拉圭"},
        {"France","法国"},{"Senegal","塞内加尔"},{"Iraq","伊拉克"},{"Norway","挪威"},
        {"Argentina","阿根廷"},{"Algeria","阿尔及利亚"},{"Austria","奥地利"},{"Jordan","约旦"},
        {"Portugal","葡萄牙"},{"DR Congo","民主刚果"},{"Uzbekistan","乌兹别克斯坦"},{"Colombia","哥伦比亚"},
        {"England","英格兰"},{"Croatia","克罗地亚"},{"Ghana","加纳"},{"Panama","巴拿马"},
        // FIFA API specific names
        {"Korea Republic","韩国"},{"Czechia","捷克"},{"Côte d'Ivoire","科特迪瓦"},
        {"Congo DR","民主刚果"},
    };
}
