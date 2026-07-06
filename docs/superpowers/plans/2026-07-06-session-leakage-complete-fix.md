# Session Leakage Complete Remediation — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close every session-leakage / broken-identity finding in the 2026-07-06 security audit (C-3, H-1, H-2, H-4, H-5, M-1, and the Referrer-Policy part of M-4) by moving authentication identity into a server-issued encrypted auth ticket, enforcing default-deny authorization, and stopping all client-controllable identity sources.

**Architecture:** Today "login" only writes strings into `Session` and populates a per-session `IMemoryCache`; no auth ticket is ever issued (`SignInAsync` appears 0 times), authorization is opt-in per action, and when the session password is empty the app **recovers identity from the `Referer` header** (`BaseChurchController.TryGetLineUserIdFromRequest`). This plan issues a real cookie auth ticket (`.ChurchReport.Auth`) carrying identity claims on every login, replaces the Referer-based recovery with claims-based recovery, adds a global default-deny authorization filter (an MVC `IAsyncAuthorizationFilter`, because the app runs legacy routing with `EnableEndpointRouting = false`, so `FallbackPolicy` is unavailable), removes credentials from the login JSON response, validates OAuth `returnUrl` as site-local, hardens logout, and sets `Referrer-Policy: no-referrer`.

**Tech Stack:** ASP.NET Core MVC on **net10.0** (legacy `UseMvc`, `EnableEndpointRouting = false`), DevExtreme 21.2.7 front end, Dynamics 365 CRM back end (`QueryExpression`), cookie authentication already registered but unused, xUnit 2.6.6 + FluentAssertions 6.12.0 test project `ChurchReport.MemberInfo.Tests`.

## Global Constraints

- **Target framework:** `net10.0`. Views compile into the DLL — `.cshtml` edits need republish+redeploy+app-pool restart (not relevant here; this plan is `.cs`/config only).
- **Encoding:** every edited `.cs` file must stay **UTF-8 without BOM + CRLF** (project `.editorconfig` / Visual Studio workflow). Do not reformat or re-encode files.
- **Routing:** `services.AddMvc(o => o.EnableEndpointRouting = false)` — endpoint routing is OFF. Do **not** use `FallbackPolicy` / `RequireAuthenticatedUser` (needs endpoint routing). Use a global MVC `IAsyncAuthorizationFilter`.
- **Cookies (intentional, do not change):** Session + Auth cookies are `HttpOnly`, `Secure=Always` (Release), `SameSite=Lax`. `SameSite=Lax` is a deliberate choice for LINE LIFF — keep it. Auth cookie name is `.ChurchReport.Auth`.
- **Caching invariants (do not regress):** `SessionBleeding:EnableResponseCaching=false` stays false; global no-store + `Vary: Cookie` stays; never put user-specific data into a shared cache; never use `[ResponseCache(VaryByQueryKeys)]` in this app.
- **Logging invariant:** sensitive values (tokens, credentials, LINE ids) may only be written through `System.Diagnostics.Debug.WriteLine` (`[Conditional("DEBUG")]`, stripped in Release). Do not add sensitive data to `ILogger`/`Console`/exception responses.
- **Auth scheme constant:** `Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme` (already the registered default).
- **Build (dev):** `dotnet build ChurchReport/ChurchReport.csproj -c Debug`. Avoid `-c Release` locally (known MSB4018 from a zero-filled `obj\Release` static-web-assets cache).
- **Test run:** `dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj` from the worktree root. New tests live under `ChurchReport.MemberInfo.Tests/Security/` in namespace `ChurchReport.MemberInfo.Tests.Security`.
- **Commit discipline:** one commit per task; never `--no-verify`. On the branch `Jesus_5.1.8.WorktreeFabelSecurityScan` (already a worktree; do not branch again).

## Out of Scope (separate remediation tracks — do NOT attempt here)

These audit findings are real but are **not** session-leakage and each is its own change with different risk/verification: **C-1** (rotate secrets, move out of git), **C-2** (password hashing + login throttling/lockout), **H-3** (Personal photo IDOR — object-level authz on `/Personal/GetContactImage(sBatch)`), **M-2** (global antiforgery/CSRF), **M-3** (generalize exception messages), **M-5** (X-Forwarded-For trust), remaining **M-4** headers (HSTS/CSP), **L-1/L-2/L-3/L-4**. Note them in the wrap-up; do not expand this plan to cover them.

---

## File Structure

**New files**
- `ChurchReport/Security/LoginResponsePayload.cs` — login JSON DTO + factory (no credentials). Namespace `ChurchReport.Security`.
- `ChurchReport/Security/LocalReturnUrl.cs` — pure site-local URL validator. Namespace `ChurchReport.Security`.
- `ChurchReport/Security/LoginClaimsFactory.cs` — builds the `ClaimsPrincipal` for the auth ticket. Namespace `ChurchReport.Security`.
- `ChurchReport/Filters/GlobalAuthorizationFilter.cs` — default-deny MVC authorization filter. Namespace `ChurchReport.Filters` (co-located with existing `StrictNoCacheFilter`).
- `ChurchReport.MemberInfo.Tests/Security/LoginResponseFactoryTests.cs`
- `ChurchReport.MemberInfo.Tests/Security/LocalReturnUrlTests.cs`
- `ChurchReport.MemberInfo.Tests/Security/LoginClaimsFactoryTests.cs`
- `ChurchReport.MemberInfo.Tests/Security/RefererIdentityRemovedTests.cs`
- `ChurchReport.MemberInfo.Tests/Security/GlobalAuthorizationFilterTests.cs`

**Modified files**
- `ChurchReport/Controllers/AuthenticationController/AuthenticationController.Private.cs` — `CreateLoginResponse` (drop creds), `InitializeUserSession` (issue ticket, honest comments).
- `ChurchReport/Controllers/AuthenticationController/AuthenticationController.LineLoginOAuth.cs` — validate `returnUrl` local in `LineLoginStart` + `ProcessLineUserLogin`.
- `ChurchReport/Controllers/AuthenticationController/AuthenticationController.Session.cs` — `Logout` hardening (SignOut + delete cookies).
- `ChurchReport/Controllers/BaseChurchController.cs` — add `IssueAuthTicket` helper; rewrite `EnsureCorrectUserData` Step 5 to use claims; **delete** `TryGetLineUserIdFromRequest`; correct `RegenerateSessionId` comment.
- `ChurchReport/Controllers/AppointmentController.cs` — issue ticket in the LINE scheduler login path.
- `ChurchReport/Controllers/SmallGroupController/SmallGroupController.LineLogin.cs` — issue ticket in `HandleLineLogin`.
- `ChurchReport/Startup.cs` — explicit session cookie name; `Referrer-Policy`/`X-Frame-Options` headers; register `GlobalAuthorizationFilter`.
- `ChurchReport/appsettings.json` — add `Security` section (kill-switches).
- Anonymous-endpoint controllers — add `[AllowAnonymous]` (Task 8 whitelist).

---

