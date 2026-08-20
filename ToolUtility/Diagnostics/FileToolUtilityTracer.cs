// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/Diagnostics/FileToolUtilityTracer.cs
// 所屬區塊：ToolUtility 診斷層，負責 legacy ToolUtility 追蹤輸出的資源擁有權。
// 檔案責任：以單一私有 writer 輸出 CHURCH_REPORT_TRACE.TXT，不加入全域
//           System.Diagnostics.Trace.Listeners，避免污染 Trace.log 或重複輸出。
// 生命週期：此型別必須由 DI 以 Singleton 建立；第一次合格輸出才開啟檔案，
//           Dispose 依序停止寫入、Flush、釋放 writer 與 stream，且方法冪等。
// 隔離要求：本型別不保存 request、Session、Claims、tenant 或 credential 狀態；
//           Release 編譯下即使被直接 new，Write 也永遠是 no-op。
// 編碼要求：本檔案需維持 UTF-8 without BOM、CRLF 與最終 CRLF。
// ============================================================================
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace ToolUtilityNameSpace.Diagnostics
{
    /// <summary>
    /// 以私有檔案 writer 輸出 legacy ToolUtility 追蹤的程序級資源擁有者。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 全域 <see cref="Trace.Listeners"/> 只由 ChurchReport Program 擁有
    /// <c>Trace.log</c> listener。本型別直接寫自己的 Big5 writer，因此一般
    /// <c>Trace.WriteLine</c> 不會被複製到 <c>CHURCH_REPORT_TRACE.TXT</c>，
    /// 也不會因多個 listener 同時操作同一檔案而產生交錯寫入。
    /// </para>
    /// <para>
    /// 寫入資源的最長生命週期等同應用程式程序；DI 容器是唯一 owner，請勿在
    /// request scope 中自行 Dispose。停用或 Release 模式不會建立 directory、
    /// FileStream、StreamWriter 或背景工作。
    /// </para>
    /// </remarks>
    public sealed class FileToolUtilityTracer : IToolUtilityTracer, IDisposable
    {
        private readonly bool _enabled;
        private readonly string _traceFilePath;
        private readonly Lazy<FileStream> _lazyFileStream;
        private readonly Lazy<StreamWriter> _lazyWriter;
        private readonly object _writeLock = new object();
        private int _disposed;

        /// <summary>以統一設定建立 tracer；停用時不會開啟或建立任何檔案。</summary>
        /// <param name="options">已由組合根驗證的程序級診斷設定。</param>
        public FileToolUtilityTracer(DiagnosticTraceOptions options)
            : this(options?.ToolUtilityTracePath, options?.Enabled ?? false)
        {
        }

        /// <summary>
        /// 建立預設停用的相容 tracer；不推測路徑、不建立檔案，也不觸碰全域 listener。
        /// </summary>
        public FileToolUtilityTracer()
            : this(traceFilePath: null, enabled: false)
        {
        }

        /// <summary>建立供測試或明確 legacy 呼叫端使用的 tracer。</summary>
        /// <param name="traceFilePath">由可信任程式碼提供的完整輸出路徑。</param>
        public FileToolUtilityTracer(string traceFilePath)
            : this(traceFilePath, enabled: !string.IsNullOrWhiteSpace(traceFilePath))
        {
        }

        private FileToolUtilityTracer(string traceFilePath, bool enabled)
        {
            _enabled = IsCompileTimeTraceEnabled() && enabled;
            _traceFilePath = _enabled
                ? Path.GetFullPath(traceFilePath)
                : string.Empty;

            _lazyFileStream = new Lazy<FileStream>(CreateFileStream, LazyThreadSafetyMode.ExecutionAndPublication);
            _lazyWriter = new Lazy<StreamWriter>(CreateWriter, LazyThreadSafetyMode.ExecutionAndPublication);
        }

        /// <summary>寫入達到層級門檻的 legacy 訊息；停用與 Release 皆在入口快速返回。</summary>
        /// <param name="totalLevel">呼叫端總層級。</param>
        /// <param name="qualifiedLevel">輸出門檻。</param>
        /// <param name="message">要寫入的訊息。</param>
        /// <param name="callerFrame">原始呼叫堆疊框架。</param>
        public void Write(int totalLevel, int qualifiedLevel, string message, StackFrame callerFrame)
        {
            if (!_enabled || Volatile.Read(ref _disposed) != 0 || totalLevel < qualifiedLevel)
            {
                return;
            }

            try
            {
                var stack = callerFrame == null
                    ? string.Empty
                    : new StackTrace(callerFrame).ToString();

                lock (_writeLock)
                {
                    if (Volatile.Read(ref _disposed) != 0)
                    {
                        return;
                    }

                    var writer = _lazyWriter.Value;
                    writer.WriteLine("Time            =" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture) + Environment.NewLine);
                    writer.WriteLine("StringToProcess =" + message + Environment.NewLine);
                    writer.WriteLine("StackTrace      =" + stack + Environment.NewLine);
                    writer.WriteLine("================================================================== " + Environment.NewLine);
                    writer.Flush();
                }
            }
            catch (ObjectDisposedException)
            {
                // 關閉競態只允許略過診斷輸出，不得影響主要 request。
            }
            catch (IOException)
            {
                // 檔案不可寫時不重試與不保存例外，避免診斷形成背景資源。
            }
        }

        /// <summary>確定性釋放 writer 與 stream；不觸碰 Program 擁有的全域 listener。</summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            lock (_writeLock)
            {
                if (_lazyWriter.IsValueCreated)
                {
                    try
                    {
                        _lazyWriter.Value.Flush();
                        _lazyWriter.Value.Dispose();
                    }
                    catch (ObjectDisposedException) { }
                    catch (IOException) { }
                }

                if (_lazyFileStream.IsValueCreated)
                {
                    try
                    {
                        _lazyFileStream.Value.Flush();
                        _lazyFileStream.Value.Dispose();
                    }
                    catch (ObjectDisposedException) { }
                    catch (IOException) { }
                }
            }
        }

        private FileStream CreateFileStream()
        {
            var directory = Path.GetDirectoryName(_traceFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            return new FileStream(
                _traceFilePath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite | FileShare.Delete);
        }

        private StreamWriter CreateWriter()
        {
#if !NET462 && !NETFRAMEWORK
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
#endif
            return new StreamWriter(
                _lazyFileStream.Value,
                Encoding.GetEncoding("big5"),
                4096,
                leaveOpen: true)
            {
                AutoFlush = false
            };
        }

        private static bool IsCompileTimeTraceEnabled()
        {
#if DEBUG
            return true;
#else
            return false;
#endif
        }
    }
}
