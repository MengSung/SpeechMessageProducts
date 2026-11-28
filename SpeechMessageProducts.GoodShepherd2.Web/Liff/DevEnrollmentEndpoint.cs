/*
檔案整體說明
目的與定位：GoodShepherd2 共通層 F12 的開發邀請註冊入口，由 Program 單一註冊呼叫接入，僅 Development 且顯式 Enabled=true 才建立精確 POST 分支。
主要功能與流程：先限制 JSON 本文為二 KiB、唯一 token 欄位及正規四十三字元 base64url，再以共用五秒官方 verifier 核對伺服器 Login Channel。以原三十二 bytes 的 SHA-256 查唯一邀請，固定時間比對雜湊，核對 UTC 期限、兩個 Choice 旗標、啟用及試用狀態、角色所屬租戶與完整租戶→名額→Channel→產品鏈。最後僅送一次 ExecuteTransaction，先 CAS 標記邀請，再建立具有伺服器正式主鍵的 Contact 與角色。
使用邊界與維護：不接受 caller 的角色、租戶或 userId；固定組織與 GoodShepherd2 連線子池由既有組合根守門。憑證、官方身分及 SDK 投影只活於單次 HTTP，不放 Session、快取、日誌或回應；原始 bytes 在 finally 清除。同步 SDK 一旦入場就等待完成，不使用 Task.Run，租約排空後 MarkFaulted／Dispose，manager 仍由 DI 擁有。只有精確交易第一步的版本衝突屬預期 403；其他 SDK、內部取消、未知提交或清理失敗保留真 Exception 交共用 diagnostics，固定 500 且不重試。已確認提交不以後到取消假稱未寫入。離線交易、競賽與清理測試不等同 D365 或手機實機驗收。
*/
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.ServiceModel;
using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Liff.AspNetCore;
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd2.Web.Startup;
using ToolUtilityNameSpace.Dataverse;
using ToolUtilityNameSpace.Diagnostics;

namespace SpeechMessageProducts.GoodShepherd2.Web.Liff;

/// <summary>沒有共享請求狀態的 F12 入口；部署開關、產品與 Channel 均由伺服器決定。</summary>
internal static class DevEnrollmentEndpoint
{
    /// <summary>平台兩個「是」Choice 的實際值；測試從 00-core.schema.json 中文標籤獨立核對。</summary>
    private const int YesChoice = 100000001;
    /// <summary>產品 schema「試用中」值；測試直接核對 metadata，不能依選項位置猜測。</summary>
    private const int TrialChoice = 100000002;
    /// <summary>邀請本文硬上限，不信任 caller 的 Content-Length。</summary>
    private const int MaximumBodyBytes = 2048;

    /// <summary>停用時零註冊；啟用時重用 F2 組織守門，將精確分支插在廣域 LIFF filter 前，不受呼叫順序影響。</summary>
    /// <param name="builder">唯一受信任組合根；本方法不建立容器、連線或通知 owner。</param>
    internal static void AddGoodShepherd2DevEnrollment(WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (!builder.Environment.IsDevelopment() || !string.Equals(
            builder.Configuration["GoodShepherd2:DevEnrollment:Enabled"], "true", StringComparison.OrdinalIgnoreCase)) return;
        GoodShepherd2StartupGuard.Validate(builder.Configuration, builder.Environment);
        builder.Services.Insert(0, ServiceDescriptor.Transient<IStartupFilter, EnrollmentStartupFilter>());
    }