## Task 1: Remove credentials from the login JSON response (C-3)

**Files:**
- Create: `ChurchReport/Security/LoginResponsePayload.cs`
- Create: `ChurchReport.MemberInfo.Tests/Security/LoginResponseFactoryTests.cs`
- Modify: `ChurchReport/Controllers/AuthenticationController/AuthenticationController.Private.cs:437-448`

**Interfaces:**
- Produces: `ChurchReport.Security.LoginResponsePayload` (record with `DisplayViewType`, `ActiveListId`, `message`, `fullname`) and `ChurchReport.Security.LoginResponseFactory.Build(string displayViewType, string activeListId, string fullName) → LoginResponsePayload`.
- Consumes: nothing new. Note: `ProcessLineUserLogin` reflects over the returned JSON for `DisplayViewType` and `ActiveListId` (LineLoginOAuth.cs:563-591) — both remain, so that path is unaffected.

- [ ] **Step 1: Write the failing test**

Create `ChurchReport.MemberInfo.Tests/Security/LoginResponseFactoryTests.cs`:

```csharp
using ChurchReport.Security;
using FluentAssertions;
using Newtonsoft.Json;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Security
{
    public class LoginResponseFactoryTests
    {
        [Fact]
        public void Build_DoesNotExposeCredentials()
        {
            var payload = LoginResponseFactory.Build("IntegrateView", "list-1", "王小明");
            var json = JsonConvert.SerializeObject(payload);

            json.Should().NotContain("password");
            json.Should().NotContain("account");
            json.Should().NotContain("new_app_pass");
        }

        [Fact]
        public void Build_PreservesFrontEndContractFields()
        {
            var payload = LoginResponseFactory.Build("IntegrateView", "list-1", "王小明");

            payload.DisplayViewType.Should().Be("IntegrateView");
            payload.ActiveListId.Should().Be("list-1");
            payload.fullname.Should().Be("王小明");
            payload.message.Should().Be("歡迎王小明登入成功!");
        }

        [Fact]
        public void Build_NullActiveListId_BecomesEmptyString()
        {
            var payload = LoginResponseFactory.Build("MultiGroupView", null, "A");
            payload.ActiveListId.Should().Be(string.Empty);
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~LoginResponseFactoryTests"`
Expected: FAIL to compile — `The type or namespace name 'LoginResponseFactory' does not exist`.

- [ ] **Step 3: Create the DTO + factory**

Create `ChurchReport/Security/LoginResponsePayload.cs` (UTF-8 no BOM, CRLF):

```csharp
namespace ChurchReport.Security
{
    /// <summary>
    /// 登入成功回應的 JSON 契約。刻意「不含」account / password —— 憑證絕不回傳前端（稽核 C-3）。
    /// 欄位名稱維持與前端既有解析一致：DisplayViewType / ActiveListId / message / fullname。
    /// </summary>
    public sealed record LoginResponsePayload(
        string DisplayViewType,
        string ActiveListId,
        string message,
        string fullname);

    /// <summary>
    /// 建立登入回應內容的唯一入口，確保回應欄位白名單、且永不夾帶憑證。
    /// </summary>
    public static class LoginResponseFactory
    {
        public static LoginResponsePayload Build(string displayViewType, string activeListId, string fullName)
            => new LoginResponsePayload(
                displayViewType,
                activeListId ?? string.Empty,
                "歡迎" + fullName + "登入成功!",
                fullName);
    }
}
```

- [ ] **Step 4: Rewrite `CreateLoginResponse` to use the factory**

In `AuthenticationController.Private.cs`, replace the `CreateLoginResponse` body (currently returns an anonymous object with `account = viewModel.Account, password = viewModel.Password`):

```csharp
        private IActionResult CreateLoginResponse(string displayViewType, string fullName, GalleryViewModel viewModel)
        {
            // C-3：登入回應不得夾帶 account / password（帳密登入＝真實密碼；LINE 登入＝LINE User ID，皆等同憑證）。
            var payload = ChurchReport.Security.LoginResponseFactory.Build(
                displayViewType,
                InMemoryContext.ListManager.ActiveListId,
                fullName);
            return Json(payload);
        }
```

The `viewModel` parameter is now unused but keep the signature (called from `ProcessLogin`); the compiler allows an unused parameter.

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~LoginResponseFactoryTests"`
Expected: PASS (3 tests).

- [ ] **Step 6: Confirm no other action echoes the password**

Run: `dotnet build ChurchReport/ChurchReport.csproj -c Debug` (expect success), then
Run (from worktree root, Git Bash): grep for stray echoes —
`git grep -nE 'password *= *viewModel|new_app_pass *=|password *= *.*Password' -- 'ChurchReport/Controllers/**/*.cs'`
Expected: no login-response matches remain (only the CRM read at `ValidateUserCredentials` comparing `storedPassword`, which is correct).

- [ ] **Step 7: Commit**

```bash
git add ChurchReport/Security/LoginResponsePayload.cs \
        ChurchReport.MemberInfo.Tests/Security/LoginResponseFactoryTests.cs \
        ChurchReport/Controllers/AuthenticationController/AuthenticationController.Private.cs
git commit -m "fix(auth): stop returning account/password in login response (C-3)"
```

---

## Task 2: Validate OAuth `returnUrl` as site-local (H-4)

**Files:**
- Create: `ChurchReport/Security/LocalReturnUrl.cs`
- Create: `ChurchReport.MemberInfo.Tests/Security/LocalReturnUrlTests.cs`
- Modify: `ChurchReport/Controllers/AuthenticationController/AuthenticationController.LineLoginOAuth.cs:64-67` (LineLoginStart) and `:452-514` (ProcessLineUserLogin)

**Interfaces:**
- Produces: `ChurchReport.Security.LocalReturnUrl.IsLocal(string url) → bool` (true only for site-local paths; mirrors `Url.IsLocalUrl` semantics without needing an `IUrlHelper`).

- [ ] **Step 1: Write the failing test**

Create `ChurchReport.MemberInfo.Tests/Security/LocalReturnUrlTests.cs`:

```csharp
using ChurchReport.Security;
using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Security
{
    public class LocalReturnUrlTests
    {
        [Theory]
        [InlineData("/SmallGroup/IntegrateView/1", true)]
        [InlineData("/", true)]
        [InlineData("~/Home/Index", true)]
        [InlineData("//evil.example.com", false)]
        [InlineData("/\\evil.example.com", false)]
        [InlineData("https://evil.example.com", false)]
        [InlineData("http://evil.example.com/path", false)]
        [InlineData("evil.example.com", false)]
        [InlineData("javascript:alert(1)", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsLocal_ClassifiesUrls(string url, bool expected)
            => LocalReturnUrl.IsLocal(url).Should().Be(expected);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~LocalReturnUrlTests"`
Expected: FAIL to compile — `LocalReturnUrl` does not exist.

- [ ] **Step 3: Create the validator**

Create `ChurchReport/Security/LocalReturnUrl.cs`:

