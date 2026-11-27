/*
 * 檔案整體說明
 * 目的與定位：定義F8每筆權限可涵蓋的本人、群組與當前租戶三種資料範圍，供角色權限的platform choice與AccessDecision共同表達範圍。
 * 主要功能與流程：DataverseAccessPolicy讀取權限列後逐項檢查Self的精確owner、Group的同租戶祖先路徑或Tenant邊界；較大範圍仍不得跨產品或租戶。
 * 使用邊界與維護：此列舉不執行查詢或擁有資源，呼叫端不能藉整數值擴權；未知choice由引擎拒絕發布允許結果。變更數值或順序須同步schema映射與本人、子孫、兄弟、未知值測試，不能以列舉大小取代權威驗證。
 */
namespace SpeechMessage.Access;

/// <summary>每筆權限的最大可見範圍，不能由呼叫端以整數擴張。</summary>
public enum ScopeLevel
{
    /// <summary>僅限精確相同的有效會友擁有者。</summary>
    Self,
    /// <summary>僅限已驗證的本人節點或其子孫節點。</summary>
    Group,
    /// <summary>整個當前已驗證租戶，但永遠不跨租戶或產品。</summary>
    Tenant
}
