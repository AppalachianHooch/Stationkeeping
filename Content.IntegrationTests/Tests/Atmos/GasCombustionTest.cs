#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.Atmos;

/// <summary>
/// Hydrogen and methane are real flammable gases: a hot fuel-and-oxygen mix burns, consuming the fuel, making
/// the expected products, and releasing heat.
/// </summary>
[TestFixture]
public sealed class GasCombustionTest : GameTest
{
    [Test]
    public async Task HydrogenBurnsToWater()
    {
        await Server.WaitAssertion(() =>
        {
            var atmos = SEntMan.System<AtmosphereSystem>();
            var mix = new GasMixture(Atmospherics.CellVolume) { Temperature = 1000f };
            mix.SetMoles(Gas.Hydrogen, 10f);
            mix.SetMoles(Gas.Oxygen, 10f);

            var hydrogenBefore = mix.GetMoles(Gas.Hydrogen);
            var waterBefore = mix.GetMoles(Gas.WaterVapor);
            var tempBefore = mix.Temperature;

            atmos.React(mix, null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(mix.GetMoles(Gas.Hydrogen), Is.LessThan(hydrogenBefore), "Hydrogen should burn off.");
                Assert.That(mix.GetMoles(Gas.WaterVapor), Is.GreaterThan(waterBefore), "Burning hydrogen makes water vapor.");
                Assert.That(mix.Temperature, Is.GreaterThan(tempBefore), "Combustion should release heat.");
            }
        });
    }

    [Test]
    public async Task MethaneBurnsToCarbonDioxide()
    {
        await Server.WaitAssertion(() =>
        {
            var atmos = SEntMan.System<AtmosphereSystem>();
            var mix = new GasMixture(Atmospherics.CellVolume) { Temperature = 1000f };
            mix.SetMoles(Gas.Methane, 10f);
            mix.SetMoles(Gas.Oxygen, 30f);

            var methaneBefore = mix.GetMoles(Gas.Methane);
            var co2Before = mix.GetMoles(Gas.CarbonDioxide);
            var tempBefore = mix.Temperature;

            atmos.React(mix, null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(mix.GetMoles(Gas.Methane), Is.LessThan(methaneBefore), "Methane should burn off.");
                Assert.That(mix.GetMoles(Gas.CarbonDioxide), Is.GreaterThan(co2Before), "Burning methane makes carbon dioxide.");
                Assert.That(mix.Temperature, Is.GreaterThan(tempBefore), "Combustion should release heat.");
            }
        });
    }
}
