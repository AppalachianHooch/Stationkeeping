using Content.Server.Chemistry.Components;
using Content.Server.NodeContainer;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Server.NodeContainer.NodeGroups;
using Content.Shared.FixedPoint;
using Content.Shared.NodeContainer;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server.Chemistry.EntitySystems;

/// <summary>
///     Drives <see cref="ReagentPipePumpComponent"/>: pushes reagents from the inlet net to the outlet net.
/// </summary>
public sealed partial class ReagentPipePumpSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _protoManager = default!;
    [Dependency] private NodeContainerSystem _nodeContainer = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ReagentPipePumpComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<ReagentPipePumpComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.NextTransfer = _timing.CurTime + ent.Comp.Duration;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ReagentPipePumpComponent, NodeContainerComponent>();
        while (query.MoveNext(out var uid, out var pump, out var nodes))
        {
            if (!pump.Enabled || _timing.CurTime < pump.NextTransfer)
                continue;

            pump.NextTransfer += pump.Duration;

            if (!_nodeContainer.TryGetNodes((uid, nodes), pump.InletName, pump.OutletName,
                    out ReagentPipeNode? inlet, out ReagentPipeNode? outlet))
                continue;

            if (inlet.NodeGroup is not ReagentPipeNet inNet || outlet.NodeGroup is not ReagentPipeNet outNet)
                continue;

            // Same net already shares reagents, nothing to pump.
            if (ReferenceEquals(inNet, outNet))
                continue;

            var amount = FixedPoint2.Min(pump.TransferRate,
                FixedPoint2.Min(inNet.Reagents.Volume, outNet.Reagents.AvailableVolume));

            if (amount <= FixedPoint2.Zero)
                continue;

            var taken = inNet.Reagents.SplitSolution(amount);
            outNet.Reagents.AddSolution(taken, _protoManager);
        }
    }
}
