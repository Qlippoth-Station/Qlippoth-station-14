using Content.Shared.Qlippoth;
using Content.Shared.Qlippoth.Components;
using Content.Server.Popups;
using Content.Shared.CCVar;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server.Qlippoth.Systems;

/// <summary>
/// Handles Qlippoth corruption effects on crew members.
/// Phase 4+ Qlippoths can corrupt nearby crew, temporarily turning them hostile.
/// </summary>
public sealed class CorruptionSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly PopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Corruption is an exposure state; sanity is only one possible consequence.
        var sanityQuery = EntityQueryEnumerator<SanityComponent, TransformComponent>();
        while (sanityQuery.MoveNext(out var uid, out _, out var xform))
        {
            if (HasComp<QlippothCorruptionComponent>(uid))
                continue;

            // Roll for corruption based on proximity to Phase 4+ Qlippoths
            var qlippothQuery = EntityQuery<QlippothComponent, TransformComponent>();

            foreach (var (qlippoth, qXform) in qlippothQuery)
            {
                if (xform.MapID != qXform.MapID)
                    continue;

                if (qlippoth.Phase < QGatePhase.Phase4Abyss)
                    continue;

                var dist = (xform.Coordinates.Position - qXform.Coordinates.Position).Length();
                if (dist > _cfg.GetCVar(CCVars.QlippothCorruptionRadius))
                    continue;

                // Higher phase = higher corruption chance
                var chance = _cfg.GetCVar(CCVars.QlippothCorruptionChance) * (int) qlippoth.Phase * frameTime;
                if (_random.Prob(chance))
                {
                    ApplyCorruption(uid, qlippoth.Phase, qXform.Owner);
                    break;
                }
            }
        }

        var corruptionQuery = EntityQueryEnumerator<QlippothCorruptionComponent>();
        while (corruptionQuery.MoveNext(out var uid, out var corruption))
        {
            corruption.RemainingSeconds -= frameTime;
            if (corruption.RemainingSeconds > 0f)
            {
                if (TryComp<SanityComponent>(uid, out var sanity))
                {
                    sanity.CurrentSanity = MathF.Max(0f,
                        sanity.CurrentSanity - _cfg.GetCVar(CCVars.QlippothCorruptionSanityDrain) * corruption.Severity * frameTime);
                    Dirty(uid, sanity);
                }

                corruption.PulseTimeRemaining -= frameTime;
                if (corruption.PulseTimeRemaining <= 0f)
                {
                    corruption.PulseTimeRemaining = _cfg.GetCVar(CCVars.QlippothCorruptionPulseInterval);
                    TrySpreadCorruption(uid, corruption);
                    if (corruption.SourceQlippoth is { } source &&
                        TryComp<QlippothCorruptionProfileComponent>(source, out var profile))
                    {
                        ExecuteEffects(uid, source, profile.PulseEffects);
                    }
                }

                Dirty(uid, corruption);
                continue;
            }

            RemoveCorruption(uid);
        }
    }

    /// <summary>
    /// Applies corruption to a crew member, marking them as hostile.
    /// </summary>
    public void ApplyCorruption(EntityUid uid, QGatePhase sourcePhase = QGatePhase.Phase4Abyss,
        EntityUid? sourceQlippoth = null)
    {
        if (HasComp<QlippothCorruptionComponent>(uid))
            return;

        var corruption = EnsureComp<QlippothCorruptionComponent>(uid);
        corruption.RemainingSeconds = _cfg.GetCVar(CCVars.QlippothCorruptionDuration);
        corruption.SourcePhase = sourcePhase;
        corruption.SourceQlippoth = sourceQlippoth;
        corruption.Severity = Math.Max(1, (int) sourcePhase - (int) QGatePhase.Phase3Eclipse);
        corruption.PulseTimeRemaining = _cfg.GetCVar(CCVars.QlippothCorruptionPulseInterval);
        Dirty(uid, corruption);
        if (sourceQlippoth is { } source && TryComp<QlippothCorruptionProfileComponent>(source, out var profile))
            ExecuteEffects(uid, source, profile.ApplyEffects);
    }

    /// <summary>
    /// Removes corruption from a crew member (e.g. via medical treatment).
    /// </summary>
    public void RemoveCorruption(EntityUid uid)
    {
        RemCompDeferred<QlippothCorruptionComponent>(uid);
    }

    private void TrySpreadCorruption(EntityUid source, QlippothCorruptionComponent corruption)
    {
        var sourceTransform = Transform(source);
        var targets = EntityQueryEnumerator<SanityComponent, TransformComponent>();
        while (targets.MoveNext(out var target, out _, out var targetTransform))
        {
            if (target == source || HasComp<QlippothCorruptionComponent>(target) ||
                targetTransform.MapID != sourceTransform.MapID ||
                (targetTransform.Coordinates.Position - sourceTransform.Coordinates.Position).Length() >
                _cfg.GetCVar(CCVars.QlippothCorruptionRadius))
                continue;

            if (_random.Prob(_cfg.GetCVar(CCVars.QlippothCorruptionSpreadChance)))
                ApplyCorruption(target, corruption.SourcePhase, corruption.SourceQlippoth);
        }
    }

    public void SpawnEffect(EntityUid target, string prototype, int count)
    {
        for (var i = 0; i < count; i++)
            Spawn(prototype, Transform(target).Coordinates);
    }

    public void DamageSanity(EntityUid target, float amount)
    {
        if (!TryComp<SanityComponent>(target, out var sanity))
            return;

        sanity.CurrentSanity = MathF.Max(0f, sanity.CurrentSanity - amount);
        Dirty(target, sanity);
    }

    public void ShowPopup(EntityUid target, string message)
    {
        _popup.PopupEntity(Loc.GetString(message), target, target);
    }

    private void ExecuteEffects(EntityUid target, EntityUid source, IEnumerable<QlippothCorruptionEffect> effects)
    {
        foreach (var effect in effects)
            effect.Execute(target, source, this);
    }
}
