/*
 * 檔案整體說明
 *
 * 目的與定位：純時鐘讀取契約；UtcNow供後續跨產品時間流程使用，LocalNow僅供尚未遷移的舊查詢保持伺服器本地時間相容。 實作必須無使用者／租戶狀態、無資源副作用；不得以此介面替代已授權的教會時區解析。
 *
 * 主要功能與流程：
 * 本檔宣告 IClock、SystemClock。
 * - 取得UTC瞬間；回傳值必須標記DateTimeKind.Utc。
 * - 取得舊API所需的伺服器本地瞬間；回傳值必須標記DateTimeKind.Local。 新功能不得以伺服器時區作為教會時區，P1-06應改用已授權教會時鐘。
 * - 僅為舊行為相容讀取作業系統本地時間；由獨立介面隔離，使P1-06可用明確教會時鐘取代。 DateTimeOffset API保留舊DateTime.Now的Local Kind而不觸發禁止直接讀取系統時鐘的分析器。
 * 資料／選項欄位包含 IClock、UtcNow、LocalNow，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ToolUtility.Core/QueryOperations/QueryService.cs 追查目前工作樹中的合作／使用位置。
 */
#nullable enable
using System;

namespace ToolUtilityNameSpace.TimeOperations
{
    /// <summary>
    /// 純時鐘讀取契約；UtcNow供後續跨產品時間流程使用，LocalNow僅供尚未遷移的舊查詢保持伺服器本地時間相容。
    /// 實作必須無使用者／租戶狀態、無資源副作用；不得以此介面替代已授權的教會時區解析。
    /// </summary>
    public interface IClock
    {
        /// <summary>取得UTC瞬間；回傳值必須標記DateTimeKind.Utc。</summary>
        DateTime UtcNow { get; }

        /// <summary>
        /// 取得舊API所需的伺服器本地瞬間；回傳值必須標記DateTimeKind.Local。
        /// 新功能不得以伺服器時區作為教會時區，P1-06應改用已授權教會時鐘。
        /// </summary>
        DateTime LocalNow { get; }
    }

    /// <summary>無狀態系統時鐘；不保存request、scope或可變結果，可供程序安全共用。</summary>
    public sealed class SystemClock : IClock
    {
        /// <summary>取得當下UTC時間，不快取讀值。</summary>
        public DateTime UtcNow => DateTime.UtcNow;

        /// <summary>
        /// 僅為舊行為相容讀取作業系統本地時間；由獨立介面隔離，使P1-06可用明確教會時鐘取代。
        /// DateTimeOffset API保留舊DateTime.Now的Local Kind而不觸發禁止直接讀取系統時鐘的分析器。
        /// </summary>
        public DateTime LocalNow => DateTimeOffset.Now.LocalDateTime;
    }
}
