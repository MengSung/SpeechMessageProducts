// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Payments/LegacyDonationPaymentModels.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於付款相關流程，註解重點在說明 provider 邊界、金流狀態、錯誤處理與不可改變的外部契約。
// 主要型別：class CreOrder、class CreOrderATMParamRes、class CreOrderCardParamRes、class CreOrderMobileParamRes、class OrderMaintain
// 主要成員：OrderNo、ShopNo、TSNo、PayType、Amount、Status、Description、Param1、Param2、Param3
// 引用命名空間：未宣告 using；請由命名空間、同檔型別或完全限定名稱判讀相依性。
// 閱讀路徑：閱讀此檔案時應先確認金額、訂單編號、付款狀態、provider profile、callback acknowledgement 與錯誤訊息是否跨層保持一致。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
