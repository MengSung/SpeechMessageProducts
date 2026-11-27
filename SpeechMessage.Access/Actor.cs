/*
 * 檔案整體說明
 * 目的與定位：承載伺服器登入流程解析的Contact主鍵、目前租戶與產品代碼，作為F8權限檢查的主體候選；不保存LINE識別、token、Session或角色快照。
 * 主要功能與流程：產品先驗證登入及租戶選擇，再建立不可變Actor交給IAccessPolicy；正式引擎仍確認產品符合組合根並重讀有效Contact與其Account，不把候選GUID視為授權證明。
 * 使用邊界與維護：本型別沒有I/O、快取或資源所有權，只有呼叫端在當次請求保留值；禁止直接信任瀏覽器輸入建立可信主體，也不得跨使用者重用。變更須維持跨產品、跨租戶及失效身分拒絕測試。
 */
namespace SpeechMessage.Access;

/// <summary>已登入主體的候選身分；引擎仍重新確認有效Contact及其Account租戶，不信任單憑GUID。</summary>
/// <param name="ContactId">伺服器驗證的會友主鍵，不是LINE userId。</param>
/// <param name="TenantId">目前選擇且由伺服器解析的租戶Account主鍵。</param>
/// <param name="Product">必須符合組合根綁定的產品，不能選擇其他組織連線。</param>
public sealed record Actor(Guid ContactId, Guid TenantId, string Product);
