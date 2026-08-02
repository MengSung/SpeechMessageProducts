using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace SpeechMessage.Dynamics.WorkerProtocol;

public static class WorkerFrameCodec
{
    public const int HeaderBytes = sizeof(int);
    public const int DefaultMaximumFrameBytes = 1024 * 1024;

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
