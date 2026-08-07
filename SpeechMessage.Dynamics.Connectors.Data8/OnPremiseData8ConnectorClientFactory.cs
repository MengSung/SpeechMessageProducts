// ============================================================================
// 檔案：SpeechMessage.Dynamics.Connectors.Data8/OnPremiseData8ConnectorClientFactory.cs
// 用途：在受控 Profile 與 Pool 邊界內建立及封裝 PowerPlatform.Dataverse.Client.OnPremiseClient。
//
// 信任、隔離與生命週期契約：
// 1. Service URI、帳號與密碼只在 host composition root 建立的 settings owner 內存在；它們不會進入
//    OperationExecutionRequest、ConnectorOperation、Pool key、結果、例外訊息或日誌。
// 2. Factory 只接受已解析的 Data8 Profile 且 credential reference 必須完全相符。任一不符都在建立 WCF
//    channel 前 fail closed，防止不同 Profile／Organization 取得錯誤 credential。
// 3. 成功建成的 IOrganizationService 所有權會唯一移交給 IConnectorClient，再由 Data8ConnectorLease 在
//    await using 結束時回池或 Dispose；Factory 不保留 service、session、token、timer 或背景 task。
// ============================================================================

using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using PowerPlatform.Dataverse.Client;
using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.Abstractions.Connectors;
using SpeechMessage.Dynamics.Abstractions.Execution;
using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.Connectors.Data8;

/// <summary>
/// 啟動期由受控 composition root 建立的 On-Premise Data8 連線設定。
/// 此物件不能從產品 request、Session、controller 或 Profile resolver 取得；它只以固定 credential reference
/// 與單一 service URI 綁定一個 host generation。密碼沒有公開 getter，避免一般產品程式、記錄或測試意外
/// 讀取它；Factory 在同一受控組件內以最短必要範圍傳入 OnPremiseClient 建構式。
/// </summary>
public sealed class Data8OnPremiseConnectionSettings
{
    private readonly string _password;

    /// <summary>
    /// 建立固定 Data8 connection settings。建構式只驗證 bounded scalar，不發出網路要求；真正 WCF 資源要等到
    /// Pool 取得 Organization admission 與 local slot 後才建立，避免冷啟動或無效 Profile 長期佔用連線。
    /// </summary>
    /// <param name="credentialReference">必須與 ResolvedProfile 完全一致的部署端 credential reference。</param>
    /// <param name="serviceUri">單一 Organization 的完整 HTTPS Organization Service URI。</param>
    /// <param name="userName">只供 OnPremiseClient 建構使用的服務帳號，不會公開或寫入結果。</param>
    /// <param name="password">只供 OnPremiseClient 建構使用的密碼，絕不記錄或外露。</param>
    public Data8OnPremiseConnectionSettings(
        string credentialReference,
        string serviceUri,
        string userName,
        string password)
    {
        if (string.IsNullOrWhiteSpace(credentialReference))
        {
            throw new ArgumentException("Credential reference is required.", nameof(credentialReference));
        }

        if (!Uri.TryCreate(serviceUri, UriKind.Absolute, out var parsedServiceUri) ||
            !string.Equals(parsedServiceUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Data8 service URI must be an absolute HTTPS URI.", nameof(serviceUri));
        }

        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new ArgumentException("Data8 user name is required.", nameof(userName));
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("Data8 password is required.", nameof(password));
        }

        CredentialReference = credentialReference.Trim();
        ServiceUri = parsedServiceUri.AbsoluteUri;
        UserName = userName;
        _password = password;
    }

    /// <summary>取得固定的 credential reference；它只是非秘密名稱，不能用來取得或推導實際密碼。</summary>
    public string CredentialReference { get; }

    /// <summary>取得受控 Organization Service URI；此設定只允許在 host composition root／Factory 內使用。</summary>
    public string ServiceUri { get; }

    /// <summary>取得服務帳號；呼叫端不得記錄此值或將它作為 Session、Pool 或 admission key。</summary>
    public string UserName { get; }

    /// <summary>
    /// 建立一個新的 OnPremiseClient。這是唯一觸及密碼的位置；Data8 client 建構子若因 WSDL、AD 或
    /// Federation 初始化失敗，既有 P2 cleanup 會反向關閉已建立的 Channel／Factory。成功後 caller 必須將
    /// ownership 轉移給 Connector client，而不是自行保存在 host 或 Session。
    /// </summary>
    internal IOrganizationService CreateOrganizationService()
        => new OnPremiseClient(ServiceUri, UserName, _password);
}

