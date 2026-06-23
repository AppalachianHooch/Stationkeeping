#nullable enable
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server.Chemistry.Components;
using Content.Server.Chemistry.EntitySystems;
using Content.Server.NodeContainer;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.NodeGroups;
using Content.Server.NodeContainer.Nodes;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Coordinates;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using Content.Shared.NodeContainer;
using Content.Shared.Verbs;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Chemistry;

/// <summary>
/// Happy-path coverage for the reagent pipe network: net formation, the solution/net connector,
/// the directional pump, and the merge/split valve.
/// </summary>
[TestFixture]
public sealed class ReagentPipeTest : GameTest
{
    private static readonly ProtoId<ReagentPrototype> Reagent = "Water";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: TestReagentPipe
  components:
  - type: Transform
    anchored: true
  - type: NodeContainer
    nodes:
      pipe:
        !type:ReagentPipeNode
        nodeGroupID: ReagentPipe
        pipeDirection: Fourway

- type: entity
  id: TestReagentPipeInput
  components:
  - type: Transform
    anchored: true
  - type: NodeContainer
    nodes:
      pipe:
        !type:ReagentPipeNode
        nodeGroupID: ReagentPipe
        pipeDirection: Fourway
  - type: Solution
    id: tank
    solution:
      maxVol: 100
      reagents:
      - ReagentId: Water
        Quantity: 50
  - type: RefillableSolution
    solution: tank
  - type: DrainableSolution
    solution: tank
  - type: ReagentPipeConnector
    mode: Input
    solutionName: tank
    nodeName: pipe

- type: entity
  id: TestReagentPipeOutput
  components:
  - type: Transform
    anchored: true
  - type: NodeContainer
    nodes:
      pipe:
        !type:ReagentPipeNode
        nodeGroupID: ReagentPipe
        pipeDirection: Fourway
  - type: Solution
    id: tank
    solution:
      maxVol: 100
  - type: RefillableSolution
    solution: tank
  - type: DrainableSolution
    solution: tank
  - type: ReagentPipeConnector
    mode: Output
    solutionName: tank
    nodeName: pipe

- type: entity
  id: TestReagentPipePump
  components:
  - type: Transform
    anchored: true
  - type: NodeContainer
    nodes:
      inlet:
        !type:ReagentPipeNode
        nodeGroupID: ReagentPipe
        pipeDirection: South
      outlet:
        !type:ReagentPipeNode
        nodeGroupID: ReagentPipe
        pipeDirection: North
  - type: ReagentPipePump

- type: entity
  id: TestReagentPipeValve
  components:
  - type: Transform
    anchored: true
  - type: NodeContainer
    nodes:
      inlet:
        !type:ReagentPipeNode
        nodeGroupID: ReagentPipe
        pipeDirection: South
      outlet:
        !type:ReagentPipeNode
        nodeGroupID: ReagentPipe
        pipeDirection: North
  - type: ReagentPipeValve

- type: entity
  id: TestReagentPipeValveOpen
  components:
  - type: Transform
    anchored: true
  - type: NodeContainer
    nodes:
      inlet:
        !type:ReagentPipeNode
        nodeGroupID: ReagentPipe
        pipeDirection: South
      outlet:
        !type:ReagentPipeNode
        nodeGroupID: ReagentPipe
        pipeDirection: North
  - type: ReagentPipeValve
    open: true
";

    [Test]
    public async Task ConnectedPipesShareOneNet()
    {
        EntityUid a = default, b = default;

        await Server.WaitAssertion(() =>
        {
            BuildLine(2, out var grid);
            a = SEntMan.SpawnEntity("TestReagentPipe", grid.ToCoordinates(0, 0));
            b = SEntMan.SpawnEntity("TestReagentPipe", grid.ToCoordinates(0, 1));
        });

        await RunTicksSync(2);

        await Server.WaitAssertion(() =>
        {
            var netA = GetNet(a, "pipe");
            var netB = GetNet(b, "pipe");

            // Both segments resolve to the same shared net with pooled capacity.
            Assert.That(netA, Is.SameAs(netB), "Adjacent pipes should form a single net.");
            Assert.That(netA.Reagents.MaxVolume, Is.EqualTo(FixedPoint2.New(200)), "Net capacity should sum its segments.");

            // A reagent added at one pipe is instantly present at the other.
            netA.Reagents.AddReagent(Reagent.Id, FixedPoint2.New(40));
            Assert.That(netB.Reagents.GetTotalPrototypeQuantity(Reagent), Is.EqualTo(FixedPoint2.New(40)));
        });
    }

    [Test]
    public async Task InputConnectorDrainsTankIntoNet()
    {
        EntityUid port = default;

        await Server.WaitAssertion(() =>
        {
            BuildLine(1, out var grid);
            port = SEntMan.SpawnEntity("TestReagentPipeInput", grid.ToCoordinates(0, 0));
        });

        // One transfer cycle (1s) moves the connector's transfer rate from the tank into the net.
        await RunSeconds(1.5f);

        await Server.WaitAssertion(() =>
        {
            var net = GetNet(port, "pipe");
            Assert.That(net.Reagents.GetTotalPrototypeQuantity(Reagent), Is.GreaterThan(FixedPoint2.Zero),
                "An input connector should push reagents from its tank into the net.");
        });
    }

