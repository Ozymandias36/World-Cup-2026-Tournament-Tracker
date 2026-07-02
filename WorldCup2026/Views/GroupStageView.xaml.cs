using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using WorldCup2026.Models;
using WorldCup2026.Services;
using WorldCup2026.ViewModels;

namespace WorldCup2026.Views;

/// <summary>
/// Displays group stage standings and match results for all 12 groups.
/// </summary>
public partial class GroupStageView : UserControl
{
    private GroupStageViewModel? _viewModel;

    public GroupStageView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        LocalizationService.LanguageChanged += () => Dispatcher.Invoke(RenderGroups);
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        _viewModel = DataContext as GroupStageViewModel;
        if (_viewModel != null)
        {
            _viewModel.PropertyChanged += (s, args) =>
            {
                if (args.PropertyName is nameof(GroupStageViewModel.OrderedGroups)
                    or nameof(GroupStageViewModel.GroupMatches))
                {
                    Dispatcher.Invoke(RenderGroups);
                }
            };
            Dispatcher.Invoke(RenderGroups);
        }
    }

    private void RenderGroups()
    {
        if (_viewModel == null) return;

        var groups = _viewModel.OrderedGroups;
        if (groups.Count == 0)
        {
            EmptyState.Text = LocalizationService.T("WaitingTournament");
            EmptyState.Visibility = Visibility.Visible;
            GroupsTabControl.Visibility = Visibility.Collapsed;
            MatchesPanel.Visibility = Visibility.Collapsed;
            return;
        }

        EmptyState.Visibility = Visibility.Collapsed;
        GroupsTabControl.Visibility = Visibility.Visible;
        MatchesPanel.Visibility = Visibility.Visible;
        GroupMatchesTitle.Text = LocalizationService.T("GroupMatches");

        GroupsTabControl.Items.Clear();

        foreach (var group in groups)
        {
            // Tag holds the raw group letter (language-independent) so selection
            // handling never has to parse the localized header text back apart.
            var tabItem = new TabItem { Header = LocalizationService.GroupLabel(group.Name), Tag = group.Name };

            var grid = CreateStandingsGrid(group);
            tabItem.Content = grid;
            GroupsTabControl.Items.Add(tabItem);
        }

        // Select first tab and show its matches
        if (GroupsTabControl.Items.Count > 0)
        {
            GroupsTabControl.SelectedIndex = 0;
            GroupsTabControl.SelectionChanged -= OnGroupSelectionChanged; // avoid stacking handlers on re-render
            GroupsTabControl.SelectionChanged += OnGroupSelectionChanged;
            ShowGroupMatches(groups[0].Name);
        }
    }

    private static Grid CreateStandingsGrid(Group group)
    {
        var grid = new Grid { Margin = new Thickness(8) };

        // Columns: Flag | # | Team | P | W | D | L | GF | GA | GD | Pts
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });     // Flag (new)
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });     // #
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Team
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });     // P
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });     // W
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });     // D
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });     // L
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });     // GF
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });     // GA
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });     // GD
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });     // Pts

        // Header
        var headers = new[]
        {
            "", LocalizationService.T("ColPos"), LocalizationService.T("ColTeam"),
            LocalizationService.T("ColPlayed"), LocalizationService.T("ColWin"), LocalizationService.T("ColDraw"), LocalizationService.T("ColLoss"),
            LocalizationService.T("ColGF"), LocalizationService.T("ColGA"), LocalizationService.T("ColGD"), LocalizationService.T("ColPts")
        };
        for (int c = 0; c < headers.Length; c++)
        {
            var cell = CreateCell(headers[c], FontWeights.Bold, 11, Colors.White,
                new SolidColorBrush(Color.FromRgb(0x1a, 0x36, 0x5d)),
                c == 2 ? HorizontalAlignment.Left : HorizontalAlignment.Center);
            Grid.SetRow(cell, 0);
            Grid.SetColumn(cell, c);
            grid.Children.Add(cell);
        }

        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Data rows
        foreach (var standing in group.Standings)
        {
            var rowIndex = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Top 2 always qualify; a 3rd-place team qualifies only if it's among the
            // best 8 third-placed teams across all groups (marked via IsQualified).
            var isTop2 = standing.Position <= 2;
            var isBestThird = standing.Position == 3 && standing.IsQualified;
            var isQualified = isTop2 || isBestThird;
            var bg = rowIndex % 2 == 0 ? Colors.White : Color.FromRgb(0xf5, 0xf5, 0xf5);
            if (isQualified) bg = Color.FromRgb(0xe8, 0xf5, 0xe8);

            var values = new[]
            {
                standing.Position.ToString(),
                LocalizationService.TeamName(standing.TeamName, standing.TeamCode),
                standing.Played.ToString(),
                standing.Wins.ToString(),
                standing.Draws.ToString(),
                standing.Losses.ToString(),
                standing.GoalsFor.ToString(),
                standing.GoalsAgainst.ToString(),
                standing.GoalDifference.ToString("+0;-0;0"),
                standing.Points.ToString()
            };

            for (int c = 0; c < values.Length; c++)
            {
                var fw = c == 0 || c == values.Length - 1 ? FontWeights.Bold : FontWeights.Normal;
                var fgColor = (isQualified && c == 0) ? Color.FromRgb(0x2d, 0x6a, 0x4f) : Colors.Black;
                var cell = CreateCell(values[c], fw, 12, fgColor,
                    new SolidColorBrush(bg),
                    c == 1 ? HorizontalAlignment.Left : HorizontalAlignment.Center);

                if (c == 2) cell.Margin = new Thickness(8, 0, 0, 0);

                Grid.SetRow(cell, rowIndex);
                Grid.SetColumn(cell, c + 1); // +1 because column 0 is flag
                grid.Children.Add(cell);
            }

            // Flag image in column 0
            var flagImg = Helpers.FlagHelper.CreateFlagImage(standing.TeamCode, 24, 16);
            if (flagImg != null) { flagImg.VerticalAlignment = VerticalAlignment.Center; flagImg.Margin = new Thickness(0); }
            var flagCell = new Border
            {
                Background = new SolidColorBrush(bg),
                Padding = new Thickness(6, 3, 4, 3),
                Child = (System.Windows.FrameworkElement?)flagImg ?? new TextBlock { Text = "", FontSize = 10 }
            };
            flagCell.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(flagCell, rowIndex);
            Grid.SetColumn(flagCell, 0);
            grid.Children.Add(flagCell);
        }

        return grid;
    }

    private static Border CreateCell(string text, FontWeight fontWeight, double fontSize,
        Color foreground, Brush background, HorizontalAlignment hAlign, bool isHeader = false)
    {
        return new Border
        {
            Background = background,
            Padding = new Thickness(6, 3, 6, 3),
            Child = new TextBlock
            {
                Text = text,
                FontSize = fontSize,
                FontWeight = fontWeight,
                Foreground = new SolidColorBrush(foreground),
                HorizontalAlignment = hAlign
            }
        };
    }

    private void OnGroupSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GroupsTabControl.SelectedItem is TabItem { Tag: string groupName })
            ShowGroupMatches(groupName);
    }

    private void ShowGroupMatches(string groupName)
    {
        if (_viewModel == null) return;

        var groupMatches = _viewModel.GroupMatches;
        if (groupMatches.TryGetValue(groupName, out var matches))
        {
            MatchesList.ItemsSource = matches;
            MatchesPanel.Visibility = Visibility.Visible;
        }
        else
        {
            MatchesPanel.Visibility = Visibility.Collapsed;
        }
    }
}
