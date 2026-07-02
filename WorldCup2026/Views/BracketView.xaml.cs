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

        // ── Upper half: left → right ──
        Layout(upperRounds, 10, 20, false, pos, "U");
        double upperRight = 10 + (upperRounds.Count - 1) * ColW + MatchW;

        // ── Lower half: right ← left (mirrored) ──
        double lowerStart = upperRight + HalfGap;
        Layout(lowerRounds, lowerStart, 20, true, pos, "L");
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
        double w = Math.Max(r, fX + MatchW) + 200;
        double h = Math.Max(pos.Values.Max(p => p.Y) + MatchH + 60, 500);
        Canvas.Width = w; Canvas.Height = h;

        // ── Trophy ──
        double tx = fX > 0 ? fX + MatchW + 40 : r + 30;
        double ty = fY > 0 ? fY - 130 : h / 2 - 100;
        RenderTrophy(tx, ty);
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
        var score = home ? m.HomeScore : m.AwayScore;
        var oppScore = home ? m.AwayScore : m.HomeScore;
        var isWin = score.HasValue && oppScore.HasValue && score > oppScore;
        var label = !string.IsNullOrEmpty(code) ? code : "—";

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
        var t = new Canvas { Width = 180, Height = 130 };
        t.Children.Add(new Rectangle { Width = 50, Height = 8, Fill = Gold(), RadiusX = 2, RadiusY = 2 }); Canvas.SetLeft(t.Children[^1], 65); Canvas.SetTop(t.Children[^1], 117);
        t.Children.Add(new Rectangle { Width = 8, Height = 40, Fill = Gold() }); Canvas.SetLeft(t.Children[^1], 86); Canvas.SetTop(t.Children[^1], 77);
        t.Children.Add(new Ellipse { Width = 55, Height = 50, Fill = Gold(), Stroke = DarkGold(), StrokeThickness = 2 }); Canvas.SetLeft(t.Children[^1], 62); Canvas.SetTop(t.Children[^1], 28);
        t.Children.Add(new Ellipse { Width = 30, Height = 30, Stroke = DarkGold(), StrokeThickness = 1 }); Canvas.SetLeft(t.Children[^1], 75); Canvas.SetTop(t.Children[^1], 38);
        t.Children.Add(new Ellipse { Width = 22, Height = 24, Fill = Gold(), Stroke = DarkGold(), StrokeThickness = 1.5 }); Canvas.SetLeft(t.Children[^1], 79); Canvas.SetTop(t.Children[^1], 5);
        Canvas.SetLeft(t, cx); Canvas.SetTop(t, cy); Canvas.Children.Add(t);
        var lb = new TextBlock { Text = "CHAMPION", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = Gold(), Width = 180, TextAlignment = TextAlignment.Center };
        Canvas.SetLeft(lb, cx); Canvas.SetTop(lb, cy + 135); Canvas.Children.Add(lb);
    }
    private static SolidColorBrush Gold() => new(Color.FromRgb(0xc8, 0xa9, 0x51));
    private static SolidColorBrush DarkGold() => new(Color.FromRgb(0xa0, 0x85, 0x35));
}
