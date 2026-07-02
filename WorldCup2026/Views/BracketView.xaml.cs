using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using WorldCup2026.Models;
using WorldCup2026.ViewModels;

namespace WorldCup2026.Views;

public partial class BracketView : UserControl
{
    // Layout
    private const double ColW = 175, MatchW = 155, MatchH = 54;
    private const double PairGap = 4,   // gap between matches that share same parent
                       QuadGap = 12,   // gap between groups of 4
                       HalfGap = 24,   // gap between upper and lower halves
                       MatchGap = 2;   // tiny gap within a pair
    private BracketViewModel? _vm;

    // All matches in bracket tree order, with pairing info
    private record Node(int Id, TournamentStage Stage, int? ParentIndex);

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

        // Flatten all matches into a single tree ordered list
        var allRounds = new List<BracketRound>();
        var stages = new[] { TournamentStage.RoundOf32, TournamentStage.RoundOf16,
            TournamentStage.QuarterFinal, TournamentStage.SemiFinal };
        foreach (var st in stages)
        {
            var up = upper.FirstOrDefault(r => r.Stage == st);
            var lo = lower.FirstOrDefault(r => r.Stage == st);
            var combined = (up?.Matches ?? new()).Concat(lo?.Matches ?? new()).ToList();
            if (combined.Count > 0)
                allRounds.Add(new BracketRound { Stage = st, Label = st.ToString(), Matches = combined });
        }

        if (allRounds.Count == 0) { EmptyState.Visibility = Visibility.Visible; return; }
        EmptyState.Visibility = Visibility.Collapsed;

        // Layout: column 0 = R32, col 1 = R16, col 2 = QF, col 3 = SF
        var positions = new Dictionary<string, Point>();

        foreach (var round in allRounds)
        {
            int n = round.Matches.Count;
            int col = round.Stage switch
            {
                TournamentStage.RoundOf32 => 0,
                TournamentStage.RoundOf16 => 1,
                TournamentStage.QuarterFinal => 2,
                TournamentStage.SemiFinal => 3,
                _ => 0
            };

            double[] ys;
            if (col == 0)
            {
                // R32: manual vertical positioning with grouping
                ys = new double[n];
                double y = 20;
                // half sizes: upper = 8 matches, lower = 8 matches
                int upN = Math.Min(8, n);
                int loN = n - upN;
                // Upper half: 8 matches in 4 pairs
                for (int i = 0; i < upN; i++)
                {
                    ys[i] = y; y += MatchH;
                    if (i % 2 == 1 && i < upN - 1) y += QuadGap; // gap between pairs
                    else if (i < upN - 1) y += PairGap;
                }
                y += HalfGap;
                // Lower half
                for (int i = 0; i < loN; i++)
                {
                    ys[upN + i] = y; y += MatchH;
                    if (i % 2 == 1 && i < loN - 1) y += QuadGap;
                    else if (i < loN - 1) y += PairGap;
                }
            }
            else
            {
                // Centered between children
                ys = new double[n];
                var prevRound = allRounds[col - 1];
                for (int i = 0; i < n; i++)
                {
                    int a = i * 2, b = i * 2 + 1;
                    int prevN = prevRound.Matches.Count;
                    double ay = 0, by = 0;
                    if (a < prevN && positions.TryGetValue($"{prevRound.Stage}|{a}", out var pa)) ay = pa.Y;
                    if (b < prevN && positions.TryGetValue($"{prevRound.Stage}|{b}", out var pb)) by = pb.Y;
                    ys[i] = (ay > 0 && by > 0) ? (ay + by) / 2 :
                            (ay > 0) ? ay : (by > 0) ? by : 20 + i * (MatchH + 20);
                }
            }

            for (int i = 0; i < n; i++)
                positions[$"{round.Stage}|{i}"] = new Point(col * ColW + 10, ys[i]);
        }

