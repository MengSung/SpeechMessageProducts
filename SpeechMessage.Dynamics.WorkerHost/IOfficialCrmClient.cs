using System;
using SpeechMessage.Dynamics.WorkerProtocol;

namespace SpeechMessage.Dynamics.WorkerHost;

/// <summary>
/// 定義單一 Official Worker process 內、單一 Profile generation 專用的 CRM client 邊界。
/// 實作不得跨 Profile、CE 版本、credential 或要求共享可變 Session 狀態；呼叫端是唯一
/// <see cref="IDisposable"/> owner，必須在 Worker drain、fault 或 process 結束前確定釋放底層 SDK 資源。
/// </summary>
public interface IOfficialCrmClient : IDisposable
{
    /// <summary>
    /// 取得啟動身分與 Organization／CE 版本均已驗證的就緒狀態；未就緒時不得執行商業 operation。
    /// </summary>
    bool IsReady { get; }

    /// <summary>
    /// 在目前 Worker-owned client 上執行已通過 IPC allowlist 與 deadline 驗證的具名要求。
    /// 回傳值只能使用 bounded <see cref="WorkerValue"/>，不得帶出 SDK Entity、credential、endpoint 或 Session。
    /// </summary>
    /// <param name="request">已由 Worker protocol 驗證且綁定目前 process nonce 的要求。</param>
    /// <returns>去除 SDK 型別與敏感路由資訊後的 bounded 結果。</returns>
    WorkerValue Execute(WorkerRequestV1 request);
}