```csharp
namespace ChurchReport.Security
{
    /// <summary>
    /// 站內導向白名單判斷。等同 Url.IsLocalUrl 的語意，但不需 IUrlHelper，
    /// 讓 OAuth 回呼流程可在任何位置驗證 returnUrl，杜絕開放重導（稽核 H-4）。
    /// 僅接受：以單一 '/' 開頭且非 '//'、'/\\' 的相對路徑，或 '~/' 開頭的應用相對路徑。
    /// </summary>
    public static class LocalReturnUrl
    {
        public static bool IsLocal(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return false;
            }

            if (url[0] == '/')
            {
                if (url.Length == 1)
                {
                    return true; // "/"
                }
                return url[1] != '/' && url[1] != '\\'; // 拒絕 "//host" 與 "/\\host"
            }

            if (url.Length > 1 && url[0] == '~' && url[1] == '/')
            {
                return true; // "~/..."
            }

            return false;
        }
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~LocalReturnUrlTests"`
Expected: PASS (11 cases).

- [ ] **Step 5: Guard `returnUrl` at both storage and use**

In `LineLoginStart` (LineLoginOAuth.cs), replace the block that stores `returnUrl`:

```csharp
                if (!string.IsNullOrEmpty(returnUrl))
                {
                    // H-4：只接受站內 returnUrl，或綁定流程哨兵值 "_BINDING_"。其餘（含外部網址）一律丟棄。
                    if (returnUrl == "_BINDING_" || ChurchReport.Security.LocalReturnUrl.IsLocal(returnUrl))
                    {
                        HttpContext.Session.SetString("_OAuthReturnUrl", returnUrl);
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[LineLoginStart] 拒絕非站內 returnUrl（不儲存）: {returnUrl}");
                    }
                }
```

In `ProcessLineUserLogin` (LineLoginOAuth.cs), immediately after `HttpContext.Session.Remove("_OAuthReturnUrl");` (currently line ~456), add:

```csharp
                    // H-4：Session 內的 returnUrl 來自 LineLoginStart 的 query（用戶端可控）。使用前再次確認為站內路徑，
                    // 非 "_BINDING_" 且非站內者一律作廢，改走一般登入流程（避免開放重導與把 LINE ID 送往外部站）。
                    if (returnUrl != "_BINDING_" && !ChurchReport.Security.LocalReturnUrl.IsLocal(returnUrl))
                    {
                        System.Diagnostics.Debug.WriteLine($"[ProcessLineUserLogin] 作廢非站內 returnUrl: {returnUrl}");
                        returnUrl = null;
                    }
```

Because `returnUrl` is now guaranteed site-local when the `if (!string.IsNullOrEmpty(returnUrl))` block runs, the existing `return Redirect($"{returnUrl}/{lineUserId}");` stays same-origin. (Residual: the LINE id still appears in a **local** URL — that is finding **L-1**, downgraded to low once `Referrer-Policy: no-referrer` lands in Task 3; fully removing it requires coordinated view changes and is a follow-up.)

- [ ] **Step 6: Build and re-run the security test group**

Run: `dotnet build ChurchReport/ChurchReport.csproj -c Debug`
Expected: success.
Run: `dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~Security"`
Expected: PASS (Task 1 + Task 2 tests).

- [ ] **Step 7: Commit**

```bash
git add ChurchReport/Security/LocalReturnUrl.cs \
        ChurchReport.MemberInfo.Tests/Security/LocalReturnUrlTests.cs \
        ChurchReport/Controllers/AuthenticationController/AuthenticationController.LineLoginOAuth.cs
git commit -m "fix(auth): reject non-local OAuth returnUrl to prevent open redirect (H-4)"
```

---

## Task 3: Add `Referrer-Policy: no-referrer` (+ `X-Frame-Options`) (M-4, supports H-1/L-1)

**Files:**
- Modify: `ChurchReport/Startup.cs:708-739` (the global no-store middleware inside `Configure`)

**Interfaces:** none (response headers only).

- [ ] **Step 1: Add the headers in the existing no-store middleware**

In `Startup.cs`, inside the `app.Use(async (context, next) => { ... })` block that already sets `Cache-Control`/`Pragma`/`Expires`/`X-Content-Type-Options`, add after the `X-Content-Type-Options` line:

```csharp
                // ✅ Referrer-Policy: 不外送 Referer —— 直接切斷「LINE User ID 經由 Referer 外流」的通道（支援 H-1 / L-1）。
                context.Response.Headers["Referrer-Policy"] = "no-referrer";

                // ✅ X-Frame-Options: 僅允許同源內嵌，降低點擊劫持風險。
                context.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
```

- [ ] **Step 2: Build**

Run: `dotnet build ChurchReport/ChurchReport.csproj -c Debug`
Expected: success.

- [ ] **Step 3: Verify headers on the running app (scripted)**

Start the app (`dotnet run --project ChurchReport/ChurchReport.csproj` — dev listens on `http://localhost:43371/`), then in PowerShell:

```powershell
$r = Invoke-WebRequest -Uri http://localhost:43371/Login -UseBasicParsing
$r.Headers['Referrer-Policy']   # expect: no-referrer
$r.Headers['X-Frame-Options']   # expect: SAMEORIGIN
```

Expected: `no-referrer` and `SAMEORIGIN`. Stop the app.

- [ ] **Step 4: Commit**

```bash
git add ChurchReport/Startup.cs
git commit -m "fix(headers): add Referrer-Policy no-referrer and X-Frame-Options (M-4)"
```

---

## Task 4: Issue a real authentication ticket on every login (H-2/H-5 foundation, H-1 enabler)

**Files:**
- Create: `ChurchReport/Security/LoginClaimsFactory.cs`
- Create: `ChurchReport.MemberInfo.Tests/Security/LoginClaimsFactoryTests.cs`
- Modify: `ChurchReport/Controllers/BaseChurchController.cs` (add `IssueAuthTicket` helper + `using`)
- Modify: `ChurchReport/Controllers/AuthenticationController/AuthenticationController.Private.cs` (`InitializeUserSession` calls `IssueAuthTicket`)
- Modify: `ChurchReport/Controllers/AppointmentController.cs` (LINE scheduler login path)
- Modify: `ChurchReport/Controllers/SmallGroupController/SmallGroupController.LineLogin.cs` (`HandleLineLogin`)

**Interfaces:**
- Produces: `ChurchReport.Security.LoginClaimsFactory` with constants `ContactIdClaim`, `AccountClaim`, `PasswordKeyClaim`, `LoginTypeClaim` and `Build(string contactId, string account, string passwordKey, string loginType) → ClaimsPrincipal` (authenticated identity).
- Produces: `BaseChurchController.IssueAuthTicket(string contactId, string account, string passwordKey, string loginType)` (protected) — signs in with the cookie scheme. `loginType` is `"LINE"` or `"ACCOUNT"`; `passwordKey` is the LINE user id for LINE logins and empty for account logins (the real account password is **never** placed in the ticket).
- Consumes: Task 6 reads `LoginTypeClaim` / `PasswordKeyClaim` / `AccountClaim`; Task 8 reads `User.Identity.IsAuthenticated`.

