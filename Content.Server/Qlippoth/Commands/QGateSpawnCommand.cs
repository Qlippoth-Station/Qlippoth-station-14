using System;
using Content.Server.Administration;
using Content.Server.Qlippoth.Systems;
using Content.Shared.Administration;
using Content.Shared.Qlippoth;
using Content.Shared.Qlippoth.Components;
using Robust.Shared.Console;
using Robust.Shared.Prototypes;

namespace Content.Server.Qlippoth.Commands;

[AdminCommand(AdminFlags.Debug)]
public sealed partial class QGateSpawnCommand : IConsoleCommand
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IEntitySystemManager _systems = default!;

    public string Command => "qgate_spawn";
    public string Description => "Spawns a test Q-Gate at the executing player's location.";
    public string Help => "qgate_spawn [phase 1-5] [immediate]";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length > 2)
        {
            shell.WriteError($"Usage: {Help}");
            return;
        }

        var phase = QGatePhase.Phase1Rift;
        if (args.Length == 1 && !TryParsePhase(args[0], out phase))
        {
            shell.WriteError("Phase must be a number from 1 to 5.");
            return;
        }

        if (args.Length == 2 && (!TryParsePhase(args[0], out phase) || args[1] != "immediate"))
        {
            shell.WriteError("Usage: qgate_spawn [phase 1-5] [immediate]");
            return;
        }

        if (shell.Player?.AttachedEntity is not { Valid: true } player)
        {
            shell.WriteError("This command must be run by a player with an attached entity.");
            return;
        }

        var coordinates = _entities.GetComponent<TransformComponent>(player).Coordinates;
        var containment = _systems.GetEntitySystem<ContainmentDimensionSystem>();
        if (containment.IsContainmentDimension(_entities.GetComponent<TransformComponent>(player).MapID))
        {
            shell.WriteError("Q-Gates cannot spawn inside the Containment Dimension.");
            return;
        }

        var gate = _entities.SpawnEntity(GetPrototype(phase), coordinates);
        if (args.Length == 2 && _entities.TryGetComponent<QGateComponent>(gate, out var qgate))
        {
            qgate.ArrivalEta = TimeSpan.Zero;
            _entities.Dirty(gate, qgate);
        }

        shell.WriteLine($"Spawned {GetPrototype(phase)} at your location.");
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length == 1
            ? CompletionResult.FromHintOptions(new[] { "1", "2", "3", "4", "5" }, "phase")
            : CompletionResult.Empty;
    }

    private static bool TryParsePhase(string value, out QGatePhase phase)
    {
        phase = value switch
        {
            "1" => QGatePhase.Phase1Rift,
            "2" => QGatePhase.Phase2Verge,
            "3" => QGatePhase.Phase3Eclipse,
            "4" => QGatePhase.Phase4Abyss,
            "5" => QGatePhase.Phase5Horizon,
            _ => default
        };

        return value is "1" or "2" or "3" or "4" or "5";
    }

    private static string GetPrototype(QGatePhase phase)
    {
        return phase switch
        {
            QGatePhase.Phase2Verge => "QGatePhase2Verge",
            QGatePhase.Phase3Eclipse => "QGatePhase3Eclipse",
            QGatePhase.Phase4Abyss => "QGatePhase4Abyss",
            QGatePhase.Phase5Horizon => "QGatePhase5Horizon",
            _ => "QGatePhase1Rift"
        };
    }
}
