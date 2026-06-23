using Content.Server.Chemistry.EntitySystems;

namespace Content.Server.Chemistry.Components;

/// <summary>
///     A manually toggled valve that joins or separates the inlet and outlet reagent pipe nets.
///     Open merges the two nets into one shared solution; closed splits them.
/// </summary>
/// <seealso cref="ReagentPipeValveSystem"/>
[RegisterComponent]
[Access(typeof(ReagentPipeValveSystem))]
public sealed partial class ReagentPipeValveComponent : Component
{
    [DataField]
    public string InletName = "inlet";

    [DataField]
    public string OutletName = "outlet";

    [DataField]
    public bool Open;
}