- [ ] **Step 1: Write the failing test**

Create `ChurchReport.MemberInfo.Tests/Security/LoginClaimsFactoryTests.cs`:

```csharp
using ChurchReport.Security;
using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Security
{
    public class LoginClaimsFactoryTests
    {
        [Fact]
        public void Build_MarksPrincipalAuthenticated()
        {
            var p = LoginClaimsFactory.Build("cid-1", "alice", "", "ACCOUNT");
            p.Identity.Should().NotBeNull();
            p.Identity!.IsAuthenticated.Should().BeTrue();
        }

        [Fact]
        public void Build_AccountLogin_DoesNotStoreCredential()
        {
            var p = LoginClaimsFactory.Build("cid-1", "alice", "", "ACCOUNT");
            p.FindFirst(LoginClaimsFactory.AccountClaim)!.Value.Should().Be("alice");
            p.FindFirst(LoginClaimsFactory.LoginTypeClaim)!.Value.Should().Be("ACCOUNT");
            p.FindFirst(LoginClaimsFactory.PasswordKeyClaim)!.Value.Should().Be("");
            p.FindFirst(LoginClaimsFactory.ContactIdClaim)!.Value.Should().Be("cid-1");
        }

        [Fact]
        public void Build_LineLogin_CarriesLineIdAsWorkingKey()
        {
            var p = LoginClaimsFactory.Build("cid-2", "LineIdLogin", "U0123456789abcdef0123456789abcdef", "LINE");
            p.FindFirst(LoginClaimsFactory.LoginTypeClaim)!.Value.Should().Be("LINE");
            p.FindFirst(LoginClaimsFactory.PasswordKeyClaim)!.Value.Should().Be("U0123456789abcdef0123456789abcdef");
        }

        [Fact]
        public void Build_NullInputs_DoNotThrow()
        {
            var p = LoginClaimsFactory.Build(null, null, null, null);
            p.Identity!.IsAuthenticated.Should().BeTrue();
            p.FindFirst(LoginClaimsFactory.AccountClaim)!.Value.Should().Be("");
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~LoginClaimsFactoryTests"`
Expected: FAIL to compile — `LoginClaimsFactory` does not exist.

- [ ] **Step 3: Create the claims factory**

Create `ChurchReport/Security/LoginClaimsFactory.cs`:

```csharp
using System.Collections.Generic;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace ChurchReport.Security
{
    /// <summary>
    /// 建立登入用的認證主體（ClaimsPrincipal）。這是「伺服器端簽發、加密、HttpOnly」的權威身分來源，
    /// 取代過去以 Referer / Session 字串推導身分的作法（稽核 H-1 / H-2）。
    ///
    /// 重要：帳號登入「不」把真實密碼放進票證；只有 LINE 登入把 LINE User ID 當作可重建工作階段的 key
    /// （LINE User ID 本即使用者識別碼，且票證本身經 Data Protection 加密、JS 不可讀）。
    /// </summary>
    public static class LoginClaimsFactory
    {
        public const string ContactIdClaim = "church:contactId";
        public const string AccountClaim = "church:account";
        public const string PasswordKeyClaim = "church:pwdkey";
        public const string LoginTypeClaim = "church:loginType";

        public static ClaimsPrincipal Build(string contactId, string account, string passwordKey, string loginType)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, contactId ?? string.Empty),
                new Claim(ContactIdClaim, contactId ?? string.Empty),
                new Claim(AccountClaim, account ?? string.Empty),
                new Claim(PasswordKeyClaim, passwordKey ?? string.Empty),
                new Claim(LoginTypeClaim, loginType ?? string.Empty),
            };

            // 傳入 authenticationType（scheme 名稱）才會讓 Identity.IsAuthenticated 為 true。
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            return new ClaimsPrincipal(identity);
        }
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~LoginClaimsFactoryTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Add the `IssueAuthTicket` helper to `BaseChurchController`**

In `BaseChurchController.cs`, add to the `using` block (near the other `using`s at top):

```csharp
using Microsoft.AspNetCore.Authentication;
```

Add this method inside the `Session 摰撽?` region (near `RegenerateSessionId`), before `#endregion`:

```csharp
        /// <summary>
        /// 簽發認證票證（.ChurchReport.Auth）。所有「建立登入身分」的進入點都應呼叫此方法，
        /// 讓身分改由伺服器端加密票證承載，而非散落於 Session 字串或用戶端可控標頭。
        /// loginType："LINE" 或 "ACCOUNT"；passwordKey：LINE 登入＝LINE User ID，帳號登入＝空字串（不放真實密碼）。
        /// </summary>
        protected void IssueAuthTicket(string contactId, string account, string passwordKey, string loginType)
        {
            try
            {
                var principal = ChurchReport.Security.LoginClaimsFactory.Build(contactId, account, passwordKey, loginType);
                HttpContext.SignInAsync(
                    Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme,
                    principal).GetAwaiter().GetResult();
                System.Diagnostics.Debug.WriteLine($"[IssueAuthTicket] 已簽發認證票證 loginType={loginType}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[IssueAuthTicket] 簽發票證失敗: {ex.Message}");
            }
        }
```

- [ ] **Step 6: Call `IssueAuthTicket` from `InitializeUserSession`**

In `AuthenticationController.Private.cs`, inside `InitializeUserSession`, after the block that writes `_LoginAccount` / `_LoginPassword` to Session (currently ends around line 258), add:

```csharp
            // H-2 / H-1：簽發權威認證票證。帳號登入不放密碼；LINE 登入以 LINE User ID 作為工作階段重建 key。
            var loginType = viewModel.Account == "LineIdLogin" ? "LINE" : "ACCOUNT";
            var passwordKey = loginType == "LINE" ? (viewModel.Password ?? string.Empty) : string.Empty;
            IssueAuthTicket(loginContact?.Id.ToString(), viewModel.Account, passwordKey, loginType);
```

- [ ] **Step 7: Call `IssueAuthTicket` from the LINE scheduler login path**

In `AppointmentController.cs`, in `SetupAppointmentAccountPassword` (lines 184-189), after setting `m_Account` / `m_Password`, add:

```csharp
            // 讓 LINE 行事曆登入也持有認證票證，避免 Task 8 全域授權將其視為未登入。
            var lineUserId = InMemoryContext.LineBindingViewModel.LineUserId;
            HttpContext?.Session?.SetString("_LoginAccount", "LineIdLogin");
            HttpContext?.Session?.SetString("_LoginPassword", lineUserId ?? string.Empty);
            HttpContext?.Session?.SetString("_SessionUserId", lineUserId ?? string.Empty);
            IssueAuthTicket(null, "LineIdLogin", lineUserId ?? string.Empty, "LINE");
```

(`AppointmentController` derives from `BaseChurchController`, so `IssueAuthTicket` is in scope. Confirm with `git grep -n "class AppointmentController"`.)

