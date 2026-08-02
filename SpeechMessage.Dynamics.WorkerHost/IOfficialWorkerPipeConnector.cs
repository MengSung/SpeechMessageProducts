using System.IO;

namespace SpeechMessage.Dynamics.WorkerHost;

/// <summary>
/// 定義官方 CRM Worker 連回本機 Gateway 監督程序之具名管線邊界。
/// 實作只能建立一條由呼叫端獨占的雙向資料流；成功回傳後由
/// <see cref="OfficialWorkerProcessHost"/> 成為唯一釋放擁有者。
/// </summary>
public interface IOfficialWorkerPipeConnector
{
    /// <summary>
    /// 連接指定的本機具名管線，並將資料流所有權移交給呼叫端。
    /// 實作必須使用有限等待時間；連線失敗時不得留下未釋放的管線控制代碼。
    /// </summary>
    /// <param name="pipeName">已由 bootstrap 契約驗證的非機密本機管線名稱。</param>
    /// <returns>由呼叫端唯一擁有並負責釋放的雙向資料流。</returns>
    Stream Connect(string pipeName);
}
