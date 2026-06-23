using System.Linq;
using Content.Server.NodeContainer.Nodes;
using Content.Shared.Chemistry.Components;
using Content.Shared.FixedPoint;
using Content.Shared.NodeContainer;
using Content.Shared.NodeContainer.NodeGroups;
using Robust.Shared.Prototypes;

namespace Content.Server.NodeContainer.NodeGroups;

/// <summary>
///     A network of connected <see cref="ReagentPipeNode"/>s sharing a single <see cref="Solution"/>.
///     The reagent analogue of <see cref="PipeNet"/>: all pipes in the net pool their reagents,
///     so anything added at one pipe is instantly available at every other pipe.
/// </summary>
[NodeGroup(NodeGroupID.ReagentPipe, NodeGroupID.FuelPipe, NodeGroupID.CoolantPipe, NodeGroupID.RcsPipe)]
public sealed class ReagentPipeNet : BaseNodeGroup
{
    [ViewVariables] public readonly Solution Reagents = new();

    /// <summary>
    ///     Current line pressure [0,1], driven by a feeding regulator. Scales throughput and, when a segment
    ///     is cut, how violently the line vents - so a hot, high-pressure line is dangerous to tear down.
    /// </summary>
    [ViewVariables] public float Pressure;

    [ViewVariables] private IPrototypeManager? _protoManager;

    public override void Initialize(Node sourceNode, IEntityManager entMan)
    {
        base.Initialize(sourceNode, entMan);

        _protoManager = IoCManager.Resolve<IPrototypeManager>();
    }

    public override void LoadNodes(List<Node> groupNodes)
    {
        base.LoadNodes(groupNodes);

        foreach (var node in groupNodes)
        {
            var pipeNode = (ReagentPipeNode) node;
            Reagents.MaxVolume += pipeNode.Volume;
        }
    }

    public override void RemoveNode(Node node)
    {
        base.RemoveNode(node);

        // Splitting into a separate group is handled by AfterRemake. Only an actual deletion
        // should pull reagents out of the net here.
        if (!node.Deleting || node is not ReagentPipeNode pipe || Reagents.MaxVolume <= FixedPoint2.Zero)
            return;

        // Drop the deleted pipe's proportional share of reagents along with its capacity.
        var lost = Reagents.Volume * (FixedPoint2.New(pipe.Volume) / Reagents.MaxVolume);
        Reagents.RemoveSolution(lost);
        Reagents.MaxVolume -= FixedPoint2.New(pipe.Volume);
    }

    public override void AfterRemake(IEnumerable<IGrouping<INodeGroup?, Node>> newGroups)
    {
        var nets = newGroups.Select(g => g.Key).OfType<ReagentPipeNet>().ToList();
        var totalCapacity = FixedPoint2.New(nets.Sum(n => n.Reagents.MaxVolume.Float()));

        if (totalCapacity <= FixedPoint2.Zero)
            return;

        // Distribute the old contents across the new nets proportional to their capacity.
        var startVolume = Reagents.Volume;
        for (var i = 0; i < nets.Count; i++)
        {
            // Last net takes whatever remains to avoid leaving stragglers from rounding.
            var split = i == nets.Count - 1
                ? Reagents.SplitSolution(Reagents.Volume)
                : Reagents.SplitSolution(startVolume * (nets[i].Reagents.MaxVolume / totalCapacity));

            nets[i].Reagents.AddSolution(split, _protoManager);
        }
    }

    public override string GetDebugData()
    {
        return @$"Volume: {Reagents.Volume:G3}
Capacity: {Reagents.MaxVolume:G3}";
    }
}