- [ ] **Step 8: Call `IssueAuthTicket` from `SmallGroupController.HandleLineLogin`**

In `SmallGroupController.LineLogin.cs`, inside the `else` branch, right after `string fullName = contact.Attributes["fullname"].ToString();` and before the `SetupSmallGroupData` task, add:

```csharp
                    // LINE 小組登入也簽發認證票證並寫入權威 Session 身分。
                    HttpContext?.Session?.SetString("_LoginAccount", "LineIdLogin");
                    HttpContext?.Session?.SetString("_LoginPassword", lineUserId);
                    HttpContext?.Session?.SetString("_SessionUserId", lineUserId);
                    IssueAuthTicket(contact.Id.ToString(), "LineIdLogin", lineUserId, "LINE");
```

- [ ] **Step 9: Build**

Run: `dotnet build ChurchReport/ChurchReport.csproj -c Debug`
Expected: success (warnings ok; no new errors).

- [ ] **Step 10: Manual smoke — a real login now issues the auth cookie**

Start the app; in a browser dev-tools Network tab, perform an account login (`POST /Authentication/ProcessLogin`). Confirm the response `Set-Cookie` includes `.ChurchReport.Auth=...; httponly`. Confirm a follow-up navigation sends that cookie. Stop the app.

- [ ] **Step 11: Commit**

```bash
git add ChurchReport/Security/LoginClaimsFactory.cs \
        ChurchReport.MemberInfo.Tests/Security/LoginClaimsFactoryTests.cs \
        ChurchReport/Controllers/BaseChurchController.cs \
        ChurchReport/Controllers/AuthenticationController/AuthenticationController.Private.cs \
        ChurchReport/Controllers/AppointmentController.cs \
        ChurchReport/Controllers/SmallGroupController/SmallGroupController.LineLogin.cs
git commit -m "feat(auth): issue encrypted auth ticket on every login path (H-2 foundation)"
```

---

## Task 5: Honest session handling + explicit session cookie name (H-5)

**Files:**
- Modify: `ChurchReport/Startup.cs` (`AddSession` — set `options.Cookie.Name`)
- Modify: `ChurchReport/Controllers/AuthenticationController/AuthenticationController.Private.cs` (`InitializeUserSession` — correct the false "regenerate Session ID" comments)
- Modify: `ChurchReport/Controllers/BaseChurchController.cs` (`RegenerateSessionId` — correct comment)

**Interfaces:** none. This task makes the fixation defense **truthful**: identity now lives in the freshly-issued auth ticket (Task 4), which is re-minted on every `SignInAsync`; the misleading comments claiming `Session.Clear()+CommitAsync()` "regenerates the Session ID" are corrected, and the session cookie gets a deterministic name so Task 7 can delete it on logout.

> **Design note (why not force a new session id here):** ASP.NET Core `ISession` has no supported "regenerate id" API; `Clear()+CommitAsync()` only clears data, it does not rotate the cookie (consistent with the project memory note "Session.Clear() keeps same Session ID"). Because login is an AJAX POST returning JSON (no new-request boundary), deleting+re-minting the session cookie mid-request would orphan the identity we just wrote. The correct, achievable fixation defense is: **authentication authority lives in the auth ticket, not the session id.** A fixated session id therefore grants nothing — it carries no authenticated principal until a legitimate `SignInAsync` occurs in the victim's own browser, which mints a new encrypted auth cookie the attacker cannot read. We still clear pre-login session data (already done) and delete the session cookie on logout (Task 7).

- [ ] **Step 1: Give the session cookie an explicit name**

In `Startup.cs`, inside `services.AddSession(options => { ... })`, add (next to the other `options.Cookie.*` lines):

```csharp
                // 明確命名 Session Cookie，讓登出時可確定性刪除（與 .ChurchReport.Auth 分離）。
                options.Cookie.Name = ".ChurchReport.Session";
```

- [ ] **Step 2: Correct the misleading comments in `InitializeUserSession`**

In `AuthenticationController.Private.cs`, replace the two comment banners that claim Session-ID regeneration. Change the Step 2 banner text to state what actually happens:

```csharp
            // ========================================
            // Session Fixation 防護 - Step 2: 清除舊工作階段資料並提交
            // ========================================
            // 注意：ASP.NET Core 的 Session.Clear()+CommitAsync() 只清資料、不會輪替 Session ID。
            // 真正的固定攻擊防護來自「認證票證」：登入會經 IssueAuthTicket 重新簽發加密票證，
            // 身分綁定於票證而非 Session ID，因此即使 Session ID 被固定也無法冒用身分。
```

And remove the false line in the completion log:

```csharp
            System.Diagnostics.Debug.WriteLine($"[InitializeUserSession]   - Session ID: 已重新生成（新的唯一 ID）");
```

Replace it with:

```csharp
            System.Diagnostics.Debug.WriteLine($"[InitializeUserSession]   - 身分綁定於認證票證（.ChurchReport.Auth）");
```

- [ ] **Step 3: Correct `RegenerateSessionId` comment in `BaseChurchController`**

In `BaseChurchController.cs`, in `RegenerateSessionId`, change the log line `"[RegenerateSessionId] Session ID regenerated."` to:

```csharp
                System.Diagnostics.Debug.WriteLine("[RegenerateSessionId] Session data cleared (note: ASP.NET Core does not rotate the Session ID here; identity is bound to the auth ticket).");
```

(Leave the method in place — it is currently only self-referenced; do not delete to avoid touching unknown reflection callers.)

- [ ] **Step 4: Build**

Run: `dotnet build ChurchReport/ChurchReport.csproj -c Debug`
Expected: success.

- [ ] **Step 5: Verify the session cookie name on the running app**

Start the app; in dev-tools, load `/Login` then log in; confirm two distinct cookies exist: `.ChurchReport.Session` and `.ChurchReport.Auth`. Stop the app.

- [ ] **Step 6: Commit**

```bash
git add ChurchReport/Startup.cs \
        ChurchReport/Controllers/AuthenticationController/AuthenticationController.Private.cs \
        ChurchReport/Controllers/BaseChurchController.cs
git commit -m "fix(session): name session cookie and correct false Session-ID rotation comments (H-5)"
```

---

## Task 6: Replace Referer-derived identity with claims-based recovery, and delete the Referer path (H-1)

**Files:**
- Modify: `ChurchReport/Controllers/BaseChurchController.cs` — rewrite `EnsureCorrectUserData` "Step 5" (lines ~737-765) to read the authenticated principal; **delete** `TryGetLineUserIdFromRequest` (lines ~869-889).
- Create: `ChurchReport.MemberInfo.Tests/Security/RefererIdentityRemovedTests.cs`

**Interfaces:**
- Consumes: `LoginClaimsFactory.LoginTypeClaim` / `PasswordKeyClaim` / `AccountClaim` from Task 4.

- [ ] **Step 1: Write the failing regression-guard test**

Create `ChurchReport.MemberInfo.Tests/Security/RefererIdentityRemovedTests.cs`:

