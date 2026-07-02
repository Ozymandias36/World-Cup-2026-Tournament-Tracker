using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using WorldCup2026.Models;
using WorldCup2026.ViewModels;

namespace WorldCup2026.Views;

public partial class BracketView : UserControl
{
    private const double ColW = 165, MatchW = 150, MatchH = 54;
    private const double PairGap = 4, QuadGap = 12;
    private const double HalfGap = 300; // space between upper SF right edge and lower SF left edge

    private BracketViewModel? _vm;

    public BracketView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            _vm = DataContext as BracketViewModel;
            if (_vm != null) _vm.PropertyChanged += (_, _) => Dispatcher.Invoke(Render);
            Dispatcher.Invoke(Render);
        };
    }

    private void Render()
    {
        if (_vm == null) return;
        var (upper, lower) = _vm.GetSplitBracket();
        Canvas.Children.Clear();

        var stages = new[] { TournamentStage.RoundOf32, TournamentStage.RoundOf16,
            TournamentStage.QuarterFinal, TournamentStage.SemiFinal };

        // Collect rounds per half
        var upperRounds = new List<BracketRound>();
        var lowerRounds = new List<BracketRound>();
        foreach (var st in stages)
        {
            var up = upper.FirstOrDefault(r => r.Stage == st);
            var lo = lower.FirstOrDefault(r => r.Stage == st);
            if (up?.Matches.Count > 0) upperRounds.Add(up);
            if (lo?.Matches.Count > 0) lowerRounds.Add(lo);
        }

        var finalRound = upper.FirstOrDefault(r => r.Stage == TournamentStage.Final);
        var tpMatch = _vm.GetThirdPlaceMatch();

        if (upperRounds.Count == 0 && lowerRounds.Count == 0)
        { EmptyState.Visibility = Visibility.Visible; return; }
        EmptyState.Visibility = Visibility.Collapsed;

        var pos = new Dictionary<string, Point>();
        double startY = finalRound?.Matches.Count > 0 ? 200 : 20;

        // ── Upper half: left → right ──
        Layout(upperRounds, 10, startY, false, pos, "U");
        double upperRight = 10 + (upperRounds.Count - 1) * ColW + MatchW;

        // ── Lower half: right ← left (mirrored) ──
        double lowerStart = upperRight + HalfGap;
        Layout(lowerRounds, lowerStart, startY, true, pos, "L");
        double lowerLeft = lowerStart;

        // ── Connectors ──
        DrawConnectors(upperRounds, pos, "U");
        DrawConnectors(lowerRounds, pos, "L");

        // ── Nodes ──
        DrawNodes(upperRounds, pos, "U");
        DrawNodes(lowerRounds, pos, "L");

        // ── Final ──
        double fX = 0, fY = 0;
        if (finalRound?.Matches.Count > 0)
        {
            var sfU = upperRounds.Count > 0 ? pos[$"U|{upperRounds.Last().Stage}|{upperRounds.Last().Matches.Count - 1}"] : new Point(0, 300);
            var sfL = lowerRounds.Count > 0 ? pos[$"L|{lowerRounds.Last().Stage}|{lowerRounds.Last().Matches.Count - 1}"] : new Point(0, 300);

            fX = (upperRight + lowerLeft) / 2 - MatchW / 2;
            fY = (sfU.Y + sfL.Y) / 2;
            pos["Final|0"] = new Point(fX, fY);

            if (upperRounds.Count > 0) Conn(sfU.X + MatchW, sfU.Y + MatchH / 2, fX, fY + MatchH / 2);
            if (lowerRounds.Count > 0) Conn(lowerLeft + MatchW, sfL.Y + MatchH / 2, fX, fY + MatchH / 2);

            Node(finalRound.Matches[0], fX, fY, TournamentStage.Final);
        }

        // ── Third Place ──
        if (tpMatch != null && fX > 0)
        {
            double tX = fX, tY = fY + MatchH + 50;
            Node(tpMatch, tX, tY, TournamentStage.ThirdPlace);
        }

        // ── Size ──
        double r = lowerRounds.Count > 0 ? lowerStart + (lowerRounds.Count - 1) * ColW + MatchW : upperRight;
        double w = Math.Max(r, fX + MatchW) + 20;
        double h = Math.Max(pos.Values.Max(p => p.Y) + MatchH + 60, 400);
        Canvas.Width = w; Canvas.Height = h;

        // ── Trophy above Final ──
        if (fX > 0)
        {
            double tc = fX + MatchW / 2;
            double ty = fY - 190;
            RenderTrophy(tc, ty);
        }
    }

    // ── Layout a half ──
    private static void Layout(List<BracketRound> rounds, double x0, double y0, bool mirrored,
        Dictionary<string, Point> pos, string tag)
    {
        for (int ri = 0; ri < rounds.Count; ri++)
        {
            var rd = rounds[ri];
            int n = rd.Matches.Count;
            int co = mirrored ? rounds.Count - 1 - ri : ri;
            double x = x0 + co * ColW;
            double[] ys;

            if (ri == 0)
            {
                ys = new double[n]; double y = y0;
                for (int i = 0; i < n; i++)
                {
                    ys[i] = y; y += MatchH;
                    if (i % 2 == 1 && i < n - 1) y += QuadGap; else if (i < n - 1) y += PairGap;
                }
            }
            else
            {
                ys = new double[n];
                var prev = rounds[ri - 1];
                for (int i = 0; i < n; i++)
                {
                    int a = i * 2, b = i * 2 + 1;
                    double ay = 0, by = 0;
                    if (a < prev.Matches.Count && pos.TryGetValue($"{tag}|{prev.Stage}|{a}", out var pa)) ay = pa.Y;
                    if (b < prev.Matches.Count && pos.TryGetValue($"{tag}|{prev.Stage}|{b}", out var pb)) by = pb.Y;
                    ys[i] = (ay > 0 && by > 0) ? (ay + by) / 2 : (ay > 0 ? ay : by > 0 ? by : y0 + i * (MatchH + 20));
                }
            }
            for (int i = 0; i < n; i++) pos[$"{tag}|{rd.Stage}|{i}"] = new Point(x, ys[i]);
        }
    }

    private void DrawConnectors(List<BracketRound> rounds, Dictionary<string, Point> pos, string tag)
    {
        for (int ri = 1; ri < rounds.Count; ri++)
        {
            var prev = rounds[ri - 1]; var curr = rounds[ri];
            for (int i = 0; i < curr.Matches.Count; i++)
            {
                if (!pos.TryGetValue($"{tag}|{curr.Stage}|{i}", out var p)) continue;
                int a = i * 2, b = i * 2 + 1;
                if (pos.TryGetValue($"{tag}|{prev.Stage}|{a}", out var ca)) Conn(ca.X + MatchW, ca.Y + MatchH / 2, p.X, p.Y + MatchH / 2);
                if (pos.TryGetValue($"{tag}|{prev.Stage}|{b}", out var cb)) Conn(cb.X + MatchW, cb.Y + MatchH / 2, p.X, p.Y + MatchH / 2);
            }
        }
    }

    private void DrawNodes(List<BracketRound> rounds, Dictionary<string, Point> pos, string tag)
    {
        foreach (var rd in rounds)
            for (int i = 0; i < rd.Matches.Count; i++)
                if (pos.TryGetValue($"{tag}|{rd.Stage}|{i}", out var pt))
                    Node(rd.Matches[i], pt.X, pt.Y, rd.Stage);
    }

    // ── Drawing ──
    private void Conn(double x1, double y1, double x2, double y2)
    {
        double mx = (x1 + x2) / 2;
        var geo = new PathGeometry();
        var fig = new PathFigure { StartPoint = new Point(x1, y1) };
        fig.Segments.Add(new LineSegment(new Point(mx, y1), true));
        fig.Segments.Add(new LineSegment(new Point(mx, y2), true));
        fig.Segments.Add(new LineSegment(new Point(x2, y2), true));
        geo.Figures.Add(fig);
        Canvas.Children.Add(new Path { Stroke = new SolidColorBrush(Color.FromRgb(0xb0, 0xb0, 0xb0)), StrokeThickness = 1.2, Data = geo });
    }

    private void Node(Match m, double x, double y, TournamentStage stage)
    {
        var accent = stage switch
        {
            TournamentStage.RoundOf32 => Color.FromRgb(0x1a, 0x36, 0x5d),
            TournamentStage.RoundOf16 => Color.FromRgb(0x2a, 0x6f, 0x9d),
            TournamentStage.QuarterFinal => Color.FromRgb(0xc8, 0xa9, 0x51),
            TournamentStage.SemiFinal => Color.FromRgb(0xd4, 0x6a, 0x0e),
            TournamentStage.Final => Color.FromRgb(0xd0, 0x00, 0x00),
            TournamentStage.ThirdPlace => Color.FromRgb(0x8b, 0x45, 0x13),
            _ => Colors.Gray
        };
        var bg = m.IsFinished ? Color.FromRgb(0xf8, 0xf8, 0xf8) : Colors.White;
        var b = new Border { Width = MatchW, Background = new SolidColorBrush(bg), BorderBrush = new SolidColorBrush(accent), BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(4), Tag = m };
        var p = new StackPanel { Margin = new Thickness(4, 2, 4, 2) };
        p.Children.Add(Row(m, true));
        p.Children.Add(new Rectangle { Height = 1, Fill = Brushes.LightGray, Margin = new Thickness(0, 1, 0, 1) });
        p.Children.Add(Row(m, false));
        if (m.HasPenalties) p.Children.Add(new TextBlock { Text = $"(p {m.HomePenalties}-{m.AwayPenalties})", FontSize = 8, Foreground = Brushes.Gray });

        // Beijing time
        if (m.DateTime.HasValue)
        {
            double off = m.UtcOffsetHours ?? 0;
            var bjt = m.DateTime.Value.AddHours(-off + 8);
            p.Children.Add(new Rectangle { Height = 1, Fill = Brushes.LightGray, Margin = new Thickness(0, 2, 0, 0) });
            p.Children.Add(new TextBlock { Text = $"北京 {bjt:MM/dd HH:mm}", FontSize = 8, Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)), TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 1, 0, 0) });
        }

        b.Child = p;
        Canvas.SetLeft(b, x); Canvas.SetTop(b, y);
        Canvas.Children.Add(b);
    }

    private static FrameworkElement Row(Match m, bool home)
    {
        var code = home ? m.HomeTeamCode : m.AwayTeamCode;
        var name = home ? m.HomeTeamName : m.AwayTeamName;
        var score = home ? m.HomeScore : m.AwayScore;
        var oppScore = home ? m.AwayScore : m.HomeScore;
        var isWin = score.HasValue && oppScore.HasValue && score > oppScore;
        var label = !string.IsNullOrEmpty(name) ? name : (!string.IsNullOrEmpty(code) ? code : "—");

        var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var flag = Helpers.FlagHelper.CreateFlagImage(code, 20, 14);
        if (flag != null) { Grid.SetColumn(flag, 0); row.Children.Add(flag); }

        var txt = new TextBlock { Text = label, FontSize = 11, FontWeight = isWin ? FontWeights.Bold : FontWeights.Normal, Foreground = isWin ? new SolidColorBrush(Colors.DarkGreen) : Brushes.Black, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
        Grid.SetColumn(txt, 1); row.Children.Add(txt);

        var sc = new TextBlock { Text = score?.ToString() ?? "—", FontSize = 11, FontWeight = FontWeights.Bold, Width = 22, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(sc, 2); row.Children.Add(sc);
        return row;
    }

    private void RenderTrophy(double cx, double cy)
    {
        var g = new SolidColorBrush(Color.FromRgb(0xd4, 0xb0, 0x42));
        var gd = new SolidColorBrush(Color.FromRgb(0xb0, 0x8a, 0x28));
        var gl = new SolidColorBrush(Color.FromRgb(0xf0, 0xd8, 0x68));
        var mg = new SolidColorBrush(Color.FromRgb(0x2e, 0x6b, 0x4e));
        var c = new Canvas { Width = 100, Height = 170 };

        // Globe
        c.Children.Add(new Ellipse { Width = 18, Height = 18, Fill = gl, Stroke = gd, StrokeThickness = 1 }); Canvas.SetLeft(c.Children[^1], 41); Canvas.SetTop(c.Children[^1], 0);
        // Globe band
        c.Children.Add(new Rectangle { Width = 18, Height = 4, Fill = g, Opacity = 0.5 }); Canvas.SetLeft(c.Children[^1], 41); Canvas.SetTop(c.Children[^1], 7);
        // Globe stem
        c.Children.Add(new Rectangle { Width = 2, Height = 5, Fill = gd }); Canvas.SetLeft(c.Children[^1], 49); Canvas.SetTop(c.Children[^1], 18);

        // Left wing figure
        c.Children.Add(new Path { Data = Geometry.Parse("M 38,60 C 33,55 28,48 26,40 C 24,33 27,28 33,27 C 37,27 40,31 43,36 C 45,40 45,47 43,54 C 41,59 38,60 38,60 Z"), Fill = g, Stroke = gd, StrokeThickness = 0.7 });
        // Right wing figure
        c.Children.Add(new Path { Data = Geometry.Parse("M 62,60 C 67,55 72,48 74,40 C 76,33 73,28 67,27 C 63,27 60,31 57,36 C 55,40 55,47 57,54 C 59,59 62,60 62,60 Z"), Fill = g, Stroke = gd, StrokeThickness = 0.7 });

        // Cup body
        c.Children.Add(new Path { Data = Geometry.Parse("M 33,60 C 33,48 36,38 42,34 C 45,32 49,28 50,25 L 50,25 C 51,28 55,32 58,34 C 64,38 67,48 67,60 C 67,67 65,72 62,75 L 38,75 C 35,72 33,67 33,60 Z"), Fill = g, Stroke = gd, StrokeThickness = 1 });
        // Cup highlight
        c.Children.Add(new Path { Data = Geometry.Parse("M 39,62 C 39,54 42,46 47,43 L 50,43 C 45,46 43,52 43,62 C 43,66 44,70 42,72 L 40,72 C 39,69 39,65 39,62 Z"), Fill = gl, Opacity = 0.3 });

        // Stem
        c.Children.Add(new Path { Data = Geometry.Parse("M 45,75 L 55,75 L 53,90 L 51,96 L 49,96 L 47,90 Z"), Fill = g, Stroke = gd, StrokeThickness = 0.7 });
        // Stem ring
        c.Children.Add(new Rectangle { Width = 18, Height = 5, Fill = gl, Stroke = gd, StrokeThickness = 0.7, RadiusX = 1, RadiusY = 1 }); Canvas.SetLeft(c.Children[^1], 41); Canvas.SetTop(c.Children[^1], 79);

        // Base — 5 tiers (gold + malachite)
        c.Children.Add(new Rectangle { Width = 30, Height = 6, Fill = g, Stroke = gd, StrokeThickness = 0.7, RadiusX = 1, RadiusY = 1 }); Canvas.SetLeft(c.Children[^1], 35); Canvas.SetTop(c.Children[^1], 95);
        c.Children.Add(new Rectangle { Width = 36, Height = 7, Fill = mg, RadiusX = 1, RadiusY = 1 }); Canvas.SetLeft(c.Children[^1], 32); Canvas.SetTop(c.Children[^1], 101);
        c.Children.Add(new Rectangle { Width = 42, Height = 6, Fill = g, Stroke = gd, StrokeThickness = 0.7, RadiusX = 1, RadiusY = 1 }); Canvas.SetLeft(c.Children[^1], 29); Canvas.SetTop(c.Children[^1], 108);
        c.Children.Add(new Rectangle { Width = 48, Height = 7, Fill = mg, RadiusX = 1, RadiusY = 1 }); Canvas.SetLeft(c.Children[^1], 26); Canvas.SetTop(c.Children[^1], 114);
        c.Children.Add(new Rectangle { Width = 54, Height = 6, Fill = g, Stroke = gd, StrokeThickness = 0.7, RadiusX = 2, RadiusY = 2 }); Canvas.SetLeft(c.Children[^1], 23); Canvas.SetTop(c.Children[^1], 121);
        // Bottom rim
        c.Children.Add(new Rectangle { Width = 58, Height = 3, Fill = gd, RadiusX = 1, RadiusY = 1 }); Canvas.SetLeft(c.Children[^1], 21); Canvas.SetTop(c.Children[^1], 127);

        // Globe sparkle
        c.Children.Add(new Ellipse { Width = 4, Height = 3, Fill = Brushes.White, Opacity = 0.5 }); Canvas.SetLeft(c.Children[^1], 45); Canvas.SetTop(c.Children[^1], 3);

        Canvas.SetLeft(c, cx - 50); Canvas.SetTop(c, cy); Canvas.Children.Add(c);

        // Labels
        var t1 = new TextBlock { Text = "FIFA WORLD CUP", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = gd, Width = 180, TextAlignment = TextAlignment.Center };
        Canvas.SetLeft(t1, cx - 90); Canvas.SetTop(t1, cy + 134); Canvas.Children.Add(t1);
        var t2 = new TextBlock { Text = "CHAMPION 2026™", FontSize = 10, Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)), Width = 180, TextAlignment = TextAlignment.Center };
        Canvas.SetLeft(t2, cx - 90); Canvas.SetTop(t2, cy + 150); Canvas.Children.Add(t2);
    }
}
