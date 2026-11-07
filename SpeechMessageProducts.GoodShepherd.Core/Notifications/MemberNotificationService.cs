/*
 * 檔案整體說明
 *
 * 目的與定位：提供 GoodShepherd 產品核心通知組合服務的 P2-02 型別骨架，
 * 供後續 Plugin、Workflow 與產品主機依正式契約引用；此版本尚未承接通知行為。
 *
 * 主要功能與流程：目前僅宣告公開服務型別及無狀態建構入口，不查詢會友或 Channel、
 * 不執行教會授權／語系選擇／訊息組裝、不解密 Token，也不呼叫 LINE。完整通知流程
 * 須待 P2-04 的查詢、傳輸及失敗契約確立後，才以測試先行方式加入。
 *
 * 使用邊界與維護：本型別不保存 Session、身分、憑證、教會資料、可變快取或外部 client，
 * 不建立背景工作及可 Dispose 資源，因此沒有跨請求資料保留或清理責任。呼叫端目前
 * 不得將建構成功視為通知可用；P2-04 必須新增教會隔離、Retry Key、取消傳遞及失敗
 * 保真測試，並更新本說明後再提供實際操作。
 */
namespace SpeechMessageProducts.GoodShepherd.Core.Notifications;

/// <summary>
/// GoodShepherd 單一會友通知組合服務的公開骨架；目前不執行查詢、授權、語系處理或傳輸。
/// </summary>
public sealed class MemberNotificationService
{
    /// <summary>
    /// 建立尚未啟用通知行為的無狀態骨架。P2-04 確立資料查詢與傳輸契約後，才擴充依賴。
    /// </summary>
    public MemberNotificationService()
    {
    }
}
