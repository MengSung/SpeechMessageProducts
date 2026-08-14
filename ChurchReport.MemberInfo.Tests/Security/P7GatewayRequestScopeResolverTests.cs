using System;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using ChurchReport.Security;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Security;

/// <summary>
/// 保護 P7 Gateway 共用授權邊界只從目前 request 的 Cookie principal 複製必要 scalar，
/// 而不把 Session、可變 claims、credential、CRM entity 或另一個使用者的狀態帶入後續 capability。
/// 每個測試建立獨立 principal；沒有共享測試資料、背景工作或外部 I/O，並以 deterministic 斷言鎖定
/// 對缺失、歧義與不一致 claim 的 fail-closed 結果。
/// </summary>
public sealed class P7GatewayRequestScopeResolverTests
{
    /// <summary>
    /// 驗證唯一 Cookie identity 的兩個相同 contact claim 與 allowlisted login kind 能建立完全新的、
    /// request-local scope。此測試保護 scope 不會回傳或保留原始 principal/claim，避免後續 request
    /// 看到前一個使用者的 mutable identity graph。
    /// </summary>
    [Fact]
    public void TryCreate_with_valid_cookie_claim_projection_returns_immutable_church_scope()
    {
        var contactId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var principal = CreateCookiePrincipal(contactId, P7GatewayLoginKind.Account);

        var resolution = P7GatewayRequestScopeResolver.TryCreate(principal);

        resolution.Failure.Should().Be(P7GatewayScopeFailure.None);
        resolution.Scope.Should().NotBeNull();
        resolution.Scope!.SubjectContactId.Should().Be(contactId);
        resolution.Scope.ProductBoundary.Should().Be("ChurchReport");
        resolution.Scope.LoginKind.Should().Be(P7GatewayLoginKind.Account);
    }

    /// <summary>
    /// 驗證未驗證身分或不是 Cookie scheme 的 identity 在任何 locator、cache、manager、connector 或 CRM
    /// 存取以前被拒絕。測試故障注入兩種身份來源，確保 legacy Session 不能被當成 fallback authority。
    /// </summary>
    [Fact]
    public void TryCreate_with_unauthenticated_or_non_cookie_identity_fails_before_scope_publication()
    {
        var contactId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var unauthenticated = new ClaimsPrincipal(new ClaimsIdentity());
        var nonCookie = new ClaimsPrincipal(
            new ClaimsIdentity(
                CreateClaims(contactId, P7GatewayLoginKind.Account),
                authenticationType: "bearer"));

        P7GatewayRequestScopeResolver.TryCreate(unauthenticated)
            .Failure.Should().Be(P7GatewayScopeFailure.Unauthenticated);
        P7GatewayRequestScopeResolver.TryCreate(nonCookie)
            .Failure.Should().Be(P7GatewayScopeFailure.InvalidAuthenticationScheme);
    }

    /// <summary>
    /// 驗證缺失、重複、格式錯誤或互相衝突的 contact claims 一律 fail closed。這避免 client/browser
    /// 可藉由模糊 claims 選取另一個 subject，也確保尚未建立 scope 時沒有可發布的資料。
    /// </summary>
    [Fact]
    public void TryCreate_with_missing_ambiguous_malformed_or_conflicting_contact_claims_fails_closed()
    {
        var contactId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var differentContactId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var missing = CreateCookiePrincipal(contactId, P7GatewayLoginKind.Account, omitContactId: true);
        var duplicate = CreateCookiePrincipal(contactId, P7GatewayLoginKind.Account, duplicateContactId: true);
        var malformed = CreateCookiePrincipal(contactId, P7GatewayLoginKind.Account, contactIdValue: "not-a-guid");
        var conflicting = CreateCookiePrincipal(
            contactId,
            P7GatewayLoginKind.Account,
            contactIdValue: differentContactId.ToString("D"));

        P7GatewayRequestScopeResolver.TryCreate(missing)
            .Failure.Should().Be(P7GatewayScopeFailure.MissingOrAmbiguousContactClaim);
        P7GatewayRequestScopeResolver.TryCreate(duplicate)
            .Failure.Should().Be(P7GatewayScopeFailure.MissingOrAmbiguousContactClaim);
        P7GatewayRequestScopeResolver.TryCreate(malformed)
            .Failure.Should().Be(P7GatewayScopeFailure.MissingOrAmbiguousContactClaim);
        P7GatewayRequestScopeResolver.TryCreate(conflicting)
            .Failure.Should().Be(P7GatewayScopeFailure.ConflictingContactClaim);
    }

