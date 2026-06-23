using Content.Server.Ghost;
using Content.Server.Light.Components;
using Content.Server.Power.Components;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Power;
using Robust.Shared.Random;

namespace Content.Server.Light.EntitySystems;

/// <summary>
///     System for the PoweredLightComponents
/// </summary>
public sealed partial class PoweredLightSystem : SharedPoweredLightSystem
{
    [Dependency] private IRobustRandom _random = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PoweredLightComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<PoweredLightComponent, GhostBooEvent>(OnGhostBoo);
    }

    private void OnGhostBoo(EntityUid uid, PoweredLightComponent light, GhostBooEvent args)
    {
        if (light.IgnoreGhostsBoo || HasComp<BlinkingPoweredLightComponent>(uid))
            return; // The light is immune or already blinking.

        // check cooldown first to prevent abuse
        var curTime = GameTiming.CurTime;
        if (light.LastGhostBlink != null && curTime <= light.LastGhostBlink + light.GhostBlinkingCooldown)
            return;

        light.LastGhostBlink = curTime;

        var blinkingComp = EnsureComp<BlinkingPoweredLightComponent>(uid);
        blinkingComp.StopBlinkingTime = curTime + light.GhostBlinkingTime;
        Dirty(uid, blinkingComp);

        args.Handled = true;
    }

    private void OnMapInit(EntityUid uid, PoweredLightComponent light, MapInitEvent args)
    {
        // TODO: Use ContainerFill dog
        if (light.HasLampOnSpawn != null)
        {
            var entity = Spawn(light.HasLampOnSpawn, Comp<TransformComponent>(uid).Coordinates);
            ContainerSystem.Insert(entity, light.LightBulbContainer);
        }
        // need this to update visualizers
        UpdateLight(uid, light);
    }

    // Runs the shared power handling (UpdateLight), then overrides the visual for fluorescent bulbs.
    protected override void OnPowerChanged(EntityUid uid, PoweredLightComponent light, ref PowerChangedEvent args)
    {
        base.OnPowerChanged(uid, light, ref args);

        if (!TryComp<ApcPowerReceiverComponent>(uid, out var receiver))
            return;

        var bulbUid = GetBulb(uid, light);
        if (!light.On
            || bulbUid == null
            || !TryComp<LightBulbComponent>(bulbUid.Value, out var bulb)
            || bulb.Type != LightBulbType.Tube
            || bulb.State != LightBulbState.Normal)
        {
            RemCompDeferred<FluorescentFlickerComponent>(uid);
            return;
        }

        var shedRatio = receiver.ShedRatio;
        if (shedRatio <= 0f || shedRatio >= 1f)
        {
            RemCompDeferred<FluorescentFlickerComponent>(uid);
            return;
        }

        var isNew = !HasComp<FluorescentFlickerComponent>(uid);
        var flicker = EnsureComp<FluorescentFlickerComponent>(uid);
        flicker.ShedRatio = shedRatio;

        if (isNew)
        {
            // Start in lit phase; UpdateLight already set a dimmed state, we override immediately.
            flicker.LitPhase = true;
            flicker.NextToggle = GameTiming.CurTime + FlickerDelay(shedRatio, litPhase: true);
            SetLight(uid, true, bulb.Color, light, bulb.LightRadius, bulb.LightEnergy, bulb.LightSoftness);
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = GameTiming.CurTime;
        var query = EntityQueryEnumerator<FluorescentFlickerComponent, PoweredLightComponent>();
        while (query.MoveNext(out var uid, out var flicker, out var light))
        {
            if (curTime < flicker.NextToggle)
                continue;

            flicker.LitPhase = !flicker.LitPhase;

            var bulbUid = GetBulb(uid, light);
            if (bulbUid == null || !TryComp<LightBulbComponent>(bulbUid.Value, out var bulb))
            {
                RemCompDeferred<FluorescentFlickerComponent>(uid);
                continue;
            }

            if (flicker.LitPhase)
                SetLight(uid, true, bulb.Color, light, bulb.LightRadius, bulb.LightEnergy, bulb.LightSoftness);
            else
                SetLight(uid, false, light: light);

            flicker.NextToggle = curTime + FlickerDelay(flicker.ShedRatio, flicker.LitPhase);
        }
    }

    // On-phase = ShedRatio fraction of a random cycle; off-phase = remainder. Min 0.15 s to avoid strobing.
    private TimeSpan FlickerDelay(float shedRatio, bool litPhase)
    {
        var cycle = _random.NextFloat(1.5f, 4.0f);
        var delay = litPhase ? cycle * shedRatio : cycle * (1f - shedRatio);
        return TimeSpan.FromSeconds(Math.Max(0.15, delay));
    }
}
