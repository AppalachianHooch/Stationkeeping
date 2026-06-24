using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Reactions;
using JetBrains.Annotations;

namespace Content.Server.Atmos.Reactions
{
    /// <summary>
    ///     Burns hydrogen with oxygen into water vapor. Hydrogen burns exactly as tritium does (both are just
    ///     hydrogen plus oxygen making water), so it reuses the same burn constants and energy.
    /// </summary>
    [UsedImplicitly]
    [DataDefinition]
    public sealed partial class HydrogenFireReaction : IGasReactionEffect
    {
        public ReactionResult React(GasMixture mixture, IGasMixtureHolder? holder, AtmosphereSystem atmosphereSystem, float heatScale)
        {
            var energyReleased = 0f;
            var oldHeatCapacity = atmosphereSystem.GetHeatCapacity(mixture, true);
            var temperature = mixture.Temperature;
            var location = holder as TileAtmosphere;
            mixture.ReactionResults[(byte)GasReaction.Fire] = 0f;
            var burnedFuel = 0f;
            var initialHydrogen = mixture.GetMoles(Gas.Hydrogen);

            if (mixture.GetMoles(Gas.Oxygen) < initialHydrogen ||
                Atmospherics.MinimumTritiumOxyburnEnergy > (temperature * oldHeatCapacity * heatScale))
            {
                burnedFuel = mixture.GetMoles(Gas.Oxygen) / Atmospherics.TritiumBurnOxyFactor;
                if (burnedFuel > initialHydrogen)
                    burnedFuel = initialHydrogen;

                mixture.AdjustMoles(Gas.Hydrogen, -burnedFuel);
                mixture.AdjustMoles(Gas.Oxygen, -burnedFuel / Atmospherics.TritiumBurnFuelRatio);
            }
            else
            {
                burnedFuel = Math.Min(initialHydrogen, mixture.GetMoles(Gas.Oxygen) / Atmospherics.TritiumBurnFuelRatio) / Atmospherics.TritiumBurnTritFactor;
                mixture.AdjustMoles(Gas.Hydrogen, -burnedFuel);
                mixture.AdjustMoles(Gas.Oxygen, -burnedFuel / Atmospherics.TritiumBurnFuelRatio);
                energyReleased += Atmospherics.FireHydrogenEnergyReleased * burnedFuel * (Atmospherics.TritiumBurnTritFactor - 1);
            }

            if (burnedFuel > 0)
            {
                energyReleased += Atmospherics.FireHydrogenEnergyReleased * burnedFuel;

                // Conservation of mass is important.
                mixture.AdjustMoles(Gas.WaterVapor, burnedFuel);

                mixture.ReactionResults[(byte)GasReaction.Fire] += burnedFuel;
            }

            energyReleased /= heatScale;
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

            return mixture.ReactionResults[(byte)GasReaction.Fire] != 0 ? ReactionResult.Reacting : ReactionResult.NoReaction;
        }
    }
}
