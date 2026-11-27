/*
 * 檔案整體說明
 * 目的與定位：提供三產品以Actor、功能代碼、CRUD操作及DataScope取得單次授權決策的共用契約；產品端負責建立可信主體與業務目標。
 * 主要功能與流程：實作者須每次重新驗證同產品、有效身分與同租戶，從有效角色權限取聯集並限縮資料範圍；預期無權回拒絕，真正SDK或清理故障必須保留原例外並交共用告警。
 * 使用邊界與維護：介面本身不持有資源；Dataverse實作唯一擁有當次租約，同步SDK完成後才歸還及發布結果。呼叫端取消不能遺棄仍執行的工作，不可跨請求快取決策。變更須同步矩陣、角色撤回、跨租戶、故障與取消排空測試。
 */
namespace SpeechMessage.Access;

/// <summary>各產品共用的即時權限入口；每次呼叫重新查詢，不接受角色名稱捷徑或前次請求快取。</summary>
public interface IAccessPolicy
{
    /// <summary>先驗身分與同租戶，再對有效角色權限取聯集；SDK故障拋出且告警，不冒充預期拒絕。</summary>
    /// <param name="actor">伺服器解析的主體。</param>
    /// <param name="feature">固定功能代碼。</param>
    /// <param name="operation">單一操作。</param>
    /// <param name="target">伺服器建立的資料範圍。</param>
    /// <param name="ct">呼叫端取消；同步SDK完成前不提前歸還租約。</param>
    /// <returns>當次完成且不可變的決策；不包含資料庫列集合。</returns>
    Task<AccessDecision> AuthorizeAsync(Actor actor, string feature, AccessOperation operation, DataScope target, CancellationToken ct = default);
}
