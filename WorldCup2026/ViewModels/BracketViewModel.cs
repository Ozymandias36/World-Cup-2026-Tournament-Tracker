using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows;
using WorldCup2026.Models;
using WorldCup2026.Services;

namespace WorldCup2026.ViewModels;

public partial class BracketViewModel : ObservableObject
{
    private readonly DataServiceAggregator _aggregator;

    // 2026 FIFA bracket tree:
    // SF101 ← QF[97,98] ← R16[89,90,93,94]  → half A (upper)
    // SF102 ← QF[99,100] ← R16[91,92,95,96] → half B (lower)
    // R32 order: paired by R16 parent, grouped by QF, separated by SF
    private static readonly int[] UpperR32Order = { 74,77, 73,75,  83,84, 81,82 };
    private static readonly int[] LowerR32Order = { 76,78, 79,80,  86,88, 85,87 };
    private static readonly int[] UpperR16Order = { 89,90, 93,94 };
    private static readonly int[] LowerR16Order = { 91,92, 95,96 };
    private static readonly int[] UpperQfOrder   = { 97, 98 };
    private static readonly int[] LowerQfOrder   = { 99, 100 };
    private static readonly int[] UpperSfOrder   = { 101 };
    private static readonly int[] LowerSfOrder   = { 102 };

    public BracketViewModel(DataServiceAggregator aggregator)
    {
        _aggregator = aggregator;
        _aggregator.DataRefreshed += () => Application.Current.Dispatcher.Invoke(Refresh);
    }

    /// <summary>
    /// Get real knockout matches from data, organized into bracket halves.
    /// </summary>
    public (List<BracketRound> upperHalf, List<BracketRound> lowerHalf) GetSplitBracket()
    {
        var allMatches = _aggregator.Matches
            .Where(m => m.Stage != TournamentStage.GroupStage)
            .ToDictionary(m => m.Id, m => m);

        Match? Find(int id) => allMatches.GetValueOrDefault(id);

        var upper = new List<BracketRound>();
        var lower = new List<BracketRound>();

        // R32
        var r32Up = UpperR32Order.Select(Find).Where(m => m != null).Cast<Match>().ToList();
        var r32Lo = LowerR32Order.Select(Find).Where(m => m != null).Cast<Match>().ToList();
        if (r32Up.Count > 0) upper.Add(new BracketRound { Stage = TournamentStage.RoundOf32, Label = "R32", Matches = r32Up });
        if (r32Lo.Count > 0) lower.Add(new BracketRound { Stage = TournamentStage.RoundOf32, Label = "R32", Matches = r32Lo });

        // R16
        var r16Up = UpperR16Order.Select(Find).Where(m => m != null).Cast<Match>().ToList();
        var r16Lo = LowerR16Order.Select(Find).Where(m => m != null).Cast<Match>().ToList();
        if (r16Up.Count > 0) upper.Add(new BracketRound { Stage = TournamentStage.RoundOf16, Label = "R16", Matches = r16Up });
        if (r16Lo.Count > 0) lower.Add(new BracketRound { Stage = TournamentStage.RoundOf16, Label = "R16", Matches = r16Lo });

        // QF
        var qfUp = UpperQfOrder.Select(Find).Where(m => m != null).Cast<Match>().ToList();
        var qfLo = LowerQfOrder.Select(Find).Where(m => m != null).Cast<Match>().ToList();
        if (qfUp.Count > 0) upper.Add(new BracketRound { Stage = TournamentStage.QuarterFinal, Label = "QF", Matches = qfUp });
        if (qfLo.Count > 0) lower.Add(new BracketRound { Stage = TournamentStage.QuarterFinal, Label = "QF", Matches = qfLo });

        // SF
        var sfUp = UpperSfOrder.Select(Find).Where(m => m != null).Cast<Match>().ToList();
        var sfLo = LowerSfOrder.Select(Find).Where(m => m != null).Cast<Match>().ToList();
        if (sfUp.Count > 0) upper.Add(new BracketRound { Stage = TournamentStage.SemiFinal, Label = "SF", Matches = sfUp });
        if (sfLo.Count > 0) lower.Add(new BracketRound { Stage = TournamentStage.SemiFinal, Label = "SF", Matches = sfLo });

        // Final (shared)
        var final = Find(104);
        if (final != null)
        {
            var fl = new List<Match> { final };
            upper.Add(new BracketRound { Stage = TournamentStage.Final, Label = "Final", Matches = fl });
        }

        return (upper, lower);
    }

    public void Refresh() => OnPropertyChanged(string.Empty); // triggers all bindings
}

public class BracketRound
{
    public TournamentStage Stage { get; set; }
    public string Label { get; set; } = string.Empty;
    public List<Match> Matches { get; set; } = new();
}
