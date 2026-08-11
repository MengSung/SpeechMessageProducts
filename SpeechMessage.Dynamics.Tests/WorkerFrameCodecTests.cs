using System.Net;
using FluentAssertions;
using SpeechMessage.Dynamics.WorkerProtocol;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證 length-prefixed Worker frame 的 big-endian、byte 上限、截斷/尾隨資料拒絕與 caller-owned Stream 生命週期。
/// 測試使用逐 byte fragmented stream 與 truncated payload 故障注入；主要斷言是 codec 精確讀滿、EOF fail closed，
/// 且不 Dispose 或保留呼叫端 Stream，避免 pipe ownership 混亂與跨要求資源洩漏。
/// </summary>
public sealed class WorkerFrameCodecTests
{
    /// <summary>證明 header 使用網路 byte order，避免不同 process/architecture 對 payload 長度解讀不一致。</summary>
    [Fact]
    public void Encode_uses_a_big_endian_length_prefix()
    {
        var frame = WorkerFrameCodec.Encode([0x10, 0x20, 0x30], maxFrameBytes: 16);

        frame.Should().Equal(0x00, 0x00, 0x00, 0x03, 0x10, 0x20, 0x30);
    }

    /// <summary>注入空與超限 payload，證明配置 frame 前以固定分類拒絕無效/過大輸入。</summary>
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

    /// <summary>在合法 frame 後加入額外 byte，證明單一 frame decoder 不接受黏包或隱藏第二段資料。</summary>
    [Fact]
    public void DecodeSingleFrame_rejects_trailing_bytes()
    {
        var encoded = WorkerFrameCodec.Encode([0x01, 0x02], maxFrameBytes: 16);
        var withTrailingByte = encoded.Concat(new byte[] { 0x03 }).ToArray();

        var action = () => WorkerFrameCodec.DecodeSingleFrame(withTrailingByte, maxFrameBytes: 16);

        action.Should().Throw<WorkerProtocolException>()
            .Which.Category.Should().Be(WorkerProtocolFailureCategory.TrailingFrameData);
    }

    /// <summary>以每次最多一 byte 的讀取故障模型證明 ReadAsync 可處理 pipe fragmentation，且 Stream Dispose 仍由 caller 負責。</summary>
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

    /// <summary>宣告四 byte 但只提供兩 byte，證明 EOF 回傳 IncompleteFrame，不重用部分 payload。</summary>
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

    /// <summary>
    /// 模擬 named pipe/network stream 的分段讀取並記錄 Dispose 次數。
    /// 測試本身以 <c>await using</c> 成為唯一 owner；codec 只借用此 Stream，不能提早關閉或保存參考。
    /// </summary>
    private sealed class FragmentedReadStream : MemoryStream
    {
        private readonly int _maximumChunkSize;

        /// <summary>
        /// 建立只讀、單一測試擁有的分段串流。<paramref name="buffer"/> 僅在此測試個體的生命週期內
        /// 被 <see cref="MemoryStream"/> 持有；<paramref name="maximumChunkSize"/> 強制每次讀取的上限，
        /// 用來重現 named pipe／網路傳輸可能發生的短讀，而不建立背景工作、計時器或外部控制代碼。
        /// </summary>
        /// <param name="buffer">由測試建立且大小受 frame 上限約束的完整輸入位元組。</param>
        /// <param name="maximumChunkSize">單次同步或非同步讀取最多可回傳的位元組數。</param>
        public FragmentedReadStream(byte[] buffer, int maximumChunkSize)
            : base(buffer, writable: false)
        {
            _maximumChunkSize = maximumChunkSize;
        }

        /// <summary>
        /// 取得 Dispose 被呼叫的次數。決定性斷言要求 codec 執行期間維持零，並由測試的
        /// <c>await using</c> 在唯一 owner scope 結束時釋放，藉此防止借用方提早關閉或重複釋放串流。
        /// </summary>
        public int DisposeCount { get; private set; }

        /// <summary>
        /// 以固定 chunk 上限代理同步讀取，模擬 transport 短讀；不快取目的 buffer、offset 或 count，
        /// 因此每次呼叫結束後不會保留呼叫端狀態。
        /// </summary>
        /// <param name="buffer">呼叫端擁有的目的緩衝區。</param>
        /// <param name="offset">目的緩衝區的起始位置。</param>
        /// <param name="count">呼叫端希望讀取的最大位元組數。</param>
        /// <returns>本次實際讀取的位元組數，永遠不超過建構時的 chunk 上限。</returns>
        public override int Read(byte[] buffer, int offset, int count)
        {
            return base.Read(buffer, offset, Math.Min(count, _maximumChunkSize));
        }

        /// <summary>
        /// 以與同步路徑相同的固定 chunk 上限代理非同步讀取，並原樣傳遞取消權杖；方法本身不建立
        /// 額外 CTS、registration 或背景 Task，取消與傳回 Task 的生命週期仍由基底串流及呼叫端擁有。
        /// </summary>
        /// <param name="buffer">呼叫端擁有的目的緩衝區。</param>
        /// <param name="offset">目的緩衝區的起始位置。</param>
        /// <param name="count">呼叫端希望讀取的最大位元組數。</param>
        /// <param name="cancellationToken">控制單次讀取的取消權杖，不會被保留到呼叫結束之後。</param>
        /// <returns>代表本次受界限讀取的 Task。</returns>
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

        /// <summary>
        /// 記錄釋放次數後交由 <see cref="MemoryStream"/> 完成清理。測試藉此確認 codec 只借用串流，
        /// 而真正的唯一 owner 會在 scope 結束時恰好執行釋放；不吞掉基底清理，也不保留 backing buffer。
        /// </summary>
        /// <param name="disposing">是否由決定性的 managed Dispose 路徑進入。</param>
        protected override void Dispose(bool disposing)
        {
            DisposeCount++;
            base.Dispose(disposing);
        }
    }
}
