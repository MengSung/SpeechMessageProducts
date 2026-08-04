// ============================================================================
// 檔案：SpeechMessageProducts.ChurchReport/Services/EmbeddedData8Runtime.cs
// 用途：ChurchReport Embedded 模式對共用 Data8 runtime 的 composition-root 包裝。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.Abstractions.Connectors;
using SpeechMessage.Dynamics.Connectors.Data8;

namespace ChurchReport.Services;

/// <summary>
/// 將 ChurchReport 的 Embedded 組態映射到共用 <see cref="Data8ProfileRuntime"/>。
/// 此類別不再擁有第二套 pool、admission 或 client 生命週期；其唯一責任是維持 ChurchReport 的既有
/// DI 介面。實際 runtime 由此 instance 唯一擁有，隨 ChurchReport 的 ServiceProvider 反向釋放，
/// 所以不會與 Dedicated Gateway 共用 Session、連線、permit、timer 或可變 profile 狀態。
/// </summary>
public sealed class EmbeddedData8Runtime : IAsyncDisposable
{
    private readonly Data8ProfileRuntime _runtime;

    /// <summary>
    /// 建立 Embedded host 的 Data8 runtime。
    /// <paramref name="logger"/> 僅保留相容的 DI contract；runtime 不記錄 credential、endpoint 或 request，
    /// 所有資源釋放仍完全由 <see cref="Data8ProfileRuntime.DisposeAsync"/> 決定。
    /// </summary>
    public EmbeddedData8Runtime(
        IReadOnlyDictionary<string, DynamicsProfileOptions> profiles,
        IReadOnlyDictionary<string, OrganizationCatalogEntry> catalog,
        string profileAlias,
        IData8ConnectorClientFactory clientFactory,
        ILogger<EmbeddedData8Runtime> logger,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _runtime = new Data8ProfileRuntime(profiles, catalog, profileAlias, clientFactory, loggerFactory);
    }

    /// <summary>取得 Embedded host 專屬的不可變 profile resolver。</summary>
    public IProfileResolver ProfileResolver => _runtime.ProfileResolver;

    /// <summary>取得 Embedded host 專屬的 Data8 router；不能存取任何 Dedicated Gateway pool。</summary>
    public IConnectorRouter Router => _runtime.Router;

    /// <summary>取得由共用 runtime 所建立的 SDK-free executor。</summary>
    public Data8ProfileOperationExecutor Executor => _runtime.Executor;

    /// <summary>
    /// 將 ChurchReport host 的停止事件轉交給唯一 resource owner。
    /// 釋放時會先 drain pool 和 lease，再釋放 admission；重複呼叫是安全的，且不留下背景工作或 client。
    /// </summary>
    public ValueTask DisposeAsync() => _runtime.DisposeAsync();
}
