using FluentAssertions;
using SpeechMessage.Dynamics.WorkerProtocol;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證 Ready、Response 與 Drain 等 Worker 生命週期 envelope 均綁定 nonce/version/generation，並維持成功/失敗 shape 隔離。
/// 故障注入包含未知 magic；主要斷言是未知訊息 fail closed、失敗只含 sanitized code、drain 一定具有 finite deadline，
/// 避免舊 process 訊息重放、raw upstream 資料洩漏或背景 Worker 無界存活。
/// </summary>
public sealed class WorkerLifecycleEnvelopeTests
{
    private const string Nonce = "0123456789abcdef0123456789abcdef";

    /// <summary>證明 Ready round-trip 同時保存 nonce、package lock、generation 與 CE version，不可跨 process 套用。</summary>
    [Fact]
    public void Ready_round_trip_is_nonce_package_and_generation_bound()
    {
        var codec = new WorkerEnvelopeCodec(WorkerProtocolLimits.Default);
        var ready = new WorkerReadyV1(
            WorkerProtocolVersion.Current,
            Nonce,
            OfficialWorkerKind.OfficialCrm91Worker,
            "crm91-xrmtooling-9.1.1.65-core-9.0.2.60",
            "profile-generation-0001",
            "9.1");

        var payload = codec.SerializeReady(ready);

        codec.DetectMessageKind(payload).Should().Be(WorkerMessageKind.Ready);
        codec.DeserializeReady(payload).Should().BeEquivalentTo(ready);
    }

    /// <summary>證明成功 response 只攜帶 typed WorkerValue，不含 error code 或 SDK response。</summary>
    [Fact]
    public void Success_response_round_trip_contains_only_a_typed_result()
    {
        var codec = new WorkerEnvelopeCodec(WorkerProtocolLimits.Default);
        var response = WorkerResponseV1.Success(
            WorkerProtocolVersion.Current,
            Nonce,
            Guid.NewGuid(),
            WorkerValue.FromObject(new Dictionary<string, WorkerValue>
            {
                ["userId"] = WorkerValue.FromGuid(Guid.Parse("11111111-1111-1111-1111-111111111111")),
                ["organizationId"] = WorkerValue.FromGuid(Guid.Parse("22222222-2222-2222-2222-222222222222"))
            }));

        var payload = codec.SerializeResponse(response);

        codec.DetectMessageKind(payload).Should().Be(WorkerMessageKind.Response);
        codec.DeserializeResponse(payload).Should().BeEquivalentTo(response);
    }

    /// <summary>證明失敗 response 只攜帶固定 error code，Result 必須為 null，避免錯誤路徑回傳敏感 payload。</summary>
    [Fact]
    public void Failure_response_round_trip_contains_only_a_sanitized_code()
    {
        var codec = new WorkerEnvelopeCodec(WorkerProtocolLimits.Default);
        var response = WorkerResponseV1.Failure(
            WorkerProtocolVersion.Current,
            Nonce,
            Guid.NewGuid(),
            WorkerResponseOutcome.UpstreamFailure,
            "crm.operation.failed");

        var decoded = codec.DeserializeResponse(codec.SerializeResponse(response));

        decoded.Should().BeEquivalentTo(response);
        decoded.Result.Should().BeNull();
        decoded.ErrorCode.Should().Be("crm.operation.failed");
    }

    /// <summary>證明 Drain envelope round-trip 保留 absolute finite deadline，供 Supervisor 之後強制終止與 cleanup。</summary>
    [Fact]
    public void Drain_round_trip_has_a_finite_absolute_deadline()
    {
        var codec = new WorkerEnvelopeCodec(WorkerProtocolLimits.Default);
        var drain = new WorkerDrainV1(
            WorkerProtocolVersion.Current,
            Nonce,
            new DateTimeOffset(2026, 8, 2, 8, 1, 0, TimeSpan.Zero).UtcTicks);

        var payload = codec.SerializeDrain(drain);

        codec.DetectMessageKind(payload).Should().Be(WorkerMessageKind.Drain);
        codec.DeserializeDrain(payload).Should().BeEquivalentTo(drain);
    }

    /// <summary>注入未知 magic，證明 codec 不猜測訊息類型或觸發錯誤生命週期分支。</summary>
    [Fact]
    public void DetectMessageKind_rejects_unknown_message_magic()
    {
        var codec = new WorkerEnvelopeCodec(WorkerProtocolLimits.Default);

        var action = () => codec.DetectMessageKind([0x01, 0x02, 0x03, 0x04]);

        action.Should().Throw<WorkerProtocolException>()
            .Which.Category.Should().Be(WorkerProtocolFailureCategory.InvalidEnvelope);
    }
}
