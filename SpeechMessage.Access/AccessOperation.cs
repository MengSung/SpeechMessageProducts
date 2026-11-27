/*
 * 檔案整體說明
 * 目的與定位：定義三產品共用授權入口的讀取、新增、修改、刪除四種操作，讓呼叫端與權限資料的操作欄位使用同一封閉契約。
 * 主要功能與流程：產品依實際業務操作傳入一個列舉值，DataverseAccessPolicy驗證定義範圍後對應平台choice；每項權限獨立，不因取得修改權而隱含刪除權。
 * 使用邊界與維護：列舉本身不驗身分、不授權、不持有使用者資料或可釋放資源；未知數值由消費端在SDK前拒絕。調整順序會影響choice映射，必須同步schema、CRUD矩陣與未知值拒絕測試，不能直接擴張既有權限。
 */
namespace SpeechMessage.Access;

/// <summary>權限資料的四種操作；未知數值在查詢之前拒絕，不把寫入擴張為其他權限。</summary>
public enum AccessOperation
{
    /// <summary>讀取經授權範圍內資料，不含修改能力。</summary>
    Read,
    /// <summary>新增資料；資料擁有者與租戶仍由伺服器建立。</summary>
    Create,
    /// <summary>修改既有資料，不隱含刪除或新增能力。</summary>
    Update,
    /// <summary>刪除既有資料，必須有獨立授權。</summary>
    Delete
}