/// <summary>
/// 將受控 settings 與 Profile binding 轉換為 SDK-free Connector client 的 Factory。
/// Factory 不保存每次建立的 WCF service；它只保存 host lifetime 的 immutable settings 與建立 delegate。
/// 因此 Pool 的 idle queue 仍只保存由 Lease 回收的 client，沒有跨 request 的 factory-side session cache。
/// </summary>
public sealed class OnPremiseData8ConnectorClientFactory : IData8ConnectorClientFactory
{
    private readonly Data8OnPremiseConnectionSettings _settings;
    private readonly Func<Data8OnPremiseConnectionSettings, IOrganizationService> _createOrganizationService;

    /// <summary>
    /// 建立 production Factory。預設 delegate 會建立已通過 P2 lifecycle 驗證的 <see cref="OnPremiseClient"/>；
    /// 呼叫端不可傳入 raw request、Profile endpoint 或 credential，僅能交付 host 啟動時已驗證的 settings owner。
    /// </summary>
    /// <param name="settings">只屬於此 host／Profile composition 的非可變連線設定。</param>
    public OnPremiseData8ConnectorClientFactory(Data8OnPremiseConnectionSettings settings)
        : this(settings, static connectionSettings => connectionSettings.CreateOrganizationService())
    {
    }

    /// <summary>
    /// 建立可替換 service creator 的 Factory。此 overload 讓離線測試能驗證 ownership 而不連線 D365；production
    /// composition 不應傳入會快取或跨 Profile 共用 service 的 delegate。Delegate 成功後的 service 唯一 owner
    /// 立即轉移給回傳的 Connector client，若中途取消則於本方法同步 rollback。
    /// </summary>
    /// <param name="settings">固定連線設定 owner。</param>
    /// <param name="createOrganizationService">建立單一、尚未交付 Pool 的 Organization service。</param>
    public OnPremiseData8ConnectorClientFactory(
        Data8OnPremiseConnectionSettings settings,
        Func<Data8OnPremiseConnectionSettings, IOrganizationService> createOrganizationService)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _createOrganizationService = createOrganizationService ?? throw new ArgumentNullException(nameof(createOrganizationService));
    }

    /// <summary>
    /// 驗證 immutable Profile 與 credential reference 後建立一個新的 service wrapper。
    /// OnPremiseClient／WCF 不提供可安全等待的原生非同步建構 API，因此建構在目前呼叫執行緒同步完成；此處刻意
    /// 不使用 Task.Run，避免把 credential-bearing 建構工作變成未受 host 管理的 ThreadPool work item。取消在
    /// 建構前與 ownership 轉移前都會檢查；第二次檢查若失敗，Factory 是唯一 owner，會立即 Dispose service。
    /// </summary>
    /// <param name="profile">resolver 輸出的 immutable Data8 Profile generation。</param>
    /// <param name="cancellationToken">Pool 作業範圍的取消訊號，不被 Factory 保存。</param>
    /// <returns>已接手 service ownership 的 SDK-free client。</returns>
    public Task<IConnectorClient> CreateAsync(ResolvedProfile profile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        cancellationToken.ThrowIfCancellationRequested();

        if (profile.ConnectorKind != ConnectorKind.Data8 ||
            !string.Equals(profile.CredentialReference, _settings.CredentialReference, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The resolved profile cannot use this Data8 connection factory.");
        }

        IOrganizationService? service = null;
        try
        {
            service = _createOrganizationService(_settings)
                ?? throw new InvalidOperationException("The Data8 service factory returned null.");
            cancellationToken.ThrowIfCancellationRequested();

            var client = new OnPremiseData8ConnectorClient(service, profile.CeVersion);
            service = null;
            return Task.FromResult<IConnectorClient>(client);
        }
        catch
        {
            // ownership 尚未成功移交給 client 時，Factory 是唯一清理者；不可讓取消留下建立中的 WCF channel。
            (service as IDisposable)?.Dispose();
            throw;
        }
    }
}

/// <summary>
/// 將同步 Data8 IOrganizationService 限縮為已審查的 SDK-free runtime 與 Package01 唯讀 Connector 操作。
/// 此類別只由 Pool 建立並由 Lease Dispose；它不快取 OrganizationResponse、request、Profile、credential 或
/// session，且不允許 generic Execute，因此產品無法藉由 Embedded 建立未審查的 CRM command 通道。所有 CRM
/// QueryExpression/Entity 投影仍留在 <see cref="Package01Data8ReadOperations"/> 的單次執行 scope。
/// </summary>
internal sealed class OnPremiseData8ConnectorClient : IConnectorClient
{
    private IOrganizationService? _service;
    private readonly string _ceVersion;
    private int _disposed;

