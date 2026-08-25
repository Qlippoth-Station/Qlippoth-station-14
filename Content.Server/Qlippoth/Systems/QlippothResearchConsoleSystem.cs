using Content.Shared.Qlippoth;
using Content.Shared.Qlippoth.Components;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Timing;

namespace Content.Server.Qlippoth.Systems;

/// <summary>
/// Server-side system for the Qlippoth Research Console.
/// Handles remote scanning, data extraction, and BUI state updates.
/// Xenoarchaeology-style remote analysis without entering the containment cell.
/// </summary>
public sealed class QlippothResearchConsoleSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<QlippothResearchConsoleComponent, QlippothResearchScanMessage>(OnScanPressed);
        SubscribeLocalEvent<QlippothResearchConsoleComponent, QlippothResearchExtractMessage>(OnExtractPressed);
        SubscribeLocalEvent<QlippothResearchConsoleComponent, BoundUIOpenedEvent>(OnUiOpened);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<QlippothResearchConsoleComponent>();
        while (query.MoveNext(out var uid, out var console))
        {
            if (!console.IsScanning)
                continue;

            console.ScanTimeRemaining -= frameTime;
            if (console.ScanTimeRemaining <= 0)
            {
                console.IsScanning = false;
                console.ScanTimeRemaining = 0;

                // Award research points based on Qlippoth phase
                if (console.LinkedChamber != null &&
                    TryComp<ContainmentChamberComponent>(console.LinkedChamber.Value, out var chamber) &&
                    chamber.ContainedQlippoth != null &&
                    TryComp<QlippothComponent>(chamber.ContainedQlippoth.Value, out var qlippoth))
                {
                    var points = (int)qlippoth.Phase * 50;
                    console.AccumulatedResearchPoints += points;
                }

                Dirty(uid, console);
                UpdateUiState(uid, console);
            }
        }
    }

    private void OnScanPressed(EntityUid uid, QlippothResearchConsoleComponent console, QlippothResearchScanMessage msg)
    {
        if (console.IsScanning)
            return;

        if (console.LinkedChamber == null)
            return;

        if (!TryComp<ContainmentChamberComponent>(console.LinkedChamber.Value, out var chamber) || !chamber.IsOccupied)
            return;

        console.IsScanning = true;
        console.ScanTimeRemaining = console.ScanDuration;
        Dirty(uid, console);
        UpdateUiState(uid, console);
    }

    private void OnExtractPressed(EntityUid uid, QlippothResearchConsoleComponent console, QlippothResearchExtractMessage msg)
    {
        if (console.AccumulatedResearchPoints <= 0)
            return;

        // TODO: Transfer points to the station's research system
        console.AccumulatedResearchPoints = 0;
        Dirty(uid, console);
        UpdateUiState(uid, console);
    }

    private void OnUiOpened(EntityUid uid, QlippothResearchConsoleComponent console, BoundUIOpenedEvent args)
    {
        UpdateUiState(uid, console);
    }

    private void UpdateUiState(EntityUid uid, QlippothResearchConsoleComponent console)
    {
        var hasLinkedChamber = console.LinkedChamber != null;
        var chamberOccupied = false;
        string? qlippothName = null;
        var phase = QGatePhase.Phase1Rift;
        var counter = 0;
        var maxCounter = 0;
        var stressLevel = 0f;

        if (hasLinkedChamber &&
            TryComp<ContainmentChamberComponent>(console.LinkedChamber!.Value, out var chamber))
        {
            chamberOccupied = chamber.IsOccupied;

            if (chamber.ContainedQlippoth != null &&
                TryComp<QlippothComponent>(chamber.ContainedQlippoth.Value, out var qlippoth))
            {
                qlippothName = MetaData(chamber.ContainedQlippoth.Value).EntityName;
                phase = qlippoth.Phase;
                counter = qlippoth.QlippothCounter;
                maxCounter = qlippoth.MaxCounter;
                stressLevel = qlippoth.StressLevel;
            }
        }

        var scanProgress = console.IsScanning
            ? 1f - (console.ScanTimeRemaining / console.ScanDuration)
            : 0f;

        var state = new QlippothResearchConsoleBuiState(
            hasLinkedChamber,
            chamberOccupied,
            qlippothName,
            phase,
            counter,
            maxCounter,
            stressLevel,
            console.IsScanning,
            scanProgress,
            console.AccumulatedResearchPoints
        );

        _ui.SetUiState(uid, QlippothResearchConsoleUiKey.Key, state);
    }
}
