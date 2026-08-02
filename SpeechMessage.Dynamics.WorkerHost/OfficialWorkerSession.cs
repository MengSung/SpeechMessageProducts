using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SpeechMessage.Dynamics.WorkerProtocol;

namespace SpeechMessage.Dynamics.WorkerHost;

/// <summary>
/// 擁有單一官方 CRM client 與一條 bounded Worker IPC session 的完整生命週期。
/// Session 依序建立 client、發布 READY、處理 request/drain，最後在所有成功、取消、protocol failure
/// 與 unexpected failure 路徑上恰好嘗試一次 client disposal；它不把 Credential、Token、Endpoint、
/// caller Session 或 CRM SDK object 保存到 static、cache 或跨 request collection。
/// </summary>
public sealed class OfficialWorkerSession
{
    private readonly IOfficialCrmClientFactory _clientFactory;
    private readonly WorkerBootstrapArguments _bootstrap;
    private readonly string _ceVersion;
    private readonly IReadOnlyDictionary<string, string> _operationRevisions;
    private readonly ISet<string> _allowedOperationIds;
    private readonly WorkerProtocolLimits _limits;
    private readonly WorkerEnvelopeCodec _codec;
    private readonly Func<DateTimeOffset> _utcNow;

    /// <summary>
    /// 建立一個由呼叫端唯一擁有的 Worker session。
    /// 若 revision map 包含 Package01 fee operation，codec 必須使用其共享
    /// <see cref="Package01FeeWorkerContract.ProtocolLimits"/>，確保合法最大 17,604 個巢狀 array item
    /// 在 Worker 與 Supervisor 兩端採用相同邊界；identity-only session 則保留 caller 指定 limits。
    /// </summary>
    /// <param name="clientFactory">為此 process generation 建立唯一 CRM client 的 factory。</param>
    /// <param name="bootstrap">已驗證的 nonce、worker kind、package lock 與 profile generation。</param>
    /// <param name="ceVersion">必須與 worker kind 完全一致的 CE 版本。</param>
    /// <param name="operationRevisions">operation ID 到 immutable revision 的 ordinal allowlist。</param>
    /// <param name="limits">identity-only session 使用的 protocol limits。</param>
    /// <param name="utcNow">可測試的 UTC clock；不會保存 request 或 caller identity。</param>
    public OfficialWorkerSession(
        IOfficialCrmClientFactory clientFactory,
        WorkerBootstrapArguments bootstrap,
        string ceVersion,
        IReadOnlyDictionary<string, string> operationRevisions,
        WorkerProtocolLimits limits,
        Func<DateTimeOffset>? utcNow = null)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _bootstrap = bootstrap ?? throw new ArgumentNullException(nameof(bootstrap));
        _ceVersion = ceVersion ?? throw new ArgumentNullException(nameof(ceVersion));
        _operationRevisions = operationRevisions is null
            ? throw new ArgumentNullException(nameof(operationRevisions))
            : operationRevisions.ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.Ordinal);
        _allowedOperationIds = new HashSet<string>(
            _operationRevisions.Keys,
            StringComparer.Ordinal);
        var requestedLimits = limits ?? throw new ArgumentNullException(nameof(limits));
        _limits = _operationRevisions.ContainsKey(
            Package01FeeWorkerContract.CapabilityOperationId)
            ? Package01FeeWorkerContract.ProtocolLimits
            : requestedLimits;
        _codec = new WorkerEnvelopeCodec(_limits);
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);

        var expectedCeVersion = bootstrap.WorkerKind switch
        {
            OfficialWorkerKind.OfficialCrm82Worker => "8.2",
            OfficialWorkerKind.OfficialCrm91Worker => "9.1",
            _ => throw new ArgumentOutOfRangeException(nameof(bootstrap))
        };
        if (!string.Equals(_ceVersion, expectedCeVersion, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The worker CE version does not match the official worker kind.",
                nameof(ceVersion));
        }
    }

    /// <summary>
    /// 執行 READY、request 與 drain state machine，並在 session 結束前決定性釋放 client。
    /// cancellation 只映射為固定 exit code；protocol failure 不會被誤包裝為普通 CRM failure；
    /// finally disposal 永遠在 message loop 停止後執行，避免 client 與同步 SDK 呼叫交錯釋放。
    /// </summary>
    /// <param name="input">由 session 借用、但不負責 disposal 的可讀 IPC stream。</param>
    /// <param name="output">由 session 借用、但不負責 disposal 的可寫 IPC stream。</param>
    /// <param name="cancellationToken">終止整個 session 的 bounded cancellation token。</param>
    /// <returns>不含 upstream 敏感內容的固定 Worker exit code。</returns>
    public async Task<OfficialWorkerSessionExitCode> RunAsync(
        Stream input,
        Stream output,
        CancellationToken cancellationToken)
    {
        if (input is null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        if (output is null)
        {
            throw new ArgumentNullException(nameof(output));
        }

        IOfficialCrmClient? client = null;
        var exitCode = OfficialWorkerSessionExitCode.UnexpectedFailure;
        try
        {
            client = _clientFactory.Create(_bootstrap.ProfileGenerationId);
            if (client is null || !client.IsReady)
            {
                exitCode = OfficialWorkerSessionExitCode.ClientNotReady;
            }
            else
            {
                await PublishReadyAsync(output, cancellationToken).ConfigureAwait(false);
                exitCode = await RunMessageLoopAsync(
                    client,
                    input,
                    output,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            exitCode = OfficialWorkerSessionExitCode.Cancelled;
        }
        catch (WorkerProtocolException)
        {
            exitCode = OfficialWorkerSessionExitCode.ProtocolFailure;
        }
        catch
        {
            exitCode = OfficialWorkerSessionExitCode.UnexpectedFailure;
        }
        finally
        {
            if (client is not null)
            {
                try
                {
                    // message loop 已終止後才釋放唯一 client owner；若 disposal 失敗，固定 exit code
                    // 覆蓋先前結果，避免回報 clean drain 卻遺留 SDK/WCF resource ownership。
                    client.Dispose();
                }
                catch
                {
                    exitCode = OfficialWorkerSessionExitCode.ClientDisposeFailure;
                }
            }
        }

        return exitCode;
    }

    /// <summary>
    /// 在 client readiness 已通過後發布一次 nonce/package/profile-bound READY frame。
    /// 方法不取得 stream ownership；任何寫入失敗由外層 session lifecycle 統一結束與清理。
    /// </summary>
    private async Task PublishReadyAsync(
        Stream output,
        CancellationToken cancellationToken)
    {
        var ready = new WorkerReadyV1(
            WorkerProtocolVersion.Current,
            _bootstrap.ProcessNonce,
            _bootstrap.WorkerKind,
            _bootstrap.PackageLockId,
            _bootstrap.ProfileGenerationId,
            _ceVersion);
        await WorkerFrameCodec.WriteAsync(
            output,
            _codec.SerializeReady(ready),
            _limits.MaximumFrameBytes,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 以單一 bounded request-ID set 依序處理 request，直到收到有效 drain。
    /// collection 只存當前 session 的 GUID，並在每次 request finally 移除，session 返回後即可回收。
    /// </summary>
    private async Task<OfficialWorkerSessionExitCode> RunMessageLoopAsync(
        IOfficialCrmClient client,
        Stream input,
        Stream output,
        CancellationToken cancellationToken)
    {
        var activeRequestIds = new HashSet<Guid>();
        while (true)
        {
            var payload = await WorkerFrameCodec.ReadAsync(
                input,
                _limits.MaximumFrameBytes,
                cancellationToken).ConfigureAwait(false);
            switch (_codec.DetectMessageKind(payload))
            {
                case WorkerMessageKind.Request:
                    await ExecuteRequestAsync(
                        client,
                        payload,
                        output,
                        activeRequestIds,
                        cancellationToken).ConfigureAwait(false);
                    break;
                case WorkerMessageKind.Drain:
                    var drain = _codec.DeserializeDrain(payload);
                    if (!string.Equals(
                            drain.ProcessNonce,
                            _bootstrap.ProcessNonce,
                            StringComparison.Ordinal) ||
                        drain.DeadlineUtcTicks <= _utcNow().UtcDateTime.Ticks)
                    {
                        return OfficialWorkerSessionExitCode.ProtocolFailure;
                    }

                    return OfficialWorkerSessionExitCode.CleanDrain;
                default:
                    return OfficialWorkerSessionExitCode.ProtocolFailure;
            }
        }
    }

    /// <summary>
    /// 驗證 generation/revision/nonce/deadline，正規化 operation-specific request，執行 client，
    /// 再驗證完整 result 後才序列化。Package01 overflow 映射為固定 result-too-large；一般 client
    /// 例外維持 generic upstream failure；malformed request/result 則重新拋出 protocol failure。
    /// active request ID 無論哪一條路徑都在 finally 移除，避免重複 ID map retention。
    /// </summary>
    private async Task ExecuteRequestAsync(
        IOfficialCrmClient client,
        byte[] payload,
        Stream output,
        ISet<Guid> activeRequestIds,
        CancellationToken cancellationToken)
    {
        var request = _codec.DeserializeRequest(payload);
        if (!string.Equals(
                request.ProfileGenerationId,
                _bootstrap.ProfileGenerationId,
                StringComparison.Ordinal) ||
            !_operationRevisions.TryGetValue(
                request.CapabilityOperationId,
                out var expectedRevision) ||
            !string.Equals(
                request.OperationDefinitionRevision,
                expectedRevision,
                StringComparison.Ordinal))
        {
            throw new WorkerProtocolException(
                WorkerProtocolFailureCategory.InvalidEnvelope,
                "The worker request generation or revision is invalid.");
        }

        WorkerRequestValidator.ValidateAndRegister(
            request,
            _bootstrap.ProcessNonce,
            _utcNow(),
            _allowedOperationIds,
            activeRequestIds,
            _limits);

        WorkerResponseV1 response;
        try
        {
            var executionRequest = OfficialWorkerOperations.PrepareRequestForExecution(request);
            var result = client.Execute(executionRequest);
            if (result is null)
            {
                response = WorkerResponseV1.Failure(
                    WorkerProtocolVersion.Current,
                    _bootstrap.ProcessNonce,
                    request.RequestId,
                    WorkerResponseOutcome.UpstreamFailure,
                    "crm.operation.empty-result");
            }
            else
            {
                OfficialWorkerOperations.ValidateResult(
                    executionRequest.CapabilityOperationId,
                    result);
                response = WorkerResponseV1.Success(
                    WorkerProtocolVersion.Current,
                    _bootstrap.ProcessNonce,
                    request.RequestId,
                    result);
            }
        }
        catch (OfficialWorkerResultLimitExceededException)
        {
            response = WorkerResponseV1.Failure(
                WorkerProtocolVersion.Current,
                _bootstrap.ProcessNonce,
                request.RequestId,
                WorkerResponseOutcome.UpstreamFailure,
                "crm.operation.result-too-large");
        }
        catch (WorkerProtocolException)
        {
            // Malformed request/result 代表 Worker boundary 已失去可信 shape，必須讓外層 session
            // 以 ProtocolFailure 結束；不可降級成可繼續使用同一 worker 的普通 upstream failure。
            throw;
        }
        catch
        {
            response = WorkerResponseV1.Failure(
                WorkerProtocolVersion.Current,
                _bootstrap.ProcessNonce,
                request.RequestId,
                WorkerResponseOutcome.UpstreamFailure,
                "crm.operation.failed");
        }
        finally
        {
            activeRequestIds.Remove(request.RequestId);
        }

        await WorkerFrameCodec.WriteAsync(
            output,
            _codec.SerializeResponse(response),
            _limits.MaximumFrameBytes,
            cancellationToken).ConfigureAwait(false);
    }
}
