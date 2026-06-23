using Content.Server.Lathe;
using Content.Server.Power.Components;
using Content.Shared.Lathe;
using Content.Shared.Power;
using Content.Shared.Power.Components;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server.Power.EntitySystems;

public sealed partial class BrownoutSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IRobustRandom _random = default!;

    // Below 50% shed, equipment is treated as a real power cut rather than cycled.
    private const float MinCyclingShedRatio = 0.5f;

    // Below this, production machines cut off instead of slowing toward a near-halt (caps slowdown at 4x).
    private const float MinProductionShedRatio = 0.25f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ApcPowerReceiverComponent, PowerChangedEvent>(OnPowerChanged);
        SubscribeLocalEvent<BrownoutCycleComponent, ComponentRemove>(OnCycleRemoved);
        SubscribeLocalEvent<BrownoutSpeedComponent, ComponentRemove>(OnSpeedRemoved);
    }

    private void OnPowerChanged(EntityUid uid, ApcPowerReceiverComponent receiver, ref PowerChangedEvent args)
    {
        if (receiver.LoadPriority != ApcPowerPriority.Equipment)
            return;

        if (receiver.PowerDisabled)
        {
            RemCompDeferred<BrownoutCycleComponent>(uid);
            RemCompDeferred<BrownoutSpeedComponent>(uid);
            return;
        }

        var shedRatio = receiver.ShedRatio;

        if (shedRatio <= 0f || shedRatio >= 1f)
        {
            RemCompDeferred<BrownoutCycleComponent>(uid);
            RemCompDeferred<BrownoutSpeedComponent>(uid);
            return;
        }

        // Production machines run slower instead of power-cycling.
        if (TryComp<LatheComponent>(uid, out var lathe))
        {
            // Below the floor, cut the machine off rather than slowing it to a near-halt.
            if (shedRatio < MinProductionShedRatio)
                RemCompDeferred<BrownoutSpeedComponent>(uid);
            else
                HandleProductionMachine(uid, lathe, shedRatio);
            return;
        }

        if (shedRatio < MinCyclingShedRatio)
        {
            RemCompDeferred<BrownoutCycleComponent>(uid);
            return;
        }

        HandleCyclingEquipment(uid, receiver, shedRatio);
    }

    private void HandleProductionMachine(EntityUid uid, LatheComponent lathe, float shedRatio)
    {
        RemCompDeferred<BrownoutCycleComponent>(uid);

        // EnsureComp out-param, not HasComp: a deferred-removed comp is culled+readded, so the real threshold is restored before recapture.
        var isNew = !EnsureComp<BrownoutSpeedComponent>(uid, out var speed);

        if (isNew && TryComp<ApcPowerReceiverComponent>(uid, out var receiver))
        {
            speed.OriginalTimeMultiplier = lathe.TimeMultiplier;
            speed.OriginalThreshold = receiver.PowerOffThreshold;
            // Force powered so lathes run (slower) rather than cutting out entirely.
            receiver.PowerOffThreshold = 0f;
        }

        lathe.TimeMultiplier = speed.OriginalTimeMultiplier / shedRatio;
    }

    private void OnSpeedRemoved(EntityUid uid, BrownoutSpeedComponent speed, ComponentRemove args)
    {
        if (!TryComp<LatheComponent>(uid, out var lathe))
            return;

        lathe.TimeMultiplier = speed.OriginalTimeMultiplier;
        if (TryComp<ApcPowerReceiverComponent>(uid, out var receiver))
            receiver.PowerOffThreshold = speed.OriginalThreshold;
    }

    private void HandleCyclingEquipment(EntityUid uid, ApcPowerReceiverComponent receiver, float shedRatio)
    {
        var isNew = !EnsureComp<BrownoutCycleComponent>(uid, out var cycle);
        cycle.ShedRatio = shedRatio;

        if (isNew)
        {
            cycle.OriginalThreshold = receiver.PowerOffThreshold;
            cycle.OnPhase = true;
            receiver.PowerOffThreshold = 0f;
            cycle.NextToggle = _gameTiming.CurTime + CycleDelay(shedRatio, onPhase: true);
        }
    }

    private void OnCycleRemoved(EntityUid uid, BrownoutCycleComponent cycle, ComponentRemove args)
    {
        if (TryComp<ApcPowerReceiverComponent>(uid, out var receiver))
            receiver.PowerOffThreshold = cycle.OriginalThreshold;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _gameTiming.CurTime;
        var query = EntityQueryEnumerator<BrownoutCycleComponent, ApcPowerReceiverComponent>();
        while (query.MoveNext(out var uid, out var cycle, out var receiver))
        {
            if (curTime < cycle.NextToggle)
                continue;

            cycle.OnPhase = !cycle.OnPhase;
            // 0 = always powered; 2 = never powered (above any possible SupplyRatio).
            receiver.PowerOffThreshold = cycle.OnPhase ? 0f : 2f;
            cycle.NextToggle = curTime + CycleDelay(cycle.ShedRatio, cycle.OnPhase);
        }
    }

    // On-phase = ShedRatio fraction of cycle; off-phase = remainder. Min 0.5 s to avoid instant restarts.
    private TimeSpan CycleDelay(float shedRatio, bool onPhase)
    {
        var period = _random.NextFloat(3f, 8f);
        var delay = onPhase ? period * shedRatio : period * (1f - shedRatio);
        return TimeSpan.FromSeconds(Math.Max(0.5, delay));
    }
}
