using Content.Shared.Power.Components;

namespace Content.Client.Power.Components;

[RegisterComponent]
public sealed partial class ApcPowerReceiverComponent : SharedApcPowerReceiverComponent
{
    public override float Load { get; set; }

    public override float ReceivingPower { get; set; }

    public override ApcPowerPriority LoadPriority { get; set; } = ApcPowerPriority.Equipment;

    public override float ShedRatio { get; set; } = 1f;
}
