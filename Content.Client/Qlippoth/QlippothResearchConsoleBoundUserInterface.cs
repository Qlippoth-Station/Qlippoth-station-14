using System.Numerics;
using Content.Shared.Qlippoth.Components;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Localization;

namespace Content.Client.Qlippoth;

/// <summary>
/// Client BUI for remote Qlippoth scanning and research extraction.
/// </summary>
public sealed class QlippothResearchConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private DefaultWindow? _window;
    private Label? _status;

    protected override void Open()
    {
        base.Open();
        _window = new DefaultWindow
        {
            Title = Loc.GetString("research-console-title"),
            MinSize = new Vector2(460, 300)
        };

        var content = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        _status = new Label { Text = Loc.GetString("research-console-waiting") };
        content.AddChild(_status);

        var scan = new Button { Text = Loc.GetString("research-console-scan") };
        scan.OnPressed += _ => SendMessage(new QlippothResearchScanMessage());
        content.AddChild(scan);

        var extract = new Button { Text = Loc.GetString("research-console-extract") };
        extract.OnPressed += _ => SendMessage(new QlippothResearchExtractMessage());
        content.AddChild(extract);

        _window.Contents.AddChild(content);
        _window.OnClose += Close;
        _window.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (_status == null || state is not QlippothResearchConsoleBuiState researchState)
            return;

        var target = researchState.QlippothName ?? Loc.GetString("research-console-no-target");
        var phase = researchState.QlippothName == null ? "--" : researchState.Phase.ToString();
        var scan = researchState.IsScanning
            ? Loc.GetString("research-console-scanning", ("progress", researchState.ScanProgress.ToString("P0")))
            : Loc.GetString("research-console-ready");
        _status.Text = Loc.GetString("research-console-status",
            ("target", target),
            ("phase", phase),
            ("stress", researchState.StressLevel.ToString("0.0")),
            ("points", researchState.AccumulatedPoints),
            ("scan", scan));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _window?.Dispose();
    }
}
