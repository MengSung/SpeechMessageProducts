using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SpeechMessage.Dynamics.WorkerProtocol;

namespace SpeechMessage.Dynamics.WorkerHost;

/// <summary>
/// 組合單一官方 CRM Worker 程序的 bootstrap、具名管線與 client session。
/// 本類別先驗證可執行檔種類及 package-lock 身分，才允許取得任何 Pipe、
/// Profile 或 CRM client 資源；成功取得的資料流由本類別唯一擁有並在所有
/// 完成、取消及失敗路徑確定釋放，CRM client 則由 <see cref="OfficialWorkerSession"/>
/// 建立及恰好釋放一次。
/// </summary>
public sealed class OfficialWorkerProcessHost
{
    private readonly OfficialWorkerKind _expectedWorkerKind;
    private readonly string _expectedPackageLockId;
    private readonly string _ceVersion;
    private readonly IOfficialCrmClientFactory _clientFactory;
    private readonly IOfficialWorkerPipeConnector _pipeConnector;
    private readonly IReadOnlyDictionary<string, string> _operationRevisions;
    private readonly WorkerProtocolLimits _limits;

    /// <summary>
    /// 建立一個固定 CE 種類與 package-lock 身分的 Worker composition host。
    /// 所有參數都是部署時不可變的非機密資料；不得從請求或使用者 session 改寫。
    /// </summary>
    public OfficialWorkerProcessHost(
        OfficialWorkerKind expectedWorkerKind,
        string expectedPackageLockId,
        string ceVersion,
        IOfficialCrmClientFactory clientFactory,
        IOfficialWorkerPipeConnector pipeConnector,
        IReadOnlyDictionary<string, string> operationRevisions,
        WorkerProtocolLimits? limits = null)
    {
        if (!Enum.IsDefined(typeof(OfficialWorkerKind), expectedWorkerKind))
        {
            throw new ArgumentOutOfRangeException(nameof(expectedWorkerKind));
        }

        _expectedWorkerKind = expectedWorkerKind;
        _expectedPackageLockId = string.IsNullOrWhiteSpace(expectedPackageLockId)
            ? throw new ArgumentException(
                "The expected package-lock identifier is required.",
                nameof(expectedPackageLockId))
            : expectedPackageLockId;
        _ceVersion = string.IsNullOrWhiteSpace(ceVersion)
            ? throw new ArgumentException(
                "The CE version is required.",
                nameof(ceVersion))
            : ceVersion;
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _pipeConnector = pipeConnector ?? throw new ArgumentNullException(nameof(pipeConnector));
        _operationRevisions = operationRevisions ??
            throw new ArgumentNullException(nameof(operationRevisions));
        _limits = limits ?? WorkerProtocolLimits.Default;
    }

    /// <summary>
    /// 驗證 bootstrap 後執行單一 Worker session。
    /// 身分不符會在配置 Pipe、Profile、Credential 或 CRM client 前 fail closed；
    /// 一旦取得 Pipe，無論 session 回傳、取消或拋錯，都由此方法確定釋放。
    /// </summary>
    /// <param name="arguments">監督程序提供的已界定非機密 bootstrap 參數。</param>
    /// <param name="cancellationToken">終止 Worker session 的擁有者取消訊號。</param>
    /// <returns>不含上游細節的 Worker session 結束分類。</returns>
    public async Task<OfficialWorkerSessionExitCode> RunAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var bootstrap = WorkerBootstrapArguments.Parse(arguments);
        ValidateExecutableIdentity(bootstrap);

        using (var pipe = _pipeConnector.Connect(bootstrap.PipeName))
        {
            if (pipe is null)
            {
                throw new WorkerProtocolException(
                    WorkerProtocolFailureCategory.InvalidEnvelope,
                    "The worker pipe connection is unavailable.");
            }

            var session = new OfficialWorkerSession(
                _clientFactory,
                bootstrap,
                _ceVersion,
                _operationRevisions,
                _limits);
            return await session.RunAsync(
                pipe,
                pipe,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private void ValidateExecutableIdentity(WorkerBootstrapArguments bootstrap)
    {
        if (bootstrap.WorkerKind != _expectedWorkerKind ||
            !string.Equals(
                bootstrap.PackageLockId,
                _expectedPackageLockId,
                StringComparison.Ordinal))
        {
            throw new WorkerProtocolException(
                WorkerProtocolFailureCategory.InvalidEnvelope,
                "The worker executable identity does not match its immutable package lock.");
        }
    }
}
