using Content.Shared.Eye;
using Robust.Shared.GameObjects;

namespace Content.Shared.CeilingLayer;

/// <summary>
///     Hides <see cref="CeilingLayerComponent"/> infrastructure on the plenum vismask layer, revealed only by a t-ray.
/// </summary>
public sealed class SharedCeilingLayerSystem : EntitySystem
{
    [Dependency] private SharedVisibilitySystem _visibility = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CeilingLayerComponent, ComponentStartup>(OnStartup);
    }

    private void OnStartup(Entity<CeilingLayerComponent> ent, ref ComponentStartup args)
    {
        EnsureComp<VisibilityComponent>(ent);
        _visibility.SetLayer(ent.Owner, (ushort) VisibilityFlags.Plenum);
    }
}
