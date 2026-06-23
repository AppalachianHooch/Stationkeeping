using Robust.Shared.GameStates;

namespace Content.Shared.CeilingLayer;

/// <summary>
///     Marks infrastructure that runs along the overhead ceiling layer instead of the subfloor.
///     Keeps reagent piping and similar conduits off the crowded subfloor shared by atmos, disposals,
///     and cabling, so the lines stay readable on a map and do not compete for that layer.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CeilingLayerComponent : Component
{
}
