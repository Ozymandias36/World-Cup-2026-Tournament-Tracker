using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows;
using WorldCup2026.Models;
using WorldCup2026.Services;

namespace WorldCup2026.ViewModels;

public partial class GroupStageViewModel : ObservableObject
{
    private readonly DataServiceAggregator _aggregator;

    public GroupStageViewModel(DataServiceAggregator aggregator)
    {
        _aggregator = aggregator;
        _aggregator.DataRefreshed += () => Application.Current.Dispatcher.Invoke(Refresh);
    }

    public List<Group> OrderedGroups => _aggregator.Groups
        .OrderBy(g => g.Name)
        .ToList();

    public Dictionary<string, List<Match>> GroupMatches => _aggregator.GetGroupStageMatches();

    public void Refresh()
    {
        OnPropertyChanged(nameof(OrderedGroups));
        OnPropertyChanged(nameof(GroupMatches));
    }
}
