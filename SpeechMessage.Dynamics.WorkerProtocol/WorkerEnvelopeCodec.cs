using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace SpeechMessage.Dynamics.WorkerProtocol;

public sealed class WorkerEnvelopeCodec
{
    private static readonly byte[] RequestMagic = { (byte)'S', (byte)'M', (byte)'W', (byte)'1' };
    private static readonly byte[] ReadyMagic = { (byte)'S', (byte)'M', (byte)'Y', (byte)'1' };
    private static readonly byte[] ResponseMagic = { (byte)'S', (byte)'M', (byte)'S', (byte)'1' };
    private static readonly byte[] DrainMagic = { (byte)'S', (byte)'M', (byte)'D', (byte)'1' };
    private readonly WorkerProtocolLimits _limits;

    public WorkerEnvelopeCodec(WorkerProtocolLimits limits)
    {
        _limits = limits ?? throw new ArgumentNullException(nameof(limits));
    }

    public byte[] SerializeRequest(WorkerRequestV1 request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        WorkerEnvelopeValidator.ValidateRequest(request, _limits);
        using var writer = new BoundedEnvelopeWriter(_limits.MaximumFrameBytes);
        writer.WriteBytes(RequestMagic);
        writer.WriteInt32(request.ProtocolVersion);
        writer.WriteString(request.ProcessNonce, _limits.MaximumIdentifierUtf8Bytes);
        writer.WriteString(request.RequestId.ToString("N"), 32);
        writer.WriteString(request.ProfileGenerationId, _limits.MaximumIdentifierUtf8Bytes);
        writer.WriteString(
            request.OperationDefinitionRevision,
            _limits.MaximumIdentifierUtf8Bytes);
        writer.WriteString(request.CapabilityOperationId, _limits.MaximumIdentifierUtf8Bytes);
        writer.WriteInt64(request.DeadlineUtcTicks);
        WriteObject(writer, request.Parameters, depth: 0);
        return writer.ToArray();
    }

    public WorkerRequestV1 DeserializeRequest(byte[] payload)
    {
        if (payload is null)
        {
            throw new ArgumentNullException(nameof(payload));
        }

        if (payload.Length == 0)
        {
            throw InvalidEnvelope("The worker request envelope is empty.");
        }

        if (payload.Length > _limits.MaximumFrameBytes)
        {
            throw FrameTooLarge();
        }

        var reader = new BoundedEnvelopeReader(payload, _limits);
        reader.RequireBytes(RequestMagic);
        var protocolVersion = reader.ReadInt32();
        var processNonce = reader.ReadString(_limits.MaximumIdentifierUtf8Bytes);
        var requestIdText = reader.ReadString(32);
        if (!Guid.TryParseExact(requestIdText, "N", out var requestId))
        {
            throw InvalidEnvelope("The worker request identifier is invalid.");
        }

        var profileGenerationId = reader.ReadString(_limits.MaximumIdentifierUtf8Bytes);
        var operationDefinitionRevision = reader.ReadString(
            _limits.MaximumIdentifierUtf8Bytes);
        var capabilityOperationId = reader.ReadString(_limits.MaximumIdentifierUtf8Bytes);
        var deadlineUtcTicks = reader.ReadInt64();
        var parameters = reader.ReadObject(depth: 0);
        reader.RequireEnd();

        var request = new WorkerRequestV1(
            protocolVersion,
            processNonce,
            requestId,
            profileGenerationId,
            operationDefinitionRevision,
            capabilityOperationId,
            deadlineUtcTicks,
            parameters);
        WorkerEnvelopeValidator.ValidateRequest(request, _limits);
        return request;
    }

    public WorkerMessageKind DetectMessageKind(byte[] payload)
    {
        ValidatePayload(payload);
        if (StartsWith(payload, RequestMagic))
        {
            return WorkerMessageKind.Request;
        }

        if (StartsWith(payload, ReadyMagic))
        {
            return WorkerMessageKind.Ready;
        }

        if (StartsWith(payload, ResponseMagic))
        {
            return WorkerMessageKind.Response;
        }

        if (StartsWith(payload, DrainMagic))
        {
            return WorkerMessageKind.Drain;
        }

        throw InvalidEnvelope("The worker message type is invalid.");
    }

