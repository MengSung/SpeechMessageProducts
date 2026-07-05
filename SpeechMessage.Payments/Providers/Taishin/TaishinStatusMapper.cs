// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：SpeechMessage.Payments/Providers/Taishin/TaishinStatusMapper.cs
// 所屬區塊：可重用付款核心、付款 provider、ASP.NET Core 整合、workflow 與測試模組。
// 檔案責任：此檔案位於付款相關流程，註解重點在說明 provider 邊界、金流狀態、錯誤處理與不可改變的外部契約。
// 主要型別：class TaishinStatusMapper
// 主要成員：Map、IsSuccessCode
// 引用命名空間：SpeechMessage.Payments.Models
// 閱讀路徑：閱讀此檔案時應先確認金額、訂單編號、付款狀態、provider profile、callback acknowledgement 與錯誤訊息是否跨層保持一致。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using SpeechMessage.Payments.Models;

namespace SpeechMessage.Payments.Providers.Taishin;

/// <summary>
/// 將台新 ret_code/state 正規化成通用 PaymentStatus。
/// 建單、查詢、callback 的成功欄位組合不同，因此集中在此處維護狀態規則。
/// </summary>
internal static class TaishinStatusMapper
{
    public static PaymentStatus Map(string? retCode, string? state)
    {
        var normalizedRetCode = retCode?.Trim();
        var normalizedState = state?.Trim();

        if (IsSuccessCode(normalizedRetCode) && string.IsNullOrEmpty(normalizedState))
        {
            return PaymentStatus.Succeeded;
        }

        if (string.Equals(normalizedState, "1", StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrEmpty(normalizedRetCode) || IsSuccessCode(normalizedRetCode)))
        {
            return PaymentStatus.Succeeded;
        }

        if (string.Equals(normalizedState, "0", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(normalizedRetCode))
        {
            return PaymentStatus.Failed;
        }

        return PaymentStatus.Unknown;
    }

    private static bool IsSuccessCode(string? retCode)
    {
        return string.Equals(retCode, "00", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(retCode, "0", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(retCode, "0000", StringComparison.OrdinalIgnoreCase);
    }
}
