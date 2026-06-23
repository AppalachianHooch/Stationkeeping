namespace Content.Shared.Power;

/// <summary>
/// Raised whenever an ApcPowerReceiver changes powered state or supplied power.
/// </summary>
[ByRefEvent]
public readonly record struct PowerChangedEvent(bool Powered, float ReceivingPower);
