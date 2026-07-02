using System.Windows;
using System.Windows.Controls;
using WorldCup2026.ViewModels;

namespace WorldCup2026.Views;

/// <summary>
/// Displays tournament statistics with charts and data grids.
/// </summary>
public partial class StatisticsView : UserControl
{
    private StatisticsViewModel? _viewModel;

    public StatisticsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        _viewModel = DataContext as StatisticsViewModel;
        if (_viewModel != null)
        {
            _viewModel.PropertyChanged += (s, args) =>
            {
                Dispatcher.Invoke(UpdateVisibility);
            };
            Dispatcher.Invoke(UpdateVisibility);
        }
    }

    private void UpdateVisibility()
    {
        if (_viewModel == null) return;

        var hasData = _viewModel.TopScorers.Count > 0;

        EmptyState.Visibility = hasData ? Visibility.Collapsed : Visibility.Visible;
        StatsTabControl.Visibility = hasData ? Visibility.Visible : Visibility.Collapsed;
    }
}
