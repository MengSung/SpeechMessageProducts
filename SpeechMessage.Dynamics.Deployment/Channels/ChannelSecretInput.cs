/*
 * 檔案整體說明
 *
 * 目的與定位：Windows 部署程序的 P2-03 stdin owner，只接受重新導向的檔案或管線，
 * 防止秘密互動輸入被回顯，亦避免管線未關閉時遺留無法回收的背景讀取工作。
 * 主要功能與流程：管線以 PeekNamedPipe 確認至少一 byte 可讀後才讀一 byte；沒有資料時
 * 有界等待 caller 取消或三十秒 deadline。嚴格 UTF-8 decoder 逐 byte 處理，總量最多16KiB。
 * 檔案來源同步讀取至 EOF；來源須為本機有限檔案，不宣稱能中斷失效裝置或網路檔案 I/O。
 * 資源擁有權與隔離：每命令一個 reader 擁有來源 stream、decoder 及一 byte／兩 char buffer；
 * Dispose 關閉 stream、重設 decoder 並清零自有 buffer。標準 handle 只借用，絕不關閉程序
 * 的原始 stdin；測試 stream 則由 reader 獨占並關閉。沒有 static 秘密、task、timer或取消註冊。
 * 單一 caller 才能讀取或 Dispose；不允許其他 consumer 同時讀同一 pipe，避免 peek/read 競爭。
 * 維護與驗證：timeout 是真正失敗，caller取消才為 OCE；native錯誤只用固定摘要。
 * 真匿名管線測試保護等待取消、deadline、UTF-8分段、EOF及handle釋放，另驗容量與關閉後拒絕。
 */
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace SpeechMessage.Dynamics.Deployment.Channels;

/// <summary>單一讀取者、有限生命週期的秘密輸入；不建立非同步或背景讀取。</summary>
internal sealed class ChannelSecretInput : TextReader
{
    /// <summary>唯一擁有的當次來源，Dispose只關閉一次，不移交給背景工作。</summary>
    private readonly Stream source;
    /// <summary>來源的借用native handle；factory只擁有其非關閉式wrapper，不能關閉真正標準handle。</summary>
    private readonly SafeHandle handle;
    /// <summary>公開來源型別，決定是否先peek；不由秘密或caller字串選native API。</summary>
    private readonly uint fileType;
    /// <summary>從首次讀取起的總deadline，不因收到零星byte而延長。</summary>
    private readonly TimeSpan timeout;
    /// <summary>借入的caller token；WaitHandle由其CTS owner釋放，不自行建立註冊。</summary>
    private readonly CancellationToken cancellationToken;
    /// <summary>只表達是否Dispose非擁有wrapper，永不關閉借入真正標準handle。</summary>
    private readonly bool ownsHandleWrapper;
    /// <summary>單一操作的單調時鐘，不使用主機本地日期判斷逾時。</summary>
    private readonly Stopwatch stopwatch = new();
    /// <summary>嚴格UTF-8跨byte狀態，關閉時Reset，不跨命令分享。</summary>
    private readonly Decoder decoder = new UTF8Encoding(false, true).GetDecoder();
    /// <summary>最多一byte暫存，每次decoder消費後清零。</summary>
    private readonly byte[] oneByte = new byte[1];
    /// <summary>最多兩個UTF-16單位，以容納代理對；每字交付後清零。</summary>
    private readonly char[] characters = new char[2];
    /// <summary>本次已讀byte總數，上限16KiB。</summary>
    private int byteCount;
    /// <summary>下一個待交付字元索引，只有唯一reader可修改。</summary>
    private int characterIndex;
    /// <summary>當次decoder已產生的字元數，不會超過二。</summary>
    private int characterCount;
    /// <summary>已讀EOF並flush decoder，後續不再嘗試native讀取。</summary>
    private bool ended;
    /// <summary>唯一關閉狀態，關閉後不能重新入場。</summary>
    private bool disposed;

    /// <summary>測試借入真正 pipe handle；來源 stream 唯一移交 reader，handle 仍隨 stream 釋放。</summary>
    /// <param name="source">移交所有權的來源，不可由第二個 consumer 讀取。</param>
    /// <param name="handle">來源的借用 OS handle，必須比讀取操作活得久。</param>
    /// <param name="fileType">Windows FILE_TYPE_DISK=1 或 FILE_TYPE_PIPE=3。</param>
    /// <param name="timeout">從首次讀取起的總輸入 deadline。</param>
    /// <param name="cancellationToken">命令 owner 的正常取消。</param>
    internal ChannelSecretInput(Stream source, SafeHandle handle, uint fileType,
        TimeSpan timeout, CancellationToken cancellationToken)
        : this(source, handle, fileType, timeout, false, cancellationToken) { }

