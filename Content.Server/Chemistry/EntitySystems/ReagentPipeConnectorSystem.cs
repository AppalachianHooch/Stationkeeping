using Content.Server.Chemistry.Components;
using Content.Server.NodeContainer;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Server.NodeContainer.NodeGroups;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.NodeContainer;
using Content.Shared.Verbs;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server.Chemistry.EntitySystems;

/// <summary>
///     Moves reagents between a local solution and a connected reagent pipe net on a fixed cycle.
/// </summary>
public sealed partial class ReagentPipeConnectorSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _protoManager = default!;
    [Dependency] private NodeContainerSystem _nodeContainer = default!;
    [Dependency] private SharedSolutionContainerSystem _solution = default!;

    // Nets pressurized this tick, so line pressure is the max of every regulator on a net, not just the last one seen.
    private readonly HashSet<ReagentPipeNet> _pressurizedThisTick = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ReagentPipeConnectorComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ReagentPipeConnectorComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);
        SubscribeLocalEvent<ReagentPipeConnectorComponent, ExaminedEvent>(OnExamine);
    }

    // Regulator pressure settings offered on the dial, low to high.
    private static readonly (string Loc, float Value)[] PressureSettings =
    {
        ("reagent-pipe-pressure-off", 0f),
        ("reagent-pipe-pressure-low", 0.25f),
        ("reagent-pipe-pressure-half", 0.5f),
        ("reagent-pipe-pressure-high", 0.75f),
        ("reagent-pipe-pressure-max", 1f),
    };

    private void OnGetVerbs(Entity<ReagentPipeConnectorComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        // Only the input connector regulates pressure; output connectors just drain the net.
        if (!args.CanInteract || !args.CanAccess || ent.Comp.Mode != ReagentPipeConnectorMode.Input)
            return;

        var category = new VerbCategory(Loc.GetString("reagent-pipe-pressure-verb-category"), null);
        var priority = 0;
        foreach (var (loc, value) in PressureSettings)
        {
            var setting = value;
            args.Verbs.Add(new AlternativeVerb
            {
                Text = Loc.GetString(loc),
                Category = category,
                Priority = priority--,
                Act = () => SetPressure(ent, setting),
            });
        }
    }

    private void OnExamine(Entity<ReagentPipeConnectorComponent> ent, ref ExaminedEvent args)
    {
        if (ent.Comp.Mode != ReagentPipeConnectorMode.Input)
            return;

        args.PushMarkup(Loc.GetString("reagent-pipe-pressure-examine",
            ("level", Loc.GetString(PressureLevelLoc(ent.Comp.Pressure)))));
    }

    /// <summary>
    /// Maps a line pressure [0,1] to a coarse player-facing label used by examine cues.
    /// </summary>
    public static string PressureLevelLoc(float pressure) => pressure switch
    {
        <= 0.1f => "reagent-pipe-pressure-level-bled",
        < 0.4f => "reagent-pipe-pressure-level-low",
        < 0.7f => "reagent-pipe-pressure-level-nominal",
        < 0.95f => "reagent-pipe-pressure-level-high",
        _ => "reagent-pipe-pressure-level-max",
    };

    private void OnMapInit(Entity<ReagentPipeConnectorComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.NextTransfer = _timing.CurTime + ent.Comp.Duration;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _pressurizedThisTick.Clear();

        var query = EntityQueryEnumerator<ReagentPipeConnectorComponent, NodeContainerComponent>();
        while (query.MoveNext(out var uid, out var comp, out var nodes))
        {
            if (!comp.Enabled)
                continue;

            if (!_nodeContainer.TryGetNode<ReagentPipeNode>((uid, nodes), comp.NodeName, out var node)
                || node.NodeGroup is not ReagentPipeNet net)
                continue;

            // Input connectors are regulators: assert line pressure every tick (max across all of them) so the
            // line tracks the strongest active regulator and drops the moment one stops, instead of latching
            // whichever transferred last. Baseline (0.5) moves the rated rate; full pressure moves half again as much.
            var pressure = Math.Clamp(comp.Pressure, 0f, 1f);
            if (comp.Mode == ReagentPipeConnectorMode.Input)
                net.Pressure = _pressurizedThisTick.Add(net) ? pressure : MathF.Max(net.Pressure, pressure);

            if (_timing.CurTime < comp.NextTransfer)
                continue;

            // Resync rather than catch up, so an idle connector resumes at one transfer per cycle, not a burst.
            comp.NextTransfer += comp.Duration;
            if (comp.NextTransfer < _timing.CurTime)
                comp.NextTransfer = _timing.CurTime + comp.Duration;

            // Resolve handles both a managed solution and a SolutionComponent sitting on the entity.
            if (!_solution.ResolveSolution((uid, null), comp.SolutionName, ref comp.Solution))
                continue;

            if (comp.Mode == ReagentPipeConnectorMode.Input)
            {
                // "Off" (pressure 0) holds the line but moves nothing.
                if (pressure > 0f)
                    DrainIntoNet(comp.Solution.Value, net, comp.TransferRate * (0.5f + pressure));
            }
            else if (net.Pressure > 0f)
            {
                // A depressurized line delivers nothing, so "off" stops the whole line.
                _solution.TryTransferSolution(comp.Solution.Value, net.Reagents, comp.TransferRate);
            }
        }
    }

    /// <summary>
    /// Sets the regulator pressure [0,1] this connector drives its line at - the throughput/danger dial.
    /// </summary>
    public void SetPressure(Entity<ReagentPipeConnectorComponent> ent, float pressure)
    {
        ent.Comp.Pressure = Math.Clamp(pressure, 0f, 1f);
    }

    private void DrainIntoNet(Entity<SolutionComponent> source, ReagentPipeNet net, FixedPoint2 rate)
    {
        var amount = FixedPoint2.Min(rate, FixedPoint2.Min(source.Comp.Solution.Volume, net.Reagents.AvailableVolume));
        if (amount <= FixedPoint2.Zero)
            return;

        var taken = _solution.SplitSolution(source, amount);
        net.Reagents.AddSolution(taken, _protoManager);
    }
}