```csharp
using System.Reflection;
using ChurchReport.Controllers;
using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Security
{
    public class RefererIdentityRemovedTests
    {
        [Fact]
        public void TryGetLineUserIdFromRequest_IsDeleted()
        {
            // H-1：身分絕不可從用戶端可控的 Referer 標頭推導。此方法必須被移除。
            var method = typeof(BaseChurchController).GetMethod(
                "TryGetLineUserIdFromRequest",
                BindingFlags.NonPublic | BindingFlags.Instance);

            method.Should().BeNull(
                "identity must never be derived from the client-controlled Referer header (H-1)");
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~RefererIdentityRemovedTests"`
Expected: FAIL — the method still exists (`method` is not null).

- [ ] **Step 3: Rewrite `EnsureCorrectUserData` Step 5 to use claims**

In `BaseChurchController.cs`, replace the entire `Step 5` block (the `if (string.IsNullOrEmpty(sessionPassword)) { var lineUserId = TryGetLineUserIdFromRequest(); ... }` section) with:

```csharp
                // ========================================
                // Step 5: Session 密碼為空時，只能從「認證票證（伺服器端加密）」重建身分，
                //         絕不從 Referer / Query 等用戶端可控來源推導（H-1）。
                //         僅 LINE 登入在票證內帶有可重建 key；帳號登入的 session 若遺失則需重新登入。
                // ========================================
                if (string.IsNullOrEmpty(sessionPassword))
                {
                    var principal = HttpContext?.User;
                    if (principal?.Identity?.IsAuthenticated == true)
                    {
                        var loginType = principal.FindFirst(ChurchReport.Security.LoginClaimsFactory.LoginTypeClaim)?.Value;
                        var pwdKey = principal.FindFirst(ChurchReport.Security.LoginClaimsFactory.PasswordKeyClaim)?.Value;

                        if (loginType == "LINE" && !string.IsNullOrEmpty(pwdKey) && pwdKey != listManagerPassword)
                        {
#if DEBUG
                            System.Diagnostics.Debug.WriteLine("[BaseChurch.EnsureCorrectUserData] Session 密碼為空：以認證票證的 LINE 身分重建工作階段");
#endif
                            InMemoryContext.ListManager.SetupListManager(
                                "LineIdLogin",
                                pwdKey,
                                InMemoryContext.ListManager.m_SelectDate != default
                                    ? InMemoryContext.ListManager.m_SelectDate
                                    : DateTime.Now);

                            HttpContext?.Session?.SetString("_LoginAccount", "LineIdLogin");
                            HttpContext?.Session?.SetString("_LoginPassword", pwdKey);

                            var linePasswordHash = GetStableHash(pwdKey);
                            var lineCacheKey = $"{sessionId}_{linePasswordHash}";
                            _userValidationCache[lineCacheKey] = (DateTime.UtcNow, true, linePasswordHash);
                        }
                    }
                }
```

- [ ] **Step 4: Delete `TryGetLineUserIdFromRequest`**

In `BaseChurchController.cs`, delete the whole `TryGetLineUserIdFromRequest` method (its XML-doc summary block plus the method body, lines ~854-889). Confirm no remaining references:

Run: `git grep -n "TryGetLineUserIdFromRequest" -- 'ChurchReport/**/*.cs'`
Expected: no matches.

- [ ] **Step 5: Build and run the guard test**

Run: `dotnet build ChurchReport/ChurchReport.csproj -c Debug`
Expected: success.
Run: `dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~RefererIdentityRemovedTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add ChurchReport/Controllers/BaseChurchController.cs \
        ChurchReport.MemberInfo.Tests/Security/RefererIdentityRemovedTests.cs
git commit -m "fix(auth): derive recovered identity from auth ticket claims, delete Referer path (H-1)"
```

---

## Task 7: Harden logout — real invalidation + cookie deletion (M-1)

**Files:**
- Modify: `ChurchReport/Controllers/AuthenticationController/AuthenticationController.Session.cs` (`Logout`)

**Interfaces:** `Logout` becomes `async Task<IActionResult>` (routes unchanged).

- [ ] **Step 1: Rewrite `Logout`**

In `AuthenticationController.Session.cs`, add to the `using` block:

```csharp
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
```

Replace the `Logout` method with:

```csharp
        [HttpGet]
        [HttpPost]
        [Route("/Authentication/Logout")]
        [Route("/Logout")]
        public async Task<IActionResult> Logout()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[Logout] 開始登出流程");

                // M-1：真正失效——清 Session 資料、撤銷認證票證、刪除兩個 Cookie。
                HttpContext.Session.Clear();
                await HttpContext.Session.CommitAsync();

                await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

                Response.Cookies.Delete(".ChurchReport.Session");
                Response.Cookies.Delete(".ChurchReport.Auth");

                System.Diagnostics.Debug.WriteLine("[Logout] 登出完成：Session 已清、認證票證已撤銷、Cookie 已刪除");
                return RedirectToAction("Login");
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine($"[Logout] 登出失敗: {e.Message}");
                return HandleError(e, "Logout");
            }
        }
```

- [ ] **Step 2: Build**

Run: `dotnet build ChurchReport/ChurchReport.csproj -c Debug`
Expected: success.

- [ ] **Step 3: Verify logout deletes both cookies (scripted)**

Start the app; log in in a browser; hit `/Logout`. In dev-tools confirm the response `Set-Cookie` expires both `.ChurchReport.Session` and `.ChurchReport.Auth` (past-dated / empty), and that re-requesting a protected page redirects to `/Login`. Stop the app.

- [ ] **Step 4: Commit**

```bash
git add ChurchReport/Controllers/AuthenticationController/AuthenticationController.Session.cs
git commit -m "fix(auth): logout revokes auth ticket and deletes session+auth cookies (M-1)"
```

---

## Task 8: Global default-deny authorization filter + `[AllowAnonymous]` whitelist (H-2)

**Files:**
- Create: `ChurchReport/Filters/GlobalAuthorizationFilter.cs`
- Create: `ChurchReport.MemberInfo.Tests/Security/GlobalAuthorizationFilterTests.cs`
- Modify: `ChurchReport/Startup.cs` (`AddMvc` — register the filter)
- Modify: `ChurchReport/appsettings.json` (add `Security` section)
- Modify: anonymous-endpoint controllers — add `[AllowAnonymous]`

**Interfaces:**
- Consumes: `User.Identity.IsAuthenticated` (from Task 4 tickets), config `Security:EnforceGlobalAuthorization` and `Security:AllowSessionIdentityFallback`.
- Produces: `ChurchReport.Filters.GlobalAuthorizationFilter : IAsyncAuthorizationFilter`.

> **Rollout safety:** the filter is registered always but honors `Security:EnforceGlobalAuthorization`. Ship the first commit with that flag **false** in `appsettings.json` (no behavior change), complete the whitelist + the verification matrix in a staging deploy with the flag **true**, then flip the committed value to `true`. Missing key defaults to **true** (secure by default) so other environments are protected. `Security:AllowSessionIdentityFallback` (default true) lets any legacy login path that establishes a server-side session (but not yet a ticket) keep working during migration; set it false once every login path issues a ticket.

