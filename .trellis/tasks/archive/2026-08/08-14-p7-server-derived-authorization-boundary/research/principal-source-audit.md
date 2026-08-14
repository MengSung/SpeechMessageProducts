# Research: principal-source-audit

- Query: 追蹤 ChurchReport 目前伺服器驗證 principal 的來源，以及 Session、InMemoryContext、保存 credential、browser/route locator、CRM `Entity`、`ListManager` 首次進入授權路徑的位置；判定是否有可供 P7 使用的 request-local immutable scope 根來源。
- Scope: internal（未執行 CE、網路或產品行為）
- Date: 2026-08-14

## Findings

### 結論（事實與建議分離）

**事實：**目前唯一可作為新 P7 邊界根的共同 principal 載體，是 Cookie 驗證完成後的 `HttpContext.User`。管線在 `UseSession` 後執行 `UseAuthentication`，全域 MVC filter 已被註冊（`SpeechMessageProducts.ChurchReport/Startup.cs:421-433,900-927`）；Cookie 方案與 30 分鐘失效設定在 `Startup.cs:638-669`。`LoginClaimsFactory` 將 `NameIdentifier`、`church:contactId`、帳號、`church:pwdkey`、登入型別放入 Cookie scheme 的 `ClaimsIdentity`（`Security/LoginClaimsFactory.cs:9-26`）。這是唯一不必讀取 legacy Session/記憶體快取即可在新 request 取得的 server-issued principal。

**限制事實：**它尚不是可直接傳遞的 immutable scope：`ClaimsPrincipal` 本身可變、claim 目前含有 LINE password key，且並非每個簽發者都提供有效 contact ID。`AppointmentController` 先把 browser 提供的 LINE ID 寫入 `InMemoryContext` 與 Session，隨後以 `contactId = null` 簽入 Cookie（`Controllers/AppointmentController.cs:135-194`）。因此「已驗證 cookie」或單一 claim 不足以代表 P7 已授權；空值、非 GUID、不一致或未知登入型別都必須拒絕。

**設計建議（非既有行為）：**以每個 request 的 Cookie `HttpContext.User` 為唯一輸入，僅投影並複核相等的 `ClaimTypes.NameIdentifier` 與 `church:contactId`（皆為非空 GUID）、Cookie scheme 與 allowlisted login type；投影不得帶入 account、`church:pwdkey`、`HttpContext`、`ClaimsPrincipal`、Session、`Entity`、`ListManager` 或集合。再由部署端固定設定推導產品/profile/generation，並於任何 locator parse、cache、manager、connector 或 CRM I/O 前產生固定拒絕或 immutable scope。此可支援 P7 prerequisite，但現有程式尚未完成這個 resolver，也沒有把 tenant/profile/generation 寫入可驗證的 principal。

### 伺服器 principal 的簽發與目前授權入口

1. 一般帳密登入的 browser model 先由 `ValidateUserCredentials` 對 CRM `contact` 以帳號及 active state 查詢，再比較 CRM 的 `new_app_pass`；連線於 `finally` 歸還（`Controllers/AuthenticationController/AuthenticationController.Private.cs:33-101`）。`ProcessLogin` 只有在驗證成功並重新取回 `Entity` 後才呼叫 session 初始化（`AuthenticationController.Login.cs:64-105`）。初始化使用 `loginContact.Id` 簽發 ticket（`AuthenticationController.Private.cs:256-260`），ticket 由 `IssueAuthTicketAsync` 組裝並 `SignInAsync`（`Controllers/BaseChurchController.cs:668-675`）。這是候選來源的已驗證正常路徑。
2. 已找到的其他 ticket 簽發點：SmallGroup LINE 路徑先以 LINE ID 取回 CRM contact，確認非 null 後才以 `contact.Id` 簽發（`Controllers/SmallGroupController/SmallGroupController.LineLogin.cs:38-64`）；OAuth 路徑也先以 LINE ID 找到 CRM contact，清除 Session 後回流 `ProcessLogin`（`Controllers/AuthenticationController/AuthenticationController.LineLoginOAuth.cs:740-784,789-832`）。相反地，Appointment 路徑如上可簽發空 contact claim，必須被新 resolver 拒絕。
3. 現行全域 filter 先接受 `HttpContext.User.Identity.IsAuthenticated`，但在預設設定下也接受 `_SessionUserId` **或** `_LoginPassword` 任一非空值（`Filters/GlobalAuthorizationFilter.cs:23-35,60-76`；註冊見 `Startup.cs:421-433`）。這個 fallback 是 legacy 存取門檻，不是可供 P7 取代 Cookie principal 的 server-derived scope。
4. Session validation middleware 在 authentication 前讀 `_SessionUserId`；其缺失會直接放行至下一個 middleware，存在時才比對/更新 User-Agent 與 IP（`Middleware/SessionValidationMiddleware.cs:106-187`）。故它是 session consistency guard，而不是建立 subject、scope 或 target authorization 的來源。

