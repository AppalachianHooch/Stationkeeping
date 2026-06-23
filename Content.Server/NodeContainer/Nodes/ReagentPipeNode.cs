using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.NodeGroups;
using Content.Shared.Atmos;
using Content.Shared.Chemistry.Components;
using Content.Shared.NodeContainer;
using Robust.Shared.Map.Components;
using Robust.Shared.Utility;

namespace Content.Server.NodeContainer.Nodes;

/// <summary>
///     Connects with other <see cref="ReagentPipeNode"/>s whose <see cref="PipeDirection"/>
///     correctly corresponds, forming a <see cref="ReagentPipeNet"/> that carries reagents.
///     Mirrors <see cref="PipeNode"/> but transports liquids instead of gas.
/// </summary>
[DataDefinition]
public sealed partial class ReagentPipeNode : Node, IRotatableNode
{
    /// <summary>
    ///     The directions in which this pipe can connect to other pipes around it.
    /// </summary>
    [DataField("pipeDirection")]
    public PipeDirection OriginalPipeDirection;

    /// <summary>
    ///     The *current* pipe directions (accounting for rotation).
    /// </summary>
    public PipeDirection CurrentPipeDirection { get; private set; }

    [DataField("rotationsEnabled")]
    public bool RotationsEnabled { get; set; } = true;

    /// <summary>
    ///     A worn-out, broken segment stops conducting: it drops out of its net so reagents no longer pass.
    /// </summary>
    public bool Broken;

    public override bool Connectable(IEntityManager entMan, TransformComponent? xform = null)
        => !Broken && base.Connectable(entMan, xform);

    /// <summary>
    ///     Volume of reagents this single pipe segment contributes to its net.
    /// </summary>
    [DataField("volume")]
    public float Volume { get; set; } = DefaultVolume;

    private const float DefaultVolume = 100f;

    /// <summary>
    ///     The shared reagents of the <see cref="ReagentPipeNet"/> this pipe is a part of.
    /// </summary>
    [ViewVariables]
    public Solution? Reagents => (NodeGroup as ReagentPipeNet)?.Reagents;

    /// <summary>
    ///     Nodes this connects to regardless of position, used by valves to merge or split nets.
    /// </summary>
    private HashSet<ReagentPipeNode>? _alwaysReachable;

    public void AddAlwaysReachable(ReagentPipeNode node)
    {
        if (node.NodeGroupID != NodeGroupID)
            return;

        _alwaysReachable ??= new();
        _alwaysReachable.Add(node);

        if (NodeGroup != null)
            IoCManager.Resolve<IEntityManager>().System<NodeGroupSystem>().QueueRemakeGroup((BaseNodeGroup) NodeGroup);
    }

    public void RemoveAlwaysReachable(ReagentPipeNode node)
    {
        if (_alwaysReachable == null)
            return;

        _alwaysReachable.Remove(node);

        if (NodeGroup != null)
            IoCManager.Resolve<IEntityManager>().System<NodeGroupSystem>().QueueRemakeGroup((BaseNodeGroup) NodeGroup);
    }

    public override void Initialize(EntityUid owner, IEntityManager entMan)
    {
        base.Initialize(owner, entMan);

        if (!RotationsEnabled)
            return;

        var xform = entMan.GetComponent<TransformComponent>(owner);
        CurrentPipeDirection = OriginalPipeDirection.RotatePipeDirection(xform.LocalRotation);
    }

    bool IRotatableNode.RotateNode(in MoveEvent ev)
    {
        if (OriginalPipeDirection == PipeDirection.Fourway)
            return false;

        if (!RotationsEnabled)
        {
            if (CurrentPipeDirection == OriginalPipeDirection)
                return false;

            CurrentPipeDirection = OriginalPipeDirection;
            return true;
        }

        var oldDirection = CurrentPipeDirection;
        CurrentPipeDirection = OriginalPipeDirection.RotatePipeDirection(ev.NewRotation);
        return oldDirection != CurrentPipeDirection;
    }

    public override void OnAnchorStateChanged(IEntityManager entityManager, bool anchored)
    {
        if (!anchored)
            return;

        if (!RotationsEnabled)
        {
            CurrentPipeDirection = OriginalPipeDirection;
            return;
        }

        var xform = entityManager.GetComponent<TransformComponent>(Owner);
        CurrentPipeDirection = OriginalPipeDirection.RotatePipeDirection(xform.LocalRotation);
    }

    public override IEnumerable<Node> GetReachableNodes(
        Entity<TransformComponent> xform,
        EntityQuery<NodeContainerComponent> nodeQuery,
        EntityQuery<TransformComponent> xformQuery,
        Entity<MapGridComponent>? grid,
        IEntityManager entMan)
    {
        if (_alwaysReachable != null)
        {
            var remQ = new RemQueue<ReagentPipeNode>();
            foreach (var node in _alwaysReachable)
            {
                if (node.Deleting)
                    remQ.Add(node);

                yield return node;
            }

            foreach (var node in remQ)
            {
                _alwaysReachable.Remove(node);
            }
        }

        if (!xform.Comp.Anchored || grid is not { } gridEnt)
            yield break;

        var mapSystem = entMan.System<SharedMapSystem>();
        var pos = mapSystem.TileIndicesFor(gridEnt, xform.Comp.Coordinates);

        for (var i = 0; i < PipeDirectionHelpers.PipeDirections; i++)
        {
            var pipeDir = (PipeDirection) (1 << i);

            if (!CurrentPipeDirection.HasDirection(pipeDir))
                continue;

            foreach (var pipe in LinkableNodesInDirection(pos, pipeDir, gridEnt, nodeQuery, mapSystem))
            {
                yield return pipe;
            }
        }
    }

    /// <summary>
    ///     Gets the reagent pipes that can connect to us from the adjacent tile in a direction.
    /// </summary>
    private IEnumerable<ReagentPipeNode> LinkableNodesInDirection(
        Vector2i pos,
        PipeDirection pipeDir,
        Entity<MapGridComponent> grid,
        EntityQuery<NodeContainerComponent> nodeQuery,
        SharedMapSystem mapSystem)
    {
        var offsetPos = pos.Offset(pipeDir.ToDirection());

        foreach (var entity in mapSystem.GetAnchoredEntities(grid, offsetPos))
        {
            if (!nodeQuery.TryGetComponent(entity, out var container))
                continue;

            foreach (var node in container.Nodes.Values)
            {
                if (node is ReagentPipeNode pipe
                    && pipe.NodeGroupID == NodeGroupID
                    && pipe.CurrentPipeDirection.HasDirection(pipeDir.GetOpposite()))
                {
                    yield return pipe;
                }
            }
        }
    }
}
