using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace SpeechMessage.Dynamics.WorkerProtocol;

/// <summary>
/// 將 Worker request／ready／response／drain DTO 轉換為 deterministic、bounded、SDK-free binary envelope。
/// Codec 不快取 request、Profile、credential、Stream 或 Session；每次序列化的 <see cref="MemoryStream"/>
/// 與 scratch buffer 都由 invocation-local writer 擁有並在 <c>using</c> 結束時確定釋放／清除。
/// 反序列化在建立大集合前強制 frame、深度、總 item/member 與 UTF-8 byte 上限，避免 memory/CPU exhaustion。
/// </summary>
public sealed class WorkerEnvelopeCodec
{
    private static readonly byte[] RequestMagic = { (byte)'S', (byte)'M', (byte)'W', (byte)'1' };
    private static readonly byte[] ReadyMagic = { (byte)'S', (byte)'M', (byte)'Y', (byte)'1' };
    private static readonly byte[] ResponseMagic = { (byte)'S', (byte)'M', (byte)'S', (byte)'1' };
    private static readonly byte[] DrainMagic = { (byte)'S', (byte)'M', (byte)'D', (byte)'1' };
    private readonly WorkerProtocolLimits _limits;

    /// <summary>
    /// 建立使用 immutable protocol limits 的 codec；limits 可以安全共用，因為不含任何 request-specific 可變狀態。
    /// </summary>
    public WorkerEnvelopeCodec(WorkerProtocolLimits limits)
    {
        _limits = limits ?? throw new ArgumentNullException(nameof(limits));
    }

    /// <summary>
    /// 驗證並序列化具名 Worker request。欄位依 ordinal key 排序，因此相同輸入產生相同 bytes；
    /// 此 canonical 特性避免 dictionary insertion order 成為跨程序差異或簽章／稽核不一致來源。
    /// </summary>
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

    /// <summary>
    /// 從 bounded byte array 解析 request，拒絕空值、超限、非法 UTF-8、重複欄位、未知 kind 與 trailing data。
    /// 回傳前再次執行完整 envelope validation，確保 parser 與 object contract 不能互相繞過。
    /// </summary>
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

    /// <summary>
    /// 只檢查固定 magic 來判斷訊息種類；payload 仍須由對應 Deserialize 方法完成完整 shape 驗證後才能使用。
    /// 未知 magic 一律 fail closed，不推測或 fallback 成其他生命週期訊息。
    /// </summary>
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

    /// <summary>序列化與 process nonce、package lock、generation 及 CE 版本綁定的 Ready 證據。</summary>
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

    /// <summary>解析 Ready 證據並精確驗證 WorkerKind 與 CE 版本對應，不接受跨版本混用。</summary>
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

    /// <summary>
    /// 序列化互斥的成功／失敗 response shape：成功只允許 bounded Result，失敗只允許 sanitized error code。
    /// </summary>
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

    /// <summary>解析 response，精確驗證 request ID、outcome 與 Result/ErrorCode 互斥契約。</summary>
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

    /// <summary>序列化具有 finite absolute deadline 的 drain 命令，避免背景排空無限延長。</summary>
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

    /// <summary>解析 drain 命令並驗證 protocol、nonce 與 UTC deadline 範圍。</summary>
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
        // 先檢查深度再遞迴，且 writer 會在每次寫入前檢查剩餘 byte；兩道邊界可避免
        // 深層 value tree 與大型 scalar 在 MemoryStream 擴張後才被拒絕。
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

    /// <summary>
    /// 單次序列化專用的受界限 writer。它是 <see cref="MemoryStream"/> 與 scalar scratch buffer 的
    /// 唯一 owner；每次寫入前先驗證剩餘容量，完成後由外層 <c>using</c> 決定性清除 scratch 並釋放
    /// stream。實例不會進入 static、cache、session 或背景工作，因此不保留前一要求的 envelope 資料。
    /// </summary>
    private sealed class BoundedEnvelopeWriter : IDisposable
    {
        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private readonly int _maximumBytes;
        private readonly MemoryStream _stream;
        private readonly byte[] _scratch = new byte[sizeof(long)];

        /// <summary>
        /// 建立單次使用的 writer。初始配置最多 4 KiB 以降低小 envelope 的配置成本；所有後續成長
        /// 仍受 <paramref name="maximumBytes"/> 限制，不能因 <see cref="MemoryStream"/> 自動擴張而越界。
        /// </summary>
        /// <param name="maximumBytes">完整 envelope 可佔用的部署端固定最大位元組數。</param>
        internal BoundedEnvelopeWriter(int maximumBytes)
        {
            // 4 KiB 是初始容量而非可接受上限；Stream 成長始終受 EnsureRemaining 控制。
            // Writer 是一次序列化的唯一 owner，Dispose 時同時清除 scratch 並釋放 Stream。
            _maximumBytes = maximumBytes;
            _stream = new MemoryStream(Math.Min(maximumBytes, 4096));
        }

