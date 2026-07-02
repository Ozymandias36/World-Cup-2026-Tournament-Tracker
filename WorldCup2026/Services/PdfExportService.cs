using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using WorldCup2026.Models;
using WorldCup2026.ViewModels;

namespace WorldCup2026.Services;

public class PdfExportService
{
    static PdfExportService() { QuestPDF.Settings.License = LicenseType.Community; }

    public void ExportBracket(string path, List<BracketRound> upper, List<BracketRound> lower,
        Match? final, Match? third, DateTime stamp)
    {
        var stages = new[] { TournamentStage.RoundOf32, TournamentStage.RoundOf16,
            TournamentStage.QuarterFinal, TournamentStage.SemiFinal };

        // Build a flat list: round label → list of (home, homeScore, awayScore, away) for each match
        var columns = new List<(string Label, List<Match> Matches)>();

        foreach (var st in stages)
        {
            var up = upper.FirstOrDefault(r => r.Stage == st);
            var lo = lower.FirstOrDefault(r => r.Stage == st);
            var combined = new List<Match>();
            if (up != null) combined.AddRange(up.Matches);
            if (lo != null) combined.AddRange(lo.Matches);
            if (combined.Count > 0)
                columns.Add((st switch
                {
                    TournamentStage.RoundOf32 => "Round of 32",
                    TournamentStage.RoundOf16 => "Round of 16",
                    TournamentStage.QuarterFinal => "Quarter-Finals",
                    TournamentStage.SemiFinal => "Semi-Finals",
                    _ => st.ToString()
                }, combined));
        }
        if (final != null) columns.Add(("Final", new List<Match> { final }));
        if (third != null) columns.Add(("3rd Place", new List<Match> { third }));

        int maxRows = columns.Max(c => c.Matches.Count);

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(420, 297, Unit.Millimetre);
                page.Margin(10);
                page.DefaultTextStyle(x => x.FontSize(6));

                page.Header().AlignCenter()
                    .Text($"FIFA World Cup 2026™ — Knockout Bracket  |  {stamp:yyyy-MM-dd HH:mm}")
                    .FontSize(9).FontColor(QuestPDF.Helpers.Colors.Grey.Medium);

                page.Content().PaddingTop(4).Table(table =>
                {
                    // Column definitions
                    table.ColumnsDefinition(c =>
                    {
                        foreach (var _ in columns)
                        {
                            c.RelativeColumn();
                            c.ConstantColumn(8); // spacer between cols
                        }
                    });

                    // Header row
                    int colIdx = 0;
                    foreach (var (label, _) in columns)
                    {
                        table.Cell().RowSpan(1).ColumnSpan(1).Background("#1a365d")
                            .Padding(2).AlignCenter().Text(label).FontSize(7).Bold().FontColor("#ffffff");
                        table.Cell().ColumnSpan(1).Background("#ffffff");
                        colIdx += 2;
                    }

                    // Match rows
                    for (int row = 0; row < maxRows; row++)
                    {
                        int c = 0;
                        foreach (var (_, matches) in columns)
                        {
                            if (row < matches.Count)
                            {
                                var m = matches[row];
                                var bg = (row / 2) % 2 == 0 ? "#ffffff" : "#f7f7f7";
                                if (m.IsFinished) bg = "#f8f8f8";

                                table.Cell().Background(bg).Padding(2).Column(col =>
                                {
                                    col.Item().Row(r =>
                                    {
                                        r.ConstantItem(60).AlignRight().Text(FormatName(m.HomeTeamName, m.HomeTeamCode)).FontSize(6);
                                        r.ConstantItem(28).AlignCenter().Text(FormatScore(m, home: true)).FontSize(6).Bold();
                                    });
                                    col.Item().PaddingTop(1).BorderTop(0.5f).BorderColor("#e0e0e0")
                                        .Row(r =>
                                    {
                                        r.ConstantItem(60).AlignRight().Text(FormatName(m.AwayTeamName, m.AwayTeamCode)).FontSize(6);
                                        r.ConstantItem(28).AlignCenter().Text(FormatScore(m, home: false)).FontSize(6).Bold();
                                    });
                                });
                            }
                            else
                            {
                                table.Cell().Background("#fafafa");
                            }
                            table.Cell().Background("#ffffff"); // spacer
                            c += 2;
                        }
                    }
                });

                page.Footer().AlignCenter().Text("FIFA World Cup 2026™ — Knockout Bracket")
                    .FontSize(5).FontColor(QuestPDF.Helpers.Colors.Grey.Lighten2);
            });
        }).GeneratePdf(path);
    }

    private static string FormatName(string? name, string? code)
        => !string.IsNullOrEmpty(name) ? name : (!string.IsNullOrEmpty(code) ? code : "—");

    private static string FormatScore(Match m, bool home)
    {
        var s = home ? m.HomeScore : m.AwayScore;
        var p = home ? m.HomePenalties : m.AwayPenalties;
        if (!s.HasValue) return "—";
        return m.HasPenalties ? $"{s} ({p})" : s.ToString()!;
    }
}