- [ ] **Step 1: Write the failing filter test**

Create `ChurchReport.MemberInfo.Tests/Security/GlobalAuthorizationFilterTests.cs`:

```csharp
using System.Collections.Generic;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using ChurchReport.Filters;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Security
{
    public class GlobalAuthorizationFilterTests
    {
        private class Fakes
        {
            [AllowAnonymous] public void AnonAction() { }
            public void SecureAction() { }
        }

        private static IConfiguration Config(bool? enforce, bool? sessionFallback = false)
        {
            var dict = new Dictionary<string, string>();
            if (enforce.HasValue) dict["Security:EnforceGlobalAuthorization"] = enforce.Value.ToString();
            if (sessionFallback.HasValue) dict["Security:AllowSessionIdentityFallback"] = sessionFallback.Value.ToString();
            return new ConfigurationBuilder().AddInMemoryCollection(dict!).Build();
        }

        private static AuthorizationFilterContext MakeContext(bool authenticated, bool allowAnonymous, bool ajax)
        {
            var http = new DefaultHttpContext();
            http.User = authenticated
                ? new ClaimsPrincipal(new ClaimsIdentity("cookie"))
                : new ClaimsPrincipal(new ClaimsIdentity());
            if (ajax) http.Request.Headers["X-Requested-With"] = "XMLHttpRequest";

            var mi = typeof(Fakes).GetMethod(allowAnonymous ? nameof(Fakes.AnonAction) : nameof(Fakes.SecureAction));
            var descriptor = new ControllerActionDescriptor
            {
                MethodInfo = mi!,
                ControllerTypeInfo = typeof(Fakes).GetTypeInfo()
            };
            var actionContext = new ActionContext(http, new RouteData(), descriptor);
            return new AuthorizationFilterContext(actionContext, new List<IFilterMetadata>());
        }

        [Fact]
        public async Task Unauthenticated_SecureAction_RedirectsToLogin()
        {
            var ctx = MakeContext(authenticated: false, allowAnonymous: false, ajax: false);
            await new GlobalAuthorizationFilter(Config(true)).OnAuthorizationAsync(ctx);
            ctx.Result.Should().BeOfType<RedirectToActionResult>();
        }

        [Fact]
        public async Task Unauthenticated_Ajax_Returns401()
        {
            var ctx = MakeContext(authenticated: false, allowAnonymous: false, ajax: true);
            await new GlobalAuthorizationFilter(Config(true)).OnAuthorizationAsync(ctx);
            ctx.Result.Should().BeOfType<StatusCodeResult>()
                .Which.StatusCode.Should().Be(401);
        }

        [Fact]
        public async Task Authenticated_SecureAction_IsAllowed()
        {
            var ctx = MakeContext(authenticated: true, allowAnonymous: false, ajax: false);
            await new GlobalAuthorizationFilter(Config(true)).OnAuthorizationAsync(ctx);
            ctx.Result.Should().BeNull();
        }

        [Fact]
        public async Task AnonymousAction_IsAllowed_EvenWhenUnauthenticated()
        {
            var ctx = MakeContext(authenticated: false, allowAnonymous: true, ajax: false);
            await new GlobalAuthorizationFilter(Config(true)).OnAuthorizationAsync(ctx);
            ctx.Result.Should().BeNull();
        }

        [Fact]
        public async Task EnforcementDisabled_AllowsEverything()
        {
            var ctx = MakeContext(authenticated: false, allowAnonymous: false, ajax: false);
            await new GlobalAuthorizationFilter(Config(false)).OnAuthorizationAsync(ctx);
            ctx.Result.Should().BeNull();
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~GlobalAuthorizationFilterTests"`
Expected: FAIL to compile — `GlobalAuthorizationFilter` does not exist.

- [ ] **Step 3: Create the filter**

Create `ChurchReport/Filters/GlobalAuthorizationFilter.cs`:

```csharp
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;

namespace ChurchReport.Filters
{
    /// <summary>
    /// 全域「預設拒絕」授權過濾器（稽核 H-2）。
    /// 任何未標記 [AllowAnonymous] 的 action，在使用者未通過認證時一律導向登入（AJAX 回 401）。
    /// 因為本專案關閉 Endpoint Routing（EnableEndpointRouting = false），無法使用 FallbackPolicy，
    /// 故以 MVC 授權過濾器達成預設拒絕。
    /// </summary>
    public sealed class GlobalAuthorizationFilter : IAsyncAuthorizationFilter
    {
        private readonly IConfiguration _configuration;

        public GlobalAuthorizationFilter(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            // 安全預設：鍵缺失視為啟用。
            var enforce = _configuration.GetValue<bool?>("Security:EnforceGlobalAuthorization") ?? true;
            if (!enforce)
            {
                return Task.CompletedTask;
            }

            if (IsAnonymousAllowed(context))
            {
                return Task.CompletedTask;
            }

            var user = context.HttpContext.User;
            if (user?.Identity?.IsAuthenticated == true)
            {
                return Task.CompletedTask;
            }

            // 遷移期相容：若舊登入流程已建立伺服器端 Session 身分但尚未簽發票證，暫時放行。
            var allowSessionFallback = _configuration.GetValue<bool?>("Security:AllowSessionIdentityFallback") ?? true;
            if (allowSessionFallback && HasServerSessionIdentity(context.HttpContext))
            {
                return Task.CompletedTask;
            }

            if (IsAjax(context.HttpContext.Request))
            {
                context.Result = new StatusCodeResult(StatusCodes.Status401Unauthorized);
            }
            else
            {
                context.Result = new RedirectToActionResult("Login", "Authentication", null);
            }

            return Task.CompletedTask;
        }

        private static bool IsAnonymousAllowed(AuthorizationFilterContext context)
        {
            if (context.ActionDescriptor is ControllerActionDescriptor cad)
            {
                var onMethod = cad.MethodInfo.GetCustomAttributes(true).OfType<IAllowAnonymous>().Any();
                var onController = cad.ControllerTypeInfo.GetCustomAttributes(true).OfType<IAllowAnonymous>().Any();
                if (onMethod || onController)
                {
                    return true;
                }
            }

            // 保險：部分過濾器管線會把 AllowAnonymous 表示為 IAllowAnonymousFilter。
            return context.Filters.OfType<IAllowAnonymousFilter>().Any();
        }

        private static bool HasServerSessionIdentity(HttpContext http)
        {
            try
            {
                var session = http.Session;
                return !string.IsNullOrEmpty(session.GetString("_SessionUserId"))
                       || !string.IsNullOrEmpty(session.GetString("_LoginPassword"));
            }
            catch
            {
                return false;
            }
        }

        private static bool IsAjax(HttpRequest request)
        {
            if (string.Equals(request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var accept = request.Headers["Accept"].ToString();
            return accept.Contains("application/json", StringComparison.OrdinalIgnoreCase);
        }
    }
}
```

