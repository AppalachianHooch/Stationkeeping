namespace Content.Server.Power.Components;

/// <summary>
///     Caches the pre-brownout TimeMultiplier for production machines.
/// </summary>
[RegisterComponent]
public sealed partial class BrownoutSpeedComponent : Component
{
    public float OriginalTimeMultiplier = 1f;
    /// <summary>Power thresholds before brownout started, restored when it ends.</summary>
    public float OriginalOffThreshold = 1f;
    public float OriginalOnThreshold = 1f;
}
