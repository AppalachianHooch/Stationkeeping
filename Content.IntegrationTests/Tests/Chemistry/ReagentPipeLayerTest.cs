#nullable enable
using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.Server.Chemistry.EntitySystems;
using Content.Server.NodeContainer;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.NodeGroups;
using Content.Server.NodeContainer.Nodes;
using Content.Shared.NodeContainer;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.Chemistry;

/// <summary>
/// The reagent pipe auto-layer picks the right fitting and rotation per tile so a tile set becomes one
/// connected net, and - because it judges connectivity only against a network's own tiles - two networks laid
/// right beside each other stay separate.
/// </summary>
[TestFixture]
public sealed class ReagentPipeLayerTest : GameTest
{
    private static readonly ReagentPipeLayout Fuel = new("FuelPipeStraight", "FuelPipeBend");
    private static readonly ReagentPipeLayout Reagent =
        new("ReagentPipeStraight", "ReagentPipeBend", "ReagentPipeTJunction", "ReagentPipeFourway");

    [Test]
    public async Task LShapeFormsOneConnectedNet()
    {
        EntityUid grid = default;
        var tiles = ReagentPipeLayerSystem.LShape(new Vector2i(0, 0), new Vector2i(3, 2));

        await Server.WaitPost(() => grid = BuildAndLay(tiles, Fuel));
        await RunTicksSync(3);

        await Server.WaitAssertion(() =>
        {
            var net = NetAt(grid, new Vector2i(0, 0));
            Assert.That(net, Is.Not.Null, "The run should have formed a net.");
            foreach (var tile in tiles)
                Assert.That(NetAt(grid, tile), Is.SameAs(net), $"Tile {tile} should be on the same net.");
        });
    }

    [Test]
    public async Task CrossUsesAFourway()
    {
        EntityUid grid = default;
        var tiles = new HashSet<Vector2i>
        {
            new(2, 2), new(2, 3), new(2, 1), new(3, 2), new(1, 2), // a plus shape
        };

        await Server.WaitPost(() => grid = BuildAndLay(tiles, Reagent));
        await RunTicksSync(3);

        await Server.WaitAssertion(() =>
        {
            var net = NetAt(grid, new Vector2i(2, 2));
            Assert.That(net, Is.Not.Null);
            foreach (var tile in tiles)
                Assert.That(NetAt(grid, tile), Is.SameAs(net),
                    $"Tile {tile} should join the one net via the centre fourway.");
        });
    }

    [Test]
    public async Task AdjacentNetsStaySeparate()
    {
        EntityUid grid = default;
        var left = new HashSet<Vector2i> { new(0, 0), new(0, 1), new(0, 2) };
        var right = new HashSet<Vector2i> { new(1, 0), new(1, 1), new(1, 2) };

        await Server.WaitPost(() =>
        {
            var all = new HashSet<Vector2i>(left);
            all.UnionWith(right);
            grid = MakeGrid(all);
            var layer = SEntMan.System<ReagentPipeLayerSystem>();
            // Two separate fuel networks, one tile apart.
            layer.LayNetwork((grid, SComp<MapGridComponent>(grid)), left, Fuel);
            layer.LayNetwork((grid, SComp<MapGridComponent>(grid)), right, Fuel);
        });
        await RunTicksSync(3);

        await Server.WaitAssertion(() =>
        {
            var leftNet = NetAt(grid, new Vector2i(0, 0));
            var rightNet = NetAt(grid, new Vector2i(1, 0));
            Assert.That(leftNet, Is.Not.Null);
            Assert.That(rightNet, Is.Not.Null);
            Assert.That(leftNet, Is.Not.SameAs(rightNet),
                "Two networks laid side by side should not merge into one.");
        });
    }

    private EntityUid BuildAndLay(IReadOnlySet<Vector2i> tiles, ReagentPipeLayout layout)
    {
        var grid = MakeGrid(tiles);
        SEntMan.System<ReagentPipeLayerSystem>().LayNetwork((grid, SComp<MapGridComponent>(grid)), tiles, layout);
        return grid;
    }

    private EntityUid MakeGrid(IReadOnlySet<Vector2i> tiles)
    {
        var mapSys = SEntMan.System<SharedMapSystem>();
        var mapManager = Server.ResolveDependency<IMapManager>();
        mapSys.CreateMap(out var mapId);
        var grid = mapManager.CreateGridEntity(mapId);
        foreach (var tile in tiles)
            mapSys.SetTile(grid, tile, new Tile(1));
        return grid.Owner;
    }

    private ReagentPipeNet? NetAt(EntityUid grid, Vector2i tile)
    {
        var mapSys = SEntMan.System<SharedMapSystem>();
        var nodeContainer = SEntMan.System<NodeContainerSystem>();
        var enumerator = mapSys.GetAnchoredEntitiesEnumerator(grid, SComp<MapGridComponent>(grid), tile);
        while (enumerator.MoveNext(out var uid))
        {
            if (SEntMan.TryGetComponent<NodeContainerComponent>(uid, out var nodes)
                && nodeContainer.TryGetNode<ReagentPipeNode>((uid.Value, nodes), "pipe", out var node)
                && node.NodeGroup is ReagentPipeNet net)
            {
                return net;
            }
        }

        return null;
    }
}
