using System.IO;
using SkiaSharp;
using WorldCup2026.Helpers;
using WorldCup2026.Models;
using WorldCup2026.ViewModels;

namespace WorldCup2026.Services;

public class PdfExportService
{
    // ── Layout constants (mirror BracketView) ──
    private const float ColW = 165, MatchW = 150, MatchH = 54;
    private const float HalfGap = 300, PairGap = 4, QuadGap = 12;
    private const float PdfW = 1682, PdfH = 1190; // A3 landscape at 96 DPI (420mm × 297mm)

    private readonly Dictionary<string, SKBitmap?> _flagCache = new();

    // Group table stat columns: P, W, D, L, GF, GA, GD (Pts has its own width below)
    private static readonly float[] StatColWidths = { 20, 20, 20, 20, 20, 22, 22 };
    private const float PtsColWidth = 22;

    /// <summary>Load (and cache) a flag bitmap by FIFA code. Raster is fine — no vectorization needed.</summary>
    private SKBitmap? GetFlag(string? code)
    {
        if (string.IsNullOrEmpty(code)) return null;
        if (_flagCache.TryGetValue(code, out var cached)) return cached;
        var bytes = FlagHelper.GetFlagPngBytes(code);
        var bmp = bytes != null ? SKBitmap.Decode(bytes) : null;
        _flagCache[code] = bmp;
        return bmp;
    }

    public void ExportBracket(string path, List<BracketRound> upper, List<BracketRound> lower,
        Match? final, Match? third, List<Group> groups, DateTime stamp)
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
        // Reserve headroom above the topmost row for the trophy, and room below Final for 3rd place
        float topPad = final != null ? 150f : 0f;
        float r = lowerRounds.Count > 0 ? ls + (lowerRounds.Count - 1) * ColW + MatchW : ur;
        float contentRight = Math.Max(r, fX + MatchW);
        float bottomMost = pos.Values.Max(p => p.Y) + MatchH;
        if (third != null) bottomMost = Math.Max(bottomMost, fY + MatchH + 50 + MatchH);
        float w = contentRight + 20;
        float h = topPad + bottomMost + 40;

        // ── Single-page layout: bracket on top, group standings below ──
        const float margin = 15f;
        const float titleH = 100f;
        const float sectionGap = 12f;
        const float groupsTitleH = 30f;
        const float groupsGridH = 330f;
        const float footerH = 16f;

        float bracketAreaTop = margin + titleH;
        float bracketAreaH = groups.Count > 0
            ? PdfH - bracketAreaTop - sectionGap - groupsTitleH - groupsGridH - footerH - margin
            : PdfH - bracketAreaTop - footerH - margin;

        // ── Scale bracket to fit its reserved area, centered within it ──
        float scale = Math.Min((PdfW - margin * 2) / w, bracketAreaH / h) * 0.97f;
        float offX = margin + ((PdfW - margin * 2) - w * scale) / 2f;
        float offY = bracketAreaTop + (bracketAreaH - h * scale) / 2f + topPad * scale;
        float SX(float x) => offX + x * scale;
        float SY(float y) => offY + y * scale;
        float SW(float v) => v * scale;

        // ── CJK-capable typeface (ask the OS font manager for one that can render Chinese) ──
        var cjkTypeface = SKFontManager.Default.MatchCharacter('国') ?? SKTypeface.Default;

        // ── Create PDF ──
        using var stream = File.Create(path);
        using var doc = SKDocument.CreatePdf(stream);
        var canvas = doc.BeginPage(PdfW, PdfH);
        canvas.Clear(SKColors.White);

        using var mainTitlePaint = new SKPaint { Color = SKColor.Parse("#1a365d"), TextSize = 50, IsAntialias = true, Typeface = cjkTypeface, FakeBoldText = true, TextAlign = SKTextAlign.Center };
        canvas.DrawText("2026年美加墨世界杯赛程", PdfW / 2, margin + 46, mainTitlePaint);
        using var pageTitlePaint = new SKPaint { Color = SKColor.Parse("#888888"), TextSize = 26, IsAntialias = true, Typeface = cjkTypeface, TextAlign = SKTextAlign.Center };
        canvas.DrawText("淘汰赛对阵图", PdfW / 2, margin + 96, pageTitlePaint);