    /// <summary>
    /// 驗證 <see cref="ClaimTypes.NameIdentifier"/> 不是可省略的相容性 claim，而是與 Church contact claim
    /// 共同構成 subject 一致性證明。測試注入缺失、重複與非 GUID D 格式值；每種情況都必須在建立 scope 前
    /// 固定拒絕，不能退回 Session、帳號、password key 或另一個 identity。
    /// </summary>
    [Fact]
    public void TryCreate_with_missing_ambiguous_or_malformed_name_identifier_fails_closed()
    {
        var contactId = Guid.Parse("4a4a4a4a-4a4a-4a4a-4a4a-4a4a4a4a4a4a");
        var missing = CreateCookiePrincipal(contactId, P7GatewayLoginKind.Account, omitNameIdentifier: true);
        var duplicate = CreateCookiePrincipal(contactId, P7GatewayLoginKind.Account, duplicateNameIdentifier: true);
        var malformed = CreateCookiePrincipal(
            contactId,
            P7GatewayLoginKind.Account,
            nameIdentifierValue: contactId.ToString("B"));

        P7GatewayRequestScopeResolver.TryCreate(missing)
            .Failure.Should().Be(P7GatewayScopeFailure.MissingOrAmbiguousContactClaim);
        P7GatewayRequestScopeResolver.TryCreate(duplicate)
            .Failure.Should().Be(P7GatewayScopeFailure.MissingOrAmbiguousContactClaim);
        P7GatewayRequestScopeResolver.TryCreate(malformed)
            .Failure.Should().Be(P7GatewayScopeFailure.MissingOrAmbiguousContactClaim);
    }

    /// <summary>
    /// 驗證 principal 同時帶有兩個已驗證 Cookie identities 時仍 fail closed。即使兩者各自看似有效，
    /// resolver 也不得猜選第一個 identity 或合併 claims，否則登入 handler 的組合順序可能把 B 的資料
    /// 帶進 A 的 scope。
    /// </summary>
    [Fact]
    public void TryCreate_with_multiple_authenticated_cookie_identities_fails_closed()
    {
        var subjectA = Guid.Parse("4b4b4b4b-4b4b-4b4b-4b4b-4b4b4b4b4b4b");
        var subjectB = Guid.Parse("4c4c4c4c-4c4c-4c4c-4c4c-4c4c4c4c4c4c");
        var identityA = CreateCookiePrincipal(subjectA, P7GatewayLoginKind.Account).Identities.Single();
        var identityB = CreateCookiePrincipal(subjectB, P7GatewayLoginKind.Line).Identities.Single();
        var ambiguousPrincipal = new ClaimsPrincipal([identityA, identityB]);

        var resolution = P7GatewayRequestScopeResolver.TryCreate(ambiguousPrincipal);

        resolution.Scope.Should().BeNull();
        resolution.Failure.Should().Be(P7GatewayScopeFailure.InvalidAuthenticationScheme);
    }

    /// <summary>
    /// 驗證 login kind 缺失、重複或不在明確 allowlist 時拒絕。封閉 enum 讓未來 capability 不會把
    /// caller 提供的任意字串當成 role、profile、connector 或 credential 選擇器。
    /// </summary>
    [Fact]
    public void TryCreate_with_missing_duplicate_or_unsupported_login_kind_fails_closed()
    {
        var contactId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var missing = CreateCookiePrincipal(contactId, P7GatewayLoginKind.Account, omitLoginKind: true);
        var duplicate = CreateCookiePrincipal(contactId, P7GatewayLoginKind.Account, duplicateLoginKind: true);
        var unsupported = CreateCookiePrincipal(contactId, P7GatewayLoginKind.Account, loginKindValue: "operator");

        P7GatewayRequestScopeResolver.TryCreate(missing)
            .Failure.Should().Be(P7GatewayScopeFailure.UnsupportedLoginKind);
        P7GatewayRequestScopeResolver.TryCreate(duplicate)
            .Failure.Should().Be(P7GatewayScopeFailure.UnsupportedLoginKind);
        P7GatewayRequestScopeResolver.TryCreate(unsupported)
            .Failure.Should().Be(P7GatewayScopeFailure.UnsupportedLoginKind);
    }

