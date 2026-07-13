using Content.Server.Ghost.Roles.Components;
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

            var tile = resultSystem.Atmosphere.GetTileMixture(uid, excite: true); // atmos state of the tile the object is on
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

    [DataDefinition]
    public sealed partial class SpawnItemResult : ProduceResult
    {
        [DataField(required: true)]
        public string SpawnItem { get; set; } = string.Empty;

        [DataField]
        public int Count { get; set; } = 1;

        public override void Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            ISawmill sawmill = Logger.GetSawmill("qlippoth"); // error logging

            // qlippothPosition is set as EntityCoordinates here because SpawnEntity uses that as the second parameter.
            var qlippothPosition = resultSystem.QlippothEntityManager.GetComponent<TransformComponent>(uid).Coordinates; // get the position of the Qlippoth to spawn items at

            for (var i = 0; i < Count; i++)
            {
                var spawnedEntity = resultSystem.QlippothEntityManager.SpawnEntity(SpawnItem, qlippothPosition);
                if (!resultSystem.QlippothEntityManager.EntityExists(spawnedEntity))
                {
                    sawmill.Warning($"SpawnItemResult: failed to spawn {SpawnItem}");
                    return;
                }
            }
        }
    }

    [DataDefinition]
    public sealed partial class SpawnMobResult : ReproduceResult
    {
        [DataField(required: true)]
        public string SpawnMob { get; set; } = string.Empty;

        [DataField]
        public int Count { get; set; } = 1;

        public override void Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            ISawmill sawmill = Logger.GetSawmill("qlippoth"); // error logging

            // qlippothPosition is set as EntityCoordinates here because SpawnEntity uses that as the second parameter.
            var qlippothPosition = resultSystem.QlippothEntityManager.GetComponent<TransformComponent>(uid).Coordinates; // get the position of the Qlippoth to spawn items at


            for (var i = 0; i < Count; i++)
            {
                var spawnedEntity = resultSystem.QlippothEntityManager.SpawnEntity(SpawnMob, qlippothPosition);
                if (!resultSystem.QlippothEntityManager.EntityExists(spawnedEntity))
                {
                    sawmill.Warning($"SpawnMobResult: failed to spawn {SpawnMob}");
                    return;
                }
                // need to separate this line with logic or a new result, but for testing: ghost offer functionality
                var ghostRole = resultSystem.QlippothEntityManager.EnsureComponent<GhostRoleComponent>(spawnedEntity);

                ghostRole.RoleName = "Qlippoth Influence Exertion";
                ghostRole.RoleDescription = "A creature spawned by a Qlippoth.";
                ghostRole.RoleRules = "You are free.";

                resultSystem.QlippothEntityManager.EnsureComponent<GhostTakeoverAvailableComponent>(spawnedEntity);
            }
        }
    }
    #endregion

    #region Qlippoth-related Result Implementations

    [DataDefinition]
    public sealed partial class SpawnQlippothResult : ReproduceResult
    {
        [DataField(required: true)]
        public string SpawnQlippoth { get; set; } = string.Empty;

        [DataField]
        public int Count { get; set; } = 1;

        public override void Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            ISawmill sawmill = Logger.GetSawmill("qlippoth"); // error logging

            // qlippothPosition is set as EntityCoordinates here because SpawnEntity uses that as the second parameter.
            var qlippothPosition = resultSystem.QlippothEntityManager.GetComponent<TransformComponent>(uid).Coordinates; // get the position of the Qlippoth to spawn items at

            for (var i = 0; i < Count; i++)
            {
                var spawnedEntity = resultSystem.QlippothEntityManager.SpawnEntity(SpawnQlippoth, qlippothPosition);
                if (!resultSystem.QlippothEntityManager.EntityExists(spawnedEntity))
                {
                    sawmill.Warning($"SpawnQlippothResult: failed to spawn {SpawnQlippoth}");
                    return;
                }
                var ghostRole = resultSystem.QlippothEntityManager.EnsureComponent<GhostRoleComponent>(spawnedEntity);

                ghostRole.RoleName = "Qlippoth Servant";
                ghostRole.RoleDescription = "A creature spawned by a Qlippoth.";
                ghostRole.RoleRules = "You are a monster. Obey your master.";

                resultSystem.QlippothEntityManager.EnsureComponent<GhostTakeoverAvailableComponent>(spawnedEntity);
            }
        }
    }
    #endregion

    #region Other Result Implementations
    #endregion
}
