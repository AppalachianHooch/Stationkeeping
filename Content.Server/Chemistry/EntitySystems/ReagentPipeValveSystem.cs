using Content.Server.Chemistry.Components;
using Content.Server.NodeContainer;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Shared.Interaction;
using Content.Shared.NodeContainer;
using Content.Shared.Popups;

namespace Content.Server.Chemistry.EntitySystems;

/// <summary>
///     Drives <see cref="ReagentPipeValveComponent"/>: toggling it merges or splits the two pipe nets.
/// </summary>
public sealed partial class ReagentPipeValveSystem : EntitySystem
{
    [Dependency] private NodeContainerSystem _nodeContainer = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ReagentPipeValveComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ReagentPipeValveComponent, ActivateInWorldEvent>(OnActivate);
    }

    private void OnMapInit(Entity<ReagentPipeValveComponent> ent, ref MapInitEvent args)
    {
        // Apply the initial state so valves that start open are linked once their nodes exist.
        if (ent.Comp.Open)
            Set(ent, true);
    }

    private void OnActivate(Entity<ReagentPipeValveComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !args.Complex)
            return;

        // Don't claim the interaction or pop a confirmation if the nodes weren't there to toggle.
        if (!Set(ent, !ent.Comp.Open))
            return;

        _popup.PopupEntity(
            Loc.GetString(ent.Comp.Open ? "reagent-pipe-valve-open" : "reagent-pipe-valve-closed"),
            ent,
            args.User);
        args.Handled = true;
    }

    private bool Set(Entity<ReagentPipeValveComponent> ent, bool open)
    {
        // Only flip the flag once the nodes resolve, so the valve's state never desyncs from its nets.
        if (!_nodeContainer.TryGetNodes((ent.Owner, null), ent.Comp.InletName, ent.Comp.OutletName,
                out ReagentPipeNode? inlet, out ReagentPipeNode? outlet))
            return false;

        ent.Comp.Open = open;

        if (open)
        {
            inlet.AddAlwaysReachable(outlet);
            outlet.AddAlwaysReachable(inlet);
        }
        else
        {
            inlet.RemoveAlwaysReachable(outlet);
            outlet.RemoveAlwaysReachable(inlet);
        }

        return true;
    }
}