- [ ] **Step 4: Run the filter test to verify it passes**

Run: `dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~GlobalAuthorizationFilterTests"`
Expected: PASS (5 tests).

- [ ] **Step 5: Register the filter in `AddMvc`**

In `Startup.cs`, inside `services.AddMvc(options => { ... })`, after the existing `options.Filters.Add<ChurchReport.Filters.StrictNoCacheFilter>();`, add:

```csharp
                    // ✅ 全域預設拒絕授權（H-2）：未認證者除白名單外一律導向登入 / 回 401。
                    options.Filters.Add<ChurchReport.Filters.GlobalAuthorizationFilter>();
                    Console.WriteLine("[Startup] ✅ GlobalAuthorizationFilter 已註冊（預設拒絕授權）");
```

(`options.Filters.Add<T>()` activates `T` through DI, so the `IConfiguration` constructor dependency is injected automatically.)

- [ ] **Step 6: Add the `Security` config section (enforcement OFF for the canary commit)**

In `ChurchReport/appsettings.json`, add a top-level `Security` object (place it near the other root sections; keep valid JSON):

```json
  "Security": {
    "EnforceGlobalAuthorization": false,
    "AllowSessionIdentityFallback": true
  },
```

- [ ] **Step 7: Build the full app**

Run: `dotnet build ChurchReport/ChurchReport.csproj -c Debug`
Expected: success. (Enforcement is off, so no runtime behavior change yet.)

- [ ] **Step 8: Enumerate every action and its anonymous status**

Produce the audit matrix from the running route table. From the worktree root (Git Bash), list every `[Route(` and `[AllowAnonymous]` occurrence to classify:

```bash
git grep -nE '\[Route\(|\[AllowAnonymous\]|public (partial )?class .*Controller' -- 'ChurchReport/Controllers/**/*.cs' > /tmp/route_audit.txt
```

Mark `[AllowAnonymous]` on the following **known pre-auth / public** surfaces (add the attribute at controller level where the whole controller is public, else per action). Add `using Microsoft.AspNetCore.Authorization;` to each edited file:

- `AuthenticationController` — **controller level** `[AllowAnonymous]` (Login, ProcessLogin, LINE OAuth start/callback, LineIdLoginView, SaveUserLineId, SaveUserId, ProcessLineLogin, LineBinding*, LineLiffView, Privacy, Logout, CheckSession, ExtendSession).
- `QrCodeController` — **controller level** (public QR landing pages).
- `MyPayController`, `TSPGController`, `PaymentReturnController`, `DonationPaymentLoginController` — **controller level** (payment provider callbacks + pre-auth donation login must be anonymous).
- `HomeController` — **per action**: `Error`, `DonationPaymentLogin`, `ProcessDonationPaymentLogin`, and every LINE/QR entry action reachable before login (e.g. `QualificationView`, `*QrCodeView`, `LineLiffView` if present). Classify each Home action from the matrix; annotate the pre-auth ones.
- `DedicationController.DediationLineLoginView`, `PhoneBindingController.ChangePhoneView` — **per action** (LINE entry views).

For each edit, the pattern is:

```csharp
using Microsoft.AspNetCore.Authorization;
// ...
    [AllowAnonymous]
    public class QrCodeController : BaseChurchController
```

or for a single action:

```csharp
        [AllowAnonymous]
        [HttpGet]
        [Route("/Home/Error")]
        public IActionResult Error() { /* ... */ }
```

- [ ] **Step 9: Build after annotations**

Run: `dotnet build ChurchReport/ChurchReport.csproj -c Debug`
Expected: success.

- [ ] **Step 10: Staging verification matrix (flag ON)**

Deploy to staging (or run locally) with `Security:EnforceGlobalAuthorization=true` (override via environment: PowerShell `\$env:Security__EnforceGlobalAuthorization = "true"`). Walk this matrix and record results:

1. Anonymous `GET /Login`, `GET /Privacy`, `GET /health`, a QR landing URL, a payment callback URL → **200/expected** (not redirected).
2. Anonymous `GET /SmallGroup/IntegrateView/1` (a protected page) → **302 → /Authentication/Login**.
3. Anonymous protected AJAX (`X-Requested-With: XMLHttpRequest`) → **401**.
4. Full account login → land on the app; then the same protected page → **200**.
5. Full LINE login (LIFF + OAuth) → land on the app; protected page → **200**.
6. `GET /Logout` → protected page again → **302 → Login**.
7. With a crafted `Referer: https://x/UAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA` and no session/ticket, hit a protected page → **302 → Login** (identity is NOT established from Referer).

Fix any endpoint that is wrongly blocked (missing `[AllowAnonymous]`) or wrongly open (unexpected pass) before proceeding.

- [ ] **Step 11: Flip enforcement on in committed config**

After the matrix passes in staging, set `ChurchReport/appsettings.json` → `"EnforceGlobalAuthorization": true`. (Keep `AllowSessionIdentityFallback: true` until a follow-up confirms every login path issues a ticket, then set it false.)

- [ ] **Step 12: Full test run + commit**

Run: `dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj`
Expected: PASS (all Security tests + pre-existing tests).

```bash
git add ChurchReport/Filters/GlobalAuthorizationFilter.cs \
        ChurchReport.MemberInfo.Tests/Security/GlobalAuthorizationFilterTests.cs \
        ChurchReport/Startup.cs \
        ChurchReport/appsettings.json \
        ChurchReport/Controllers
git commit -m "feat(auth): global default-deny authorization filter with AllowAnonymous whitelist (H-2)"
```

---

## Verification Checklist (maps to audit §14)

- [ ] Login response JSON contains no `password` / `account` / `new_app_pass` (Task 1 test + captured response).
- [ ] Every successful login issues `.ChurchReport.Auth`; value differs on each login; `User.Identity.IsAuthenticated` is true afterwards (Task 4).
- [ ] Session cookie is named `.ChurchReport.Session`; comments no longer claim false Session-ID rotation (Task 5).
- [ ] `TryGetLineUserIdFromRequest` is gone; a crafted `Referer` establishes no identity (Task 6 test + matrix #7).
- [ ] Logout revokes the ticket and deletes both cookies; post-logout protected access → Login (Task 7 + matrix #6).
- [ ] `LineLoginStart?returnUrl=<external>` never redirects off-site (Task 2 + inspection).
- [ ] Responses carry `Referrer-Policy: no-referrer` (Task 3).
- [ ] Unauthenticated access to every protected action → 302 Login / 401; only whitelisted anonymous endpoints pass (Task 8 matrix).

## Wrap-up note for the requester

This plan closes the **session-leakage / broken-identity** cluster only. Still open as separate tracks (see "Out of Scope"): **C-1** secrets rotation + git history purge, **C-2** password hashing + login lockout, **H-3** Personal photo IDOR, **M-2** CSRF, **M-3** exception-message generalization, and the remaining **M-4** headers (HSTS/CSP). Recommend scheduling C-1 and C-2 next (highest residual risk).
