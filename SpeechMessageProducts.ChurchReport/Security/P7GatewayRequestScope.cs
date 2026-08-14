using System;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace ChurchReport.Security
{
    /// <summary>
    /// 表示 P7 Gateway request scope 可接受的登入來源種類。此 enum 是封閉 allowlist，避免任意 claim
    /// 文字被誤用為 role、Dynamics profile、connector、organization 或 credential 選擇器。
    /// </summary>
    public enum P7GatewayLoginKind
    {
        /// <summary>由一般帳號登入完成後、Cookie middleware 驗證的伺服器簽發 identity。</summary>
        Account,

        /// <summary>由已驗證的 LINE 對應登入完成後、Cookie middleware 驗證的伺服器簽發 identity。</summary>
        Line
    }

    /// <summary>
    /// 表示建立 P7 Gateway request scope 時的固定去識別化結果分類。列舉值不得含 claim 原值、帳號、
    /// credential、endpoint、CRM ID 以外的可識別資料或上游例外，讓呼叫端可 fail closed 而不洩漏資訊。
    /// </summary>
    public enum P7GatewayScopeFailure
    {
        /// <summary>唯一 Cookie identity 與所有必要 claim 已通過驗證。</summary>
        None,

        /// <summary>沒有已驗證 identity，或 principal 為 null，因此不得建立 scope。</summary>
        Unauthenticated,

        /// <summary>已驗證 identity 不是唯一 Cookie scheme identity，可能表示 scheme 或 identity 混淆。</summary>
        InvalidAuthenticationScheme,

        /// <summary>必要 contact claim 缺失、重複、空白或不是唯一有效 GUID，故 subject 不可判定。</summary>
        MissingOrAmbiguousContactClaim,

        /// <summary>NameIdentifier 與 Church contact claim 解析為不同 GUID，故 principal 的 subject 不可信任。</summary>
        ConflictingContactClaim,

        /// <summary>login type 缺失、重複或不在封閉 allowlist，故不得建立 scope。</summary>
        UnsupportedLoginKind
    }

    /// <summary>
    /// 保存後續 P7 capability 使用的最小、不可變、request-local identity baseline。此型別只保留 subject GUID、
    /// 固定產品邊界與封閉 login kind；不保留 <see cref="ClaimsPrincipal"/>、claim、<c>HttpContext</c>、
    /// Session、account、password key、token、profile、connector、CRM entity 或可變集合。
    /// </summary>
    public sealed class P7GatewayRequestScope
    {
        private const string ChurchReportProductBoundary = "ChurchReport";

        /// <summary>
        /// 以已驗證的 contact GUID 與 login kind 建立 scope。建構子只接受 resolver 已正規化的 scalar，
        /// 不驗證 browser/route 輸入，也不進行 cache、manager、connector 或 CRM I/O；因此沒有外部資源
        /// 所有權、背景工作、retry 或 dispose 責任。
        /// </summary>
        /// <param name="subjectContactId">resolver 已驗證為非空且雙 claim 一致的 server-derived contact GUID。</param>
        /// <param name="loginKind">resolver 已驗證為 allowlisted 值的登入種類。</param>
        internal P7GatewayRequestScope(Guid subjectContactId, P7GatewayLoginKind loginKind)
        {
            SubjectContactId = subjectContactId;
            LoginKind = loginKind;
        }

        /// <summary>
        /// 取得由 Cookie principal 投影且已驗證的 subject contact GUID。此值只描述當前 request 的 identity
        /// baseline，不授權任何目標資料；未來 capability 必須在 connector allocation 前另行建立 target policy。
        /// </summary>
        public Guid SubjectContactId { get; }

        /// <summary>
        /// 取得固定的產品隔離邊界。它是常數而非 caller input，避免 client 或 route 將此 scope 重導向其他產品。
        /// Profile 與 generation 屬 future capability 的 deployment-owned boundary，不能由此 shared scope 猜測。
        /// </summary>
        public string ProductBoundary => ChurchReportProductBoundary;

        /// <summary>
        /// 取得已驗證且封閉的登入種類。它不是角色、權限、profile、credential 或 target access decision。
        /// </summary>
        public P7GatewayLoginKind LoginKind { get; }
    }

    /// <summary>
    /// 封裝 immutable scope 建立結果。失敗時 <see cref="Scope"/> 為 null，且 <see cref="Failure"/> 只提供
    /// 固定分類；成功時 <see cref="Failure"/> 為 <see cref="P7GatewayScopeFailure.None"/>。此值不保存 principal、
    /// exception、claim、credential 或其他 request state，沒有需釋放的資源。
    /// </summary>
    public readonly record struct P7GatewayRequestScopeResolution(
        P7GatewayRequestScope? Scope,
        P7GatewayScopeFailure Failure);

    /// <summary>
    /// 從 Cookie middleware 已驗證的 principal 建立 P7 Gateway 的最小 immutable scope。resolver 是純函式：
    /// 不讀取 Session、<c>HttpContext</c>、DI、cache、legacy manager、profile、connector 或 CRM，且不把 input
    /// principal/claim 留在欄位、static state、closure、timer、queue 或 background task。因此 A/B request 可安全交錯；
    /// 呼叫端在取得成功 scope 前不得解析 locator、取得 client 或發出外部 I/O。
    /// </summary>
    public static class P7GatewayRequestScopeResolver
    {
        /// <summary>
        /// 嘗試從唯一已驗證 Cookie identity 投影 scope。只接受相同且唯一的 NameIdentifier/contact GUID 及唯一
        /// allowlisted login type；任何缺失、歧義或 mismatch 都回傳固定 failure，且不丟出含原始 claim 的例外。
        /// </summary>
        /// <param name="principal">目前 request 由 Cookie middleware 驗證的 principal；不會被保存或修改。</param>
        /// <returns>成功時為新的 scalar scope；失敗時 scope 為 null 且結果為固定 fail-closed 分類。</returns>
        public static P7GatewayRequestScopeResolution TryCreate(ClaimsPrincipal? principal)
        {
            if (principal is null)
            {
                return Fail(P7GatewayScopeFailure.Unauthenticated);
            }

            var authenticatedIdentities = principal.Identities
                .Where(identity => identity.IsAuthenticated)
                .ToArray();
            if (authenticatedIdentities.Length == 0)
            {
                return Fail(P7GatewayScopeFailure.Unauthenticated);
            }

            if (authenticatedIdentities.Length != 1
                || !string.Equals(
                    authenticatedIdentities[0].AuthenticationType,
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    StringComparison.Ordinal))
            {
                return Fail(P7GatewayScopeFailure.InvalidAuthenticationScheme);
            }

            var identity = authenticatedIdentities[0];
            if (!TryGetUniqueGuid(identity, ClaimTypes.NameIdentifier, out var nameIdentifier)
                || !TryGetUniqueGuid(identity, LoginClaimsFactory.ContactIdClaim, out var contactId))
            {
                return Fail(P7GatewayScopeFailure.MissingOrAmbiguousContactClaim);
            }

            if (nameIdentifier != contactId)
            {
                return Fail(P7GatewayScopeFailure.ConflictingContactClaim);
            }

            if (!TryGetUniqueLoginKind(identity, out var loginKind))
            {
                return Fail(P7GatewayScopeFailure.UnsupportedLoginKind);
            }

            return new P7GatewayRequestScopeResolution(
                new P7GatewayRequestScope(contactId, loginKind),
                P7GatewayScopeFailure.None);
        }

        /// <summary>
        /// 讀取 identity 中唯一且為 GUID D 格式的 claim。多個、空白、非 D 格式或空 GUID 都不可用，
        /// 且不將原始值記錄、快取或回傳；這防止 claim 混淆造成跨 subject scope。
        /// </summary>
        /// <param name="identity">已確認為唯一 Cookie identity 的目前 request identity。</param>
        /// <param name="claimType">固定的伺服器 claim type，不接受 caller 指定類型。</param>
        /// <param name="contactId">成功時的 normalized GUID；失敗時為 empty。</param>
        /// <returns>只有精確唯一、有效且非空 GUID D 格式 claim 才回傳 true。</returns>
        private static bool TryGetUniqueGuid(ClaimsIdentity identity, string claimType, out Guid contactId)
        {
            contactId = Guid.Empty;
            var matches = identity.FindAll(claimType).ToArray();
            if (matches.Length != 1
                || !Guid.TryParseExact(matches[0].Value, "D", out var parsedContactId)
                || parsedContactId == Guid.Empty)
            {
                return false;
            }

            contactId = parsedContactId;
            return true;
        }

        /// <summary>
        /// 將唯一 login type claim 轉換成封閉 enum。未知、重複或大小寫以外的變體一律拒絕，
        /// 因為寬鬆解析可能讓外部輸入逐步變成路由或授權 authority。
        /// </summary>
        /// <param name="identity">已確認為唯一 Cookie identity 的目前 request identity。</param>
        /// <param name="loginKind">成功時的 allowlisted enum；失敗時為預設值且不可使用。</param>
        /// <returns>只有唯一且明確為 ACCOUNT 或 LINE 的 claim 才回傳 true。</returns>
        private static bool TryGetUniqueLoginKind(ClaimsIdentity identity, out P7GatewayLoginKind loginKind)
        {
            loginKind = default;
            var matches = identity.FindAll(LoginClaimsFactory.LoginTypeClaim).ToArray();
            if (matches.Length != 1)
            {
                return false;
            }

            if (string.Equals(matches[0].Value, "ACCOUNT", StringComparison.Ordinal))
            {
                loginKind = P7GatewayLoginKind.Account;
                return true;
            }

            if (string.Equals(matches[0].Value, "LINE", StringComparison.Ordinal))
            {
                loginKind = P7GatewayLoginKind.Line;
                return true;
            }

            return false;
        }

        /// <summary>
        /// 建立不含 scope 的固定失敗結果。helper 不保存任何輸入、例外或可變 state，並讓所有早期拒絕分支
        /// 回傳一致的 ownership 形狀；resolver 沒有取得資源，因此也沒有 cleanup、retry 或 disposal 工作。
        /// </summary>
        /// <param name="failure">唯一可公開的去識別化 failure 分類。</param>
        /// <returns>scope 為 null 的 fail-closed resolution。</returns>
        private static P7GatewayRequestScopeResolution Fail(P7GatewayScopeFailure failure)
            => new(null, failure);
    }
}
