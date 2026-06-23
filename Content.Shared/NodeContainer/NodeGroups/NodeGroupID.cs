namespace Content.Shared.NodeContainer.NodeGroups;

public enum NodeGroupID : byte
{
    Default,
    HVPower,
    MVPower,
    Apc,
    AMEngine,
    Pipe,
    WireNet,

    /// <summary>
    /// Group used by the TEG.
    /// </summary>
    /// <seealso cref="Content.Server.Power.Generation.Teg.TegSystem"/>
    /// <seealso cref="Content.Server.Power.Generation.Teg.TegNodeGroup"/>
    Teg,
    ExCable,

    /// <summary>
    /// Pipes that carry liquid reagents instead of gas.
    /// </summary>
    /// <seealso cref="Content.Server.NodeContainer.Nodes.ReagentPipeNode"/>
    /// <seealso cref="Content.Server.NodeContainer.NodeGroups.ReagentPipeNet"/>
    ReagentPipe,

    /// <summary>
    /// Reagent pipes dedicated to engine fuel, kept on their own net so fuel never mixes with coolant.
    /// </summary>
    FuelPipe,

    /// <summary>
    /// Reagent pipes dedicated to engine coolant, kept on their own net.
    /// </summary>
    CoolantPipe,

    /// <summary>
    /// Pressurized RCS gas lines, kept on their own net. A cut segment whips off under pressure.
    /// </summary>
    RcsPipe,
}
