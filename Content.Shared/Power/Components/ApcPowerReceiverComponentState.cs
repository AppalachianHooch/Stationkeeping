using Robust.Shared.Serialization;

namespace Content.Shared.Power.Components;

[Serializable, NetSerializable]
public sealed class ApcPowerReceiverComponentState : ComponentState
{
    public bool Powered;
    public bool NeedsPower;
    public bool PowerDisabled;
    public float Load;
    public float ReceivingPower;
    public ApcPowerPriority LoadPriority;
    public float ShedRatio;
}
