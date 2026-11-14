/*
檔案整體說明

目的與定位：為P5-02離線舊來源的FileStream改寫提供容量受限的實體scratch stream，
避免舊Trace或StreamWriter繞過便利寫檔API的一MiB限制而填滿暫存磁碟。
主要功能與流程：只有ScratchFiles.Open可建立此內部型別；以既有檔案與FileMode語意
開啟單案例映射路徑，在每次Write、WriteByte、SetLength與Seek／Position設定前驗證
單檔界線。WriteAsync在目前案例同步完成有界寫入，不排入背景工作；舊APM寫入拒絕。
使用邊界與維護：stream由ExecutionCapture登錄並於案例結束反向Dispose，舊呼叫端可先
Dispose且重複關閉安全。最多128個路徑、256個登錄stream由capture限制，不跨案例共用。
本檔不決定路徑、不連網、不讀秘密；來源不得透過native handle或反射存取基底資源，
該邊界由TransportRewriter拒絕。驗證以實際改寫舊FileStream的容量故障與清理測試為準。
*/
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LegacyGolden.PluginHarness;

/// <summary>單案例scratch檔案的有界stream，保留同步FileStream使用方式。</summary>
internal sealed class ScratchBoundedStream : FileStream
{
    private const long Capacity = 1024 * 1024;

    /// <summary>路徑已由ScratchFiles映射；唯一外層owner登錄後負責最後的Dispose。</summary>
    internal ScratchBoundedStream(string path, FileMode mode, FileAccess access, FileShare share)
        : base(path, mode, access, share)
    {
    }

    /// <summary>禁止seek製造超過單檔上限的稀疏檔案或後續越界寫入。</summary>
    public override long Position
    {
        get => base.Position;
        set
        {
            ValidateLength(value);
            base.Position = value;
        }
    }

    /// <summary>先驗證目標位置，失敗不改變目前stream位置。</summary>
    public override long Seek(long offset, SeekOrigin origin)
    {
        var start = origin == SeekOrigin.Begin ? 0 : origin == SeekOrigin.Current ? base.Position : base.Length;
        ValidateLength(checked(start + offset));
        return base.Seek(offset, origin);
    }

    /// <summary>SetLength不得繞過寫入上限。</summary>
    public override void SetLength(long value)
    {
        ValidateLength(value);
        base.SetLength(value);
    }

    /// <summary>先驗證容量再交給FileStream，禁止部分越界寫入。</summary>
    public override void Write(byte[] array, int offset, int count)
    {
        ValidateLength(checked(base.Position + count));
        base.Write(array, offset, count);
    }

    /// <summary>單byte寫入使用相同容量界線。</summary>
    public override void WriteByte(byte value)
    {
        ValidateLength(checked(base.Position + 1));
        base.WriteByte(value);
    }

    /// <summary>取消先拒絕；有界寫入同步結束後回完成Task，不留下案例外背景IO。</summary>
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Write(buffer, offset, count);
        return Task.CompletedTask;
    }

    /// <summary>APM callback生命週期無法由舊呼叫端證明，明確拒絕而不安排背景寫入。</summary>
    public override IAsyncResult BeginWrite(byte[] array, int offset, int numBytes, AsyncCallback? userCallback, object? stateObject) =>
        throw new NotSupportedException("Scratch asynchronous callback unsupported.");

    /// <summary>固定失敗訊息不含來源路徑或內容。</summary>
    private static void ValidateLength(long length)
    {
        if (length < 0 || length > Capacity)
        {
            throw new InvalidDataException("Scratch capacity exceeded.");
        }
    }
}
