/*
 * 檔案整體說明
 * 目的與定位：描述待操作業務記錄的租戶、可選Contact擁有者及群組節點，提供F8的Self、Group與Tenant範圍判斷，不是可由request body宣告的授權。
 * 主要功能與流程：產品從權威記錄或伺服器的新建規則建立此不可變候選；DataverseAccessPolicy先比租戶，再驗擁有者或透過群組來源核對節點祖先，缺少必要範圍即拒絕。
 * 使用邊界與維護：本檔不讀取業務資料，也無連線或背景工作；權威來源與當次請求內的保留期限由呼叫端負責。不能以暫存draft key取代正式記錄身分；變更須驗證空值、不同owner、兄弟群組及跨租戶拒絕。
 */
namespace SpeechMessage.Access;

/// <summary>伺服器從權威資料或新建規則建立的範圍；不是可從request body直接信任的授權主張。</summary>
/// <param name="TenantId">業務記錄所屬租戶，跨租戶直接拒絕。</param>
/// <param name="OwnerContactId">可選擁有者，非空時須確認仍屬同租戶。</param>
/// <param name="GroupNodeId">可選群組節點，Group授權另需權威祖先路徑。</param>
public sealed record DataScope(Guid TenantId, Guid? OwnerContactId, Guid? GroupNodeId);
