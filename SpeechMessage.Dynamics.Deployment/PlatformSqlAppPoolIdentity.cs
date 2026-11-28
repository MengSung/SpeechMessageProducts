/*
檔案整體說明
目的與定位：P0／F2 平台 SQL 唯讀驗收的 Windows 身分 owner；避免在 IIS 虛擬帳號另啟 CLR 的主機初始化限制，被誤認為 SQL 驗收結果。
主要功能與流程：只在 Windows 測試主機 D365APP01，最多檢查三十二個本機 w3wp 程序的 token SID；必須恰好一個符合固定 GoodShepherd2Test 集區。複製短期 impersonation token，在既有已啟動 CLI 中以 RunImpersonatedAsync 完整等待單次固定唯讀身分查詢，進入時再核對實際 SID。沒有任意 PID、帳號、密碼或產品路由；無符合或多符合均拒絕，絕不退回原管理員身分查 SQL。
使用邊界與維護：本檔不修改 IIS、帳號權限或 D365，不讀工作程序環境與秘密，不建立子程序、背景工作或全域身分快取。原生來源 token、WindowsIdentity 與所有 Process owner 在掃描結束釋放；唯一複製 token 的租約只活至 awaited 查詢完成、取消或失敗，using 確定 Dispose。外層 CLI 擁有十五秒期限及 SQL 連線，身分範圍必須涵蓋 OpenAsync 至連線 Dispose；不可把未完成 Task 移到範圍外。所有錯誤保留真 Exception 交 Program 共用診斷，不輸出使用者資料或 Windows 錯誤本文。
驗證方式：合成身分 owner 驗證 await、原例外、取消及 Dispose；真正 SID 選取、Windows impersonation 與 Strict SELECT 1 必須在持鎖測試 VM 額外驗收，不能以合成案例代替。
*/
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace SpeechMessage.Dynamics.Deployment;

/// <summary>單次身分 owner 的最小測試接縫；查詢完成前不得 Dispose 或逸出未等待的 Task。</summary>
internal interface IPlatformSqlIdentityScope : IDisposable
{
    /// <summary>在此 owner 的身分內完整等待借用查詢；token 仍屬外層，不自行重試或建立工作。</summary>
    Task<int> ExecuteAsync(Func<CancellationToken, Task<int>> query, CancellationToken token);
}

/// <summary>只供固定測試 VM 的短生命週期 Windows token owner，不對 HTTP 或一般產品開放。</summary>
internal static class PlatformSqlAppPoolIdentity
{
    /// <summary>確保 SQL 與身分租約同生同滅；已取消的 caller 不取得任何 token，取得後取消仍釋放。</summary>
    internal static async Task<int> RunAsync(Func<CancellationToken, Task<int>> query, CancellationToken token,
        Func<IPlatformSqlIdentityScope>? acquire = null)
    {
        token.ThrowIfCancellationRequested();
        using var scope = acquire is null ? Acquire(token) : acquire();
        token.ThrowIfCancellationRequested();
        return await scope.ExecuteAsync(query, token).ConfigureAwait(false);
    }

    /// <summary>先驗固定主機，再取得恰好一個相符 SID 的 token；任何掃描故障都清理候選並拒絕。</summary>
    private static WindowsIdentityScope Acquire(CancellationToken token)
    {
        if (!OperatingSystem.IsWindows() || !string.Equals(Environment.MachineName, "D365APP01", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The fixed test VM is required for app-pool identity verification.");
        return CaptureWindowsToken(token);
    }

    /// <summary>所有 Process 及來源 token 由本方法釋放；只有唯一相符的複製 token 可移交給租約。</summary>
    [SupportedOSPlatform("windows")]
    private static WindowsIdentityScope CaptureWindowsToken(CancellationToken cancellationToken)
    {
        var expected = (SecurityIdentifier)new NTAccount(@"IIS APPPOOL\GoodShepherd2Test").Translate(typeof(SecurityIdentifier));
        var workers = Process.GetProcessesByName("w3wp");
        SafeAccessTokenHandle? selected = null;
        try
        {
            if (workers.Length > 32) throw new InvalidOperationException("The test worker inspection limit was exceeded.");
            foreach (var worker in workers)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var process = OpenProcess(0x1000, false, worker.Id);
                if (process.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
                if (!OpenProcessToken(process, 0x000A, out var source))
                { var error = Marshal.GetLastWin32Error(); source.Dispose(); throw new Win32Exception(error); }
                using (source)
                using (var identity = new WindowsIdentity(source.DangerousGetHandle()))
                {
                    if (identity.User != expected) continue;
                    if (selected is not null) throw new InvalidOperationException("The fixed app-pool worker is not unique.");
                    if (!DuplicateTokenEx(source, 0x000E, IntPtr.Zero, 2, 2, out var duplicate))
                    { var error = Marshal.GetLastWin32Error(); duplicate.Dispose(); throw new Win32Exception(error); }
                    selected = duplicate;
                }
            }
            if (selected is null) throw new InvalidOperationException("The fixed app-pool worker was not found.");
            var result = new WindowsIdentityScope(selected, expected);
            selected = null;
            return result;
        }
        finally
        {
            selected?.Dispose();
            foreach (var worker in workers) worker.Dispose();
        }
    }

    /// <summary>只取得查詢程序身分所需的最低權限；不開啟寫入、注入、終止或建立程序權限。</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int id);

    /// <summary>只讀本機來源程序 token；SafeHandle 避免在原生呼叫期間提早關閉程序 owner。</summary>
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);

    /// <summary>複製有界 impersonation token；不取得 primary-token 的建立程序權限。</summary>
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DuplicateTokenEx(SafeAccessTokenHandle token, uint access, IntPtr attributes,
        int level, int type, out SafeAccessTokenHandle duplicate);

    /// <summary>唯一複製 token owner；只允許單次已驗 SID 內的 awaited 查詢，Dispose 關閉原生 handle。</summary>
    [SupportedOSPlatform("windows")]
    private sealed class WindowsIdentityScope(SafeAccessTokenHandle token, SecurityIdentifier expected) : IPlatformSqlIdentityScope
    {
        /// <summary>身分隨 ExecutionContext 跨 await 保持，回到 caller 時自動恢復；查詢內新連線不使用池。</summary>
        public Task<int> ExecuteAsync(Func<CancellationToken, Task<int>> query, CancellationToken cancellationToken) =>
            WindowsIdentity.RunImpersonatedAsync(token, async () =>
            {
                using var current = WindowsIdentity.GetCurrent();
                if (current.User != expected) throw new InvalidOperationException("The app-pool identity boundary was not established.");
                cancellationToken.ThrowIfCancellationRequested();
                return await query(cancellationToken).ConfigureAwait(false);
            });

        /// <summary>在 await 完成後確定釋放唯一 token；SafeHandle 本身支援重入 Dispose，不保留原生權限。</summary>
        public void Dispose() => token.Dispose();
    }
}
