using Content.Server.Administration;
using Content.Server.Chemistry.EntitySystems;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.Server.Chemistry.Commands;

/// <summary>
/// Auto-lays an L-shaped reagent pipe run between two tiles on the grid the caller is standing on, choosing the
/// right fittings via <see cref="ReagentPipeLayerSystem"/>. A mapping convenience and a live caller for the layer.
/// </summary>
[AdminCommand(AdminFlags.Mapping)]
public sealed class AutoReagentPipeCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entManager = default!;

    public string Command => "autoreagentpipe";
    public string Description => "Auto-lays an L-shaped reagent pipe run between two tiles on the grid you're on.";
    public string Help => $"{Command} <reagent> <x1> <y1> <x2> <y2>";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 5)
        {
            shell.WriteError($"Expected 5 arguments.\nUsage: {Help}");
            return;
        }

        ReagentPipeLayout layout;
        switch (args[0].ToLowerInvariant())
        {
            case "reagent":
                layout = new ReagentPipeLayout("ReagentPipeStraight", "ReagentPipeBend",
                    "ReagentPipeTJunction", "ReagentPipeFourway");
                break;
            default:
                shell.WriteError($"Unknown pipe type '{args[0]}'. Expected reagent.");
                return;
        }

        if (!int.TryParse(args[1], out var x1) || !int.TryParse(args[2], out var y1)
            || !int.TryParse(args[3], out var x2) || !int.TryParse(args[4], out var y2))
        {
            shell.WriteError("Coordinates must be integer tile indices.");
            return;
        }

        if (shell.Player?.AttachedEntity is not { } player
            || !_entManager.TryGetComponent<TransformComponent>(player, out var xform)
            || xform.GridUid is not { } gridUid
            || !_entManager.TryGetComponent<MapGridComponent>(gridUid, out var gridComp))
        {
            shell.WriteError("You must be standing on a grid.");
            return;
        }

        var tiles = ReagentPipeLayerSystem.LShape(new Vector2i(x1, y1), new Vector2i(x2, y2));
        _entManager.System<ReagentPipeLayerSystem>().LayNetwork((gridUid, gridComp), tiles, layout);
        shell.WriteLine($"Laid a {args[0]} line of {tiles.Count} segments.");
    }
}
