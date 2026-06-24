using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Reactions;
using JetBrains.Annotations;

namespace Content.Server.Atmos.Reactions
{
    /// <summary>
    ///     Burns methane with oxygen into carbon dioxide and water vapor: CH4 + 2 O2 -> CO2 + 2 H2O. The burn
    ///     rate is throttled so a methane fire sustains across ticks rather than flashing off in one.
    /// </summary>
    [UsedImplicitly]
    [DataDefinition]
    public sealed partial class MethaneFireReaction : IGasReactionEffect
    {
        public ReactionResult React(GasMixture mixture, IGasMixtureHolder? holder, AtmosphereSystem atmosphereSystem, float heatScale)
        {
            var oldHeatCapacity = atmosphereSystem.GetHeatCapacity(mixture, true);
            var temperature = mixture.Temperature;
            var location = holder as TileAtmosphere;
            mixture.ReactionResults[(byte)GasReaction.Fire] = 0f;

            var initialMethane = mixture.GetMoles(Gas.Methane);
            var initialOxygen = mixture.GetMoles(Gas.Oxygen);

            // Limited by whichever of fuel or oxidizer runs out first, then throttled to a per-tick rate.
            var burnedFuel = Math.Min(initialMethane, initialOxygen / Atmospherics.MethaneBurnOxyRatio) / Atmospherics.MethaneBurnRateDelta;
            if (burnedFuel <= 0f)
                return ReactionResult.NoReaction;

            mixture.AdjustMoles(Gas.Methane, -burnedFuel);
            mixture.AdjustMoles(Gas.Oxygen, -burnedFuel * Atmospherics.MethaneBurnOxyRatio);
            mixture.AdjustMoles(Gas.CarbonDioxide, burnedFuel);
            mixture.AdjustMoles(Gas.WaterVapor, burnedFuel * 2f);

            mixture.ReactionResults[(byte)GasReaction.Fire] += burnedFuel;

            var energyReleased = Atmospherics.FireMethaneEnergyReleased * burnedFuel / heatScale;
            if (energyReleased > 0)
            {
                var newHeatCapacity = atmosphereSystem.GetHeatCapacity(mixture, true);
                if (newHeatCapacity > Atmospherics.MinimumHeatCapacity)
                    mixture.Temperature = (temperature * oldHeatCapacity + energyReleased) / newHeatCapacity;
            }

            if (location != null)
            {
                temperature = mixture.Temperature;
                if (temperature > Atmospherics.FireMinimumTemperatureToExist)
                {
                    atmosphereSystem.HotspotExpose(location, temperature, mixture.Volume);
                }
            }

            return ReactionResult.Reacting;
        }
    }
}
