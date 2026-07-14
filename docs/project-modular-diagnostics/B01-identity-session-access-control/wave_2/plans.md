# Wave 2 實施合約：B01-SEC-003

CONTRACT_STATUS: WAVE_PLAN_APPROVED

審查證據：Claude-only review 無可用輸出；依 wave workflow 執行的一次唯讀 Codex fallback 複審已明確核准，且無未解決 Critical 或 Warning。此核准只確認合約品質；本文列出的 CRM row-version、non-production route probe 與 ToolUtility caller inventory 仍是未滿足前不得部署的 repair/deployment gates。

## 範圍與不變量

- Wave：`wave_2`
- 工作區：`B01-identity-session-access-control`
- 唯一 canonical issue：`B01-SEC-003`
- 目標：帳號登入不再直接比較 CRM `new_app_pass` 與提交值；改為 strict adaptive-hash verification，並且只在可測試的 fake CRM credential seam 與 CRM concurrency 前置條件皆成立時執行一次性 legacy migration。
- 路由相容性：保留 `POST /Authentication/ProcessLogin`、現有 request model、成功與失敗 JSON 欄位（`DisplayViewType`、`ActiveListId`、`message`、`fullname`）、cookie 簽發時機及現有登入後資料初始化結果。不得改 route、redirect、HTTP method 或 response schema。
- 資料保護：帳號提交密碼的可存活範圍只到 verifier 完成。之後不得出現在 session、claims/auth ticket、response/view-model、logging/diagnostics/exception message、InMemory/cache persistence、CRM update payload 或測試 evidence。
- 身分界線：既有 auth cookie 的 authenticated principal 仍是登入身分；本波新增的 compatibility key 僅供既有資料 loader 由已驗證 contact 找回資料，絕不是授權憑證，也不改變 global authorization 或 session fallback 行為。

## 明確排除

- Issue：`B01-SEC-001`、`B01-SEC-002`、`B01-SEC-004`、`B01-PERF-001`，以及所有未列出的 identity、route、LINE、OAuth、全域授權、session fallback、session-id rotation、效能、CRM schema 或 runtime configuration 工作。
- 不得修改：`Filters/GlobalAuthorizationFilter.cs`、`Controllers/BaseChurchController.cs`、`Controllers/FeeManagementController.cs`、`Models/ListManager.cs`、`Models/FeeList.cs`、`Models/AppointmentsListManager.cs`、任何 LINE/OAuth controller、任何 `appsettings*.json`、CRM schema/migration/configuration、前端 route/view/script，以及 issue/evidence/blueprint/inventory/workflow 文件。
- 現有 `_LoginPassword`、`m_Password` 等名稱不在本波改名範圍。ACCOUNT 路徑可在這些既有相容性欄位放入 server-issued compatibility key，但絕不可放提交密碼；LINE working key 與其行為不可為了本 issue 改動。

## 未來修復精確 allowlist

下列為修復子代理唯一可建立或修改的產品與測試路徑。未列路徑一律禁止；`Startup.cs` 只可新增本合約服務的 DI registration，不得調整 authentication/session/security 設定。