    [Test]
    public async Task PressureScalesFeedRate()
    {
        EntityUid high = default, low = default;

        await Server.WaitAssertion(() =>
        {
            BuildLine(3, out var grid);
            high = SEntMan.SpawnEntity("TestReagentPipeInput", grid.ToCoordinates(0, 0));
            low = SEntMan.SpawnEntity("TestReagentPipeInput", grid.ToCoordinates(0, 2));
        });

        await RunTicksSync(2);
        await Server.WaitAssertion(() =>
        {
            var sys = SEntMan.System<ReagentPipeConnectorSystem>();
            sys.SetPressure((high, SComp<ReagentPipeConnectorComponent>(high)), 1f);
            sys.SetPressure((low, SComp<ReagentPipeConnectorComponent>(low)), 0.2f);
        });

        await RunSeconds(1.5f);

        await Server.WaitAssertion(() =>
        {
            var highNet = GetNet(high, "pipe");
            var lowNet = GetNet(low, "pipe");
            Assert.That(highNet.Pressure, Is.EqualTo(1f), "The input regulator should pressurize its line.");
            Assert.That(highNet.Reagents.Volume, Is.GreaterThan(lowNet.Reagents.Volume),
                "A higher-pressure regulator should feed reagents into the line faster.");
        });
    }

    [Test]
    public async Task MultipleRegulatorsAggregateToMaxPressure()
    {
        EntityUid high = default, low = default;

        await Server.WaitAssertion(() =>
        {
            // Two input regulators on adjacent pipes share one net.
            BuildLine(2, out var grid);
            high = SEntMan.SpawnEntity("TestReagentPipeInput", grid.ToCoordinates(0, 0));
            low = SEntMan.SpawnEntity("TestReagentPipeInput", grid.ToCoordinates(0, 1));
        });

        await RunTicksSync(2);
        await Server.WaitAssertion(() =>
        {
            var sys = SEntMan.System<ReagentPipeConnectorSystem>();
            sys.SetPressure((high, SComp<ReagentPipeConnectorComponent>(high)), 1f);
            sys.SetPressure((low, SComp<ReagentPipeConnectorComponent>(low)), 0.2f);
        });

        await RunSeconds(1.5f);

        await Server.WaitAssertion(() =>
        {
            // Line pressure is the strongest regulator regardless of enumeration order, not whichever ran last.
            Assert.That(GetNet(high, "pipe"), Is.SameAs(GetNet(low, "pipe")), "Both regulators should share one net.");
            Assert.That(GetNet(high, "pipe").Pressure, Is.EqualTo(1f),
                "Line pressure should aggregate to the highest regulator, not the last one processed.");
        });
    }

    [Test]
    public async Task PressureVerbSetsLinePressure()
    {
        EntityUid input = default, output = default;
        var user = await Spawn("TestReagentPipe"); // any entity works as the acting user

        await Server.WaitAssertion(() =>
        {
            BuildLine(2, out var grid);
            input = SEntMan.SpawnEntity("TestReagentPipeInput", grid.ToCoordinates(0, 0));
            output = SEntMan.SpawnEntity("TestReagentPipeOutput", grid.ToCoordinates(0, 1));
        });

        await RunTicksSync(2);

        await Server.WaitAssertion(() =>
        {
            var verbs = SEntMan.System<SharedVerbSystem>();
            var loc = Server.ResolveDependency<Robust.Shared.Localization.ILocalizationManager>();
            var category = loc.GetString("reagent-pipe-pressure-verb-category");

            // The output connector has no regulator, so it offers no pressure dial.
            var outputVerbs = verbs.GetLocalVerbs(output, user, typeof(AlternativeVerb), force: true);
            Assert.That(outputVerbs.Any(v => v.Category?.Text == category), Is.False,
                "Only the input regulator should expose pressure verbs.");

            // Crank the input regulator to maximum through its verb.
            var inputVerbs = verbs.GetLocalVerbs(input, user, typeof(AlternativeVerb), force: true);
            var max = inputVerbs.FirstOrDefault(v => v.Text == loc.GetString("reagent-pipe-pressure-max"));
            Assert.That(max?.Act, Is.Not.Null, "The input regulator should offer a maximum-pressure verb.");
            max!.Act!.Invoke();
        });

        await RunSeconds(1.5f);

        await Server.WaitAssertion(() =>
            Assert.That(GetNet(input, "pipe").Pressure, Is.EqualTo(1f),
                "Invoking the maximum-pressure verb should drive the line to full pressure."));
    }

