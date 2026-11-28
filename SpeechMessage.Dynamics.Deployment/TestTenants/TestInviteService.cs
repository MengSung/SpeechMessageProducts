/*
檔案整體說明
目的與定位：F12 測試租戶邀請的同步簽發邊界，借用當次命令的 SDK 服務，不負責 LIFF 兌換或 LINE 呼叫。
主要功能與流程：以伺服器組織名稱與 WhoAmI 核對實際連線，驗證租戶兩個 Choice 是旗標、角色代碼與管理員旗標，以及當前已配發名額、產品池 Channel、營運狀態與固定測試 Channel ID；最後產生三十二位元組密碼學亂數，只將原始位元組的 SHA-256 大寫十六進位與 UTC 到期時刻交給單筆原子 Create。
輸入與輸出：組織、產品與固定測試 Channel key 由伺服器組合根提供；輸出只供明確交付邀請。BasicId 暫空，由 CLI 透過已驗 Channel 的 BotInfo 補齊。
資源與隔離：服務與 TimeProvider 由命令 owner 借入，本型別不接管 Dispose；組合根須用有 transport timeout 的獨占租約，結果或例外返回外部前先 Dispose。服務實例只活到本次命令，不可註冊為 singleton 或跨命令共用。同步 SDK 返回前不能歸還租約，不建立背景工作、快取、timer 或取消註冊；角色與名額查詢最多兩列，拒絕 MoreRecords 及任何歧義。原始亂數只活到本次 Create 的 finally，隨即清零；不可清除的結果字串由 CLI 只保留到明確邀請交付完成，最長有效期三十分鐘，不能寫入 manifest、例外或日誌。
失敗與驗證：所有候選僅限單次呼叫，讀取期間取消立即停止；Create 前最後檢查取消，入場後同步排空，已提交則仍回傳正式主鍵，不能以取消掩蓋成功或盲目重試。只寫一筆邀請，不建立會友／角色指派；唯一雜湊衝突由 schema 的 new_ak_testinvite 拒絕，不先查再寫。SDK 真正例外原樣上傳，由外層釋放租約後交共用診斷；驗證拒絕只用固定安全訊息。合成 SDK 測試保護權威身分、單列發布、取消排空與租約釋放，不代表實機索引或交易已驗收。兌換端仍須在自己的交易內重新驗證完整範圍及 RowVersion，先前讀取不是跨交易授權證明。
*/
using System;
using System.Buffers.Text;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace SpeechMessage.Dynamics.Deployment.TestTenants;

/// <summary>當次命令使用的邀請簽發器；不持有跨命令快取或建立外部連線。</summary>
internal sealed class TestInviteService
{
    private readonly IOrganizationService service;
    private readonly string organization;
    private readonly string product;
    private readonly TimeProvider timeProvider;
    private readonly string channelKey;

    /// <summary>
    /// 只接收本次命令獨占服務與精確三組測試範圍；正式／legacy 組織及錯誤產品在任何 SDK I/O 前拒絕。
    /// 不從租戶輸入、顯示名稱或資料庫第一筆 Channel 推導 T01；組合根必須顯式傳入已核定的伺服器 Channel key。
    /// </summary>
    /// <param name="service">由外層 OrganizationLease 擁有，且已設定 transport timeout 的同步 SDK 服務。</param>
    /// <param name="organization">只允許 goodshepherd20、construction、association 的精確名稱。</param>
    /// <param name="product">必須與測試組織精確對應的 GoodShepherd2、GoodButler 或 GoodGuild。</param>
    /// <param name="timeProvider">借用時鐘；不建立 timer，未提供時使用系統 UTC。</param>
    /// <param name="channelKey">伺服器設定的固定測試 Messaging Channel ID，最多二十位 ASCII 數字；未提供即拒絕。</param>
    internal TestInviteService(IOrganizationService service, string organization, string product,
        TimeProvider? timeProvider = null, string? channelKey = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        var expected = organization switch
        {
            "goodshepherd20" => "GoodShepherd2", "construction" => "GoodButler", "association" => "GoodGuild", _ => null
        };
        if (expected is null || !string.Equals(expected, product, StringComparison.Ordinal) ||
            channelKey is not { Length: >= 1 and <= 20 }) throw Rejected();
        foreach (var character in channelKey)
            if (character is < '0' or > '9') throw Rejected();
        this.service = service;
        this.organization = organization;
        this.product = product;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.channelKey = channelKey;
    }

