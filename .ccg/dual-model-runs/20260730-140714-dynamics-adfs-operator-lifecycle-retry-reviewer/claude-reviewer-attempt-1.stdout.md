## 審查結論(dynamics-adfs-operator-lifecycle-retry-reviewer)

**方法**：完整讀取 diff 涉及的 6 個原始碼檔案(`DiagnosticsController.cs`、`Startup.cs`、`appsettings.json`、新增的 `DiagnosticsOperatorAuthorization.cs`、`AdfsOAuthTokenProvider.cs`)及 3 個測試檔案的完整內容(非僅字串比對)，並用 `dotnet build` + `dotnet test` 實際編譯執行了 `SpeechMessage.Dynamics.Tests`(19 項全過)與 `ChurchReport.MemberInfo.Tests` 中的相關測試(25 項全過)，驗證測試斷言與 production 行為一致。

### Critical 🔴
無。

### Warning 🟡
無。

### Info 🟢
- **ChurchReport.MemberInfo.Tests/Security/AdfsDiagnosticSecurityTests.cs:71** — `Diagnostics_operator_authorization_uses_server_issued_contact_claim_and_fails_closed` 涵蓋缺少 claim、重複 claim、非 GUID、未驗證身分、清單外識別、空 allowlist 六種 fail-closed 案例,但缺少「`NameIdentifier` claim 值為空字串」這個需求文字明確列出的案例。程式碼本身透過 `NormalizeContactId` 對空字串 `Guid.TryParse` 失敗而正確 fail closed(已用 dotnet test 驗證整體測試綠燈),此為測試矩陣的補強建議,非缺陷。建議之後補一個 `new Claim(ClaimTypes.NameIdentifier, "")` 的顯式案例。

### 逐項驗證結果

1. **操作員授權邊界**：`DiagnosticsController` 現在用 `[Authorize(Policy = DiagnosticsOperatorAuthorization.PolicyName)]`,Policy 只信任 Cookie scheme 內由 `LoginClaimsFactory.cs:18` 簽發的唯一 `NameIdentifier` claim,比對 Host 啟動時建立一次的不可變 `FrozenSet` allowlist(`appsettings.json` 新增 `Diagnostics:OperatorContactIds`,預設空陣列)。缺少/空白/非法/重複/未驗證/清單外皆在 Session、ADFS、CRM 之前 fail closed(`DiagnosticsOperatorAuthorization.cs:53-81`)。**未發現授權繞過**。
2. **無自造角色、無跨請求可變狀態**：`IsAuthorized` 只讀 `ClaimsPrincipal`,不讀 Session/query/header/產品 JSON;allowlist 是 Startup 閉包捕捉的一次性 `FrozenSet`,無 static cache/timer。
3. **HTTP client 邊界**：`Startup.cs:196-213` 新增具名 `adfs-diagnostics` client,`UseCookies=false`、`AllowAutoRedirect=false`、`UseProxy=false`、`AutomaticDecompression=None`、`PreAuthenticate=false`,`Timeout=30s`、`MaxConnectionsPerServer=4`、`PooledConnectionLifetime=5min`、`PooledConnectionIdleTimeout=2min`、`SetHandlerLifetime=10min`,全部有界。Controller 內 `using var http/request/response`、`ArrayPool` + `CryptographicOperations.ZeroMemory` 皆確定性釋放/清零。
4. **`AdfsOAuthTokenProviderTests` 覆蓋 production-owned handler 分支**：新增 `Owned_handler_client_is_disposed_with_profile_generation`(`AdfsOAuthTokenProviderTests.cs:279-296`)以反射取得 `_ownedHttpClient`,呼叫 `DisposeAsync()` 後驗證 `SendAsync` 立即拋出 `ObjectDisposedException`(HttpClient 在任何網路 I/O 前同步檢查 disposed 狀態),證明 generation dispose 會在後續網路工作前關閉底層 handler/socket。實測通過。
5. **LINE callback replay 測試**：`SensitiveDiagnosticOutputSecurityTests.cs:184-208` 用同一 `ReplaySession` 對真正的 `AuthenticationController.LineCallback` 連續呼叫兩次同 code/state。核對 production 程式碼(`AuthenticationController.LineLoginOAuth.cs:154-160`)確認 callback 一開始就 read-and-remove `_LineLoginState/_LineLoginStateIssuedAtUtcTicks/_LineLoginCallbackUrl/_LineLoginNonce`,第二次因這些值已被移除而落入 State 驗證失敗分支,測試斷言與行為一致,實測通過。
6. **零外洩**:通篇未發現 token、code、Session ID、LINE user ID、client ID、callback URI、authority/resource endpoint 被寫入 log、response、`.ccg/dual-model-runs` 產出檔或 test 輸出;`appsettings.json` 新增區塊本身只放空陣列,不含任何憑證。
7. **資源生命週期**:Controller/Provider 對 handler、client、request、response、stream、pooled buffer、Session byte[]、CancellationTokenSource 均有單一 owner 與確定性 Dispose 路徑(`using`/`finally`/`DisposeAsync`)。
8. **保留項目**:`Package01FeeReadsEnabled` 仍為 `false`(`appsettings.json:565`);`Data8`(`PowerPlatform.Dataverse.Client.csproj` 內 Data8 Ltd 著作權/namespace)與 `Microsoft.PowerPlatform.Dataverse.Client`(`SpeechMessageProducts.ChurchReport.csproj:63`)均未被移除。

**最終判定**：Operator-authorization bypass — 無;Session Leakage — 無;Profile Leakage — 無;Credential Leakage — 無;Memory/Resource Leakage — 無。此 diff 可視為安全,僅有一項測試矩陣補強建議(非阻斷項)。

---
SESSION_ID: <redacted>
