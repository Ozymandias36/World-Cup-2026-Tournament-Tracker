using WorldCup2026.Helpers;

namespace WorldCup2026.Services;

public enum AppLanguage { Chinese, English }

/// <summary>
/// Global language switch. Team/UI names stay in English on the data model at all times;
/// this service does display-time translation only, so switching language never requires
/// re-fetching or re-merging data — views just re-render.
/// </summary>
public static class LocalizationService
{
    public static AppLanguage Current { get; private set; } = AppLanguage.English;

    public static event Action? LanguageChanged;

    public static void SetLanguage(AppLanguage lang)
    {
        if (Current == lang) return;
        Current = lang;
        LanguageChanged?.Invoke();
    }

    public static void Toggle() => SetLanguage(Current == AppLanguage.Chinese ? AppLanguage.English : AppLanguage.Chinese);

    /// <summary>Localized team display name. <paramref name="englishName"/> is the canonical name on the model.</summary>
    public static string TeamName(string? englishName, string? code)
    {
        if (Current == AppLanguage.English)
            return !string.IsNullOrEmpty(englishName) ? englishName : (code ?? "—");

        if (!string.IsNullOrEmpty(englishName) && TeamNameMap.CodeOrNameToChinese.TryGetValue(englishName, out var zh)) return zh;
        if (!string.IsNullOrEmpty(code) && TeamNameMap.CodeOrNameToChinese.TryGetValue(code, out zh)) return zh;
        return !string.IsNullOrEmpty(englishName) ? englishName : (code ?? "—");
    }

    /// <summary>"A组" in Chinese, "Group A" in English.</summary>
    public static string GroupLabel(string groupName) =>
        Current == AppLanguage.Chinese ? $"{groupName}组" : $"Group {groupName}";

    private static readonly Dictionary<string, (string Zh, string En)> Strings = new()
    {
        ["AppTitle"] = ("FIFA世界杯2026™ — 赛事追踪", "FIFA World Cup 2026™ — Tournament Tracker"),
        ["HeaderTitle"] = ("2026美加墨世界杯", "FIFA WORLD CUP 2026™"),
        ["HeaderSub"] = ("", "CANADA • MEXICO • USA"),
        ["Refresh"] = ("刷新", "Refresh"),
        ["AutoOn"] = ("自动: 开", "Auto: ON"),
        ["AutoOff"] = ("自动: 关", "Auto: OFF"),
        ["ExportPdf"] = ("导出 PDF", "Export PDF"),
        ["Language"] = ("EN", "中文"),
        ["KnockoutTitle"] = ("淘汰赛对阵表", "Knockout Stage Bracket"),
        ["GroupTitle"] = ("小组赛战绩", "Group Stage Results"),
        ["WaitingBracket"] = ("等待对阵数据…", "Waiting for bracket data..."),
        ["WaitingTournament"] = ("等待赛事数据…", "Waiting for tournament data..."),
        ["GroupMatches"] = ("小组赛详细对决", "Group Matches"),
        ["ColPos"] = ("#", "#"),
        ["ColTeam"] = ("队伍", "Team"),
        ["ColPlayed"] = ("场", "P"),
        ["ColWin"] = ("胜", "W"),
        ["ColDraw"] = ("平", "D"),
        ["ColLoss"] = ("负", "L"),
        ["ColGF"] = ("进", "GF"),
        ["ColGA"] = ("失", "GA"),
        ["ColGD"] = ("净", "GD"),
        ["ColPts"] = ("积", "Pts"),
        ["BeijingTime"] = ("UTC+8", "UTC+8"),
        ["Live"] = ("进行中", "LIVE"),
        ["Champion"] = ("冠军 2026™", "CHAMPION 2026™"),
        ["FifaWorldCup"] = ("FIFA世界杯", "FIFA WORLD CUP"),
        ["PdfMainTitle"] = ("2026年美加墨世界杯赛程", "2026 FIFA World Cup — Canada · Mexico · USA"),
        ["PdfBracketSubtitle"] = ("淘汰赛对阵图", "Knockout Bracket"),
        ["PdfGroupsTitle"] = ("小组赛积分榜", "Group Standings"),

        // Status bar / toolbar messages
        ["Never"] = ("从未", "Never"),
        ["StatusOn"] = ("开", "ON"),
        ["StatusOff"] = ("关", "OFF"),
        ["StatusSource"] = ("数据源", "Source"),
        ["StatusLastUpdate"] = ("更新时间", "Last update"),
        ["StatusAuto"] = ("自动", "Auto"),
        ["Refreshing"] = ("刷新中…", "Refreshing..."),
        ["ExportingPdf"] = ("正在导出 PDF…", "Exporting PDF..."),
        ["PdfExported"] = ("PDF 导出完成！", "PDF exported!"),
        ["NoBracketData"] = ("暂无对阵数据", "No bracket data"),
        ["ErrorPrefix"] = ("错误", "Error"),
    };

    public static string T(string key) => Current == AppLanguage.Chinese ? Strings[key].Zh : Strings[key].En;
}
