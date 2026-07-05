// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：SpeechMessage.Payments/Models/PaymentProviderKind.cs
// 所屬區塊：可重用付款核心、付款 provider、ASP.NET Core 整合、workflow 與測試模組。
// 檔案責任：此檔案位於資料模型或 ViewModel 層，註解重點在說明欄位語意、序列化/繫結用途與相容性限制。
// 主要型別：enum PaymentProviderKind
// 主要成員：未偵測到公開/受保護成員；維護時請以檔案內的常數、欄位、private helper 或屬性初始化邏輯為主要閱讀入口。
// 引用命名空間：未宣告 using；請由命名空間、同檔型別或完全限定名稱判讀相依性。
// 閱讀路徑：閱讀此檔案時應先確認金額、訂單編號、付款狀態、provider profile、callback acknowledgement 與錯誤訊息是否跨層保持一致。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
namespace SpeechMessage.Payments.Models;

/// <summary>
/// 金流核心目前支援的 provider 種類。
/// 此 enum 是跨專案設定與 runtime routing 的穩定識別，不應塞入 provider SDK 型別。
/// </summary>
public enum PaymentProviderKind
{
    /// <summary>
    /// 未指定或尚未解析；一般只用於輸入預設值，正式執行時應由 profile 決定實際 provider。
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// 永豐 QPay 金流。
    /// </summary>
    Sinopac = 1,

    /// <summary>
    /// 高鉅 MyPay 金流。
    /// </summary>
    MyPay = 2,

    /// <summary>
    /// 台新 TSPG 金流。
    /// </summary>
    Taishin = 3
}
