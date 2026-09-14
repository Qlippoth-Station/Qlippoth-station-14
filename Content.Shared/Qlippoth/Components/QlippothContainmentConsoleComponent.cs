namespace Content.Shared.Qlippoth.Components;

using Robust.Shared.Serialization;

/// <summary>
/// Identifies a containment console that can be used without station power.
/// </summary>
[RegisterComponent]
public sealed partial class QlippothContainmentConsoleComponent : Component
{
    [DataField]
    public string Title = string.Empty;

    [DataField]
    public string Status = string.Empty;

    [DataField]
    public string? SelectedMarketProtoId;
}

[Serializable, NetSerializable]
public enum QlippothContainmentConsoleUiKey : byte
{
    Key
}

[Serializable, NetSerializable]
public enum QlippothMarketConsoleUiKey : byte
{
    Key
}

[Serializable, NetSerializable]
public enum ContainmentBlueprintConsoleUiKey : byte
{
    Key
}

[Serializable, NetSerializable]
public sealed class QlippothMarketPurchaseMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class QlippothMarketSelectMessage : BoundUserInterfaceMessage
{
    public string ProtoId;

    public QlippothMarketSelectMessage(string protoId)
    {
        ProtoId = protoId;
    }
}

[Serializable, NetSerializable]
public sealed class ContainmentBlueprintBuildMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class QlippothContainmentConsoleBuiState : BoundUserInterfaceState
{
    public string Title;
    public string Status;
    public string Detail;
    public List<QlippothMarketEntry> MarketEntries;

    public QlippothContainmentConsoleBuiState(string title, string status, string detail,
        List<QlippothMarketEntry>? marketEntries = null)
    {
        Title = title;
        Status = status;
        Detail = detail;
        MarketEntries = marketEntries ?? new List<QlippothMarketEntry>();
    }
}

[Serializable, NetSerializable]
public sealed class QlippothMarketEntry
{
    public string ProtoId;
    public string Name;
    public string Phase;
    public int Price;

    public QlippothMarketEntry(string protoId, string name, string phase, int price)
    {
        ProtoId = protoId;
        Name = name;
        Phase = phase;
        Price = price;
    }
}
