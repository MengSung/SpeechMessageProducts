// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/Diagnostics/TraceLogger.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
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

namespace ToolUtilityNameSpace.Diagnostics
{
    /// <summary>
    /// 追蹤日誌介面
    /// 遵循 Interface Segregation Principle (ISP)
    /// </summary>
    public interface ITraceLogger
    {
        /// <summary>寫入追蹤訊息</summary>
        void Write(string message);

        /// <summary>寫入追蹤訊息並換行</summary>
        void WriteLine(string message);

        /// <summary>寫入錯誤訊息</summary>
        void WriteError(Exception exception, string context = null);

        /// <summary>清理資源</summary>
        void Dispose();
    }

    /// <summary>
    /// 追蹤日誌實現
    /// 使用 Lazy<T> 延遲初始化優化效能
    /// 遵循 Dispose Pattern 確保資源正確釋放
    /// </summary>
    public class TraceLogger : ITraceLogger, IDisposable
    {
        private readonly string _logFilePath;
        private readonly Lazy<FileStream> _lazyFileStream;
        private readonly Lazy<StreamWriter> _lazyStreamWriter;
        private readonly Lazy<TextWriterTraceListener> _lazyListener;
        private bool _disposed = false;

        /// <summary>
        /// 建構函數
        /// </summary>
        /// <param name="logFilePath">日誌檔案路徑</param>
        public TraceLogger(string logFilePath = @"D:\除錯追蹤\CHURCH_REPORT_TRACE.TXT")
        {
            _logFilePath = logFilePath ?? throw new ArgumentNullException(nameof(logFilePath));

            // 確保目錄存在
            var directory = Path.GetDirectoryName(_logFilePath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Lazy 初始化 FileStream
            _lazyFileStream = new Lazy<FileStream>(() =>
                new FileStream(_logFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite));

            // Lazy 初始化 StreamWriter
            _lazyStreamWriter = new Lazy<StreamWriter>(() =>
            {
#if !NET462 && !NETFRAMEWORK
                // 註冊編碼提供者（僅在 .NET 5+ 需要）
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
#endif
                return new StreamWriter(_lazyFileStream.Value, Encoding.GetEncoding("big5"));
            });

            // Lazy 初始化 TraceListener
            _lazyListener = new Lazy<TextWriterTraceListener>(() =>
            {
                var listener = new TextWriterTraceListener(_lazyStreamWriter.Value);
                Trace.AutoFlush = true;
                Trace.Listeners.Add(listener);
                return listener;
            });
        }

        /// <summary>
        /// 寫入追蹤訊息
        /// </summary>
        public void Write(string message)
        {
            try
            {
                if (string.IsNullOrEmpty(message))
                    return;

                // 確保 Listener 已初始化
                var listener = _lazyListener.Value;
                Trace.Write(message);
            }
            catch (Exception ex)
            {
                // 避免追蹤本身造成應用程式崩潰
                Debug.WriteLine($"[TraceLogger] Write failed: {ex.Message}");
            }
        }

        /// <summary>
        /// 寫入追蹤訊息並換行
        /// </summary>
        public void WriteLine(string message)
        {
            try
            {
                if (string.IsNullOrEmpty(message))
                    return;

                // 確保 Listener 已初始化
                var listener = _lazyListener.Value;

                var timestampedMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
                Trace.WriteLine(timestampedMessage);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TraceLogger] WriteLine failed: {ex.Message}");
            }
        }

        /// <summary>
        /// 寫入錯誤訊息
        /// </summary>
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

        /// <summary>
        /// 釋放資源
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// 釋放資源（保護方法）
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                try
                {
                    // 釋放 Listener
                    if (_lazyListener.IsValueCreated)
                    {
                        var listener = _lazyListener.Value;
                        Trace.Listeners.Remove(listener);
                        listener.Dispose();
                    }

                    // 釋放 StreamWriter
                    if (_lazyStreamWriter.IsValueCreated)
                    {
                        _lazyStreamWriter.Value.Dispose();
                    }

                    // 釋放 FileStream
                    if (_lazyFileStream.IsValueCreated)
                    {
                        _lazyFileStream.Value.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[TraceLogger] Dispose failed: {ex.Message}");
                }
            }

            _disposed = true;
        }

        /// <summary>
        /// 解構函數
        /// </summary>
        ~TraceLogger()
        {
            Dispose(false);
        }

        #endregion
    }

    /// <summary>
    /// 效能監控器
    /// 用於測量操作執行時間
    /// </summary>
    public class PerformanceMonitor : IDisposable
    {
        private readonly Stopwatch _stopwatch;
        private readonly string _operationName;
        private readonly ITraceLogger _logger;
        private bool _disposed = false;

        /// <summary>
        /// 建構函數 - 開始計時
        /// </summary>
        public PerformanceMonitor(string operationName, ITraceLogger logger)
        {
            _operationName = operationName ?? "Unknown Operation";
            _logger = logger;
            _stopwatch = Stopwatch.StartNew();

            _logger?.WriteLine($"[Performance] {_operationName} - 開始執行");
        }

        /// <summary>
        /// 停止計時並記錄
        /// </summary>
        public void Stop()
        {
            if (_stopwatch.IsRunning)
            {
                _stopwatch.Stop();
                _logger?.WriteLine($"[Performance] {_operationName} - 完成執行 (耗時: {_stopwatch.ElapsedMilliseconds} ms)");
            }
        }

        /// <summary>
        /// 釋放資源
        /// </summary>
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
