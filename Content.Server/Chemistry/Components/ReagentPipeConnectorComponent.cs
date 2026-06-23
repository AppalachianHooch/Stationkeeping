using Content.Server.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Components;
using Content.Shared.FixedPoint;

namespace Content.Server.Chemistry.Components;

/// <summary>
///     Bridges a named <see cref="Solution"/> on this entity to a connected
///     <see cref="Content.Server.NodeContainer.NodeGroups.ReagentPipeNet"/>, moving reagents
///     between the two on a fixed cycle. The reagent equivalent of an atmos vent/pump.
/// </summary>
/// <seealso cref="ReagentPipeConnectorSystem"/>
[RegisterComponent]
[Access(typeof(ReagentPipeConnectorSystem))]
public sealed partial class ReagentPipeConnectorComponent : Component
{
    /// <summary>
    ///     Name of the <see cref="Content.Server.NodeContainer.Nodes.ReagentPipeNode"/> to bridge to.
    /// </summary>
    [DataField]
    public string NodeName = "pipe";

    /// <summary>
    ///     Name of the local solution to move reagents to/from.
    /// </summary>
    [DataField]
    public string SolutionName = "tank";

    /// <summary>
    ///     Which way reagents flow each cycle.
    /// </summary>
    [DataField]
    public ReagentPipeConnectorMode Mode = ReagentPipeConnectorMode.Output;

    /// <summary>
    ///     Maximum volume moved per cycle (at baseline pressure).
    /// </summary>
    [DataField]
    public FixedPoint2 TransferRate = FixedPoint2.New(15);

    /// <summary>
    ///     Regulator pressure [0,1] this connector drives the line at. An input connector pressurizes its net
    ///     to this and feeds faster the higher it is set, but a pressurized line is far more dangerous to cut.
    ///     This is the risk/reward dial: crank it for throughput, or back it off before tearing the line down.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float Pressure = 0.5f;

    /// <summary>
    ///     How often a transfer happens.
    /// </summary>
    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     Whether this connector is currently moving reagents.
    /// </summary>
    [DataField]
    public bool Enabled = true;

    [DataField]
    public TimeSpan NextTransfer;

    /// <summary>
    ///     Cached handle to the local solution, resolved lazily.
    /// </summary>
    public Entity<SolutionComponent>? Solution;
}

public enum ReagentPipeConnectorMode : byte
{
    /// <summary>
    ///     Drain the local solution into the pipe net.
    /// </summary>
    Input,

    /// <summary>
    ///     Fill the local solution from the pipe net.
    /// </summary>
    Output,
}
