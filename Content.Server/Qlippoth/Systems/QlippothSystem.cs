using Content.Shared.Qlippoth;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server.Qlippoth.Systems;

/// <summary>
/// The Qlippoth spawn pool: which Qlippoth prototypes fit which Q-Gate phase, read from the
/// QlippothComponent (gatePhases / spawnWeight) on every entity prototype. QGateSystem asks this
/// when a rift opens. Nothing else lives here; presence and movement have their own systems.
/// </summary>
public sealed class QlippothSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IComponentFactory _factory = default!;
    [Dependency] private IRobustRandom _random = default!;

    private readonly Dictionary<QGatePhase, List<(EntProtoId Id, float Weight)>> _pool = new();

    public override void Initialize()
    {
        base.Initialize();
        RebuildPool();
        _prototypes.PrototypesReloaded += OnPrototypesReloaded;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _prototypes.PrototypesReloaded -= OnPrototypesReloaded;
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<EntityPrototype>())
            RebuildPool();
    }

    private void RebuildPool()
    {
        _pool.Clear();
        foreach (var prototype in _prototypes.EnumeratePrototypes<EntityPrototype>())
        {
            if (prototype.Abstract || !prototype.TryGetComponent<QlippothComponent>(out var qlippoth, _factory))
                continue;

            foreach (var phase in qlippoth.GatePhases)
            {
                if (!_pool.TryGetValue(phase, out var list))
                    _pool[phase] = list = new List<(EntProtoId, float)>();
                list.Add((prototype.ID, qlippoth.SpawnWeight));
            }
        }
    }

    /// <summary>Everything that can come through a gate of this phase, with weights.</summary>
    public IReadOnlyList<(EntProtoId Id, float Weight)> GetPool(QGatePhase phase)
    {
        return _pool.TryGetValue(phase, out var list) ? list : Array.Empty<(EntProtoId, float)>();
    }

    /// <summary>Weighted random pick from the pool of a gate phase. False if nothing (with weight > 0) fits.</summary>
    public bool TryPickQlippoth(QGatePhase phase, out EntProtoId prototype)
    {
        prototype = default;
        var candidates = GetPool(phase);
        var total = 0f;
        foreach (var (_, weight) in candidates)
            total += Math.Max(0f, weight);
        if (total <= 0f)
            return false;

        var roll = _random.NextFloat() * total;
        foreach (var (id, weight) in candidates)
        {
            roll -= Math.Max(0f, weight);
            if (roll <= 0f)
            {
                prototype = id;
                return true;
            }
        }

        prototype = candidates[^1].Id;
        return true;
    }

    /// <summary>The QlippothComponent as written on the prototype (price, dungeon...), without spawning anything.</summary>
    public QlippothComponent? GetPrototypeData(EntProtoId prototype)
    {
        return _prototypes.TryIndex(prototype, out var proto) && proto.TryGetComponent<QlippothComponent>(out var qlippoth, _factory)
            ? qlippoth
            : null;
    }
}