    /// <summary>
    /// 驗證 legacy account/password-key claims 即使仍為舊登入相容而存在，也不會被新 scope 複製或用作
    /// authority。這個 regression 防止 credential 從 Cookie ticket 擴散至 Gateway 連線或 response。
    /// </summary>
    [Fact]
    public void TryCreate_ignores_legacy_account_and_password_key_claims()
    {
        var contactId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var principal = CreateCookiePrincipal(
            contactId,
            P7GatewayLoginKind.Line,
            account: "legacy-account",
            passwordKey: "legacy-working-key");

        var resolution = P7GatewayRequestScopeResolver.TryCreate(principal);

        resolution.Failure.Should().Be(P7GatewayScopeFailure.None);
        resolution.Scope!.SubjectContactId.Should().Be(contactId);
        resolution.Scope.LoginKind.Should().Be(P7GatewayLoginKind.Line);
        var publishedProperties = typeof(P7GatewayRequestScope).GetProperties(BindingFlags.Instance | BindingFlags.Public);
        publishedProperties.Select(property => property.Name)
            .Should().NotContain(name => name.Contains("Account", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Password", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Credential", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 以交錯的 A/B principal 平行建立 scope，證明 resolver 沒有 static/shared cache 或可變輸入重用。
    /// 決定性斷言同時檢查每個回傳 scope 的 subject 與 login kind，不允許另一個 request 的 marker 滲入。
    /// </summary>
    [Fact]
    public async Task TryCreate_interleaved_subjects_never_cross_publish_identity_state()
    {
        var subjectA = Guid.Parse("77777777-7777-7777-7777-777777777777");
        var subjectB = Guid.Parse("88888888-8888-8888-8888-888888888888");
        var requestA = CreateCookiePrincipal(subjectA, P7GatewayLoginKind.Account);
        var requestB = CreateCookiePrincipal(subjectB, P7GatewayLoginKind.Line);

        var resolutions = await Task.WhenAll(
            Enumerable.Range(0, 64).Select(index => Task.Run(() =>
                index % 2 == 0
                    ? P7GatewayRequestScopeResolver.TryCreate(requestA)
                    : P7GatewayRequestScopeResolver.TryCreate(requestB))));

        resolutions.Where((_, index) => index % 2 == 0).Should().OnlyContain(resolution =>
            resolution.Failure == P7GatewayScopeFailure.None
            && resolution.Scope!.SubjectContactId == subjectA
            && resolution.Scope.LoginKind == P7GatewayLoginKind.Account);
        resolutions.Where((_, index) => index % 2 != 0).Should().OnlyContain(resolution =>
            resolution.Failure == P7GatewayScopeFailure.None
            && resolution.Scope!.SubjectContactId == subjectB
            && resolution.Scope.LoginKind == P7GatewayLoginKind.Line);
    }

    /// <summary>
    /// 驗證公開 resolver API 只接受 principal，不給 browser locator、Session、profile、connector、CRM service
    /// 或 cancellation registration 任何輸入位置；並驗證 scope 的公開狀態只含 allowlisted scalar。這讓
    /// scope 建立本身無 I/O、無背景 owner、無 retry/fallback，後續 capability 必須另行補 target authorization。
    /// </summary>
    [Fact]
    public void Public_contract_accepts_only_principal_and_scope_retains_only_allowlisted_scalars()
    {
        var method = typeof(P7GatewayRequestScopeResolver).GetMethod(
            "TryCreate",
            BindingFlags.Public | BindingFlags.Static);

        method.Should().NotBeNull();
        method!.GetParameters().Should().ContainSingle();
        method.GetParameters()[0].ParameterType.Should().Be(typeof(ClaimsPrincipal));

        var publicProperties = typeof(P7GatewayRequestScope)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .OrderBy(property => property.Name)
            .ToArray();
        publicProperties.Select(property => property.Name)
            .Should().Equal("LoginKind", "ProductBoundary", "SubjectContactId");
        publicProperties.Select(property => property.PropertyType)
            .Should().Equal(typeof(P7GatewayLoginKind), typeof(string), typeof(Guid));
        var retainedRequestStateMembers = typeof(P7GatewayRequestScope)
            .GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(static member => member switch
            {
                FieldInfo field => field.FieldType == typeof(ClaimsPrincipal)
                    || field.FieldType == typeof(Claim)
                    || field.FieldType == typeof(HttpContext),
                PropertyInfo property => property.PropertyType == typeof(ClaimsPrincipal)
                    || property.PropertyType == typeof(Claim)
                    || property.PropertyType == typeof(HttpContext),
                _ => false
            })
            .ToArray();

        retainedRequestStateMembers.Should().BeEmpty();
        typeof(P7GatewayRequestScopeResolver)
            .GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Should().BeEmpty("resolver 不得以 static 欄位保留任何 request、principal 或授權結果");
    }

    /// <summary>
    /// 建立 cookie middleware 已驗證 principal 的測試快照。helper 僅產生每個測試擁有的 claims，
    /// 不寫入 Session、cache、靜態欄位、connector 或 CRM，並允許故障注入每一項拒絕條件。
    /// </summary>
    private static ClaimsPrincipal CreateCookiePrincipal(
        Guid subjectContactId,
        P7GatewayLoginKind loginKind,
        bool omitNameIdentifier = false,
        bool duplicateNameIdentifier = false,
        bool omitContactId = false,
        bool duplicateContactId = false,
        bool omitLoginKind = false,
        bool duplicateLoginKind = false,
        string? nameIdentifierValue = null,
        string? contactIdValue = null,
        string? loginKindValue = null,
        string? account = null,
        string? passwordKey = null)
    {
        var claims = CreateClaims(subjectContactId, loginKind).ToList();
        if (omitNameIdentifier)
        {
            claims.RemoveAll(claim => claim.Type == ClaimTypes.NameIdentifier);
        }
        else if (nameIdentifierValue is not null)
        {
            claims.RemoveAll(claim => claim.Type == ClaimTypes.NameIdentifier);
            claims.Add(new Claim(ClaimTypes.NameIdentifier, nameIdentifierValue));
        }

        if (duplicateNameIdentifier)
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, subjectContactId.ToString("D")));
        }

        if (omitContactId)
        {
            claims.RemoveAll(claim => claim.Type == LoginClaimsFactory.ContactIdClaim);
        }
        else if (contactIdValue is not null)
        {
            claims.RemoveAll(claim => claim.Type == LoginClaimsFactory.ContactIdClaim);
            claims.Add(new Claim(LoginClaimsFactory.ContactIdClaim, contactIdValue));
        }

        if (duplicateContactId)
        {
            claims.Add(new Claim(LoginClaimsFactory.ContactIdClaim, subjectContactId.ToString("D")));
        }

        if (omitLoginKind)
        {
            claims.RemoveAll(claim => claim.Type == LoginClaimsFactory.LoginTypeClaim);
        }
        else if (loginKindValue is not null)
        {
            claims.RemoveAll(claim => claim.Type == LoginClaimsFactory.LoginTypeClaim);
            claims.Add(new Claim(LoginClaimsFactory.LoginTypeClaim, loginKindValue));
        }

        if (duplicateLoginKind)
        {
            claims.Add(new Claim(LoginClaimsFactory.LoginTypeClaim, loginKind.ToString().ToUpperInvariant()));
        }

        if (account is not null)
        {
            claims.Add(new Claim(LoginClaimsFactory.AccountClaim, account));
        }

        if (passwordKey is not null)
        {
            claims.Add(new Claim(LoginClaimsFactory.PasswordKeyClaim, passwordKey));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }

    /// <summary>
    /// 建立最小有效 claim 集合。回傳新的列舉結果，呼叫端可安全地加入故障注入 claim；production resolver
    /// 不得保留此 enumerable 或其中的可變 Claim instance。
    /// </summary>
    private static Claim[] CreateClaims(Guid subjectContactId, P7GatewayLoginKind loginKind)
    {
        var contactId = subjectContactId.ToString("D");
        return
        [
            new Claim(ClaimTypes.NameIdentifier, contactId),
            new Claim(LoginClaimsFactory.ContactIdClaim, contactId),
            new Claim(LoginClaimsFactory.LoginTypeClaim, loginKind.ToString().ToUpperInvariant())
        ];
    }
}
