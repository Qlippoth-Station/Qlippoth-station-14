using Content.Shared.Qlippoth.Components;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Timing;
using System.Linq;

namespace Content.Server.Qlippoth.Systems;

/// <summary>
/// Supplies the shared state for the tracker, market, and blueprint console UIs.
/// </summary>
public sealed partial class QlippothContainmentConsoleSystem : EntitySystem
{
    private static readonly TimeSpan OperationsBoardRefreshInterval = TimeSpan.FromSeconds(1);

    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly QlippothMarketSystem _market = default!;
    [Dependency] private readonly ContainmentDimensionSystem _containment = default!;
    [Dependency] private readonly QGateSystem _qgates = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    private TimeSpan _nextOperationsBoardRefresh;

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
        if (_timing.CurTime < _nextOperationsBoardRefresh)
            return;

        _nextOperationsBoardRefresh = _timing.CurTime + OperationsBoardRefreshInterval;
        var consoles = EntityQueryEnumerator<QlippothContainmentConsoleComponent>();
        while (consoles.MoveNext(out var uid, out var component))
        {
            if (component.Title.Contains("Operations Board", StringComparison.OrdinalIgnoreCase) &&
                _ui.IsUiOpen(uid, QGateRadarUiKey.Key))
                UpdateUiState(uid, component, QGateRadarUiKey.Key);
        }
    }

    private void OnMarketPurchase(EntityUid uid, QlippothContainmentConsoleComponent component,
        QlippothMarketPurchaseMessage message)
    {
        if (!component.Title.Contains("Auction", StringComparison.OrdinalIgnoreCase))
            return;

        var purchased = component.SelectedMarketProtoId is { } selected
            ? _market.PurchaseQlippoth(selected, uid, "command-starter")
            : _market.PurchaseFirstAvailable(uid, "command-starter");
        UpdateUiState(uid, component, QlippothMarketConsoleUiKey.Key,
            purchased ? Loc.GetString("containment-market-purchased") :
                Loc.GetString("containment-market-insufficient-funds"));
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
        else if (component.Title.Contains("Operations Board", StringComparison.OrdinalIgnoreCase))
            detail = GetOperationsBoardDetail();
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
        _ui.SetUiState(uid, uiKey,
            new QlippothContainmentConsoleBuiState(component.Title, statusOverride ?? component.Status, detail, entries));
    }

    private string GetOperationsBoardDetail()
    {
        _containment.EnsureContainmentDimensionCreated();

        var lines = new List<string>
        {
            Loc.GetString("containment-operations-board-gates"),
            _qgates.GetTrackerDetail(_timing.CurTime)
        };
        var chambers = new List<(ContainmentChamberComponent Chamber, TransformComponent Transform)>();
        var query = EntityQueryEnumerator<ContainmentChamberComponent, TransformComponent>();
        while (query.MoveNext(out _, out var chamber, out var xform))
        {
            if (_containment.IsContainmentDimension(xform.MapID))
                chambers.Add((chamber, xform));
        }

        var breached = chambers.Count(entry => entry.Chamber.IsBreached);
        var occupied = chambers.Count(entry => entry.Chamber.IsOccupied);
        var unfinished = chambers.Count(entry => !entry.Chamber.IsBuilt);
        lines.Add(string.Empty);
        lines.Add(Loc.GetString("containment-operations-board-chambers",
            ("total", chambers.Count),
            ("occupied", occupied),
            ("breached", breached),
            ("unfinished", unfinished)));

        foreach (var (chamber, _) in chambers.Where(entry => entry.Chamber.IsBreached))
        {
            lines.Add(Loc.GetString("containment-operations-board-breach",
                ("chamber", chamber.ChamberId),
                ("sector", chamber.Sector)));
        }

        return string.Join("\n", lines);
    }
}