    public byte[] SerializeReady(WorkerReadyV1 ready)
    {
        ValidateReady(ready);
        using var writer = new BoundedEnvelopeWriter(_limits.MaximumFrameBytes);
        writer.WriteBytes(ReadyMagic);
        writer.WriteInt32(ready.ProtocolVersion);
        writer.WriteString(ready.ProcessNonce, _limits.MaximumIdentifierUtf8Bytes);
        writer.WriteInt32((int)ready.WorkerKind);
        writer.WriteString(ready.PackageLockId, _limits.MaximumIdentifierUtf8Bytes);
        writer.WriteString(ready.ProfileGenerationId, _limits.MaximumIdentifierUtf8Bytes);
        writer.WriteString(ready.CeVersion, 8);
        return writer.ToArray();
    }

    public WorkerReadyV1 DeserializeReady(byte[] payload)
    {
        ValidatePayload(payload);
        var reader = new BoundedEnvelopeReader(payload, _limits);
        reader.RequireBytes(ReadyMagic);
        var ready = new WorkerReadyV1(
            reader.ReadInt32(),
            reader.ReadString(_limits.MaximumIdentifierUtf8Bytes),
            (OfficialWorkerKind)reader.ReadInt32(),
            reader.ReadString(_limits.MaximumIdentifierUtf8Bytes),
            reader.ReadString(_limits.MaximumIdentifierUtf8Bytes),
            reader.ReadString(8));
        reader.RequireEnd();
        ValidateReady(ready);
        return ready;
    }

    public byte[] SerializeResponse(WorkerResponseV1 response)
    {
        ValidateResponse(response);
        using var writer = new BoundedEnvelopeWriter(_limits.MaximumFrameBytes);
        writer.WriteBytes(ResponseMagic);
        writer.WriteInt32(response.ProtocolVersion);
        writer.WriteString(response.ProcessNonce, _limits.MaximumIdentifierUtf8Bytes);
        writer.WriteString(response.RequestId.ToString("N"), 32);
        writer.WriteInt32((int)response.Outcome);
        if (response.Outcome == WorkerResponseOutcome.Success)
        {
            WriteValue(writer, response.Result!, depth: 1);
        }
        else
        {
            writer.WriteString(response.ErrorCode!, _limits.MaximumIdentifierUtf8Bytes);
        }

        return writer.ToArray();
    }

    public WorkerResponseV1 DeserializeResponse(byte[] payload)
    {
        ValidatePayload(payload);
        var reader = new BoundedEnvelopeReader(payload, _limits);
        reader.RequireBytes(ResponseMagic);
        var protocolVersion = reader.ReadInt32();
        var processNonce = reader.ReadString(_limits.MaximumIdentifierUtf8Bytes);
        var requestIdText = reader.ReadString(32);
        if (!Guid.TryParseExact(requestIdText, "N", out var requestId))
        {
            throw InvalidEnvelope("The worker response identifier is invalid.");
        }

        var outcome = (WorkerResponseOutcome)reader.ReadInt32();
        WorkerResponseV1 response;
        if (outcome == WorkerResponseOutcome.Success)
        {
            response = WorkerResponseV1.Success(
                protocolVersion,
                processNonce,
                requestId,
                reader.ReadValue(depth: 1));
        }
        else
        {
            response = WorkerResponseV1.Failure(
                protocolVersion,
                processNonce,
                requestId,
                outcome,
                reader.ReadString(_limits.MaximumIdentifierUtf8Bytes));
        }

        reader.RequireEnd();
        ValidateResponse(response);
        return response;
    }