### legacy authority 資料首次進入點

| 類別 | 可觀察到的最早/關鍵進入點 | 對 P7 的判定 |
| --- | --- | --- |
| Session 與保存 credential | 成功登入把 `_SessionUserId`、識別碼、建立時間、UA、IP 寫入 Session（`AuthenticationController.Private.cs:208-225`），並把 browser model 的帳號及**明文 password** 寫入 `_LoginAccount`/`_LoginPassword`（`:234-250`）。`BaseChurchController.EnsureCorrectUserData` 讀 password，與 `ListManager.m_Password` 比較，並可用它重建 manager（`Controllers/BaseChurchController.cs:411-519`）。 | Session 和保存 password 是可變 legacy state；不得作為新 scope 或 Gateway authority。全域 filter 的 password fallback 是跨使用者隔離風險。 |
| CRM `Entity` | 一般登入先用最小欄位驗證，再以 `Retrieve(..., new ColumnSet(true))` 取得完整 contact（`AuthenticationController.Private.cs:109-169`）。初始化隨即把它保存至 `PersonalInfomationModel.m_LoginContact`（`:256-264`）。Fee audit 又從此 cache 取得 `loginContact` 做職務授權（`Controllers/DedicationAuditController.cs:376-403`；resolver 直接接受 `Entity`，`Services/Donation/DonationFeeAuditAccessResolver.cs:48-65`）。 | CRM `Entity` 是可變、可保留 attribute graph，僅能是舊流程短期資料；新 scope/result 不可保存它。 |
| `InMemoryContext` | Base controller 優先接 DI context，否則自行 new `InMemoryDataContextSmallGroup`（`Controllers/BaseChurchController.cs:128-161`）。實作每次透過 `IHttpContextAccessor` 讀 Session（`Models/InMemoryDataContextSmallGroup.cs:144,190`），但 `ListManager` 仍放入 process `IMemoryCache` 30 分鐘（`:574-612`）。 | DI `Scoped` 註冊（`Startup.cs:708-709`）不改變其 session-keyed cache 內部持有 mutable manager 的事實；它不可作為 request-local scope 的根。 |
| `ListManager` 與 credential | `ListManager.SetupListManager` 將 account/password 寫入 public fields，再載入資料（`Models/ListManager.cs:25-86`）；登入後的 `SetupSystemData` 以 model account/password 建立它（`AuthenticationController.Private.cs:274-317`）。後續 action 也從 `ListManager.m_Account/m_Password` 重載資料，例如日期更新（`Controllers/SmallGroupController/SmallGroupController.Date.cs:68-90`）。 | manager 既持有 credential 又有可變資料/目前清單；不可作為 principal、scope 或 profile selector。 |
| browser/route locator → shared state | `SaveUserId` 的 POST parameter `UserLineId/GroupId/RoomId/ViewType` 在任何 CRM 檢查前被寫到 `InMemoryContext.LineBindingViewModel`，才查 CRM（`Controllers/AuthenticationController/AuthenticationController.SaveUserId.cs:28-65`）。`DedicationController.SetupUserLineId` 同樣先寫 shared view model，之後以 caller `UserLineId` 做 CRM lookup（`Controllers/DedicationController.cs:679-711`）。Appointment 亦先寫兩個 manager，接著把 caller LINE ID 變成 Session/cookie state（`Controllers/AppointmentController.cs:135-194`）。 | 這些 locator 是 caller-controlled，不能作為 subject、owner、profile、connector 或 scope；現有順序不符合 P7 的 no-I/O-before-authorization 前提。 |
| browser target locator 在既有「授權」內 | `GetFeesByContactId(string id)` 先呼叫會讀 Session/`ListManager` 的 `EnsureCorrectUserData`，再從 cached CRM Entity 授權，最後才 parse browser `id` 並讀取 target（`Controllers/DedicationAuditController.cs:372-414`）。 | 它雖把 GUID parse 放在職務檢查後，仍以 legacy cache Entity 作 principal，且未從 immutable scope 證明 target contact 的可見性；不可直接重用為 P7 boundary。 |

### 隔離與資源生命週期含意

