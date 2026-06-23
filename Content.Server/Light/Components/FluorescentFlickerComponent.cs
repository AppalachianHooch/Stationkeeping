namespace Content.Server.Light.Components;

/// <summary>
///     Flickers a fluorescent light during a brownout.
/// </summary>
[RegisterComponent]
public sealed partial class FluorescentFlickerComponent : Component
{
    public TimeSpan NextToggle;
    public bool LitPhase = true;
    public float ShedRatio = 1f;
}
