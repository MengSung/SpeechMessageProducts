# Dynamics ADFS / OAuth Zero-Leak 安全審查報告

審查範圍：`6301b4f29..2eee597cb`（本 worktree 目前 HEAD）。已直接讀取全部受影響原始碼、測試、設定與腳本檔案，並交叉核對 `.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md`。以下發現皆為讀取實際檔案後獨立驗證的結果（含對已產生的 Gemini 審查結果之覆核與修正）。

---

## Critical

**（本次獨立驗證後，未發現可確認的 Critical 等級發現。）**

Gemini 草稿將 `DiagnosticsController` 授權問題列為 Critical，但覆核後降級為 Warning（見下），原因：(1) 整個型別包在 `#if DEBUG`（`DiagnosticsController.cs:33`），Release 組態（`SpeechMessageProducts.ChurchReport.csproj:117` 只疊加 `RELEASE` 常數，未定義 `DEBUG`）不會編譯進此型別；(2) 本專案目前完全沒有 Role/Policy 基礎設施（`grep Roles=|Policy=|ClaimTypes.Role|AddPolicy` 在 ChurchReport 全專案零命中），Gemini 建議的 `[Authorize(Roles="Operator")]` 修法在目前架構下無法生效，屬於不成立的修復方案；(3) SPEC 檔案中出現「operator-only / fail-closed」的段落實際描述的是 Gateway 的 operation-level SID/name 授權器（`.trellis/spec/.../dynamics-gateway-hosting-version-routing.md:713-741`），並未對 ChurchReport 這支 DEBUG-only 診斷 controller 定義專屬的 Operator 角色需求。

---

## Warning

### W1. DiagnosticsController 對已登入一般會友開放真實 ADFS/CRM 呼叫（非 Operator 專屬）
- **檔案/行號**：`SpeechMessageProducts.ChurchReport/Controllers/DiagnosticsController.cs:39`（`[Authorize]`）、`83-119`（`AdfsAuthorize`）、`130-213`（`AdfsCallback`）
- **失效情境**：`[Authorize]` 只要求「任何已登入使用者」（LINE 登入或帳密登入的一般會友皆可），沒有角色/權限區分。已登入會友對 `/diagnostics/adfs-authorize?go=1` 發出請求，會用部署設定的真實 `AuthorityUri/ResourceUri/ClientId` 導向真正 ADFS 授權頁；`/diagnostics/adfs-callback` 會執行真正的 authorization-code 換 token 及 CRM `WhoAmI` 呼叫（`ExchangeAuthorizationCodeAsync`、`CallWhoAmIAsync`）。雖然 token 本身不回顯給呼叫者，但一般會友因此能以自己的登入身分觸發對外真實 ADFS/CRM 流量、探測部署是否正確設定，這超出「協助已登入本機開發者」的設計意圖（見 `DiagnosticsController.cs:34-37` 的自我聲明）。此風險僅限於 **DEBUG 建置且被部署到可被一般會友連線的環境** 時才會實際發生。
- **最小安全修復**：新增一個不依賴角色系統的最小 gate，例如比對設定檔中的允許帳號/SID 清單，或要求額外的 constant header/query token（部署時設定，不落地到程式碼），在 `Index`/`AdfsAuthorize`/`AdfsCallback`/`GetSessionInfo`/`GetPerformanceInfo` 進入點統一檢查並在失敗時回傳 403，不建立 Session state 或發出任何外部請求。
- **回歸測試（修復前應失敗）**：
  ```csharp
  [Fact]
  public async Task AdfsAuthorize_rejects_authenticated_non_operator_caller()
  {
      var controller = CreateController(new RecordingSession("s"), operatorAllowed: false);
      var result = await controller.AdfsAuthorize(go: "1");
      result.Should().BeOfType<ForbidResult>(); // 目前會回傳 Redirect
  }
  ```

### W2. `DiagnosticsController.CreateHttpClient()` 未沿用本次提交在別處建立的 IHttpClientFactory 慣例
- **檔案/行號**：`SpeechMessageProducts.ChurchReport/Controllers/DiagnosticsController.cs:372-385`
- **失效情境**：同一次提交已為 LINE OAuth（`Startup.cs` 新增 `"line-login-oauth"` 具名 client）與 ADFS token provider（`"dynamics-adfs-token"`）建立了 host-owned handler pool，但 `DiagnosticsController.CreateHttpClient()` 仍每次呼叫 `new HttpClient(new SocketsHttpHandler{...}, disposeHandler:true)`。雖有 `using` 確定釋放，且此端點是 DEBUG-only、非熱路徑（人工觸發），實際 Socket 耗盡風險低，但與本次提交建立的一致性原則（避免 per-request handler 建立）不符，且是唯一未走 `IHttpClientFactory` 的 ADFS/LINE HTTP 呼叫點。
- **最小安全修復**：改為建構式注入 `IHttpClientFactory`，新增 `"diagnostics-adfs"` 具名 client 並在 `Startup.cs` 綁定同樣的 `UseCookies=false/AllowAutoRedirect=false/UseProxy=false` handler。
- **回歸測試**：對 `DiagnosticsController` 建構式簽章做反射檢查，斷言存在 `IHttpClientFactory` 相依性；或在整合測試中連續呼叫 callback 多次，斷言底層 handler 由固定 pool 提供（非逐次新建）。

