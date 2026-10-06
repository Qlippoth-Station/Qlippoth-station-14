using Robust.Shared.Prototypes;
using Content.Shared.Qlippoth;

namespace Content.Server.Qlippoth.Components;

/// <summary>
/// Persistent-in-runtime research state for one Qlippoth entity.
/// This component belongs to the specimen rather than its chamber or console, so transporting the
/// specimen preserves its discoveries and a replacement occupant starts with a fresh profile.
/// </summary>
[RegisterComponent]
public sealed partial class QlippothResearchProfileComponent : Component
{
    /// <summary>
    /// The generated graph is snapshotted here on first research use. It is generated once per
    /// specimen, not per console visit, so moving a Qlippoth cannot reroll its discoveries.
    /// </summary>
    [DataField]
    public List<QlippothResearchNodeProgress> Nodes = new();

    /// <summary>The specimen-specific Q-Gear design is copied from its identity when generating the graph.</summary>
    [DataField]
    public EntProtoId? GearPrototype;

    /// <summary>Set after issuing the terminal reward to prevent repeat claims from another console.</summary>
    [DataField]
    public bool GearIssued;

    /// <summary>One shared cooldown prevents rapidly cycling methods to bypass experiment pacing.</summary>
    [DataField]
    public TimeSpan NextActivityAt;

    /// <summary>Verified server events attributed to this specimen and already considered for progress.</summary>
    [DataField]
    public List<QlippothResearchEvidenceRecord> Evidence = new();

    [DataField]
    public string? LatestOutcome;

    /// <summary>Non-empty only after a valid graph has been generated and persisted.</summary>
    [DataField]
    public bool GraphGenerated;
}
