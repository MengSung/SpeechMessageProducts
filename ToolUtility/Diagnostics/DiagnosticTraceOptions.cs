// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/Diagnostics/DiagnosticTraceOptions.cs
// 檔案責任：定義三種診斷檔案的單一組態入口與固定檔名契約。
// 生命週期責任：本型別只保存已驗證的程序級路徑與啟用狀態，不建立目錄、檔案、
//               writer、timer 或背景工作；檔案資源由各自的 singleton owner 管理。
// 安全邊界：Release 組合根傳入 allowEnabled=false，任何 appsettings 或環境變數
//           都不能逆轉停用結果；路徑不接受 request、Session 或使用者輸入。
// 編碼要求：本檔案需維持 UTF-8 without BOM、CRLF 與最終 CRLF。
// ============================================================================
using System;
using System.IO;
using Microsoft.Extensions.Configuration;

namespace ToolUtilityNameSpace.Diagnostics
{
    /// <summary>
    /// 三種診斷輸出的程序級統一設定。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 這個型別是設定資料，不是檔案資源擁有者。它只在啟動時解析一次，之後由
    /// DI 以 singleton 共享；任何 request、Session、Claims、tenant 或 credential
    /// 都不得進入這裡。真正的 writer、stream、listener 與背景 task 仍由各自的
    /// singleton owner 確定性 Dispose。
    /// </para>
    /// <para>
    /// Release 的組合根必須以 <c>allowEnabled: false</c> 呼叫
    /// <see cref="FromConfiguration(IConfiguration, string, bool)"/>，形成不能由部署
    /// 設定繞過的第二層 fail-closed 防線。
    /// </para>
    /// </remarks>
    public sealed class DiagnosticTraceOptions
    {
        /// <summary>預設的診斷資料夾；正式環境仍由 Release 硬性停用保護。</summary>
        public const string DefaultDirectory = @"D:\除錯追蹤";

        /// <summary>固定的 Dataverse JSONL 檔名。</summary>
        public const string DataverseTraceFileName = "dataverse-trace.jsonl";

        /// <summary>固定的 ASP.NET/效能 Trace 檔名。</summary>
        public const string TraceLogFileName = "Trace.log";

        /// <summary>固定的 legacy ToolUtility Trace 檔名。</summary>
        public const string ToolUtilityTraceFileName = "CHURCH_REPORT_TRACE.TXT";

        private DiagnosticTraceOptions(string directory, bool enabled)
        {
            Directory = directory;
            Enabled = enabled;
            DataverseTracePath = Path.Combine(directory, DataverseTraceFileName);
            TraceLogPath = Path.Combine(directory, TraceLogFileName);
            ToolUtilityTracePath = Path.Combine(directory, ToolUtilityTraceFileName);
        }

        /// <summary>取得是否允許本次程序建立三種診斷 writer。</summary>
        public bool Enabled { get; }

        /// <summary>取得已解析且未包含檔名的診斷資料夾。</summary>
        public string Directory { get; }

        /// <summary>取得 Dataverse JSONL 完整路徑。</summary>
        public string DataverseTracePath { get; }

        /// <summary>取得應用程式 Trace 完整路徑。</summary>
        public string TraceLogPath { get; }

        /// <summary>取得 legacy ToolUtility Trace 完整路徑。</summary>
        public string ToolUtilityTracePath { get; }

        /// <summary>
        /// 建立供測試或已解析組合根使用的設定，不會建立目錄或檔案。
        /// </summary>
        /// <param name="directory">可信任的程序級診斷目錄。</param>
        /// <param name="enabled">是否允許 writer；Release 組合根不得直接傳入 true。</param>
        /// <returns>包含固定三個檔名的設定物件。</returns>
        public static DiagnosticTraceOptions Create(string directory, bool enabled)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new ArgumentException("診斷目錄不得為空白。", nameof(directory));
            }

            var fullDirectory = Path.GetFullPath(directory.Trim());
            if (Path.GetPathRoot(fullDirectory) == null)
            {
                throw new ArgumentException("診斷目錄必須是可解析的檔案系統路徑。", nameof(directory));
            }

            return new DiagnosticTraceOptions(fullDirectory, enabled);
        }

        /// <summary>
        /// 從可信任啟動組態建立設定；<paramref name="allowEnabled"/> 是 Release 防線。
        /// </summary>
        /// <param name="configuration">ASP.NET Core 組態來源。</param>
        /// <param name="contentRootPath">應用程式 content root，用於解析相對路徑。</param>
        /// <param name="allowEnabled">只有 Debug 組合根可傳入 true。</param>
        /// <returns>已解析的統一設定。</returns>
        public static DiagnosticTraceOptions FromConfiguration(
            IConfiguration configuration,
            string contentRootPath,
            bool allowEnabled)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            if (string.IsNullOrWhiteSpace(contentRootPath))
            {
                throw new ArgumentException("Content root 不得為空白。", nameof(contentRootPath));
            }

            var section = configuration.GetSection("DiagnosticsTrace");
            var configuredDirectory = section["Directory"];
            if (string.IsNullOrWhiteSpace(configuredDirectory))
            {
                configuredDirectory = DefaultDirectory;
            }

            var directory = Path.IsPathRooted(configuredDirectory)
                ? configuredDirectory
                : Path.Combine(contentRootPath, configuredDirectory);

            var configuredEnabled = section.GetValue("Enabled", false);
            return Create(directory, allowEnabled && configuredEnabled);
        }

        /// <summary>
        /// 建立明確停用的設定；不讀取外部啟用值，供 Release 組合根使用。
        /// </summary>
        /// <param name="contentRootPath">應用程式 content root；只用於相容的預設路徑解析。</param>
        /// <returns>Enabled 永遠為 false 的設定。</returns>
        public static DiagnosticTraceOptions CreateDisabled(string contentRootPath)
        {
            if (string.IsNullOrWhiteSpace(contentRootPath))
            {
                throw new ArgumentException("Content root 不得為空白。", nameof(contentRootPath));
            }

            return Create(DefaultDirectory, enabled: false);
        }
    }
}