### W3. `AdfsOAuthTokenProvider` 生產路徑（owned-handler 分支）缺少 Dispose 測試覆蓋
- **檔案/行號**：`SpeechMessage.Dynamics.WebApi/Runtime/AdfsOAuthTokenProvider.cs:96-104`（`_ownedHttpClient` 建立）、`459`（`_ownedHttpClient?.Dispose()`）；production 呼叫點在 `DynamicsProfileRuntimeFactory.cs:96-99`（**未傳入** `IHttpClientFactory`）
- **失效情境**：`SpeechMessage.Dynamics.Tests/AdfsOAuthTokenProviderTests.cs` 內所有測試（`CreateProvider`/`CreateAsyncProvider` helper，以及個別內聯建構）都明確傳入 stub `IHttpClientFactory`，因此 `_httpClientFactory is null` 分支（也就是 `DynamicsProfileRuntimeFactory` 實際用來服務 Local/Central Gateway 多 Profile 的真實生產路徑）從未被任何測試執行到。若 `CreateOwnedHandler()`/`_ownedHttpClient` 的 Dispose 邏輯有 regressions（例如漏 Dispose、重複 Dispose 拋例外），現有測試套件不會發現。
- **最小安全修復**：新增測試直接以 `new AdfsOAuthTokenProvider(options, secretResolver, logger)`（不傳 factory）建構，執行一次 acquisition 後 Dispose，並用回應標頭 `Connection: close` 或 loopback server 計數驗證底層 socket 確實釋放。
- **回歸測試**：
  ```csharp
  [Fact]
  public async Task Owned_handler_path_disposes_http_client_after_generation_dispose()
  {
      await using var server = ScriptedLoopbackServer.Start(
          (HttpStatusCode.OK, """{"access_token":"t","expires_in":900}"""));
      var provider = new AdfsOAuthTokenProvider(
          Options.Create(CreateRefreshGrantOptionsWithSecret(server.BaseUri)),
          new DictionarySecretResolver(CreateRefreshGrantSecrets()),
          NullLogger<AdfsOAuthTokenProvider>.Instance); // no factory => owned handler
      _ = await provider.GetAccessTokenAsync();
      await provider.DisposeAsync();
      var act = async () => await provider.GetAccessTokenAsync();
      await act.Should().ThrowAsync<ObjectDisposedException>();
  }
  ```

### W4. LINE OAuth state 一次性消費僅有靜態原始碼順序測試，無真實 replay 行為測試
- **檔案/行號**：`SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.LineLoginOAuth.cs:146-224`（`LineCallback`）；對應測試 `ChurchReport.MemberInfo.Tests/Security/SensitiveDiagnosticOutputSecurityTests.cs:118-160`
- **失效情境**：`Line_callback_consumes_oauth_session_material_before_early_returns` 只用 `source.IndexOf(...)` 比對原始碼中字串出現的相對位置，並未真正呼叫 `LineCallback` 兩次驗證第二次是否真的被拒絕。相較之下，ADFS 診斷流程有真正的行為測試 `OAuth_state_is_consumed_only_once`（`ChurchReport.MemberInfo.Tests/Security/AdfsDiagnosticSecurityTests.cs:245-261`），對 LINE OAuth 缺少對應的行為測試；純文字順序測試無法在重構（例如把 `Session.Remove` 抽到 helper 方法、更動變數名稱）後繼續有效攔截 replay 缺陷。
- **最小安全修復**：不需改動生產程式碼；補上以 `TestServer`/`WebApplicationFactory` 或直接呼叫 `AuthenticationController.LineCallback` 兩次（同一 `code`/`state`）的整合測試。
- **回歸測試**：對同一個 `RecordingSession` 連續呼叫 `LineCallback(code, state, null, null)` 兩次，斷言第二次回傳結果導向 `Login` 且帶有 state 驗證失敗訊息，而非重新嘗試換 token。

---

## Info

