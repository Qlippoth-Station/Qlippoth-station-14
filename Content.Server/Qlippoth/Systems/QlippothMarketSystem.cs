using Content.Shared.Qlippoth.Components;
using System.Numerics;
using Content.Shared.Qlippoth;
using Robust.Shared.Prototypes;
using Content.Server.Cargo.Systems;
using Content.Server.Station.Systems;
using Content.Shared.Cargo.Components;

namespace Content.Server.Qlippoth.Systems;

public sealed class QlippothMarketSystem : EntitySystem
{
    private readonly List<EntProtoId> _availableMarketQlippoths = new();
    private readonly Dictionary<EntProtoId, QGatePhase> _marketPhases = new();
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly CargoSystem _cargo = default!;
    [Dependency] private readonly StationSystem _stations = default!;

    [Dependency] private readonly IConfigurationManager _cfg = default!;

    public override void Initialize()
    {
        base.Initialize();
    }

    public void AddSecuredQlippothToMarket(EntProtoId protoId, QGatePhase phase)
    {
        _availableMarketQlippoths.Add(protoId);
        _marketPhases[protoId] = phase;
    }

    public IReadOnlyList<EntProtoId> GetAvailableQlippoths()
    {
        return _availableMarketQlippoths;
    }

    public bool PurchaseQlippoth(EntProtoId protoId, EntityUid cargoSpawnLocation, string targetChamberId)
    {
        if (!_availableMarketQlippoths.Contains(protoId))
            return false;

        var phase = _marketPhases.GetValueOrDefault(protoId, QGatePhase.Phase1Rift);
        var price = GetPrice(phase);
        var station = _stations.GetOwningStation(cargoSpawnLocation);
        if (station == null || !TryWithdraw(station.Value, price))
            return false;

        _availableMarketQlippoths.Remove(protoId);
        _marketPhases.Remove(protoId);

        // Spawn transport capsule at Cargo
        var capsuleUid = Spawn("CapsuleQlippothTransport", Transform(cargoSpawnLocation).Coordinates.Offset(new Vector2(0f, -1f)));
        if (TryComp<QlippothCapsuleComponent>(capsuleUid, out var capsule))
        {
            capsule.ContainedQlippothProto = protoId;
            capsule.TargetChamberId = targetChamberId;
            Dirty(capsuleUid, capsule);
        }

        return true;
    }

    public IReadOnlyList<QlippothMarketEntry> GetMarketEntries()
    {
        return _availableMarketQlippoths.Select(proto =>
        {
            var phase = _marketPhases.GetValueOrDefault(proto, QGatePhase.Phase1Rift);
            return new QlippothMarketEntry(proto.Id, _prototypes.Index(proto).Name ?? proto.Id,
                GetPhaseName(phase), GetPrice(phase));
        }).ToList();
    }

    private bool TryWithdraw(EntityUid station, int amount)
    {
        if (!TryComp<StationBankAccountComponent>(station, out var bank) ||
            _cargo.GetBalanceFromAccount((station, bank), "Cargo") < amount)
            return false;

        _cargo.UpdateBankAccount((station, bank), -amount,
            new Dictionary<ProtoId<CargoAccountPrototype>, double> { { "Cargo", 1 } });
        return true;
    }

    public bool PurchaseFirstAvailable(EntityUid spawnLocation, string targetChamberId)
    {
        if (_availableMarketQlippoths.Count == 0)
            return false;

        return PurchaseQlippoth(_availableMarketQlippoths[0], spawnLocation, targetChamberId);
    }

    public string GetMarketDisplay()
    {
        if (_availableMarketQlippoths.Count == 0)
            return Loc.GetString("containment-market-no-stock");

        var lines = new List<string>();
        foreach (var proto in _availableMarketQlippoths)
        {
            var name = _prototypes.Index(proto).Name ?? proto.Id;
            var phase = _marketPhases.TryGetValue(proto, out var value) ? value : QGatePhase.Phase1Rift;
            lines.Add(Loc.GetString("containment-market-entry",
                ("name", name),
                ("phase", GetPhaseName(phase))));
        }

        return Loc.GetString("containment-market-stock", ("stock", string.Join("\n", lines)));
    }

    private static string GetPhaseName(QGatePhase phase)
    {
        return phase switch
        {
            QGatePhase.Phase1Rift => Loc.GetString("qgate-phase-rift"),
            QGatePhase.Phase2Verge => Loc.GetString("qgate-phase-verge"),
            QGatePhase.Phase3Eclipse => Loc.GetString("qgate-phase-eclipse"),
            QGatePhase.Phase4Abyss => Loc.GetString("qgate-phase-abyss"),
            QGatePhase.Phase5Horizon => Loc.GetString("qgate-phase-horizon"),
            _ => Loc.GetString("qgate-phase-rift")
        };
    }

    private int GetPrice(QGatePhase phase)
    {
        return phase switch
        {
            QGatePhase.Phase1Rift => _cfg.GetCVar(CCVars.QlippothPhase1Price),
            QGatePhase.Phase2Verge => _cfg.GetCVar(CCVars.QlippothPhase2Price),
            QGatePhase.Phase3Eclipse => _cfg.GetCVar(CCVars.QlippothPhase3Price),
            QGatePhase.Phase4Abyss => _cfg.GetCVar(CCVars.QlippothPhase4Price),
            QGatePhase.Phase5Horizon => _cfg.GetCVar(CCVars.QlippothPhase5Price),
            _ => _cfg.GetCVar(CCVars.QlippothPhase1Price)
        };
    }
}