        // Colors
        using var linePaint = new SKPaint { Color = SKColor.Parse("#b0b0b0"), StrokeWidth = Math.Max(1f, 1.2f * scale), IsAntialias = true, Style = SKPaintStyle.Stroke };
        using var sepPaint = new SKPaint { Color = SKColor.Parse("#d0d0d0"), StrokeWidth = Math.Max(0.5f, 0.5f * scale), Style = SKPaintStyle.Stroke };

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
            StrokeWidth = Math.Max(1f, 1.5f * scale), IsAntialias = true, Style = SKPaintStyle.Stroke
        };

        SKPaint MakeTextPaint(string hex, bool bold = false) => new SKPaint
        {
            Color = SKColor.Parse(hex), TextSize = 11 * scale, IsAntialias = true,
            Typeface = cjkTypeface, FakeBoldText = bold
        };

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
            using var accent = StageColor(stage);

            var bg = m.IsFinished ? SKColor.Parse("#f8f8f8") : SKColors.White;
            using var bgPaint = new SKPaint { Color = bg, Style = SKPaintStyle.Fill };
            canvas.DrawRoundRect(rx, ry, rw, rh, 3 * scale, 3 * scale, bgPaint);
            canvas.DrawRoundRect(rx, ry, rw, rh, 3 * scale, 3 * scale, accent);

            float midY = ry + rh / 2f;
            canvas.DrawLine(rx + pad, midY, rx + rw - pad, midY, sepPaint);

            void Row(bool home, float rowTop, float rowH)
            {
                var name = home ? m.HomeTeamName : m.AwayTeamName;
                var code = home ? m.HomeTeamCode : m.AwayTeamCode;
                var score = home ? m.HomeScore : m.AwayScore;
                var opp = home ? m.AwayScore : m.HomeScore;
                var pen = home ? m.HomePenalties : m.AwayPenalties;
                var oppPen = home ? m.AwayPenalties : m.HomePenalties;
                bool isWin = m.HasPenalties && score.HasValue && opp.HasValue && score == opp
                    ? (pen.HasValue && oppPen.HasValue && pen > oppPen)
                    : (score.HasValue && opp.HasValue && score > opp);
                var label = !string.IsNullOrEmpty(name) ? name : "—";
                var scoreText = score.HasValue ? (m.HasPenalties ? $"{score}({pen})" : score.ToString()!) : "—";

                using var nmPaint = MakeTextPaint(isWin ? "#006400" : "#333333", isWin);
                using var scPaint = MakeTextPaint(isWin ? "#006400" : "#333333", true);

                float textX = rx + pad;
                float centerY = rowTop + rowH / 2f;

                // Flag
                var flag = GetFlag(code);
                if (flag != null)
                {
                    float flagW = SW(18), flagH = SW(13);
                    var dest = new SKRect(textX, centerY - flagH / 2, textX + flagW, centerY + flagH / 2);
                    canvas.DrawBitmap(flag, dest);
                    textX += flagW + 3 * scale;
                }

                float scoreW = 42 * scale;
                float maxNameW = (rx + rw - pad - scoreW) - textX;
                var displayLabel = label;
                float nameW = nmPaint.MeasureText(displayLabel);
                while (nameW > maxNameW && displayLabel.Length > 1)
                {
                    displayLabel = displayLabel[..^1];
                    nameW = nmPaint.MeasureText(displayLabel + "…");
                }
                if (displayLabel != label) displayLabel += "…";

                var fm = nmPaint.FontMetrics;
                float baselineY = centerY - (fm.Ascent + fm.Descent) / 2f;

                canvas.DrawText(displayLabel, textX, baselineY, nmPaint);
                canvas.DrawText(scoreText, rx + rw - pad - scoreW, baselineY, scPaint);
            }
            Row(true, ry, rh / 2f);
            Row(false, midY, rh / 2f);
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

        // ── Trophy (vector, matches the in-app design) ──
        if (final != null)
        {
            float centerX = SX(fX + MatchW / 2);
            float trophyScale = SW(1f) * 0.85f; // local units → PDF units
            float baseY = SY(fY) - 20 * scale; // bottom of trophy sits just above the Final node
            DrawTrophy(canvas, centerX, baseY, trophyScale, cjkTypeface);
        }

        // ── Group stage standings — 6 groups per row × 2 rows, same page ──
        if (groups.Count > 0)
        {
            float groupsTitleY = bracketAreaTop + bracketAreaH + sectionGap;
            using var groupsTitlePaint = new SKPaint { Color = SKColor.Parse("#888888"), TextSize = 26, IsAntialias = true, Typeface = cjkTypeface, FakeBoldText = true, TextAlign = SKTextAlign.Center };
            canvas.DrawText("小组赛积分榜", PdfW / 2, groupsTitleY + 20, groupsTitlePaint);

            float gridTop = groupsTitleY + groupsTitleH;
            const int cols = 6, rows = 2;
            float cellW = (PdfW - margin * 2) / cols;
            float cellH = groupsGridH / rows;

            var ordered = groups.OrderBy(g => g.Name).ToList();
            for (int idx = 0; idx < ordered.Count && idx < cols * rows; idx++)
            {
                int col = idx % cols, row = idx / cols;
                float cx = margin + col * cellW;
                float cy = gridTop + row * cellH;
                DrawGroupTable(canvas, ordered[idx], cx + 4, cy + 4, cellW - 8, cellH - 8, cjkTypeface);
            }
        }

        // ── Footer ──
        using var footPaint = new SKPaint { Color = SKColor.Parse("#aaaaaa"), TextSize = 7, IsAntialias = true, Typeface = cjkTypeface, TextAlign = SKTextAlign.Center };
        canvas.DrawText($"FIFA World Cup 2026™  |  {stamp:yyyy-MM-dd HH:mm}", PdfW / 2, PdfH - 6, footPaint);

        doc.EndPage();
        doc.Close();

        foreach (var bmp in _flagCache.Values) bmp?.Dispose();
        _flagCache.Clear();
    }

    private void DrawGroupTable(SKCanvas canvas, Group group, float x, float y, float w, float h, SKTypeface typeface)
    {
        const float headerH = 22, colHeaderH = 16;
        using var headerBg = new SKPaint { Color = SKColor.Parse("#1a365d"), Style = SKPaintStyle.Fill };
        canvas.DrawRect(new SKRect(x, y, x + w, y + headerH), headerBg);
        using var headerText = new SKPaint { Color = SKColors.White, TextSize = 13, IsAntialias = true, Typeface = typeface, FakeBoldText = true };
        canvas.DrawText($"{group.Name} 组", x + 8, y + headerH - 6, headerText);

        // Column widths: # | Team(flex) | P W D L GF GA GD | Pts
        const float posW = 18;
        float teamW = Math.Max(60, w - posW - StatColWidths.Sum() - PtsColWidth);

        // Column header row
        string[] labels = { "#", "队伍", "场", "胜", "平", "负", "进", "失", "净", "积" };
        using var colHeaderBg = new SKPaint { Color = SKColor.Parse("#eef1f5"), Style = SKPaintStyle.Fill };
        canvas.DrawRect(new SKRect(x, y + headerH, x + w, y + headerH + colHeaderH), colHeaderBg);
        using var colHeaderText = new SKPaint { Color = SKColor.Parse("#555555"), TextSize = 9, IsAntialias = true, Typeface = typeface, FakeBoldText = true };
        DrawStatRow(canvas, labels, null, x, y + headerH, colHeaderH, teamW, posW, colHeaderText, colHeaderText, null);

        // Data rows
        float rowY = y + headerH + colHeaderH;
        float rowH = Math.Max(14, (h - headerH - colHeaderH) / Math.Max(1, group.Standings.Count));

        using var normalText = new SKPaint { Color = SKColor.Parse("#333333"), TextSize = 9.5f, IsAntialias = true, Typeface = typeface };
        using var boldText = new SKPaint { Color = SKColor.Parse("#333333"), TextSize = 9.5f, IsAntialias = true, Typeface = typeface, FakeBoldText = true };
        using var qualifiedPosText = new SKPaint { Color = SKColor.Parse("#2d6a4f"), TextSize = 9.5f, IsAntialias = true, Typeface = typeface, FakeBoldText = true };
        using var altRowBg = new SKPaint { Color = SKColor.Parse("#f5f5f5"), Style = SKPaintStyle.Fill };
        using var qualifiedBg = new SKPaint { Color = SKColor.Parse("#e8f5e8"), Style = SKPaintStyle.Fill };
        using var whiteBg = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill };
        using var borderPaint = new SKPaint { Color = SKColor.Parse("#e0e0e0"), Style = SKPaintStyle.Stroke, StrokeWidth = 0.5f };

        for (int i = 0; i < group.Standings.Count; i++)
        {
            var s = group.Standings[i];
            bool isTop2 = s.Position <= 2;
            bool isBestThird = s.Position == 3 && s.IsQualified;
            bool qualified = isTop2 || isBestThird;

            var rowBg = qualified ? qualifiedBg : (i % 2 == 0 ? whiteBg : altRowBg);
            canvas.DrawRect(new SKRect(x, rowY, x + w, rowY + rowH), rowBg);

            string[] values = { s.Position.ToString(), s.TeamName, s.Played.ToString(), s.Wins.ToString(),
                s.Draws.ToString(), s.Losses.ToString(), s.GoalsFor.ToString(), s.GoalsAgainst.ToString(),
                s.GoalDifference.ToString("+0;-0;0"), s.Points.ToString() };

            var posPaint = qualified ? qualifiedPosText : normalText;
            DrawStatRow(canvas, values, GetFlag(s.TeamCode), x, rowY, rowH, teamW, posW, posPaint, normalText, boldText);

            rowY += rowH;
        }

        canvas.DrawRect(new SKRect(x, y, x + w, rowY), borderPaint);
    }

    /// <summary>Draw one row of a group table: position, optional flag, team name, then 8 numeric stat columns.</summary>
    private static void DrawStatRow(SKCanvas canvas, string[] values, SKBitmap? flag, float x, float rowY, float rowH,
        float teamW, float posW, SKPaint posPaint, SKPaint teamAndStatPaint, SKPaint? ptsPaint)
    {
        float centerY = rowY + rowH / 2f;
        var fm = teamAndStatPaint.FontMetrics;
        float baselineY = centerY - (fm.Ascent + fm.Descent) / 2f;

        // Position (col 0), centered in posW
        using (var p = new SKPaint { Color = posPaint.Color, TextSize = posPaint.TextSize, IsAntialias = true, Typeface = posPaint.Typeface, FakeBoldText = posPaint.FakeBoldText, TextAlign = SKTextAlign.Center })
            canvas.DrawText(values[0], x + posW / 2f, baselineY, p);

        // Team name (col 1) with optional flag icon, left-aligned
        using (var p = new SKPaint { Color = teamAndStatPaint.Color, TextSize = teamAndStatPaint.TextSize, IsAntialias = true, Typeface = teamAndStatPaint.Typeface })
        {
            float nameX = x + posW + 3;
            if (flag != null)
            {
                float flagH = Math.Min(rowH * 0.6f, 12f);
                float flagW = flagH * (18f / 13f);
                var dest = new SKRect(nameX, centerY - flagH / 2, nameX + flagW, centerY + flagH / 2);
                canvas.DrawBitmap(flag, dest);
                nameX += flagW + 3;
            }

            var label = values[1];
            float maxW = (x + posW + teamW) - nameX - 2;
            while (p.MeasureText(label) > maxW && label.Length > 1) label = label[..^1];
            canvas.DrawText(label, nameX, baselineY, p);
        }

        // Stat columns (P W D L GF GA GD Pts) — centered in each column
        float statX = x + posW + teamW;
        for (int i = 2; i < values.Length; i++)
        {
            bool isPtsCol = i == values.Length - 1;
            float w = isPtsCol ? PtsColWidth : StatColWidths[i - 2];
            var paint = (isPtsCol && ptsPaint != null) ? ptsPaint : teamAndStatPaint;
            using var p = new SKPaint { Color = paint.Color, TextSize = paint.TextSize, IsAntialias = true, Typeface = paint.Typeface, FakeBoldText = paint.FakeBoldText, TextAlign = SKTextAlign.Center };
            canvas.DrawText(values[i], statX + w / 2f, baselineY, p);
            statX += w;
        }
    }

    /// <summary>
    /// Draw the FIFA World Cup trophy, ported 1:1 from the WPF BracketView design
    /// (100×170 local coordinate box: globe, wings, cup body, stem, 5-tier base).
    /// (cx, bottomY) = horizontal center and bottom anchor point in PDF units.
    /// </summary>
    private static void DrawTrophy(SKCanvas canvas, float cx, float bottomY, float s, SKTypeface typeface)
    {
        var gold = SKColor.Parse("#d4b042");
        var goldDark = SKColor.Parse("#b08a28");
        var goldLight = SKColor.Parse("#f0d868");
        var malachite = SKColor.Parse("#2e6b4e");

        float ox = cx - 50 * s;      // local (0,0) maps here
        float oy = bottomY - 170 * s; // local box is 170 tall
        float L(float v) => ox + v * s;
        float T(float v) => oy + v * s;

        using var fillGold = new SKPaint { Color = gold, IsAntialias = true, Style = SKPaintStyle.Fill };
        using var fillGoldDarkPaint = new SKPaint { Color = goldDark, IsAntialias = true, Style = SKPaintStyle.Fill };
        using var strokeGoldDark = new SKPaint { Color = goldDark, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = Math.Max(0.6f, s) };
        using var fillGoldLight = new SKPaint { Color = goldLight, IsAntialias = true, Style = SKPaintStyle.Fill };
        using var fillMalachite = new SKPaint { Color = malachite, IsAntialias = true, Style = SKPaintStyle.Fill };
        using var fillWhite = new SKPaint { Color = SKColors.White.WithAlpha(128), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var bandPaint = new SKPaint { Color = gold.WithAlpha(128), IsAntialias = true };
        using var sparklePaint = new SKPaint { Color = SKColors.White.WithAlpha(128), IsAntialias = true };

        // Globe
        canvas.DrawOval(new SKRect(L(41), T(0), L(59), T(18)), fillGoldLight);
        canvas.DrawOval(new SKRect(L(41), T(0), L(59), T(18)), strokeGoldDark);
        canvas.DrawRect(new SKRect(L(41), T(7), L(59), T(11)), bandPaint);
        canvas.DrawRect(new SKRect(L(49), T(18), L(51), T(23)), fillGoldDarkPaint);

        // Wings (left/right)
        using (var leftWing = SKPath.ParseSvgPathData("M 38,60 C 33,55 28,48 26,40 C 24,33 27,28 33,27 C 37,27 40,31 43,36 C 45,40 45,47 43,54 C 41,59 38,60 38,60 Z"))
        {
            DrawLocalPath(canvas, leftWing, L, T, fillGold, strokeGoldDark);
        }
        using (var rightWing = SKPath.ParseSvgPathData("M 62,60 C 67,55 72,48 74,40 C 76,33 73,28 67,27 C 63,27 60,31 57,36 C 55,40 55,47 57,54 C 59,59 62,60 62,60 Z"))
        {
            DrawLocalPath(canvas, rightWing, L, T, fillGold, strokeGoldDark);
        }

        // Cup body + highlight
        using (var cupBody = SKPath.ParseSvgPathData("M 33,60 C 33,48 36,38 42,34 C 45,32 49,28 50,25 L 50,25 C 51,28 55,32 58,34 C 64,38 67,48 67,60 C 67,67 65,72 62,75 L 38,75 C 35,72 33,67 33,60 Z"))
        {
            DrawLocalPath(canvas, cupBody, L, T, fillGold, strokeGoldDark);
        }
        using (var cupHighlight = SKPath.ParseSvgPathData("M 39,62 C 39,54 42,46 47,43 L 50,43 C 45,46 43,52 43,62 C 43,66 44,70 42,72 L 40,72 C 39,69 39,65 39,62 Z"))
        {
            DrawLocalPath(canvas, cupHighlight, L, T, fillWhite, null);
        }

        // Stem
        using (var stem = SKPath.ParseSvgPathData("M 45,75 L 55,75 L 53,90 L 51,96 L 49,96 L 47,90 Z"))
        {
            DrawLocalPath(canvas, stem, L, T, fillGold, strokeGoldDark);
        }
        // Stem ring
        canvas.DrawRoundRect(new SKRect(L(41), T(79), L(59), T(84)), s, s, fillGoldLight);
        canvas.DrawRoundRect(new SKRect(L(41), T(79), L(59), T(84)), s, s, strokeGoldDark);

        // Base — 5 tiers (alternating gold / malachite)
        canvas.DrawRoundRect(new SKRect(L(35), T(95), L(65), T(101)), s, s, fillGold);
        canvas.DrawRoundRect(new SKRect(L(35), T(95), L(65), T(101)), s, s, strokeGoldDark);
        canvas.DrawRoundRect(new SKRect(L(32), T(101), L(68), T(108)), s, s, fillMalachite);
        canvas.DrawRoundRect(new SKRect(L(29), T(108), L(71), T(114)), s, s, fillGold);
        canvas.DrawRoundRect(new SKRect(L(29), T(108), L(71), T(114)), s, s, strokeGoldDark);
        canvas.DrawRoundRect(new SKRect(L(26), T(114), L(74), T(121)), s, s, fillMalachite);
        canvas.DrawRoundRect(new SKRect(L(23), T(121), L(77), T(127)), 2 * s, 2 * s, fillGold);
        canvas.DrawRoundRect(new SKRect(L(23), T(121), L(77), T(127)), 2 * s, 2 * s, strokeGoldDark);
        // Bottom rim
        canvas.DrawRoundRect(new SKRect(L(21), T(127), L(79), T(130)), s, s, fillGoldDarkPaint);

        // Globe sparkle
        canvas.DrawOval(new SKRect(L(45), T(3), L(49), T(6)), sparklePaint);

        // Labels
        using var labelPaint = new SKPaint { Color = goldDark, TextSize = 12 * s, IsAntialias = true, Typeface = typeface, FakeBoldText = true, TextAlign = SKTextAlign.Center };
        canvas.DrawText("FIFA WORLD CUP", cx, T(147), labelPaint);
        using var sublabelPaint = new SKPaint { Color = SKColor.Parse("#888888"), TextSize = 10 * s, IsAntialias = true, Typeface = typeface, TextAlign = SKTextAlign.Center };
        canvas.DrawText("CHAMPION 2026™", cx, T(163), sublabelPaint);
    }

    private static void DrawLocalPath(SKCanvas canvas, SKPath localPath, Func<float, float> L, Func<float, float> T,
        SKPaint fill, SKPaint? stroke)
    {
        // Transform the path's local (SVG-space) coordinates into PDF space using L/T maps.
        using var transformed = new SKPath();
        using var iter = localPath.CreateRawIterator();
        var pts = new SKPoint[4];
        SKPathVerb verb;
        while ((verb = iter.Next(pts)) != SKPathVerb.Done)
        {
            switch (verb)
            {
                case SKPathVerb.Move:
                    transformed.MoveTo(L(pts[0].X), T(pts[0].Y));
                    break;
                case SKPathVerb.Line:
                    transformed.LineTo(L(pts[1].X), T(pts[1].Y));
                    break;
                case SKPathVerb.Cubic:
                    transformed.CubicTo(L(pts[1].X), T(pts[1].Y), L(pts[2].X), T(pts[2].Y), L(pts[3].X), T(pts[3].Y));
                    break;
                case SKPathVerb.Quad:
                    transformed.QuadTo(L(pts[1].X), T(pts[1].Y), L(pts[2].X), T(pts[2].Y));
                    break;
                case SKPathVerb.Close:
                    transformed.Close();
                    break;
            }
        }
        canvas.DrawPath(transformed, fill);
        if (stroke != null) canvas.DrawPath(transformed, stroke);
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
