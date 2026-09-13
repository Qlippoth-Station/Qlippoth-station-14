using Content.Shared.Qlippoth.Components;
using Robust.Shared.Random;

namespace Content.Server.Qlippoth.Systems;

/// <summary>
/// Handles the Corruption exposure state on crew members.
/// What a specific Qlippoth *does* to its victims, and when it corrupts them, is not defined here:
/// the Qlippoth's own actions call ApplyCorruption (ApplyCorruptionResult) with its own numbers, and this system
/// keeps the timers running, drains sanity, spreads the corruption and raises
/// OnCorruptionAppliedInitiation / OnCorruptionPulseInitiation on the source Qlippoth.
/// </summary>
public sealed class CorruptionSystem : EntitySystem
{
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SanitySystem _sanity = default!;
    [Dependency] private QlippothActionInitiationSystem _initiation = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<QlippothCorruptionComponent>();
        while (query.MoveNext(out var uid, out var corruption))
        {
            corruption.RemainingSeconds -= frameTime;
            if (corruption.RemainingSeconds <= 0f)
            {
                RemoveCorruption(uid);
                continue;
            }

            if (corruption.SanityDrainPerSecond > 0f)
                _sanity.DamageSanity(uid, corruption.SanityDrainPerSecond * corruption.Severity * frameTime);

            corruption.PulseTimeRemaining -= frameTime;
            if (corruption.PulseTimeRemaining <= 0f)
            {
                corruption.PulseTimeRemaining = corruption.PulseInterval;
                TrySpreadCorruption(uid, corruption);
                if (corruption.SourceQlippoth is { } source && Exists(source))
                    _initiation.Dispatch<OnCorruptionPulseInitiation>(source, new QlippothCorruptionEventArgs(uid, corruption.Severity));
            }

            Dirty(uid, corruption);
        }
    }

    /// <summary>
    /// Corrupt a crew member. Returns false if they are already corrupted.
    /// Raises OnCorruptionAppliedInitiation on the source Qlippoth so its actions can react.
    /// </summary>
    public bool ApplyCorruption(EntityUid uid, EntityUid? sourceQlippoth, CorruptionProfile profile)
    {
        if (HasComp<QlippothCorruptionComponent>(uid))
            return false;

        var corruption = EnsureComp<QlippothCorruptionComponent>(uid);
        corruption.RemainingSeconds = profile.Duration;
        corruption.Severity = Math.Max(1, profile.Severity);
        corruption.SanityDrainPerSecond = profile.SanityDrainPerSecond;
        corruption.PulseInterval = profile.PulseInterval;
        corruption.PulseTimeRemaining = profile.PulseInterval;
        corruption.SpreadChance = profile.SpreadChance;
        corruption.SpreadRadius = profile.SpreadRadius;
        corruption.SourceQlippoth = sourceQlippoth;
        Dirty(uid, corruption);

        if (sourceQlippoth is { } source && Exists(source))
            _initiation.Dispatch<OnCorruptionAppliedInitiation>(source, new QlippothCorruptionEventArgs(uid, corruption.Severity));

        return true;
    }

    /// <summary>Removes corruption from a crew member (e.g. via medical treatment).</summary>
    public void RemoveCorruption(EntityUid uid)
    {
        RemCompDeferred<QlippothCorruptionComponent>(uid);
    }

    private void TrySpreadCorruption(EntityUid carrier, QlippothCorruptionComponent corruption)
    {
        if (corruption.SpreadChance <= 0f || corruption.SpreadRadius <= 0f)
            return;

        var profile = new CorruptionProfile(corruption.RemainingSeconds, corruption.Severity, corruption.SanityDrainPerSecond,
            corruption.PulseInterval, corruption.SpreadChance, corruption.SpreadRadius);

        var carrierTransform = Transform(carrier);
        var targets = EntityQueryEnumerator<SanityComponent, TransformComponent>();
        while (targets.MoveNext(out var target, out _, out var targetTransform))
        {
            if (target == carrier || HasComp<QlippothCorruptionComponent>(target) ||
                targetTransform.MapID != carrierTransform.MapID ||
                (targetTransform.Coordinates.Position - carrierTransform.Coordinates.Position).Length() > corruption.SpreadRadius)
                continue;

            if (_random.Prob(corruption.SpreadChance))
                ApplyCorruption(target, corruption.SourceQlippoth, profile);
        }
    }
}
