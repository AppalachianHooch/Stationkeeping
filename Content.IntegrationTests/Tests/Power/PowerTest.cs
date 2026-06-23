#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Power.Nodes;
using Content.Shared.Coordinates;
using Content.Shared.NodeContainer;
using Content.Shared.Power.Components;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests.Power
{
    [TestFixture]
    public sealed class PowerTest : GameTest
    {
        [TestPrototypes]
        private const string Prototypes = @"
- type: entity
  id: GeneratorDummy
  components:
  - type: NodeContainer
    nodes:
      output:
        !type:CableDeviceNode
        nodeGroupID: HVPower
  - type: PowerSupplier
  - type: Transform
    anchored: true

- type: entity
  id: ConsumerDummy
  components:
  - type: Transform
    anchored: true
  - type: NodeContainer
    nodes:
      input:
        !type:CableDeviceNode
        nodeGroupID: HVPower
  - type: PowerConsumer

- type: entity
  id: ChargingBatteryDummy
  components:
  - type: Transform
    anchored: true
  - type: NodeContainer
    nodes:
      output:
        !type:CableDeviceNode
        nodeGroupID: HVPower
  - type: PowerNetworkBattery
  - type: Battery
    netsync: false
  - type: BatteryCharger

- type: entity
  id: DischargingBatteryDummy
  components:
  - type: Transform
    anchored: true
  - type: NodeContainer
    nodes:
      output:
        !type:CableDeviceNode
        nodeGroupID: HVPower
  - type: PowerNetworkBattery
  - type: Battery
    netsync: false
  - type: BatteryDischarger

- type: entity
  id: FullBatteryDummy
  components:
  - type: Transform
    anchored: true
  - type: NodeContainer
    nodes:
      output:
        !type:CableDeviceNode
        nodeGroupID: HVPower
      input:
        !type:CableTerminalPortNode
        nodeGroupID: HVPower
  - type: PowerNetworkBattery
  - type: Battery
    netsync: false
  - type: BatteryDischarger
    node: output
  - type: BatteryCharger
    node: input

- type: entity
  id: SubstationDummy
  components:
  - type: NodeContainer
    nodes:
      input:
        !type:CableDeviceNode
        nodeGroupID: HVPower
      output:
        !type:CableDeviceNode
        nodeGroupID: MVPower
  - type: BatteryCharger
    voltage: High
  - type: BatteryDischarger
    voltage: Medium
  - type: PowerNetworkBattery
    maxChargeRate: 1000
    maxSupply: 1000
    supplyRampTolerance: 1000
  - type: Battery
    netsync: false
    maxCharge: 1000
    startingCharge: 1000
  - type: Transform
    anchored: true

- type: entity
  id: ApcDummy
  components:
  - type: Battery
    netsync: false
    maxCharge: 10000
    startingCharge: 10000
  - type: PowerNetworkBattery
    maxChargeRate: 1000
    maxSupply: 1000
    supplyRampTolerance: 1000
  - type: BatteryCharger
    voltage: Medium
  - type: BatteryDischarger
    voltage: Apc
  - type: Apc
    voltage: Apc
  - type: NodeContainer
    nodes:
      input:
        !type:CableDeviceNode
        nodeGroupID: MVPower
      output:
        !type:CableDeviceNode
        nodeGroupID: Apc
  - type: Transform
    anchored: true
  - type: UserInterface
    interfaces:
      enum.ApcUiKey.Key:
        type: ApcBoundUserInterface
  - type: AccessReader
    access: [['Engineering']]

- type: entity
  id: ApcPowerReceiverDummy
  components:
  - type: ApcPowerReceiver
  - type: ExtensionCableReceiver
  - type: Transform
    anchored: true
";
        /// <summary>
        ///     Test small power net with a simple surplus of power over the loads.
        /// </summary>
        [Test]
        public async Task TestSimpleSurplus()
        {
            var pair = Pair;
            var server = pair.Server;
            var mapManager = server.ResolveDependency<IMapManager>();
            var entityManager = server.ResolveDependency<IEntityManager>();
            var mapSys = entityManager.System<SharedMapSystem>();
            const float loadPower = 200;
            PowerSupplierComponent supplier = default!;
            PowerConsumerComponent consumer1 = default!;
            PowerConsumerComponent consumer2 = default!;

            await server.WaitAssertion(() =>
            {
                var map = mapSys.CreateMap(out var mapId);
                var grid = mapManager.CreateGridEntity(mapId);

                // Power only works when anchored
                for (var i = 0; i < 3; i++)
                {
                    mapSys.SetTile(grid, new Vector2i(0, i), new Tile(1));
                    entityManager.SpawnEntity("CableHV", grid.Owner.ToCoordinates(0, i));
                }

                var generatorEnt = entityManager.SpawnEntity("GeneratorDummy", grid.Owner.ToCoordinates());
                var consumerEnt1 = entityManager.SpawnEntity("ConsumerDummy", grid.Owner.ToCoordinates(0, 1));
                var consumerEnt2 = entityManager.SpawnEntity("ConsumerDummy", grid.Owner.ToCoordinates(0, 2));

                supplier = entityManager.GetComponent<PowerSupplierComponent>(generatorEnt);
                consumer1 = entityManager.GetComponent<PowerConsumerComponent>(consumerEnt1);
                consumer2 = entityManager.GetComponent<PowerConsumerComponent>(consumerEnt2);

                // Plenty of surplus and tolerance
                supplier.MaxSupply = loadPower * 4;
                supplier.SupplyRampTolerance = loadPower * 4;
                consumer1.DrawRate = loadPower;
                consumer2.DrawRate = loadPower;
            });

            server.RunTicks(1); //let run a tick for PowerNet to process power

            await server.WaitAssertion(() =>
            {
                Assert.Multiple(() =>
                {
                    // Assert both consumers fully powered
                    Assert.That(consumer1.ReceivedPower, Is.EqualTo(consumer1.DrawRate).Within(0.1));
                    Assert.That(consumer2.ReceivedPower, Is.EqualTo(consumer2.DrawRate).Within(0.1));

                    // Assert that load adds up on supply.
                    Assert.That(supplier.CurrentSupply, Is.EqualTo(loadPower * 2).Within(0.1));
                });
            });
        }


        /// <summary>
        ///     Test small power net with a simple deficit of power over the loads.
        /// </summary>
        [Test]
        public async Task TestSimpleDeficit()
        {
            var pair = Pair;
            var server = pair.Server;
            var mapManager = server.ResolveDependency<IMapManager>();
            var entityManager = server.ResolveDependency<IEntityManager>();
            var mapSys = entityManager.System<SharedMapSystem>();
            const float loadPower = 200;
            PowerSupplierComponent supplier = default!;
            PowerConsumerComponent consumer1 = default!;
            PowerConsumerComponent consumer2 = default!;

            await server.WaitAssertion(() =>
            {
                var map = mapSys.CreateMap(out var mapId);
                var grid = mapManager.CreateGridEntity(mapId);

                // Power only works when anchored
                for (var i = 0; i < 3; i++)
                {
                    mapSys.SetTile(grid, new Vector2i(0, i), new Tile(1));
                    entityManager.SpawnEntity("CableHV", grid.Owner.ToCoordinates(0, i));
                }

                var generatorEnt = entityManager.SpawnEntity("GeneratorDummy", grid.Owner.ToCoordinates());
                var consumerEnt1 = entityManager.SpawnEntity("ConsumerDummy", grid.Owner.ToCoordinates(0, 1));
                var consumerEnt2 = entityManager.SpawnEntity("ConsumerDummy", grid.Owner.ToCoordinates(0, 2));

                supplier = entityManager.GetComponent<PowerSupplierComponent>(generatorEnt);
                consumer1 = entityManager.GetComponent<PowerConsumerComponent>(consumerEnt1);
                consumer2 = entityManager.GetComponent<PowerConsumerComponent>(consumerEnt2);

                // Too little supply, both consumers should get 33% power.
                supplier.MaxSupply = loadPower;
                supplier.SupplyRampTolerance = loadPower;
                consumer1.DrawRate = loadPower;
                consumer2.DrawRate = loadPower * 2;
            });

            server.RunTicks(1); //let run a tick for PowerNet to process power

            await server.WaitAssertion(() =>
            {
                Assert.Multiple(() =>
                {
                    // Assert both consumers get 33% power.
                    Assert.That(consumer1.ReceivedPower, Is.EqualTo(consumer1.DrawRate / 3).Within(0.1));
                    Assert.That(consumer2.ReceivedPower, Is.EqualTo(consumer2.DrawRate / 3).Within(0.1));

                    // Supply should be maxed out
                    Assert.That(supplier.CurrentSupply, Is.EqualTo(supplier.MaxSupply).Within(0.1));
                });
            });
        }

        [Test]
        public async Task TestSupplyRamp()
        {
            var pair = Pair;
            var server = pair.Server;
            var mapManager = server.ResolveDependency<IMapManager>();
            var entityManager = server.ResolveDependency<IEntityManager>();
            var mapSys = entityManager.System<SharedMapSystem>();
            var gameTiming = server.ResolveDependency<IGameTiming>();
            PowerSupplierComponent supplier = default!;
            PowerConsumerComponent consumer = default!;

            await server.WaitAssertion(() =>
            {
                var map = mapSys.CreateMap(out var mapId);
                var grid = mapManager.CreateGridEntity(mapId);

                // Power only works when anchored
                for (var i = 0; i < 3; i++)
                {
                    mapSys.SetTile(grid, new Vector2i(0, i), new Tile(1));
                    entityManager.SpawnEntity("CableHV", grid.Owner.ToCoordinates(0, i));
                }

                var generatorEnt = entityManager.SpawnEntity("GeneratorDummy", grid.Owner.ToCoordinates());
                var consumerEnt = entityManager.SpawnEntity("ConsumerDummy", grid.Owner.ToCoordinates(0, 2));

                supplier = entityManager.GetComponent<PowerSupplierComponent>(generatorEnt);
                consumer = entityManager.GetComponent<PowerConsumerComponent>(consumerEnt);

                // Supply has enough total power but needs to ramp up to match.
                supplier.MaxSupply = 400;
                supplier.SupplyRampRate = 400;
                supplier.SupplyRampTolerance = 100;
                consumer.DrawRate = 400;
            });

            // Exact values can/will be off by a tick, add tolerance for that.
            var tickPeriod = (float) gameTiming.TickPeriod.TotalSeconds;
            var tickDev = 400 * tickPeriod * 1.1f;

            server.RunTicks(1);

            await server.WaitAssertion(() =>
            {
                Assert.Multiple(() =>
                {
                    // First tick, supply should be delivering 100 W (max tolerance) and start ramping up.
                    Assert.That(supplier.CurrentSupply, Is.EqualTo(100).Within(0.1));
                    Assert.That(consumer.ReceivedPower, Is.EqualTo(100).Within(0.1));
                });
            });

            // run for 0.25 seconds (minus the previous tick)
            var ticks = (int) Math.Round(0.25 * gameTiming.TickRate) - 1;
            server.RunTicks(ticks);

            await server.WaitAssertion(() =>
            {
                Assert.Multiple(() =>
                {
                    // After 15 ticks (0.25 seconds), supply ramp pos should be at 100 W and supply at 100, approx.
                    Assert.That(supplier.CurrentSupply, Is.EqualTo(200).Within(tickDev));
                    Assert.That(supplier.SupplyRampPosition, Is.EqualTo(100).Within(tickDev));
                    Assert.That(consumer.ReceivedPower, Is.EqualTo(200).Within(tickDev));
                });
            });



            // run for 0.75 seconds
            ticks = (int) Math.Round(0.75 * gameTiming.TickRate);
            server.RunTicks(ticks);

            await server.WaitAssertion(() =>
            {
                Assert.Multiple(() =>
                {
                    // After 1 second total, ramp should be at 400 and supply should be at 400, everybody happy.
                    Assert.That(supplier.CurrentSupply, Is.EqualTo(400).Within(tickDev));
                    Assert.That(supplier.SupplyRampPosition, Is.EqualTo(400).Within(tickDev));
                    Assert.That(consumer.ReceivedPower, Is.EqualTo(400).Within(tickDev));
                });
            });
        }

        [Test]
        public async Task TestBatteryRamp()
        {
            var pair = Pair;
            var server = pair.Server;
            var mapManager = server.ResolveDependency<IMapManager>();
            var entityManager = server.ResolveDependency<IEntityManager>();
            var gameTiming = server.ResolveDependency<IGameTiming>();
            var batterySys = entityManager.System<BatterySystem>();
            var mapSys = entityManager.System<SharedMapSystem>();
            const float startingCharge = 100_000;

            PowerNetworkBatteryComponent netBattery = default!;
            EntityUid generatorEnt = default!;
            EntityUid consumerEnt = default!;
            BatteryComponent battery = default!;
            PowerConsumerComponent consumer = default!;

            await server.WaitAssertion(() =>
            {
                var map = mapSys.CreateMap(out var mapId);
                var grid = mapManager.CreateGridEntity(mapId);

                // Power only works when anchored
                for (var i = 0; i < 3; i++)
                {
                    mapSys.SetTile(grid, new Vector2i(0, i), new Tile(1));
                    entityManager.SpawnEntity("CableHV", grid.Owner.ToCoordinates(0, i));
                }

                generatorEnt = entityManager.SpawnEntity("DischargingBatteryDummy", grid.Owner.ToCoordinates());
                consumerEnt = entityManager.SpawnEntity("ConsumerDummy", grid.Owner.ToCoordinates(0, 2));

                netBattery = entityManager.GetComponent<PowerNetworkBatteryComponent>(generatorEnt);
                battery = entityManager.GetComponent<BatteryComponent>(generatorEnt);
                consumer = entityManager.GetComponent<PowerConsumerComponent>(consumerEnt);

                batterySys.SetMaxCharge((generatorEnt, battery), startingCharge);
                batterySys.SetCharge((generatorEnt, battery), startingCharge);
                netBattery.MaxSupply = 400;
                netBattery.SupplyRampRate = 400;
                netBattery.SupplyRampTolerance = 100;
                consumer.DrawRate = 400;
            });

            // Exact values can/will be off by a tick, add tolerance for that.
            var tickPeriod = (float) gameTiming.TickPeriod.TotalSeconds;
            var tickDev = 400 * tickPeriod * 1.1f;

            server.RunTicks(1);

            await server.WaitAssertion(() =>
            {
                Assert.Multiple(() =>
                {
                    // First tick, supply should be delivering 100 W (max tolerance) and start ramping up.
                    Assert.That(netBattery.CurrentSupply, Is.EqualTo(100).Within(0.1));
                    Assert.That(consumer.ReceivedPower, Is.EqualTo(100).Within(0.1));
                });
            });

            // run for 0.25 seconds (minus the previous tick)
            var ticks = (int) Math.Round(0.25 * gameTiming.TickRate) - 1;
            server.RunTicks(ticks);

            await server.WaitAssertion(() =>
            {
                Assert.Multiple(() =>
                {
                    // After 15 ticks (0.25 seconds), supply ramp pos should be at 100 W and supply at 100, approx.
                    Assert.That(netBattery.CurrentSupply, Is.EqualTo(200).Within(tickDev));
                    Assert.That(netBattery.SupplyRampPosition, Is.EqualTo(100).Within(tickDev));
                    Assert.That(consumer.ReceivedPower, Is.EqualTo(200).Within(tickDev));

                    // Trivial integral to calculate expected power spent.
                    const double spentExpected = (200 + 100) / 2.0 * 0.25;
                    var currentCharge = batterySys.GetCharge((generatorEnt, battery));
                    Assert.That(currentCharge, Is.EqualTo(startingCharge - spentExpected).Within(tickDev));
                });
            });

            // run for 0.75 seconds
            ticks = (int) Math.Round(0.75 * gameTiming.TickRate);
            server.RunTicks(ticks);

            await server.WaitAssertion(() =>
            {
                Assert.Multiple(() =>
                {
                    // After 1 second total, ramp should be at 400 and supply should be at 400, everybody happy.
                    Assert.That(netBattery.CurrentSupply, Is.EqualTo(400).Within(tickDev));
                    Assert.That(netBattery.SupplyRampPosition, Is.EqualTo(400).Within(tickDev));
                    Assert.That(consumer.ReceivedPower, Is.EqualTo(400).Within(tickDev));

                    // Trivial integral to calculate expected power spent.
                    const double spentExpected = (400 + 100) / 2.0 * 0.75 + 400 * 0.25;
                    var currentCharge = batterySys.GetCharge((generatorEnt, battery));
                    Assert.That(currentCharge, Is.EqualTo(startingCharge - spentExpected).Within(tickDev));
                });
            });
        }

        [Test]
        public async Task TestNoDemandRampdown()
        {
            // checks that batteries and supplies properly ramp down if the load is disconnected/disabled.

            var pair = Pair;
            var server = pair.Server;
            var mapManager = server.ResolveDependency<IMapManager>();
            var entityManager = server.ResolveDependency<IEntityManager>();
            var batterySys = entityManager.System<BatterySystem>();
            var mapSys = entityManager.System<SharedMapSystem>();
            PowerSupplierComponent supplier = default!;
            PowerNetworkBatteryComponent netBattery = default!;
            BatteryComponent battery = default!;
            PowerConsumerComponent consumer = default!;

            var rampRate = 500;
            var rampTol = 100;
            var draw = 1000;

            await server.WaitAssertion(() =>
            {
                var map = mapSys.CreateMap(out var mapId);
                var grid = mapManager.CreateGridEntity(mapId);

                // Power only works when anchored
                for (var i = 0; i < 3; i++)
                {
                    mapSys.SetTile(grid, new Vector2i(0, i), new Tile(1));
                    entityManager.SpawnEntity("CableHV", grid.Owner.ToCoordinates(0, i));
                }

                var generatorEnt = entityManager.SpawnEntity("GeneratorDummy", grid.Owner.ToCoordinates());
                var consumerEnt = entityManager.SpawnEntity("ConsumerDummy", grid.Owner.ToCoordinates(0, 1));
                var batteryEnt = entityManager.SpawnEntity("DischargingBatteryDummy", grid.Owner.ToCoordinates(0, 2));
                netBattery = entityManager.GetComponent<PowerNetworkBatteryComponent>(batteryEnt);
                battery = entityManager.GetComponent<BatteryComponent>(batteryEnt);
                supplier = entityManager.GetComponent<PowerSupplierComponent>(generatorEnt);
                consumer = entityManager.GetComponent<PowerConsumerComponent>(consumerEnt);

                consumer.DrawRate = draw;

                supplier.MaxSupply = draw / 2;
                supplier.SupplyRampRate = rampRate;
                supplier.SupplyRampTolerance = rampTol;

                batterySys.SetMaxCharge((batteryEnt, battery), 100_000);
                batterySys.SetCharge((batteryEnt, battery), 100_000);
                netBattery.MaxSupply = draw / 2;
                netBattery.SupplyRampRate = rampRate;
                netBattery.SupplyRampTolerance = rampTol;
            });

            server.RunTicks(1);

            await server.WaitAssertion(() =>
            {
                Assert.Multiple(() =>
                {
                    Assert.That(supplier.CurrentSupply, Is.EqualTo(rampTol).Within(0.1));
                    Assert.That(netBattery.CurrentSupply, Is.EqualTo(rampTol).Within(0.1));
                    Assert.That(consumer.ReceivedPower, Is.EqualTo(rampTol * 2).Within(0.1));
                });
            });

            server.RunTicks(60);

            await server.WaitAssertion(() =>
            {
                Assert.Multiple(() =>
                {
                    Assert.That(supplier.CurrentSupply, Is.EqualTo(draw / 2).Within(0.1));
                    Assert.That(supplier.SupplyRampPosition, Is.EqualTo(draw / 2).Within(0.1));
                    Assert.That(netBattery.CurrentSupply, Is.EqualTo(draw / 2).Within(0.1));
                    Assert.That(netBattery.SupplyRampPosition, Is.EqualTo(draw / 2).Within(0.1));
                    Assert.That(consumer.ReceivedPower, Is.EqualTo(draw).Within(0.1));
                });
            });

            // now we disconnect the load;
            consumer.NetworkLoad.Enabled = false;

            server.RunTicks(60);

            await server.WaitAssertion(() =>
            {
                Assert.Multiple(() =>
                {
                    Assert.That(supplier.CurrentSupply, Is.EqualTo(0).Within(0.1));
                    Assert.That(supplier.SupplyRampPosition, Is.EqualTo(0).Within(0.1));
                    Assert.That(netBattery.CurrentSupply, Is.EqualTo(0).Within(0.1));
                    Assert.That(netBattery.SupplyRampPosition, Is.EqualTo(0).Within(0.1));
                    Assert.That(consumer.ReceivedPower, Is.EqualTo(0).Within(0.1));
                });
            });
        }

        [Test]
        public async Task TestSimpleBatteryChargeDeficit()
        {
            var pair = Pair;
            var server = pair.Server;
            var mapManager = server.ResolveDependency<IMapManager>();
            var gameTiming = server.ResolveDependency<IGameTiming>();
            var entityManager = server.ResolveDependency<IEntityManager>();
            var batterySys = entityManager.System<BatterySystem>();
            var mapSys = entityManager.System<SharedMapSystem>();
            EntityUid generatorEnt = default!;
            EntityUid batteryEnt = default!;
            PowerSupplierComponent supplier = default!;
            BatteryComponent battery = default!;

            await server.WaitAssertion(() =>
            {
                var map = mapSys.CreateMap(out var mapId);
                var grid = mapManager.CreateGridEntity(mapId);

                // Power only works when anchored
                for (var i = 0; i < 3; i++)
                {
                    mapSys.SetTile(grid, new Vector2i(0, i), new Tile(1));
                    entityManager.SpawnEntity("CableHV", grid.Owner.ToCoordinates(0, i));
                }

                generatorEnt = entityManager.SpawnEntity("GeneratorDummy", grid.Owner.ToCoordinates());
                batteryEnt = entityManager.SpawnEntity("ChargingBatteryDummy", grid.Owner.ToCoordinates(0, 2));

                supplier = entityManager.GetComponent<PowerSupplierComponent>(generatorEnt);
                var netBattery = entityManager.GetComponent<PowerNetworkBatteryComponent>(batteryEnt);
                battery = entityManager.GetComponent<BatteryComponent>(batteryEnt);

                supplier.MaxSupply = 500;
                supplier.SupplyRampTolerance = 500;
                batterySys.SetMaxCharge((batteryEnt, battery), 100_000);
                netBattery.MaxChargeRate = 1_000;
                netBattery.Efficiency = 0.5f;
            });

            // run for 0.5 seconds
            var ticks = (int) Math.Round(0.5 * gameTiming.TickRate);
            server.RunTicks(ticks);

            await server.WaitAssertion(() =>
            {
                Assert.Multiple(() =>
                {
                    // half a second @ 500 W = 250
                    // 50% efficiency, so 125 J stored total.
                    var currentCharge = batterySys.GetCharge((batteryEnt, battery));
                    Assert.That(currentCharge, Is.EqualTo(125).Within(0.1));
                    Assert.That(supplier.CurrentSupply, Is.EqualTo(500).Within(0.1));
                });
            });
        }

        [Test]
        public async Task TestFullBattery()
        {
            var pair = Pair;
            var server = pair.Server;
            var mapManager = server.ResolveDependency<IMapManager>();
            var entityManager = server.ResolveDependency<IEntityManager>();
            var gameTiming = server.ResolveDependency<IGameTiming>();
            var batterySys = entityManager.System<BatterySystem>();
            var mapSys = entityManager.System<SharedMapSystem>();
            EntityUid batteryEnt = default!;
            EntityUid supplyEnt = default!;
            EntityUid consumerEnt = default!;
            PowerConsumerComponent consumer = default!;
            PowerSupplierComponent supplier = default!;
            PowerNetworkBatteryComponent netBattery = default!;
            BatteryComponent battery = default!;

            await server.WaitAssertion(() =>
            {
                var map = mapSys.CreateMap(out var mapId);
                var grid = mapManager.CreateGridEntity(mapId);

                // Power only works when anchored
                for (var i = 0; i < 4; i++)
                {
                    mapSys.SetTile(grid, new Vector2i(0, i), new Tile(1));
                    entityManager.SpawnEntity("CableHV", grid.Owner.ToCoordinates(0, i));
                }

                var terminal = entityManager.SpawnEntity("CableTerminal", grid.Owner.ToCoordinates(0, 1));
                entityManager.GetComponent<TransformComponent>(terminal).LocalRotation = Angle.FromDegrees(180);

                batteryEnt = entityManager.SpawnEntity("FullBatteryDummy", grid.Owner.ToCoordinates(0, 2));
                supplyEnt = entityManager.SpawnEntity("GeneratorDummy", grid.Owner.ToCoordinates(0, 0));
                consumerEnt = entityManager.SpawnEntity("ConsumerDummy", grid.Owner.ToCoordinates(0, 3));

                consumer = entityManager.GetComponent<PowerConsumerComponent>(consumerEnt);
                supplier = entityManager.GetComponent<PowerSupplierComponent>(supplyEnt);
                netBattery = entityManager.GetComponent<PowerNetworkBatteryComponent>(batteryEnt);
                battery = entityManager.GetComponent<BatteryComponent>(batteryEnt);

                // Consumer (behind the bridge battery) needs 1000 W, but the battery is rated for only 400 W
                // and supplies entirely from its own storage, so the consumer is capped at 400 W.
                consumer.DrawRate = 1000;
                supplier.MaxSupply = 800;
                supplier.SupplyRampTolerance = 800;

                netBattery.MaxSupply = 400;
                netBattery.SupplyRampTolerance = 400;
                netBattery.SupplyRampRate = 100_000;
                batterySys.SetMaxCharge((batteryEnt, battery), 100_000);
                batterySys.SetCharge((batteryEnt, battery), 100_000);
            });

            // Run some ticks so everything is stable.
            server.RunTicks(gameTiming.TickRate);

            await server.WaitAssertion(() =>
            {
                Assert.Multiple(() =>
                {
                    // The battery's rated output caps throughput; it cannot relay more than MaxSupply.
                    Assert.That(consumer.ReceivedPower, Is.EqualTo(400).Within(0.5));
                    Assert.That(netBattery.CurrentSupply, Is.EqualTo(400).Within(0.5));
                    Assert.That(netBattery.SupplyRampPosition, Is.EqualTo(400).Within(0.5));
                });
            });
        }

        [Test]
        public async Task TestFullBatteryEfficiencyDemandPassThrough()
        {
            var pair = Pair;
            var server = pair.Server;
            var mapManager = server.ResolveDependency<IMapManager>();
            var entityManager = server.ResolveDependency<IEntityManager>();
            var batterySys = entityManager.System<BatterySystem>();
            var mapSys = entityManager.System<SharedMapSystem>();
            PowerConsumerComponent consumer1 = default!;
            PowerConsumerComponent consumer2 = default!;
            PowerSupplierComponent supplier = default!;

            await server.WaitAssertion(() =>
            {
                var map = mapSys.CreateMap(out var mapId);
                var grid = mapManager.CreateGridEntity(mapId);

                // Map layout here is
                // C - consumer
                // B - battery
                // G - generator
                // B - battery
                // C - consumer
                // Connected in the only way that makes sense.

                // Power only works when anchored
                for (var i = 0; i < 5; i++)
                {
                    mapSys.SetTile(grid, new Vector2i(0, i), new Tile(1));
                    entityManager.SpawnEntity("CableHV", grid.Owner.ToCoordinates(0, i));
                }

                entityManager.SpawnEntity("CableTerminal", grid.Owner.ToCoordinates(0, 2));
                var terminal = entityManager.SpawnEntity("CableTerminal", grid.Owner.ToCoordinates(0, 2));
                entityManager.GetComponent<TransformComponent>(terminal).LocalRotation = Angle.FromDegrees(180);

                var batteryEnt1 = entityManager.SpawnEntity("FullBatteryDummy", grid.Owner.ToCoordinates(0, 1));
                var batteryEnt2 = entityManager.SpawnEntity("FullBatteryDummy", grid.Owner.ToCoordinates(0, 3));
                var supplyEnt = entityManager.SpawnEntity("GeneratorDummy", grid.Owner.ToCoordinates(0, 2));
                var consumerEnt1 = entityManager.SpawnEntity("ConsumerDummy", grid.Owner.ToCoordinates(0, 0));
                var consumerEnt2 = entityManager.SpawnEntity("ConsumerDummy", grid.Owner.ToCoordinates(0, 4));

                consumer1 = entityManager.GetComponent<PowerConsumerComponent>(consumerEnt1);
                consumer2 = entityManager.GetComponent<PowerConsumerComponent>(consumerEnt2);
                supplier = entityManager.GetComponent<PowerSupplierComponent>(supplyEnt);
                var netBattery1 = entityManager.GetComponent<PowerNetworkBatteryComponent>(batteryEnt1);
                var netBattery2 = entityManager.GetComponent<PowerNetworkBatteryComponent>(batteryEnt2);
                var battery1 = entityManager.GetComponent<BatteryComponent>(batteryEnt1);
                var battery2 = entityManager.GetComponent<BatteryComponent>(batteryEnt2);

                // There are two loads, 500 W and 1000 W respectively.
                // The 500 W load is behind a 50% efficient battery,
                // so *effectively* it needs 2x as much power from the supply to run.
                // Assert that both are getting 50% power.
                // Batteries are empty and only a bridge.

                consumer1.DrawRate = 500;
                consumer2.DrawRate = 1000;
                supplier.MaxSupply = 1000;
                supplier.SupplyRampTolerance = 1000;

                batterySys.SetMaxCharge((batteryEnt1, battery1), 1_000_000);
                batterySys.SetMaxCharge((batteryEnt2, battery2), 1_000_000);

                netBattery1.MaxChargeRate = 1_000;
                netBattery2.MaxChargeRate = 1_000;

                netBattery1.Efficiency = 0.5f;

                netBattery1.MaxSupply = 1_000_000;
                netBattery2.MaxSupply = 1_000_000;

                netBattery1.SupplyRampTolerance = 1_000_000;
                netBattery2.SupplyRampTolerance = 1_000_000;
            });

            // Run some ticks so everything is stable.
            server.RunTicks(10);

            await server.WaitAssertion(() =>
            {
                Assert.Multiple(() =>
                {
                    Assert.That(consumer1.ReceivedPower, Is.EqualTo(250).Within(0.1));
                    Assert.That(consumer2.ReceivedPower, Is.EqualTo(500).Within(0.1));
                    Assert.That(supplier.CurrentSupply, Is.EqualTo(supplier.MaxSupply).Within(0.1));
                });
            });
        }

        /// <summary>
        ///     Checks that if there is insufficient supply to meet demand, generators will run at full power instead of
        ///     having generators and batteries sharing the load.
        /// </summary>
        [Test]
        public async Task TestSupplyPrioritized()
        {
            var pair = Pair;
            var server = pair.Server;
            var mapManager = server.ResolveDependency<IMapManager>();
            var entityManager = server.ResolveDependency<IEntityManager>();
            var gameTiming = server.ResolveDependency<IGameTiming>();
            var batterySys = entityManager.System<BatterySystem>();
            var mapSys = entityManager.System<SharedMapSystem>();
            PowerConsumerComponent consumer = default!;
            PowerSupplierComponent supplier1 = default!;
            PowerSupplierComponent supplier2 = default!;
            PowerNetworkBatteryComponent netBattery1 = default!;
            PowerNetworkBatteryComponent netBattery2 = default!;
            BatteryComponent battery1 = default!;
            BatteryComponent battery2 = default!;

            await server.WaitAssertion(() =>
            {
                var map = mapSys.CreateMap(out var mapId);
                var grid = mapManager.CreateGridEntity(mapId);

                // Layout is two generators, two batteries, and one load. As to why two: because previously this test
                // would fail ONLY if there were more than two batteries present, because each of them tries to supply
                // the unmet load, leading to a double-battery supply attempt and ramping down of power generation from
                // supplies.

                // Actual layout is Battery Supply, Load, Supply,  Battery

                // Place cables
                for (var i = -2; i <= 2; i++)
                {
                    mapSys.SetTile(grid, new Vector2i(0, i), new Tile(1));
                    entityManager.SpawnEntity("CableHV", grid.Owner.ToCoordinates(0, i));
                }

                var batteryEnt1 = entityManager.SpawnEntity("FullBatteryDummy", grid.Owner.ToCoordinates(0, 2));
                var batteryEnt2 = entityManager.SpawnEntity("FullBatteryDummy", grid.Owner.ToCoordinates(0, -2));

                var supplyEnt1 = entityManager.SpawnEntity("GeneratorDummy", grid.Owner.ToCoordinates(0, 1));
                var supplyEnt2 = entityManager.SpawnEntity("GeneratorDummy", grid.Owner.ToCoordinates(0, -1));

                var consumerEnt = entityManager.SpawnEntity("ConsumerDummy", grid.Owner.ToCoordinates(0, 0));

                consumer = entityManager.GetComponent<PowerConsumerComponent>(consumerEnt);
                supplier1 = entityManager.GetComponent<PowerSupplierComponent>(supplyEnt1);
                supplier2 = entityManager.GetComponent<PowerSupplierComponent>(supplyEnt2);
                netBattery1 = entityManager.GetComponent<PowerNetworkBatteryComponent>(batteryEnt1);
                netBattery2 = entityManager.GetComponent<PowerNetworkBatteryComponent>(batteryEnt2);
                battery1 = entityManager.GetComponent<BatteryComponent>(batteryEnt1);
                battery2 = entityManager.GetComponent<BatteryComponent>(batteryEnt2);

                // Consumer wants 2k, supplies can only provide 1k (500 each). Expectation is that batteries will only provide the necessary remaining 1k (500 each).
                // Previously this failed with a 2x 333 w supplies and 2x 666 w batteries.

                consumer.DrawRate = 2000;

                supplier1.MaxSupply = 500;
                supplier2.MaxSupply = 500;
                supplier1.SupplyRampTolerance = 500;
                supplier2.SupplyRampTolerance = 500;

                netBattery1.MaxSupply = 1000;
                netBattery2.MaxSupply = 1000;
                netBattery1.SupplyRampTolerance = 1000;
                netBattery2.SupplyRampTolerance = 1000;
                netBattery1.SupplyRampRate = 100_000;
                netBattery2.SupplyRampRate = 100_000;
                batterySys.SetMaxCharge((batteryEnt1, battery1), 100_000);
                batterySys.SetMaxCharge((batteryEnt2, battery2), 100_000);
                batterySys.SetCharge((batteryEnt1, battery1), 100_000);
                batterySys.SetCharge((batteryEnt2, battery2), 100_000);
            });

            // Run some ticks so everything is stable.
            server.RunTicks(60);

            await server.WaitAssertion(() =>
            {
                Assert.Multiple(() =>
                {
                    Assert.That(consumer.ReceivedPower, Is.EqualTo(consumer.DrawRate).Within(0.1));
                    Assert.That(supplier1.CurrentSupply, Is.EqualTo(supplier1.MaxSupply).Within(0.1));
                    Assert.That(supplier2.CurrentSupply, Is.EqualTo(supplier2.MaxSupply).Within(0.1));

                    Assert.That(netBattery1.CurrentSupply, Is.EqualTo(500).Within(0.1));
                    Assert.That(netBattery2.CurrentSupply, Is.EqualTo(500).Within(0.1));
                    Assert.That(netBattery2.SupplyRampPosition, Is.EqualTo(500).Within(0.1));
                    Assert.That(netBattery2.SupplyRampPosition, Is.EqualTo(500).Within(0.1));
                });
            });
        }

        /// <summary>
        ///     Test that power is distributed proportionally, even through batteries.
        /// </summary>
        [Test]
        public async Task TestBatteriesProportional()
        {
            var pair = Pair;
            var server = pair.Server;
            var mapManager = server.ResolveDependency<IMapManager>();
            var entityManager = server.ResolveDependency<IEntityManager>();
            var batterySys = entityManager.System<BatterySystem>();
            var mapSys = entityManager.System<SharedMapSystem>();
            PowerConsumerComponent consumer1 = default!;
            PowerConsumerComponent consumer2 = default!;
            PowerSupplierComponent supplier = default!;

            await server.WaitAssertion(() =>
            {
                var map = mapSys.CreateMap(out var mapId);
                var grid = mapManager.CreateGridEntity(mapId);

                // Map layout here is
                // C - consumer
                // B - battery
                // G - generator
                // B - battery
                // C - consumer
                // Connected in the only way that makes sense.

                // Power only works when anchored
                for (var i = 0; i < 5; i++)
                {
                    mapSys.SetTile(grid, new Vector2i(0, i), new Tile(1));
                    entityManager.SpawnEntity("CableHV", grid.Owner.ToCoordinates(0, i));
                }

                entityManager.SpawnEntity("CableTerminal", grid.Owner.ToCoordinates(0, 2));
                var terminal = entityManager.SpawnEntity("CableTerminal", grid.Owner.ToCoordinates(0, 2));
                entityManager.GetComponent<TransformComponent>(terminal).LocalRotation = Angle.FromDegrees(180);

                var batteryEnt1 = entityManager.SpawnEntity("FullBatteryDummy", grid.Owner.ToCoordinates(0, 1));
                var batteryEnt2 = entityManager.SpawnEntity("FullBatteryDummy", grid.Owner.ToCoordinates(0, 3));
                var supplyEnt = entityManager.SpawnEntity("GeneratorDummy", grid.Owner.ToCoordinates(0, 2));
                var consumerEnt1 = entityManager.SpawnEntity("ConsumerDummy", grid.Owner.ToCoordinates(0, 0));
                var consumerEnt2 = entityManager.SpawnEntity("ConsumerDummy", grid.Owner.ToCoordinates(0, 4));

                consumer1 = entityManager.GetComponent<PowerConsumerComponent>(consumerEnt1);
                consumer2 = entityManager.GetComponent<PowerConsumerComponent>(consumerEnt2);
                supplier = entityManager.GetComponent<PowerSupplierComponent>(supplyEnt);
                var netBattery1 = entityManager.GetComponent<PowerNetworkBatteryComponent>(batteryEnt1);
                var netBattery2 = entityManager.GetComponent<PowerNetworkBatteryComponent>(batteryEnt2);
                var battery1 = entityManager.GetComponent<BatteryComponent>(batteryEnt1);
                var battery2 = entityManager.GetComponent<BatteryComponent>(batteryEnt2);

                consumer1.DrawRate = 500;
                consumer2.DrawRate = 1000;
                supplier.MaxSupply = 1000;
                supplier.SupplyRampTolerance = 1000;

                batterySys.SetMaxCharge((batteryEnt1, battery1), 1_000_000);
                batterySys.SetMaxCharge((batteryEnt2, battery2), 1_000_000);

                netBattery1.MaxChargeRate = 20;
                netBattery2.MaxChargeRate = 20;

                netBattery1.MaxSupply = 1_000_000;
                netBattery2.MaxSupply = 1_000_000;

                netBattery1.SupplyRampTolerance = 1_000_000;
                netBattery2.SupplyRampTolerance = 1_000_000;
            });

            // Run some ticks so everything is stable.
            server.RunTicks(60);

            await server.WaitAssertion(() =>
            {
                Assert.Multiple(() =>
                {
                    // NOTE: MaxChargeRate on batteries actually skews the demand.
                    // So that's why the tolerance is so high, the charge rate is so *low*,
                    // and we run so many ticks to stabilize.
                    Assert.That(consumer1.ReceivedPower, Is.EqualTo(333.333).Within(10));
                    Assert.That(consumer2.ReceivedPower, Is.EqualTo(666.666).Within(10));
                    Assert.That(supplier.CurrentSupply, Is.EqualTo(supplier.MaxSupply).Within(0.1));
                });
            });
        }

        /// <summary>
        ///     Test that <see cref="CableTerminalNode"/> correctly isolates two networks.
        /// </summary>
        [Test]
        public async Task TestTerminalNodeGroups()
        {
            var pair = Pair;
            var server = pair.Server;
            var mapManager = server.ResolveDependency<IMapManager>();
            var entityManager = server.ResolveDependency<IEntityManager>();
            var nodeContainer = entityManager.System<NodeContainerSystem>();
            var mapSys = entityManager.System<SharedMapSystem>();
            CableNode leftNode = default!;
            CableNode rightNode = default!;
            Node batteryInput = default!;
            Node batteryOutput = default!;

            await server.WaitAssertion(() =>
            {
                var map = mapSys.CreateMap(out var mapId);
                var grid = mapManager.CreateGridEntity(mapId);

                // Power only works when anchored
                for (var i = 0; i < 4; i++)
                {
                    mapSys.SetTile(grid, new Vector2i(0, i), new Tile(1));
                }

                var leftEnt = entityManager.SpawnEntity("CableHV", grid.Owner.ToCoordinates(0, 0));
                entityManager.SpawnEntity("CableHV", grid.Owner.ToCoordinates(0, 1));
                entityManager.SpawnEntity("CableHV", grid.Owner.ToCoordinates(0, 2));
                var rightEnt = entityManager.SpawnEntity("CableHV", grid.Owner.ToCoordinates(0, 3));

                var terminal = entityManager.SpawnEntity("CableTerminal", grid.Owner.ToCoordinates(0, 1));
                entityManager.GetComponent<TransformComponent>(terminal).LocalRotation = Angle.FromDegrees(180);

                var battery = entityManager.SpawnEntity("FullBatteryDummy", grid.Owner.ToCoordinates(0, 2));
                var batteryNodeContainer = entityManager.GetComponent<NodeContainerComponent>(battery);

                if (nodeContainer.TryGetNode<CableNode>(entityManager.GetComponent<NodeContainerComponent>(leftEnt),
                        "power", out var leftN))
                    leftNode = leftN;
                if (nodeContainer.TryGetNode<CableNode>(entityManager.GetComponent<NodeContainerComponent>(rightEnt),
                        "power", out var rightN))
                    rightNode = rightN;

                if (nodeContainer.TryGetNode<Node>(batteryNodeContainer, "input", out var nInput))
                    batteryInput = nInput;
                if (nodeContainer.TryGetNode<Node>(batteryNodeContainer, "output", out var nOutput))
                    batteryOutput = nOutput;
            });

            // Run ticks to allow node groups to update.
            server.RunTicks(1);

            await server.WaitAssertion(() =>
            {
                Assert.Multiple(() =>
                {
                    Assert.That(batteryInput.NodeGroup, Is.EqualTo(leftNode.NodeGroup));
                    Assert.That(batteryOutput.NodeGroup, Is.EqualTo(rightNode.NodeGroup));

                    Assert.That(leftNode.NodeGroup, Is.Not.EqualTo(rightNode.NodeGroup));
                });
            });
        }

        [Test]
        public async Task ApcChargingTest()
        {
            var pair = Pair;
            var server = pair.Server;
            var mapManager = server.ResolveDependency<IMapManager>();
            var entityManager = server.ResolveDependency<IEntityManager>();
            var batterySys = entityManager.System<BatterySystem>();
            var mapSys = entityManager.System<SharedMapSystem>();
            EntityUid generatorEnt = default!;
            EntityUid substationEnt = default!;
            EntityUid apcEnt = default!;
            PowerNetworkBatteryComponent substationNetBattery = default!;
            BatteryComponent apcBattery = default!;

            await server.WaitAssertion(() =>
            {
                var map = mapSys.CreateMap(out var mapId);
                var grid = mapManager.CreateGridEntity(mapId);

                // Power only works when anchored
                for (var i = 0; i < 3; i++)
                {
                    mapSys.SetTile(grid, new Vector2i(0, i), new Tile(1));
                }

                entityManager.SpawnEntity("CableHV", grid.Owner.ToCoordinates(0, 0));
                entityManager.SpawnEntity("CableHV", grid.Owner.ToCoordinates(0, 1));
                entityManager.SpawnEntity("CableMV", grid.Owner.ToCoordinates(0, 1));
                entityManager.SpawnEntity("CableMV", grid.Owner.ToCoordinates(0, 2));

                generatorEnt = entityManager.SpawnEntity("GeneratorDummy", grid.Owner.ToCoordinates(0, 0));
                substationEnt = entityManager.SpawnEntity("SubstationDummy", grid.Owner.ToCoordinates(0, 1));
                apcEnt = entityManager.SpawnEntity("ApcDummy", grid.Owner.ToCoordinates(0, 2));

                var generatorSupplier = entityManager.GetComponent<PowerSupplierComponent>(generatorEnt);
                substationNetBattery = entityManager.GetComponent<PowerNetworkBatteryComponent>(substationEnt);
                apcBattery = entityManager.GetComponent<BatteryComponent>(apcEnt);

                generatorSupplier.MaxSupply = 1000;
                generatorSupplier.SupplyRampTolerance = 1000;

                batterySys.SetCharge((apcEnt, apcBattery), 0);
            });

            server.RunTicks(5); //let run a few ticks for PowerNets to reevaluate and start charging apc

            await server.WaitAssertion(() =>
            {
                Assert.Multiple(() =>
                {
                    var currentCharge = batterySys.GetCharge((apcEnt, apcBattery));
                    Assert.That(substationNetBattery.CurrentSupply, Is.GreaterThan(0)); //substation should be providing power
                    Assert.That(currentCharge, Is.GreaterThan(0)); //apc battery should have gained charge
                });
            });
        }

        [Test]
        public async Task ApcNetTest()
        {
            var pair = Pair;
            var server = pair.Server;
            var mapManager = server.ResolveDependency<IMapManager>();
            var entityManager = server.ResolveDependency<IEntityManager>();
            var batterySys = entityManager.System<BatterySystem>();
            var extensionCableSystem = entityManager.System<ExtensionCableSystem>();
            var mapSys = entityManager.System<SharedMapSystem>();
            PowerNetworkBatteryComponent apcNetBattery = default!;
            ApcPowerReceiverComponent receiver = default!;
            ApcPowerReceiverComponent unpoweredReceiver = default!;

            await server.WaitAssertion(() =>
            {
                var map = mapSys.CreateMap(out var mapId);
                var grid = mapManager.CreateGridEntity(mapId);

                const int range = 5;

                // Power only works when anchored
                for (var i = 0; i < range; i++)
                {
                    mapSys.SetTile(grid, new Vector2i(0, i), new Tile(1));
                }

                var apcEnt = entityManager.SpawnEntity("ApcDummy", grid.Owner.ToCoordinates(0, 0));
                var apcExtensionEnt = entityManager.SpawnEntity("CableApcExtension", grid.Owner.ToCoordinates(0, 0));

                // Create a powered receiver in range (range is 0 indexed)
                var powerReceiverEnt = entityManager.SpawnEntity("ApcPowerReceiverDummy", grid.Owner.ToCoordinates(0, range - 1));
                receiver = entityManager.GetComponent<ApcPowerReceiverComponent>(powerReceiverEnt);

                // Create an unpowered receiver outside range
                var unpoweredReceiverEnt = entityManager.SpawnEntity("ApcPowerReceiverDummy", grid.Owner.ToCoordinates(0, range));
                unpoweredReceiver = entityManager.GetComponent<ApcPowerReceiverComponent>(unpoweredReceiverEnt);

                var battery = entityManager.GetComponent<BatteryComponent>(apcEnt);
                apcNetBattery = entityManager.GetComponent<PowerNetworkBatteryComponent>(apcEnt);

                extensionCableSystem.SetProviderTransferRange(apcExtensionEnt, range);
                extensionCableSystem.SetReceiverReceptionRange(powerReceiverEnt, range);

                batterySys.SetMaxCharge((apcEnt, battery), 10000);  //arbitrary nonzero amount of charge
                batterySys.SetCharge((apcEnt, battery), battery.MaxCharge); //fill battery

                receiver.Load = 1; //arbitrary small amount of power
            });

            server.RunTicks(1); //let run a tick for ApcNet to process power

            await server.WaitAssertion(() =>
            {
                Assert.Multiple(() =>
                {
                    Assert.That(receiver.Powered, "Receiver in range should be powered");
                    Assert.That(!unpoweredReceiver.Powered, "Out of range receiver should not be powered");
                    Assert.That(apcNetBattery.CurrentSupply, Is.EqualTo(1).Within(0.1));
                });
            });
        }

        [Test]
        public async Task ApcReceiverTracksPartialSupply()
        {
            var pair = Pair;
            var server = pair.Server;
            var mapManager = server.ResolveDependency<IMapManager>();
            var entityManager = server.ResolveDependency<IEntityManager>();
            var batterySys = entityManager.System<BatterySystem>();
            var extensionCableSystem = entityManager.System<ExtensionCableSystem>();
            var mapSys = entityManager.System<SharedMapSystem>();
            PowerNetworkBatteryComponent apcNetBattery = default!;
            ApcPowerReceiverComponent receiver = default!;

            await server.WaitAssertion(() =>
            {
                mapSys.CreateMap(out var mapId);
                var grid = mapManager.CreateGridEntity(mapId);

                const int range = 5;
                for (var i = 0; i < range; i++)
                {
                    mapSys.SetTile(grid, new Vector2i(0, i), new Tile(1));
                }

                var apcEnt = entityManager.SpawnEntity("ApcDummy", grid.Owner.ToCoordinates(0, 0));
                var apcExtensionEnt = entityManager.SpawnEntity("CableApcExtension", grid.Owner.ToCoordinates(0, 0));
                var powerReceiverEnt = entityManager.SpawnEntity("ApcPowerReceiverDummy", grid.Owner.ToCoordinates(0, range - 1));

                receiver = entityManager.GetComponent<ApcPowerReceiverComponent>(powerReceiverEnt);
                var battery = entityManager.GetComponent<BatteryComponent>(apcEnt);
                apcNetBattery = entityManager.GetComponent<PowerNetworkBatteryComponent>(apcEnt);

                extensionCableSystem.SetProviderTransferRange(apcExtensionEnt, range);
                extensionCableSystem.SetReceiverReceptionRange(powerReceiverEnt, range);

                batterySys.SetMaxCharge((apcEnt, battery), 10000);
                batterySys.SetCharge((apcEnt, battery), battery.MaxCharge);

                receiver.Load = 100;
                apcNetBattery.MaxSupply = 50;
                apcNetBattery.SupplyRampTolerance = 50;
            });

            server.RunTicks(2);

            await server.WaitAssertion(() =>
            {
                Assert.Multiple(() =>
                {
                    Assert.That(receiver.Powered, Is.False);
                    Assert.That(receiver.PowerReceived, Is.EqualTo(50).Within(0.1));
                    Assert.That(receiver.SupplyRatio, Is.EqualTo(0.5f).Within(0.01f));
                    Assert.That(apcNetBattery.CurrentSupply, Is.EqualTo(50).Within(0.1));
                });
            });
        }

        [Test]
        public async Task PoweredLightDimsOnPartialApcSupply()
        {
            var pair = Pair;
            var server = pair.Server;
            var mapManager = server.ResolveDependency<IMapManager>();
            var entityManager = server.ResolveDependency<IEntityManager>();
            var batterySys = entityManager.System<BatterySystem>();
            var extensionCableSystem = entityManager.System<ExtensionCableSystem>();
            var mapSys = entityManager.System<SharedMapSystem>();
            ApcPowerReceiverComponent receiver = default!;
            PointLightComponent pointLight = default!;

            await server.WaitAssertion(() =>
            {
                mapSys.CreateMap(out var mapId);
                var grid = mapManager.CreateGridEntity(mapId);

                const int range = 5;
                for (var i = 0; i < range; i++)
                {
                    mapSys.SetTile(grid, new Vector2i(0, i), new Tile(1));
                }

                var apcEnt = entityManager.SpawnEntity("ApcDummy", grid.Owner.ToCoordinates(0, 0));
                var apcExtensionEnt = entityManager.SpawnEntity("CableApcExtension", grid.Owner.ToCoordinates(0, 0));
                var lightEnt = entityManager.SpawnEntity("Poweredlight", grid.Owner.ToCoordinates(0, range - 1));

                receiver = entityManager.GetComponent<ApcPowerReceiverComponent>(lightEnt);
                pointLight = entityManager.GetComponent<PointLightComponent>(lightEnt);
                var battery = entityManager.GetComponent<BatteryComponent>(apcEnt);
                var apcNetBattery = entityManager.GetComponent<PowerNetworkBatteryComponent>(apcEnt);

                extensionCableSystem.SetProviderTransferRange(apcExtensionEnt, range);
                extensionCableSystem.SetReceiverReceptionRange(lightEnt, range);

                batterySys.SetMaxCharge((apcEnt, battery), 10000);
                batterySys.SetCharge((apcEnt, battery), battery.MaxCharge);

                apcNetBattery.MaxSupply = 12.5f;
                apcNetBattery.SupplyRampTolerance = 12.5f;
            });

            server.RunTicks(3);

            await server.WaitAssertion(() =>
            {
                Assert.Multiple(() =>
                {
                    Assert.That(receiver.Load, Is.EqualTo(25).Within(0.1));
                    Assert.That(receiver.Powered, Is.False);
                    Assert.That(receiver.PowerReceived, Is.EqualTo(12.5f).Within(0.1));
                    Assert.That(pointLight.Enabled, Is.True);
                    Assert.That(pointLight.Energy, Is.EqualTo(0.4f).Within(0.01f));
                });
            });
        }

        [Test]
        public async Task ApcShedsLowerPriorityTiersFirst()
        {
            var pair = Pair;
            var server = pair.Server;
            var mapManager = server.ResolveDependency<IMapManager>();
            var entityManager = server.ResolveDependency<IEntityManager>();
            var batterySys = entityManager.System<BatterySystem>();
            var extensionCableSystem = entityManager.System<ExtensionCableSystem>();
            var mapSys = entityManager.System<SharedMapSystem>();
            ApcPowerReceiverComponent lifeSafety = default!;
            ApcPowerReceiverComponent lighting = default!;
            ApcPowerReceiverComponent comfort = default!;

            await server.WaitAssertion(() =>
            {
                mapSys.CreateMap(out var mapId);
                var grid = mapManager.CreateGridEntity(mapId);

                const int range = 5;
                for (var i = 0; i < range; i++)
                    mapSys.SetTile(grid, new Vector2i(0, i), new Tile(1));

                var apcEnt = entityManager.SpawnEntity("ApcDummy", grid.Owner.ToCoordinates(0, 0));
                var apcExtensionEnt = entityManager.SpawnEntity("CableApcExtension", grid.Owner.ToCoordinates(0, 0));
                var lifeEnt = entityManager.SpawnEntity("ApcPowerReceiverDummy", grid.Owner.ToCoordinates(0, range - 1));
                var lightingEnt = entityManager.SpawnEntity("ApcPowerReceiverDummy", grid.Owner.ToCoordinates(0, range - 2));
                var comfortEnt = entityManager.SpawnEntity("ApcPowerReceiverDummy", grid.Owner.ToCoordinates(0, range - 3));

                lifeSafety = entityManager.GetComponent<ApcPowerReceiverComponent>(lifeEnt);
                lighting = entityManager.GetComponent<ApcPowerReceiverComponent>(lightingEnt);
                comfort = entityManager.GetComponent<ApcPowerReceiverComponent>(comfortEnt);

                lifeSafety.Load = 100;
                lifeSafety.LoadPriority = ApcPowerPriority.LifeSafety;
                lighting.Load = 100;
                lighting.LoadPriority = ApcPowerPriority.Lighting;
                comfort.Load = 100;
                comfort.LoadPriority = ApcPowerPriority.Comfort;

                var battery = entityManager.GetComponent<BatteryComponent>(apcEnt);
                var apcNetBattery = entityManager.GetComponent<PowerNetworkBatteryComponent>(apcEnt);

                extensionCableSystem.SetProviderTransferRange(apcExtensionEnt, range);
                extensionCableSystem.SetReceiverReceptionRange(lifeEnt, range);
                extensionCableSystem.SetReceiverReceptionRange(lightingEnt, range);
                extensionCableSystem.SetReceiverReceptionRange(comfortEnt, range);

                batterySys.SetMaxCharge((apcEnt, battery), 10000);
                batterySys.SetCharge((apcEnt, battery), battery.MaxCharge);

                apcNetBattery.MaxSupply = 150f;
                apcNetBattery.SupplyRampTolerance = 150f;
            });

            server.RunTicks(3);

            await server.WaitAssertion(() =>
            {
                Assert.Multiple(() =>
                {
                    Assert.That(lifeSafety.SupplyRatio, Is.EqualTo(1f).Within(0.01f));
                    Assert.That(lighting.SupplyRatio, Is.EqualTo(0.5f).Within(0.01f));
                    Assert.That(comfort.SupplyRatio, Is.EqualTo(0f).Within(0.01f));
                    Assert.That(lifeSafety.ShedState, Is.EqualTo(ApcPowerTierState.Full));
                    Assert.That(lighting.ShedState, Is.EqualTo(ApcPowerTierState.Brownout));
                    Assert.That(comfort.ShedState, Is.EqualTo(ApcPowerTierState.Shed));
                });
            });
        }

        [Test]
        public async Task ApcReceiverHysteresisDoesNotFlickerAroundBrownoutThreshold()
        {
            var pair = Pair;
            var server = pair.Server;
            var mapManager = server.ResolveDependency<IMapManager>();
            var entityManager = server.ResolveDependency<IEntityManager>();
            var batterySys = entityManager.System<BatterySystem>();
            var extensionCableSystem = entityManager.System<ExtensionCableSystem>();
            var mapSys = entityManager.System<SharedMapSystem>();
            PowerNetworkBatteryComponent apcNetBattery = default!;
            ApcPowerReceiverComponent receiver = default!;

            await server.WaitAssertion(() =>
            {
                mapSys.CreateMap(out var mapId);
                var grid = mapManager.CreateGridEntity(mapId);

                const int range = 5;
                for (var i = 0; i < range; i++)
                    mapSys.SetTile(grid, new Vector2i(0, i), new Tile(1));

                var apcEnt = entityManager.SpawnEntity("ApcDummy", grid.Owner.ToCoordinates(0, 0));
                var apcExtensionEnt = entityManager.SpawnEntity("CableApcExtension", grid.Owner.ToCoordinates(0, 0));
                var powerReceiverEnt = entityManager.SpawnEntity("ApcPowerReceiverDummy", grid.Owner.ToCoordinates(0, range - 1));

                receiver = entityManager.GetComponent<ApcPowerReceiverComponent>(powerReceiverEnt);
                receiver.Load = 100;
                receiver.PowerOffThreshold = 0.5f;
                receiver.PowerOnThreshold = 0.75f;

                var battery = entityManager.GetComponent<BatteryComponent>(apcEnt);
                apcNetBattery = entityManager.GetComponent<PowerNetworkBatteryComponent>(apcEnt);

                extensionCableSystem.SetProviderTransferRange(apcExtensionEnt, range);
                extensionCableSystem.SetReceiverReceptionRange(powerReceiverEnt, range);

                batterySys.SetMaxCharge((apcEnt, battery), 10000);
                batterySys.SetCharge((apcEnt, battery), battery.MaxCharge);

                apcNetBattery.SupplyRampTolerance = 100f;
                apcNetBattery.MaxSupply = 60f;
            });

            server.RunTicks(3);
            await server.WaitAssertion(() => Assert.That(receiver.Powered, Is.False));

            await server.WaitAssertion(() => apcNetBattery.MaxSupply = 80f);
            server.RunTicks(3);
            await server.WaitAssertion(() => Assert.That(receiver.Powered, Is.True));

            await server.WaitAssertion(() => apcNetBattery.MaxSupply = 60f);
            server.RunTicks(3);
            await server.WaitAssertion(() => Assert.That(receiver.Powered, Is.True));

            await server.WaitAssertion(() => apcNetBattery.MaxSupply = 40f);
            server.RunTicks(3);
            await server.WaitAssertion(() => Assert.That(receiver.Powered, Is.False));
        }

        // Load sheds when demand exceeds the APC's MaxSupply even while it charges from a healthy grid.
        [Test]
        public async Task ApcShedsWhenLoadExceedsMaxSupplyWhileCharging()
        {
            var pair = Pair;
            var server = pair.Server;
            var mapManager = server.ResolveDependency<IMapManager>();
            var entityManager = server.ResolveDependency<IEntityManager>();
            var batterySys = entityManager.System<BatterySystem>();
            var extensionCableSystem = entityManager.System<ExtensionCableSystem>();
            var mapSys = entityManager.System<SharedMapSystem>();
            PowerNetworkBatteryComponent apcNetBattery = default!;
            ApcPowerReceiverComponent lifeSafety = default!;
            ApcPowerReceiverComponent comfort = default!;

            await server.WaitAssertion(() =>
            {
                mapSys.CreateMap(out var mapId);
                var grid = mapManager.CreateGridEntity(mapId);

                const int range = 6;
                for (var i = 0; i < range; i++)
                    mapSys.SetTile(grid, new Vector2i(0, i), new Tile(1));

                // Upstream chain: generator -> substation -> APC, so the APC is actively charging.
                entityManager.SpawnEntity("CableHV", grid.Owner.ToCoordinates(0, 0));
                entityManager.SpawnEntity("CableHV", grid.Owner.ToCoordinates(0, 1));
                entityManager.SpawnEntity("CableMV", grid.Owner.ToCoordinates(0, 1));
                entityManager.SpawnEntity("CableMV", grid.Owner.ToCoordinates(0, 2));

                var generatorEnt = entityManager.SpawnEntity("GeneratorDummy", grid.Owner.ToCoordinates(0, 0));
                var substationEnt = entityManager.SpawnEntity("SubstationDummy", grid.Owner.ToCoordinates(0, 1));
                var apcEnt = entityManager.SpawnEntity("ApcDummy", grid.Owner.ToCoordinates(0, 2));
                var apcExtensionEnt = entityManager.SpawnEntity("CableApcExtension", grid.Owner.ToCoordinates(0, 2));
                var lifeEnt = entityManager.SpawnEntity("ApcPowerReceiverDummy", grid.Owner.ToCoordinates(0, 3));
                var comfortEnt = entityManager.SpawnEntity("ApcPowerReceiverDummy", grid.Owner.ToCoordinates(0, 4));

                // Generous upstream supply so the APC, not the grid, is the bottleneck.
                var generator = entityManager.GetComponent<PowerSupplierComponent>(generatorEnt);
                generator.MaxSupply = 1_000_000;
                generator.SupplyRampTolerance = 1_000_000;

                var substationBattery = entityManager.GetComponent<BatteryComponent>(substationEnt);
                var substationNetBattery = entityManager.GetComponent<PowerNetworkBatteryComponent>(substationEnt);
                batterySys.SetMaxCharge((substationEnt, substationBattery), 1_000_000);
                batterySys.SetCharge((substationEnt, substationBattery), 1_000_000);
                substationNetBattery.MaxSupply = 1_000_000;
                substationNetBattery.MaxChargeRate = 1_000_000;
                substationNetBattery.SupplyRampTolerance = 1_000_000;

                apcNetBattery = entityManager.GetComponent<PowerNetworkBatteryComponent>(apcEnt);
                var apcBattery = entityManager.GetComponent<BatteryComponent>(apcEnt);
                // Big reserve started part-full so the APC keeps drawing charge current the whole test.
                batterySys.SetMaxCharge((apcEnt, apcBattery), 1_000_000);
                batterySys.SetCharge((apcEnt, apcBattery), 500_000);
                apcNetBattery.MaxSupply = 1000;
                apcNetBattery.SupplyRampTolerance = 1000;

                lifeSafety = entityManager.GetComponent<ApcPowerReceiverComponent>(lifeEnt);
                comfort = entityManager.GetComponent<ApcPowerReceiverComponent>(comfortEnt);
                lifeSafety.Load = 600;
                lifeSafety.LoadPriority = ApcPowerPriority.LifeSafety;
                comfort.Load = 600;
                comfort.LoadPriority = ApcPowerPriority.Comfort;

                extensionCableSystem.SetProviderTransferRange(apcExtensionEnt, range);
                extensionCableSystem.SetReceiverReceptionRange(lifeEnt, range);
                extensionCableSystem.SetReceiverReceptionRange(comfortEnt, range);
            });

            server.RunTicks(10);

            await server.WaitAssertion(() =>
            {
                Assert.Multiple(() =>
                {
                    // The APC is actively charging from the grid...
                    Assert.That(apcNetBattery.CurrentReceiving, Is.GreaterThan(200));
                    // ...yet total demand (1200) exceeds the APC's 1000 W rating, so the lowest tier still sheds.
                    Assert.That(lifeSafety.SupplyRatio, Is.GreaterThan(0.99f));
                    Assert.That(comfort.ShedState, Is.EqualTo(ApcPowerTierState.Brownout));
                    Assert.That(comfort.SupplyRatio, Is.LessThan(0.9f));
                });
            });
        }

        // A brownout slows a lathe (bounded), and below the floor cuts it off instead of near-halting it.
        [Test]
        public async Task BrownoutSlowsLatheButCutsOffBelowFloor()
        {
            var pair = Pair;
            var server = pair.Server;
            var mapManager = server.ResolveDependency<IMapManager>();
            var entityManager = server.ResolveDependency<IEntityManager>();
            var batterySys = entityManager.System<BatterySystem>();
            var extensionCableSystem = entityManager.System<ExtensionCableSystem>();
            var mapSys = entityManager.System<SharedMapSystem>();
            PowerNetworkBatteryComponent apcNetBattery = default!;
            Content.Shared.Lathe.LatheComponent lathe = default!;

            await server.WaitAssertion(() =>
            {
                mapSys.CreateMap(out var mapId);
                var grid = mapManager.CreateGridEntity(mapId);

                const int range = 5;
                for (var i = 0; i < range; i++)
                    mapSys.SetTile(grid, new Vector2i(0, i), new Tile(1));

                var apcEnt = entityManager.SpawnEntity("ApcDummy", grid.Owner.ToCoordinates(0, 0));
                var apcExtensionEnt = entityManager.SpawnEntity("CableApcExtension", grid.Owner.ToCoordinates(0, 0));
                var latheEnt = entityManager.SpawnEntity("Autolathe", grid.Owner.ToCoordinates(0, range - 1));

                lathe = entityManager.GetComponent<Content.Shared.Lathe.LatheComponent>(latheEnt);
                var receiver = entityManager.GetComponent<ApcPowerReceiverComponent>(latheEnt);
                receiver.Load = 150;
                receiver.LoadPriority = ApcPowerPriority.Equipment;

                var battery = entityManager.GetComponent<BatteryComponent>(apcEnt);
                apcNetBattery = entityManager.GetComponent<PowerNetworkBatteryComponent>(apcEnt);
                batterySys.SetMaxCharge((apcEnt, battery), 10000);
                batterySys.SetCharge((apcEnt, battery), battery.MaxCharge);

                extensionCableSystem.SetProviderTransferRange(apcExtensionEnt, range);
                extensionCableSystem.SetReceiverReceptionRange(latheEnt, range);

                // 90 W of 150 W demand -> shedRatio 0.6 -> slowed but powered.
                apcNetBattery.MaxSupply = 90f;
                apcNetBattery.SupplyRampTolerance = 90f;
            });

            server.RunTicks(3);

            await server.WaitAssertion(() =>
            {
                Assert.Multiple(() =>
                {
                    // 1 / 0.6 == ~1.67x slower, and well under the 1/0.25 == 4x floor cap.
                    Assert.That(lathe.TimeMultiplier, Is.EqualTo(1f / 0.6f).Within(0.1f));
                    Assert.That(lathe.TimeMultiplier, Is.LessThanOrEqualTo(4f));
                });
            });

            // Drop below the floor: 30 W of 150 W -> shedRatio 0.2 < 0.25 -> cut off, multiplier restored.
            await server.WaitAssertion(() => apcNetBattery.MaxSupply = 30f);
            server.RunTicks(3);

            await server.WaitAssertion(() =>
                Assert.That(lathe.TimeMultiplier, Is.EqualTo(1f).Within(0.01f)));
        }
    }
}
