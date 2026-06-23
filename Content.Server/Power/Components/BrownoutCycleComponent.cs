namespace Content.Server.Power.Components;

/// <summary>
///     Cycles equipment power on/off during a brownout.
/// </summary>
[RegisterComponent]
public sealed partial class BrownoutCycleComponent : Component
{
    public TimeSpan NextToggle;
    public bool OnPhase = true;
    public float ShedRatio = 1f;
    /// <summary>PowerOffThreshold before cycling started.</summary>
    public float OriginalThreshold = 1f;
}
