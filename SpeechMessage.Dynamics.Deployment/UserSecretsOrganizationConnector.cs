/*
 * 檔案整體說明
 *
 * 目的與定位：從 User Secrets 為單一命令建立 白名單組織的獨占地端連線。連線直接使用 ToolUtility 的 CreateOnPremiseClient，不經共用池；credential
 * 只在 Connect 的呼叫框架內短暫存在， 回傳租約是唯一 owner，命令結束必定 Dispose 並解除服務參考。
 *
 * 主要功能與流程：
 * 本檔宣告 UserSecretsOrganizationConnector。
 * - 保留 provider 供單次連線讀取，不複製或快取任何解析後的帳密。
 * - 建立可測的地端 client 邊界。正式入口仍委派至 ToolUtility；測試注入記憶體服務， 不建立網路連線且不保存組織以外的狀態。
 * - 驗證組織白名單、端點與必要 secrets 後建立獨占地端服務租約。
 * - 只接受清單核定組織的 HTTPS Organization.svc 主機與固定路徑。
 * 主要操作入口包含 Connect、IsAllowedEndpoint。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 */
using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Xrm.Sdk;
using ToolUtilityNameSpace.ConnectionOperations;

namespace SpeechMessage.Dynamics.Deployment;

/// <summary>
/// 從 User Secrets 為單一命令建立 白名單組織的獨占地端連線。連線直接使用 ToolUtility
/// 的 CreateOnPremiseClient，不經共用池；credential 只在 Connect 的呼叫框架內短暫存在，
/// 回傳租約是唯一 owner，命令結束必定 Dispose 並解除服務參考。
/// </summary>
internal sealed class UserSecretsOrganizationConnector : IOrganizationConnector
{
    private readonly OrganizationCatalog catalog;
    private const string AllowedPath = "/XRMServices/2011/Organization.svc";
    private readonly IConfiguration configuration;
    private readonly Func<string, string, string, IOrganizationService> createClient;

    /// <summary>保留 provider 供單次連線讀取，不複製或快取任何解析後的帳密。</summary>
    /// <param name="configuration">只含部署工具 User Secrets 的設定根。</param>
    public UserSecretsOrganizationConnector(IConfiguration configuration)
        : this(
            configuration,
            static (organizationUrl, username, password) =>
                new CrmConnectionService().CreateOnPremiseClient(organizationUrl, username, password))
    {
    }

    /// <summary>
    /// 建立可測的地端 client 邊界。正式入口仍委派至 ToolUtility；測試注入記憶體服務，
    /// 不建立網路連線且不保存組織以外的狀態。
    /// </summary>
    /// <param name="configuration">只含部署工具 User Secrets 的設定根。</param>
    /// <param name="createClient">只在端點與必要設定通過驗證後呼叫的 client 工廠。</param>
    public UserSecretsOrganizationConnector(
        IConfiguration configuration,
        Func<string, string, string, IOrganizationService> createClient)
        : this(configuration, createClient, OrganizationCatalog.LoadDefault()) { }

    /// <summary>命令區域catalog與工廠借入；只保存provider，所有帳密逐次Connect讀取，舊AllowedOrganizations不能擴權。</summary>
    internal UserSecretsOrganizationConnector(IConfiguration configuration,
        Func<string, string, string, IOrganizationService> createClient, OrganizationCatalog catalog)
    {
        this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        this.createClient = createClient ?? throw new ArgumentNullException(nameof(createClient));
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        foreach (var child in configuration.GetSection("Deployment:AllowedOrganizations").GetChildren())
        {
            if (string.IsNullOrWhiteSpace(child.Value)) continue;
            try { _ = catalog.Get(child.Value); }
            catch (InvalidDataException) { throw new InvalidOperationException("Configured organization is not allowlisted."); }
        }
    }

    /// <summary>驗證組織白名單、端點與必要 secrets 後建立獨占地端服務租約。</summary>
    /// <param name="organization">命令列提供且已通過 P0 白名單的組織別名。</param>
    /// <returns>由呼叫端在此命令結束時 Dispose 的服務租約。</returns>
    /// <exception cref="InvalidOperationException">組織、服務網址或 User Secrets 設定不符合 P0 限制。</exception>
    public OrganizationLease Connect(string organization)
    {
        OrganizationDefinition target;
        try { target = catalog.Get(organization); }
        catch (InvalidDataException) { throw new InvalidOperationException("Target organization is not allowlisted."); }
        var prefix = "Deployment:Organizations:" + target.Name + ":";
        var organizationUrl = configuration[prefix + "OrganizationUrl"];
        var username = configuration[prefix + "Username"];
        var password = configuration[prefix + "Password"];
        if (!IsAllowedEndpoint(organizationUrl, target.Host))
        {
            throw new InvalidOperationException("OrganizationUrl does not match the allowlisted endpoint.");
        }

        if (string.IsNullOrWhiteSpace(username))
        {
            throw new InvalidOperationException("部署 User Secrets 缺少必要的 Username。");
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("部署 User Secrets 缺少必要的 Password。");
        }

        IOrganizationService client = createClient(organizationUrl!, username, password);
        if (client is not IDisposable disposable)
        {
            (client as IDisposable)?.Dispose();
            throw new InvalidOperationException("地端 Dataverse client 未提供可釋放的連線生命週期。");
        }

        return new OrganizationLease(client, disposable.Dispose);
    }

    /// <summary>只接受清單核定組織的 HTTPS Organization.svc 主機與固定路徑。</summary>
    /// <param name="value">User Secrets 中的服務 URL，不寫入錯誤訊息。</param>
    /// <param name="host">catalog核定的純主機名稱，不來自User Secrets或命令列覆寫。</param>
    /// <returns>URL 完全符合核定端點時為 true。</returns>
    private static bool IsAllowedEndpoint(string? value, string host)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(uri.IdnHost, host, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(uri.AbsolutePath, AllowedPath, StringComparison.OrdinalIgnoreCase) &&
            uri.IsDefaultPort &&
            string.IsNullOrEmpty(uri.Query) &&
            string.IsNullOrEmpty(uri.Fragment);
    }
}
