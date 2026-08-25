using System.Numerics;
using Content.Shared.Qlippoth.Components;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Localization;

namespace Content.Client.Qlippoth;

/// <summary>
/// Shared client window used by the tracker, market, and blueprint consoles.
/// </summary>
public sealed class QlippothContainmentConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private DefaultWindow? _window;
    private Label? _detail;
    private Button? _purchaseButton;
    private Button? _buildButton;
    private OptionButton? _marketSelection;

    protected override void Open()
    {
        base.Open();
        _window = new DefaultWindow
        {
            Title = Loc.GetString("containment-console-title"),
            MinSize = new Vector2(420, 240)
        };

        var content = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        _detail = new Label { Text = Loc.GetString("containment-console-initializing") };
        var detailScroll = new ScrollContainer
        {
            VerticalExpand = true,
            MinSize = new Vector2(400, 180)
        };
        detailScroll.AddChild(_detail);
        content.AddChild(detailScroll);

        _purchaseButton = new Button { Text = Loc.GetString("containment-market-purchase") };
        _purchaseButton.OnPressed += _ => SendMessage(new QlippothMarketPurchaseMessage());
        content.AddChild(_purchaseButton);

        _marketSelection = new OptionButton();
        _marketSelection.OnItemSelected += args =>
        {
            if (_marketSelection.GetItemMetadata(args.Id) is string protoId)
                SendMessage(new QlippothMarketSelectMessage(protoId));
        };
        content.AddChild(_marketSelection);

        _buildButton = new Button { Text = Loc.GetString("containment-blueprint-build") };
        _buildButton.OnPressed += _ => SendMessage(new ContainmentBlueprintBuildMessage());
        content.AddChild(_buildButton);

        _window.Contents.AddChild(content);
        _window.OnClose += Close;
        _window.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (_detail == null || state is not QlippothContainmentConsoleBuiState consoleState)
            return;

        _window!.Title = consoleState.Title;
        _detail.Text = $"{consoleState.Status}\n\n{consoleState.Detail}";
        _purchaseButton!.Visible = consoleState.Title.Contains("Auction", StringComparison.OrdinalIgnoreCase);
        _marketSelection!.Visible = _purchaseButton.Visible;
        _marketSelection.Clear();
        foreach (var entry in consoleState.MarketEntries)
        {
            _marketSelection.AddItem($"{entry.Name} | {entry.Phase} | {entry.Price}", entry.ProtoId);
        }
        _buildButton!.Visible = consoleState.Title.Contains("Blueprint", StringComparison.OrdinalIgnoreCase);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _window?.Dispose();
    }
}
