using System;
using System.Security;
using System.Threading;

namespace SpeechMessage.Dynamics.WorkerHost;

/// <summary>
/// 擁有一個官方 CRM client 世代所需的 Windows 服務帳號與唯讀 <see cref="SecureString"/>。
/// 使用者名稱與網域是非機密路由外資料；密碼只有這個物件擁有，且必須保留到
/// <c>CrmServiceClient</c> 已停止使用並完成 Dispose 後才可清除，讓 IFD reconnect 仍能使用同一受控秘密。
/// XRM Tooling 或 <c>NetworkCredential</c> 可能建立無法經公開 API 清除的內部複本，因此 Worker process exit
/// 是該 SDK 內部狀態的最終清理邊界。此物件本身不得進入快取、IPC、例外或 telemetry。
/// </summary>
public sealed class OfficialCrmCredential : IDisposable
{
    private SecureString? _password;

    /// <summary>
    /// 建立 credential 的唯一 managed secret owner。密碼必須已設為唯讀，
    /// 使 Worker client 的完整存活期間不能意外改寫同一份 credential 世代。
    /// </summary>
    internal OfficialCrmCredential(
        string userName,
        string domain,
        SecureString password)
    {
        UserName = userName ?? throw new ArgumentNullException(nameof(userName));
        Domain = domain ?? throw new ArgumentNullException(nameof(domain));
        _password = password ?? throw new ArgumentNullException(nameof(password));
        if (!password.IsReadOnly())
        {
            throw new ArgumentException(
                "The official CRM credential password must be read-only.",
                nameof(password));
        }
    }

    /// <summary>
    /// 取得服務帳號使用者名稱。它不得作為跨產品、跨 Session 或跨 profile 的快取鍵。
    /// </summary>
    public string UserName { get; }

    /// <summary>
    /// 取得選擇性的 Windows 網域；UPN 身分的網域為空字串。
    /// </summary>
    public string Domain { get; }

    /// <summary>
    /// 取得目前仍由本物件擁有的唯讀密碼。Dispose 後存取會失敗，
    /// adapter 必須先停止並 Dispose CRM client，再釋放此 managed secret owner；反向順序可能破壞
    /// XRM Tooling 的 reconnect 路徑，並留下難以診斷的半清理狀態。
    /// </summary>
    public SecureString Password =>
        Volatile.Read(ref _password) ??
        throw new ObjectDisposedException(nameof(OfficialCrmCredential));

    /// <summary>
    /// 以 idempotent 方式釋放 <see cref="SecureString"/>；只有第一次呼叫取得釋放所有權，
    /// 後續呼叫不會重複釋放，也不會重新發佈密碼 reference。正常生命週期只會在 CRM client
    /// Dispose 後呼叫；若 SDK 呼叫或 Dispose 卡死，Supervisor 會有界終止 Worker process，清除 SDK 內部複本。
    /// </summary>
    public void Dispose()
    {
        Interlocked.Exchange(ref _password, null)?.Dispose();
    }
}
