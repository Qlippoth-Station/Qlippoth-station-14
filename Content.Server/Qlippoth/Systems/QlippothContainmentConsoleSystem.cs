using System;
using System.Linq;
using Content.Shared.Qlippoth.Components;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Timing;

namespace Content.Server.Qlippoth.Systems;

/// <summary>
/// Supplies the shared state for the tracker, market, and blueprint console UIs.
/// </summary>
public sealed partial class QlippothContainmentConsoleSystem : EntitySystem
{
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private QlippothMarketSystem _market = default!;
    [Dependency] private ContainmentDimensionSystem _containment = default!;
    [Dependency] private QGateSystem _qgates = default!;
    [Dependency] private IGameTiming _timing = default!;

    private TimeSpan _nextRadarUiUpdate;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<QlippothContainmentConsoleComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<QlippothContainmentConsoleComponent, QlippothMarketPurchaseMessage>(OnMarketPurchase);
        SubscribeLocalEvent<QlippothContainmentConsoleComponent, QlippothMarketSelectMessage>(OnMarketSelect);
        SubscribeLocalEvent<QlippothContainmentConsoleComponent, ContainmentBlueprintBuildMessage>(OnBlueprintBuild);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextRadarUiUpdate)
            return;

        _nextRadarUiUpdate = _timing.CurTime + TimeSpan.FromSeconds(1);
        var radars = EntityQueryEnumerator<QlippothContainmentConsoleComponent, QGateRadarComponent>();
        while (radars.MoveNext(out var uid, out var console, out _))
        {
            if (_ui.IsUiOpen(uid, QGateRadarUiKey.Key))
                UpdateUiState(uid, console, QGateRadarUiKey.Key);
        }
    }

    private void OnMarketPurchase(EntityUid uid, QlippothContainmentConsoleComponent component,
        QlippothMarketPurchaseMessage message)
    {
        if (!component.Title.Contains("Auction", StringComparison.OrdinalIgnoreCase))
            return;

        if (!_containment.IsChamberAvailable(message.TargetChamberId))
        {
            UpdateUiState(uid, component, QlippothMarketConsoleUiKey.Key,
                Loc.GetString("containment-market-chamber-unavailable"));
            return;
        }

        var result = component.SelectedMarketProtoId is { } selected
            ? _market.PurchaseQlippoth(selected, uid, message.TargetChamberId)
            : _market.PurchaseFirstAvailable(uid, message.TargetChamberId);
        UpdateUiState(uid, component, QlippothMarketConsoleUiKey.Key,
            result switch
            {
                QlippothMarketPurchaseResult.Success => Loc.GetString("containment-market-purchased"),
                QlippothMarketPurchaseResult.ChamberUnavailable =>
                    Loc.GetString("containment-market-chamber-unavailable"),
                QlippothMarketPurchaseResult.InsufficientFunds =>
                    Loc.GetString("containment-market-insufficient-funds"),
                QlippothMarketPurchaseResult.CapsuleDeploymentFailed =>
                    Loc.GetString("containment-market-deployment-failed"),
                _ => Loc.GetString("containment-market-out-of-stock")
            });
    }

    private void OnMarketSelect(EntityUid uid, QlippothContainmentConsoleComponent component,
        QlippothMarketSelectMessage message)
    {
        if (!component.Title.Contains("Auction", StringComparison.OrdinalIgnoreCase))
            return;

        component.SelectedMarketProtoId = message.ProtoId;
        UpdateUiState(uid, component, QlippothMarketConsoleUiKey.Key);
    }

    private void OnBlueprintBuild(EntityUid uid, QlippothContainmentConsoleComponent component,
        ContainmentBlueprintBuildMessage message)
    {
        if (!component.Title.Contains("Blueprint", StringComparison.OrdinalIgnoreCase))
            return;

        var blueprint = _containment.CreateEngineeringBlueprint(uid);
        UpdateUiState(uid, component, ContainmentBlueprintConsoleUiKey.Key,
            blueprint.Valid ? Loc.GetString("containment-blueprint-created") :
                Loc.GetString("containment-blueprint-failed"));
    }

    private void OnUiOpened(EntityUid uid, QlippothContainmentConsoleComponent component, BoundUIOpenedEvent args)
    {
        UpdateUiState(uid, component, args.UiKey);
    }

    private void UpdateUiState(EntityUid uid, QlippothContainmentConsoleComponent component, Enum uiKey,
        string? statusOverride = null)
    {
        var detail = component.Status;
        if (TryComp<QGateRadarComponent>(uid, out var radar))
            detail = _qgates.GetTrackerDetail(_timing.CurTime);
        else if (component.Title.Contains("Auction", StringComparison.OrdinalIgnoreCase))
        {
            detail = _market.GetMarketDisplay();
        }
        else if (component.Title.Contains("Blueprint", StringComparison.OrdinalIgnoreCase))
        {
            detail = Loc.GetString("containment-blueprint-active");
        }

        var entries = component.Title.Contains("Auction", StringComparison.OrdinalIgnoreCase)
            ? _market.GetMarketEntries().ToList()
            : null;
        var chambers = component.Title.Contains("Auction", StringComparison.OrdinalIgnoreCase)
            ? _containment.GetAvailableChambers()
            : null;
        _ui.SetUiState(uid, uiKey,
            new QlippothContainmentConsoleBuiState(component.Title, statusOverride ?? component.Status, detail, entries, chambers));
    }
}