    /// <summary>
    /// 接手已建立 service 的唯一 Dispose ownership，並複製 resolver 已固定的 CE version。建構後呼叫端不得
    /// 再直接使用 service；version 不是 request input，而是此 client 所屬 immutable Pool generation 的部署資料，
    /// 用於回應 envelope 一致性而非選擇 SDK、endpoint 或 connector。
    /// </summary>
    /// <param name="service">已成功建立、尚未交給其他 owner 的 Data8 organization service。</param>
    /// <param name="ceVersion">解析 Profile 的固定 CE version，僅允許已支援的 8.2 或 9.1。</param>
    internal OnPremiseData8ConnectorClient(IOrganizationService service, CeVersion ceVersion)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _ceVersion = ceVersion switch
        {
            CeVersion.Ce82 => "8.2",
            CeVersion.Ce91 => "9.1",
            _ => throw new ArgumentOutOfRangeException(nameof(ceVersion), ceVersion, "Unsupported CE version.")
        };
    }

    /// <summary>
    /// 執行已完成安全投影的 WhoAmI 或 Package01 server-owned read operation。
    /// WCF IOrganizationService 是同步 API，因此不建立額外 ThreadPool task；取消在呼叫前後檢查。若呼叫
    /// 期間發生取消或 service 例外，例外會回到 Lease，Lease 隨即標記 faulted 並 Dispose 此 client，不會將
    /// 健康狀態未知的 WCF Session 放回 Pool。
    /// </summary>
    /// <param name="operation">由 Data8ProfileOperationExecutor 建立的 allowlisted、型別化 operation。</param>
    /// <param name="cancellationToken">單次 lease 的取消訊號，永不保存。</param>
    /// <returns>僅含 WhoAmI scalar 或 Package01 封閉 response branch 的 SDK-free 結果。</returns>
    public Task<ConnectorOperationResult> ExecuteAsync(
        ConnectorOperation operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        cancellationToken.ThrowIfCancellationRequested();
        var service = Volatile.Read(ref _service)
            ?? throw new ObjectDisposedException(nameof(OnPremiseData8ConnectorClient));
        if (string.Equals(operation.OperationId, OperationIds.RuntimeHealthWhoAmI, StringComparison.Ordinal))
        {
            if (operation.Parameters is not { Count: 0 })
            {
                throw new InvalidOperationException("The Data8 connector operation is not permitted.");
            }

            var response = service.Execute(new WhoAmIRequest()) as WhoAmIResponse
                ?? throw new InvalidOperationException("The Data8 WhoAmI response is invalid.");
            cancellationToken.ThrowIfCancellationRequested();

            if (response.UserId == Guid.Empty ||
                response.BusinessUnitId == Guid.Empty ||
                response.OrganizationId == Guid.Empty)
            {
                throw new InvalidOperationException("The Data8 WhoAmI response is incomplete.");
            }

            return Task.FromResult(new ConnectorOperationResult(true)
            {
                Values = new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["userId"] = response.UserId.ToString("D"),
                    ["businessUnitId"] = response.BusinessUnitId.ToString("D"),
                    ["organizationId"] = response.OrganizationId.ToString("D")
                }
            });
        }

        // Package01 helper 是唯一可觸碰 QueryExpression、EntityCollection 與 CRM Entity 的位置；它不接受
        // request-time CRM metadata，且一旦同步 SDK 呼叫、投影或 paging 發生例外，Lease 會淘汰本 client。
        var data = Package01Data8ReadOperations.Execute(service, operation, _ceVersion);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ConnectorOperationResult(true)
        {
            Data = data
        });
    }

    /// <summary>
    /// 確定性釋放底層 WCF/ADFS 資源。Interlocked 讓 Pool fault、Drain、同步 Dispose 與非同步 Dispose 的競爭
    /// 最多執行一次；服務若實作 IDisposable，其 Close／Abort 細節仍由既有 OnPremiseClient owner 處理。
    /// 不建立補償 Task 或重試 timer，失敗直接交由 Lease／Pool cleanup aggregation 觀察。
    /// </summary>
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            var service = Interlocked.Exchange(ref _service, null);
            (service as IDisposable)?.Dispose();
        }

        return ValueTask.CompletedTask;
    }
}
