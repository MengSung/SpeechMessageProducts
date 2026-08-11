using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace SpeechMessage.Dynamics.WorkerProtocol;

/// <summary>
/// 讀寫 Worker IPC 的 4-byte big-endian length-prefixed frame。
/// Codec 只配置宣告上限內的 invocation-local byte array，不 Dispose 呼叫端 Stream，也不快取
/// payload、Session 或 Profile；取消由呼叫端 token 傳入，截斷、超限與 trailing data 均 fail closed。
/// </summary>
public static class WorkerFrameCodec
{
    /// <summary>固定 frame header byte 數。</summary>
    public const int HeaderBytes = sizeof(int);
    /// <summary>預設單一 payload 上限為 1 MiB，防止無界 IPC allocation。</summary>
    public const int DefaultMaximumFrameBytes = 1024 * 1024;

    /// <summary>將非空且未超限的 payload 複製成單一 big-endian length-prefixed frame。</summary>
    public static byte[] Encode(
        byte[] payload,
        int maxFrameBytes = DefaultMaximumFrameBytes)
    {
        ValidatePayload(payload, maxFrameBytes);

        var frame = new byte[HeaderBytes + payload.Length];
        var networkLength = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(payload.Length));
        Buffer.BlockCopy(networkLength, 0, frame, 0, HeaderBytes);
        Buffer.BlockCopy(payload, 0, frame, HeaderBytes, payload.Length);
        return frame;
    }

    /// <summary>解碼記憶體中的單一完整 frame，拒絕 header/payload 截斷與任何 trailing bytes。</summary>
    public static byte[] DecodeSingleFrame(
        byte[] frame,
        int maxFrameBytes = DefaultMaximumFrameBytes)
    {
        if (frame is null)
        {
            throw new ArgumentNullException(nameof(frame));
        }

        ValidateMaximumFrameBytes(maxFrameBytes);
        if (frame.Length < HeaderBytes)
        {
            throw ProtocolFailure(
                WorkerProtocolFailureCategory.IncompleteFrame,
                "The worker frame header is incomplete.");
        }

        var payloadLength = ReadPayloadLength(frame, maxFrameBytes);
        var expectedFrameLength = checked(HeaderBytes + payloadLength);
        if (frame.Length < expectedFrameLength)
        {
            throw ProtocolFailure(
                WorkerProtocolFailureCategory.IncompleteFrame,
                "The worker frame payload is incomplete.");
        }

        if (frame.Length > expectedFrameLength)
        {
            throw ProtocolFailure(
                WorkerProtocolFailureCategory.TrailingFrameData,
                "The worker frame contains trailing data.");
        }

        var payload = new byte[payloadLength];
        Buffer.BlockCopy(frame, HeaderBytes, payload, 0, payloadLength);
        return payload;
    }

    /// <summary>
    /// 從 caller-owned Stream 精確讀滿一個 header 與 payload。
    /// 方法不關閉 Stream；只回傳 payload byte array，取消或 EOF 會在目前 invocation 結束並由呼叫端決定 Stream/pipe cleanup。
    /// </summary>
    public static async Task<byte[]> ReadAsync(
        Stream stream,
        int maxFrameBytes = DefaultMaximumFrameBytes,
        CancellationToken cancellationToken = default)
    {
        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        if (!stream.CanRead)
        {
            throw new ArgumentException("The worker frame stream must be readable.", nameof(stream));
        }

        ValidateMaximumFrameBytes(maxFrameBytes);

        var header = new byte[HeaderBytes];
        await ReadExactlyAsync(stream, header, cancellationToken).ConfigureAwait(false);
        var payloadLength = ReadPayloadLength(header, maxFrameBytes);
        var payload = new byte[payloadLength];
        await ReadExactlyAsync(stream, payload, cancellationToken).ConfigureAwait(false);
        return payload;
    }

    /// <summary>
    /// 將單一 bounded frame 寫入 caller-owned Stream 並 Flush；不關閉 Stream，也不啟動背景寫入，
    /// 因此 completion/failure 可被唯一 owner 直接觀察，不會留下 fire-and-forget Task。
    /// </summary>
    public static async Task WriteAsync(
        Stream stream,
        byte[] payload,
        int maxFrameBytes = DefaultMaximumFrameBytes,
        CancellationToken cancellationToken = default)
    {
        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        if (!stream.CanWrite)
        {
            throw new ArgumentException("The worker frame stream must be writable.", nameof(stream));
        }

        var frame = Encode(payload, maxFrameBytes);
        await stream.WriteAsync(frame, 0, frame.Length, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static int ReadPayloadLength(byte[] frameOrHeader, int maxFrameBytes)
    {
        var networkLength = BitConverter.ToInt32(frameOrHeader, 0);
        var payloadLength = IPAddress.NetworkToHostOrder(networkLength);
        if (payloadLength <= 0)
        {
            throw ProtocolFailure(
                WorkerProtocolFailureCategory.InvalidFrameLength,
                "The worker frame length must be positive.");
        }

        if (payloadLength > maxFrameBytes)
        {
            throw ProtocolFailure(
                WorkerProtocolFailureCategory.FrameTooLarge,
                "The worker frame exceeds the configured maximum.");
        }

        return payloadLength;
    }

    private static async Task ReadExactlyAsync(
        Stream stream,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        // Stream 可合法分段回傳；迴圈只累積至固定 buffer 長度。Read=0 代表 remote 已結束，
        // 必須立即以 IncompleteFrame fail closed，不能重用部分 payload 或等待無界資料。
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(
                buffer,
                offset,
                buffer.Length - offset,
                cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw ProtocolFailure(
                    WorkerProtocolFailureCategory.IncompleteFrame,
                    "The worker frame ended before the declared length was read.");
            }

            offset += read;
        }
    }

    private static void ValidatePayload(byte[] payload, int maxFrameBytes)
    {
        if (payload is null)
        {
            throw new ArgumentNullException(nameof(payload));
        }

        ValidateMaximumFrameBytes(maxFrameBytes);
        if (payload.Length == 0)
        {
            throw ProtocolFailure(
                WorkerProtocolFailureCategory.InvalidFrameLength,
                "The worker frame payload must not be empty.");
        }

        if (payload.Length > maxFrameBytes)
        {
            throw ProtocolFailure(
                WorkerProtocolFailureCategory.FrameTooLarge,
                "The worker frame exceeds the configured maximum.");
        }
    }

    private static void ValidateMaximumFrameBytes(int maxFrameBytes)
    {
        if (maxFrameBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxFrameBytes),
                "The maximum worker frame size must be positive.");
        }
    }

    private static WorkerProtocolException ProtocolFailure(
        WorkerProtocolFailureCategory category,
        string message)
    {
        return new WorkerProtocolException(category, message);
    }
}
