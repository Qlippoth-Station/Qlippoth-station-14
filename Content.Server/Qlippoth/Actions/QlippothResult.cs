using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Content.Shared.Atmos;

namespace Content.Server.Qlippoth
{
    /// <summary>
    /// Abstract base for all Qlippoth results.
    /// Defines what happens when a Qlippoth action is executed.
    /// </summary>
    [ImplicitDataDefinitionForInheritors]
    public abstract partial class QlippothResult
    {
        abstract public void Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null);
    }


    #region Result Types
    [DataDefinition]
    public abstract partial class EffectResult : QlippothResult { }
    // -------------------------------------------------------------------------
    // Effect - Forms of attack, heal, buff, debuff on players or mobs
    // -------------------------------------------------------------------------

    [DataDefinition]
    public abstract partial class ProduceResult : QlippothResult { }
    // -------------------------------------------------------------------------
    // Produce - Includes any physical change or material that is released from a Qlippoth. (Spawning new objects, gas, sound etc.)
    // -------------------------------------------------------------------------

    [DataDefinition]
    public abstract partial class ReproduceResult : QlippothResult { }
    // -------------------------------------------------------------------------
    // Reproduce - Spawns new Qlippoths or mobs.
    // -------------------------------------------------------------------------

    [DataDefinition]
    public abstract partial class ExternalMovementResult : QlippothResult { }
    // -------------------------------------------------------------------------
    // ExternalMovement - Moves a group of targets to a desired place. May include itself in the group.
    // -------------------------------------------------------------------------

    [DataDefinition]
    public abstract partial class ReactionResult : QlippothResult { }
    // -------------------------------------------------------------------------
    // Reaction - Creates chemical or atmospheric reactions in surrounding environment or a container.
    // This container can be built-in, inserted to the Qlippoth or just exist nearby.
    // -------------------------------------------------------------------------

    [DataDefinition]
    public abstract partial class ConversionResult : QlippothResult { }
    // -------------------------------------------------------------------------
    // Conversion - Converts existing physical objects or mobs to a Qlippoth variant.
    // -------------------------------------------------------------------------

    [DataDefinition]
    public abstract partial class EventResult : QlippothResult { }
    // -------------------------------------------------------------------------
    // Event - Starts or concludes an in-game event. Mostly useful for scripted story-based event chains.
    // -------------------------------------------------------------------------
    #endregion

    #region Fundamental Result Implementations
    [DataDefinition]
    public sealed partial class PlaySoundResult : ProduceResult
    {
        [DataField(required: true)]
        public string SoundPath { get; set; } = string.Empty;

        [DataField]
        public float Volume { get; set; } = 0f;
        public override void Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var specifier = new SoundPathSpecifier(SoundPath);
            resultSystem.Audio.PlayPvs(specifier, uid, AudioParams.Default.WithVolume(Volume));
        }

    }

    [DataDefinition]
    public sealed partial class ReleaseGasResult : ProduceResult
    {
        [DataField(required: true)]
        public Gas GasType { get; set; } = Gas.Oxygen; // transform this into a dictionary: Dictionary<Gas, float> to have concoctions

        [DataField]
        public float Moles { get; set; } = 1f;

        [DataField]
        public float Temperature { get; set; } = Atmospherics.T20C;

        public override void Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            ISawmill sawmill = Logger.GetSawmill("qlippoth"); // error logging

            var tile = resultSystem.Atmosphere.GetTileMixture(uid, excite: true); // tile of the object
            if (tile == null)
            {
                sawmill.Warning($"ReleaseGasResult.Execute() failed to get tile mixture for entity {uid}");
                return;
            }

            var mixture = new GasMixture(volume: 1f) // combination of gasses (1 for now) to be released
            {
                Temperature = Temperature  //mixture.Temp = ReleaseGasResult.Temp
            };
            mixture.SetMoles(GasType, Moles); // setup mixture


            // merging the gasses in the tile and in our mixture
            resultSystem.Atmosphere.Merge(tile, mixture);
        }
    }
    #endregion

    #region Complex Result Implementations
    #endregion

    #region Other Result Implementations
    #endregion
}
