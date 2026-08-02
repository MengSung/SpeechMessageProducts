using System;

namespace SpeechMessage.Dynamics.WorkerHost;

/// <summary>
/// 表示已通過嚴格 XML 契約驗證的 Worker-local CRM 連線形狀。
/// 此不可變物件只保存主機、組織、驗證模式與 Credential Manager reference；
/// 它不保存密碼、Token、Cookie、完整 endpoint 或 connection string，亦不得跨 Worker 行程共用可變狀態。
/// </summary>
public sealed class WorkerProfileSettings
{
    /// <summary>
    /// 建立一份不可變的 Worker 設定快照。呼叫端只能使用 <see cref="XmlWorkerProfileStore"/>
    /// 已驗證的資料建立此物件，避免執行期間局部改寫身分或路由。
    /// </summary>
    internal WorkerProfileSettings(
        string hostName,
        int port,
        string organizationName,
        Guid expectedOrganizationId,
        bool useSsl,
        OfficialCrmAuthenticationMode authenticationMode,
        OfficialCrmIdentityMode identityMode,
        string? credentialReference,
        string? homeRealm)
    {
        HostName = hostName;
        Port = port;
        OrganizationName = organizationName;
        ExpectedOrganizationId = expectedOrganizationId;
        UseSsl = useSsl;
        AuthenticationMode = authenticationMode;
        IdentityMode = identityMode;
        CredentialReference = credentialReference;
        HomeRealm = homeRealm;
    }

    /// <summary>
    /// 取得不含 scheme、path、query 或 fragment 的核准 CRM DNS 主機名稱。
    /// </summary>
    public string HostName { get; }

    /// <summary>
    /// 取得 Worker 建立官方 CRM client 時使用的有限 TCP 連接埠。
    /// </summary>
    public int Port { get; }

    /// <summary>
    /// 取得 CE 組織唯一名稱；它不是可由產品要求改寫的路由輸入。
    /// </summary>
    public string OrganizationName { get; }

    /// <summary>
    /// 取得部署端已核准且不可為空的 Dynamics Organization GUID。Worker 的 Ready 探測必須將
    /// 官方 WhoAmI 結果與此值比對，不能只依賴 hostname 或 organization name 推定實體租戶。
    /// </summary>
    public Guid ExpectedOrganizationId { get; }

    /// <summary>
    /// 取得是否要求 TLS。正式設定應維持 <see langword="true"/>，實際憑證驗證由官方 client 負責且不得被繞過。
    /// </summary>
    public bool UseSsl { get; }

    /// <summary>
    /// 取得建立官方 CRM client 時固定使用的 CE 驗證形狀。
    /// </summary>
    public OfficialCrmAuthenticationMode AuthenticationMode { get; }

    /// <summary>
    /// 取得服務身分來源；HostIdentity 與 WindowsCredentialReference 為嚴格互斥聯集。
    /// </summary>
    public OfficialCrmIdentityMode IdentityMode { get; }

    /// <summary>
    /// 取得 Windows Credential Manager 的非機密 target reference。
    /// HostIdentity 模式一定為 <see langword="null"/>，不會保留密碼或解析後的 Credential。
    /// </summary>
    public string? CredentialReference { get; }

    /// <summary>
    /// 取得 IFD claims 驗證使用的絕對 HTTPS HomeRealm URI。只有
    /// WindowsCredentialReference + IFD 組合可具有此值；Active Directory 與 HostIdentity
    /// 一律為 <see langword="null"/>，避免同一 profile 被兩種驗證分支解讀。
    /// </summary>
    public string? HomeRealm { get; }
}
