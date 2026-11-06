/*
 * 檔案整體說明
 *
 * 目的與定位：本檔整合下列相關契約：CreOrder：舊 ChurchReport QPay 建立訂單流程使用的相容模型。 新金流核心不公開這個型別；它只留在 ChurchReport adapter，協助舊前端與 
 * processor 逐步遷移到 provider-neutral contract。；CreOrderATMParamRes：舊 QPay ATM 建立訂單回應欄位。 ATM 虛擬帳號是繳費指示，必須由核心 
 * provider data 映射回來給 ChurchReport 顯示與通知。；CreOrderCardParamRes：舊 QPay 信用卡付款頁 URL 欄位。 若核心沒有回傳有效付款頁 URL，adapter 
 * 必須讓建立訂單失敗，避免瀏覽器跳回原頁。
 *
 * 主要功能與流程：
 * 本檔宣告 CreOrder、CreOrderATMParamRes、CreOrderCardParamRes、CreOrderMobileParamRes、OrderMaintain。
 * 資料／選項欄位包含 CreOrder、OrderNo、ShopNo、TSNo、PayType、Amount、Status、Description、Param1、Param2、Param3、ATMParam、
 * CardParam、MobileParam、CreOrderATMParamRes、AtmPayNo，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 SpeechMessageProducts.ChurchReport/Models/DonationPaymentManager.cs、
 * SpeechMessageProducts.ChurchReport/Payments/DonationPaymentCreateGatewayAdapter.cs、
 * SpeechMessageProducts.ChurchReport/Payments/IDonationPaymentCreateGatewayAdapter.cs、
 * SpeechMessageProducts.ChurchReport/Services/DonationBookingService.cs、
 * SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.PaymentGateway.cs 
 * 追查目前工作樹中的合作／使用位置。
 */
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