    public byte[] SerializeDrain(WorkerDrainV1 drain)
    {
        ValidateDrain(drain);
        using var writer = new BoundedEnvelopeWriter(_limits.MaximumFrameBytes);
        writer.WriteBytes(DrainMagic);
        writer.WriteInt32(drain.ProtocolVersion);
        writer.WriteString(drain.ProcessNonce, _limits.MaximumIdentifierUtf8Bytes);
        writer.WriteInt64(drain.DeadlineUtcTicks);
        return writer.ToArray();
    }

    public WorkerDrainV1 DeserializeDrain(byte[] payload)
    {
        ValidatePayload(payload);
        var reader = new BoundedEnvelopeReader(payload, _limits);
        reader.RequireBytes(DrainMagic);
        var drain = new WorkerDrainV1(
            reader.ReadInt32(),
            reader.ReadString(_limits.MaximumIdentifierUtf8Bytes),
            reader.ReadInt64());
        reader.RequireEnd();
        ValidateDrain(drain);
        return drain;
    }

    private void ValidateReady(WorkerReadyV1 ready)
    {
        if (ready is null)
        {
            throw new ArgumentNullException(nameof(ready));
        }

        ValidateProtocolVersion(ready.ProtocolVersion);
        WorkerEnvelopeValidator.ValidateIdentifier(
            ready.ProcessNonce,
            _limits,
            "process nonce");
        WorkerEnvelopeValidator.ValidateIdentifier(
            ready.PackageLockId,
            _limits,
            "package lock identifier");
        WorkerEnvelopeValidator.ValidateIdentifier(
            ready.ProfileGenerationId,
            _limits,
            "profile generation identifier");
        var expectedCeVersion = ready.WorkerKind switch
        {
            OfficialWorkerKind.OfficialCrm82Worker => "8.2",
            OfficialWorkerKind.OfficialCrm91Worker => "9.1",
            _ => throw InvalidEnvelope("The official worker kind is invalid.")
        };
        if (!string.Equals(ready.CeVersion, expectedCeVersion, StringComparison.Ordinal))
        {
            throw InvalidEnvelope("The official worker CE version is invalid.");
        }
    }

    private void ValidateResponse(WorkerResponseV1 response)
    {
        if (response is null)
        {
            throw new ArgumentNullException(nameof(response));
        }

        ValidateProtocolVersion(response.ProtocolVersion);
        WorkerEnvelopeValidator.ValidateIdentifier(
            response.ProcessNonce,
            _limits,
            "process nonce");
        if (response.RequestId == Guid.Empty ||
            !Enum.IsDefined(typeof(WorkerResponseOutcome), response.Outcome))
        {
            throw InvalidEnvelope("The worker response metadata is invalid.");
        }

        if (response.Outcome == WorkerResponseOutcome.Success)
        {
            if (response.Result is null || response.ErrorCode is not null)
            {
                throw InvalidEnvelope("The worker success response shape is invalid.");
            }

            WorkerEnvelopeValidator.ValidateStandaloneValue(response.Result, _limits);
            return;
        }

        if (response.Result is not null || string.IsNullOrWhiteSpace(response.ErrorCode))
        {
            throw InvalidEnvelope("The worker failure response shape is invalid.");
        }

        WorkerEnvelopeValidator.ValidateIdentifier(
            response.ErrorCode!,
            _limits,
            "error code");
    }

    private void ValidateDrain(WorkerDrainV1 drain)
    {
        if (drain is null)
        {
            throw new ArgumentNullException(nameof(drain));
        }

        ValidateProtocolVersion(drain.ProtocolVersion);
        WorkerEnvelopeValidator.ValidateIdentifier(
            drain.ProcessNonce,
            _limits,
            "process nonce");
        if (drain.DeadlineUtcTicks <= 0 || drain.DeadlineUtcTicks > DateTime.MaxValue.Ticks)
        {
            throw InvalidEnvelope("The worker drain deadline is invalid.");
        }
    }

    private static void ValidateProtocolVersion(int protocolVersion)
    {
        if (protocolVersion != WorkerProtocolVersion.Current)
        {
            throw new WorkerProtocolException(
                WorkerProtocolFailureCategory.UnsupportedProtocolVersion,
                "The worker protocol version is unsupported.");
        }
    }

