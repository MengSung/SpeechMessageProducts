# Dynamics ADFS / OAuth Zero-Leak 安全審查報告（獨立覆核版）

審查範圍：`6301b4f29..2eee597cb`。已直接讀取受影響原始碼（`DiagnosticsController.cs`、`AuthenticationController.LineLoginOAuth.cs`、`AdfsOAuthTokenProvider.cs`、`DynamicsProfileRuntimeFactory.cs`、`WebApiServiceCollectionExtensions.cs`、`EmbeddedServiceCollectionExtensions.cs`、csproj/appsettings）、比對 `.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md`，並交叉核對本目錄下先前 Claude/Gemini 兩份草稿（`.ccg/dual-model-runs/20260730-130724-.../claude-reviewer-attempt-1.stdout.md`、`gemini-reviewer-attempt-2.stdout.md`）。以下為獨立驗證後的結論，對兩份草稿有分歧處已標明並給出實測依據。

---

## Critical

**未發現可確認的 Critical。**

Gemini 草稿將 `DiagnosticsController` 的 `[Authorize]`（`DiagnosticsController.cs:39`）列為 Critical 並建議改成 `[Authorize(Roles="Operator")]`。獨立驗證後降級為 Warning（見 W1），理由：
1. 整個型別包在 `#if DEBUG`（`DiagnosticsController.cs:33`）；`SpeechMessageProducts.ChurchReport.csproj:116-118` 顯示 Release 組態只疊加 `RELEASE` 常數（`<DefineConstants>$(DefineConstants);RELEASE</DefineConstants>`），未移除/覆寫 SDK 預設的 Debug‑only `DEBUG` 常數，因此 Release 建置不會編譯進此 controller。
2. 專案內全域搜尋 `Roles=|Policy=|ClaimTypes.Role|AddPolicy` 零命中，Gemini 建議的 `Roles="Operator"` 在目前 DI/驗證架構下無角色系統可用，屬不成立的修復方案。

Gemini 草稿另列 Warning「`InMemoryContext.LineBindingViewModel` 靜態狀態導致 Profile Leakage」（引用 `AuthenticationController.LineLoginOAuth.cs` 197-199 行等）。逐行比對 `git diff 6301b4f29..2eee597cb` 後確認：`InMemoryContext.LineBindingViewModel.LineUserId = userProfile.userId;` 一行在本次 diff 中是**未變更的 context 行**（無 `+`/`-`），且 `git log -S` 顯示此賦值早於本次 commit range（可回溯至更早的 rename commit）。`InMemoryContext` 是橫跨 85 個既有檔案的全域靜態容器（`ListManager`/`FeeList`/`HappyGroupDataManager` 等），屬整個 ChurchReport 既有架構的長期反模式，本次變更**未觸碰、未擴大**此洩漏面，不符合任務要求的「this change creates or worsens」門檻，故不計入本次 Critical/Warning，改列 Info（見 Info-1）。

---

## Warning

### W1. DiagnosticsController 對已登入一般會友開放真實 ADFS/CRM 呼叫（非 Operator 專屬）
- **檔案/行號**：`DiagnosticsController.cs:39`（`[Authorize]`）、`83-119`（`AdfsAuthorize`）、`121-` 起（`AdfsCallback`）
- **失效情境**：`[Authorize]` 只要求任一已登入使用者（含 LINE 登入的一般會友）。已登入會友對 `/diagnostics/adfs-authorize?go=1` 發請求，會用部署設定的真實 `AuthorityUri/ResourceUri/ClientId` 導向真正 ADFS 授權頁；`adfs-callback` 會執行真正 authorization-code 換 token 及 CRM WhoAmI 呼叫。Token 本身不回顯，但一般會友能以自身登入身分觸發對外真實 ADFS/CRM 流量、探測部署設定，超出型別自我聲明的「協助已登入本機開發者」設計意圖（`DiagnosticsController.cs:34-37`）。**此風險僅在 DEBUG 建置被部署到一般會友可連線的環境時才實際發生**——這是本次獨立驗證與 Gemini 草稿的核心分歧點：SPEC 中 operator-only/fail-closed 段落（`.trellis/spec/.../dynamics-gateway-hosting-version-routing.md:713-741`）描述的是 Gateway 的 operation-level SID/name 授權器，並未對這支 DEBUG-only 診斷 controller 定義專屬 Operator 角色需求，故列 Warning 而非 Critical。
- **最小安全修復**：新增不依賴角色系統的最小 gate（比對設定檔允許帳號/SID 清單，或部署時設定的 constant header/query token，不落地到程式碼），在 `Index`/`AdfsAuthorize`/`AdfsCallback`/`GetSessionInfo`/`GetPerformanceInfo` 統一檢查，失敗回傳 403，不建立 Session state 或發出外部請求。
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

