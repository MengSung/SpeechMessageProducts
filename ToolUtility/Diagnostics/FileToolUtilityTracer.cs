// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/Diagnostics/FileToolUtilityTracer.cs
// 所屬區塊：ToolUtility 診斷層，負責追蹤輸出的資源擁有權與寫入契約。
// 檔案責任：以檔案為輸出目的地的 IToolUtilityTracer 實作，唯一擁有追蹤串流與 listener。
// 主要型別：class FileToolUtilityTracer
// 主要成員：Write、Dispose
// 引用命名空間：System、System.Diagnostics、System.IO、System.Text
// 閱讀路徑：先看 remarks 說明為何必須是 Singleton，再看 Dispose 的釋放順序。
// 維護重點：Trace.Listeners.Add 在整個程序只能執行一次；請勿把本型別註冊為
//           Scoped 或 Transient，否則會造成 listener 無界成長與日誌重複。
// 行為保護：輸出格式與原 ToolUtilityClass.TraceByLevel 完全一致，不得變更欄位或順序。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig。
// ============================================================================
using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace ToolUtilityNameSpace.Diagnostics
{
    /// <summary>
    /// 以檔案為輸出目的地的追蹤實作，為程序級單一資源擁有者。
    /// </summary>
    /// <remarks>
    /// 單一擁有者：本型別是整個 Worker Process 中唯一持有追蹤用
    /// <see cref="FileStream"/>、<see cref="StreamWriter"/> 與
    /// <see cref="TextWriterTraceListener"/> 的物件，且只會向
    /// <see cref="Trace.Listeners"/>（行程級靜態集合）加入 listener 一次。
    /// **必須註冊為 Singleton。** 註冊為 Scoped 或 Transient 會使 listener 隨請求累積，
    /// 造成記憶體無界成長，且每行日誌被重複寫入 N 份。
    ///
    /// 資源最大生命週期：等同應用程式生命週期。串流採延遲建立
    /// （<see cref="Lazy{T}"/>），因此在第一次實際輸出前不會開啟檔案，
    /// 與重構前的行為一致。
    ///
    /// 確定性釋放路徑：由 DI 容器於應用程式關閉時呼叫 <see cref="Dispose"/>，
    /// 順序為 listener（先自 <see cref="Trace.Listeners"/> 移除）→ writer → stream，
    /// 每一步都先 Flush 再 Close，確保不遺失尾端輸出。
    ///
    /// 跨請求隔離：本型別不保存任何使用者、Session 或請求層級狀態，
    /// 僅將呼叫端傳入的訊息寫出，因此多請求共用不會造成資料交叉。
    /// </remarks>
    public sealed class FileToolUtilityTracer : IToolUtilityTracer, IDisposable
    {
        /// <summary>
        /// 預設的追蹤檔案路徑。沿用重構前 ToolUtilityClass 內的常數值，避免既有維運流程改變。
        /// </summary>
        public const string DefaultTraceFilePath = @"D:\除錯追蹤\CHURCH_REPORT_TRACE.TXT";

        private readonly Lazy<FileStream> _lazyFileStream;
        private readonly Lazy<StreamWriter> _lazyWriter;
        private readonly Lazy<TextWriterTraceListener> _lazyListener;
        private bool _disposed;

        /// <summary>
        /// 建立追蹤器。此時不會開啟檔案，第一次輸出才會建立串流與 listener。
        /// </summary>
        /// <param name="traceFilePath">
        /// 追蹤檔案路徑；省略時使用 <see cref="DefaultTraceFilePath"/>。
        /// 提供此參數是為了讓測試能指向暫存路徑，避免測試寫入正式的追蹤檔。
        /// </param>
        public FileToolUtilityTracer(string traceFilePath = null)
        {
            var path = string.IsNullOrWhiteSpace(traceFilePath) ? DefaultTraceFilePath : traceFilePath;

            // 以 FileShare.ReadWrite 開啟，維持與重構前相同的共用模式，
            // 讓維運人員可在程式執行中檢視檔案。
            _lazyFileStream = new Lazy<FileStream>(() =>
                new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite));

            _lazyWriter = new Lazy<StreamWriter>(() =>
            {
#if !NET462 && !NETFRAMEWORK
                // .NET Core 之後預設不含 big5；必須先註冊 CodePages 提供者才能取得該編碼。
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
#endif
                return new StreamWriter(_lazyFileStream.Value, Encoding.GetEncoding("big5"));
            });

            _lazyListener = new Lazy<TextWriterTraceListener>(() =>
            {
                var listener = new TextWriterTraceListener(_lazyWriter.Value);
                Trace.AutoFlush = true;

                // 這是整個程序唯一一次對全域 Trace.Listeners 的加入動作。
                Trace.Listeners.Add(listener);
                return listener;
            });
        }

        /// <inheritdoc />
        /// <remarks>
        /// 輸出格式（欄位名稱、對齊空白、換行位置）與重構前的
        /// <c>ToolUtilityClass.TraceByLevel</c> 完全相同，既有日誌解析流程不受影響。
        /// 存取 <c>_lazyListener.Value</c> 是必要的：它負責在首次輸出時把 listener
        /// 掛上 <see cref="Trace.Listeners"/>，否則 <see cref="Trace.WriteLine(string)"/>
        /// 不會寫入檔案。
        /// </remarks>
        public void Write(int totalLevel, int qualifiedLevel, string message, StackFrame callerFrame)
        {
            if (_disposed)
            {
                return;
            }

            if (totalLevel < qualifiedLevel)
            {
                return;
            }

            _ = _lazyListener.Value;

            var stack = callerFrame == null
                ? string.Empty
                : new StackTrace(callerFrame).ToString();

            Trace.WriteLine("Time            =" + DateTime.Now.ToString() + Environment.NewLine);
            Trace.WriteLine("StringToProcess =" + message + Environment.NewLine);
            Trace.WriteLine("StackTrace      =" + stack + Environment.NewLine);
            Trace.WriteLine("================================================================== " + Environment.NewLine);
        }

        /// <summary>
        /// 釋放追蹤資源。由 DI 容器於應用程式關閉時呼叫，不應由任何請求範圍物件呼叫。
        /// </summary>
        /// <remarks>
        /// 釋放順序為 listener → writer → stream。listener 必須先自
        /// <see cref="Trace.Listeners"/> 移除，否則後續的 <see cref="Trace.WriteLine(string)"/>
        /// 會寫入已釋放的 writer。每一步各自 try/catch，確保單一步驟失敗不會中斷其餘清理。
        /// 本方法為冪等。
        /// </remarks>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_lazyListener.IsValueCreated)
            {
                try
                {
                    var listener = _lazyListener.Value;
                    Trace.Listeners.Remove(listener);
                    listener.Flush();
                    listener.Close();
                    listener.Dispose();
                }
                catch (ObjectDisposedException) { }
            }

            if (_lazyWriter.IsValueCreated)
            {
                try
                {
                    var writer = _lazyWriter.Value;
                    writer.Flush();
                    writer.Close();
                    writer.Dispose();
                }
                catch (ObjectDisposedException) { }
            }

            if (_lazyFileStream.IsValueCreated)
            {
                try
                {
                    var stream = _lazyFileStream.Value;
                    stream.Flush();
                    stream.Close();
                    stream.Dispose();
                }
                catch (ObjectDisposedException) { }
            }
        }
    }
}
