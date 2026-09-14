using Robust.Shared.Serialization.Manager.Attributes;
using System.Collections.Generic;

namespace Content.Server.Qlippoth
{
    /// <summary>
    /// A single Qlippoth action.
    /// Pairs one initiation condition with one or more results.
    ///
    /// YAML example:
    ///   - !type:QlippothAction
    ///     actionName: parry
    ///     initiation: !type:OnHolderDamagedInitiation
    ///     requireState: { stance: defensive }   # optional, see QlippothActionsComponent.State
    ///     cooldown: 6                            # optional, seconds
    ///     results:
    ///       - !type:NegateDamageResult
    ///       - !type:PlaySoundResult
    ///         soundPath: /Audio/Weapons/block_metal1.ogg
    /// </summary>
    [DataDefinition]
    public sealed partial class QlippothAction
    {
        [DataField(required: true)]
        public string ActionName { get; set; } = default!;

        [DataField(required: true)]
        public QlippothInitiation Initiation { get; set; } = default!;

        [DataField]
        public List<QlippothResult> Results { get; set; } = new();

        /// <summary>
        /// Only fire if every entry matches the Qlippoth's current state (QlippothActionsComponent.State).
        /// Change state with SetStateResult / ToggleStateResult.
        /// </summary>
        [DataField]
        public Dictionary<string, string> RequireState { get; set; } = new();

        /// <summary>Seconds this action cannot fire again after firing. 0 = no cooldown.</summary>
        [DataField]
        public float Cooldown { get; set; } = 0f;

        /// <summary>Keep running the remaining results even if one of them reports failure.</summary>
        [DataField]
        public bool ContinueOnFailure { get; set; } = false;

        /// <summary>Runtime: earliest time the action may fire again.</summary>
        public TimeSpan NextReadyAt;
    }
}