    [Test]
    public async Task OutputConnectorFillsTankFromNet()
    {
        EntityUid port = default;

        await Server.WaitAssertion(() =>
        {
            BuildLine(1, out var grid);
            port = SEntMan.SpawnEntity("TestReagentPipeOutput", grid.ToCoordinates(0, 0));
        });

        await RunTicksSync(2);
        await Server.WaitAssertion(() => GetNet(port, "pipe").Reagents.AddReagent(Reagent.Id, FixedPoint2.New(50)));
        await RunSeconds(1.5f);

        await Server.WaitAssertion(() =>
        {
            var sol = SEntMan.System<SharedSolutionContainerSystem>();
            Assert.That(sol.TryGetSolution(port, "tank", out _, out var tank), "Output port should have its tank solution.");
            Assert.That(tank!.GetTotalPrototypeQuantity(Reagent), Is.GreaterThan(FixedPoint2.Zero),
                "An output connector should pull reagents from the net into its tank.");
        });
    }

    [Test]
    public async Task PumpMovesReagentsBetweenNets()
    {
        EntityUid source = default, dest = default;

        await Server.WaitAssertion(() =>
        {
            BuildLine(3, out var grid);
            source = SEntMan.SpawnEntity("TestReagentPipe", grid.ToCoordinates(0, 0));
            SEntMan.SpawnEntity("TestReagentPipePump", grid.ToCoordinates(0, 1));
            dest = SEntMan.SpawnEntity("TestReagentPipe", grid.ToCoordinates(0, 2));
        });

        await RunTicksSync(2);
        await Server.WaitAssertion(() => GetNet(source, "pipe").Reagents.AddReagent(Reagent.Id, FixedPoint2.New(40)));
        await RunSeconds(1.5f);

        await Server.WaitAssertion(() =>
        {
            var inNet = GetNet(source, "pipe");
            var outNet = GetNet(dest, "pipe");

            Assert.That(inNet, Is.Not.SameAs(outNet), "The pump should separate inlet and outlet nets.");
            Assert.That(outNet.Reagents.GetTotalPrototypeQuantity(Reagent), Is.GreaterThan(FixedPoint2.Zero),
                "The pump should push reagents from the inlet net into the outlet net.");
        });
    }

    [Test]
    public async Task ValveTogglesNetMerge()
    {
        EntityUid source = default, valve = default, dest = default;
        var user = await Spawn("TestReagentPipe"); // any entity works as the acting user

        await Server.WaitAssertion(() =>
        {
            BuildLine(3, out var grid);
            source = SEntMan.SpawnEntity("TestReagentPipe", grid.ToCoordinates(0, 0));
            valve = SEntMan.SpawnEntity("TestReagentPipeValve", grid.ToCoordinates(0, 1));
            dest = SEntMan.SpawnEntity("TestReagentPipe", grid.ToCoordinates(0, 2));
        });

        await RunTicksSync(2);

        await Server.WaitAssertion(() =>
        {
            // Closed: the valve keeps the two sides apart.
            Assert.That(GetNet(source, "pipe"), Is.Not.SameAs(GetNet(dest, "pipe")),
                "A closed valve should keep the nets separate.");

            var ev = new ActivateInWorldEvent(user, valve, complex: true);
            SEntMan.EventBus.RaiseLocalEvent(valve, ev);
        });

        await RunTicksSync(2);

        await Server.WaitAssertion(() =>
        {
            Assert.That(GetNet(source, "pipe"), Is.SameAs(GetNet(dest, "pipe")),
                "Opening the valve should merge the two nets into one.");

            // Toggle again: closing the valve should split the merged net back apart.
            var ev = new ActivateInWorldEvent(user, valve, complex: true);
            SEntMan.EventBus.RaiseLocalEvent(valve, ev);
        });

        await RunTicksSync(2);

        await Server.WaitAssertion(() =>
        {
            Assert.That(GetNet(source, "pipe"), Is.Not.SameAs(GetNet(dest, "pipe")),
                "Closing the valve should split the nets back apart.");
        });
    }

    [Test]
    public async Task ValveOpenAtMapInitMergesNets()
    {
        EntityUid source = default, dest = default;

        await Server.WaitAssertion(() =>
        {
            BuildLine(3, out var grid);
            source = SEntMan.SpawnEntity("TestReagentPipe", grid.ToCoordinates(0, 0));
            SEntMan.SpawnEntity("TestReagentPipeValveOpen", grid.ToCoordinates(0, 1));
            dest = SEntMan.SpawnEntity("TestReagentPipe", grid.ToCoordinates(0, 2));
        });

        await RunTicksSync(2);

        await Server.WaitAssertion(() =>
        {
            // A valve that starts open must link its nets during MapInit, with no activation needed.
            Assert.That(GetNet(source, "pipe"), Is.SameAs(GetNet(dest, "pipe")),
                "A valve spawned open should merge its nets on map init.");
        });
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

    private ReagentPipeNet GetNet(EntityUid uid, string node)
    {
        var nodeContainer = SEntMan.System<NodeContainerSystem>();
        var nc = SComp<NodeContainerComponent>(uid);
        Assert.That(nodeContainer.TryGetNode<ReagentPipeNode>((uid, nc), node, out var pipe), $"{node} node missing.");
        Assert.That(pipe!.NodeGroup, Is.InstanceOf<ReagentPipeNet>(), $"{node} node has no reagent net.");
        return (ReagentPipeNet) pipe.NodeGroup!;
    }
}
