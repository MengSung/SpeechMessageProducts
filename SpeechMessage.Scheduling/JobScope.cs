/*
檔案整體說明
目的與定位：供共用排程器持久化的產品、組織與可選租戶範圍純值，不保存登入者、LINE身分、Session或SDK連線。
主要功能與流程：排程時與固定部署邊界核對，執行時交產品scope validator重新查停用及生命週期。null租戶只表示產品明確支援的組織級工作，並非自動獲得全租戶權限。
使用邊界與維護：值物件本身不查授權；序列化只含三項純值。生命週期由SQL工作保留政策及單次執行scope界定；任何handler與validator都不能把它當成可略過伺服器查核的權限票證。
*/
namespace SpeechMessage.Scheduling;

/// <summary>排程與執行重驗用的純值範圍；不保存連線、憑證或使用者可變狀態。</summary>
/// <param name="Product">伺服器核定的產品。</param>
/// <param name="OrganizationId">受信任部署的權威組織ID。</param>
/// <param name="TenantId">可選正式租戶ID；組織級工作仍須明確授權。</param>
public sealed record JobScope(string Product, Guid OrganizationId, Guid? TenantId);
