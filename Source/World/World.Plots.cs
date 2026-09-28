namespace GardenGuardians;

/// <summary>A splinter in the making: a disloyal member quietly gathering the discontented before it leads them out (see World.Plots).</summary>
public sealed class Plot
{
    public Plot(Bramblekin instigator) => Instigator = instigator;

    public Bramblekin Instigator { get; }

    public List<Bramblekin> Conspirators { get; } = new();

    /// <summary>Leader decisions it has survived undiscovered.</summary>
    public int Decisions { get; set; }
}

public sealed partial class World
{
    /// <summary>A plot needs this many Leader decisions undiscovered…</summary>
    private const int PlotMaturity = 3;

    /// <summary>…and at least this many conspirators besides its instigator, before it leads them out.</summary>
    private const int PlotMinConspirators = 1;

    /// <summary>Each decision, a Leader uncovers a plot with odds this × (0.5 + its Intelligence) × (1 + conspirators) — the bigger the plot, the harder to hide.</summary>
    private const double PlotDiscoveryChance = 0.05;

    /// <summary>An instigator whose loyalty recovers past this gives its plot up.</summary>
    private const float PlotAbandonLoyalty = 0.35f;

    /// <summary>What plots came to this round — exiles, walk-outs and splinters change the groups, so they're applied after every Leader has decided (see <see cref="ProcessPlots"/>).</summary>
    private readonly List<Action> _pendingPlotOutcomes = new();

    public int PlotsHatched { get; private set; }
    public int PlotsUncovered { get; private set; }
    public int PlotsCarried { get; private set; }
    public int PlotsAbandoned { get; private set; }

    /// <summary>A rebel who'd lead a splinter starts a plot instead — or, if one is afoot already, joins it.</summary>
    private void HatchPlot(KinGroup group, Bramblekin rebel)
    {
        if (group.Plot is { } plot)
        {
            if (plot.Instigator != rebel && !plot.Conspirators.Contains(rebel))
                plot.Conspirators.Add(rebel);
            return;
        }
        group.Plot = new Plot(rebel);
        PlotsHatched++;
        Game.AddEventLog($"[PLOT] {rebel.Name} has begun quietly plotting against {group.Leader?.Name ?? "the Leader"} of {group.Title}");
    }

    /// <summary>
    /// At each Leader decision, a plot afoot in <paramref name="group"/>
    /// grows, is uncovered, is given up, or — once it has gone long enough
    /// unnoticed with company — leads its conspirators out as a splinter.
    /// </summary>
    private void AdvancePlot(KinGroup group, Bramblekin leader)
    {
        if (group.Plot is not { } plot)
            return;
        Bramblekin instigator = plot.Instigator;
        if (instigator.IsDead || instigator.GroupId != group.Id || instigator == leader || instigator.IsDueling)
        {
            group.Plot = null;
            return;
        }
        if (instigator.Loyalty > PlotAbandonLoyalty)
        {
            group.Plot = null;
            PlotsAbandoned++;
            Game.AddEventLog($"[PLOT] {instigator.Name} has given up plotting against {leader.Name}");
            return;
        }
        plot.Conspirators.RemoveAll(c => c.IsDead || c.GroupId != group.Id || c == leader || c.Loyalty > PlotAbandonLoyalty + 0.1f);
        plot.Decisions++;

        // Recruiting: the unhappiest member not yet in on it may be won over — a persuasive plotter wins more.
        float swayed = SplinterLoyaltyThreshold + 0.25f * (instigator.Personality.Persuasiveness - 0.5f) + 0.1f;
        Bramblekin? recruit = group.Members
            .Where(m => m != leader && m != instigator && !m.IsDead && !m.IsYoung && !m.IsDueling && m.Loyalty < swayed && !plot.Conspirators.Contains(m))
            .MinBy(m => m.Loyalty);
        if (recruit is not null && Rng.NextDouble() < 0.4 + 0.5 * instigator.Personality.Persuasiveness)
            plot.Conspirators.Add(recruit);

        // The Leader may get wind of it: a sharp one, a big plot.
        if (Rng.NextDouble() < PlotDiscoveryChance * (0.5 + leader.Personality.Intelligence) * (1 + plot.Conspirators.Count))
        {
            UncoverPlot(group, leader, plot);
            return;
        }

        // Nobody would join: it gives up and walks out alone.
        if (plot.Decisions >= PlotMaturity * 2 && plot.Conspirators.Count == 0)
        {
            group.Plot = null;
            PlotsAbandoned++;
            _pendingPlotOutcomes.Add(() =>
            {
                if (!instigator.IsDead && instigator.GroupId == group.Id && group.Leader != instigator)
                    Depart(group, instigator);
            });
            return;
        }

        // Ripe, and not in the snow: out they go.
        if (plot.Decisions >= PlotMaturity && plot.Conspirators.Count >= PlotMinConspirators && CurrentSeason != Season.Winter)
        {
            group.Plot = null;
            _pendingPlotOutcomes.Add(() =>
            {
                if (!instigator.IsDead && instigator.GroupId == group.Id && group.Leader != instigator && TrySplinter(group, instigator, plot.Conspirators))
                    PlotsCarried++;
            });
        }
    }

    /// <summary>A plot uncovered: an Aggressive Leader throws its instigator out; a milder one talks it round, and its conspirators think better of it.</summary>
    private void UncoverPlot(KinGroup group, Bramblekin leader, Plot plot)
    {
        group.Plot = null;
        PlotsUncovered++;
        Bramblekin instigator = plot.Instigator;
        string who = plot.Conspirators.Count > 0 ? $"{instigator.Name} and {plot.Conspirators.Count} others" : instigator.Name;
        Chronicle($"{leader.Name} uncovered a plot by {who} against {group.Title}'s leadership", group);
        if (leader.Personality.Aggression >= ExileLeaderAggression)
        {
            _pendingPlotOutcomes.Add(() =>
            {
                if (!instigator.IsDead && instigator.GroupId == group.Id && group.Leader == leader)
                    Exile(group, instigator, "for plotting");
            });
            foreach (Bramblekin conspirator in plot.Conspirators)
                conspirator.SetLoyalty(conspirator.Loyalty - 0.05f); // Cowed, but resentful.
            return;
        }
        Game.AddEventLog($"[PLOT] {leader.Name} found out {who}'s plot, and talked them round");
        instigator.SetLoyalty(0.3f);
        foreach (Bramblekin conspirator in plot.Conspirators)
            conspirator.SetLoyalty(conspirator.Loyalty + 0.1f);
    }

    private void ProcessPlots()
    {
        foreach (Action outcome in _pendingPlotOutcomes)
            outcome();
        _pendingPlotOutcomes.Clear();
    }
}
