using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared.Qlippoth;

/// <summary>
/// Research methods deliberately describe different ways of studying one contained specimen.
/// They are not the shared station-R&amp;D disciplines and never add points to a ResearchServer.
/// </summary>
[Serializable, NetSerializable]
public enum QlippothResearchActivity : byte
{
    Observation,
    ResonanceScan,
    ControlledTest,
    ContainmentDiagnostics,
}

/// <summary>
/// A guaranteed checkpoint that every specimen of a Qlippoth kind can reveal. These checkpoint
/// templates are mixed into a procedurally generated per-specimen tree, like artifact nodes are
/// selected from a shared trigger pool during Xenoarchaeology generation.
/// </summary>
[DataDefinition]
public sealed partial class QlippothResearchNode
{
    /// <summary>Stable identifier for this kind-specific checkpoint template.</summary>
    [DataField(required: true)]
    public string Id { get; set; } = string.Empty;

    /// <summary>Localized title for this checkpoint.</summary>
    [DataField(required: true)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Localized description shown when this checkpoint is found.</summary>
    [DataField]
    public string Description { get; set; } = string.Empty;

    /// <summary>Which experiment types may reveal this checkpoint on a specimen.</summary>
    [DataField]
    public List<QlippothResearchActivity> Activities { get; set; } = new();
}

/// <summary>
/// A node in one specimen's generated graph. Unlike the checkpoint template above, this contains
/// mutable progress, prerequisites, and unlock state and is stored on the Qlippoth entity.
/// </summary>
[DataDefinition]
public sealed partial class QlippothResearchNodeProgress
{
    [DataField]
    public string Id { get; set; } = string.Empty;

    [DataField]
    public string Name { get; set; } = string.Empty;

    [DataField]
    public string Description { get; set; } = string.Empty;

    [DataField]
    public int RequiredProgress { get; set; } = 1;

    [DataField]
    public int Progress { get; set; }

    [DataField]
    public List<string> Prerequisites { get; set; } = new();

    [DataField]
    public List<QlippothResearchActivity> Activities { get; set; } = new();

    [DataField]
    public bool IsCheckpoint { get; set; }

    [DataField]
    public bool IsGear { get; set; }

    [DataField]
    public bool Unlocked { get; set; }

}

/// <summary>
/// Read-only row sent to the research console. Keeping UI state separate from the profile prevents
/// clients from changing node progress, prerequisites, or fabrication state.
/// </summary>
[Serializable, NetSerializable]
public sealed class QlippothResearchNodeState
{
    public string Id;
    public string Name;
    public string Description;
    public int Progress;
    public int RequiredProgress;
    public bool Unlocked;
    public bool Available;
    public bool IsCheckpoint;
    public bool IsGear;
    public List<QlippothResearchActivity> Activities;
    public string? GearPrototype;
    public bool GearIssued;

    public QlippothResearchNodeState(string id, string name, string description, int progress,
        int requiredProgress, bool unlocked, bool available, bool isCheckpoint, bool isGear,
        List<QlippothResearchActivity> activities,
        string? gearPrototype, bool gearIssued)
    {
        Id = id;
        Name = name;
        Description = description;
        Progress = progress;
        RequiredProgress = requiredProgress;
        Unlocked = unlocked;
        Available = available;
        IsCheckpoint = isCheckpoint;
        IsGear = isGear;
        Activities = activities;
        GearPrototype = gearPrototype;
        GearIssued = gearIssued;
    }
}

/// <summary>
/// Client request to perform a method on a specific available node. The server rechecks the
/// chamber occupant, prerequisites, method, and cooldown; this message is only a request.
/// </summary>
[Serializable, NetSerializable]
public sealed class QlippothResearchActivityMessage : BoundUserInterfaceMessage
{
    public string NodeId;
    public QlippothResearchActivity Activity;

    public QlippothResearchActivityMessage(string nodeId, QlippothResearchActivity activity)
    {
        NodeId = nodeId;
        Activity = activity;
    }
}

/// <summary>Request to issue the specimen-specific Q-Gear design unlocked by a research node.</summary>
[Serializable, NetSerializable]
public sealed class QlippothResearchGearMessage : BoundUserInterfaceMessage
{
    public string NodeId;

    public QlippothResearchGearMessage(string nodeId)
    {
        NodeId = nodeId;
    }
}