    private void ValidatePayload(byte[] payload)
    {
        if (payload is null)
        {
            throw new ArgumentNullException(nameof(payload));
        }

        if (payload.Length < RequestMagic.Length)
        {
            throw InvalidEnvelope("The worker message envelope is incomplete.");
        }

        if (payload.Length > _limits.MaximumFrameBytes)
        {
            throw FrameTooLarge();
        }
    }

    private static bool StartsWith(byte[] payload, byte[] prefix)
    {
        if (payload.Length < prefix.Length)
        {
            return false;
        }

        for (var index = 0; index < prefix.Length; index++)
        {
            if (payload[index] != prefix[index])
            {
                return false;
            }
        }

        return true;
    }

    private void WriteValue(BoundedEnvelopeWriter writer, WorkerValue value, int depth)
    {
        if (depth > _limits.MaximumValueDepth)
        {
            throw LimitExceeded("The worker value nesting limit was exceeded.");
        }

        writer.WriteByte((byte)value.Kind);
        switch (value.Kind)
        {
            case WorkerValueKind.Null:
                return;
            case WorkerValueKind.Boolean:
                writer.WriteByte(value.Scalar == "true" ? (byte)1 : (byte)0);
                return;
            case WorkerValueKind.Int64:
                writer.WriteInt64(long.Parse(value.Scalar!, CultureInfo.InvariantCulture));
                return;
            case WorkerValueKind.Decimal:
            case WorkerValueKind.String:
                writer.WriteString(value.Scalar!, _limits.MaximumStringUtf8Bytes);
                return;
            case WorkerValueKind.Guid:
                writer.WriteString(value.Scalar!, 32);
                return;
            case WorkerValueKind.UtcDateTime:
                writer.WriteInt64(long.Parse(value.Scalar!, CultureInfo.InvariantCulture));
                return;
            case WorkerValueKind.Array:
                writer.WriteInt32(value.Items!.Count);
                foreach (var item in value.Items)
                {
                    WriteValue(writer, item, depth + 1);
                }

                return;
            case WorkerValueKind.Object:
                WriteObject(writer, value.Members!, depth + 1);
                return;
            default:
                throw InvalidEnvelope("The worker value kind is unsupported.");
        }
    }

    private void WriteObject(
        BoundedEnvelopeWriter writer,
        IReadOnlyDictionary<string, WorkerValue> members,
        int depth)
    {
        writer.WriteInt32(members.Count);
        foreach (var member in members.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            writer.WriteString(member.Key, _limits.MaximumIdentifierUtf8Bytes);
            WriteValue(writer, member.Value, depth + 1);
        }
    }

    private static WorkerProtocolException InvalidEnvelope(string message) =>
        new WorkerProtocolException(WorkerProtocolFailureCategory.InvalidEnvelope, message);

    private static WorkerProtocolException LimitExceeded(string message) =>
        new WorkerProtocolException(
            WorkerProtocolFailureCategory.EnvelopeLimitExceeded,
            message);

    private static WorkerProtocolException FrameTooLarge() =>
        new WorkerProtocolException(
            WorkerProtocolFailureCategory.FrameTooLarge,
            "The worker request envelope exceeds the configured maximum.");

    private sealed class BoundedEnvelopeWriter : IDisposable
    {
        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private readonly int _maximumBytes;
        private readonly MemoryStream _stream;
        private readonly byte[] _scratch = new byte[sizeof(long)];

        internal BoundedEnvelopeWriter(int maximumBytes)
        {
            _maximumBytes = maximumBytes;
            _stream = new MemoryStream(Math.Min(maximumBytes, 4096));
        }

        internal void WriteByte(byte value)
        {
            EnsureRemaining(1);
            _stream.WriteByte(value);
        }

        internal void WriteBytes(byte[] value)
        {
            EnsureRemaining(value.Length);
            _stream.Write(value, 0, value.Length);
        }