1. **DI 生命週期模型分歧（非漏洞）**：`WebApiServiceCollectionExtensions.cs:137` 以 `TryAddSingleton<IAdfsOAuthTokenProvider, AdfsOAuthTokenProvider>()` 註冊，並經 `AddSpeechMessageDynamicsEmbedded`（`EmbeddedServiceCollectionExtensions.cs:118`）用於 ChurchReport 自身 Embedded 連線。這與 `AdfsOAuthTokenProvider.cs` class doc 宣稱的「one immutable profile-generation owner」模型不同——該模型只在 Gateway 多 Profile 路徑（`DynamicsProfileRuntimeFactory`）成立，並已由測試 `WebApiServiceCollectionExtensionsTests.cs:29-54`（`Multi_profile_registration_uses_manager_without_global_mutable_client_state`）證明多 Profile 註冊**不會**建立全域共用 Client/Transport/TokenProvider。因為 Embedded 模式本來就只服務單一固定 Profile，Singleton 在此不構成跨 Profile 洩漏，但建議在 class doc 中明確區分「Gateway 多 Profile：generation-owned」與「Embedded 單一 Profile：process-lifetime singleton」兩種合法生命週期，避免未來維護者誤用。

2. **Source-string 防呆測試屬性**：`AdfsDiagnosticSourceContractTests`（`AdfsDiagnosticSecurityTests.cs:29-95`）與 `SensitiveDiagnosticOutputSecurityTests.cs` 均以固定字串黑名單掃描原始碼。這類測試本質是 tautology（隨字串重構就會失真），但用途明確聲明為「保護 Release 建置排除的 DEBUG-only 型別」的次要防線，且都搭配了同檔案內的真實行為測試（loopback server、真實 controller 執行），屬合理搭配，非單獨依賴。

3. **確認修復（正向發現）**：`SmallGroupController.Crud.cs:60-66` 移除了 `Debug.WriteLine($"...Session ID: {HttpContext.Session.Id}")`，這是本次提交實際修掉的一個 Session ID 洩漏點（進 Debug output），而非本次引入的新問題。

4. **確認：無明文 token 落盤或隱藏回退**：`LocalDevAdfsTokenStore.cs`／`.cs.bak` 已完全刪除，且 `AdfsOAuthTokenProvider.ValidateSafeTokenSourceConfiguration/ResolveRequiredRefreshToken`（`AdfsOAuthTokenProvider.cs:266-296`）在任何 HTTP 或 secret 解析前 fail closed，無檔案／Session／環境預設回退路徑；`DonationDynamicsAccessBootstrap.cs` 移除了自動啟用 `AllowLocalDevPasswordGrant` 的 Tier-A 回退與自動產生 `Logs/adfs-local-token.json` 路徑的邏輯；已追蹤的 `SpeechMessageProducts.ChurchReport/Logs/adfs-token-probe-latest.json` 已刪除，`.gitignore` 亦覆蓋相關檔名樣式。僅存的字串命中皆為測試中的「不存在」斷言。

5. **確認：Rollout Gate 與相依套件保留**：`SpeechMessageProducts.ChurchReport/appsettings.json:559`、`appsettings.Development.json:6` 的 `Package01FeeReadsEnabled` 均維持 `false`（本次 diff 未觸碰此鍵，僅刪除 `LocalDevTokenStorePath` 並新增 Gateway 端 `RefreshTokenSecretName` reference-only 設定）；`PowerPlatform.Dataverse.Client.csproj`（含 Data8 版權/命名空間）與 `SpeechMessageProducts.ChurchReport.csproj` 對其 `PackageReference` 均維持存在。

---

## 明確結論

- **Session Leakage**：未發現。本次變更實際 *修補* 了一處 Session ID 進 Debug output 的洩漏（`SmallGroupController.Crud.cs`）；ADFS/LINE OAuth state 均在 callback 一開始 read-and-remove，成功/失敗/例外路徑一致。
- **Profile Leakage**：未發現。Gateway 多 Profile 路徑已用測試證明不共用 Client/Transport/TokenProvider；Embedded 單 Profile 的 DI Singleton 不構成跨 Profile 洩漏（見 Info-1）。
- **Memory/Resource Leakage**：未發現漏洞，但存在真實測試覆蓋缺口（見 Warning W3：owned-handler 分支從未被測試 Dispose）。
- **Credential Leakage**：未發現。明文 token store、追蹤診斷產物、密碼 fallback 均已移除；所有回應皆有界讀取並在 finally 清零；redirect／JSON 只含固定分類，不含 token、code、Session ID、client ID、endpoint 或例外訊息。
- **Operator-authorization Bypass**：**部分存在，但影響範圍受限**——`DiagnosticsController` 對「已登入」而非「操作員」開放，可觸發真實 ADFS/CRM 流量（見 Warning W1）；因整個型別為 `#if DEBUG` 且 Release 建置確認不含 `DEBUG` 常數，此風險僅在 DEBUG 建置被部署到可被一般會友存取的環境時才會真正發生。

---
SESSION_ID: <redacted>