| 類型 | 路徑 | 有界用途 |
| --- | --- | --- |
| 產品 | `SpeechMessageProducts.ChurchReport/Security/AccountCredentialVerification.cs` | 新增 versioned credential envelope parser、驗證結果、`IAccountCredentialStore`、PBKDF2 verifier 與 account post-login context；不得含 CRM I/O、logging 或 raw-value response。 |
| 產品 | `SpeechMessageProducts.ChurchReport/Services/Authentication/CrmAccountCredentialStore.cs` | 讀取 active contact 的 id、`new_app_pass` 與 row version；只以 row-version conditional update 寫入新 envelope，回傳一般化結果碼。 |
| 產品 | `SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.Core.cs` | 注入 verifier/store，保存 private readonly 欄位；不改 action/route 宣告。 |
| 產品 | `SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.Private.cs` | 將帳號分支改用 verifier/store；驗證後建立 compatibility context，讓 `InitializeUserSessionAsync`、`SetupSystemData`、List/Fee/Appointment 初始化只接收 key 而非 submitted password；保留 LINE 分支。 |
| 產品 | `SpeechMessageProducts.ChurchReport/Startup.cs` | 註冊 verifier/store 為 scoped service；不新增 password 設定或祕密。 |
| 產品 | `ToolUtility/ContactOperations/AccountLoginCompatibilityKey.cs` | 新增嚴格 parser/creator，定義唯一保留前綴及 verified contact-id key；它不是 credential、不可接受使用者輸入。 |
| 產品 | `ToolUtility/ContactOperations/ContactService.cs` | 僅取代 `RetrieveByAccountNumber` 的 `new_app_pass` 直接比較：只接受嚴格 compatibility key，並以 active `contactid + new_app_acount` 查詢 contact；非 key 一律 fail closed。 |
| 測試 | `ChurchReport.MemberInfo.Tests/Security/AccountCredentialVerificationTests.cs` | verifier、格式分類、legacy migration fake-store 與不洩露測試。 |
| 測試 | `ChurchReport.MemberInfo.Tests/Security/AuthenticationCredentialLoginContractTests.cs` | `ProcessLogin -> InitializeUserSessionAsync -> SetupSystemData` 的 account post-login compatibility contract；fake HTTP/session/CRM 只記錄 case id、key class 與呼叫計數。 |
| 測試 | `ChurchReport.MemberInfo.Tests/Security/LoginClaimsFactoryTests.cs` | 補強 ACCOUNT principal 不含 submitted credential，保留 LINE working-key coverage。 |
| 測試 | `ChurchReport.MemberInfo.Tests/Security/LoginResponseFactoryTests.cs` | 補強 JSON response 與 view-model contract 不回傳 credential。 |
| 測試 | `ToolUtility.Tests/ContactOperations/ContactServiceTests.cs` | 證明 valid key 查得正確 active account contact；raw/non-key、格式錯誤 key、帳號不符與 inactive contact 均 fail closed。 |
| 測試 | `ToolUtility.Tests/Core/ToolUtilityFacadeIntegrationTests.cs` | 證明既有 `ToolUtilityClass/ToolUtilityFacade` delegation 可把 compatibility key 送入更新後的 `ContactService`，不需修改各個 List/Fee/Appointment connector。 |

### 已驗證的相容性理由

目前 `ProcessLogin` 成功後呼叫 `SetupSystemData`；該方法把 `viewModel.Password` 傳入 `ListManager.SetupListManager` 與 `FeeList.SetupLessonList`，而前者再流向多個資料 loader。這些 loader 最終透過既有 `ToolUtilityClass -> ToolUtilityFacade -> ContactService.RetrieveByAccountNumber` 取得 account contact。`ContactService.cs:210` 目前仍直接比較 `new_app_pass`。

因此修復不應只清空 session，也不應逐一修改 List/Fee/Appointment/OAuth 模組。最小可測的處置是：登入 verifier 成功後以 verified `contactid` 建立嚴格 `B01` compatibility key；原來的 account 保留為 account，原來向下傳遞 password 的位置改傳此 key。中央 `ContactService` 只接受此 key，並以 `contactid + new_app_acount + statecode=0` 找回 contact。如此既有 loader 仍得到同一 contact，卻不再依賴 submitted password。未驗證或非 B01 key 不可被當成 password 使用。

## Credential envelope 與分類

目前版本唯一可接受的 envelope 為：

```text
B01PH$v1$pbkdf2-sha256$600000$<base64url-16-byte-salt>$<base64url-32-byte-subkey>
```

