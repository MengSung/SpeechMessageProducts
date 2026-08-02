// ============================================================================
// 檔案：SpeechMessage.Dynamics.ControlPlane/Runtime/DynamicsProfileRuntimeFactory.cs
// 目的：建立官方 Worker-backed Runtime Generation，並在任一步驟失敗時反向 rollback 全部資源。
// ============================================================================

using System.Runtime.ExceptionServices;
using SpeechMessage.Dynamics.ControlPlane.Capacity;
using SpeechMessage.Dynamics.WorkerSupervisor;

namespace SpeechMessage.Dynamics.ControlPlane.Runtime;

/// <summary>
/// 官方 NuGet Worker Runtime Factory 的預設實作。
/// 每次 <see cref="CreateAsync"/> 都建立新的 admission registration、有限 Worker process 集合、named pipes、
/// process handles、stream drain tasks 與 retirement state；Factory 自身只保存 registry reference，
/// 不保存 Request、User、Session、Credential、Token、Worker result 或跨 generation mutable cache。
/// </summary>
public sealed class DynamicsProfileRuntimeFactory : IDynamicsProfileRuntimeFactory
{
    private readonly IOrganizationAdmissionRegistry _admissionRegistry;

    /// <summary>
    /// 建立 Factory。Registry 是 Organization capacity 的唯一共用邊界；Worker executor、Process、Pipe 與
    /// CRM SDK/WCF state 永遠不放入 Registry，也不由 Factory 跨呼叫快取。
    /// </summary>
    /// <param name="admissionRegistry">建立引用計數 admission registration 的 host-owned registry。</param>
    public DynamicsProfileRuntimeFactory(IOrganizationAdmissionRegistry admissionRegistry)
    {
        _admissionRegistry = admissionRegistry ??
            throw new ArgumentNullException(nameof(admissionRegistry));
    }

    /// <summary>
    /// 建立一個尚未發布但已完成全部 Worker READY handshake 的隔離 Runtime Generation。
    /// 方法先取得 admission registration，再依設定數量逐一啟動 Worker；每個 Worker 都驗證 executable hash、
    /// nonce、package lock、CE kind 與 worker-profile.xml generation identity。只有 Runtime 成功建構後 ownership 才轉移。
    /// </summary>
    /// <remarks>
    /// 任一 Worker 啟動、取消或 Runtime 建構失敗時，Factory 會先反向終止所有已啟動 Worker，
    /// 再釋放 admission registration；每一個 cleanup 都會被嘗試。原始失敗保持第一原因，
    /// cleanup failure 只以 AggregateException 附加，避免半建立 Process、Pipe、Registration 或 Host Slot 洩漏。
    /// </remarks>
    public async Task<IDynamicsProfileRuntime> CreateAsync(
        DynamicsProfileDefinition definition,
        long generation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (generation < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(generation),
                "Runtime generation must be at least one.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        IOrganizationAdmissionRegistration? registration = null;
        var workers = new List<OfficialWorkerProfileExecutor>(definition.WorkerCount);
        try
        {
            registration = _admissionRegistry.Acquire(definition.AdmissionPlan);
            for (var index = 0; index < definition.WorkerCount; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // 每次都建立全新的 options instance；雖然 worker-local profile identity 相同，
                // Process nonce、pipe name、operation gate 與 SDK/WCF graph 仍由 Supervisor 個別擁有。
                var worker = await OfficialWorkerProfileExecutor.StartAsync(
                    definition.CreateWorkerOptions(),
                    cancellationToken).ConfigureAwait(false);
                workers.Add(worker);
            }

            var key = new ProfileRuntimeKey(
                definition.ProfileAlias,
                generation,
                definition.CeVersion,
                definition.AdmissionPlan.CanonicalKey);
            var runtime = new DynamicsProfileRuntime(
                key,
                definition,
                registration,
                workers);

            registration = null;
            workers.Clear();
            return runtime;
        }
        catch (Exception creationFailure)
        {
            var failures = new List<Exception> { creationFailure };
            for (var index = workers.Count - 1; index >= 0; index--)
            {
                await CaptureRollbackFailureAsync(
                    workers[index],
                    static worker => worker.DisposeAsync().AsTask(),
                    failures).ConfigureAwait(false);
            }

            await CaptureRollbackFailureAsync(
                registration,
                static ownedRegistration => ownedRegistration.DisposeAsync().AsTask(),
                failures).ConfigureAwait(false);

            if (failures.Count == 1)
            {
                ExceptionDispatchInfo.Capture(creationFailure).Throw();
            }

            throw new AggregateException(
                "Official worker profile runtime construction and one or more rollback operations failed.",
                failures);
        }
    }

    /// <summary>
    /// 對一項已取得 ownership 的部分資源執行 rollback 並收集失敗。
    /// null 代表該步驟尚未建立資源；方法本身不重新拋出，確保後續 Worker/Registration 仍會被清理。
    /// </summary>
    private static async Task CaptureRollbackFailureAsync<TResource>(
        TResource? resource,
        Func<TResource, Task> disposeAsync,
        ICollection<Exception> failures)
        where TResource : class
    {
        if (resource is null)
        {
            return;
        }

        try
        {
            await disposeAsync(resource).ConfigureAwait(false);
        }
        catch (AggregateException aggregateException)
        {
            foreach (var cleanupFailure in aggregateException.Flatten().InnerExceptions)
            {
                failures.Add(cleanupFailure);
            }
        }
        catch (Exception cleanupFailure)
        {
            failures.Add(cleanupFailure);
        }
    }
}