    /// <summary>
    /// 完整驗證本次權威讀取後才建立單筆邀請；角色／名額必須唯一，名稱相同不能代替資料庫身分。
    /// 原始憑證只透過成功結果明確交付；寫入送出前尊重 caller 取消，送出後必須排空並保留已提交主鍵。
    /// </summary>
    /// <param name="tenantId">必須讀回且核對兩個測試租戶旗標的 account 正式主鍵。</param>
    /// <param name="role">精確 member 或 admin，不接受大小寫、空白或顯示名稱後備。</param>
    /// <param name="minutes">一至三十分鐘；呼叫端 CLI 預設三十分鐘。</param>
    /// <param name="ct">僅呼叫端取消，不能在已送 SDK 時提前返回或釋放連線。</param>
    /// <returns>正式邀請主鍵、canonical base64url 憑證與 UTC 到期時間；BasicId 暫空，交 CLI 安全補齊。</returns>
    internal TestInviteResult Create(Guid tenantId, string role, int minutes, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (tenantId == Guid.Empty || role is not ("member" or "admin") || minutes is < 1 or > 30) throw Rejected();
        VerifyServer(ct);
        var tenant = Retrieve("account", tenantId,
            new ColumnSet("new_istenant", "new_istesttenant", "new_tenantcode", "new_channelslot", "statecode"), ct);
        if (Choice(tenant, "new_istenant") != 100000001 || Choice(tenant, "new_istesttenant") != 100000001 ||
            Choice(tenant, "statecode") != 0 || tenant.GetAttributeValue<string>("new_tenantcode") is not { Length: >= 1 and <= 64 } code ||
            string.IsNullOrWhiteSpace(code)) throw Rejected();
        var slotId = Reference(tenant, "new_channelslot", "new_channelslot").Id;

        var roleQuery = new QueryExpression("new_role")
        {
            TopCount = 2, ColumnSet = new ColumnSet("new_tenantid", "new_code", "new_isadmin", "statecode")
        };
        roleQuery.Criteria.AddCondition("new_tenantid", ConditionOperator.Equal, tenantId);
        roleQuery.Criteria.AddCondition("new_code", ConditionOperator.Equal, role);
        roleQuery.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
        var assignedRole = Single(roleQuery, ct);
        if (Reference(assignedRole, "new_tenantid", "account").Id != tenantId || Choice(assignedRole, "statecode") != 0 ||
            !string.Equals(assignedRole.GetAttributeValue<string>("new_code"), role, StringComparison.Ordinal) ||
            Choice(assignedRole, "new_isadmin") != (role == "admin" ? 100000001 : 100000000)) throw Rejected();

        var slotQuery = new QueryExpression("new_channelslot")
        {
            TopCount = 2, ColumnSet = new ColumnSet("new_tenant", "new_channel", "new_slotstatus", "statecode")
        };
        slotQuery.Criteria.AddCondition("new_tenant", ConditionOperator.Equal, tenantId);
        slotQuery.Criteria.AddCondition("new_slotstatus", ConditionOperator.Equal, 100000001);
        slotQuery.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
        var slot = Single(slotQuery, ct);
        if (slot.Id != slotId || Reference(slot, "new_tenant", "account").Id != tenantId ||
            Choice(slot, "new_slotstatus") != 100000001 || Choice(slot, "statecode") != 0) throw Rejected();
        var channelId = Reference(slot, "new_channel", "new_linechannel").Id;
        var channel = Retrieve("new_linechannel", channelId,
            new ColumnSet("new_channelid", "new_product", "new_channeltype", "new_channelstatus", "statecode"), ct);
        if (!string.Equals(channel.GetAttributeValue<string>("new_channelid"), channelKey, StringComparison.Ordinal) ||
            !string.Equals(channel.GetAttributeValue<string>("new_product"), product, StringComparison.Ordinal) ||
            Choice(channel, "new_channeltype") != 100000001 || Choice(channel, "statecode") != 0 ||
            Choice(channel, "new_channelstatus") is not (100000001 or 100000002)) throw Rejected();

        // 最後一個停止點在寫入前。之後 SDK 必須返回；提交後不再丟取消，以免 CLI 遺失精確清理 ID。
        ct.ThrowIfCancellationRequested();
        var expiresAt = timeProvider.GetUtcNow().ToUniversalTime().AddMinutes(minutes);
        Span<byte> raw = stackalloc byte[32];
        try
        {
            RandomNumberGenerator.Fill(raw);
            var token = Base64Url.EncodeToString(raw);
            var invite = new Entity("new_testinvite")
            {
                ["new_name"] = "F12 test invitation",
                ["new_tenant"] = new EntityReference("account", tenantId),
                ["new_role"] = new EntityReference("new_role", assignedRole.Id),
                ["new_tokenhash"] = Convert.ToHexString(SHA256.HashData(raw)),
                ["new_expireson"] = expiresAt.UtcDateTime
            };
            ct.ThrowIfCancellationRequested();
            var inviteId = service.Create(invite);
            if (inviteId == Guid.Empty) throw Rejected();
            return new TestInviteResult(inviteId, token, expiresAt, channelKey, "");
        }
        finally { CryptographicOperations.ZeroMemory(raw); }
    }

