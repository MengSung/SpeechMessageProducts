using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Dynamics.Crm91Worker;

namespace SpeechMessage.Dynamics.Crm91Worker.Tests;

/// <summary>
/// 提供 CE 9.1 Worker 專用、單一測試案例擁有的同步 SDK client 替身。
/// 替身只保存有界的呼叫次數與目前案例提供的 delegate；每個測試結束後即失去參考，
/// 不建立連線、認證、Session、Timer、背景工作或跨案例 static cache。
/// </summary>
internal sealed class FakeCrm91SdkClient : ICrm91SdkClient
{
    private bool _disposed;

    /// <summary>取得或設定 adapter readiness probe 觀察到的固定狀態。</summary>
    internal bool Ready { get; set; } = true;

    /// <summary>取得或設定用來驗證 CE 9.1 major/minor 的固定組織版本。</summary>
    internal Version? OrganizationVersion { get; set; } = new Version(9, 1, 0, 0);

    /// <summary>
    /// 取得或設定 test-owned SDK startup exception。它只在本測試的 adapter stack scope 內投影為
    /// 固定 enum，替身不保留或模擬 IFD endpoint、Credential、Token 或 CRM payload。
    /// </summary>
    internal Exception? StartupException { get; set; }

    /// <summary>取得或設定同步 Execute 的案例專屬回應工廠。</summary>
    internal Func<OrganizationRequest, OrganizationResponse>? ExecuteHandler { get; set; }

    /// <summary>取得或設定同步 RetrieveMultiple 的案例專屬回應工廠。</summary>
    internal Func<QueryExpression, EntityCollection>? RetrieveMultipleHandler { get; set; }

    /// <summary>取得 Execute 被呼叫的總次數，供 identity probe 與實際 operation 斷言。</summary>
    internal int ExecuteCallCount { get; private set; }

    /// <summary>取得 RetrieveMultiple 被呼叫的總次數，確保 fail-closed 不會多送下一頁。</summary>
    internal int RetrieveMultipleCallCount { get; private set; }

    /// <summary>取得此替身被釋放的次數，驗證 adapter 的唯一 client owner 只 dispose 一次。</summary>
    internal int DisposeCallCount { get; private set; }

    /// <summary>
    /// 回傳測試設定的 readiness；釋放後固定為 false，避免測試替身掩蓋 use-after-dispose。
    /// </summary>
    bool ICrm91SdkClient.IsReady => !_disposed && Ready;

    /// <summary>回傳測試固定的 CE 版本，不解析或保存任何端點或 credential。</summary>
    Version? ICrm91SdkClient.ConnectedOrgVersion => OrganizationVersion;

    /// <summary>回傳案例提供的 startup exception；production classifier 不會輸出其原始 detail。</summary>
    Exception? ICrm91SdkClient.LastStartupException => StartupException;

    /// <summary>
    /// 同步執行 identity request；釋放後立即拒絕，且未設定 handler 時 fail closed。
    /// </summary>
    /// <param name="request">adapter 建立的 SDK request。</param>
    /// <returns>案例提供的 SDK response。</returns>
    OrganizationResponse ICrm91SdkClient.Execute(OrganizationRequest request)
    {
        ThrowIfDisposed();
        ExecuteCallCount++;
        return ExecuteHandler?.Invoke(request) ??
            throw new InvalidOperationException("The fake CE 9.1 Execute handler is unavailable.");
    }

    /// <summary>
    /// 同步執行固定 QueryExpression；只把目前呼叫借給 handler，絕不跨頁保存 mutable query。
    /// </summary>
    /// <param name="query">本次 RetrieveMultiple 的 worker-owned query。</param>
    /// <returns>案例提供的完整 SDK page。</returns>
    EntityCollection ICrm91SdkClient.RetrieveMultiple(QueryExpression query)
    {
        ThrowIfDisposed();
        RetrieveMultipleCallCount++;
        return RetrieveMultipleHandler?.Invoke(query) ??
            throw new InvalidOperationException(
                "The fake CE 9.1 RetrieveMultiple handler is unavailable.");
    }

    /// <summary>
    /// 將替身標記為已釋放；重複呼叫不增加計數，模擬 production client 的 idempotent owner。
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DisposeCallCount++;
    }

    /// <summary>在任何 SDK 模擬呼叫前拒絕已釋放替身，保護 lifecycle assertion。</summary>
    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(FakeCrm91SdkClient));
        }
    }
}