    /// <summary>建立單次 owner；factory 的非擁有 handle wrapper 另在 Dispose 釋放。</summary>
    private ChannelSecretInput(Stream source, SafeHandle handle, uint fileType,
        TimeSpan timeout, bool ownsHandleWrapper, CancellationToken cancellationToken)
    {
        this.source = source;
        this.handle = handle;
        this.fileType = fileType;
        this.timeout = timeout;
        this.ownsHandleWrapper = ownsHandleWrapper;
        this.cancellationToken = cancellationToken;
    }

    /// <summary>只在真正讀取秘密前建立；拒絕互動 console 與非 Windows，不觸碰任何秘密。</summary>
    /// <param name="cancellationToken">程序擁有的 caller 取消訊號。</param>
    /// <returns>caller 必須 using 關閉的 stdin owner。</returns>
    internal static ChannelSecretInput Open(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows() || !Console.IsInputRedirected)
            throw new InvalidOperationException("Channel input requires redirected Windows stdin.");
        var handle = new SafeFileHandle(GetStdHandle(-10), ownsHandle: false);
        Stream? source = null;
        try
        {
            var type = GetFileType(handle);
            if (handle.IsInvalid || type is not (1 or 3))
                throw new InvalidOperationException("Channel input source is not supported.");
            source = Console.OpenStandardInput();
            return new ChannelSecretInput(source, handle, type, TimeSpan.FromSeconds(30), true, cancellationToken);
        }
        catch
        {
            source?.Dispose();
            handle.Dispose();
            throw;
        }
    }

    /// <summary>讀一個嚴格 UTF-8 字元；代理對第二字元留在當次兩字元 buffer，交付後即清零。</summary>
    /// <returns>下一個 UTF-16 code unit；完整 EOF 為 -1。</returns>
    public override int Read()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        CheckDeadline();
        while (characterIndex == characterCount)
        {
            if (ended) return -1;
            var value = ReadByte();
            if (value < 0)
            {
                ended = true;
                decoder.Convert(ReadOnlySpan<byte>.Empty, characters.AsSpan(), true, out _, out characterCount, out _);
            }
            else
            {
                if (++byteCount > 16 * 1024)
                    throw new InvalidDataException("Channel input exceeds its byte limit.");
                oneByte[0] = (byte)value;
                try { decoder.Convert(oneByte.AsSpan(), characters.AsSpan(), false, out _, out characterCount, out _); }
                finally { oneByte[0] = 0; }
            }
            characterIndex = 0;
        }
        var result = characters[characterIndex];
        characters[characterIndex++] = '\0';
        return result;
    }

    /// <summary>有界等待 pipe 資料，確認可讀後只取一 byte；同步檔案讀取不能假稱具任意 I/O 中斷。</summary>
    private int ReadByte()
    {
        CheckDeadline();
        if (fileType == 3)
        {
            while (true)
            {
                if (!PeekNamedPipe(handle, IntPtr.Zero, 0, IntPtr.Zero, out var available, IntPtr.Zero))
                {
                    if (Marshal.GetLastPInvokeError() == 109) return -1; // ERROR_BROKEN_PIPE：writer 已關閉。
                    throw new IOException("Channel input pipe is unavailable.");
                }
                if (available > 0) break;
                CheckDeadline();
                var remaining = timeout - stopwatch.Elapsed;
                var wait = remaining < TimeSpan.FromMilliseconds(20) ? remaining : TimeSpan.FromMilliseconds(20);
                if (wait > TimeSpan.Zero) cancellationToken.WaitHandle.WaitOne(wait);
                CheckDeadline();
            }
        }
        return source.ReadByte();
    }

    /// <summary>首次讀取才啟動 deadline；timeout 不借用 caller token 冒充正常取消。</summary>
    private void CheckDeadline()
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!stopwatch.IsRunning) stopwatch.Start();
        if (stopwatch.Elapsed >= timeout)
            throw new TimeoutException("Channel input deadline exceeded.");
    }

    /// <summary>唯一關閉來源的 owner；就算來源關閉失敗也清空 buffer 及借用 wrapper。</summary>
    /// <param name="disposing">由 TextReader.Dispose 觸發的確定性清理。</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed)
        {
            disposed = true;
            try { source.Dispose(); }
            finally
            {
                if (ownsHandleWrapper) handle.Dispose();
                decoder.Reset();
                Array.Clear(oneByte);
                Array.Clear(characters);
                base.Dispose(disposing);
            }
        }
    }

    /// <summary>僅借用程序 stdin handle，回傳值由非擁有 SafeFileHandle 包裝。</summary>
    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr GetStdHandle(int standardHandle);

    /// <summary>辨認來源類型，不讀資料或改動 handle。</summary>
    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint GetFileType(SafeHandle file);

    /// <summary>非破壞性檢查 pipe 可讀 byte 數；SafeHandle 在 native 呼叫期間保持存活。</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekNamedPipe(SafeHandle pipe, IntPtr buffer, uint size,
        IntPtr bytesRead, out uint available, IntPtr bytesLeft);
}