        /// <summary>
        /// 寫入一個 protocol byte；先做容量檢查，確保拒絕發生在 stream 擴張之前。
        /// </summary>
        /// <param name="value">要寫入的固定 protocol byte。</param>
        internal void WriteByte(byte value)
        {
            EnsureRemaining(1);
            _stream.WriteByte(value);
        }

        /// <summary>
        /// 寫入固定、呼叫端擁有的 byte array。writer 不保存陣列參考，呼叫返回後只由自己的
        /// bounded stream 持有複本，並在 Dispose 時釋放。
        /// </summary>
        /// <param name="value">要複製到 envelope 的受界限位元組。</param>
        internal void WriteBytes(byte[] value)
        {
            EnsureRemaining(value.Length);
            _stream.Write(value, 0, value.Length);
        }

        /// <summary>
        /// 以 big-endian 寫入 32-bit 整數。共用的 8-byte scratch 只屬於此 writer 實例，寫入完成後
        /// 不跨 invocation 暴露，且 Dispose 會主動清零。
        /// </summary>
        /// <param name="value">要以網路位元序保存的整數。</param>
        internal void WriteInt32(int value)
        {
            _scratch[0] = (byte)(value >> 24);
            _scratch[1] = (byte)(value >> 16);
            _scratch[2] = (byte)(value >> 8);
            _scratch[3] = (byte)value;
            EnsureRemaining(sizeof(int));
            _stream.Write(_scratch, 0, sizeof(int));
        }

        /// <summary>
        /// 以 big-endian 寫入 64-bit 整數；容量驗證先於 stream write，避免在超限後才配置 backing buffer。
        /// </summary>
        /// <param name="value">要以網路位元序保存的長整數。</param>
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

        /// <summary>
        /// 以嚴格 UTF-8 編碼字串，先計算 byte count 並同時套用欄位上限與完整 frame 上限。
        /// 暫時 byte array 僅活在方法內，不被快取或寫入共享狀態；非法 surrogate 會由嚴格 encoder 拒絕。
        /// </summary>
        /// <param name="value">要序列化的已驗證字串。</param>
        /// <param name="maximumUtf8Bytes">此欄位可使用的最大 UTF-8 位元組數。</param>
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

        /// <summary>
        /// 建立完成 envelope 的獨立 byte array；空 envelope fail closed。回傳陣列的 owner 轉交呼叫端，
        /// writer 隨後仍可獨立 Dispose 自己的 stream，不會讓回傳值引用已釋放的 backing buffer。
        /// </summary>
        /// <returns>由呼叫端擁有的完整 envelope 複本。</returns>
        internal byte[] ToArray()
        {
            if (_stream.Length == 0)
            {
                throw InvalidEnvelope("The worker request envelope is empty.");
            }

            return _stream.ToArray();
        }

        /// <summary>
        /// 依序清除 invocation-local scalar scratch，再釋放唯一的 <see cref="MemoryStream"/> owner。
        /// 方法不把 stream 回收到共用 pool，避免不確定或未來可能含敏感值的 buffer 被下一要求重用。
        /// </summary>
        public void Dispose()
        {
            // scratch 可能暫存 protocol scalar；雖不應包含 credential，仍主動清除並立即
            // Dispose MemoryStream，避免 byte buffer 延長至下一次 GC 或被未來修改誤用。
            Array.Clear(_scratch, 0, _scratch.Length);
            _stream.Dispose();
        }

        /// <summary>
        /// 在任何配置或寫入前，以避免整數溢位的減法形式驗證剩餘 frame 容量。
        /// </summary>
        /// <param name="additionalBytes">本次即將追加的位元組數。</param>
        private void EnsureRemaining(int additionalBytes)
        {
            if (additionalBytes < 0 ||
                _stream.Length > _maximumBytes - additionalBytes)
            {
                throw FrameTooLarge();
            }
        }
    }

    /// <summary>
    /// 單次反序列化專用的受界限 reader。它只借用呼叫端 payload 並維護 method-local offset／累計器，
    /// 不建立 stream、timer、registration 或背景 Task。所有集合在配置前先驗證深度與總量上限，
    /// 以防惡意或損壞 frame 造成無界配置與記憶體保留。
    /// </summary>
    private sealed class BoundedEnvelopeReader
    {
        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private readonly byte[] _payload;
        private readonly WorkerProtocolLimits _limits;
        private int _offset;
        private int _totalItems;
        private int _totalMembers;