### W2. `DiagnosticsController.CreateHttpClient()` 未沿用本次提交建立的 `IHttpClientFactory` 慣例
- **檔案/行號**：`DiagnosticsController.cs:372-385`
- **失效情境**：同一次提交已為 LINE OAuth（`"line-login-oauth"`）與 ADFS token provider（`"dynamics-adfs-token"`）建立 host-owned named handler pool，唯獨此處仍每次 `new HttpClient(new SocketsHttpHandler{...}, disposeHandler:true)`。雖有 `using` 保證釋放，且此端點是 DEBUG-only、非熱路徑，Socket 耗盡風險低，但與本次一致性原則不符。
- **最小安全修復**：改為建構式注入 `IHttpClientFactory`，新增 `"diagnostics-adfs"` 具名 client，於 `Startup.cs` 綁定相同 `UseCookies=false/AllowAutoRedirect=false/UseProxy=false` handler。
- **回歸測試**：對 `DiagnosticsController` 建構式做反射檢查，斷言存在 `IHttpClientFactory` 相依；或整合測試連續呼叫 callback 多次，斷言底層 handler 來自固定 pool 而非逐次新建。

### W3. `AdfsOAuthTokenProvider` 生產路徑（owned-handler 分支）缺少 Dispose 測試覆蓋
- **檔案/行號**：`AdfsOAuthTokenProvider.cs:96-104`（`_ownedHttpClient` 建立）、`AdfsOAuthTokenProvider.cs`（`_ownedHttpClient?.Dispose()`）；生產呼叫點在 `DynamicsProfileRuntimeFactory.cs`（明確**不傳** `IHttpClientFactory` 給 `AdfsOAuthTokenProvider`，並有明文註解「避免不同 Generation 共用 named handler pool」）
- **失效情境**：此不傳 factory 是**刻意設計**（每個 Profile Generation 獨立擁有 handler，避免跨 Generation 共用連線池），意味著 owned-handler 分支正是 Gateway 多 Profile 的**真實生產路徑**，而非備援分支。然而 `AdfsOAuthTokenProviderTests.cs` 中所有測試 helper 都明確傳入 stub `IHttpClientFactory`，該生產路徑從未被任何測試執行到。若 `CreateOwnedHandler()`/`_ownedHttpClient` 的 Dispose 邏輯有 regression（漏 Dispose、重複 Dispose 拋例外），現有套件不會發現。
- **最小安全修復**：新增測試直接以不傳 factory 的建構子建構 provider，執行一次 acquisition 後 Dispose，並以 loopback server 連線計數驗證底層 socket 確實釋放。
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
          NullLogger<AdfsOAuthTokenProvider>.Instance); // no factory => owned handler，即生產路徑
      _ = await provider.GetAccessTokenAsync();
      await provider.DisposeAsync();
      var act = async () => await provider.GetAccessTokenAsync();
      await act.Should().ThrowAsync<ObjectDisposedException>();
  }
  ```

### W4. LINE OAuth state 一次性消費僅有靜態原始碼順序測試，缺真實 replay 行為測試
- **檔案/行號**：`AuthenticationController.LineLoginOAuth.cs:146-224`（`LineCallback`）；對應測試 `ChurchReport.MemberInfo.Tests/Security/SensitiveDiagnosticOutputSecurityTests.cs:118-160`
- **失效情境**：既有測試只用字串在原始碼中出現的相對位置比對，並未真正呼叫 `LineCallback` 兩次驗證第二次是否被拒。相較之下 ADFS 診斷流程有真行為測試（`AdfsDiagnosticSecurityTests.cs` 中 `OAuth_state_is_consumed_only_once`）。純文字順序測試無法在重構後繼續攔截 replay 缺陷。
- **最小安全修復**：不需改動生產程式碼，補上呼叫 `LineCallback` 兩次（同一 `code`/`state`）的整合測試。
- **回歸測試**：對同一 `RecordingSession` 連續呼叫 `LineCallback(code, state, null, null)` 兩次，斷言第二次導向 `Login` 並帶 state 驗證失敗訊息，而非重新換 token。

---

## Info

1. **`InMemoryContext` 全域靜態狀態是既有架構問題，非本次引入/惡化**：`InMemoryContext.LineBindingViewModel.LineUserId/DisplayId` 賦值（`AuthenticationController.LineLoginOAuth.cs`）在本次 diff 中是未變更 context 行，且早於本次 commit range 即存在，橫跨 85 個檔案的既有全域容器模式超出本次 zero-leak 變更範圍。建議另立獨立任務追蹤（不阻塞本次審查）。
2. **DI 生命週期模型分歧（非漏洞）**：`WebApiServiceCollectionExtensions.cs` 以 `TryAddSingleton<IAdfsOAuthTokenProvider, AdfsOAuthTokenProvider>()` 註冊供 ChurchReport Embedded 連線使用，這與 class doc 宣稱的「one immutable profile-generation owner」模型不同——該模型只在 Gateway 多 Profile 路徑（`DynamicsProfileRuntimeFactory`，已用 `Multi_profile_registration_uses_manager_without_global_mutable_client_state` 測試證明不共用）成立。Embedded 模式只服務單一固定 Profile，Singleton 不構成跨 Profile 洩漏，但建議在 class doc 中明確區分兩種合法生命週期。
3. **確認修復（正向發現）**：`SmallGroupController.Crud.cs` 移除了 `Debug.WriteLine($"...Session ID: {HttpContext.Session.Id}")`，是本次提交實際修掉的 Session ID 洩漏點。
4. **確認：無明文 token 落盤或隱藏回退**：`LocalDevAdfsTokenStore.cs`／`.cs.bak` 已完全刪除；`AllowLocalDevPasswordGrant` 在所有 appsettings（含 `bin/` 輸出）與 `EmbeddedServiceCollectionExtensions.cs:134` 預設值均為 `false`；`AdfsOAuthTokenProvider.cs:268` 對此旗標的檢查在任何 HTTP/secret 解析前 fail closed；`DonationDynamicsAccessBootstrap.cs:228` 註解明確聲明此旗標僅為「舊設定的 fail-closed migration trap」，不自動開啟；已追蹤的 `Logs/adfs-token-probe-latest.json` 已刪除。僅存字串命中皆為測試中的「不存在」斷言。
5. **確認：Rollout Gate 與相依套件保留**：`appsettings.json`／`appsettings.Development.json` 的 `Package01FeeReadsEnabled` 均維持 `false`；`PowerPlatform.Dataverse.Client`（含 Data8）與 Embedded 專案的 `PackageReference` 均維持存在，本次 diff 未觸碰此鍵。
6. **State 固定時間比較與一次性消費確認**：ADFS（`DiagnosticsController.cs:149,475-481`）與 LINE（`AuthenticationController.LineLoginOAuth.cs:172,394-401`）兩處皆使用 `CryptographicOperations.FixedTimeEquals`，且皆在 callback 一開始 read-and-remove Session 中的 state/issued-at，成功/失敗/例外路徑一致。

---

## 明確結論

- **Session Leakage**：未發現。本次變更修補了一處 Session ID 進 Debug output 的既有洩漏；ADFS/LINE OAuth state 均在 callback 一開始 read-and-remove。
- **Profile Leakage**：Gateway 多 Profile 路徑未發現（有測試佐證不共用 Client/Transport/TokenProvider）；`InMemoryContext` 既有的跨使用者靜態狀態問題**存在但屬本次變更範圍外**（Info-1），非本次 diff 創造或惡化。
- **Memory/Resource Leakage**：未發現實際漏洞，但存在測試覆蓋缺口——production 實際使用的 owned-handler Dispose 路徑（W3）從未被測試執行到，且該路徑是刻意設計的生產路徑而非備援分支，建議優先補測。
- **Credential Leakage**：未發現。明文 token store、追蹤診斷產物、密碼 fallback 均已移除且 fail-closed；所有回應皆有界讀取；redirect/JSON 只含固定分類，不含 token、code、Session ID、client ID、endpoint 或例外訊息。
- **Operator-authorization Bypass**：**部分存在，但影響範圍受限（Warning，非 Critical）**——`DiagnosticsController` 對「已登入」而非「操作員」開放，可觸發真實 ADFS/CRM 流量（W1）；因整個型別為 `#if DEBUG` 且已用 csproj 驗證 Release 組態不含 `DEBUG` 常數，此風險僅在 DEBUG 建置被部署到可被一般會友存取的環境時才會真正發生。

---
SESSION_ID: <redacted>

---
SESSION_ID: <redacted>
