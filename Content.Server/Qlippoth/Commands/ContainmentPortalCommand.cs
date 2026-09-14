using Content.Server.Administration;
using Content.Server.Qlippoth.Systems;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server.Qlippoth.Commands;

[AdminCommand(AdminFlags.Debug)]
public sealed partial class ContainmentPortalCommand : IConsoleCommand
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IEntitySystemManager _systems = default!;

    public string Command => "qcontainment_portal";
    public string Description => "Spawns a Dev Station portal to the Qlippoth Containment Dimension.";
    public string Help => "qcontainment_portal";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 0)
        {
            shell.WriteError($"Usage: {Help}");
            return;
        }

        if (shell.Player?.AttachedEntity is not { Valid: true } player)
        {
            shell.WriteError("This command must be run by a player with an attached entity.");
            return;
        }

        var transform = _entities.GetComponent<TransformComponent>(player);
        var containment = _systems.GetEntitySystem<ContainmentDimensionSystem>();
        containment.EnsureContainmentDimensionCreated();
        if (containment.IsContainmentDimension(transform.MapID))
        {
            shell.WriteError("The Dev Station portal can only be placed outside the Containment Dimension.");
            return;
        }

        _entities.SpawnEntity("DevStationContainmentPortal", transform.Coordinates);
        shell.WriteLine("Spawned the Dev Station Containment Dimension portal at your location.");
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args) => CompletionResult.Empty;
}