    /// <summary>只攔截精確 POST；其餘請求繼續原管線，故不擴大既有 API 或靜態檔權限。</summary>
    private sealed class EnrollmentStartupFilter : IStartupFilter
    {
        /// <summary>Startup filter 以註冊順序包覆管線，第一個登錄者必須先執行 MapWhen 再交廣域分支。</summary>
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.MapWhen(context => HttpMethods.IsPost(context.Request.Method) &&
                context.Request.Path.Equals("/api/liff/dev/enroll", StringComparison.Ordinal),
                branch => branch.Run(HandleAsync));
            next(app);
        };
    }

    /// <summary>
    /// 本分支位於 F6 與 Program 廣域管線外，因此自行使用同一 DI diagnostics 接住真實故障。
    /// 身分驗證只取部署 Login Channel；不先解析 actor，因為受邀者尚未有 Contact。
    /// 預期拒絕一律空本文 403，服務故障固定 500；呼叫端取消原樣傳遞，雜湊在所有出口清除。
    /// </summary>
    /// <param name="context">單次借入 HTTP；不保存至欄位，回應不含憑證、userId 或資料庫主鍵。</param>
    private static async Task HandleAsync(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        byte[]? hash = null;
        try
        {
            context.RequestAborted.ThrowIfCancellationRequested();
            var options = context.RequestServices.GetRequiredService<LiffOptions>();
            var headers = context.Request.Headers.Authorization;
            if (!options.IsConfigured || headers.Count != 1 || headers[0] is not { } header ||
                !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) { Deny(context); return; }
            var bearer = header[7..];
            if (bearer.Length is 0 or > 16384 || bearer.Any(c => c <= ' ' || c > '~' || c == ',')) { Deny(context); return; }
            hash = await ReadHashAsync(context.Request, context.RequestAborted).ConfigureAwait(false);
            if (hash is null) { Deny(context); return; }
            LiffIdentity identity;
            try
            {
                identity = await context.RequestServices.GetRequiredService<LiffIdTokenVerifier>()
                    .VerifyAsync(bearer, options.LoginChannelId, context.RequestAborted).ConfigureAwait(false);
            }
            catch (LiffVerificationException exception) when (!exception.IsServiceFailure) { Deny(context); return; }
            catch (LiffVerificationException exception)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException ?? exception).Throw();
                throw;
            }
            if (identity.UserId.Length is 0 or > 128 || identity.UserId.Any(char.IsControl)) { Deny(context); return; }
            var diagnostics = context.RequestServices.GetRequiredService<ExceptionDiagnostics>();
            var joined = Enroll(context.RequestServices.GetRequiredService<IDataverseConnectionManager>(),
                context.RequestServices.GetRequiredService<IChurchClock>(), diagnostics, options, identity, hash, context.RequestAborted);
            if (!joined) { Deny(context); return; }
            await Results.Json(new { message = "已加入測試教會。" }).ExecuteAsync(context).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            context.RequestServices.GetRequiredService<ExceptionDiagnostics>()
                .Report(exception, "GoodShepherd2.DevEnrollment.Http", context.RequestAborted);
            if (context.Response.HasStarted) throw;
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        }
        finally { if (hash is not null) CryptographicOperations.ZeroMemory(hash); }
    }

    /// <summary>統一預期拒絕，不以 JSON 或原因碼洩漏邀請、身分或租戶存在與否。</summary>
    private static void Deny(HttpContext context) => context.Response.StatusCode = StatusCodes.Status403Forbidden;

    /// <summary>
    /// 讀至二 KiB 加一 byte 以辨識超量，不能只信任 Content-Length；不接管主機擁有的 request stream。
    /// JSON 必須僅含唯一 token，正規解碼後再編碼精確比對，拒絕尾端 padding bits 產生的別名。
    /// 格式拒絕回 null，真正 I/O 或 caller 取消仍向上傳播；成功只留下 SHA-256，原始緩衝完整清除。
    /// </summary>
    /// <param name="request">本次主機擁有的請求，正文不快取也不記錄。</param>
    /// <param name="cancellationToken">原始 caller token，不以內部期限冒充正常取消。</param>
    /// <returns>本次操作獨有的三十二 bytes 雜湊，由 HandleAsync 最終清除；非法輸入回 null。</returns>
    private static async Task<byte[]?> ReadHashAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (!System.Net.Http.Headers.MediaTypeHeaderValue.TryParse(request.ContentType, out var contentType) ||
            !string.Equals(contentType.MediaType, "application/json", StringComparison.OrdinalIgnoreCase) ||
            request.ContentLength > MaximumBodyBytes) return null;
        var payload = new byte[MaximumBodyBytes + 1];
        byte[]? raw = null;
        try
        {
            var length = 0;
            while (length < payload.Length)
            {
                var read = await request.Body.ReadAsync(payload.AsMemory(length), cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                length += read;
            }
            if (length > MaximumBodyBytes) return null;
            using var document = JsonDocument.Parse(payload.AsMemory(0, length),
                new JsonDocumentOptions { MaxDepth = 4, AllowDuplicateProperties = false });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 1 ||
                !root.TryGetProperty("token", out var value) || value.ValueKind != JsonValueKind.String) return null;
            var token = value.GetString();
            if (token is null || token.Length != 43 || token.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))) return null;
            raw = Convert.FromBase64String(token.Replace('-', '+').Replace('_', '/') + "=");
            if (raw.Length != 32 || !string.Equals(token,
                Convert.ToBase64String(raw).TrimEnd('=').Replace('+', '-').Replace('/', '_'), StringComparison.Ordinal)) return null;
            return SHA256.HashData(raw);
        }
        catch (JsonException) { return null; }
        catch (FormatException) { return null; }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
            if (raw is not null) CryptographicOperations.ZeroMemory(raw);
        }
    }

    /// <summary>
    /// 單一操作借用固定 GoodShepherd2 manager 的短命租約，完整讀取後重新核對 token 到期瞬間。
    /// CAS 與兩筆 Create 必須同一 ExecuteTransaction；GUID 是這批實際 Create 的正式主鍵，並非 draft。
    /// 同步 SDK 入場後沒有可安全放棄的背景工作，所有路徑等完成並歸還租約才返回。
    /// 版本衝突回 false；未知結果、內部取消與清理失敗不重試，保留第一個真實例外及額外清理診斷。
    /// </summary>
    /// <param name="manager">經組合根固定產品與 goodshepherd20 組織的 DI manager，只借用不 Dispose。</param>
    /// <param name="clock">借入 UTC 時鐘，不保留任何租戶狀態。</param>
    /// <param name="diagnostics">Host 唯一故障入口，額外清理失敗不能覆蓋主故障。</param>
    /// <param name="options">啟動時固定的公開 Channel 映射，不由前端提供。</param>
    /// <param name="identity">本次官方驗證的身分，只在 Contact 交易中使用 sub。</param>
    /// <param name="hash">本次獨有的 SHA-256，由外層最終清除。</param>
    /// <param name="cancellationToken">caller 取消只在 SDK 呼叫前後觀察，已確認提交不能假裝未寫入。</param>
    /// <returns>交易及清理均成功才 true；預期資格拒絕與明確 CAS 競爭回 false。</returns>
    private static bool Enroll(IDataverseConnectionManager manager, IChurchClock clock, ExceptionDiagnostics diagnostics,
        LiffOptions options, LiffIdentity identity, byte[] hash, CancellationToken cancellationToken)
    {
        IClientLease? lease = null;
        Exception? primary = null;
        var joined = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            lease = manager.Acquire(cancellationToken);
            var rows = lease.Service.RetrieveMultiple(BuildQuery(Convert.ToHexString(hash), options.MessagingChannelKey));
            cancellationToken.ThrowIfCancellationRequested();
            if (rows is null || rows.MoreRecords || rows.Entities.Count > 1)
                throw new InvalidDataException("Enrollment invitation result is ambiguous or incomplete.");
            var now = clock.UtcNow.UtcDateTime;
            if (rows.Entities.Count == 1 && identity.ExpiresAt.UtcDateTime > now &&
                ValidInvite(rows.Entities[0], hash, options, now, out var tenant, out var role))
            {
                var invite = rows.Entities[0];
                var contact = new Entity("contact", Guid.NewGuid())
                {
                    ["lastname"] = "測試會友", ["parentcustomerid"] = new EntityReference("account", tenant), ["new_lineid"] = identity.UserId
                };
                var assignment = new Entity("new_roleassignment", Guid.NewGuid())
                {
                    ["new_name"] = "測試邀請角色", ["new_contactid"] = contact.ToEntityReference(), ["new_roleid"] = new EntityReference("new_role", role)
                };
                var transaction = new ExecuteTransactionRequest
                {
                    ReturnResponses = false,
                    Requests = new OrganizationRequestCollection
                    {
                        new UpdateRequest
                        {
                            Target = new Entity("new_testinvite", invite.Id) { RowVersion = invite.RowVersion, ["new_usedon"] = now },
                            ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches
                        },
                        new CreateRequest { Target = contact }, new CreateRequest { Target = assignment }
                    }
                };
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (lease.Service.Execute(transaction) is not ExecuteTransactionResponse)
                        throw new InvalidDataException("Enrollment transaction result is unavailable.");
                    joined = true;
                }
                catch (FaultException<OrganizationServiceFault> conflict) when (
                    conflict.Detail is ExecuteTransactionFault { FaultedRequestIndex: 0, ErrorCode: -2147088254 })
                {
                    // 只有邀請 CAS 的精確版本衝突可以視為重播；其餘錯誤及未知提交結果均不可重試。
                }
            }
        }
        catch (Exception exception)
        {
            primary = exception;
            if (lease is not null)
            {
                try { lease.MarkFaulted(); }
                catch (Exception cleanup) { diagnostics.Report(cleanup, "GoodShepherd2.DevEnrollment.FaultLease", CancellationToken.None); }
            }
        }
        finally
        {
            if (lease is not null)
            {
                try { lease.Dispose(); }
                catch (Exception cleanup)
                {
                    if (primary is null) primary = cleanup;
                    else diagnostics.Report(cleanup, "GoodShepherd2.DevEnrollment.ReleaseLease", CancellationToken.None);
                }
            }
        }
        if (primary is not null) ExceptionDispatchInfo.Capture(primary).Throw();
        return joined;
    }

    /// <summary>
    /// 建立本次獨有的 joined 查詢，只選授權欄位及 RowVersion；二筆硬上限用來辨識唯一鍵衝突。
    /// 租戶、名額、Channel 與角色的正式關聯都必須存在；查詢 filter 只是縮小資料，不取代投影再驗。
    /// </summary>
    /// <param name="hash">由原三十二 bytes 計算的大寫 SHA-256，不是原憑證。</param>
    /// <param name="channelKey">伺服器 T01 Messaging Channel 公開識別；不讀取 Channel 密文。</param>
    /// <returns>不與其他 request 共用的 SDK 查詢物件。</returns>
    private static QueryExpression BuildQuery(string hash, string channelKey)
    {
        var query = new QueryExpression("new_testinvite")
        {
            TopCount = 2, ColumnSet = new ColumnSet("new_testinviteid", "new_tokenhash", "new_tenant", "new_role", "new_expireson", "new_usedon", "createdon", "versionnumber")
        };
        query.Criteria.AddCondition("new_tokenhash", ConditionOperator.Equal, hash);
        var tenant = query.AddLink("account", "new_tenant", "accountid", JoinOperator.Inner);
        tenant.EntityAlias = "tenant";
        tenant.Columns = new ColumnSet("accountid", "statecode", "new_istenant", "new_istesttenant", "new_lifecyclestatus", "new_tenantcode", "new_channelslot");
        var slot = tenant.AddLink("new_channelslot", "new_channelslot", "new_channelslotid", JoinOperator.Inner);
        slot.EntityAlias = "slot";
        slot.Columns = new ColumnSet("new_channelslotid", "new_tenant", "new_channel");
        var channel = slot.AddLink("new_linechannel", "new_channel", "new_linechannelid", JoinOperator.Inner);
        channel.EntityAlias = "channel";
        channel.Columns = new ColumnSet("new_linechannelid", "new_product", "new_channelid");
        channel.LinkCriteria.AddCondition("new_product", ConditionOperator.Equal, GoodShepherd2Product.Name);
        channel.LinkCriteria.AddCondition("new_channelid", ConditionOperator.Equal, channelKey);
        var role = query.AddLink("new_role", "new_role", "new_roleid", JoinOperator.Inner);
        role.EntityAlias = "role";
        role.Columns = new ColumnSet("new_roleid", "new_tenantid", "new_code");
        return query;
    }

    /// <summary>
    /// 重新核對唯一 SDK 列的正式身分、完整關聯與兩個精確 Choice，而不盲信查詢 filter。
    /// SHA-256 固定時間比較；邀請必須未用，且從資料庫 createdon 到期限不超過三十分鐘。
    /// 日期按 SDK UTC／Unspecified UTC 契約讀取，不做本機時區換算；所有輸出只在完整通過後發布。
    /// </summary>
    /// <param name="row">本次借入 SDK 投影，不被成功結果保存。</param>
    /// <param name="hash">原憑證雜湊，只有比較用途。</param>
    /// <param name="options">已驗部署 Channel 與固定產品映射。</param>
    /// <param name="now">SDK 讀取結束後的 UTC 瞬間，用於嚴格到期判斷。</param>
    /// <param name="tenant">完整驗證後的實際 account 主鍵，拒絕時為空。</param>
    /// <param name="role">同一租戶的 member／admin 角色主鍵，拒絕時為空。</param>
    /// <returns>只有可一次性兌換的完整測試 scope 才 true，不以姓名去重或補上 draft 身分。</returns>
    private static bool ValidInvite(Entity row, byte[] hash, LiffOptions options, DateTime now, out Guid tenant, out Guid role)
    {
        tenant = role = Guid.Empty;
        if (row.LogicalName != "new_testinvite" || row.Id == Guid.Empty || string.IsNullOrEmpty(row.RowVersion) ||
            row.RowVersion.Length > 64 || row.RowVersion.Any(c => c is < '0' or > '9') ||
            row.Attributes.TryGetValue("new_usedon", out var used) && used is not null ||
            !row.Attributes.TryGetValue("new_tokenhash", out var stored) || stored is not string { Length: 64 } storedHash ||
            storedHash.Any(c => !(c is >= '0' and <= '9' or >= 'A' and <= 'F'))) return false;
        var expected = Convert.FromHexString(storedHash);
        try { if (!CryptographicOperations.FixedTimeEquals(hash, expected)) return false; }
        finally { CryptographicOperations.ZeroMemory(expected); }
        if (!Utc(row, "new_expireson", out var expires) || !Utc(row, "createdon", out var created) ||
            expires <= now || expires > now.AddMinutes(30) || created > now || expires <= created || expires > created.AddMinutes(30) ||
            row.GetAttributeValue<EntityReference>("new_tenant") is not { LogicalName: "account", Id: var tenantId } || tenantId == Guid.Empty ||
            row.GetAttributeValue<EntityReference>("new_role") is not { LogicalName: "new_role", Id: var roleId } || roleId == Guid.Empty ||
            Alias(row, "tenant", "account", "accountid") is not Guid accountId || accountId != tenantId ||
            Alias(row, "tenant", "account", "statecode") is not OptionSetValue { Value: 0 } ||
            Alias(row, "tenant", "account", "new_istenant") is not OptionSetValue { Value: YesChoice } ||
            Alias(row, "tenant", "account", "new_istesttenant") is not OptionSetValue { Value: YesChoice } ||
            Alias(row, "tenant", "account", "new_lifecyclestatus") is not OptionSetValue { Value: TrialChoice } ||
            Alias(row, "tenant", "account", "new_tenantcode") is not string { Length: > 0 and <= 64 } code || string.IsNullOrWhiteSpace(code) ||
            Alias(row, "tenant", "account", "new_channelslot") is not EntityReference { LogicalName: "new_channelslot", Id: var slotId } || slotId == Guid.Empty ||
            Alias(row, "slot", "new_channelslot", "new_channelslotid") is not Guid actualSlot || actualSlot != slotId ||
            !Lookup(Alias(row, "slot", "new_channelslot", "new_tenant"), "account", tenantId) ||
            Alias(row, "slot", "new_channelslot", "new_channel") is not EntityReference { LogicalName: "new_linechannel", Id: var channelId } || channelId == Guid.Empty ||
            Alias(row, "channel", "new_linechannel", "new_linechannelid") is not Guid actualChannel || actualChannel != channelId ||
            !string.Equals(Alias(row, "channel", "new_linechannel", "new_channelid") as string, options.MessagingChannelKey, StringComparison.Ordinal) ||
            !string.Equals(Alias(row, "channel", "new_linechannel", "new_product") as string, GoodShepherd2Product.Name, StringComparison.Ordinal) ||
            Alias(row, "role", "new_role", "new_roleid") is not Guid actualRole || actualRole != roleId ||
            !Lookup(Alias(row, "role", "new_role", "new_tenantid"), "account", tenantId) ||
            Alias(row, "role", "new_role", "new_code") is not ("member" or "admin")) return false;
        tenant = tenantId; role = roleId;
        return true;
    }

    /// <summary>別名鍵、來源實體及欄位三者皆精確吻合才讀值，防止另一欄位冒充授權投影。</summary>
    private static object? Alias(Entity row, string alias, string entity, string field) =>
        row.Attributes.TryGetValue(alias + "." + field, out var value) &&
        value is AliasedValue source && source.EntityLogicalName == entity && source.AttributeLogicalName == field ? source.Value : null;

    /// <summary>lookup 必須指向預期實體的非空正式主鍵；名稱及替代暫存 key 不能當權威。</summary>
    private static bool Lookup(object? value, string entity, Guid id) => value is EntityReference reference &&
        reference.LogicalName == entity && reference.Id == id && id != Guid.Empty;

    /// <summary>SDK UserLocal 日期在傳輸中為 UTC；只接受 UTC 或 SDK Unspecified，拒絕 Local 型別避免主機時區影響。</summary>
    private static bool Utc(Entity row, string field, out DateTime utc)
    {
        utc = default;
        if (!row.Attributes.TryGetValue(field, out var value) || value is not DateTime date || date.Kind == DateTimeKind.Local) return false;
        utc = DateTime.SpecifyKind(date, DateTimeKind.Utc);
        return true;
    }
}
