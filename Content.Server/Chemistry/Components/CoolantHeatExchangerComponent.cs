using Content.Server.Chemistry.EntitySystems;

namespace Content.Server.Chemistry.Components;

/// <summary>
/// Sheds heat from a connected <see cref="NodeGroupID.CoolantPipe"/> net into the surrounding atmosphere.
/// Install one or more in the coolant loop to keep the AME coolant from cooking.
/// </summary>
[Access(typeof(CoolantHeatExchangerSystem))]
[RegisterComponent]
public sealed partial class CoolantHeatExchangerComponent : Component
{
    /// <summary>Fraction of excess coolant temperature (above ambient) shed per second, in K/s per K.</summary>
    [DataField]
    public float ExchangeRate = 0.02f;

    [DataField]
    public string NodeName = "coolant";
}
