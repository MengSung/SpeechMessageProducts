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
/// manager 本身不依賴任何 scoped 服務，Dispose 時會確定性釋放所有 pool client。
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
    /// 不採用呼叫端提供的資料作為隔離權限來源。
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

        _organizationUrl = _configuration["CrmConnection:ServerUrl"]
            ?? "https://jesus.speechmessage.com.tw/XRMServices/2011/Organization.svc";
        _effectiveIdentity = _configuration["CrmConnection:Username"] ?? "service-account";
        _password = _configuration["CrmConnection:Password"] ?? string.Empty;
        Pool = new BoundedClientPool(CreateClient, IsHealthy, options);
    }

    /// <summary>供組合根註冊為 IBoundedClientPool 的同一個 Singleton 實例。</summary>
    public IBoundedClientPool Pool { get; }

    /// <inheritdoc />
    public IClientLease Acquire(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return Pool.Acquire(new DataverseConnectionKey(
            _product,
            _environment,
            _organizationUrl,
            _effectiveIdentity), cancellationToken);
    }

    /// <inheritdoc />
    public DataversePoolMetrics GetMetrics()
    {
        ThrowIfDisposed();
        return Pool.GetMetrics();
    }

    /// <inheritdoc />
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

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(DataverseConnectionManager));
    }
}
