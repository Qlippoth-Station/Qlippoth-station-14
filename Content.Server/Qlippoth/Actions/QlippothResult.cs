using System.Linq;
using System.Numerics;
using Content.Shared.Atmos;
using Content.Shared.Chat;
using Content.Shared.Chemistry.Components;
using Content.Shared.Damage;
using Content.Shared.Doors.Components;
using Content.Shared.Mobs;
using Content.Shared.Polymorph;
using Content.Shared.Popups;
using Content.Shared.Qlippoth.Components;
using Robust.Shared.Audio;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Server.Qlippoth
{
    // Results are grouped below by the design-doc category (Effect / Produce / Reproduce / Movement / Reaction / Conversion / Event / State / Flow).
    // This file keeps the base classes, the category markers and the original set.

    /// <summary>
    /// Abstract base for all Qlippoth results.
    /// Defines what happens when a Qlippoth action is executed.
    /// Execute returns false when the result could not do its job (wrong target, welded door...);
    /// the remaining results of that action are then skipped unless the action sets continueOnFailure.
    /// Most results carry a <c>targeting:</c> field (QlippothTargeting) that decides who they act on; default is the initiation's target.
    /// </summary>
    [ImplicitDataDefinitionForInheritors]
    public abstract partial class QlippothResult
    {
        abstract public bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null);
    }

    /// <summary>
    /// Type: ENUM (a fixed list of choices, not a result).
    /// Desc: Who a popup is shown to. Used by PopupResult's <c>recipient:</c> field.
    ///
    /// Why it lives here, at the top of the file rather than next to PopupResult: popups are the one output that every
    /// category of result may want to attach, and "Target / Actor / Everyone" is the same audience vocabulary the
    /// shared building blocks use (QlippothTargeting), so it is kept beside the base class as a general concept.
    /// Enums that belong to exactly one result (QlippothDoorHoldMode, QlippothThrowDirection, QlippothPoweredLightMode,
    /// QlippothMindTransfer) are instead declared right above that result.
    ///
    /// Note: this enum only decides who SEES the popup. Which entity the popup is anchored to still comes from the
    /// initiation's target.
    /// </summary>
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
    // Effect - Forms of attack, heal, buff, debuff on players, mobs and objects.
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

    [DataDefinition]
    public abstract partial class FlowResult : QlippothResult { }
    // -------------------------------------------------------------------------
    // Flow - Does nothing to the world by itself; controls which of the following results run (chance, random pick, conditions, delays, chains).
    // -------------------------------------------------------------------------
    #endregion

    #region Fundamental Result Implementations (original set)
    [DataDefinition]
    public sealed partial class DamageSanityResult : EffectResult
    {
        [DataField]
        public float Amount { get; set; } = 2f;

        /// <summary>Apply the target's DrainMultiplier (true for passive auras, false for direct costs like a Seek press).</summary>
        [DataField]
        public bool Scaled { get; set; } = false;

        [DataField]
        public QlippothTargeting Targeting { get; set; } = new();

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            // acts on the initiation's target (the corrupted crew member, the holder...); a target without SanityComponent is simply ignored.
            var any = false;
            foreach (var target in resultSystem.ResolveTargets(uid, eventArgs, Targeting))
            {
                resultSystem.Sanity.DamageSanity(target, Amount, Scaled);
                any = true;
            }
            return any;
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

        [DataField]
        public QlippothTargeting Targeting { get; set; } = new();

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            if (Chance < 1f && !resultSystem.Random.Prob(Chance))
                return false;

            var profile = new CorruptionProfile(Duration, Severity, SanityDrainPerSecond, PulseInterval, SpreadChance, SpreadRadius);
            var any = false;
            foreach (var target in resultSystem.ResolveTargets(uid, eventArgs, Targeting))
                any |= resultSystem.Corruption.ApplyCorruption(target, uid, profile);
            return any;
        }
    }

    [DataDefinition]
    public sealed partial class PopupResult : EffectResult
    {
        /// <summary>Locale key. May use {$target} for the target's name and {$self} for the Qlippoth's name.</summary>
        [DataField(required: true)]
        public string Message { get; set; } = string.Empty;

        [DataField]
        public QlippothPopupRecipient Recipient { get; set; } = QlippothPopupRecipient.Target;

        [DataField]
        public PopupType Type { get; set; } = PopupType.Small;

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var target = resultSystem.ResolveTarget(uid, eventArgs);
            var text = Loc.GetString(Message, ("target", resultSystem.EntityName(target)), ("self", resultSystem.EntityName(uid)));

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

        /// <summary>Overrides atTarget when set: spawn the effect at every resolved target.</summary>
        [DataField]
        public QlippothTargeting? Targeting { get; set; }

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var targeting = Targeting ?? new QlippothTargeting { Mode = AtTarget ? QlippothTargetMode.Target : QlippothTargetMode.Self };
            var any = false;
            foreach (var at in resultSystem.ResolveTargets(uid, eventArgs, targeting))
            {
                var coordinates = resultSystem.QlippothEntityManager.GetComponent<TransformComponent>(at).Coordinates;
                for (var i = 0; i < Count; i++)
                    resultSystem.QlippothEntityManager.SpawnEntity(Prototype, coordinates);
                any = true;
            }
            return any;
        }
    }

    [DataDefinition]
    public sealed partial class PlaySoundResult : ProduceResult
    {
        [DataField(required: true)]
        public string SoundPath { get; set; } = string.Empty;

        [DataField]
        public float Volume { get; set; } = 0f;

        /// <summary>Play at the initiation's target instead of at the Qlippoth.</summary>
        [DataField]
        public bool AtTarget { get; set; } = false;

        /// <summary>Play only for the target / actor player instead of for everyone nearby.</summary>
        [DataField]
        public bool Private { get; set; } = false;

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var specifier = new SoundPathSpecifier(SoundPath);
            var at = AtTarget ? resultSystem.ResolveTarget(uid, eventArgs) : uid;
            var parameters = AudioParams.Default.WithVolume(Volume);
            if (Private)
            {
                var listener = resultSystem.ResolveActor(uid, eventArgs) ?? resultSystem.ResolveTarget(uid, eventArgs);
                resultSystem.Audio.PlayEntity(specifier, listener, at, parameters);
            }
            else
                resultSystem.Audio.PlayPvs(specifier, at, parameters);
            return true;
        }
    }

    /// <summary>
    /// Releases gas onto the tile under the Qlippoth (or under the target). Use either gasType + moles for a single gas
    /// or gases: { Plasma: 5, Oxygen: 2 } for a mixture.
    /// </summary>
    [DataDefinition]
    public sealed partial class ReleaseGasResult : ProduceResult
    {
        [DataField]
        public Gas GasType { get; set; } = Gas.Oxygen;

        [DataField]
        public float Moles { get; set; } = 1f;

        /// <summary>Several gases at once. When non-empty, gasType/moles are ignored.</summary>
        [DataField]
        public Dictionary<Gas, float> Gases { get; set; } = new();

        [DataField]
        public float Temperature { get; set; } = Atmospherics.T20C;

        [DataField]
        public QlippothTargeting Targeting { get; set; } = QlippothTargeting.SelfOnly();

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var any = false;
            foreach (var at in resultSystem.ResolveTargets(uid, eventArgs, Targeting))
            {
                var tile = resultSystem.Atmosphere.GetTileMixture(at, excite: true); // atmos state of the tile the object is on
                if (tile == null)
                {
                    resultSystem.Sawmill.Debug($"ReleaseGasResult: no tile mixture under {at}");
                    continue;
                }

                var mixture = new GasMixture(volume: 1f) { Temperature = Temperature };
                if (Gases.Count > 0)
                {
                    foreach (var (gas, moles) in Gases)
                        mixture.SetMoles(gas, moles);
                }
                else
                    mixture.SetMoles(GasType, Moles);

                resultSystem.Atmosphere.Merge(tile, mixture);
                any = true;
            }
            return any;
        }
    }

    /// <summary>Spawns items. By default at the Qlippoth; destination / targeting / scatter make it spawn elsewhere.</summary>
    [DataDefinition]
    public sealed partial class SpawnItemResult : ProduceResult
    {
        [DataField(required: true)]
        public string SpawnItem { get; set; } = string.Empty;

        [DataField]
        public int Count { get; set; } = 1;

        /// <summary>Where to spawn. Default: at the Qlippoth.</summary>
        [DataField]
        public QlippothDestination Destination { get; set; } = new();

        /// <summary>Random spread in tiles around the destination.</summary>
        [DataField]
        public float Scatter { get; set; } = 0f;

        /// <summary>Anchor the spawned entity (walls, machines).</summary>
        [DataField]
        public bool Anchor { get; set; }

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            if (resultSystem.ResolveDestination(uid, eventArgs, Destination) is not { } coordinates)
                return false;

            for (var i = 0; i < Count; i++)
            {
                var spawned = resultSystem.SpawnAt(SpawnItem, coordinates, Scatter);
                if (spawned == null)
                {
                    resultSystem.Sawmill.Warning($"SpawnItemResult: failed to spawn {SpawnItem}");
                    return false;
                }
                if (Anchor)
                    resultSystem.QlippothTransform.AnchorEntity(spawned.Value);
            }
            return true;
        }
    }

    /// <summary>Spawns mobs, optionally offered as ghost roles and tracked as offspring.</summary>
    [DataDefinition]
    public sealed partial class SpawnMobResult : ReproduceResult
    {
        [DataField(required: true)]
        public string SpawnMob { get; set; } = string.Empty;

        [DataField]
        public int Count { get; set; } = 1;

        [DataField]
        public QlippothDestination Destination { get; set; } = new();

        [DataField]
        public float Scatter { get; set; } = 0.5f;

        /// <summary>Offer the spawned mob to ghosts as a playable role.</summary>
        [DataField]
        public bool GhostRole { get; set; } = true;

        [DataField]
        public string RoleName { get; set; } = "qlippoth-ghost-role-servant-name";

        [DataField]
        public string RoleDescription { get; set; } = "qlippoth-ghost-role-servant-description";

        [DataField]
        public string RoleRules { get; set; } = "qlippoth-ghost-role-servant-rules";

        /// <summary>Remember the spawned mob as offspring (maxAlive cap, Offspring targeting, OnOffspringDiedInitiation).</summary>
        [DataField]
        public bool TrackOffspring { get; set; } = true;

        /// <summary>Do not spawn while this many offspring are alive. 0 = no cap.</summary>
        [DataField]
        public int MaxAlive { get; set; } = 0;

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            return resultSystem.SpawnBrood(uid, eventArgs, SpawnMob, Count, Destination, Scatter, GhostRole, RoleName, RoleDescription, RoleRules, TrackOffspring, MaxAlive);
        }
    }
    #endregion

    #region State Result Implementations
    /// <summary>Set one key of the Qlippoth's state. Other actions gate on it with requireState; OnStateChangedInitiation reacts to it.</summary>
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

    /// <summary>
    /// Treat a state key as a number and add <see cref="Amount"/> to it (counters: how many times fed, how many kills...).
    /// Clamped to [min, max]; with wrap it goes back to min after max. Fails if the value was already at the clamp edge.
    /// </summary>
    [DataDefinition]
    public sealed partial class IncrementStateResult : StateResult
    {
        [DataField(required: true)]
        public string Key { get; set; } = string.Empty;

        [DataField]
        public int Amount { get; set; } = 1;

        [DataField]
        public int Min { get; set; } = 0;

        [DataField]
        public int Max { get; set; } = int.MaxValue;

        [DataField]
        public bool Wrap { get; set; }

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            int.TryParse(resultSystem.GetState(uid, Key), out var current);
            var next = current + Amount;
            if (Wrap && Max != int.MaxValue)
            {
                var span = Max - Min + 1;
                next = Min + ((next - Min) % span + span) % span;
            }
            else
                next = Math.Clamp(next, Min, Max);

            if (next == current)
                return false;
            resultSystem.SetState(uid, Key, next.ToString());
            return true;
        }
    }

    /// <summary>Remove a state key entirely.</summary>
    [DataDefinition]
    public sealed partial class ClearStateResult : StateResult
    {
        [DataField(required: true)]
        public string Key { get; set; } = string.Empty;

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            return resultSystem.ClearState(uid, Key);
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
            if (QlippothUtil.Unwrap(eventArgs) is not QlippothHolderDamagedEventArgs args)
                return false;

            args.Damage.Damage = new DamageSpecifier();
            return true;
        }
    }

    /// <summary>
    /// Multiplies the damage the holder is about to take (0.5 = half, 2 = double). Only meaningful under OnHolderDamagedInitiation.
    /// </summary>
    [DataDefinition]
    public sealed partial class ScaleIncomingDamageResult : EffectResult
    {
        [DataField(required: true)]
        public float Multiplier { get; set; } = 0.5f;

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            if (QlippothUtil.Unwrap(eventArgs) is not QlippothHolderDamagedEventArgs args)
                return false;

            args.Damage.Damage = args.Damage.Damage * Multiplier;
            return true;
        }
    }

    /// <summary>
    /// Adds bonus damage to the melee hit that triggered the action. Only meaningful under OnMeleeHitInitiation / OnSelfMeleeHitInitiation.
    /// </summary>
    [DataDefinition]
    public sealed partial class BonusMeleeDamageResult : EffectResult
    {
        [DataField(required: true)]
        public DamageSpecifier Damage { get; set; } = new();

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            if (QlippothUtil.Unwrap(eventArgs) is not QlippothMeleeHitEventArgs args || Damage.Empty)
                return false;

            args.Hit.BonusDamage += Damage;
            return true;
        }
    }

    /// <summary>
    /// Adds bonus damage to the attack that just hit the Qlippoth (thorns: the attacker's weapon bites deeper, or negative values to blunt it).
    /// Only meaningful under OnAttackedInitiation.
    /// </summary>
    [DataDefinition]
    public sealed partial class BonusAttackedDamageResult : EffectResult
    {
        [DataField(required: true)]
        public DamageSpecifier Damage { get; set; } = new();

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            if (QlippothUtil.Unwrap(eventArgs) is not QlippothAttackedEventArgs args || Damage.Empty)
                return false;

            args.Attack.BonusDamage += Damage;
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

        [DataField]
        public QlippothTargeting Targeting { get; set; } = new();

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var actor = resultSystem.ResolveActor(uid, eventArgs);
            var any = false;
            EntityUid? failed = null;
            foreach (var target in resultSystem.ResolveTargets(uid, eventArgs, Targeting))
            {
                if (resultSystem.ForceOpenDoor(target, actor))
                    any = true;
                else
                    failed ??= target;
            }

            if (any)
                return true;

            if (FailMessage != null && actor != null)
                resultSystem.Popup.PopupEntity(Loc.GetString(FailMessage), failed ?? uid, actor.Value, PopupType.SmallCaution);
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

    /// <summary>Bolt or unbolt the target door(s) permanently (until something else changes it).</summary>
    [DataDefinition]
    public sealed partial class SetDoorBoltsResult : EffectResult
    {
        [DataField]
        public bool Down { get; set; } = true;

        [DataField]
        public QlippothTargeting Targeting { get; set; } = new();

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var any = false;
            foreach (var target in resultSystem.ResolveTargets(uid, eventArgs, Targeting))
            {
                if (!resultSystem.QlippothEntityManager.HasComponent<DoorBoltComponent>(target))
                    continue;
                resultSystem.SetDoorBolts(target, Down);
                any = true;
            }
            return any;
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
            EntityUid? target = null;
            foreach (var componentName in TargetComponents)
            {
                target = resultSystem.FindNearestWithComponent(uid, componentName);
                if (target != null)
                    break;
            }
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
    }
    #endregion

    #region Qlippoth-related Result Implementations
    /// <summary>Spawns new Qlippoths (any prototype with QlippothActions). Same knobs as SpawnMobResult.</summary>
    [DataDefinition]
    public sealed partial class SpawnQlippothResult : ReproduceResult
    {
        [DataField(required: true)]
        public string SpawnQlippoth { get; set; } = string.Empty;

        [DataField]
        public int Count { get; set; } = 1;

        [DataField]
        public QlippothDestination Destination { get; set; } = new();

        [DataField]
        public float Scatter { get; set; } = 0.5f;

        /// <summary>Offer the spawned Qlippoth to ghosts as a playable role (only sensible for mob Qlippoths).</summary>
        [DataField]
        public bool GhostRole { get; set; } = false;

        [DataField]
        public string RoleName { get; set; } = "qlippoth-ghost-role-spawn-name";

        [DataField]
        public string RoleDescription { get; set; } = "qlippoth-ghost-role-spawn-description";

        [DataField]
        public string RoleRules { get; set; } = "qlippoth-ghost-role-spawn-rules";

        [DataField]
        public bool TrackOffspring { get; set; } = true;

        [DataField]
        public int MaxAlive { get; set; } = 0;

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            return resultSystem.SpawnBrood(uid, eventArgs, SpawnQlippoth, Count, Destination, Scatter, GhostRole, RoleName, RoleDescription, RoleRules, TrackOffspring, MaxAlive);
        }
    }
    #endregion

    #region Effect Result Implementations
    // -------------------------------------------------------------------------
    // Effect results: attacks, heals, buffs, debuffs, and direct manipulation of players / mobs / objects.
    // Every one of them carries `targeting:` (default: the initiation's target), so the same result is a touch, an aura or a nova
    // depending on targeting.mode.
    // -------------------------------------------------------------------------

    /// <summary>Deal damage. damage: { types: { Heat: 10 } } or { groups: { Brute: 15 } }. Negative values heal.</summary>
    [DataDefinition]
    public sealed partial class DamageResult : EffectResult
    {
        [DataField(required: true)]
        public DamageSpecifier Damage { get; set; } = new();

        [DataField]
        public bool IgnoreResistances { get; set; }

        [DataField]
        public QlippothTargeting Targeting { get; set; } = new();

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var any = false;
            foreach (var target in resultSystem.ResolveTargets(uid, eventArgs, Targeting))
                any |= resultSystem.Damageable.TryChangeDamage(target, Damage, IgnoreResistances, origin: uid);
            return any;
        }
    }

    /// <summary>Heal damage. Either a specific amount per type (damage: { types: { Brute: 20 } }) or everything with healAll.</summary>
    [DataDefinition]
    public sealed partial class HealResult : EffectResult
    {
        [DataField]
        public DamageSpecifier Damage { get; set; } = new();

        [DataField]
        public bool HealAll { get; set; }

        [DataField]
        public QlippothTargeting Targeting { get; set; } = new();

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var any = false;
            foreach (var target in resultSystem.ResolveTargets(uid, eventArgs, Targeting))
            {
                if (!resultSystem.QlippothEntityManager.HasComponent<Content.Shared.Damage.Components.DamageableComponent>(target))
                    continue;
                if (HealAll)
                    resultSystem.Damageable.ClearAllDamage(target);
                else
                    resultSystem.Damageable.TryChangeDamage(target, Damage * -1f, true, origin: uid);
                any = true;
            }
            return any;
        }
    }

    /// <summary>Stun (paralyze) the target for Duration seconds. With knockdown: false the target stays standing but cannot act.</summary>
    [DataDefinition]
    public sealed partial class StunResult : EffectResult
    {
        [DataField]
        public float Duration { get; set; } = 3f;

        [DataField]
        public bool Knockdown { get; set; } = true;

        [DataField]
        public QlippothTargeting Targeting { get; set; } = new();

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var time = TimeSpan.FromSeconds(Duration);
            var any = false;
            foreach (var target in resultSystem.ResolveTargets(uid, eventArgs, Targeting))
            {
                any |= Knockdown
                    ? resultSystem.Stun.TryAddParalyzeDuration(target, time)
                    : resultSystem.Stun.TryAddStunDuration(target, time);
            }
            return any;
        }
    }

    /// <summary>Knock the target down (it can still crawl / act) for Duration seconds.</summary>
    [DataDefinition]
    public sealed partial class KnockdownResult : EffectResult
    {
        [DataField]
        public float Duration { get; set; } = 3f;

        [DataField]
        public QlippothTargeting Targeting { get; set; } = new();

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var any = false;
            foreach (var target in resultSystem.ResolveTargets(uid, eventArgs, Targeting))
                any |= resultSystem.Stun.TryKnockdown(target, TimeSpan.FromSeconds(Duration), force: true);
            return any;
        }
    }

    /// <summary>Drain stamina (enough of it knocks the target out). Negative values restore.</summary>
    [DataDefinition]
    public sealed partial class StaminaDamageResult : EffectResult
    {
        [DataField]
        public float Amount { get; set; } = 30f;

        [DataField]
        public QlippothTargeting Targeting { get; set; } = new();

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var any = false;
            foreach (var target in resultSystem.ResolveTargets(uid, eventArgs, Targeting))
            {
                if (!resultSystem.QlippothEntityManager.HasComponent<Content.Shared.Damage.Components.StaminaComponent>(target))
                    continue;
                resultSystem.Stamina.TakeStaminaDamage(target, Amount, source: uid);
                any = true;
            }
            return any;
        }
    }

    /// <summary>Electrocute the target: shock damage plus a stun. Insulated gloves protect unless ignoreInsulation.</summary>
    [DataDefinition]
    public sealed partial class ElectrocuteResult : EffectResult
    {
        [DataField]
        public int Damage { get; set; } = 10;

        [DataField]
        public float Duration { get; set; } = 2f;

        [DataField]
        public bool IgnoreInsulation { get; set; }

        [DataField]
        public QlippothTargeting Targeting { get; set; } = new();

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var any = false;
            foreach (var target in resultSystem.ResolveTargets(uid, eventArgs, Targeting))
                any |= resultSystem.Electrocution.TryDoElectrocution(target, uid, Damage, TimeSpan.FromSeconds(Duration), true, ignoreInsulation: IgnoreInsulation);
            return any;
        }
    }

    /// <summary>Flash the target (blind + slow). Use targeting InRange for a room-wide flash.</summary>
    [DataDefinition]
    public sealed partial class FlashResult : EffectResult
    {
        [DataField]
        public float Duration { get; set; } = 4f;

        /// <summary>Movement speed multiplier while flashed.</summary>
        [DataField]
        public float SlowTo { get; set; } = 0.8f;

        [DataField]
        public QlippothTargeting Targeting { get; set; } = new();

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var any = false;
            foreach (var target in resultSystem.ResolveTargets(uid, eventArgs, Targeting))
            {
                resultSystem.Flash.Flash(target, resultSystem.ResolveActor(uid, eventArgs), uid, TimeSpan.FromSeconds(Duration), SlowTo);
                any = true;
            }
            return any;
        }
    }

    /// <summary>
    /// Add (or remove) a status effect entity prototype: StatusEffectBlindness, StatusEffectDrunk, StatusEffectForcedSleeping,
    /// StatusEffectStutter, StatusEffectSlowdown, StatusEffectSeeingRainbow... duration 0 = permanent until removed.
    /// </summary>
    [DataDefinition]
    public sealed partial class StatusEffectResult : EffectResult
    {
        [DataField(required: true)]
        public EntProtoId Effect { get; set; }

        [DataField]
        public float Duration { get; set; } = 10f;

        /// <summary>Remove the effect instead of adding it.</summary>
        [DataField]
        public bool Remove { get; set; }

        [DataField]
        public QlippothTargeting Targeting { get; set; } = new();

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var any = false;
            foreach (var target in resultSystem.ResolveTargets(uid, eventArgs, Targeting))
            {
                if (Remove)
                    any |= resultSystem.StatusEffects.TryRemoveStatusEffect(target, Effect);
                else if (Duration <= 0f)
                    any |= resultSystem.StatusEffects.TrySetStatusEffectDuration(target, Effect, null);
                else
                    any |= resultSystem.StatusEffects.TryAddStatusEffectDuration(target, Effect, TimeSpan.FromSeconds(Duration));
            }
            return any;
        }
    }

    /// <summary>Make the target shake for Duration seconds.</summary>
    [DataDefinition]
    public sealed partial class JitterResult : EffectResult
    {
        [DataField]
        public float Duration { get; set; } = 5f;

        [DataField]
        public float Amplitude { get; set; } = 10f;

        [DataField]
        public float Frequency { get; set; } = 4f;

        [DataField]
        public QlippothTargeting Targeting { get; set; } = new();

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var any = false;
            foreach (var target in resultSystem.ResolveTargets(uid, eventArgs, Targeting))
            {
                resultSystem.Jitter.DoJitter(target, TimeSpan.FromSeconds(Duration), true, Amplitude, Frequency);
                any = true;
            }
            return any;
        }
    }

    /// <summary>Set the target on fire (adds fire stacks). Needs Flammable on the target.</summary>
    [DataDefinition]
    public sealed partial class IgniteResult : EffectResult
    {
        [DataField]
        public float FireStacks { get; set; } = 2f;

        [DataField]
        public QlippothTargeting Targeting { get; set; } = new();

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var any = false;
            foreach (var target in resultSystem.ResolveTargets(uid, eventArgs, Targeting))
            {
                if (!resultSystem.QlippothEntityManager.TryGetComponent<Content.Shared.Atmos.Components.FlammableComponent>(target, out var flammable))
                    continue;
                resultSystem.Flammable.AdjustFireStacks(target, FireStacks, flammable, ignite: true);
                any = true;
            }
            return any;
        }
    }

    /// <summary>Put out the target's fire.</summary>
    [DataDefinition]
    public sealed partial class ExtinguishResult : EffectResult
    {
        [DataField]
        public QlippothTargeting Targeting { get; set; } = new();

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var any = false;
            foreach (var target in resultSystem.ResolveTargets(uid, eventArgs, Targeting))
            {
                if (!resultSystem.QlippothEntityManager.TryGetComponent<Content.Shared.Atmos.Components.FlammableComponent>(target, out var flammable))
                    continue;
                resultSystem.Flammable.Extinguish(target, flammable);
                any = true;
            }
            return any;
        }
    }

    /// <summary>
    /// Multiply the target's walk / sprint speed for Duration seconds (0.5 = slowed to half, 1.5 = hasted).
    /// Lasting effect: managed by QlippothActionResultSystem, stacks by replacing.
    /// </summary>
    [DataDefinition]
    public sealed partial class SpeedModifierResult : EffectResult
    {
        [DataField]
        public float Walk { get; set; } = 0.5f;

        [DataField]
        public float Sprint { get; set; } = 0.5f;

        [DataField]
        public float Duration { get; set; } = 5f;

        [DataField]
        public QlippothTargeting Targeting { get; set; } = new();

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var any = false;
            foreach (var target in resultSystem.ResolveTargets(uid, eventArgs, Targeting))
                any |= resultSystem.ApplySpeedModifier(target, Walk, Sprint, Duration);
            return any;
        }
    }

    /// <summary>Give sanity back.</summary>
    [DataDefinition]
    public sealed partial class RestoreSanityResult : EffectResult
    {
        [DataField]
        public float Amount { get; set; } = 10f;

        [DataField]
        public QlippothTargeting Targeting { get; set; } = new();

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var any = false;
            foreach (var target in resultSystem.ResolveTargets(uid, eventArgs, Targeting))
            {
                if (!resultSystem.QlippothEntityManager.HasComponent<Content.Shared.Qlippoth.Components.SanityComponent>(target))
                    continue;
                resultSystem.Sanity.RestoreSanity(target, Amount);
                any = true;
            }
            return any;
        }
    }

    /// <summary>Change how fast the target loses sanity to auras (drainMultiplier). 0 = immune, 2 = twice as fragile.</summary>
    [DataDefinition]
    public sealed partial class SetSanityDrainMultiplierResult : EffectResult
    {
        [DataField]
        public float Multiplier { get; set; } = 1f;

        [DataField]
        public QlippothTargeting Targeting { get; set; } = new();

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var any = false;
            foreach (var target in resultSystem.ResolveTargets(uid, eventArgs, Targeting))
            {
                if (!resultSystem.QlippothEntityManager.TryGetComponent<Content.Shared.Qlippoth.Components.SanityComponent>(target, out var sanity))
                    continue;
                sanity.DrainMultiplier = Multiplier;
                resultSystem.QlippothEntityManager.Dirty(target, sanity);
                any = true;
            }
            return any;
        }
    }

    /// <summary>Cure corruption on the target.</summary>
    [DataDefinition]
    public sealed partial class RemoveCorruptionResult : EffectResult
    {
        [DataField]
        public QlippothTargeting Targeting { get; set; } = new();

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var any = false;
            foreach (var target in resultSystem.ResolveTargets(uid, eventArgs, Targeting))
            {
                if (!resultSystem.QlippothEntityManager.HasComponent<Content.Shared.Qlippoth.Components.QlippothCorruptionComponent>(target))
                    continue;
                resultSystem.Corruption.RemoveCorruption(target);
                any = true;
            }
            return any;
        }
    }

    /// <summary>Inject a reagent straight into the target's bloodstream (poison, medicine, hallucinogens...).</summary>
    [DataDefinition]
    public sealed partial class InjectReagentResult : EffectResult
    {
        [DataField(required: true)]
        public string Reagent { get; set; } = string.Empty;

        [DataField]
        public float Amount { get; set; } = 5f;

        [DataField]
        public QlippothTargeting Targeting { get; set; } = new();

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var any = false;
            foreach (var target in resultSystem.ResolveTargets(uid, eventArgs, Targeting))
            {
                if (!resultSystem.QlippothEntityManager.HasComponent<Content.Shared.Body.Components.BloodstreamComponent>(target))
                    continue;
                any |= resultSystem.Bloodstream.TryAddToBloodstream(target, new Solution(Reagent, Amount));
            }
            return any;
        }
    }

    /// <summary>Force the target into a mob state (Critical, Dead, Alive). Dead on an already dead mob fails.</summary>
    [DataDefinition]
    public sealed partial class SetMobStateResult : EffectResult
    {
        [DataField]
        public MobState State { get; set; } = MobState.Dead;

        [DataField]
        public QlippothTargeting Targeting { get; set; } = new();

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var any = false;
            foreach (var target in resultSystem.ResolveTargets(uid, eventArgs, Targeting))
            {
                if (!resultSystem.QlippothEntityManager.TryGetComponent<Content.Shared.Mobs.Components.MobStateComponent>(target, out var mobState) || mobState.CurrentState == State)
                    continue;
                resultSystem.MobState.ChangeMobState(target, State, mobState, uid);
                any = true;
            }
            return any;
        }
    }

    /// <summary>Gib the target (needs a body).</summary>
    [DataDefinition]
    public sealed partial class GibResult : EffectResult
    {
        [DataField]
        public QlippothTargeting Targeting { get; set; } = new();

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var any = false;
            foreach (var target in resultSystem.ResolveTargets(uid, eventArgs, Targeting).ToArray())
            {
                if (target == uid)
                    continue;
                var giblets = resultSystem.Gibbing.Gib(target, true, uid);
                any |= giblets.Count > 0 || !resultSystem.QlippothEntityManager.EntityExists(target);
            }
            return any;
        }
    }

    /// <summary>Delete the target outright. targeting Self makes the Qlippoth vanish (do this last in a chain).</summary>
    [DataDefinition]
    public sealed partial class DeleteResult : EffectResult
    {
        [DataField]
        public QlippothTargeting Targeting { get; set; } = QlippothTargeting.SelfOnly();

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var any = false;
            foreach (var target in resultSystem.ResolveTargets(uid, eventArgs, Targeting).ToArray())
            {
                resultSystem.QlippothEntityManager.QueueDeleteEntity(target);
                any = true;
            }
            return any;
        }
    }

    /// <summary>Make the target drop everything in its hands.</summary>
    [DataDefinition]
    public sealed partial class DropHeldItemsResult : EffectResult
    {
        [DataField]
        public QlippothTargeting Targeting { get; set; } = new();

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var any = false;
            foreach (var target in resultSystem.ResolveTargets(uid, eventArgs, Targeting))
            {
                if (!resultSystem.QlippothEntityManager.HasComponent<Content.Shared.Hands.Components.HandsComponent>(target))
                    continue;
                foreach (var held in resultSystem.Hands.EnumerateHeld(target).ToArray())
                    any |= resultSystem.Hands.TryDrop(target, held, checkActionBlocker: false);
            }
            return any;
        }
    }

    /// <summary>Force the target to perform an emote (Scream, Laugh, Cry...).</summary>
    [DataDefinition]
    public sealed partial class ForceEmoteResult : EffectResult
    {
        [DataField(required: true)]
        public string Emote { get; set; } = "Scream";

        [DataField]
        public QlippothTargeting Targeting { get; set; } = new();

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var any = false;
            foreach (var target in resultSystem.ResolveTargets(uid, eventArgs, Targeting))
                any |= resultSystem.Chat.TryEmoteWithChat(target, Emote, ignoreActionBlocker: true, forceEmote: true);
            return any;
        }
    }

    /// <summary>Force the target to say something out loud (possession, compulsion). Message is a locale key or raw text.</summary>
    [DataDefinition]
    public sealed partial class ForceSayResult : EffectResult
    {
        [DataField(required: true)]
        public string Message { get; set; } = string.Empty;

        [DataField]
        public bool Whisper { get; set; }

        [DataField]
        public QlippothTargeting Targeting { get; set; } = new();

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var text = Loc.GetString(Message);
            var any = false;
            foreach (var target in resultSystem.ResolveTargets(uid, eventArgs, Targeting))
            {
                resultSystem.Chat.TrySendInGameICMessage(target, text, Whisper ? InGameICChatType.Whisper : InGameICChatType.Speak, false, ignoreActionBlocker: true);
                any = true;
            }
            return any;
        }
    }

    /// <summary>Rename the target and / or change its description (a Qlippoth that marks its victims, or renames itself per phase).</summary>
    [DataDefinition]
    public sealed partial class RenameResult : EffectResult
    {
        /// <summary>Locale key or raw text. Null = keep name.</summary>
        [DataField]
        public string? Name { get; set; }

        /// <summary>Locale key or raw text. Null = keep description.</summary>
        [DataField]
        public string? Description { get; set; }

        [DataField]
        public QlippothTargeting Targeting { get; set; } = QlippothTargeting.SelfOnly();

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var any = false;
            foreach (var target in resultSystem.ResolveTargets(uid, eventArgs, Targeting))
            {
                if (Name != null)
                    resultSystem.MetaData.SetEntityName(target, Loc.GetString(Name));
                if (Description != null)
                    resultSystem.MetaData.SetEntityDescription(target, Loc.GetString(Description));
                any = true;
            }
            return any;
        }
    }

    /// <summary>Appends a line to the examine text. Only meaningful under OnExaminedInitiation. Message may use {$self}.</summary>
    [DataDefinition]
    public sealed partial class ExamineTextResult : EffectResult
    {
        [DataField(required: true)]
        public string Message { get; set; } = string.Empty;

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            if (QlippothUtil.Unwrap(eventArgs) is not QlippothExamineEventArgs args)
                return false;

            args.Examine.PushMarkup(Loc.GetString(Message, ("self", resultSystem.EntityName(uid))));
            return true;
        }
    }

    /// <summary>Change the target's PointLight (by default the Qlippoth's own glow): on/off, color, radius, energy.</summary>
    [DataDefinition]
    public sealed partial class SetLightResult : EffectResult
    {
        [DataField]
        public bool? Enabled { get; set; }

        [DataField]
        public Color? Color { get; set; }

        [DataField]
        public float? Radius { get; set; }

        [DataField]
        public float? Energy { get; set; }

        [DataField]
        public QlippothTargeting Targeting { get; set; } = QlippothTargeting.SelfOnly();

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var any = false;
            foreach (var target in resultSystem.ResolveTargets(uid, eventArgs, Targeting))
            {
                if (!resultSystem.QlippothEntityManager.HasComponent<SharedPointLightComponent>(target))
                    continue;
                if (Enabled != null)
                    resultSystem.PointLight.SetEnabled(target, Enabled.Value);
                if (Color != null)
                    resultSystem.PointLight.SetColor(target, Color.Value);
                if (Radius != null)
                    resultSystem.PointLight.SetRadius(target, Radius.Value);
                if (Energy != null)
                    resultSystem.PointLight.SetEnergy(target, Energy.Value);
                any = true;
            }
            return any;
        }
    }

    public enum QlippothPoweredLightMode : byte
    {
        Off,
        On,
        Toggle,
        /// <summary>Shatter the bulb.</summary>
        Break,
    }

    /// <summary>
    /// Switch off / on / toggle / break station lights (PoweredLight fixtures). Default targeting: every light within 6 tiles.
    /// </summary>
    [DataDefinition]
    public sealed partial class PoweredLightResult : EffectResult
    {
        [DataField]
        public QlippothPoweredLightMode Mode { get; set; } = QlippothPoweredLightMode.Off;

        [DataField]
        public QlippothTargeting Targeting { get; set; } = new()
        {
            Mode = QlippothTargetMode.InRange,
            Range = 6f,
            Filter = new QlippothTargetFilter { RequiredComponent = "PoweredLight" },
        };

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var any = false;
            foreach (var target in resultSystem.ResolveTargets(uid, eventArgs, Targeting))
            {
                if (!resultSystem.QlippothEntityManager.TryGetComponent<Content.Shared.Light.Components.PoweredLightComponent>(target, out var light))
                    continue;
                switch (Mode)
                {
                    case QlippothPoweredLightMode.Off:
                        resultSystem.PoweredLight.SetState(target, false, light);
                        break;
                    case QlippothPoweredLightMode.On:
                        resultSystem.PoweredLight.SetState(target, true, light);
                        break;
                    case QlippothPoweredLightMode.Toggle:
                        resultSystem.PoweredLight.ToggleLight(target, light);
                        break;
                    case QlippothPoweredLightMode.Break:
                        resultSystem.PoweredLight.TryDestroyBulb(target, light);
                        break;
                }
                any = true;
            }
            return any;
        }
    }

    /// <summary>Fire an EMP pulse around the target (default: around the Qlippoth). Drains batteries, disables machines.</summary>
    [DataDefinition]
    public sealed partial class EmpResult : EffectResult
    {
        [DataField]
        public float Range { get; set; } = 4f;

        [DataField]
        public float EnergyConsumption { get; set; } = 50000f;

        [DataField]
        public float Duration { get; set; } = 10f;

        [DataField]
        public QlippothTargeting Targeting { get; set; } = QlippothTargeting.SelfOnly();

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var any = false;
            foreach (var target in resultSystem.ResolveTargets(uid, eventArgs, Targeting))
            {
                var coordinates = resultSystem.QlippothTransform.GetMapCoordinates(target);
                resultSystem.Emp.EmpPulse(coordinates, Range, EnergyConsumption, TimeSpan.FromSeconds(Duration), uid);
                any = true;
            }
            return any;
        }
    }

    /// <summary>Explode at the target (default: at the Qlippoth). Type is an explosion prototype (Default, Cryo, Radioactive...).</summary>
    [DataDefinition]
    public sealed partial class ExplosionResult : EffectResult
    {
        [DataField]
        public string Type { get; set; } = "Default";

        [DataField]
        public float TotalIntensity { get; set; } = 30f;

        [DataField]
        public float Slope { get; set; } = 5f;

        [DataField]
        public float MaxTileIntensity { get; set; } = 10f;

        [DataField]
        public QlippothTargeting Targeting { get; set; } = QlippothTargeting.SelfOnly();

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var any = false;
            foreach (var target in resultSystem.ResolveTargets(uid, eventArgs, Targeting))
            {
                var coordinates = resultSystem.QlippothTransform.GetMapCoordinates(target);
                resultSystem.Explosion.QueueExplosion(coordinates, Type, TotalIntensity, Slope, MaxTileIntensity, uid);
                any = true;
            }
            return any;
        }
    }

    /// <summary>Make the target start pulling something (by default the Qlippoth pulls the initiation's target: "it grabs you").</summary>
    [DataDefinition]
    public sealed partial class PullResult : EffectResult
    {
        /// <summary>Who pulls. Default: the Qlippoth.</summary>
        [DataField]
        public QlippothTargeting Puller { get; set; } = QlippothTargeting.SelfOnly();

        /// <summary>Who gets pulled. Default: the initiation's target.</summary>
        [DataField]
        public QlippothTargeting Targeting { get; set; } = new();

        /// <summary>Release instead of grab.</summary>
        [DataField]
        public bool Release { get; set; }

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var puller = resultSystem.ResolveTargets(uid, eventArgs, Puller).FirstOrDefault();
            if (puller == default)
                return false;

            var any = false;
            foreach (var target in resultSystem.ResolveTargets(uid, eventArgs, Targeting))
            {
                if (target == puller)
                    continue;
                if (Release)
                {
                    if (resultSystem.QlippothEntityManager.TryGetComponent<Content.Shared.Movement.Pulling.Components.PullableComponent>(target, out var pullable) && pullable.Puller == puller)
                        any |= resultSystem.Pulling.TryStopPull(target, pullable, puller);
                }
                else
                    any |= resultSystem.Pulling.TryStartPull(puller, target);
            }
            return any;
        }
    }
    #endregion

    #region Produce Result Implementations
    // -------------------------------------------------------------------------
    // Produce results: things that come out of the Qlippoth (objects, sound, speech, projectiles).
    // SpawnItemResult / SpawnEffectResult / PlaySoundResult / ReleaseGasResult are in the original set region above.
    // -------------------------------------------------------------------------

    /// <summary>The Qlippoth (or the target, with targeting) speaks in local chat. Message is a locale key or raw text; may use {$target}.</summary>
    [DataDefinition]
    public sealed partial class SpeakResult : ProduceResult
    {
        [DataField(required: true)]
        public string Message { get; set; } = string.Empty;

        /// <summary>Pick one at random from these instead of Message (leave message as a fallback).</summary>
        [DataField]
        public List<string> Messages { get; set; } = new();

        [DataField]
        public bool Whisper { get; set; }

        /// <summary>Show as an emote (*the statue hums*) instead of speech.</summary>
        [DataField]
        public bool Emote { get; set; }

        /// <summary>Who speaks. Default: the Qlippoth.</summary>
        [DataField]
        public QlippothTargeting Targeting { get; set; } = QlippothTargeting.SelfOnly();

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var key = Messages.Count > 0 ? resultSystem.Random.Pick(Messages) : Message;
            var target = resultSystem.ResolveTarget(uid, eventArgs);
            var text = Loc.GetString(key, ("target", resultSystem.EntityName(target)), ("self", resultSystem.EntityName(uid)));
            var type = Emote ? InGameICChatType.Emote : Whisper ? InGameICChatType.Whisper : InGameICChatType.Speak;

            var any = false;
            foreach (var speaker in resultSystem.ResolveTargets(uid, eventArgs, Targeting))
            {
                resultSystem.Chat.TrySendInGameICMessage(speaker, text, type, false, ignoreActionBlocker: true);
                any = true;
            }
            return any;
        }
    }

    /// <summary>Send a message straight to the target player's chat (a whisper only they hear; "the voice in your head"). Locale key or raw text.</summary>
    [DataDefinition]
    public sealed partial class WhisperToResult : ProduceResult
    {
        [DataField(required: true)]
        public string Message { get; set; } = string.Empty;

        [DataField]
        public List<string> Messages { get; set; } = new();

        [DataField]
        public QlippothTargeting Targeting { get; set; } = new();

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var key = Messages.Count > 0 ? resultSystem.Random.Pick(Messages) : Message;
            var any = false;
            foreach (var target in resultSystem.ResolveTargets(uid, eventArgs, Targeting))
                any |= resultSystem.SendPrivateMessage(target, Loc.GetString(key, ("target", resultSystem.EntityName(target)), ("self", resultSystem.EntityName(uid))));
            return any;
        }
    }

    /// <summary>
    /// Shoot a projectile prototype (BulletKinetic, BulletLaser, any projectile entity) from the Qlippoth toward each target.
    /// </summary>
    [DataDefinition]
    public sealed partial class ShootProjectileResult : ProduceResult
    {
        [DataField(required: true)]
        public string Projectile { get; set; } = string.Empty;

        [DataField]
        public float Speed { get; set; } = 20f;

        /// <summary>Shots per target. Extra shots get random spread.</summary>
        [DataField]
        public int Count { get; set; } = 1;

        /// <summary>Random spread in degrees.</summary>
        [DataField]
        public float Spread { get; set; } = 0f;

        /// <summary>Who to shoot at. Default: the initiation's target.</summary>
        [DataField]
        public QlippothTargeting Targeting { get; set; } = new();

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var any = false;
            foreach (var target in resultSystem.ResolveTargets(uid, eventArgs, Targeting))
            {
                if (target == uid)
                    continue;
                for (var i = 0; i < Count; i++)
                    any |= resultSystem.ShootAt(uid, target, Projectile, Speed, Spread);
            }
            return any;
        }
    }

    /// <summary>
    /// Throw a spawned prototype outward from the Qlippoth (shrapnel, spit, bone shards). Toward the target when there is one, else random.
    /// </summary>
    [DataDefinition]
    public sealed partial class ThrowSpawnedResult : ProduceResult
    {
        [DataField(required: true)]
        public string Prototype { get; set; } = string.Empty;

        [DataField]
        public int Count { get; set; } = 3;

        [DataField]
        public float Speed { get; set; } = 8f;

        /// <summary>Always scatter in random directions, even with a target.</summary>
        [DataField]
        public bool Random { get; set; }

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var origin = resultSystem.QlippothTransform.GetMapCoordinates(uid);
            var target = resultSystem.ResolveTarget(uid, eventArgs);
            Vector2? toward = null;
            if (!Random && target != uid)
            {
                var targetPosition = resultSystem.QlippothTransform.GetMapCoordinates(target);
                if (targetPosition.MapId == origin.MapId)
                    toward = targetPosition.Position - origin.Position;
            }

            var any = false;
            for (var i = 0; i < Count; i++)
            {
                var spawned = resultSystem.SpawnAt(Prototype, resultSystem.QlippothEntityManager.GetComponent<TransformComponent>(uid).Coordinates, 0f);
                if (spawned == null)
                    continue;
                var direction = toward ?? resultSystem.Random.NextAngle().ToVec();
                if (toward != null && Count > 1)
                    direction = (new Angle(direction) + Angle.FromDegrees(resultSystem.Random.NextFloat(-20f, 20f))).ToVec();
                resultSystem.Throwing.TryThrow(spawned.Value, direction, Speed, uid);
                any = true;
            }
            return any;
        }
    }

    /// <summary>Spawn a prototype on every tile in range (carpet the area with webs, flesh, glass shards...).</summary>
    [DataDefinition]
    public sealed partial class SpawnOnTilesInRangeResult : ProduceResult
    {
        [DataField(required: true)]
        public string Prototype { get; set; } = string.Empty;

        [DataField]
        public int Range { get; set; } = 2;

        /// <summary>Chance per tile.</summary>
        [DataField]
        public float Chance { get; set; } = 1f;

        /// <summary>Skip tiles that already hold this prototype.</summary>
        [DataField]
        public bool NoDuplicates { get; set; } = true;

        [DataField]
        public bool Anchor { get; set; }

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var any = false;
            foreach (var coordinates in resultSystem.TilesInRange(uid, Range))
            {
                if (Chance < 1f && !resultSystem.Random.Prob(Chance))
                    continue;
                if (NoDuplicates && resultSystem.HasPrototypeAt(coordinates, Prototype))
                    continue;
                var spawned = resultSystem.SpawnAt(Prototype, coordinates, 0f);
                if (spawned == null)
                    continue;
                if (Anchor)
                    resultSystem.QlippothTransform.AnchorEntity(spawned.Value);
                any = true;
            }
            return any;
        }
    }
    #endregion

    #region Reproduce Result Implementations
    // -------------------------------------------------------------------------
    // Reproduce results: new Qlippoths and mobs. SpawnMobResult / SpawnQlippothResult are in the original set region above.
    // -------------------------------------------------------------------------

    /// <summary>Spawn a copy of the Qlippoth's own prototype (mitosis). Same brood knobs as SpawnMobResult.</summary>
    [DataDefinition]
    public sealed partial class SpawnCopyResult : ReproduceResult
    {
        [DataField]
        public int Count { get; set; } = 1;

        [DataField]
        public QlippothDestination Destination { get; set; } = new() { Mode = QlippothDestinationMode.RandomNearSelf, Range = 1.5f };

        [DataField]
        public float Scatter { get; set; } = 0f;

        [DataField]
        public bool GhostRole { get; set; } = false;

        [DataField]
        public string RoleName { get; set; } = "qlippoth-ghost-role-spawn-name";

        [DataField]
        public string RoleDescription { get; set; } = "qlippoth-ghost-role-spawn-description";

        [DataField]
        public string RoleRules { get; set; } = "qlippoth-ghost-role-spawn-rules";

        [DataField]
        public bool TrackOffspring { get; set; } = true;

        [DataField]
        public int MaxAlive { get; set; } = 0;

        /// <summary>Copy the parent's state dictionary onto the child.</summary>
        [DataField]
        public bool CopyState { get; set; } = true;

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            if (resultSystem.PrototypeIdOf(uid) is not { } prototype)
                return false;
            return resultSystem.SpawnBrood(uid, eventArgs, prototype, Count, Destination, Scatter, GhostRole, RoleName, RoleDescription, RoleRules, TrackOffspring, MaxAlive, CopyState);
        }
    }

    /// <summary>Offer the Qlippoth ITSELF (or the target) to ghosts as a playable role. Turns a scripted Qlippoth into a player-controlled one.</summary>
    [DataDefinition]
    public sealed partial class OfferGhostRoleResult : ReproduceResult
    {
        [DataField]
        public string RoleName { get; set; } = "qlippoth-ghost-role-spawn-name";

        [DataField]
        public string RoleDescription { get; set; } = "qlippoth-ghost-role-spawn-description";

        [DataField]
        public string RoleRules { get; set; } = "qlippoth-ghost-role-spawn-rules";

        [DataField]
        public QlippothTargeting Targeting { get; set; } = QlippothTargeting.SelfOnly();

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var any = false;
            foreach (var target in resultSystem.ResolveTargets(uid, eventArgs, Targeting))
                any |= resultSystem.MakeGhostRole(target, RoleName, RoleDescription, RoleRules);
            return any;
        }
    }

    /// <summary>Kill every tracked offspring (or the ones in range). Recall the brood.</summary>
    [DataDefinition]
    public sealed partial class CullOffspringResult : ReproduceResult
    {
        [DataField]
        public QlippothTargeting Targeting { get; set; } = new() { Mode = QlippothTargetMode.Offspring };

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var any = false;
            foreach (var target in resultSystem.ResolveTargets(uid, eventArgs, Targeting))
            {
                resultSystem.QlippothEntityManager.QueueDeleteEntity(target);
                any = true;
            }
            return any;
        }
    }
    #endregion

    #region External Movement Result Implementations
    // -------------------------------------------------------------------------
    // External movement results: move targets (possibly including the Qlippoth) somewhere.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Teleport targets to a destination. Examples:
    ///   targeting Self + destination RandomNearSelf 6   -> blink
    ///   targeting Self + destination Target             -> jump to whoever triggered it
    ///   targeting Target + destination RandomOnStation  -> banish the victim somewhere random
    ///   targeting InRange + destination Self            -> drag everyone nearby to the Qlippoth
    /// </summary>
    [DataDefinition]
    public sealed partial class TeleportResult : ExternalMovementResult
    {
        [DataField]
        public QlippothTargeting Targeting { get; set; } = QlippothTargeting.SelfOnly();

        [DataField]
        public QlippothDestination Destination { get; set; } = new() { Mode = QlippothDestinationMode.RandomNearSelf, Range = 5f };

        /// <summary>Random spread around the destination, so a group does not stack on one tile.</summary>
        [DataField]
        public float Scatter { get; set; } = 0.5f;

        /// <summary>Effect prototype spawned at the origin and at the destination (EffectVoidBlink...). Null = none.</summary>
        [DataField]
        public string? Effect { get; set; }

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            if (resultSystem.ResolveDestination(uid, eventArgs, Destination) is not { } destination)
                return false;

            var any = false;
            foreach (var target in resultSystem.ResolveTargets(uid, eventArgs, Targeting).ToArray())
            {
                if (Effect != null)
                    resultSystem.SpawnAt(Effect, resultSystem.QlippothEntityManager.GetComponent<TransformComponent>(target).Coordinates, 0f);

                var coordinates = resultSystem.Scatter(destination, Scatter);
                resultSystem.TeleportEntity(target, coordinates);

                if (Effect != null)
                    resultSystem.SpawnAt(Effect, coordinates, 0f);
                any = true;
            }
            return any;
        }
    }

    /// <summary>Swap places with the target (or two arbitrary targets: the first two resolved).</summary>
    [DataDefinition]
    public sealed partial class SwapPlacesResult : ExternalMovementResult
    {
        [DataField]
        public QlippothTargeting Targeting { get; set; } = new();

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var target = resultSystem.ResolveTargets(uid, eventArgs, Targeting).FirstOrDefault(t => t != uid);
            if (target == default)
                return false;
            return resultSystem.QlippothTransform.SwapPositions(uid, target);
        }
    }

    public enum QlippothThrowDirection : byte
    {
        /// <summary>Push targets away from the Qlippoth (shockwave).</summary>
        AwayFromSelf,
        /// <summary>Pull targets toward the Qlippoth (vortex).</summary>
        TowardSelf,
        /// <summary>Push targets away from the initiation's target.</summary>
        AwayFromTarget,
        /// <summary>Throw targets toward the initiation's target.</summary>
        TowardTarget,
        /// <summary>Random direction each.</summary>
        Random,
        /// <summary>Fixed world-space direction (see Direction).</summary>
        Fixed,
    }

    /// <summary>
    /// Throw targets. targeting InRange + AwayFromSelf = knockback nova; targeting InRange + TowardSelf = vacuum;
    /// targeting Self + TowardTarget = lunge.
    /// </summary>
    [DataDefinition]
    public sealed partial class ThrowResult : ExternalMovementResult
    {
        [DataField]
        public QlippothThrowDirection Mode { get; set; } = QlippothThrowDirection.AwayFromSelf;

        /// <summary>Tiles to fly (roughly).</summary>
        [DataField]
        public float Distance { get; set; } = 3f;

        [DataField]
        public float Speed { get; set; } = 10f;

        /// <summary>For Fixed mode.</summary>
        [DataField]
        public Vector2 Direction { get; set; } = Vector2.UnitY;

        [DataField]
        public QlippothTargeting Targeting { get; set; } = new();

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var selfPosition = resultSystem.QlippothTransform.GetWorldPosition(uid);
            var initiationTarget = resultSystem.ResolveTarget(uid, eventArgs);
            var targetPosition = resultSystem.QlippothTransform.GetWorldPosition(initiationTarget);

            var any = false;
            foreach (var target in resultSystem.ResolveTargets(uid, eventArgs, Targeting).ToArray())
            {
                var position = resultSystem.QlippothTransform.GetWorldPosition(target);
                Vector2 direction = Mode switch
                {
                    QlippothThrowDirection.AwayFromSelf => position - selfPosition,
                    QlippothThrowDirection.TowardSelf => selfPosition - position,
                    QlippothThrowDirection.AwayFromTarget => position - targetPosition,
                    QlippothThrowDirection.TowardTarget => targetPosition - position,
                    QlippothThrowDirection.Fixed => Direction,
                    _ => resultSystem.Random.NextAngle().ToVec(),
                };
                if (direction.LengthSquared() < 0.001f)
                    direction = resultSystem.Random.NextAngle().ToVec();

                resultSystem.Throwing.TryThrow(target, Vector2.Normalize(direction) * Distance, Speed, uid, recoil: false, playSound: false);
                any = true;
            }
            return any;
        }
    }

    /// <summary>Physics push without the "thrown" state (targets stay in control): a shove.</summary>
    [DataDefinition]
    public sealed partial class PushResult : ExternalMovementResult
    {
        [DataField]
        public QlippothThrowDirection Mode { get; set; } = QlippothThrowDirection.AwayFromSelf;

        [DataField]
        public float Force { get; set; } = 50f;

        [DataField]
        public Vector2 Direction { get; set; } = Vector2.UnitY;

        [DataField]
        public QlippothTargeting Targeting { get; set; } = new();

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var selfPosition = resultSystem.QlippothTransform.GetWorldPosition(uid);
            var initiationTarget = resultSystem.ResolveTarget(uid, eventArgs);
            var targetPosition = resultSystem.QlippothTransform.GetWorldPosition(initiationTarget);

            var any = false;
            foreach (var target in resultSystem.ResolveTargets(uid, eventArgs, Targeting))
            {
                if (!resultSystem.QlippothEntityManager.HasComponent<Robust.Shared.Physics.Components.PhysicsComponent>(target))
                    continue;
                var position = resultSystem.QlippothTransform.GetWorldPosition(target);
                Vector2 direction = Mode switch
                {
                    QlippothThrowDirection.AwayFromSelf => position - selfPosition,
                    QlippothThrowDirection.TowardSelf => selfPosition - position,
                    QlippothThrowDirection.AwayFromTarget => position - targetPosition,
                    QlippothThrowDirection.TowardTarget => targetPosition - position,
                    QlippothThrowDirection.Fixed => Direction,
                    _ => resultSystem.Random.NextAngle().ToVec(),
                };
                if (direction.LengthSquared() < 0.001f)
                    direction = resultSystem.Random.NextAngle().ToVec();

                resultSystem.Physics.ApplyLinearImpulse(target, Vector2.Normalize(direction) * Force);
                any = true;
            }
            return any;
        }
    }

    /// <summary>Anchor or unanchor targets (default: the Qlippoth itself: root in place / break free).</summary>
    [DataDefinition]
    public sealed partial class SetAnchoredResult : ExternalMovementResult
    {
        [DataField]
        public bool Anchored { get; set; } = true;

        [DataField]
        public QlippothTargeting Targeting { get; set; } = QlippothTargeting.SelfOnly();

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var any = false;
            foreach (var target in resultSystem.ResolveTargets(uid, eventArgs, Targeting))
            {
                if (Anchored)
                    resultSystem.QlippothTransform.AnchorEntity(target);
                else
                    resultSystem.QlippothTransform.Unanchor(target);
                any = true;
            }
            return any;
        }
    }

    /// <summary>Turn targets to face a direction / the Qlippoth (default: the Qlippoth faces the initiation's target).</summary>
    [DataDefinition]
    public sealed partial class FaceResult : ExternalMovementResult
    {
        [DataField]
        public QlippothTargeting Targeting { get; set; } = QlippothTargeting.SelfOnly();

        /// <summary>What to face. Default: the initiation's target.</summary>
        [DataField]
        public QlippothTargeting FaceTarget { get; set; } = new();

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var face = resultSystem.ResolveTargets(uid, eventArgs, FaceTarget).FirstOrDefault();
            if (face == default)
                return false;
            var facePosition = resultSystem.QlippothTransform.GetWorldPosition(face);

            var any = false;
            foreach (var target in resultSystem.ResolveTargets(uid, eventArgs, Targeting))
            {
                if (target == face)
                    continue;
                var position = resultSystem.QlippothTransform.GetWorldPosition(target);
                var direction = facePosition - position;
                if (direction.LengthSquared() < 0.001f)
                    continue;
                resultSystem.QlippothTransform.SetWorldRotation(target, direction.ToWorldAngle());
                any = true;
            }
            return any;
        }
    }
    #endregion

    #region Reaction Result Implementations
    // -------------------------------------------------------------------------
    // Reaction results: chemistry and atmospherics in the surroundings or in a container.
    // ReleaseGasResult (gas onto the tile) is in the original set region above.
    // -------------------------------------------------------------------------

    /// <summary>Heat or cool the air on the target's tile (default: the Qlippoth's tile). delta adds; absolute sets.</summary>
    [DataDefinition]
    public sealed partial class HeatTileResult : ReactionResult
    {
        /// <summary>Kelvin added (negative cools).</summary>
        [DataField]
        public float Delta { get; set; } = 0f;

        /// <summary>Kelvin to set. Overrides delta when set.</summary>
        [DataField]
        public float? Absolute { get; set; }

        /// <summary>Also affect the tiles around, this many tiles out.</summary>
        [DataField]
        public int Radius { get; set; } = 0;

        [DataField]
        public QlippothTargeting Targeting { get; set; } = QlippothTargeting.SelfOnly();

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var any = false;
            foreach (var target in resultSystem.ResolveTargets(uid, eventArgs, Targeting))
            {
                foreach (var mixture in resultSystem.TileMixturesAround(target, Radius))
                {
                    mixture.Temperature = Absolute ?? Math.Max(Atmospherics.TCMB, mixture.Temperature + Delta);
                    any = true;
                }
            }
            return any;
        }
    }

    /// <summary>Expose the target's tile to a hotspot: ignites flammable gas there (plasma leak + this = fire).</summary>
    [DataDefinition]
    public sealed partial class HotspotResult : ReactionResult
    {
        [DataField]
        public float Temperature { get; set; } = 1000f;

        [DataField]
        public float Volume { get; set; } = 100f;

        [DataField]
        public QlippothTargeting Targeting { get; set; } = QlippothTargeting.SelfOnly();

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var any = false;
            foreach (var target in resultSystem.ResolveTargets(uid, eventArgs, Targeting))
                any |= resultSystem.ExposeHotspot(target, Temperature, Volume);
            return any;
        }
    }

    /// <summary>Remove gas from the target's tile. gas null = everything (a small vacuum).</summary>
    [DataDefinition]
    public sealed partial class RemoveGasResult : ReactionResult
    {
        [DataField]
        public Gas? Gas { get; set; }

        /// <summary>Moles to remove; ignored when gas is null (removes all). float.MaxValue = all of that gas.</summary>
        [DataField]
        public float Moles { get; set; } = float.MaxValue;

        [DataField]
        public int Radius { get; set; } = 0;

        [DataField]
        public QlippothTargeting Targeting { get; set; } = QlippothTargeting.SelfOnly();

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var any = false;
            foreach (var target in resultSystem.ResolveTargets(uid, eventArgs, Targeting))
            {
                foreach (var mixture in resultSystem.TileMixturesAround(target, Radius))
                {
                    if (Gas == null)
                        mixture.Clear();
                    else
                        mixture.AdjustMoles(Gas.Value, -Math.Min(Moles, mixture.GetMoles(Gas.Value)));
                    any = true;
                }
            }
            return any;
        }
    }

    /// <summary>Spill a reagent puddle at the target's feet (default: under the Qlippoth). Blood, acid, lube...</summary>
    [DataDefinition]
    public sealed partial class SpillReagentResult : ReactionResult
    {
        [DataField(required: true)]
        public string Reagent { get; set; } = string.Empty;

        [DataField]
        public float Amount { get; set; } = 20f;

        [DataField]
        public QlippothTargeting Targeting { get; set; } = QlippothTargeting.SelfOnly();

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var any = false;
            foreach (var target in resultSystem.ResolveTargets(uid, eventArgs, Targeting))
            {
                var coordinates = resultSystem.QlippothEntityManager.GetComponent<TransformComponent>(target).Coordinates;
                any |= resultSystem.Puddle.TrySpillAt(coordinates, new Solution(Reagent, Amount), out _);
            }
            return any;
        }
    }

    /// <summary>Release a chemical smoke (or foam) cloud carrying a reagent from the target's tile (default: the Qlippoth's).</summary>
    [DataDefinition]
    public sealed partial class SmokeResult : ReactionResult
    {
        /// <summary>Reagent inside the cloud. Null = plain smoke.</summary>
        [DataField]
        public string? Reagent { get; set; }

        [DataField]
        public float Amount { get; set; } = 10f;

        /// <summary>How many tiles the cloud spreads.</summary>
        [DataField]
        public int Spread { get; set; } = 5;

        [DataField]
        public float Duration { get; set; } = 10f;

        /// <summary>Use the Foam entity instead of Smoke.</summary>
        [DataField]
        public bool Foam { get; set; }

        [DataField]
        public QlippothTargeting Targeting { get; set; } = QlippothTargeting.SelfOnly();

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var any = false;
            foreach (var target in resultSystem.ResolveTargets(uid, eventArgs, Targeting))
            {
                var coordinates = resultSystem.QlippothEntityManager.GetComponent<TransformComponent>(target).Coordinates;
                var solution = Reagent != null ? new Solution(Reagent, Amount) : new Solution();
                any |= resultSystem.StartSmoke(coordinates, solution, Duration, Spread, Foam);
            }
            return any;
        }
    }

    /// <summary>
    /// Add a reagent to a container. If the target is a mob it goes into its bloodstream; otherwise into the named solution
    /// (solution null = the first solution the target has: a beaker's "beaker", a drink's "drink", a Qlippoth's own built-in container).
    /// Default targeting: the Qlippoth itself, so a Qlippoth can brew inside its own SolutionContainer.
    /// </summary>
    [DataDefinition]
    public sealed partial class AddReagentResult : ReactionResult
    {
        [DataField(required: true)]
        public string Reagent { get; set; } = string.Empty;

        [DataField]
        public float Amount { get; set; } = 5f;

        [DataField]
        public string? Solution { get; set; }

        [DataField]
        public QlippothTargeting Targeting { get; set; } = QlippothTargeting.SelfOnly();

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var any = false;
            foreach (var target in resultSystem.ResolveTargets(uid, eventArgs, Targeting))
                any |= resultSystem.AddReagent(target, Solution, new Solution(Reagent, Amount));
            return any;
        }
    }

    /// <summary>Empty a container's solution (default: the Qlippoth's own).</summary>
    [DataDefinition]
    public sealed partial class EmptySolutionResult : ReactionResult
    {
        [DataField]
        public string? Solution { get; set; }

        [DataField]
        public QlippothTargeting Targeting { get; set; } = QlippothTargeting.SelfOnly();

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var any = false;
            foreach (var target in resultSystem.ResolveTargets(uid, eventArgs, Targeting))
                any |= resultSystem.EmptySolution(target, Solution);
            return any;
        }
    }

    /// <summary>Set the temperature of a container's solution (boil a beaker, freeze a drink).</summary>
    [DataDefinition]
    public sealed partial class HeatSolutionResult : ReactionResult
    {
        [DataField]
        public float Temperature { get; set; } = 400f;

        [DataField]
        public string? Solution { get; set; }

        [DataField]
        public QlippothTargeting Targeting { get; set; } = QlippothTargeting.SelfOnly();

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var any = false;
            foreach (var target in resultSystem.ResolveTargets(uid, eventArgs, Targeting))
                any |= resultSystem.HeatSolution(target, Solution, Temperature);
            return any;
        }
    }

    /// <summary>Pour the Qlippoth's own solution (or the target's) onto the floor as a puddle.</summary>
    [DataDefinition]
    public sealed partial class SpillSolutionResult : ReactionResult
    {
        [DataField]
        public string? Solution { get; set; }

        [DataField]
        public QlippothTargeting Targeting { get; set; } = QlippothTargeting.SelfOnly();

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var any = false;
            foreach (var target in resultSystem.ResolveTargets(uid, eventArgs, Targeting))
                any |= resultSystem.SpillSolution(target, Solution);
            return any;
        }
    }
    #endregion

    #region Conversion Result Implementations
    // -------------------------------------------------------------------------
    // Conversion results: existing objects / mobs / tiles become something else, usually a Qlippoth variant.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Replace targets with another prototype at the same spot. Either one prototype for everything, or a map from
    /// source prototype id to replacement (WallSolid: WallFlesh, AirlockCommand: QlippothDoorFlesh...) with unmapped targets left alone.
    /// targeting Self = the Qlippoth transforms into its next form.
    /// </summary>
    [DataDefinition]
    public sealed partial class ReplaceEntityResult : ConversionResult
    {
        [DataField]
        public string? Prototype { get; set; }

        [DataField]
        public Dictionary<string, string> Map { get; set; } = new();

        /// <summary>Move a controlling player's mind into the replacement.</summary>
        [DataField]
        public bool TransferMind { get; set; } = true;

        /// <summary>Keep the old entity's name.</summary>
        [DataField]
        public bool KeepName { get; set; }

        /// <summary>Copy the Qlippoth state dictionary onto the replacement if both have QlippothActions.</summary>
        [DataField]
        public bool CopyState { get; set; } = true;

        /// <summary>Chance per target.</summary>
        [DataField]
        public float Chance { get; set; } = 1f;

        [DataField]
        public QlippothTargeting Targeting { get; set; } = new();

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var any = false;
            foreach (var target in resultSystem.ResolveTargets(uid, eventArgs, Targeting).ToArray())
            {
                if (Chance < 1f && !resultSystem.Random.Prob(Chance))
                    continue;

                var replacement = Prototype;
                if (Map.Count > 0)
                {
                    var id = resultSystem.PrototypeIdOf(target);
                    if (id == null || !Map.TryGetValue(id, out replacement))
                        continue;
                }
                if (string.IsNullOrEmpty(replacement))
                    continue;

                any |= resultSystem.ReplaceEntity(target, replacement, TransferMind, KeepName, CopyState) != null;
            }
            return any;
        }
    }

    /// <summary>Polymorph targets with a PolymorphPrototype (Mouse, Monkey, Chicken...) or an inline configuration. Reverts by itself if the prototype says so.</summary>
    [DataDefinition]
    public sealed partial class PolymorphResult : ConversionResult
    {
        [DataField]
        public ProtoId<PolymorphPrototype>? Polymorph { get; set; }

        /// <summary>Inline configuration used when polymorph is null: entity, duration, forced, revertOnDeath...</summary>
        [DataField]
        public PolymorphConfiguration? Configuration { get; set; }

        [DataField]
        public QlippothTargeting Targeting { get; set; } = new();

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var any = false;
            foreach (var target in resultSystem.ResolveTargets(uid, eventArgs, Targeting).ToArray())
            {
                EntityUid? result = null;
                if (Polymorph != null)
                    result = resultSystem.Polymorph.PolymorphEntity(target, Polymorph.Value);
                else if (Configuration != null)
                    result = resultSystem.Polymorph.PolymorphEntity(target, Configuration);
                any |= result != null;
            }
            return any;
        }
    }

    /// <summary>Undo a polymorph on the targets.</summary>
    [DataDefinition]
    public sealed partial class RevertPolymorphResult : ConversionResult
    {
        [DataField]
        public QlippothTargeting Targeting { get; set; } = new();

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var any = false;
            foreach (var target in resultSystem.ResolveTargets(uid, eventArgs, Targeting).ToArray())
            {
                if (!resultSystem.QlippothEntityManager.HasComponent<Content.Server.Polymorph.Components.PolymorphedEntityComponent>(target))
                    continue;
                any |= resultSystem.Polymorph.Revert(target) != null;
            }
            return any;
        }
    }

    /// <summary>
    /// Replace floor tiles around the target (default: around the Qlippoth) with another tile (FloorFlesh, FloorBasalt, Lattice...).
    /// Chance per tile. This is how a Qlippoth "grows" its domain.
    /// </summary>
    [DataDefinition]
    public sealed partial class ConvertTilesResult : ConversionResult
    {
        [DataField(required: true)]
        public string Tile { get; set; } = string.Empty;

        [DataField]
        public int Radius { get; set; } = 1;

        [DataField]
        public float Chance { get; set; } = 1f;

        /// <summary>Only convert tiles whose current id is in this list. Empty = any floor.</summary>
        [DataField]
        public List<string> From { get; set; } = new();

        [DataField]
        public QlippothTargeting Targeting { get; set; } = QlippothTargeting.SelfOnly();

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var any = false;
            foreach (var target in resultSystem.ResolveTargets(uid, eventArgs, Targeting))
                any |= resultSystem.ConvertTiles(target, Tile, Radius, Chance, From) > 0;
            return any;
        }
    }

    /// <summary>
    /// Add components to the targets from YAML. The real "make it a Qlippoth": give a crew member QlippothActions with its own action list,
    /// give a wall Qlippoth + QlippothPresence, give an item a Sanity aura...
    /// YAML:
    ///   components:
    ///     - type: QlippothActions
    ///       actions: [...]
    /// </summary>
    [DataDefinition]
    public sealed partial class AddComponentsResult : ConversionResult
    {
        [DataField(required: true)]
        public ComponentRegistry Components { get; set; } = new();

        /// <summary>Replace components the target already has.</summary>
        [DataField]
        public bool Overwrite { get; set; } = false;

        [DataField]
        public QlippothTargeting Targeting { get; set; } = new();

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var any = false;
            foreach (var target in resultSystem.ResolveTargets(uid, eventArgs, Targeting).ToArray())
            {
                resultSystem.QlippothEntityManager.AddComponents(target, Components, Overwrite);
                any = true;
            }
            return any;
        }
    }

    /// <summary>Remove components (YAML names) from the targets: strip Sanity, Hands, Pullable...</summary>
    [DataDefinition]
    public sealed partial class RemoveComponentsResult : ConversionResult
    {
        [DataField(required: true)]
        public List<string> Components { get; set; } = new();

        [DataField]
        public QlippothTargeting Targeting { get; set; } = new();

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var any = false;
            foreach (var target in resultSystem.ResolveTargets(uid, eventArgs, Targeting).ToArray())
            {
                foreach (var name in Components)
                    any |= resultSystem.RemoveComponentNamed(target, name);
            }
            return any;
        }
    }

    public enum QlippothMindTransfer : byte
    {
        /// <summary>The Qlippoth's controlling player moves into the target (possession).</summary>
        SelfToTarget,
        /// <summary>The target's player moves into the Qlippoth (absorption).</summary>
        TargetToSelf,
        /// <summary>Swap the two players.</summary>
        Swap,
    }

    /// <summary>Move player minds between the Qlippoth and the target.</summary>
    [DataDefinition]
    public sealed partial class TransferMindResult : ConversionResult
    {
        [DataField]
        public QlippothMindTransfer Mode { get; set; } = QlippothMindTransfer.SelfToTarget;

        [DataField]
        public QlippothTargeting Targeting { get; set; } = new();

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var target = resultSystem.ResolveTargets(uid, eventArgs, Targeting).FirstOrDefault(t => t != uid);
            if (target == default)
                return false;
            return resultSystem.TransferMinds(uid, target, Mode);
        }
    }

    /// <summary>
    /// Convert the target mob into a servant: it is offered as a ghost role (if nobody controls it) and marked as this Qlippoth's offspring.
    /// Pair with AddComponentsResult to also give it powers.
    /// </summary>
    [DataDefinition]
    public sealed partial class EnthrallResult : ConversionResult
    {
        [DataField]
        public string RoleName { get; set; } = "qlippoth-ghost-role-servant-name";

        [DataField]
        public string RoleDescription { get; set; } = "qlippoth-ghost-role-servant-description";

        [DataField]
        public string RoleRules { get; set; } = "qlippoth-ghost-role-servant-rules";

        /// <summary>Only offer the ghost role if the mob has no player.</summary>
        [DataField]
        public bool OnlyIfMindless { get; set; } = true;

        [DataField]
        public QlippothTargeting Targeting { get; set; } = new();

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var any = false;
            foreach (var target in resultSystem.ResolveTargets(uid, eventArgs, Targeting))
            {
                if (target == uid)
                    continue;
                resultSystem.TrackOffspring(uid, target);
                if (!OnlyIfMindless || !resultSystem.HasPlayer(target))
                    resultSystem.MakeGhostRole(target, RoleName, RoleDescription, RoleRules);
                any = true;
            }
            return any;
        }
    }
    #endregion

    #region Event Result Implementations
    // -------------------------------------------------------------------------
    // Event results: start / end round-level events, talk to the station, talk to other Qlippoths.
    // -------------------------------------------------------------------------

    /// <summary>Start a game rule / station event by prototype id (MeteorSwarm, PowerGridCheck, SolarFlare, KudzuGrowth...).</summary>
    [DataDefinition]
    public sealed partial class StartGameRuleResult : EventResult
    {
        [DataField(required: true)]
        public string RuleId { get; set; } = string.Empty;

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            return resultSystem.GameTicker.StartGameRule(RuleId, out _);
        }
    }

    /// <summary>End every active game rule with this prototype id.</summary>
    [DataDefinition]
    public sealed partial class EndGameRuleResult : EventResult
    {
        [DataField(required: true)]
        public string RuleId { get; set; } = string.Empty;

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            return resultSystem.EndGameRules(RuleId) > 0;
        }
    }

    /// <summary>Station-wide announcement. Message/sender are locale keys or raw text; may use {$self} and {$target}.</summary>
    [DataDefinition]
    public sealed partial class AnnounceResult : EventResult
    {
        [DataField(required: true)]
        public string Message { get; set; } = string.Empty;

        [DataField]
        public string Sender { get; set; } = "qlippoth-announcement-sender";

        [DataField]
        public Color Color { get; set; } = Color.FromHex("#9B30FF");

        [DataField]
        public bool PlaySound { get; set; } = true;

        [DataField]
        public string? Sound { get; set; }

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var target = resultSystem.ResolveTarget(uid, eventArgs);
            var text = Loc.GetString(Message, ("self", resultSystem.EntityName(uid)), ("target", resultSystem.EntityName(target)));
            var sound = Sound != null ? new SoundPathSpecifier(Sound) : null;
            resultSystem.Chat.DispatchGlobalAnnouncement(text, Loc.GetString(Sender), PlaySound, sound, Color);
            return true;
        }
    }

    /// <summary>Set the station alert level (green, blue, red, delta, gamma, epsilon...).</summary>
    [DataDefinition]
    public sealed partial class AlertLevelResult : EventResult
    {
        [DataField(required: true)]
        public string Level { get; set; } = "red";

        [DataField]
        public bool Announce { get; set; } = true;

        [DataField]
        public bool Force { get; set; } = true;

        [DataField]
        public bool Locked { get; set; } = false;

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            return resultSystem.SetAlertLevel(uid, Level, Announce, Force, Locked);
        }
    }

    /// <summary>Call (or recall) the emergency shuttle.</summary>
    [DataDefinition]
    public sealed partial class CallShuttleResult : EventResult
    {
        [DataField]
        public bool Recall { get; set; }

        /// <summary>Countdown in seconds for a call. Null = server default.</summary>
        [DataField]
        public float? Countdown { get; set; }

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            if (Recall)
            {
                if (!resultSystem.RoundEnd.IsRoundEndRequested())
                    return false;
                resultSystem.RoundEnd.CancelRoundEndCountdown(uid, forceRecall: true);
                return true;
            }

            if (resultSystem.RoundEnd.IsRoundEndRequested())
                return false;
            if (Countdown != null)
                resultSystem.RoundEnd.RequestRoundEnd(TimeSpan.FromSeconds(Countdown.Value), uid, checkCooldown: false);
            else
                resultSystem.RoundEnd.RequestRoundEnd(uid, checkCooldown: false);
            return true;
        }
    }

    /// <summary>
    /// Broadcast a named signal to other Qlippoths (OnSignalInitiation { signal: ... }). range 0 = every Qlippoth on the same map; global = everywhere.
    /// </summary>
    [DataDefinition]
    public sealed partial class SignalResult : EventResult
    {
        [DataField(required: true)]
        public string Signal { get; set; } = string.Empty;

        [DataField]
        public float Range { get; set; } = 0f;

        [DataField]
        public bool Global { get; set; }

        [DataField]
        public bool IncludeSelf { get; set; }

        [DataField]
        public QlippothTargetFilter Filter { get; set; } = new();

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            return resultSystem.BroadcastSignal(uid, Signal, Range, Global, IncludeSelf, Filter) > 0;
        }
    }

    /// <summary>Breach every Q-Gate on the station now (the Qlippoth calls its kin through).</summary>
    [DataDefinition]
    public sealed partial class BreachGatesResult : EventResult
    {
        /// <summary>Only the nearest gate instead of all of them.</summary>
        [DataField]
        public bool NearestOnly { get; set; } = true;

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            return resultSystem.BreachGates(uid, NearestOnly) > 0;
        }
    }
    #endregion

    #region Flow Result Implementations
    // -------------------------------------------------------------------------
    // Flow results: control the result chain itself. A result returning false stops the rest of the chain
    // (unless the action sets continueOnFailure), which is what these use as their "gate".
    // -------------------------------------------------------------------------

    /// <summary>Continue the chain only with this probability. ChanceResult { chance: 0.25 } before an ExplosionResult = 25% explosion.</summary>
    [DataDefinition]
    public sealed partial class ChanceResult : FlowResult
    {
        [DataField(required: true)]
        public float Chance { get; set; } = 0.5f;

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            return resultSystem.Random.Prob(Chance);
        }
    }

    /// <summary>Continue only if the initiation's target (or the chosen targeting) passes the filter. Requires at least one match.</summary>
    [DataDefinition]
    public sealed partial class RequireTargetResult : FlowResult
    {
        [DataField]
        public QlippothTargetFilter Filter { get; set; } = new();

        [DataField]
        public QlippothTargeting Targeting { get; set; } = new();

        /// <summary>Invert: continue only if NO target passes.</summary>
        [DataField]
        public bool Not { get; set; }

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var any = resultSystem.ResolveTargets(uid, eventArgs, Targeting).Any(t => resultSystem.PassesFilter(uid, t, Filter));
            return any != Not;
        }
    }

    /// <summary>Continue only while the Qlippoth's state matches (a mid-chain requireState).</summary>
    [DataDefinition]
    public sealed partial class RequireStateResult : FlowResult
    {
        [DataField(required: true)]
        public string Key { get; set; } = string.Empty;

        [DataField]
        public string? Value { get; set; }

        /// <summary>For numeric states: continue only if the value is at least this.</summary>
        [DataField]
        public int? Min { get; set; }

        [DataField]
        public int? Max { get; set; }

        [DataField]
        public bool Not { get; set; }

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var current = resultSystem.GetState(uid, Key);
            var ok = true;
            if (Value != null && current != Value)
                ok = false;
            if (Min != null || Max != null)
            {
                int.TryParse(current, out var number);
                if (Min != null && number < Min.Value)
                    ok = false;
                if (Max != null && number > Max.Value)
                    ok = false;
            }
            if (Value == null && Min == null && Max == null)
                ok = current != null;
            return ok != Not;
        }
    }

    /// <summary>Continue only if the Qlippoth is (or is not) held, contained, anchored, in a container...</summary>
    [DataDefinition]
    public sealed partial class RequireSituationResult : FlowResult
    {
        [DataField]
        public bool? Held { get; set; }

        [DataField]
        public bool? Contained { get; set; }

        [DataField]
        public bool? Anchored { get; set; }

        [DataField]
        public bool? InContainer { get; set; }

        [DataField]
        public bool? HasPlayer { get; set; }

        /// <summary>Total damage at least this.</summary>
        [DataField]
        public float? MinDamage { get; set; }

        [DataField]
        public float? MaxDamage { get; set; }

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            return resultSystem.CheckSituation(uid, Held, Contained, Anchored, InContainer, HasPlayer, MinDamage, MaxDamage);
        }
    }

    /// <summary>Run one of the listed results, picked by weight. Each entry: { weight: 2, result: !type:... }.</summary>
    [DataDefinition]
    public sealed partial class RandomResult : FlowResult
    {
        [DataField(required: true)]
        public List<QlippothWeightedResult> Options { get; set; } = new();

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var total = Options.Sum(o => Math.Max(0f, o.Weight));
            if (total <= 0f)
                return false;

            var roll = resultSystem.Random.NextFloat() * total;
            foreach (var option in Options)
            {
                roll -= Math.Max(0f, option.Weight);
                if (roll <= 0f)
                    return option.Result.Execute(uid, resultSystem, eventArgs);
            }
            return Options[^1].Result.Execute(uid, resultSystem, eventArgs);
        }
    }

    [DataDefinition]
    public sealed partial class QlippothWeightedResult
    {
        [DataField]
        public float Weight { get; set; } = 1f;

        [DataField(required: true)]
        public QlippothResult Result { get; set; } = default!;
    }

    /// <summary>Run a sub-list of results as a block. With continueOnFailure the block keeps going after a failed result; the block itself never fails.</summary>
    [DataDefinition]
    public sealed partial class GroupResult : FlowResult
    {
        [DataField(required: true)]
        public List<QlippothResult> Results { get; set; } = new();

        [DataField]
        public bool ContinueOnFailure { get; set; } = true;

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            foreach (var result in Results)
            {
                if (!result.Execute(uid, resultSystem, eventArgs) && !ContinueOnFailure)
                    break;
            }
            return true;
        }
    }

    /// <summary>Run the sub-list once per resolved target, with that target as the initiation target (a per-victim chain inside an aura action).</summary>
    [DataDefinition]
    public sealed partial class ForEachTargetResult : FlowResult
    {
        [DataField]
        public QlippothTargeting Targeting { get; set; } = new() { Mode = QlippothTargetMode.InRange };

        [DataField(required: true)]
        public List<QlippothResult> Results { get; set; } = new();

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var any = false;
            foreach (var target in resultSystem.ResolveTargets(uid, eventArgs, Targeting).ToArray())
            {
                var inner = new QlippothTargetEventArgs(target);
                foreach (var result in Results)
                {
                    if (!result.Execute(uid, resultSystem, inner))
                        break;
                }
                any = true;
            }
            return any;
        }
    }

    /// <summary>Repeat the sub-list N times (with an optional delay between repeats via DelayResult inside).</summary>
    [DataDefinition]
    public sealed partial class RepeatResult : FlowResult
    {
        [DataField]
        public int Times { get; set; } = 3;

        [DataField(required: true)]
        public List<QlippothResult> Results { get; set; } = new();

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            for (var i = 0; i < Times; i++)
            {
                foreach (var result in Results)
                {
                    if (!result.Execute(uid, resultSystem, eventArgs))
                        break;
                }
            }
            return true;
        }
    }

    /// <summary>
    /// Fire another action of this Qlippoth right now: the one whose initiation is OnChainInitiation { key }.
    /// The original eventArgs are forwarded, so the chained action still knows the target.
    /// </summary>
    [DataDefinition]
    public sealed partial class ChainResult : FlowResult
    {
        [DataField(required: true)]
        public string Key { get; set; } = string.Empty;

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            return resultSystem.Chain(uid, Key, eventArgs) > 0;
        }
    }

    /// <summary>
    /// Fire the OnChainInitiation { key } action after Delay seconds. This is how "N seconds after X" behaviours are built
    /// (pickup -> DelayResult 30 -> the curse takes hold). Deleted Qlippoths drop their pending delays.
    /// </summary>
    [DataDefinition]
    public sealed partial class DelayResult : FlowResult
    {
        [DataField(required: true)]
        public string Key { get; set; } = string.Empty;

        [DataField]
        public float Delay { get; set; } = 5f;

        /// <summary>Random +/- seconds.</summary>
        [DataField]
        public float Variance { get; set; } = 0f;

        /// <summary>If a delay with this key is already pending, restart it instead of queueing another.</summary>
        [DataField]
        public bool Refresh { get; set; } = true;

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var delay = Delay + (Variance > 0f ? resultSystem.Random.NextFloat(-Variance, Variance) : 0f);
            resultSystem.ScheduleChain(uid, Key, Math.Max(0f, delay), eventArgs, Refresh);
            return true;
        }
    }

    /// <summary>Cancel pending DelayResults with this key (the curse was lifted in time).</summary>
    [DataDefinition]
    public sealed partial class CancelDelayResult : FlowResult
    {
        [DataField(required: true)]
        public string Key { get; set; } = string.Empty;

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            return resultSystem.CancelChain(uid, Key) > 0;
        }
    }

    /// <summary>Reset the cooldown of another action (by actionName) so it can fire again immediately. Null = all actions.</summary>
    [DataDefinition]
    public sealed partial class ResetCooldownResult : FlowResult
    {
        [DataField]
        public string? ActionName { get; set; }

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            return resultSystem.ResetCooldowns(uid, ActionName) > 0;
        }
    }

    /// <summary>Always fails: stops the rest of the chain. Useful at the end of a RandomResult branch or after a RequireStateResult.</summary>
    [DataDefinition]
    public sealed partial class StopResult : FlowResult
    {
        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null) => false;
    }

    /// <summary>Write a line to the server log (sawmill "qlippoth"). For debugging action chains.</summary>
    [DataDefinition]
    public sealed partial class LogResult : FlowResult
    {
        [DataField(required: true)]
        public string Message { get; set; } = string.Empty;

        public override bool Execute(EntityUid uid, QlippothActionResultSystem resultSystem, object? eventArgs = null)
        {
            var target = resultSystem.ResolveTarget(uid, eventArgs);
            resultSystem.Sawmill.Info($"[{resultSystem.EntityName(uid)} {uid}] {Message} (target: {resultSystem.EntityName(target)} {target}, args: {QlippothUtil.Unwrap(eventArgs)?.GetType().Name ?? "none"})");
            return true;
        }
    }
    #endregion
}
