// ============================================================================
// 檔案：SpeechMessage.Dynamics.ControlPlane/Runtime/PreparedOperationDispatch.cs
// 目的：擁有 admission queue 與 outbound dispatch 之間唯一可保留的有界參數狀態。
// ============================================================================

using System.Buffers;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using SpeechMessage.Dynamics.ControlPlane.Capacity;

namespace SpeechMessage.Dynamics.ControlPlane.Runtime;

/// <summary>
/// 表示已在第一個 await 前完成名稱、型別、值域與精確 byte 上限驗證的派送資料。
/// 唯一 owner 是 <see cref="ControlledOperationExecutor"/>；物件可跨過 admission wait 與單次 client 執行，
/// 但不得交給 singleton cache、background queue 或另一個 request。
/// </summary>
/// <remarks>
/// 可保留的內容只有有界 <see cref="DispatchEnvelope"/>、已正規化 scalar dictionary、
/// canonical pooled byte buffer 與 SHA-256 hash。禁止保留 <c>OperationExecutionRequest</c>、來源 dictionary、
/// <c>JsonElement</c>/<c>JsonDocument</c>、HttpContext、ClaimsPrincipal、user/session identity、token、credential、
/// client、runtime、handler 或 cancellation registration。參數中的 managed string 受精確 envelope 上限限制，
/// 但 GC 不提供字串即時抹除保證，因此此邊界只允許非秘密 operation scalar。
/// </remarks>
internal sealed class PreparedOperationDispatch : IDisposable
{
    private DispatchEnvelope? _envelope;
    private Dictionary<string, object?>? _parameters;
    private ReadOnlyDictionary<string, object?>? _readOnlyParameters;
    private ArrayPool<byte>? _bufferPool;
    private byte[]? _canonicalBuffer;
    private string? _canonicalSha256;
    private readonly int _canonicalLength;
    private int _disposed;

    /// <summary>
    /// 建立 prepared owner 並接管參數 dictionary 與 pooled buffer。建構後呼叫端不得再清除、
    /// 歸還或重用這兩個資源，否則會破壞單一 ownership 與並行安全。
    /// </summary>
    internal PreparedOperationDispatch(
        DispatchEnvelope envelope,
        Dictionary<string, object?> parameters,
        byte[] canonicalBuffer,
        int canonicalLength,
        string canonicalSha256,
        ArrayPool<byte> bufferPool)
    {
        _envelope = envelope ?? throw new ArgumentNullException(nameof(envelope));
        _parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
        _readOnlyParameters = new ReadOnlyDictionary<string, object?>(parameters);
        _canonicalBuffer = canonicalBuffer ?? throw new ArgumentNullException(nameof(canonicalBuffer));
        _canonicalLength = canonicalLength;
        _canonicalSha256 = canonicalSha256 ?? throw new ArgumentNullException(nameof(canonicalSha256));
        _bufferPool = bufferPool ?? throw new ArgumentNullException(nameof(bufferPool));
    }

    /// <summary>
    /// 取得 admission queue 可保留的非秘密 bounded envelope；Dispose 後拒絕存取，
    /// 避免使用者把已結束的工作重新派送。
    /// </summary>
    internal DispatchEnvelope Envelope
        => Volatile.Read(ref _disposed) == 0 && _envelope is { } envelope
            ? envelope
            : throw new ObjectDisposedException(nameof(PreparedOperationDispatch));

    /// <summary>
    /// 取得經 registry 審核後的唯讀 scalar 參數。Dispose 會清空底層 dictionary 並移除 wrapper 參考，
    /// 讓成功、失敗、例外與取消路徑都不會延長 managed value 壽命。
    /// </summary>
    internal IReadOnlyDictionary<string, object?> Parameters
        => Volatile.Read(ref _disposed) == 0 && _readOnlyParameters is { } parameters
            ? parameters
            : throw new ObjectDisposedException(nameof(PreparedOperationDispatch));

    /// <summary>
    /// 取得已寫入的 canonical bytes；只供同 assembly dispatch/hash 與 friend test 驗證，
    /// 不得將 memory 儲存到 owner 生命週期之外。
    /// </summary>
    internal ReadOnlyMemory<byte> CanonicalBytes
        => Volatile.Read(ref _disposed) == 0 && _canonicalBuffer is { } buffer
            ? buffer.AsMemory(0, _canonicalLength)
            : throw new ObjectDisposedException(nameof(PreparedOperationDispatch));

    /// <summary>取得 canonical bytes 的小寫 SHA-256；這是非秘密指紋，不是 credential HMAC。</summary>
    internal string CanonicalSha256
        => Volatile.Read(ref _disposed) == 0 && _canonicalSha256 is { } hash
            ? hash
            : throw new ObjectDisposedException(nameof(PreparedOperationDispatch));

    /// <summary>取得是否已有某一 thread 取得唯一 cleanup ownership。</summary>
    internal bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    /// <summary>
    /// 以 <see cref="Interlocked.Exchange(ref int,int)"/> 取得唯一 cleanup ownership。順序是先從物件清除
    /// envelope/hash/wrapper 與 dictionary 內容，再以 <see cref="CryptographicOperations.ZeroMemory(Span{byte})"/>
    /// 清除整個租借陣列（不只是 written span），最後才 Return 給 pool。
    /// </summary>
    /// <remarks>
    /// 全陣列清零會多寫入 ArrayPool 提供的 tail，是有意識的安全/效能取捨：
    /// 派送上限小且有界，確定性不把前一請求 bytes 暴露給後續 renter 優先於省略 tail clear。
    /// </remarks>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        var parameters = Interlocked.Exchange(ref _parameters, null);
        _readOnlyParameters = null;
        _envelope = null;
        _canonicalSha256 = null;
        var buffer = Interlocked.Exchange(ref _canonicalBuffer, null);
        var pool = Interlocked.Exchange(ref _bufferPool, null);

        parameters?.Clear();
        if (buffer is null || pool is null)
        {
            return;
        }

        // 安全順序不可互換：Return 之後 buffer 就可被其他 request 並行租用。
        CryptographicOperations.ZeroMemory(buffer.AsSpan());
        pool.Return(buffer, clearArray: false);
    }
}
