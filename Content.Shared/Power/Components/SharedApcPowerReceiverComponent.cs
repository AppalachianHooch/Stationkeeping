using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared.Power.Components;

[NetworkedComponent]
public abstract partial class SharedApcPowerReceiverComponent : Component
{
    [ViewVariables]
    public bool Powered;

    /// <summary>
    ///     When false, causes this to appear powered even if not receiving power from an Apc.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public virtual bool NeedsPower { get; set;}

    /// <summary>
    ///     When true, causes this to never appear powered.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public virtual bool PowerDisabled { get; set; }

    // Doesn't actually do anything on the client just here for shared code.
    public abstract float Load { get; set; }

    /// <summary>
    ///     Amount of power currently supplied to this receiver.
    /// </summary>
    [ViewVariables]
    public virtual float ReceivingPower { get; set; }

    /// <summary>
    ///     APC load tier used for priority shedding.
    /// </summary>
    [ViewVariables]
    public virtual ApcPowerPriority LoadPriority { get; set; } = ApcPowerPriority.Equipment;

    /// <summary>
    ///     APC-controlled cap applied to this receiver's requested load before solving.
    /// </summary>
    [ViewVariables]
    public virtual float ShedRatio { get; set; } = 1f;

    /// <summary>
    ///     Fraction of desired power currently supplied, clamped to 0..1.
    /// </summary>
    [ViewVariables]
    public float SupplyRatio
    {
        get
        {
            if (!NeedsPower)
                return PowerDisabled ? 0f : 1f;

            if (PowerDisabled)
                return 0f;

            if (Load <= 0f)
                return Powered ? 1f : 0f;

            return Math.Clamp(ReceivingPower / Load, 0f, 1f);
        }
    }
}

[Serializable, NetSerializable]
public enum ApcPowerPriority : byte
{
    LifeSafety = 0,
    Environment = 1,
    Equipment = 2,
    Lighting = 3,
    Comfort = 4,
}

[Serializable, NetSerializable]
public enum ApcPowerPriorityOverride : byte
{
    Auto = 0,
    ForceOn = 1,
    ForceOff = 2,
}

[Serializable, NetSerializable]
public enum ApcPowerTierState : byte
{
    Full = 0,
    Brownout = 1,
    Shed = 2,
}
