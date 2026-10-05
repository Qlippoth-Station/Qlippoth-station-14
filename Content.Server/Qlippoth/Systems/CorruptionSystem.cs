using Content.Server.Popups;
using Content.Shared.Examine;
using Content.Shared.Qlippoth.Components;
using Robust.Shared.Timing;
using Robust.Shared.Random;

namespace Content.Server.Qlippoth.Systems;

/// <summary>
/// Owns corruption duration, exposure, containment mitigation, pulses, spread, and recovery events.
/// The effect is stored on the exposed entity, so rift evacuation preserves its remaining duration.
/// </summary>
public sealed partial class CorruptionSystem : EntitySystem
{
    private const float MaxDuration = 300f;
    private const int MaxSeverity = 5;
    private const float MaxSanityDrain = 10f;
    private const float MaxSpreadChance = 0.5f;
    private const float MaxSpreadRadius = 8f;
    private static readonly TimeSpan MitigationNoticeCooldown = TimeSpan.FromSeconds(15);

    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SanitySystem _sanity = default!;
    [Dependency] private QlippothActionInitiationSystem _initiation = default!;
    [Dependency] private ContainmentDimensionSystem _containment = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private readonly Dictionary<EntityUid, TimeSpan> _nextMitigationNotice = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<QlippothCorruptionComponent, ExaminedEvent>(OnCorruptionExamined);
        SubscribeLocalEvent<EntityTerminatingEvent>(OnEntityTerminating);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<QlippothCorruptionComponent>();
        while (query.MoveNext(out var uid, out var corruption))
        {
            if (corruption.IsRemoving)
                continue;

            if (corruption.SourceQlippoth is { } source && !Exists(source))
            {
                corruption.SourceQlippoth = null;
                Dirty(uid, corruption);
                RaiseLocalEvent(uid, new QlippothCorruptionSourceLostEvent(uid, source), broadcast: true);
                _popup.PopupEntity(Loc.GetString("qlippoth-corruption-source-lost"), uid, uid);
            }

            if (TryComp<TransformComponent>(uid, out var targetTransform) &&
                targetTransform.MapID != corruption.AppliedMapId)
            {
                var previousMap = corruption.AppliedMapId;
                corruption.AppliedMapId = targetTransform.MapID;
                Dirty(uid, corruption);
                RaiseLocalEvent(uid, new QlippothCorruptionTransferredEvent(
                    uid, previousMap, targetTransform.MapID, corruption.RemainingSeconds), broadcast: true);
            }

            corruption.RemainingSeconds -= frameTime;
            if (corruption.RemainingSeconds <= 0f)
            {
                RemoveCorruption(uid, QlippothCorruptionRemovalReason.Expired);
                continue;
            }

            if (corruption.SanityDrainPerSecond > 0f)
                _sanity.DamageSanity(uid, corruption.SanityDrainPerSecond * corruption.Severity * frameTime);

            if (corruption.PulseInterval > 0f)
            {
                corruption.PulseTimeRemaining -= frameTime;
                if (corruption.PulseTimeRemaining <= 0f)
                {
                    corruption.PulseTimeRemaining = corruption.PulseInterval;
                    TrySpreadCorruption(uid, corruption);
                    RaiseLocalEvent(uid, new QlippothCorruptionPulseEvent(
                        uid, corruption.SourceQlippoth, corruption.Severity), broadcast: true);
                    _popup.PopupEntity(Loc.GetString("qlippoth-corruption-pulse"), uid, uid);

                    if (corruption.SourceQlippoth is { } pulseSource &&
                        Exists(pulseSource) && HasComp<QlippothActionsComponent>(pulseSource))
                    {
                        _initiation.Dispatch<OnCorruptionPulseInitiation>(pulseSource,
                            new QlippothCorruptionEventArgs(uid, corruption.Severity));
                    }

                    if (corruption.IsRemoving)
                        continue;
                }
            }

            Dirty(uid, corruption);
        }
    }

    /// <summary>
    /// Applies the source's bounded profile. Re-exposure is rejected until this instance expires
    /// or is treated; an intact chamber can prevent exposure across its containment boundary.
    /// </summary>
    public bool ApplyCorruption(EntityUid uid, EntityUid? sourceQlippoth, CorruptionProfile profile, bool isSpread = false)
    {
        if (!TryComp<SanityComponent>(uid, out _) || HasComp<QlippothCorruptionComponent>(uid) ||
            !TryComp<TransformComponent>(uid, out var targetTransform))
            return false;

        if (_containment.IsCorruptionProtected(uid, sourceQlippoth, out var chamber))
        {
            NotifyContainmentMitigation(uid, sourceQlippoth, chamber);
            return false;
        }

        var duration = ClampFinite(profile.Duration, 0.1f, MaxDuration, 60f);
        var severity = Math.Clamp(profile.Severity, 1, MaxSeverity);
        var drain = ClampFinite(profile.SanityDrainPerSecond, 0f, MaxSanityDrain, 0f);
        var pulseInterval = ClampFinite(profile.PulseInterval, 0f, 60f, 0f);
        if (pulseInterval > 0f)
            pulseInterval = Math.Max(5f, pulseInterval);
        var spreadChance = ClampFinite(profile.SpreadChance, 0f, MaxSpreadChance, 0f);
        var spreadRadius = ClampFinite(profile.SpreadRadius, 0f, MaxSpreadRadius, 0f);

        var corruption = EnsureComp<QlippothCorruptionComponent>(uid);
        corruption.RemainingSeconds = duration;
        corruption.Severity = severity;
        corruption.SanityDrainPerSecond = drain;
        corruption.PulseInterval = pulseInterval;
        corruption.PulseTimeRemaining = pulseInterval;
        corruption.SpreadChance = spreadChance;
        corruption.SpreadRadius = spreadRadius;
        corruption.SourceQlippoth = sourceQlippoth is { } source && Exists(source) ? source : null;
        corruption.AppliedMapId = targetTransform.MapID;
        corruption.IsRemoving = false;
        Dirty(uid, corruption);

        RaiseLocalEvent(uid, new QlippothCorruptionAppliedEvent(
            uid, corruption.SourceQlippoth, severity, isSpread), broadcast: true);
        _popup.PopupEntity(Loc.GetString("qlippoth-corruption-applied"), uid, uid);
        if (corruption.SourceQlippoth is { } sourceUid &&
            Exists(sourceUid) && HasComp<QlippothActionsComponent>(sourceUid))
            _initiation.Dispatch<OnCorruptionAppliedInitiation>(
                sourceUid, new QlippothCorruptionEventArgs(uid, severity));

        return true;
    }

    /// <summary>Removes corruption exactly once, whether through treatment or natural expiry.</summary>
    public bool RemoveCorruption(
        EntityUid uid,
        QlippothCorruptionRemovalReason reason = QlippothCorruptionRemovalReason.Treated)
    {
        if (!TryComp<QlippothCorruptionComponent>(uid, out var corruption) || corruption.IsRemoving)
            return false;

        corruption.IsRemoving = true;
        Dirty(uid, corruption);
        var source = corruption.SourceQlippoth;
        var severity = corruption.Severity;
        RaiseLocalEvent(uid, new QlippothCorruptionRemovedEvent(uid, source, severity, reason), broadcast: true);
        if (source is { } sourceUid && Exists(sourceUid) && HasComp<QlippothActionsComponent>(sourceUid))
        {
            _initiation.Dispatch<OnCorruptionRemovedInitiation>(sourceUid,
                new QlippothCorruptionRemovedEventArgs(uid, severity, reason));
        }

        _popup.PopupEntity(Loc.GetString(reason == QlippothCorruptionRemovalReason.Treated
            ? "qlippoth-corruption-treated"
            : "qlippoth-corruption-expired"), uid, uid);
        RemCompDeferred<QlippothCorruptionComponent>(uid);
        return true;
    }

    private void TrySpreadCorruption(EntityUid carrier, QlippothCorruptionComponent corruption)
    {
        if (corruption.SpreadChance <= 0f || corruption.SpreadRadius <= 0f ||
            !TryComp<TransformComponent>(carrier, out _))
            return;

        var carrierCoordinates = _transform.GetMapCoordinates(carrier);
        var profile = new CorruptionProfile(
            corruption.RemainingSeconds,
            corruption.Severity,
            corruption.SanityDrainPerSecond,
            corruption.PulseInterval,
            corruption.SpreadChance,
            corruption.SpreadRadius);

        var targets = EntityQueryEnumerator<SanityComponent, TransformComponent>();
        while (targets.MoveNext(out var target, out _, out var targetTransform))
        {
            if (target == carrier || HasComp<QlippothCorruptionComponent>(target) ||
                targetTransform.MapID != carrierCoordinates.MapId ||
                (_transform.GetMapCoordinates(target).Position - carrierCoordinates.Position).Length() >
                corruption.SpreadRadius)
                continue;

            if (_containment.IsCorruptionProtected(target, corruption.SourceQlippoth, out var chamber))
            {
                NotifyContainmentMitigation(target, corruption.SourceQlippoth, chamber);
                continue;
            }

            if (!_random.Prob(corruption.SpreadChance))
                continue;

            if (ApplyCorruption(target, corruption.SourceQlippoth, profile, isSpread: true))
            {
                RaiseLocalEvent(target,
                    new QlippothCorruptionSpreadEvent(carrier, target, corruption.Severity), broadcast: true);
            }
        }
    }

    private void OnCorruptionExamined(EntityUid uid, QlippothCorruptionComponent component, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        args.PushMarkup(Loc.GetString("qlippoth-corruption-status",
            ("severity", component.Severity),
            ("duration", Math.Max(0, (int) MathF.Ceiling(component.RemainingSeconds)))));
    }

    private void OnEntityTerminating(ref EntityTerminatingEvent args)
    {
        var source = args.Entity;
        var query = EntityQueryEnumerator<QlippothCorruptionComponent>();
        while (query.MoveNext(out var uid, out var corruption))
        {
            if (corruption.SourceQlippoth != source)
                continue;

            corruption.SourceQlippoth = null;
            Dirty(uid, corruption);
            RaiseLocalEvent(uid, new QlippothCorruptionSourceLostEvent(uid, source), broadcast: true);
            _popup.PopupEntity(Loc.GetString("qlippoth-corruption-source-lost"), uid, uid);
        }

        _nextMitigationNotice.Remove(source);
    }

    private void NotifyContainmentMitigation(EntityUid target, EntityUid? source, EntityUid chamber)
    {
        RaiseLocalEvent(target, new QlippothCorruptionMitigatedEvent(target, source, chamber), broadcast: true);
        if (_nextMitigationNotice.TryGetValue(target, out var next) && _timing.CurTime < next)
            return;

        _nextMitigationNotice[target] = _timing.CurTime + MitigationNoticeCooldown;
        _popup.PopupEntity(Loc.GetString("qlippoth-corruption-contained"), target, target);
    }

    private static float ClampFinite(float value, float minimum, float maximum, float fallback)
    {
        return float.IsNaN(value) || float.IsInfinity(value)
            ? fallback
            : Math.Clamp(value, minimum, maximum);
    }
}