- `InMemoryDataContextSmallGroup` 的 key 以 Session ID、可選 `_SessionRegeneratedFor`、IP/UA fingerprint 與 timestamp 組合（`Models/InMemoryDataContextSmallGroup.cs:213-392`）；但 `_SessionRegeneratedFor` 在所查 ChurchReport 程式中僅被讀取，未找到寫入點。快取 key 不是授權決策，不能代替完整 subject/profile/generation boundary。
- `ListManager` 及多個資料物件是 30 分鐘 absolute + sliding `IMemoryCache` 條目（`Models/InMemoryDataContextSmallGroup.cs:574-612`）。登出/重登入對 Donation manager 有顯式 drain 後才 `Session.Clear`（`AuthenticationController.Session.cs:87-117`），且 logout 接著 sign-out 和刪 cookie（`:36-66`）；這是特定 resource 的正確 owner pattern，卻不使其他 cached user graph 可供新 scope 保存。
- 新 resolver 必須只活在 request 中，不訂閱、不快取、不捕捉 `HttpContext`；取消/錯誤時不建立或借用 connector。目標授權結果僅能發布 immutable scalar/read-only projection，避免 A/B 交錯時把 Session、CRM Entity、credential 或 `ListManager` 留給下一個 request。

## Files Found

- `SpeechMessageProducts.ChurchReport/Startup.cs` — Session、Cookie、middleware 順序、DI 與全域 authorization filter 註冊。
- `SpeechMessageProducts.ChurchReport/Security/LoginClaimsFactory.cs` — Cookie ticket claim 的唯一集中工廠。
- `SpeechMessageProducts.ChurchReport/Filters/GlobalAuthorizationFilter.cs` — cookie 與 Session/password fallback 的實際門檻。
- `SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.Private.cs` — CRM 帳密驗證、Session/Entity/cache 初始化與 ticket 簽發。
- `SpeechMessageProducts.ChurchReport/Controllers/BaseChurchController.cs` — ticket 寫入、Session/password 與 `ListManager` 對齊邏輯。
- `SpeechMessageProducts.ChurchReport/Models/InMemoryDataContextSmallGroup.cs` — session-keyed `IMemoryCache` context/manager 生命週期。
- `SpeechMessageProducts.ChurchReport/Models/ListManager.cs` — 保存 credential 與可變清單/授權相關狀態。
- `SpeechMessageProducts.ChurchReport/Controllers/AppointmentController.cs` — caller LINE locator 可直接形成空 contact cookie 的不合格 issuer。
- `SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.SaveUserId.cs`、`Controllers/DedicationController.cs` — browser locator 寫入 shared context 後才查 CRM。
- `SpeechMessageProducts.ChurchReport/Controllers/DedicationAuditController.cs`、`Services/Donation/DonationFeeAuditAccessResolver.cs` — legacy cached `Entity` principal 與 target GUID audit 路徑。

## External References

- 本專案 target 為 `.NET 10`：`SpeechMessageProducts.ChurchReport/SpeechMessageProducts.ChurchReport.csproj:1-4`。
- Microsoft Learn, *Cookie authentication in ASP.NET Core*（`view=aspnetcore-10.0`）：用於後續實作時確認 Cookie handler 與 `HttpContext.User` 的標準語意；本次依指令未進行網路存取或外部驗證。

## Related Specs

- `.trellis/spec/backend/cross-user-isolation-and-performance.md` — scope 必須 server-derived、request-local、immutable，並在 cache/I/O 前 fail closed。
- `.trellis/spec/guides/cross-user-isolation-and-performance-review.md` — A/B isolation、資源 owner 與 cleanup 審查清單。
- `.trellis/tasks/08-14-p7-server-derived-authorization-boundary/prd.md:20-25,40-42` — 指定尋找唯一 server-owned source、排除 legacy authority，並要求 no-I/O-before-authorization。
- `.trellis/tasks/08-14-p7-server-derived-authorization-boundary/design.md:7-20` — immutable scope 不得持有 principal、Session、Entity、ListManager、credential 或 collection。

## Caveats / Not Found

- 未找到現成的 `ValidatedRequestScope`、request-local principal resolver、或將完整 profile/generation/tenant isolation boundary 放進 ChurchReport Cookie 的實作；因此候選是「可安全投影的輸入」，不是已完成的 P7 scope。
- 未找到 `_SessionRegeneratedFor` 的寫入點；現有 cache key 的這個預期 partition 目前不能視為有效保證。
- `IAuthenticationService` 與 `AuthResult` 雖宣告存在，但本次搜尋未找到實作或 DI 使用；且 `AuthResult` 仍攜帶 CRM `Entity`（`Models/Authentication/AuthResult.cs:21-72`），不適合作為 immutable boundary。
- 上述 locator 例子涵蓋 P7 相關且已確認會在 shared state/CRM I/O 前使用 caller 值的入口；本報告不宣稱所有 ChurchReport action 已完成 target-level authorization 稽核。
