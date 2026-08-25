using Robust.Shared.GameStates;

namespace Content.Shared.Qlippoth.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class QGateComponent : Component
{
    [DataField("phase"), AutoNetworkedField]
    public QGatePhase Phase { get; set; } = QGatePhase.Phase1Rift;

    [DataField("duration"), AutoNetworkedField]
    public TimeSpan Duration { get; set; } = TimeSpan.FromMinutes(10);

    [DataField("arrivalEta"), AutoNetworkedField]
    public TimeSpan ArrivalEta { get; set; } = TimeSpan.FromMinutes(10);

    [DataField("warningEta"), AutoNetworkedField]
    public TimeSpan WarningEta { get; set; } = TimeSpan.FromMinutes(5);

    [DataField("spawnedAt"), AutoNetworkedField]
    public TimeSpan SpawnedAt { get; set; }

    [DataField("fiveMinWarningSent"), AutoNetworkedField]
    public bool FiveMinWarningSent { get; set; } = false;

    [DataField("arrivalAnnouncementSent"), AutoNetworkedField]
    public bool ArrivalAnnouncementSent { get; set; } = false;

    [DataField("riftOpened"), AutoNetworkedField]
    public bool RiftOpened { get; set; } = false;

    [DataField("riftOpenedAt"), AutoNetworkedField]
    public TimeSpan RiftOpenedAt { get; set; }

    [DataField("objectiveCompleted"), AutoNetworkedField]
    public bool ObjectiveCompleted { get; set; } = false;

    [DataField("requiredObjectives"), AutoNetworkedField]
    public int RequiredObjectives { get; set; } = 3;

    [DataField("completedObjectives"), AutoNetworkedField]
    public int CompletedObjectives { get; set; }

    [DataField("portalClosing"), AutoNetworkedField]
    public bool PortalClosing { get; set; } = false;

    [DataField("portalCloseAt"), AutoNetworkedField]
    public TimeSpan PortalCloseAt { get; set; }

    [DataField("isCleared"), AutoNetworkedField]
    public bool IsCleared { get; set; } = false;

    [DataField("isBreached"), AutoNetworkedField]
    public bool IsBreached { get; set; } = false;

    [DataField("locationName"), AutoNetworkedField]
    public string LocationName { get; set; } = "Unknown Sector";
}
