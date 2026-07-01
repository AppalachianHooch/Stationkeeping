#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Server.NodeContainer;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.NodeGroups;
using Content.Server.NodeContainer.Nodes;
using Content.Shared.Coordinates;
using Content.Shared.NodeContainer;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.Chemistry;

/// <summary>
/// The plenum is its own routing plane: overhead reagent pipes only join other overhead pipes, never a
/// subfloor pipe, even on an adjacent tile.
/// </summary>
[TestFixture]
public sealed class ReagentPipePlenumTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: TestPlenumPipe
  components:
  - type: Transform
    anchored: true
  - type: NodeContainer
    nodes:
      pipe:
        !type:ReagentPipeNode
        nodeGroupID: ReagentPipe
        pipeDirection: Fourway
        layer: Plenum

- type: entity
  id: TestSubfloorPipe
  components:
  - type: Transform
    anchored: true
  - type: NodeContainer
    nodes:
      pipe:
        !type:ReagentPipeNode
        nodeGroupID: ReagentPipe
        pipeDirection: Fourway
        layer: Subfloor

# A port-like endpoint: no CeilingLayer marker and no explicit layer, so it takes the default (Plenum).
- type: entity
  id: TestPlenumEndpoint
  components:
  - type: Transform
    anchored: true
  - type: NodeContainer
    nodes:
      pipe:
        !type:ReagentPipeNode
        nodeGroupID: ReagentPipe
        pipeDirection: Fourway
";

    [Test]
    public async Task OverheadPipesShareOneNet()
    {
        EntityUid a = default, b = default;

        await Server.WaitAssertion(() =>
        {
            BuildLine(2, out var grid);
            a = SEntMan.SpawnEntity("TestPlenumPipe", grid.ToCoordinates(0, 0));
            b = SEntMan.SpawnEntity("TestPlenumPipe", grid.ToCoordinates(0, 1));
        });

        await RunTicksSync(2);

        await Server.WaitAssertion(() =>
            Assert.That(GetNet(a), Is.SameAs(GetNet(b)), "Two overhead pipes should form one net."));
    }

    [Test]
    public async Task EndpointWithoutMarkerJoinsPlenumRun()
    {
        EntityUid pipe = default, endpoint = default;

        await Server.WaitAssertion(() =>
        {
            BuildLine(2, out var grid);
            pipe = SEntMan.SpawnEntity("TestPlenumPipe", grid.ToCoordinates(0, 0));
            endpoint = SEntMan.SpawnEntity("TestPlenumEndpoint", grid.ToCoordinates(0, 1));
        });

        await RunTicksSync(2);

        await Server.WaitAssertion(() =>
            Assert.That(GetNet(endpoint), Is.SameAs(GetNet(pipe)),
                "A port-like endpoint without the ceiling marker must still join the plenum run."));
    }

    [Test]
    public async Task OverheadPipeAndSubfloorPipeStaySeparate()
    {
        EntityUid plenum = default, subfloor = default;

        await Server.WaitAssertion(() =>
        {
            BuildLine(2, out var grid);
            plenum = SEntMan.SpawnEntity("TestPlenumPipe", grid.ToCoordinates(0, 0));
            subfloor = SEntMan.SpawnEntity("TestSubfloorPipe", grid.ToCoordinates(0, 1));
        });

        await RunTicksSync(2);

        await Server.WaitAssertion(() =>
            Assert.That(GetNet(plenum), Is.Not.SameAs(GetNet(subfloor)),
                "A plenum pipe must not join a subfloor pipe on the adjacent tile."));
    }

    private void BuildLine(int length, out EntityUid grid)
    {
        var mapSys = SEntMan.System<SharedMapSystem>();
        var mapManager = Server.ResolveDependency<IMapManager>();

        mapSys.CreateMap(out var mapId);
        var gridEnt = mapManager.CreateGridEntity(mapId);
        for (var i = 0; i < length; i++)
            mapSys.SetTile(gridEnt, new Vector2i(0, i), new Tile(1));

        grid = gridEnt.Owner;
    }

    private ReagentPipeNet GetNet(EntityUid uid)
    {
        var nodeContainer = SEntMan.System<NodeContainerSystem>();
        var nc = SComp<NodeContainerComponent>(uid);
        Assert.That(nodeContainer.TryGetNode<ReagentPipeNode>((uid, nc), "pipe", out var pipe), "pipe node missing.");
        Assert.That(pipe!.NodeGroup, Is.InstanceOf<ReagentPipeNet>(), "pipe node has no reagent net.");
        return (ReagentPipeNet) pipe.NodeGroup!;
    }
}
