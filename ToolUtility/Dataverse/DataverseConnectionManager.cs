using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Extensions.Configuration;
using Microsoft.Xrm.Sdk;
using ToolUtilityNameSpace.ConnectionOperations;

namespace ToolUtilityNameSpace.Dataverse;

/// <summary>
/// Singleton Dataverse 連線管理器。
/// 它解析完整 Pool Key、建立 bounded pool，並把 client 建立與 WhoAmI 健康檢查集中在唯一路徑；
/// manager 本身不依賴任何 scoped 服務，存活至 DI 容器關閉並在 <see cref="Dispose"/> 時確定性釋放
/// pool。Pool Key 僅取自受信任的組合根與必要組態，絕不採納 caller 的使用者、tenant 或 profile 資料，
/// 因此不會在跨 request 或跨環境間共用可變連線狀態。
/// </summary>
public sealed class DataverseConnectionManager : IDataverseConnectionManager
{
    private readonly ICrmConnectionService _connectionService;
    private readonly IConfiguration _configuration;
    private readonly string _product;
    private readonly string _environment;
    private readonly string _organizationUrl;
    private readonly string _effectiveIdentity;
    private readonly string _password;
    private int _disposed;

    /// <summary>
    /// 建立 manager 與其唯一 bounded pool。product 與 environment 由組合根傳入，
    /// 不採用呼叫端提供的資料作為隔離權限來源。ServerUrl 與 Username 是完整隔離鍵與
    /// 有效服務身分的必要設定；任一缺漏都會立即拒絕建立，絕不回退到硬編碼環境或帳號。
    /// </summary>
    public DataverseConnectionManager(
        ICrmConnectionService connectionService,
        IConfiguration configuration,
        string product,
        string environment,
        DataversePoolOptions options)
    {
        _connectionService = connectionService ?? throw new ArgumentNullException(nameof(connectionService));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _product = RequireValue(product, nameof(product));
        _environment = RequireValue(environment, nameof(environment));
        options = options ?? throw new ArgumentNullException(nameof(options));

        _organizationUrl = RequireConfigurationValue(
            _configuration["CrmConnection:ServerUrl"],
            "CrmConnection:ServerUrl");
        _effectiveIdentity = RequireConfigurationValue(
            _configuration["CrmConnection:Username"],
            "CrmConnection:Username");
        _password = _configuration["CrmConnection:Password"] ?? string.Empty;
        Pool = new BoundedClientPool(CreateClient, IsHealthy, options);
    }

    /// <summary>
    /// 取得由此 manager 唯一擁有並供組合根註冊的 singleton pool。呼叫端只能透過 lease 操作，
    /// 不得自行 Dispose 底層 client；manager 關閉時會依 pool 的生命週期規則統一釋放資源。
    /// </summary>
    public IBoundedClientPool Pool { get; }

    /// <summary>
    /// 使用從受信任 Product、Environment、OrganizationUrl 與 EffectiveIdentity 組成的完整 key
    /// 取得 lease。呼叫端無法提供或覆寫隔離邊界，因此不可藉此跨 tenant、profile 或環境取得連線。
    /// </summary>
    public IClientLease Acquire(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return Pool.Acquire(new DataverseConnectionKey(
            _product,
            _environment,
            _organizationUrl,
            _effectiveIdentity), cancellationToken);
    }

    /// <summary>取得 pool 的唯讀健康與容量計數，供診斷使用且不暴露 raw client 或任何憑證。</summary>
    public DataversePoolMetrics GetMetrics()
    {
        ThrowIfDisposed();
        return Pool.GetMetrics();
    }

    /// <summary>
    /// 由 DI singleton 關閉流程呼叫，將所有可安全釋放的 pool 資源交給 pool 決定性清理；
    /// 重複呼叫為冪等，且不會將仍被 lease 擁有的 client 交叉釋放。
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        Pool.Dispose();
    }

    private IOrganizationService CreateClient(DataverseConnectionKey key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var service = _connectionService.CreateOnPremiseClient(key.OrganizationUrl, _effectiveIdentity, _password);
        if (service == null)
            throw new InvalidOperationException("ICrmConnectionService 不得回傳 null Dataverse client。");
        return service;
    }

    private static bool IsHealthy(IOrganizationService service)
    {
        if (service == null)
            return false;
        try
        {
            return service.Execute(new WhoAmIRequest()) is WhoAmIResponse;
        }
        catch
        {
            return false;
        }
    }

    private static string RequireValue(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("設定值不得為空白。", name);
        return value;
    }

    /// <summary>
    /// 讀取會決定連線環境與有效身分的必要組態；缺漏時 fail fast，禁止靜默連到
    /// 另一個組織或以未驗證的服務帳號建立池化 client。
    /// </summary>
    private static string RequireConfigurationValue(string value, string key)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"缺少必要的 Dataverse 組態 '{key}'；不允許回退到硬編碼環境或服務身分。");
        }

        return value;
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(DataverseConnectionManager));
    }
}