        internal void WriteInt32(int value)
        {
            _scratch[0] = (byte)(value >> 24);
            _scratch[1] = (byte)(value >> 16);
            _scratch[2] = (byte)(value >> 8);
            _scratch[3] = (byte)value;
            EnsureRemaining(sizeof(int));
            _stream.Write(_scratch, 0, sizeof(int));
        }

        internal void WriteInt64(long value)
        {
            _scratch[0] = (byte)(value >> 56);
            _scratch[1] = (byte)(value >> 48);
            _scratch[2] = (byte)(value >> 40);
            _scratch[3] = (byte)(value >> 32);
            _scratch[4] = (byte)(value >> 24);
            _scratch[5] = (byte)(value >> 16);
            _scratch[6] = (byte)(value >> 8);
            _scratch[7] = (byte)value;
            EnsureRemaining(sizeof(long));
            _stream.Write(_scratch, 0, sizeof(long));
        }

        internal void WriteString(string value, int maximumUtf8Bytes)
        {
            var byteCount = StrictUtf8.GetByteCount(value);
            if (byteCount > maximumUtf8Bytes)
            {
                throw LimitExceeded("The worker string limit was exceeded.");
            }

            WriteInt32(byteCount);
            EnsureRemaining(byteCount);
            if (byteCount == 0)
            {
                return;
            }

            var bytes = StrictUtf8.GetBytes(value);
            _stream.Write(bytes, 0, bytes.Length);
        }

        internal byte[] ToArray()
        {
            if (_stream.Length == 0)
            {
                throw InvalidEnvelope("The worker request envelope is empty.");
            }

            return _stream.ToArray();
        }

        public void Dispose()
        {
            Array.Clear(_scratch, 0, _scratch.Length);
            _stream.Dispose();
        }

        private void EnsureRemaining(int additionalBytes)
        {
            if (additionalBytes < 0 ||
                _stream.Length > _maximumBytes - additionalBytes)
            {
                throw FrameTooLarge();
            }
        }
    }

    private sealed class BoundedEnvelopeReader
    {
        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private readonly byte[] _payload;
        private readonly WorkerProtocolLimits _limits;
        private int _offset;
        private int _totalItems;
        private int _totalMembers;

        internal BoundedEnvelopeReader(byte[] payload, WorkerProtocolLimits limits)
        {
            _payload = payload;
            _limits = limits;
        }

        internal void RequireBytes(byte[] expected)
        {
            EnsureAvailable(expected.Length);
            for (var index = 0; index < expected.Length; index++)
            {
                if (_payload[_offset + index] != expected[index])
                {
                    throw InvalidEnvelope("The worker request envelope type is invalid.");
                }
            }

            _offset += expected.Length;
        }

        internal int ReadInt32()
        {
            EnsureAvailable(sizeof(int));
            var value =
                (_payload[_offset] << 24) |
                (_payload[_offset + 1] << 16) |
                (_payload[_offset + 2] << 8) |
                _payload[_offset + 3];
            _offset += sizeof(int);
            return value;
        }

        internal long ReadInt64()
        {
            EnsureAvailable(sizeof(long));
            ulong value = 0;
            for (var index = 0; index < sizeof(long); index++)
            {
                value = (value << 8) | _payload[_offset + index];
            }

            _offset += sizeof(long);
            return unchecked((long)value);
        }

        internal string ReadString(int maximumUtf8Bytes)
        {
            var byteCount = ReadInt32();
            if (byteCount < 0)
            {
                throw InvalidEnvelope("The worker string length is invalid.");
            }

            if (byteCount > maximumUtf8Bytes)
            {
                throw LimitExceeded("The worker string limit was exceeded.");
            }

            EnsureAvailable(byteCount);
            try
            {
                var value = StrictUtf8.GetString(_payload, _offset, byteCount);
                _offset += byteCount;
                return value;
            }
            catch (DecoderFallbackException)
            {
                throw InvalidEnvelope("The worker string encoding is invalid.");
            }
        }

