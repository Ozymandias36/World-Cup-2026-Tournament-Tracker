using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using System.Windows;
using WorldCup2026.Models;
using WorldCup2026.Services;

namespace WorldCup2026.ViewModels;

public partial class StatisticsViewModel : ObservableObject
{
    private readonly DataServiceAggregator _aggregator;

    public StatisticsViewModel(DataServiceAggregator aggregator)
    {
        _aggregator = aggregator;
        _aggregator.DataRefreshed += () => Application.Current.Dispatcher.Invoke(Refresh);
    }

    public ISeries[] GoalScorersSeries
    {
        get
        {
            var top10 = _aggregator.PlayerStats
                .OrderByDescending(s => s.Goals)
                .Take(10)
                .ToList();

            return new ISeries[]
            {
                new ColumnSeries<PlayerStat>
                {
                    Values = top10,
                    Mapping = (stat, index) => new(stat.Goals, stat.Goals),
                    Name = "Goals",
                    Fill = new SolidColorPaint(SKColors.DarkGreen)
                }
            };
        }
    }

    public Axis[] XAxes => new Axis[]
    {
        new Axis
        {
            Labels = _aggregator.PlayerStats
                .OrderByDescending(s => s.Goals)
                .Take(10)
                .Select(s => s.PlayerName)
                .ToArray(),
            LabelsRotation = 45,
            TextSize = 12
        }
    };

    public ISeries[] TeamGoalsSeries
    {
        get
        {
            var top10 = _aggregator.TeamStats
                .OrderByDescending(t => t.GoalsFor)
                .Take(10)
                .ToList();

            return new ISeries[]
            {
                new RowSeries<TeamStat>
                {
                    Values = top10,
                    Mapping = (stat, index) => new(stat.GoalsFor, stat.GoalsFor),
                    Name = "Goals For",
                    Fill = new SolidColorPaint(SKColor.Parse("#1a365d"))
                }
            };
        }
    }

    public List<PlayerStat> TopScorers => _aggregator.PlayerStats
        .OrderByDescending(s => s.Goals)
        .ThenByDescending(s => s.Assists)
        .Take(20)
        .ToList();

    public List<PlayerStat> TopAssists => _aggregator.PlayerStats
        .OrderByDescending(s => s.Assists)
        .ThenByDescending(s => s.Goals)
        .Take(20)
        .ToList();

    public List<PlayerStat> MostCards => _aggregator.PlayerStats
        .OrderByDescending(s => s.YellowCards + s.RedCards * 2)
        .Take(20)
        .ToList();

    public void Refresh()
    {
        OnPropertyChanged(nameof(GoalScorersSeries));
        OnPropertyChanged(nameof(XAxes));
        OnPropertyChanged(nameof(TeamGoalsSeries));
        OnPropertyChanged(nameof(TopScorers));
        OnPropertyChanged(nameof(TopAssists));
        OnPropertyChanged(nameof(MostCards));
    }
}
