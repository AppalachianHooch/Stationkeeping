using Content.Server.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;

namespace Content.Server.Chemistry.Components;

/// <summary>
///     Moves reagents directionally from an inlet pipe net to an outlet pipe net at a fixed rate.
///     The reagent equivalent of a gas volume pump.
/// </summary>
/// <seealso cref="ReagentPipePumpSystem"/>
[RegisterComponent]
[Access(typeof(ReagentPipePumpSystem))]
public sealed partial class ReagentPipePumpComponent : Component
{
    [DataField]
    public string InletName = "inlet";

    [DataField]
    public string OutletName = "outlet";

    /// <summary>
    ///     Maximum volume moved per cycle.
    /// </summary>
    [DataField]
    public FixedPoint2 TransferRate = FixedPoint2.New(15);

    /// <summary>
    ///     How often a transfer happens.
    /// </summary>
    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(1);

    [DataField]
    public bool Enabled = true;

    [DataField]
    public TimeSpan NextTransfer;
}
