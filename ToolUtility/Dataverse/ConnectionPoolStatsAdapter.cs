/*
 * 檔案整體說明
 *
 * 目的與定位：將新的 Dataverse pool metrics 映射為舊有 Controller 契約的唯讀相容層。 16 個 Controller 仍可注入 ICrmConnectionPool，但本 adapter
 * 不會 暴露 raw client；舊式取得、歸還與驗證 API 一律拒絕，避免繞過 Gateway 的 lease 邊界。 此型別是 Singleton，僅保存 manager 參考，不擁有或釋放 manager、pool
 * 或 client。
 *
 * 主要功能與流程：
 * 本檔宣告 ConnectionPoolStatsAdapter。
 * - 建立 metrics 相容層；manager 的生命週期由 DI Singleton 擁有。
 * - 舊式 raw client 入口已由 Gateway 取代，明確拒絕以防止繞過隔離與清理契約。
 * - 舊式 raw client 歸還入口已停用；lease 的歸還只能由 Gateway／pool 內部完成。
 * - 將新 pool 的計數映射成既有診斷端點使用的 ConnectionPoolStats。 映射只複製數值，不保存任何 client、租約、身分或 request 資料。
 * 主要操作入口包含 AcquireConnection、ReleaseConnection、GetStats、ValidateConnection、Dispose。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ToolUtility/DependencyInjection/ServiceCollectionExtensions.cs 追查目前工作樹中的合作／使用位置。
 */
using System;
using Microsoft.Xrm.Sdk;
using ToolUtilityNameSpace.ConnectionOperations;

namespace ToolUtilityNameSpace.Dataverse;

/// <summary>
/// 將新的 Dataverse pool metrics 映射為舊有 Controller 契約的唯讀相容層。
/// 16 個 Controller 仍可注入 <see cref="ICrmConnectionPool"/>，但本 adapter 不會
/// 暴露 raw client；舊式取得、歸還與驗證 API 一律拒絕，避免繞過 Gateway 的 lease 邊界。
/// 此型別是 Singleton，僅保存 manager 參考，不擁有或釋放 manager、pool 或 client。
/// </summary>
public sealed class ConnectionPoolStatsAdapter : ICrmConnectionPool
{
    private readonly IDataverseConnectionManager _manager;
    private readonly DateTime _createdAtUtc = DateTime.UtcNow;

    /// <summary>
    /// 建立 metrics 相容層；manager 的生命週期由 DI Singleton 擁有。
    /// </summary>
    /// <param name="manager">新的 Dataverse Singleton connection manager。</param>
    /// <exception cref="ArgumentNullException">manager 未提供時擲回。</exception>
    public ConnectionPoolStatsAdapter(IDataverseConnectionManager manager)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
    }

    /// <summary>
    /// 舊式 raw client 入口已由 Gateway 取代，明確拒絕以防止繞過隔離與清理契約。
    /// </summary>
    public IOrganizationService AcquireConnection()
    {
        throw new NotSupportedException(
            "請透過 IDataverseGateway 執行 Dataverse 操作；ICrmConnectionPool 不再暴露 raw client。");
    }

    /// <summary>
    /// 舊式 raw client 歸還入口已停用；lease 的歸還只能由 Gateway／pool 內部完成。
    /// </summary>
    public void ReleaseConnection(IOrganizationService service)
    {
        throw new NotSupportedException(
            "請透過 IDataverseGateway 執行 Dataverse 操作；ICrmConnectionPool 不再接受 raw client。");
    }

    /// <summary>
    /// 將新 pool 的計數映射成既有診斷端點使用的 ConnectionPoolStats。
    /// 映射只複製數值，不保存任何 client、租約、身分或 request 資料。
    /// </summary>
    public ConnectionPoolStats GetStats()
    {
        var metrics = _manager.GetMetrics();
        return new ConnectionPoolStats
        {
            TotalConnections = metrics.Idle + metrics.Leased,
            ActiveConnections = metrics.Leased,
            IdleConnections = metrics.Idle,
            WaitingRequests = metrics.Waiting,
            CreatedAt = _createdAtUtc,
            LastActivityAt = DateTime.UtcNow,
            TotalAcquireCount = metrics.TotalAcquires,
            TotalReleaseCount = metrics.TotalReleases,
            TimeoutCount = metrics.AcquireTimeouts,
            ValidationFailureCount = metrics.Faulted
        };
    }

    /// <summary>
    /// 舊式直接驗證入口已停用，避免呼叫端取得並操作 pool client。
    /// </summary>
    public bool ValidateConnection(IOrganizationService service)
    {
        throw new NotSupportedException(
            "請透過 IDataverseGateway 執行 Dataverse 操作；ICrmConnectionPool 不再驗證 raw client。");
    }

    /// <summary>
    /// adapter 不擁有 manager 或 pool，因此不在此處 Dispose 長命資源；
    /// DI 會在應用程式關閉時依 Singleton 擁有權釋放 manager。
    /// </summary>
    public void Dispose()
    {
        // 刻意留空：短命／相容層不得釋放其未建立的長命 manager。
    }
}
