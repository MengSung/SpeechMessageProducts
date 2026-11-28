/*
檔案整體說明
目的與定位：F5 主機啟動快照的安全政策；預設只允許明確標記的測試租戶發送。
主要功能與流程：由組合根讀取 PushSafety:TestTenantsOnly 後建構不可變選項，再注入 Gateway；
本檔不讀取環境變數、不接受 request 選項，也不把標記當成免除收件人隔離的權限。
使用邊界與維護：純不可變設定可由固定產品 owner 借用，沒有可釋放資源或跨使用者資料。
變更部署開關必須重新建立主機快照；本包實機流程不得因測試方便把預設保護關閉。
*/
namespace LineMessagingProcessor.Core.Gateway;

/// <summary>只由受信任部署組合根建立；每次 Send 仍驗租戶及 Channel 關聯。</summary>
/// <param name="TestTenantsOnly">缺省 true，只允許權威資料明確為測試的租戶。</param>
public sealed record PushSafetyOptions(bool TestTenantsOnly = true);