        /// <summary>
        /// 建立只存活於一次 Deserialize 呼叫的 reader。<paramref name="payload"/> 仍由呼叫端擁有，
        /// reader 不修改、不複製到共享狀態，也不延長其生命週期；limits 為 immutable protocol policy。
        /// </summary>
        /// <param name="payload">已通過 frame 大小檢查的完整 envelope。</param>
        /// <param name="limits">部署端固定且可安全共用的 protocol 上限。</param>
        internal BoundedEnvelopeReader(byte[] payload, WorkerProtocolLimits limits)
        {
            // Reader 只借用呼叫端 payload，沒有 Stream、Timer 或背景 Task；整個狀態僅存活於
            // 單次 Deserialize call，不可存入 static/cache，因此不會跨要求保留 frame 資料。
            _payload = payload;
            _limits = limits;
        }

        /// <summary>
        /// 驗證目前位置的固定 magic bytes；不足或不符立即 fail closed，不嘗試猜測其他訊息種類。
        /// </summary>
        /// <param name="expected">對應具名 envelope 類型的固定 magic。</param>
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

        /// <summary>
        /// 讀取 big-endian 32-bit 整數。方法先證明剩餘長度，再推進 invocation-local offset。
        /// </summary>
        /// <returns>解碼後的 32-bit 整數。</returns>
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

        /// <summary>
        /// 讀取 big-endian 64-bit 整數；固定八次迴圈不配置暫存陣列，兼顧界限與低配置效能。
        /// </summary>
        /// <returns>解碼後的 64-bit 整數。</returns>
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

        /// <summary>
        /// 讀取 length-prefixed 嚴格 UTF-8 字串。負長度、欄位超限、frame 截斷或非法 UTF-8 均在
        /// 回傳字串前 fail closed，且錯誤不回顯原始 payload 或可能的敏感內容。
        /// </summary>
        /// <param name="maximumUtf8Bytes">此欄位可接受的最大 UTF-8 位元組數。</param>
        /// <returns>完成嚴格解碼、由本次 object graph 擁有的字串。</returns>
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

        /// <summary>
        /// 讀取具有 ordinal key 與唯一名稱的物件。單一 object 數量與整棵樹累計 member 數都在配置前
        /// 受限，避免以大量小 object 繞過總量上限；失敗時不保留部分 dictionary 到共享狀態。
        /// </summary>
        /// <param name="depth">目前 value tree 深度。</param>
        /// <returns>只屬於本次反序列化結果的 bounded member dictionary。</returns>
        internal IReadOnlyDictionary<string, WorkerValue> ReadObject(int depth)
        {
            if (depth > _limits.MaximumValueDepth)
            {
                throw LimitExceeded("The worker value nesting limit was exceeded.");
            }

            var count = ReadBoundedCount(
                _limits.MaximumObjectMembers,
                "object member");
            // 除單一 object 上限外再累計整棵樹的 member 數，防止多個小 object 疊加成
            // 大型保留圖而繞過 allocation bound。
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

        /// <summary>
        /// 要求 offset 精確位於 payload 結尾，拒絕尾隨資料、黏包或隱藏的第二個訊息。
        /// </summary>
        internal void RequireEnd()
        {
            if (_offset != _payload.Length)
            {
                throw InvalidEnvelope("The worker request envelope contains trailing data.");
            }
        }

        /// <summary>
        /// 依封閉 kind allowlist 讀取一個 value。每個 scalar／array／object 分支在配置前套用深度、
        /// byte 與全樹累計上限，不接受未知 kind 或混合 shape。
        /// </summary>
        /// <param name="depth">目前 value tree 深度。</param>
        /// <returns>由本次反序列化 object graph 單獨擁有的 <see cref="WorkerValue"/>。</returns>
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
                    // 與 object member 相同，累計整棵樹的 item 數，避免巢狀小陣列繞過總量上限。
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

        /// <summary>在先證明剩餘長度後讀取單一 byte，並只推進本 reader 的 offset。</summary>
        /// <returns>目前位置的 byte。</returns>
        private byte ReadByte()
        {
            EnsureAvailable(1);
            return _payload[_offset++];
        }

        /// <summary>
        /// 讀取非負且不超過具名上限的 count；錯誤只輸出固定 category，不回顯 payload。
        /// </summary>
        /// <param name="maximum">此集合允許的最大元素數。</param>
        /// <param name="category">供固定錯誤訊息使用的非敏感類別。</param>
        /// <returns>已驗證、可安全用於 bounded 配置的元素數。</returns>
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

        /// <summary>
        /// 以避免整數溢位的減法形式驗證 payload 剩餘長度，截斷 frame 在任何讀取前 fail closed。
        /// </summary>
        /// <param name="byteCount">後續操作需要的位元組數。</param>
        private void EnsureAvailable(int byteCount)
        {
            if (byteCount < 0 || _offset > _payload.Length - byteCount)
            {
                throw InvalidEnvelope("The worker request envelope is incomplete.");
            }
        }
    }
}