        internal IReadOnlyDictionary<string, WorkerValue> ReadObject(int depth)
        {
            if (depth > _limits.MaximumValueDepth)
            {
                throw LimitExceeded("The worker value nesting limit was exceeded.");
            }

            var count = ReadBoundedCount(
                _limits.MaximumObjectMembers,
                "object member");
            _totalMembers = checked(_totalMembers + count);
            if (_totalMembers > _limits.MaximumObjectMembers)
            {
                throw LimitExceeded("The worker total object member limit was exceeded.");
            }

            var members = new Dictionary<string, WorkerValue>(count, StringComparer.Ordinal);
            for (var index = 0; index < count; index++)
            {
                var name = ReadString(_limits.MaximumIdentifierUtf8Bytes);
                if (members.ContainsKey(name))
                {
                    throw InvalidEnvelope("The worker object contains a duplicate member.");
                }

                members.Add(name, ReadValue(depth + 1));
            }

            return members;
        }

        internal void RequireEnd()
        {
            if (_offset != _payload.Length)
            {
                throw InvalidEnvelope("The worker request envelope contains trailing data.");
            }
        }

        internal WorkerValue ReadValue(int depth)
        {
            if (depth > _limits.MaximumValueDepth)
            {
                throw LimitExceeded("The worker value nesting limit was exceeded.");
            }

            var kind = (WorkerValueKind)ReadByte();
            switch (kind)
            {
                case WorkerValueKind.Null:
                    return WorkerValue.Null();
                case WorkerValueKind.Boolean:
                    var boolean = ReadByte();
                    if (boolean > 1)
                    {
                        throw InvalidEnvelope("The worker Boolean value is invalid.");
                    }

                    return WorkerValue.FromBoolean(boolean == 1);
                case WorkerValueKind.Int64:
                    return WorkerValue.FromInt64(ReadInt64());
                case WorkerValueKind.Decimal:
                    return new WorkerValue(
                        WorkerValueKind.Decimal,
                        ReadString(_limits.MaximumStringUtf8Bytes),
                        null,
                        null);
                case WorkerValueKind.String:
                    return WorkerValue.FromString(ReadString(_limits.MaximumStringUtf8Bytes));
                case WorkerValueKind.Guid:
                    var guidText = ReadString(32);
                    if (!Guid.TryParseExact(guidText, "N", out var guid))
                    {
                        throw InvalidEnvelope("The worker Guid value is invalid.");
                    }

                    return WorkerValue.FromGuid(guid);
                case WorkerValueKind.UtcDateTime:
                    return new WorkerValue(
                        WorkerValueKind.UtcDateTime,
                        ReadInt64().ToString(CultureInfo.InvariantCulture),
                        null,
                        null);
                case WorkerValueKind.Array:
                    var count = ReadBoundedCount(_limits.MaximumArrayItems, "array item");
                    _totalItems = checked(_totalItems + count);
                    if (_totalItems > _limits.MaximumArrayItems)
                    {
                        throw LimitExceeded("The worker total array item limit was exceeded.");
                    }

                    var items = new WorkerValue[count];
                    for (var index = 0; index < count; index++)
                    {
                        items[index] = ReadValue(depth + 1);
                    }

                    return WorkerValue.FromArray(items);
                case WorkerValueKind.Object:
                    return WorkerValue.FromObject(ReadObject(depth + 1));
                default:
                    throw InvalidEnvelope("The worker value kind is unsupported.");
            }
        }

        private byte ReadByte()
        {
            EnsureAvailable(1);
            return _payload[_offset++];
        }

        private int ReadBoundedCount(int maximum, string category)
        {
            var count = ReadInt32();
            if (count < 0)
            {
                throw InvalidEnvelope($"The worker {category} count is invalid.");
            }

            if (count > maximum)
            {
                throw LimitExceeded($"The worker {category} limit was exceeded.");
            }

            return count;
        }

        private void EnsureAvailable(int byteCount)
        {
            if (byteCount < 0 || _offset > _payload.Length - byteCount)
            {
                throw InvalidEnvelope("The worker request envelope is incomplete.");
            }
        }
    }
}
