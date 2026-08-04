// ============================================================================
// 檔案：SpeechMessage.Dynamics.Abstractions/Execution/CeVersion.cs
// 用途：宣告部署端已驗證的 Dynamics Customer Engagement 版本。
// ============================================================================

namespace SpeechMessage.Dynamics.Abstractions.Execution;

/// <summary>
/// 定義 Profile 指向之 Customer Engagement 主要版本。版本只參與部署端相容性驗證與
/// Connector 選擇，不會進入產品請求或被當作 Session/Cache key，以避免呼叫端改變既有
/// Organization 的傳輸與連線生命週期。
/// </summary>
public enum CeVersion
{
    /// <summary>Customer Engagement 8.2。</summary>
    Ce82 = 0,

    /// <summary>Customer Engagement 9.1。</summary>
    Ce91 = 1
}
