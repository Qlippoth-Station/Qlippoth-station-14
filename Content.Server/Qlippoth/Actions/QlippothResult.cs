using Content.Server.Ghost.Roles.Components;
using Content.Shared.Damage;
using Content.Shared.Doors.Components;
using Content.Shared.Popups;
using Robust.Shared.Random;
using Content.Shared.Qlippoth.Components;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Content.Shared.Atmos;

namespace Content.Server.Qlippoth
{
    /// <summary>
    /// Abstract base for all Qlippoth results.
    /// Defines what happens when a Qlippoth action is executed.
    /// Execute returns false when the result could not do its job (wrong target, welded door...);
    /// the remaining results of that action are then skipped unless the action sets continueOnFailure.
    /// </summary>
    [ImplicitDataDefinitionForInheritors]
    public abstract partial class QlippothResult
    {
        abstract public bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null);
    }

    /// <summary>Who a popup is shown to.</summary>
    public enum QlippothPopupRecipient : byte
    {
        /// <summary>Only the initiation's target sees it (falls back to the Qlippoth).</summary>
        Target,
        /// <summary>Only the mob that caused the initiation sees it. Skipped if there is none.</summary>
        Actor,
        /// <summary>Everyone in range of the target sees it.</summary>
        Everyone,
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

    [DataDefinition]
    public abstract partial class StateResult : QlippothResult { }
    // -------------------------------------------------------------------------
    // State - Changes the Qlippoth's own state dictionary so other actions can gate on it (requireState).
    // -------------------------------------------------------------------------
    #endregion

    #region Fundamental Result Implementations
    [DataDefinition]
    public sealed partial class DamageSanityResult : EffectResult
    {
        [DataField]
        public float Amount { get; set; } = 2f;

        /// <summary>Apply the target's DrainMultiplier (true for passive auras, false for direct costs like a Seek press).</summary>
        [DataField]
        public bool Scaled { get; set; } = false;

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            // acts on the initiation's target (the corrupted crew member, the holder...); a target without SanityComponent is simply ignored.
            resultSystem.Sanity.DamageSanity(resultSystem.ResolveTarget(uid, eventArgs), Amount, Scaled);
            return true;
        }
    }

    /// <summary>
    /// Corrupts the initiation's target (usually a crew member found by ProximityInitiation).
    /// All numbers live here, so every Qlippoth defines its own corruption. Fails (skipping later results)
    /// if the chance roll misses or the target is already corrupted.
    /// </summary>
    [DataDefinition]
    public sealed partial class ApplyCorruptionResult : EffectResult
    {
        /// <summary>Chance per execution, 0..1.</summary>
        [DataField]
        public float Chance { get; set; } = 1f;

        [DataField]
        public float Duration { get; set; } = 60f;

        [DataField]
        public int Severity { get; set; } = 1;

        /// <summary>Sanity lost per second per severity point while corrupted. 0 = none.</summary>
        [DataField]
        public float SanityDrainPerSecond { get; set; } = 2f;

        /// <summary>Seconds between pulses (spread attempt + OnCorruptionPulseInitiation on this Qlippoth).</summary>
        [DataField]
        public float PulseInterval { get; set; } = 10f;

        /// <summary>Chance per pulse to corrupt each uncorrupted crew member within SpreadRadius. 0 = does not spread.</summary>
        [DataField]
        public float SpreadChance { get; set; } = 0.15f;

        [DataField]
        public float SpreadRadius { get; set; } = 5f;

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            if (Chance < 1f && !resultSystem.Random.Prob(Chance))
                return false;

            var profile = new CorruptionProfile(Duration, Severity, SanityDrainPerSecond, PulseInterval, SpreadChance, SpreadRadius);
            return resultSystem.Corruption.ApplyCorruption(resultSystem.ResolveTarget(uid, eventArgs), uid, profile);
        }
    }

    [DataDefinition]
    public sealed partial class PopupResult : EffectResult
    {
        /// <summary>Locale key. May use {$target} for the target's name.</summary>
        [DataField(required: true)]
        public string Message { get; set; } = string.Empty;

        [DataField]
        public QlippothPopupRecipient Recipient { get; set; } = QlippothPopupRecipient.Target;

        [DataField]
        public PopupType Type { get; set; } = PopupType.Small;

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var target = resultSystem.ResolveTarget(uid, eventArgs);
            var text = Loc.GetString(Message, ("target", resultSystem.EntityName(target)));

            switch (Recipient)
            {
                case QlippothPopupRecipient.Target:
                    resultSystem.Popup.PopupEntity(text, target, target, Type);
                    break;
                case QlippothPopupRecipient.Actor:
                    if (resultSystem.ResolveActor(uid, eventArgs) is { } actor)
                        resultSystem.Popup.PopupEntity(text, target, actor, Type);
                    break;
                case QlippothPopupRecipient.Everyone:
                    resultSystem.Popup.PopupEntity(text, target, Type);
                    break;
            }
            return true;
        }
    }

    [DataDefinition]
    public sealed partial class SpawnEffectResult : ProduceResult
    {
        /// <summary>Visual/effect entity to spawn (EffectVoidBlink, EffectSparks...).</summary>
        [DataField(required: true)]
        public string Prototype { get; set; } = string.Empty;

        [DataField]
        public int Count { get; set; } = 1;

        /// <summary>Spawn at the initiation's target instead of at the Qlippoth. Falls back to the Qlippoth if there is no target.</summary>
        [DataField]
        public bool AtTarget { get; set; } = true;

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var at = AtTarget ? resultSystem.ResolveTarget(uid, eventArgs) : uid;
            var coordinates = resultSystem.QlippothEntityManager.GetComponent<TransformComponent>(at).Coordinates;
            for (var i = 0; i < Count; i++)
                resultSystem.QlippothEntityManager.SpawnEntity(Prototype, coordinates);
            return true;
        }
    }

    [DataDefinition]
    public sealed partial class PlaySoundResult : ProduceResult
    {
        [DataField(required: true)]
        public string SoundPath { get; set; } = string.Empty;

        [DataField]
        public float Volume { get; set; } = 0f;

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var specifier = new SoundPathSpecifier(SoundPath);
            resultSystem.Audio.PlayPvs(specifier, uid, AudioParams.Default.WithVolume(Volume));
            return true;
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

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            ISawmill sawmill = Logger.GetSawmill("qlippoth"); // error logging

            var tile = resultSystem.Atmosphere.GetTileMixture(uid, excite: true); // atmos state of the tile the object is on
            if (tile == null)
            {
                sawmill.Warning($"ReleaseGasResult.Execute() failed to get tile mixture for entity {uid}");
                return false;
            }

            var mixture = new GasMixture(volume: 1f) // combination of gasses (1 for now) to be released
            {
                Temperature = Temperature  //mixture.Temp = ReleaseGasResult.Temp
            };
            mixture.SetMoles(GasType, Moles); // setup mixture

            // merging the gasses in the tile and in our mixture
            resultSystem.Atmosphere.Merge(tile, mixture);
            return true;
        }
    }

    [DataDefinition]
    public sealed partial class SpawnItemResult : ProduceResult
    {
        [DataField(required: true)]
        public string SpawnItem { get; set; } = string.Empty;

        [DataField]
        public int Count { get; set; } = 1;

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
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
                    return false;
                }
            }
            return true;
        }
    }

    [DataDefinition]
    public sealed partial class SpawnMobResult : ReproduceResult
    {
        [DataField(required: true)]
        public string SpawnMob { get; set; } = string.Empty;

        [DataField]
        public int Count { get; set; } = 1;

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
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
                    return false;
                }
                // need to separate this line with logic or a new result, but for testing: ghost offer functionality
                var ghostRole = resultSystem.QlippothEntityManager.EnsureComponent<GhostRoleComponent>(spawnedEntity);

                ghostRole.RoleName = "Qlippoth Influence Exertion";
                ghostRole.RoleDescription = "A creature spawned by a Qlippoth.";
                ghostRole.RoleRules = "You are free.";

                resultSystem.QlippothEntityManager.EnsureComponent<GhostTakeoverAvailableComponent>(spawnedEntity);
            }
            return true;
        }
    }
    #endregion

    #region State Result Implementations
    /// <summary>Set one key of the Qlippoth's state. Other actions gate on it with requireState.</summary>
    [DataDefinition]
    public sealed partial class SetStateResult : StateResult
    {
        [DataField(required: true)]
        public string Key { get; set; } = string.Empty;

        [DataField(required: true)]
        public string Value { get; set; } = string.Empty;

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            resultSystem.SetState(uid, Key, Value);
            return true;
        }
    }

    /// <summary>Cycle one key of the Qlippoth's state through <see cref="Values"/> (wraps around; unknown current value → first).</summary>
    [DataDefinition]
    public sealed partial class ToggleStateResult : StateResult
    {
        [DataField(required: true)]
        public string Key { get; set; } = string.Empty;

        [DataField(required: true)]
        public List<string> Values { get; set; } = new();

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            if (Values.Count == 0)
                return false;

            var index = Values.IndexOf(resultSystem.GetState(uid, Key) ?? string.Empty);
            resultSystem.SetState(uid, Key, Values[(index + 1) % Values.Count]);
            return true;
        }
    }
    #endregion

    #region Holder / Weapon Result Implementations
    /// <summary>
    /// Cancels the damage the holder is about to take. Only meaningful under OnHolderDamagedInitiation.
    /// </summary>
    [DataDefinition]
    public sealed partial class NegateDamageResult : EffectResult
    {
        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            if (eventArgs is not QlippothHolderDamagedEventArgs args)
                return false;

            args.Damage.Damage = new DamageSpecifier();
            return true;
        }
    }

    /// <summary>
    /// Adds bonus damage to the melee hit that triggered the action. Only meaningful under OnMeleeHitInitiation.
    /// </summary>
    [DataDefinition]
    public sealed partial class BonusMeleeDamageResult : EffectResult
    {
        [DataField(required: true)]
        public DamageSpecifier Damage { get; set; } = new();

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            if (eventArgs is not QlippothMeleeHitEventArgs args || Damage.Empty)
                return false;

            args.Hit.BonusDamage += Damage;
            return true;
        }
    }
    #endregion

    #region Door Result Implementations
    public enum QlippothDoorHoldMode : byte
    {
        /// <summary>Door is forced closed and bolted; opening attempts are refused.</summary>
        Sealed,
        /// <summary>Door is forced unbolted and open; closing attempts are refused.</summary>
        Released,
    }

    /// <summary>
    /// Marker the door results put on a door while holding it sealed or released.
    /// Enforced and expired by QlippothActionResultSystem (lasting effect: door hold).
    /// </summary>
    [RegisterComponent]
    public sealed partial class QlippothDoorHoldComponent : Component
    {
        public QlippothDoorHoldMode Mode;

        public TimeSpan Until;

        /// <summary>Whether the door was already bolted before the hold began, so Sealed can leave it that way.</summary>
        public bool WasBolted;
    }

    /// <summary>
    /// Unbolts and opens the target door regardless of access. Fails (and shows <see cref="FailMessage"/> to the actor)
    /// if the target is not a door or is welded shut.
    /// </summary>
    [DataDefinition]
    public sealed partial class OpenDoorResult : EffectResult
    {
        /// <summary>Locale key shown to the actor when the door cannot be opened. Null = silent.</summary>
        [DataField]
        public string? FailMessage { get; set; }

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var target = resultSystem.ResolveTarget(uid, eventArgs);
            var actor = resultSystem.ResolveActor(uid, eventArgs);

            if (resultSystem.ForceOpenDoor(target, actor))
                return true;

            if (FailMessage != null && actor != null)
                resultSystem.Popup.PopupEntity(Loc.GetString(FailMessage), target, actor.Value, PopupType.SmallCaution);
            return false;
        }
    }

    /// <summary>
    /// Holds every airlock within <see cref="Range"/> tiles of the Qlippoth sealed (closed + bolted) or released (unbolted + open)
    /// for <see cref="Duration"/> seconds. Welded doors are skipped.
    /// </summary>
    [DataDefinition]
    public sealed partial class HoldDoorsInRangeResult : EffectResult
    {
        [DataField(required: true)]
        public QlippothDoorHoldMode Mode { get; set; } = QlippothDoorHoldMode.Sealed;

        [DataField]
        public float Range { get; set; } = 7f;

        [DataField]
        public float Duration { get; set; } = 15f;

        /// <summary>Locale key shown to the actor afterwards. May use {$count}. Null = silent.</summary>
        [DataField]
        public string? Message { get; set; }

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var actor = resultSystem.ResolveActor(uid, eventArgs);
            var coordinates = resultSystem.QlippothEntityManager.GetComponent<TransformComponent>(uid).Coordinates;

            var count = 0;
            foreach (var airlock in resultSystem.Lookup.GetEntitiesInRange<AirlockComponent>(coordinates, Range))
            {
                if (resultSystem.HoldDoor(airlock.Owner, Mode, Duration, actor))
                    count++;
            }

            if (Message != null && actor != null)
                resultSystem.Popup.PopupEntity(Loc.GetString(Message, ("count", count)), actor.Value, actor.Value);
            return true;
        }
    }
    #endregion

    #region Pinpointer Result Implementations
    /// <summary>Switch the Qlippoth's Pinpointer screen on or off. Turning it off also clears the target.</summary>
    [DataDefinition]
    public sealed partial class SetPinpointerActiveResult : EffectResult
    {
        [DataField]
        public bool Active { get; set; } = true;

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            if (!Active)
                resultSystem.Pinpointer.SetTarget(uid, null); // clear first so the arrow visuals reset
            resultSystem.Pinpointer.SetActive(uid, Active);
            return true;
        }
    }

    /// <summary>
    /// Points the Qlippoth's Pinpointer at the closest entity (same map) that has one of <see cref="TargetComponents"/>,
    /// checked in order, first component with any hit wins. Fails if nothing is found.
    /// </summary>
    [DataDefinition]
    public sealed partial class PointAtNearestResult : EffectResult
    {
        /// <summary>Component YAML names, in priority order. Example: [ContainmentPortal, QGate].</summary>
        [DataField(required: true)]
        public List<string> TargetComponents { get; set; } = new();

        /// <summary>Locale key shown to the actor on success. May use {$target}. Null = silent.</summary>
        [DataField]
        public string? FoundMessage { get; set; }

        /// <summary>Locale key shown to the actor when nothing is found. Null = silent.</summary>
        [DataField]
        public string? NoTargetMessage { get; set; }

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var target = FindClosest(uid, resultSystem);
            resultSystem.Pinpointer.SetTarget(uid, target);

            var actor = resultSystem.ResolveActor(uid, eventArgs);
            if (target == null)
            {
                if (NoTargetMessage != null && actor != null)
                    resultSystem.Popup.PopupEntity(Loc.GetString(NoTargetMessage), actor.Value, actor.Value, PopupType.SmallCaution);
                return false;
            }

            if (FoundMessage != null && actor != null)
                resultSystem.Popup.PopupEntity(Loc.GetString(FoundMessage, ("target", resultSystem.EntityName(target.Value))), actor.Value, actor.Value);
            return true;
        }

        private EntityUid? FindClosest(EntityUid uid, QlippothActionResultSystem resultSystem)
        {
            var entityManager = resultSystem.QlippothEntityManager;
            var xform = entityManager.GetComponent<TransformComponent>(uid);
            var mapId = xform.MapID;
            var origin = resultSystem.QlippothTransform.GetWorldPosition(xform);

            foreach (var componentName in TargetComponents)
            {
                if (!entityManager.ComponentFactory.TryGetRegistration(componentName, out var registration))
                {
                    Logger.GetSawmill("qlippoth").Warning($"PointAtNearestResult on {uid}: unknown component '{componentName}' in targetComponents.");
                    continue;
                }

                EntityUid? best = null;
                var bestDistance = float.MaxValue;

                foreach (var (otherUid, _) in entityManager.GetAllComponents(registration.Type))
                {
                    if (otherUid == uid || !entityManager.TryGetComponent(otherUid, out TransformComponent? otherXform) || otherXform.MapID != mapId)
                        continue;

                    var distance = (resultSystem.QlippothTransform.GetWorldPosition(otherXform) - origin).LengthSquared();
                    if (distance >= bestDistance)
                        continue;

                    bestDistance = distance;
                    best = otherUid;
                }

                if (best != null)
                    return best;
            }

            return null;
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

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
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
                    return false;
                }
                var ghostRole = resultSystem.QlippothEntityManager.EnsureComponent<GhostRoleComponent>(spawnedEntity);

                ghostRole.RoleName = "Qlippoth Servant";
                ghostRole.RoleDescription = "A creature spawned by a Qlippoth.";
                ghostRole.RoleRules = "You are a monster. Obey your master.";

                resultSystem.QlippothEntityManager.EnsureComponent<GhostTakeoverAvailableComponent>(spawnedEntity);
            }
            return true;
        }
    }
    #endregion

    #region Other Result Implementations
    #endregion
}
