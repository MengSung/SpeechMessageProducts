using FluentAssertions;
using SpeechMessage.Dynamics.WorkerProtocol;

namespace SpeechMessage.Dynamics.Tests;

public sealed class WorkerLifecycleEnvelopeTests
{
    private const string Nonce = "0123456789abcdef0123456789abcdef";

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

    [Fact]
    public void DetectMessageKind_rejects_unknown_message_magic()
    {
        var codec = new WorkerEnvelopeCodec(WorkerProtocolLimits.Default);

        var action = () => codec.DetectMessageKind([0x01, 0x02, 0x03, 0x04]);

        action.Should().Throw<WorkerProtocolException>()
            .Which.Category.Should().Be(WorkerProtocolFailureCategory.InvalidEnvelope);
    }
}
