// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/Diagnostics/TraceLogger.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案提供 legacy 私有 writer 與效能計時器；不得註冊全域 Trace listener。
// 主要型別：interface ITraceLogger、class TraceLogger、class PerformanceMonitor
// 主要成員：Write、WriteLine、WriteError、Dispose、Stop
// 引用命名空間：System、System.Diagnostics、System.IO、System.Text
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace ToolUtilityNameSpace.Diagnostics
{
    /// <summary>
    /// 定義 legacy ToolUtility 訊息的最小寫入與確定性釋放契約。
    /// </summary>
    /// <remarks>
    /// 實作只能由程序級擁有者持有，不得把 request、Session、Claims、租戶或憑證保存於
    /// logger 欄位。呼叫端不得把密碼、token、完整身分資料或其他敏感值組成訊息；停用實作
    /// 必須在建立檔案資源前快速返回，且 <see cref="Dispose"/> 必須可重複呼叫。
    /// </remarks>
    public interface ITraceLogger
    {
        /// <summary>寫入不自動換行的追蹤訊息；停用或已釋放時為空操作。</summary>
        /// <param name="message">不含機密、token 或完整個人資料的診斷訊息。</param>
        void Write(string message);

        /// <summary>加上本機時間戳後寫入一行追蹤訊息；停用或已釋放時為空操作。</summary>
        /// <param name="message">不含機密、token 或完整個人資料的診斷訊息。</param>
        void WriteLine(string message);

        /// <summary>將例外型別、訊息與堆疊寫入診斷檔；呼叫端須先確保內容不含敏感資料。</summary>
        /// <param name="exception">要記錄的例外；為 <see langword="null"/> 時不輸出。</param>
        /// <param name="context">可選的非敏感操作脈絡，不得包含 request/使用者識別或憑證。</param>
        void WriteError(Exception exception, string context = null);

        /// <summary>停止後續寫入並確定性 Flush/Dispose 私有 writer 與 stream；方法必須冪等。</summary>
        void Dispose();
    }

    /// <summary>
    /// 使用延遲初始化私有 Big5 writer 的 legacy 程序級追蹤器。
    /// </summary>
    /// <remarks>
    /// 本型別不加入程序全域 <see cref="Trace.Listeners"/>，因此不會把 ToolUtility 訊息複製到
    /// <c>Trace.log</c>。檔案最長存活到 DI singleton 或明確 owner 呼叫 <see cref="Dispose"/>；
    /// Dispose 先以原子旗標停止新寫入，再在同一把鎖內 Flush/Dispose。Release 編譯與停用設定
    /// 都不建立目錄、stream 或 writer，避免部署設定繞過編譯期防線。
    /// </remarks>
    public class TraceLogger : ITraceLogger, IDisposable
    {
        private readonly string _logFilePath;
        private readonly Lazy<FileStream> _lazyFileStream;
        private readonly Lazy<StreamWriter> _lazyStreamWriter;
        private readonly bool _enabled;
        private readonly object _writeLock = new object();
        private int _disposed;

        /// <summary>
        /// 建立預設停用的相容 logger；此建構式不推測路徑，也不建立任何檔案資源。
        /// </summary>
        /// <remarks>
        /// 正常產品組合根應傳入 <see cref="DiagnosticTraceOptions"/>。保留無參數建構式只為
        /// 避免舊呼叫端在升級時直接中斷，但 fail-closed 行為可防止繞過單一設定入口。
        /// </remarks>
        public TraceLogger()
            : this(logFilePath: null, enabled: false)
        {
        }

        /// <summary>以統一設定建立 legacy logger；停用時不解析路徑或建立任何檔案。</summary>
        /// <param name="options">由可信任組合根建立的程序級診斷設定。</param>
        public TraceLogger(DiagnosticTraceOptions options)
            : this(options?.ToolUtilityTracePath, options?.Enabled ?? false)
        {
        }

        /// <summary>
        /// 以明確路徑建立測試或 legacy 相容 logger；只有 Debug 編譯會實際寫入。
        /// </summary>
        /// <param name="logFilePath">由可信任程式碼提供的完整日誌檔案路徑。</param>
        public TraceLogger(string logFilePath)
            : this(logFilePath, enabled: !string.IsNullOrWhiteSpace(logFilePath))
        {
        }

        private TraceLogger(string logFilePath, bool enabled)
        {
            _enabled = IsCompileTimeTraceEnabled() && enabled;
            _logFilePath = _enabled
                ? Path.GetFullPath(logFilePath)
                : string.Empty;

            // Lazy 初始化 FileStream
            _lazyFileStream = new Lazy<FileStream>(() =>
                CreateFileStream());

            // Lazy 初始化 StreamWriter
            _lazyStreamWriter = new Lazy<StreamWriter>(() =>
            {
#if !NET462 && !NETFRAMEWORK
                // 註冊編碼提供者（僅在 .NET 5+ 需要）
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
#endif
                return new StreamWriter(
                    _lazyFileStream.Value,
                    Encoding.GetEncoding("big5"),
                    4096,
                    leaveOpen: true);
            });
        }

        /// <summary>將訊息直接寫入私有 writer；停用、空訊息或已釋放時不配置檔案資源。</summary>
        /// <param name="message">不含敏感資料且不需自動換行的診斷文字。</param>
        public void Write(string message)
        {
            try
            {
                if (!_enabled || Volatile.Read(ref _disposed) != 0 || string.IsNullOrEmpty(message))
                    return;

                WritePrivate(message, appendNewLine: false);
            }
            catch (Exception ex)
            {
                // 避免追蹤本身造成應用程式崩潰
                Debug.WriteLine($"[TraceLogger] Write failed: {ex.Message}");
            }
        }

        /// <summary>加上毫秒時間戳後寫入一行；每次寫入立即 Flush，方便當機前保留證據。</summary>
        /// <param name="message">不含敏感資料的單筆診斷文字。</param>
        public void WriteLine(string message)
        {
            try
            {
                if (!_enabled || Volatile.Read(ref _disposed) != 0 || string.IsNullOrEmpty(message))
                    return;

                var timestampedMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
                WritePrivate(timestampedMessage, appendNewLine: true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TraceLogger] WriteLine failed: {ex.Message}");
            }
        }

        /// <summary>輸出例外診斷區塊；診斷失敗只寫入 Debug，不得影響主要工作流程。</summary>
        /// <param name="exception">要記錄的例外；為 <see langword="null"/> 時不輸出。</param>
        /// <param name="context">選用的非敏感操作脈絡。</param>
        public void WriteError(Exception exception, string context = null)
        {
            try
            {
                if (exception == null)
                    return;

                var errorMessage = new StringBuilder();
                errorMessage.AppendLine("========== ERROR ==========");
                errorMessage.AppendLine($"時間: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");

                if (!string.IsNullOrWhiteSpace(context))
                {
                    errorMessage.AppendLine($"上下文: {context}");
                }

                errorMessage.AppendLine($"例外類型: {exception.GetType().FullName}");
                errorMessage.AppendLine($"錯誤訊息: {exception.Message}");
                errorMessage.AppendLine($"堆疊追蹤: {exception.StackTrace}");

                if (exception.InnerException != null)
                {
                    errorMessage.AppendLine("--- Inner Exception ---");
                    errorMessage.AppendLine($"類型: {exception.InnerException.GetType().FullName}");
                    errorMessage.AppendLine($"訊息: {exception.InnerException.Message}");
                }

                errorMessage.AppendLine("===========================");

                WriteLine(errorMessage.ToString());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TraceLogger] WriteError failed: {ex.Message}");
            }
        }

        #region IDisposable 實現

        /// <summary>停止後續輸出並確定性釋放私有 writer/stream；重複呼叫為空操作。</summary>
        public void Dispose()
        {
            Dispose(disposing: true);
        }

        /// <summary>供衍生型別共用的 Dispose 核心；只有明確 managed cleanup 才釋放檔案資源。</summary>
        /// <param name="disposing">是否由公開 Dispose 路徑釋放 managed 資源。</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing || Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            lock (_writeLock)
            {
                try
                {
                    if (_lazyStreamWriter.IsValueCreated)
                    {
                        _lazyStreamWriter.Value.Flush();
                        _lazyStreamWriter.Value.Dispose();
                    }

                    if (_lazyFileStream.IsValueCreated)
                    {
                        _lazyFileStream.Value.Flush();
                        _lazyFileStream.Value.Dispose();
                    }
                }
                catch (ObjectDisposedException) { }
                catch (IOException) { }
            }
        }

        private void WritePrivate(string message, bool appendNewLine)
        {
            lock (_writeLock)
            {
                if (Volatile.Read(ref _disposed) != 0)
                {
                    return;
                }

                if (appendNewLine)
                {
                    _lazyStreamWriter.Value.WriteLine(message);
                }
                else
                {
                    _lazyStreamWriter.Value.Write(message);
                }

                _lazyStreamWriter.Value.Flush();
            }
        }

        private FileStream CreateFileStream()
        {
            var directory = Path.GetDirectoryName(_logFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            return new FileStream(
                _logFilePath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite | FileShare.Delete);
        }

        private static bool IsCompileTimeTraceEnabled()
        {
#if DEBUG
            return true;
#else
            return false;
#endif
        }

        #endregion
    }

    /// <summary>
    /// 測量單一同步操作耗時，並透過呼叫端提供的 logger 記錄開始與完成事件。
    /// </summary>
    /// <remarks>
    /// 此物件只保存操作名稱、<see cref="Stopwatch"/> 與 logger 參考，預期由單一工作流程以
    /// <see langword="using"/> 管理；它不建立 timer、背景 task 或 request 快取。Dispose 只停止
    /// 計時，不擁有也不會釋放注入的 logger，避免短命操作提前關閉程序級 writer。
    /// </remarks>
    public class PerformanceMonitor : IDisposable
    {
        private readonly Stopwatch _stopwatch;
        private readonly string _operationName;
        private readonly ITraceLogger _logger;
        private bool _disposed = false;

        /// <summary>建立監控器、立即開始計時，並在 logger 可用時記錄開始事件。</summary>
        /// <param name="operationName">非敏感操作名稱；空值會使用 <c>Unknown Operation</c>。</param>
        /// <param name="logger">由外部擁有的 logger；本監控器不負責釋放。</param>
        public PerformanceMonitor(string operationName, ITraceLogger logger)
        {
            _operationName = operationName ?? "Unknown Operation";
            _logger = logger;
            _stopwatch = Stopwatch.StartNew();

            _logger?.WriteLine($"[Performance] {_operationName} - 開始執行");
        }

        /// <summary>只在計時仍執行時停止一次，並記錄總毫秒數；重複呼叫不重複輸出。</summary>
        public void Stop()
        {
            if (_stopwatch.IsRunning)
            {
                _stopwatch.Stop();
                _logger?.WriteLine($"[Performance] {_operationName} - 完成執行 (耗時: {_stopwatch.ElapsedMilliseconds} ms)");
            }
        }

        /// <summary>停止計時但不釋放外部 logger；重複呼叫為空操作。</summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                Stop();
                _disposed = true;
            }
        }
    }
}