- PBKDF2：HMAC-SHA256；輸出 32 bytes；salt 必須是 CSPRNG 產生的 16 bytes。
- 新產生 hash 的 iteration 必須剛好 `600000`。解析接受範圍為 `600000` 至 `1000000`（含），讓同一 `v1` format 可提高 work factor；低於、超過、非十進位、前導/尾隨空白、額外 segment、非 canonical base64url、salt/subkey 長度不符均拒絕。
- 比較須使用 `CryptographicOperations.FixedTimeEquals`；不可使用 CLR 字串相等比較。
- 分類互斥：
  1. 僅有完全符合上述 strict `B01PH$v1` envelope 的值是 current hash，走 PBKDF2 verifier。
  2. 任一以保留 magic `B01PH` 開頭但格式不完整、未知 version/algorithm、iteration 越界或 encoding 不正確的值，一律 fail closed；不可 legacy fallback、不可 migration write。
  3. 只有不以 `B01PH` 開頭的值才是 legacy candidate；可在記憶體中以 fixed-time legacy verify 嘗試一次，且不記錄、不回傳、不持久化 submitted/stored 值。

## 最小安全修復步驟

1. 先建立 fake `IAccountCredentialStore` 與 strict parser 測試骨架。fake 可回傳 contact id、row version、credential class、update outcome；不得連真 CRM 或輸出 credential material。若此 seam 不能隔離，停止修復。
2. 實作 PBKDF2 envelope/parser 與三分法分類。空白、malformed、unknown version、work-factor 越界均為一般登入失敗，且 update 次數為零。
3. `CrmAccountCredentialStore` 只讀取必要欄位。legacy migration 只能以 CRM `RowVersion` 的 conditional update 寫入完整新 envelope；禁止先清空、覆寫成 null 或無條件 update。
4. legacy 驗證成功後 migration 是 **best-effort、非登入前置條件**：
   - conditional update 成功：本次登入成功；後續登入必走 current hash。
   - concurrency conflict：只重讀一次。若新值是 strict hash，重新驗證 submitted input；若仍為 legacy candidate，僅依重讀值驗證，不再盲目寫入。
   - timeout/ambiguous/update unavailable：重讀一次可得結果時依最新值驗證；仍無法確定時，已成功 legacy verify 的本次登入維持成功、不得宣告 migration completed、不得寫入任何替代值。下一次登入可重試。
   此規則避免 partial migration 導致 lockout 或資料毀損；不能證明 row-version conditional update 時，migration 必須 disabled，不能以無條件寫入替代。
5. ACCOUNT 驗證成功後在 `AuthenticationController.Private.cs` 由 verified `loginContact.Id` 建立 key；所有 post-login manager/session/cache 呼叫用 key，不可再傳 `viewModel.Password`。ACCOUNT auth-ticket password key 必須維持空字串，`CreateLoginResponse` 不得讀取或序列化 submitted password。
6. `ContactService.RetrieveByAccountNumber` 的 B01 account path 只接收 parser 驗證過的 key，並查詢同一 active account/contact。舊 raw password 值、偽造/格式錯誤 key、帳號不符 key 都 fail closed。這取代 line 210 的直接比較，而不調整不相關 loader。
7. 完成 fake unit tests 後，必須取得下列 non-production runtime evidence 才可宣告登入後相容性成功；缺少它是 deployment blocker，不得以 credential-store fake 代替。

## 外部前置條件與真實 blocker

