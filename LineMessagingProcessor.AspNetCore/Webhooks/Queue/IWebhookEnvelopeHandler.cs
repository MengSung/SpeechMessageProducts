/*
檔案整體說明
目的與定位：定義有界 Webhook 背景佇列與 P3-06 事件分派器之間的窄介面，不包含業務規則或預設成功處理器。
主要功能與流程：queue 每筆建立獨立非同步 DI scope，將已驗簽的不可變 envelope 與事件專用取消訊號交給 handler，完整等待工作完成後才關閉 scope。
使用邊界與維護：handler 不得把 envelope、使用者、憑證或 scoped 依賴保存至 singleton；所有子工作必須在回傳前排空。必須合作回應三十秒事件期限與 Host 取消，DisposeAsync 亦須有限完成；queue 不會以遺棄 task 冒充終止。真正失敗拋原例外交 queue 共用診斷入口，正常取消拋取消例外。正式 Host 缺此介面必須拒絕啟動。
*/
namespace LineMessagingProcessor.AspNetCore.Webhooks.Queue;

/// <summary>一次事件、一次 scope 的分派邊界；實作必須等待自己的全部子工作，不得保存跨使用者狀態或 fire-and-forget。</summary>
public interface IWebhookEnvelopeHandler
{
    /// <summary>處理已解析事件；三十秒期限或停止取消時必須合作結束，回傳後才允許 queue 釋放 scope 及容量。</summary>
    /// <param name="envelope">本筆已驗簽頻道與不可變事件，只借用至工作完成。</param>
    /// <param name="cancellationToken">本筆獨立期限與 Host 強制排空訊號，不能保存或轉交未觀察的工作。</param>
    /// <returns>包含全部子操作的可觀察工作；真正失敗保留原始例外。</returns>
    ValueTask HandleAsync(WebhookEnvelope envelope, CancellationToken cancellationToken);
}
