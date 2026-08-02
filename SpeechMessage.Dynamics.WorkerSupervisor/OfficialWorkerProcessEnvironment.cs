using System.Collections;
using System.Diagnostics;

namespace SpeechMessage.Dynamics.WorkerSupervisor;

/// <summary>
/// 建立 official Worker child process 唯一允許繼承的最小 Windows／.NET runtime 環境。
/// 此類別不保存父行程 environment snapshot；每次啟動只在目前 stack scope 逐項複製 allowlist，
/// 並再次移除 credential／token／session 類名稱，避免父行程的 mutable 身分狀態跨 generation 洩漏。
/// </summary>
internal static class OfficialWorkerProcessEnvironment
{
    /// <summary>
    /// 清空 <paramref name="startInfo" /> 的預設完整環境繼承，再複製啟動 Windows apphost、
    /// .NET Framework CLR 與同一服務帳號 Credential Manager 所需的最小 OS 變數。方法不複製
    /// 任意應用程式變數，也不讀取或記錄任何秘密值；返回後唯一 owner 是即將啟動的 ProcessStartInfo。
    /// </summary>
    /// <param name="startInfo">尚未啟動且由本次 Worker generation 獨占的 process 設定。</param>
    public static void Configure(ProcessStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        startInfo.Environment.Clear();

        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is not string name ||
                entry.Value is not string value ||
                string.IsNullOrEmpty(value) ||
                !IsMinimumRuntimeVariable(name) ||
                IsSensitiveVariable(name))
            {
                continue;
            }

            startInfo.Environment[name] = value;
        }

        // Defense in depth：即使未來 allowlist 擴充，也不能讓敏感形狀的名稱留下。
        foreach (var name in startInfo.Environment.Keys.ToArray())
        {
            if (IsSensitiveVariable(name))
            {
                startInfo.Environment.Remove(name);
            }
        }
    }

    /// <summary>
    /// 判斷 Worker 啟動時可保留的固定 OS/runtime 名稱。清單刻意不包含任何 SpeechMessage、
    /// Dynamics、CRM、HTTP、Session 或產品設定；新增項目前必須以真實 Worker startup gate 證明必要性。
    /// </summary>
    private static bool IsMinimumRuntimeVariable(string name) =>
        name.Equals("SystemRoot", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("WINDIR", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("ComSpec", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("PATH", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("PATHEXT", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("TEMP", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("TMP", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("USERPROFILE", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("HOMEDRIVE", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("HOMEPATH", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("APPDATA", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("LOCALAPPDATA", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("ProgramData", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("USERNAME", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("USERDOMAIN", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("USERDOMAIN_ROAMINGPROFILE", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("COMPUTERNAME", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("NUMBER_OF_PROCESSORS", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("PROCESSOR_ARCHITECTURE", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("PROCESSOR_IDENTIFIER", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("PROCESSOR_LEVEL", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("PROCESSOR_REVISION", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("OS", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("DOTNET_ROOT", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("DOTNET_ROOT_X64", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("DOTNET_ROOT_X86", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 判斷 credential、token、connection、session 與產品整合狀態的名稱形狀。此檢查是 allowlist
    /// 之外的第二層保護，避免未來維護時誤把秘密或 caller/session bridge 加入 child environment。
    /// </summary>
    private static bool IsSensitiveVariable(string name) =>
        name.Contains("DYNAMICS", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("CRM", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("CREDENTIAL", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("PASSWORD", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("PASSWD", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("SECRET", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("TOKEN", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("AUTH", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("CONNECTION", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("CONNSTR", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("SQL", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("KEY", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("SESSION", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("COOKIE", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("PRINCIPAL", StringComparison.OrdinalIgnoreCase);
}