        // Draw connectors first (behind nodes)
        for (int c = 1; c < allRounds.Count; c++)
        {
            var prev = allRounds[c - 1];
            var curr = allRounds[c];
            for (int i = 0; i < curr.Matches.Count; i++)
            {
                int a = i * 2, b = i * 2 + 1;
                if (positions.TryGetValue($"{curr.Stage}|{i}", out var parent))
                {
                    if (positions.TryGetValue($"{prev.Stage}|{a}", out var ca))
                        Canvas.Children.Add(MakeLine(ca.X + MatchW, ca.Y + MatchH / 2, parent.X, parent.Y + MatchH / 2));
                    if (positions.TryGetValue($"{prev.Stage}|{b}", out var cb))
                        Canvas.Children.Add(MakeLine(cb.X + MatchW, cb.Y + MatchH / 2, parent.X, parent.Y + MatchH / 2));
                }
            }
        }

        // Draw match nodes
        foreach (var round in allRounds)
        {
            for (int i = 0; i < round.Matches.Count; i++)
            {
                if (positions.TryGetValue($"{round.Stage}|{i}", out var pt))
                    Canvas.Children.Add(MakeNode(round.Matches[i], pt.X, pt.Y, round.Stage));
            }
        }

        double totalH = positions.Values.Max(p => p.Y) + MatchH + 60;
        double totalW = allRounds.Count * ColW + 200;
        Canvas.Width = totalW; Canvas.Height = totalH;

        // Trophy after final column
        double cx = (allRounds.Count - 1) * ColW + MatchW + 30;
        double allCY = positions.Values.Where(p => p.X > (allRounds.Count - 2) * ColW).Select(p => p.Y).DefaultIfEmpty(totalH / 2 - 80).Average();
        RenderTrophy(cx, allCY);
    }

    private static Path MakeLine(double x1, double y1, double x2, double y2)
    {
        double mx = (x1 + x2) / 2;
        var geo = new PathGeometry();
        var fig = new PathFigure { StartPoint = new Point(x1, y1) };
        fig.Segments.Add(new LineSegment(new Point(mx, y1), true));
        fig.Segments.Add(new LineSegment(new Point(mx, y2), true));
        fig.Segments.Add(new LineSegment(new Point(x2, y2), true));
        geo.Figures.Add(fig);
        return new Path { Stroke = new SolidColorBrush(Color.FromRgb(0xb0, 0xb0, 0xb0)), StrokeThickness = 1.2, Data = geo };
    }

    private static Border MakeNode(Match m, double x, double y, TournamentStage stage)
    {
        var accent = stage switch
        {
            TournamentStage.RoundOf32 => Color.FromRgb(0x1a, 0x36, 0x5d),
            TournamentStage.RoundOf16 => Color.FromRgb(0x2a, 0x6f, 0x9d),
            TournamentStage.QuarterFinal => Color.FromRgb(0xc8, 0xa9, 0x51),
            TournamentStage.SemiFinal => Color.FromRgb(0xd4, 0x6a, 0x0e),
            TournamentStage.Final => Color.FromRgb(0xd0, 0x00, 0x00),
            _ => Colors.Gray
        };
        var bg = m.IsFinished ? Color.FromRgb(0xf8, 0xf8, 0xf8) : Colors.White;
        var border = new Border { Width = MatchW, Background = new SolidColorBrush(bg), BorderBrush = new SolidColorBrush(accent), BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(4), Tag = m };
        var panel = new StackPanel { Margin = new Thickness(4, 2, 4, 2) };
        panel.Children.Add(MakeRow(m, true));
        panel.Children.Add(new Rectangle { Height = 1, Fill = Brushes.LightGray, Margin = new Thickness(0, 1, 0, 1) });
        panel.Children.Add(MakeRow(m, false));
        if (m.HasPenalties) panel.Children.Add(new TextBlock { Text = $"(p {m.HomePenalties}-{m.AwayPenalties})", FontSize = 8, Foreground = Brushes.Gray });
        border.Child = panel;
        Canvas.SetLeft(border, x); Canvas.SetTop(border, y);
        return border;
    }

    private static FrameworkElement MakeRow(Match m, bool home)
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