- Owner：F03A/CRM operations owner 與 non-production environment owner。
- 必要 evidence：受控 non-production active contact 能以 CRM SDK 讀到 row version，且 `UpdateRequest` 的 `IfRowVersionMatches` 對該 contact 可成功、衝突可被可靠區分；結果只記錄 case id、status、row-version capability pass/fail，不記錄 account/contact id/credential/hash。
- 必要 action：提供可重設的 synthetic QA contact 與最小權限 test identity，執行 `ProcessLogin -> SetupSystemData` 成功/失敗 route probe，確認 List/Fee/Appointment 初始化經 key 仍解析同一 contact。
- Owner：ToolUtility/F03A compatibility owner。
- 必要 evidence：以 source/package consumer inventory 盤點 `RetrieveContactEntityByAccountNumber`、`RetrieveContactByAccountNumber`、`AccountLogin` 的 deployed callers；B01 account post-login callers 必須全數經 B01 key，任何仍傳 raw password 的 caller 必須列出 owner 與 wave，不能默認相容。
- 必要 action：在 release 前保存只含 caller path、owner、key/raw classification 的盤點結果；若發現未列 allowlist 的 caller 需要 raw password，將該 caller 移交其 owner 的獨立 wave，不在本 wave 修改。
- 真實 blocker：若 row-version conditional update、QA probe 或 caller inventory 無法取得，migration 與「完整登入後相容性」均不得宣告成功或部署。可保留 unit-test-only 修復候選，但 wave repair 結果必須標記 blocked；不得改動 F03A code、CRM schema 或 global authorization 來繞過。

## 本機驗證與必要證據

```powershell
dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~AccountCredentialVerificationTests|FullyQualifiedName~AuthenticationCredentialLoginContractTests|FullyQualifiedName~LoginClaimsFactoryTests|FullyQualifiedName~LoginResponseFactoryTests"
dotnet test .\ToolUtility.Tests\ToolUtility.Tests.csproj --filter "FullyQualifiedName~ContactServiceTests|FullyQualifiedName~ToolUtilityFacadeIntegrationTests"
rg -n --glob '*.cs' 'GetAttributeValue<string>\("new_app_pass"\)\s*==|storedPassword\s*!=|new_app_pass.*==|==.*new_app_pass' .\SpeechMessageProducts.ChurchReport .\ToolUtility
rg -n --glob '*.cs' 'SetString\(|Session\.Set|\.SetString\(|new Claim\(|LoginClaimsFactory\.Build|SignInAsync|Json\(|return View\(|Debug\.WriteLine|ILogger|Log[A-Z]|Trace\.Write|throw new Exception|new_app_pass.*=|SetEntityStringAttribute.*new_app_pass|Update\(' .\SpeechMessageProducts.ChurchReport .\ToolUtility
git diff --check
git diff --name-only
```

必須產出：指定 test totals、case id pass/fail、direct-comparison search count、每個 sink category 的 zero/raw-or-key-only 判定、ToolUtility delegation result、allowlist diff result。輸出只能包含路徑、行號、category、case id、count、exit code 和 pass/fail；不得列印 fixture、hash、salt、account/contact identifier、session/claim value、response body 或 CRM entity payload。

## 完整 rollback 邊界

本 wave 的程式 rollback 僅限上表 paths 所構成的單一修復 commit；禁止回退到任何 direct comparison。已遷移的 CRM value 不能由 hash 還原成 legacy material，因此 release rollback 只能回到仍支援同一 `B01PH$v1` verifier/key 的前一版本；不可只還原 controller 或 ContactService。若需要資料 rollback，必須由 CRM owner 在受控程序使用既有受保護備份執行，且先在 non-production 驗證登入與 key compatibility。任何 row-version 不支援、update ambiguity 無法安全讀回、route probe/loader failure、raw persistence 命中或 diff 超出 allowlist，都使 wave 停止或 rollback，不能以擴張到其他 B01 issue 處理。

## 審查核准政策

本合約可由以下任一完整審查路徑核准：

1. Claude 有可用輸出且明確 `APPROVED`，無未解決 Critical/Warning；或
2. Claude 無可用輸出時，依 wave workflow 執行的**恰好一位唯讀 Codex fallback reviewer**明確 `APPROVED`，無未解決 Critical/Warning。

不得把 fallback 視為拒絕，也不得在 Claude unavailable 時探測 Gemini。本 wave 已由上述第二條 Codex fallback 路徑核准；此狀態不表示任何 repair/deployment gate 已滿足。
