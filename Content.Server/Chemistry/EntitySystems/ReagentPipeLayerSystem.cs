using System.Numerics;
using Content.Shared.Atmos;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Server.Chemistry.EntitySystems;

/// <summary>
/// The four reagent-pipe fitting prototypes used to lay a network. Straight and bend are required; the
/// junction and crossing are optional - a layout that omits them can only form non-branching runs.
/// </summary>
public readonly record struct ReagentPipeLayout(
    EntProtoId Straight,
    EntProtoId Bend,
    EntProtoId? TJunction = null,
    EntProtoId? Fourway = null);

/// <summary>
/// Auto-lays a reagent pipe network over a set of tiles, choosing the correct fitting and rotation per tile
/// from its neighbours <em>within that same tile set</em>. Because connectivity is judged only against the
/// network's own tiles, two networks can run side by side without merging - the property the engine's
/// power-only auto-cabler can't give. The reusable core for plumbing generated ships with fuel/coolant/RCS lines.
/// </summary>
public sealed class ReagentPipeLayerSystem : EntitySystem
{
    [Dependency] private readonly SharedMapSystem _maps = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;

    private static readonly Angle[] Quarters =
    {
        Angle.Zero, Angle.FromDegrees(90), Angle.FromDegrees(180), Angle.FromDegrees(270),
    };

    /// <summary>
    /// Spawns a connected reagent pipe network over <paramref name="tiles"/> on the grid, each segment oriented
    /// to link only its in-network neighbours. Tiles needing a fitting the layout doesn't provide are skipped.
    /// </summary>
    public void LayNetwork(Entity<MapGridComponent> grid, IReadOnlySet<Vector2i> tiles, ReagentPipeLayout layout)
    {
        foreach (var tile in tiles)
        {
            var connections = Connections(tile, tiles);
            if (!TryFitting(connections, layout, out var proto, out var baseDir, out var desired))
            {
                Log.Warning($"ReagentPipeLayer: no '{connections}' fitting in layout at {tile}; leaving a gap.");
                continue;
            }

            var angle = Angle.Zero;
            foreach (var q in Quarters)
            {
                if (baseDir.RotatePipeDirection(q) == desired)
                {
                    angle = q;
                    break;
                }
            }

            var uid = Spawn(proto, _maps.GridTileToLocal(grid, grid.Comp, tile));
            _xform.SetLocalRotation(uid, angle);

            var xform = Transform(uid);
            if (!xform.Anchored)
                _xform.AnchorEntity((uid, xform), (grid.Owner, grid.Comp));
        }
    }

    /// <summary>
    /// A simple L-shaped run of tiles from <paramref name="from"/> to <paramref name="to"/>: horizontal first,
    /// then vertical. Needs only straights and a bend, so it works for any pipe type.
    /// </summary>
    public static HashSet<Vector2i> LShape(Vector2i from, Vector2i to)
    {
        var tiles = new HashSet<Vector2i>();

        var stepX = Math.Sign(to.X - from.X);
        for (var x = from.X; x != to.X; x += stepX)
            tiles.Add(new Vector2i(x, from.Y));

        var stepY = Math.Sign(to.Y - from.Y);
        for (var y = from.Y; y != to.Y; y += stepY)
            tiles.Add(new Vector2i(to.X, y));

        tiles.Add(to);
        return tiles;
    }

    private static PipeDirection Connections(Vector2i tile, IReadOnlySet<Vector2i> tiles)
    {
        var d = PipeDirection.None;
        if (tiles.Contains(tile + new Vector2i(0, 1)))
            d |= PipeDirection.North;
        if (tiles.Contains(tile + new Vector2i(0, -1)))
            d |= PipeDirection.South;
        if (tiles.Contains(tile + new Vector2i(-1, 0)))
            d |= PipeDirection.West;
        if (tiles.Contains(tile + new Vector2i(1, 0)))
            d |= PipeDirection.East;
        return d;
    }

    private static bool TryFitting(PipeDirection conn, ReagentPipeLayout layout,
        out EntProtoId proto, out PipeDirection baseDir, out PipeDirection desired)
    {
        proto = default;
        baseDir = default;
        desired = default;

        switch (BitCount(conn))
        {
            case 0:
            case 1:
                // Dead end or stray: a straight laid along the neighbour's axis (its far end just dangles).
                proto = layout.Straight;
                baseDir = PipeDirection.Longitudinal;
                desired = (conn & PipeDirection.Lateral) != 0 ? PipeDirection.Lateral : PipeDirection.Longitudinal;
                return true;
            case 2:
                if (conn == PipeDirection.Longitudinal || conn == PipeDirection.Lateral)
                {
                    proto = layout.Straight;
                    baseDir = PipeDirection.Longitudinal;
                }
                else
                {
                    proto = layout.Bend;
                    baseDir = PipeDirection.SWBend;
                }
                desired = conn;
                return true;
            case 3:
                if (layout.TJunction is not { } t)
                    return false;
                proto = t;
                baseDir = PipeDirection.TSouth;
                desired = conn;
                return true;
            default:
                if (layout.Fourway is not { } f)
                    return false;
                proto = f;
                baseDir = PipeDirection.Fourway;
                desired = PipeDirection.Fourway;
                return true;
        }
    }

    private static int BitCount(PipeDirection d)
    {
        var n = 0;
        if ((d & PipeDirection.North) != 0) n++;
        if ((d & PipeDirection.South) != 0) n++;
        if ((d & PipeDirection.West) != 0) n++;
        if ((d & PipeDirection.East) != 0) n++;
        return n;
    }
}
