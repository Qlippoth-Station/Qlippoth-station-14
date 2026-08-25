using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared.Qlippoth.Components;

/// <summary>
/// Console component for remotely scanning and analyzing contained Qlippoths.
/// Xenoarchaeology-style interface for Science department.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class QlippothResearchConsoleComponent : Component
{
    /// <summary>
    /// The linked containment chamber entity this console monitors.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? LinkedChamber;

    /// <summary>
    /// Whether a scan is currently in progress.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool IsScanning;

    /// <summary>
    /// Time remaining on the current scan in seconds.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float ScanTimeRemaining;

    /// <summary>
    /// Duration of a full scan in seconds.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float ScanDuration = 15f;

    /// <summary>
    /// Accumulated research data points from completed scans.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int AccumulatedResearchPoints;
}

[Serializable, NetSerializable]
public enum QlippothResearchConsoleUiKey : byte
{
    Key
}

[Serializable, NetSerializable]
public sealed class QlippothResearchScanMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class QlippothResearchExtractMessage : BoundUserInterfaceMessage;

/// <summary>
/// BUI state sent to client with Qlippoth containment data.
/// </summary>
[Serializable, NetSerializable]
public sealed class QlippothResearchConsoleBuiState : BoundUserInterfaceState
{
    public bool HasLinkedChamber;
    public bool ChamberOccupied;
    public string? QlippothName;
    public QGatePhase Phase;
    public int QlippothCounter;
    public int MaxCounter;
    public float StressLevel;
    public bool IsScanning;
    public float ScanProgress;
    public int AccumulatedPoints;

    public QlippothResearchConsoleBuiState(
        bool hasLinkedChamber,
        bool chamberOccupied,
        string? qlippothName,
        QGatePhase phase,
        int qlippothCounter,
        int maxCounter,
        float stressLevel,
        bool isScanning,
        float scanProgress,
        int accumulatedPoints)
    {
        HasLinkedChamber = hasLinkedChamber;
        ChamberOccupied = chamberOccupied;
        QlippothName = qlippothName;
        Phase = phase;
        QlippothCounter = qlippothCounter;
        MaxCounter = maxCounter;
        StressLevel = stressLevel;
        IsScanning = isScanning;
        ScanProgress = scanProgress;
        AccumulatedPoints = accumulatedPoints;
    }
}
