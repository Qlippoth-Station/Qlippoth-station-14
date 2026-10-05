using Content.Shared.Qlippoth.Components;
using System.Numerics;
using Content.Shared.Qlippoth;
using Robust.Shared.Prototypes;
using Content.Server.Cargo.Systems;
using Content.Server.Station.Systems;
using Content.Shared.Cargo.Components;
using Content.Shared.Cargo.Prototypes;
using System.Linq;

namespace Content.Server.Qlippoth.Systems;

public enum QlippothMarketPurchaseResult : byte
{
    Success,
    OutOfStock,
    ChamberUnavailable,
    InsufficientFunds,
    CapsuleDeploymentFailed
}

/// <summary>
/// Secured Qlippoths (cleared rifts) go on sale here. Price comes from the Qlippoth prototype (QlippothComponent.MarketPrice);
/// the gate phase it came through is kept only for display.
/// </summary>
public sealed partial class QlippothMarketSystem : EntitySystem
{
    private readonly List<EntProtoId> _availableMarketQlippoths = new();
    private readonly Dictionary<EntProtoId, QGatePhase> _marketPhases = new();
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private CargoSystem _cargo = default!;
    [Dependency] private StationSystem _stations = default!;
    [Dependency] private QlippothSystem _qlippoths = default!;
    [Dependency] private QGateSystem _gates = default!;
    [Dependency] private ContainmentDimensionSystem _containment = default!;

    public void AddSecuredQlippothToMarket(EntProtoId protoId, QGatePhase phase)
    {
        if (_availableMarketQlippoths.Contains(protoId))
            return;

        _availableMarketQlippoths.Add(protoId);
        _marketPhases[protoId] = phase;
    }

    public IReadOnlyList<EntProtoId> GetAvailableQlippoths()
    {
        return _availableMarketQlippoths;
    }

    public QlippothMarketPurchaseResult PurchaseQlippoth(
        EntProtoId protoId, EntityUid cargoSpawnLocation, string targetChamberId)
    {
        if (!_availableMarketQlippoths.Contains(protoId))
            return QlippothMarketPurchaseResult.OutOfStock;

        if (!_containment.IsChamberAvailable(targetChamberId))
            return QlippothMarketPurchaseResult.ChamberUnavailable;

        var price = GetPrice(protoId);
        var station = _stations.GetOwningStation(cargoSpawnLocation);
        if (station == null || price < 0)
            return QlippothMarketPurchaseResult.InsufficientFunds;

        var capsuleUid = Spawn("CapsuleQlippothTransport", Transform(cargoSpawnLocation).Coordinates.Offset(new Vector2(0f, -1f)));
        if (!TryComp<QlippothCapsuleComponent>(capsuleUid, out var capsule))
        {
            QueueDel(capsuleUid);
            return QlippothMarketPurchaseResult.CapsuleDeploymentFailed;
        }

        capsule.ContainedQlippothProto = protoId;
        capsule.TargetChamberId = targetChamberId;
        capsule.FallbackLocation = cargoSpawnLocation;
        Dirty(capsuleUid, capsule);
        if (!TryWithdraw(station.Value, price))
        {
            QueueDel(capsuleUid);
            return QlippothMarketPurchaseResult.InsufficientFunds;
        }

        _availableMarketQlippoths.Remove(protoId);
        _marketPhases.Remove(protoId);
        return QlippothMarketPurchaseResult.Success;
    }

    public IReadOnlyList<QlippothMarketEntry> GetMarketEntries()
    {
        return _availableMarketQlippoths.Select(proto =>
        {
            var phase = _marketPhases.GetValueOrDefault(proto, QGatePhase.Phase1Rift);
            return new QlippothMarketEntry(proto.Id, _prototypes.Index(proto).Name ?? proto.Id,
                _gates.GetPhaseName(phase), GetPrice(proto));
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

    public QlippothMarketPurchaseResult PurchaseFirstAvailable(EntityUid spawnLocation, string targetChamberId)
    {
        if (_availableMarketQlippoths.Count == 0)
            return QlippothMarketPurchaseResult.OutOfStock;

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
            var phase = _marketPhases.GetValueOrDefault(proto, QGatePhase.Phase1Rift);
            lines.Add(Loc.GetString("containment-market-entry",
                ("name", name),
                ("phase", _gates.GetPhaseName(phase))));
        }

        return Loc.GetString("containment-market-stock", ("stock", string.Join("\n", lines)));
    }

    private int GetPrice(EntProtoId protoId)
    {
        return _qlippoths.GetPrototypeData(protoId)?.MarketPrice ?? 0;
    }
}
