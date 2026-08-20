// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility.Dataverse.Tests/DiagnosticTraceOptionsTests.cs
// 檔案責任：驗證三種診斷檔案共用的設定契約，以及停用時不建立任何檔案資源。
// 維護重點：這些測試保護 Release fail-closed 與 Debug 單一設定入口；不可把
//           request、Session、使用者輸入或租戶值帶入輸出目錄。
// 編碼要求：本檔案需維持 UTF-8 without BOM、CRLF 與最終 CRLF。
// ============================================================================
using System;
using System.Diagnostics;
using System.IO;
using ToolUtilityNameSpace.Dataverse;
using ToolUtilityNameSpace.Diagnostics;
using Xunit;

namespace ToolUtility.Dataverse.Tests
{
    /// <summary>
    /// 驗證統一診斷設定與停用 tracer 的副作用契約。
    /// </summary>
    public sealed class DiagnosticTraceOptionsTests
    {
        /// <summary>
        /// 保護三檔固定檔名與目錄集中契約；斷言三個路徑均源自同一個暫存目錄。
        /// </summary>
        [Fact]
        public void Disabled_options_use_one_directory_for_all_trace_files()
        {
            var directory = Path.Combine(Path.GetTempPath(), "diagnostic-options", Guid.NewGuid().ToString("N"));

            var options = DiagnosticTraceOptions.Create(directory, enabled: false);

            Assert.False(options.Enabled);
            Assert.Equal(Path.Combine(directory, "dataverse-trace.jsonl"), options.DataverseTracePath);
            Assert.Equal(Path.Combine(directory, "Trace.log"), options.TraceLogPath);
            Assert.Equal(Path.Combine(directory, "CHURCH_REPORT_TRACE.TXT"), options.ToolUtilityTracePath);
        }

        /// <summary>
        /// 保護停用模式的資源零副作用契約；故障注入是呼叫 tracer 寫入，
        /// 決定性斷言是檔案不存在且全域 listener 集合不變。
        /// </summary>
        [Fact]
        public void Null_tracer_does_not_create_file_or_global_listener()
        {
            var path = Path.Combine(Path.GetTempPath(), "null-tracer", Guid.NewGuid().ToString("N"), "trace.txt");
            var before = Trace.Listeners.Count;

            using (var tracer = new NullToolUtilityTracer())
            {
                tracer.Write(5, 1, "不應輸出", new StackFrame(0, true));
            }

            Assert.False(File.Exists(path));
            Assert.Equal(before, Trace.Listeners.Count);
        }

        /// <summary>
        /// 保護 File tracer 收到 Disabled options 時的 fail-closed 契約；
        /// 即使呼叫端直接建立實作，也不得建立檔案或註冊全域 listener。
        /// </summary>
        [Fact]
        public void Disabled_file_tracer_does_not_open_file_or_register_listener()
        {
            var directory = Path.Combine(Path.GetTempPath(), "disabled-file-tracer", Guid.NewGuid().ToString("N"));
            var options = DiagnosticTraceOptions.Create(directory, enabled: false);
            var before = Trace.Listeners.Count;

            using (var tracer = new FileToolUtilityTracer(options))
            {
                tracer.Write(5, 1, "不應輸出", new StackFrame(0, true));
            }

            Assert.False(File.Exists(options.ToolUtilityTracePath));
            Assert.Equal(before, Trace.Listeners.Count);
        }

        /// <summary>
        /// 保護 Dataverse JSONL 不得再擁有第二組啟用開關或路徑來源；故障注入是建立
        /// Enabled 的統一設定，決定性斷言是 Dataverse 選項完整沿用同一個狀態與固定路徑。
        /// </summary>
        [Fact]
        public void Dataverse_options_are_derived_from_unified_diagnostic_options()
        {
            var directory = Path.Combine(Path.GetTempPath(), "dataverse-options", Guid.NewGuid().ToString("N"));
            var diagnosticOptions = DiagnosticTraceOptions.Create(directory, enabled: true);

            var dataverseOptions = DataverseTraceOptions.FromDiagnosticOptions(diagnosticOptions);

            Assert.True(dataverseOptions.Enabled);
            Assert.Equal(diagnosticOptions.DataverseTracePath, dataverseOptions.Path);
        }

        /// <summary>
        /// 保護零使用者的 legacy logger 只能由統一設定決定是否開檔；故障注入是以停用
        /// options 呼叫 WriteLine，決定性斷言是檔案不存在且全域 listener 集合完全不變。
        /// </summary>
        [Fact]
        public void Disabled_options_keep_legacy_trace_logger_side_effect_free()
        {
            var directory = Path.Combine(Path.GetTempPath(), "disabled-legacy-logger", Guid.NewGuid().ToString("N"));
            var options = DiagnosticTraceOptions.Create(directory, enabled: false);
            var before = Trace.Listeners.Count;

            using (var logger = new TraceLogger(options))
            {
                logger.WriteLine("不應輸出");
            }

            Assert.False(File.Exists(options.ToolUtilityTracePath));
            Assert.Equal(before, Trace.Listeners.Count);
        }
    }
}