    /// <summary>兩個唯讀訊息交叉驗證伺服器正式 ID／精確名稱與有效登入；不保存身分回應到本次呼叫之外。</summary>
    private void VerifyServer(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var current = service.Execute(new RetrieveCurrentOrganizationRequest());
        ct.ThrowIfCancellationRequested();
        if (current is not RetrieveCurrentOrganizationResponse { Detail: { } detail } || detail.OrganizationId == Guid.Empty ||
            !string.Equals(detail.UniqueName, organization, StringComparison.Ordinal)) throw Rejected();
        var who = service.Execute(new WhoAmIRequest());
        ct.ThrowIfCancellationRequested();
        if (who is not WhoAmIResponse response || response.OrganizationId != detail.OrganizationId || response.UserId == Guid.Empty)
            throw Rejected();
    }

    /// <summary>單列主鍵讀取前後檢查 caller 取消，返回值必須是精確實體與非空請求 ID；不接管服務 owner。</summary>
    private Entity Retrieve(string entity, Guid id, ColumnSet columns, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var row = service.Retrieve(entity, id, columns);
        ct.ThrowIfCancellationRequested();
        if (row is null || row.Id != id || id == Guid.Empty || !string.Equals(row.LogicalName, entity, StringComparison.Ordinal)) throw Rejected();
        return row;
    }

    /// <summary>最多兩筆的唯一性查詢，零筆、重複 ID、不同 ID 的歧義與續頁都拒絕，不取第一筆遮掩衝突。</summary>
    private Entity Single(QueryExpression query, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var result = service.RetrieveMultiple(query);
        ct.ThrowIfCancellationRequested();
        if (result is null || result.MoreRecords || result.Entities.Count != 1 || result.Entities[0] is not { } row ||
            row.Id == Guid.Empty || !string.Equals(row.LogicalName, query.EntityName, StringComparison.Ordinal)) throw Rejected();
        return row;
    }

    /// <summary>必須是 SDK Choice 型別，不能把 bool、整數或缺漏默認為相同旗標，避免測試租戶範圍擴權。</summary>
    private static int Choice(Entity row, string attribute) =>
        row.Attributes.TryGetValue(attribute, out var value) && value is OptionSetValue option ? option.Value : throw Rejected();

    /// <summary>lookup 必須具正確實體與非空正式 ID；不採用名稱、替代鍵或 caller 的暫存身分。</summary>
    private static EntityReference Reference(Entity row, string attribute, string entity) =>
        row.Attributes.TryGetValue(attribute, out var value) && value is EntityReference reference && reference.Id != Guid.Empty &&
        string.Equals(reference.LogicalName, entity, StringComparison.Ordinal) ? reference : throw Rejected();

    /// <summary>所有預期拒絕共用固定安全訊息，不回顯租戶、Channel、角色、憑證或 SDK 回應；Web 映射固定 403 由兌換端負責。</summary>
    private static InvalidDataException Rejected() => new("Test invite request rejected.");
}

/// <summary>只供明確邀請交付的不可變結果；不得寫入 manifest 或診斷。</summary>
/// <param name="InviteId">SDK 返回的正式邀請 ID。</param>
/// <param name="Token">限本次交付使用的原始憑證。</param>
/// <param name="ExpiresAt">UTC 到期時刻。</param>
/// <param name="ChannelKey">已核對的固定測試 Messaging Channel ID。</param>
/// <param name="BasicId">暫空，由 CLI 從已驗 Channel 的 BotInfo 補齊。</param>
internal sealed record TestInviteResult(Guid InviteId, string Token, DateTimeOffset ExpiresAt, string ChannelKey, string BasicId)
{
    /// <summary>預設格式化不展示原始邀請憑證，避免一般字串插值外洩。</summary>
    public override string ToString() => "TestInviteResult { Token = [REDACTED] }";
}
