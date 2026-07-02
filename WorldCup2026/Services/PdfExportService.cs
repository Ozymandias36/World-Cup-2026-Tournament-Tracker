using System.IO;
using SkiaSharp;
using WorldCup2026.Models;
using WorldCup2026.ViewModels;

namespace WorldCup2026.Services;

public class PdfExportService
{
    // ── Layout constants (mirror BracketView) ──
    private const float ColW = 165, MatchW = 150, MatchH = 54;
    private const float HalfGap = 300, PairGap = 4, QuadGap = 12;
    private const float PdfW = 1682, PdfH = 1190; // A3 landscape at 96 DPI (420mm × 297mm)

    public void ExportBracket(string path, List<BracketRound> upper, List<BracketRound> lower,
        Match? final, Match? third, DateTime stamp)
    {
        var stages = new[] { TournamentStage.RoundOf32, TournamentStage.RoundOf16,
            TournamentStage.QuarterFinal, TournamentStage.SemiFinal };

        // Collect rounds
        var upperRounds = new List<BracketRound>();
        var lowerRounds = new List<BracketRound>();
        foreach (var st in stages)
        {
            var u = upper.FirstOrDefault(r => r.Stage == st); if (u?.Matches.Count > 0) upperRounds.Add(u);
            var l = lower.FirstOrDefault(r => r.Stage == st); if (l?.Matches.Count > 0) lowerRounds.Add(l);
        }

        // ── Compute positions (same logic as BracketView) ──
        var pos = new Dictionary<string, SKPoint>();
        LayoutHalf(upperRounds, 10, 20, false, pos, "U");
        float ur = 10 + (upperRounds.Count - 1) * ColW + MatchW;
        float ls = ur + HalfGap;
        LayoutHalf(lowerRounds, ls, 20, true, pos, "L");
        float fX = 0, fY = 0;
        if (final != null)
        {
            var su = upperRounds.Count > 0 ? pos[$"U|{upperRounds.Last().Stage}|0"] : new SKPoint(0, 300);
            var sl = lowerRounds.Count > 0 ? pos[$"L|{lowerRounds.Last().Stage}|0"] : new SKPoint(0, 300);
            fX = (ur + ls) / 2 - MatchW / 2;
            fY = (su.Y + sl.Y) / 2;
            pos["Final|0"] = new SKPoint(fX, fY);
        }
        float r = lowerRounds.Count > 0 ? ls + (lowerRounds.Count - 1) * ColW + MatchW : ur;
        float w = Math.Max(r, fX + MatchW) + 40;
        float h = Math.Max(pos.Values.Max(p => p.Y) + MatchH + 60, 500f);

        // ── Scale to fit A3 landscape ──
        float scale = Math.Min(PdfW / w, PdfH / h);
        float SX(float x) => x * scale;
        float SY(float y) => y * scale;
        float SW(float v) => v * scale;

        // ── Create PDF ──
        using var stream = File.Create(path);
        using var doc = SKDocument.CreatePdf(stream);
        var canvas = doc.BeginPage(PdfW, PdfH);
        canvas.Clear(SKColors.White);

        // Colors
        var linePaint = new SKPaint { Color = SKColor.Parse("#b0b0b0"), StrokeWidth = 1.2f * scale, IsAntialias = true, Style = SKPaintStyle.Stroke };
        var trophyGold = new SKPaint { Color = SKColor.Parse("#c8a951"), IsAntialias = true };
        var trophyDark = new SKPaint { Color = SKColor.Parse("#a08535"), IsAntialias = true };

        SKPaint StageColor(TournamentStage st) => new SKPaint
        {
            Color = st switch
            {
                TournamentStage.RoundOf32 => SKColor.Parse("#1a365d"),
                TournamentStage.RoundOf16 => SKColor.Parse("#2a6f9d"),
                TournamentStage.QuarterFinal => SKColor.Parse("#c8a951"),
                TournamentStage.SemiFinal => SKColor.Parse("#d46a0e"),
                TournamentStage.Final => SKColor.Parse("#d00000"),
                TournamentStage.ThirdPlace => SKColor.Parse("#8b4513"),
                _ => SKColor.Parse("#808080")
            },
            StrokeWidth = 1.5f * scale, IsAntialias = true, Style = SKPaintStyle.Stroke
        };
        SKPaint FillColor(bool finished) => new SKPaint
        {
            Color = finished ? SKColor.Parse("#f8f8f8") : SKColors.White,
            Style = SKPaintStyle.Fill
        };

        var textPaint = new SKPaint { Color = SKColor.Parse("#333333"), TextSize = 11 * scale, IsAntialias = true, SubpixelText = true };
        var boldPaint = new SKPaint { Color = SKColor.Parse("#333333"), TextSize = 11 * scale, IsAntialias = true, SubpixelText = true, FakeBoldText = true };
        var greenPaint = new SKPaint { Color = SKColor.Parse("#006400"), TextSize = 11 * scale, IsAntialias = true, FakeBoldText = true };
        var greyPaint = new SKPaint { Color = SKColor.Parse("#888888"), TextSize = 7 * scale, IsAntialias = true };
        var sepPaint = new SKPaint { Color = SKColor.Parse("#d0d0d0"), StrokeWidth = 0.5f * scale, Style = SKPaintStyle.Stroke };

        // ── Draw connectors ──
        void Conn(float x1, float y1, float x2, float y2)
        {
            float mx = (x1 + x2) / 2;
            canvas.DrawLine(SX(x1), SY(y1), SX(mx), SY(y1), linePaint);
            canvas.DrawLine(SX(mx), SY(y1), SX(mx), SY(y2), linePaint);
            canvas.DrawLine(SX(mx), SY(y2), SX(x2), SY(y2), linePaint);
        }
        void HalfConnectors(List<BracketRound> rounds, string tag)
        {
            for (int ri = 1; ri < rounds.Count; ri++)
            {
                var prev = rounds[ri - 1]; var curr = rounds[ri];
                for (int i = 0; i < curr.Matches.Count; i++)
                {
                    if (!pos.TryGetValue($"{tag}|{curr.Stage}|{i}", out var par)) continue;
                    for (int j = 0; j < 2; j++)
                    {
                        if (pos.TryGetValue($"{tag}|{prev.Stage}|{i * 2 + j}", out var ch))
                            Conn(ch.X + MatchW, ch.Y + MatchH / 2, par.X, par.Y + MatchH / 2);
                    }
                }
            }
        }
        HalfConnectors(upperRounds, "U");
        HalfConnectors(lowerRounds, "L");

        // Final connectors
        if (final != null)
        {
            if (upperRounds.Count > 0 && pos.TryGetValue($"U|{upperRounds.Last().Stage}|0", out var su))
                Conn(su.X + MatchW, su.Y + MatchH / 2, fX, fY + MatchH / 2);
            if (lowerRounds.Count > 0 && pos.TryGetValue($"L|{lowerRounds.Last().Stage}|0", out var sl))
                Conn(ls + MatchW, sl.Y + MatchH / 2, fX, fY + MatchH / 2);
        }

        // ── Draw match nodes ──
        void Node(Match m, float x, float y, TournamentStage stage)
        {
            float rx = SX(x), ry = SY(y), rw = SW(MatchW), rh = SW(MatchH);
            float pad = 4 * scale;
            var accent = StageColor(stage);

            // Background + border
            var bg = m.IsFinished ? SKColor.Parse("#f8f8f8") : SKColors.White;
            canvas.DrawRoundRect(rx, ry, rw, rh, 3 * scale, 3 * scale, new SKPaint { Color = bg, Style = SKPaintStyle.Fill });
            canvas.DrawRoundRect(rx, ry, rw, rh, 3 * scale, 3 * scale, accent);

            // Separator
            float midY = ry + (rh - pad * 2) / 2 + pad;
            canvas.DrawLine(rx + pad, midY, rx + rw - pad, midY, sepPaint);

            // Rows
            void Row(bool home, float topY)
            {
                var name = home ? m.HomeTeamName : m.AwayTeamName;
                var score = home ? m.HomeScore : m.AwayScore;
                var opp = home ? m.AwayScore : m.HomeScore;
                var pen = home ? m.HomePenalties : m.AwayPenalties;
                var oppPen = home ? m.AwayPenalties : m.HomePenalties;
                bool isWin = m.HasPenalties && score.HasValue && opp.HasValue && score == opp
                    ? (pen.HasValue && oppPen.HasValue && pen > oppPen)
                    : (score.HasValue && opp.HasValue && score > opp);
                var label = !string.IsNullOrEmpty(name) ? name : "—";
                var scoreText = score.HasValue ? (m.HasPenalties ? $"{score}({pen})" : score.ToString()!) : "—";

                var nmPaint = isWin ? greenPaint : textPaint;
                var scPaint = isWin ? greenPaint : boldPaint;

                // Truncate name if too long
                float maxNameW = rw * 0.65f;
                float nameW = nmPaint.MeasureText(label);
                if (nameW > maxNameW) { var ratio = maxNameW / nameW; nmPaint.TextSize *= ratio; }

                canvas.DrawText(label, rx + pad, topY + rh * 0.30f, nmPaint);
                nmPaint.TextSize = 11 * scale; // reset
                canvas.DrawText(scoreText, rx + rw - pad - 40 * scale, topY + rh * 0.30f, scPaint);
            }
            Row(true, ry + pad);
            Row(false, midY + 2 * scale);
        }

        void DrawNodes(List<BracketRound> rounds, string tag)
        {
            foreach (var rd in rounds)
                for (int i = 0; i < rd.Matches.Count; i++)
                    if (pos.TryGetValue($"{tag}|{rd.Stage}|{i}", out var pt))
                        Node(rd.Matches[i], pt.X, pt.Y, rd.Stage);
        }
        DrawNodes(upperRounds, "U");
        DrawNodes(lowerRounds, "L");
        if (final != null) Node(final, fX, fY, TournamentStage.Final);
        if (third != null) Node(third, fX, fY + MatchH + 50, TournamentStage.ThirdPlace);

        // ── Trophy ──
        if (final != null)
        {
            float tc = SX(fX + MatchW / 2), ty = SY(fY) - SW(140);
            // Cup body
            canvas.DrawOval(tc - SW(20), ty, SW(40), SW(20), trophyGold);
            canvas.DrawOval(tc - SW(20), ty, SW(40), SW(20), new SKPaint { Color = trophyDark.Color, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f * scale });
            // Stem
            canvas.DrawRect(tc - SW(4), ty + SW(20), SW(8), SW(35), trophyGold);
            // Base
            canvas.DrawRoundRect(tc - SW(30), ty + SW(55), SW(60), SW(38), 3 * scale, 3 * scale, trophyGold);
            canvas.DrawRoundRect(tc - SW(30), ty + SW(55), SW(60), SW(38), 3 * scale, 3 * scale, new SKPaint { Color = trophyDark.Color, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f * scale });
            // Label
            var chPaint = new SKPaint { Color = trophyDark.Color, TextSize = 14 * scale, IsAntialias = true, FakeBoldText = true, TextAlign = SKTextAlign.Center };
            canvas.DrawText("CHAMPION", tc, ty + SW(100), chPaint);
        }

        // ── Footer ──
        var footPaint = new SKPaint { Color = SKColor.Parse("#aaaaaa"), TextSize = 6 * scale, IsAntialias = true, TextAlign = SKTextAlign.Center };
        canvas.DrawText($"FIFA World Cup 2026™ — Knockout Bracket  |  {stamp:yyyy-MM-dd HH:mm}", PdfW / 2, PdfH - 15, footPaint);

        doc.EndPage();
        doc.Close();
    }

