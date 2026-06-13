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
    ///     initiation: !type:TriggerInitiation
    ///     results:
    ///       - !type:PlaySoundResult
    ///         soundPath: /Audio/Qlippoth/screech.ogg
    ///         volume: -3
    ///       - !type:SpawnEntityResult
    ///         prototype: MobRatServant
    ///         count: 2
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
    }
}
