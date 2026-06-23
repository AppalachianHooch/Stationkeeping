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
    /// <summary>Power thresholds before cycling started, restored when it ends.</summary>
    public float OriginalOffThreshold = 1f;
    public float OriginalOnThreshold = 1f;
}