    // ── Layout (same as BracketView) ──
    private static void LayoutHalf(List<BracketRound> rounds, float x0, float y0, bool mirrored,
        Dictionary<string, SKPoint> pos, string tag)
    {
        for (int ri = 0; ri < rounds.Count; ri++)
        {
            var rd = rounds[ri]; int n = rd.Matches.Count;
            int co = mirrored ? rounds.Count - 1 - ri : ri;
            float x = x0 + co * ColW;
            float[] ys;
            if (ri == 0)
            {
                ys = new float[n]; float y = y0;
                for (int i = 0; i < n; i++) { ys[i] = y; y += MatchH; if (i % 2 == 1 && i < n - 1) y += QuadGap; else if (i < n - 1) y += PairGap; }
            }
            else
            {
                ys = new float[n]; var prev = rounds[ri - 1];
                for (int i = 0; i < n; i++)
                {
                    float ay = 0, by = 0; int a = i * 2, b = i * 2 + 1;
                    if (a < prev.Matches.Count && pos.TryGetValue($"{tag}|{prev.Stage}|{a}", out var pa)) ay = pa.Y;
                    if (b < prev.Matches.Count && pos.TryGetValue($"{tag}|{prev.Stage}|{b}", out var pb)) by = pb.Y;
                    ys[i] = (ay > 0 && by > 0) ? (ay + by) / 2f : (ay > 0 ? ay : by > 0 ? by : y0 + i * (MatchH + 20));
                }
            }
            for (int i = 0; i < n; i++) pos[$"{tag}|{rd.Stage}|{i}"] = new SKPoint(x, ys[i]);
        }
    }
}
