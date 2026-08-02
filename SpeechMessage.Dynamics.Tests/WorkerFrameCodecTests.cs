using System.Net;
using FluentAssertions;
using SpeechMessage.Dynamics.WorkerProtocol;

namespace SpeechMessage.Dynamics.Tests;

public sealed class WorkerFrameCodecTests
{
    [Fact]
    public void Encode_uses_a_big_endian_length_prefix()
    {
        var frame = WorkerFrameCodec.Encode([0x10, 0x20, 0x30], maxFrameBytes: 16);

        frame.Should().Equal(0x00, 0x00, 0x00, 0x03, 0x10, 0x20, 0x30);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void Encode_rejects_empty_or_oversized_payloads(int payloadLength)
    {
        var payload = new byte[payloadLength];

        var action = () => WorkerFrameCodec.Encode(payload, maxFrameBytes: 4);

        action.Should().Throw<WorkerProtocolException>()
            .Which.Category.Should().Be(
                payloadLength == 0
                    ? WorkerProtocolFailureCategory.InvalidFrameLength
                    : WorkerProtocolFailureCategory.FrameTooLarge);
    }

    [Fact]
    public void DecodeSingleFrame_rejects_trailing_bytes()
    {
        var encoded = WorkerFrameCodec.Encode([0x01, 0x02], maxFrameBytes: 16);
        var withTrailingByte = encoded.Concat(new byte[] { 0x03 }).ToArray();

        var action = () => WorkerFrameCodec.DecodeSingleFrame(withTrailingByte, maxFrameBytes: 16);

        action.Should().Throw<WorkerProtocolException>()
            .Which.Category.Should().Be(WorkerProtocolFailureCategory.TrailingFrameData);
    }

    [Fact]
    public async Task ReadAsync_handles_fragmented_stream_reads_without_retaining_the_stream()
    {
        var encoded = WorkerFrameCodec.Encode([0x11, 0x22, 0x33, 0x44], maxFrameBytes: 16);
        await using var stream = new FragmentedReadStream(encoded, maximumChunkSize: 1);

        var payload = await WorkerFrameCodec.ReadAsync(
            stream,
            maxFrameBytes: 16,
            CancellationToken.None);

        payload.Should().Equal(0x11, 0x22, 0x33, 0x44);
        stream.DisposeCount.Should().Be(0,
            because: "the caller owns the stream lifecycle");
    }

    [Fact]
    public async Task ReadAsync_rejects_a_truncated_payload()
    {
        var header = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(4));
        await using var stream = new FragmentedReadStream(
            header.Concat(new byte[] { 0x11, 0x22 }).ToArray(),
            maximumChunkSize: 1);

        var action = async () => await WorkerFrameCodec.ReadAsync(
            stream,
            maxFrameBytes: 16,
            CancellationToken.None);

        (await action.Should().ThrowAsync<WorkerProtocolException>())
            .Which.Category.Should().Be(WorkerProtocolFailureCategory.IncompleteFrame);
    }

    private sealed class FragmentedReadStream : MemoryStream
    {
        private readonly int _maximumChunkSize;

        public FragmentedReadStream(byte[] buffer, int maximumChunkSize)
            : base(buffer, writable: false)
        {
            _maximumChunkSize = maximumChunkSize;
        }

        public int DisposeCount { get; private set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return base.Read(buffer, offset, Math.Min(count, _maximumChunkSize));
        }

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            return base.ReadAsync(
                buffer,
                offset,
                Math.Min(count, _maximumChunkSize),
                cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            DisposeCount++;
            base.Dispose(disposing);
        }
    }
}
