namespace ChurchReport.Payments;

/// <summary>
/// 舊 ChurchReport QPay 建立訂單流程使用的相容模型。
/// 新金流核心不公開這個型別；它只留在 ChurchReport adapter，協助舊前端與 processor 逐步遷移到 provider-neutral contract。
/// </summary>
public sealed class CreOrder
{
    public string OrderNo { get; set; } = string.Empty;
    public string ShopNo { get; set; } = string.Empty;
    public string TSNo { get; set; } = string.Empty;
    public string PayType { get; set; } = string.Empty;
    public int Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Param1 { get; set; } = string.Empty;
    public string Param2 { get; set; } = string.Empty;
    public string Param3 { get; set; } = string.Empty;
    public CreOrderATMParamRes ATMParam { get; set; }
    public CreOrderCardParamRes CardParam { get; set; }
    public CreOrderMobileParamRes MobileParam { get; set; }
}

/// <summary>
/// 舊 QPay ATM 建立訂單回應欄位。
/// ATM 虛擬帳號是繳費指示，必須由核心 provider data 映射回來給 ChurchReport 顯示與通知。
/// </summary>
public sealed class CreOrderATMParamRes
{
    public string AtmPayNo { get; set; } = string.Empty;
    public string WebAtmURL { get; set; } = string.Empty;
    public string OtpURL { get; set; } = string.Empty;
}

/// <summary>
/// 舊 QPay 信用卡付款頁 URL 欄位。
/// 若核心沒有回傳有效付款頁 URL，adapter 必須讓建立訂單失敗，避免瀏覽器跳回原頁。
/// </summary>
public sealed class CreOrderCardParamRes
{
    public string CardPayURL { get; set; } = string.Empty;
}

/// <summary>
/// 舊 QPay 行動支付付款頁 URL 欄位。
/// </summary>
public sealed class CreOrderMobileParamRes
{
    public string MobilePayURL { get; set; } = string.Empty;
}

/// <summary>
/// 舊 QPay 維護操作相容模型。
/// 第一版通用金流核心尚未支援退款、請款、取消或維護操作；此模型只為 ChurchReport 舊碼編譯相容保留。
/// </summary>
public sealed class OrderMaintain
{
    public string OrderNo { get; set; } = string.Empty;
    public string ShopNo { get; set; } = string.Empty;
    public string TSNo { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public int? Amount { get; set; }
    public string Date { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}
