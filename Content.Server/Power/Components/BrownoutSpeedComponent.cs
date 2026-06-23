namespace Content.Server.Power.Components;

/// <summary>
///     Caches the pre-brownout TimeMultiplier for production machines.
/// </summary>
[RegisterComponent]
public sealed partial class BrownoutSpeedComponent : Component
{
    public float OriginalTimeMultiplier = 1f;
    /// <summary>PowerOffThreshold before brownout started.</summary>
    public float OriginalThreshold = 1f;
}
