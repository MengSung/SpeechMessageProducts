# Wave 2 可重現量測：B01-SEC-003

CONTRACT_STATUS: WAVE_PLAN_APPROVED

審查證據：Claude-only review 無可用輸出；依 wave workflow 執行的一次唯讀 Codex fallback 複審已明確核准，且無未解決 Critical 或 Warning。此合約核准不滿足或取代本文要求的 non-production runtime prerequisite。

## 量測邊界與資料遮罩

唯一 issue 為 `B01-SEC-003`。所有 unit test 以 `IAccountCredentialStore` fake、mock CRM query service 與 fake HTTP session 執行；不得呼叫真 CRM。fixture 在 test process 內產生，僅以 case id 表示。assertion helper 必須避免將 checked value、hash、salt、account、contact id、session value、claim value、response body 或 CRM entity payload 放入失敗訊息、`ToString()`、logger 或 test output。

不允許用 credential-store fake 單獨宣告完整 route 成功。完整 post-login 相容性另需 plans.md 所列 non-production CRM row-version/route probe；該 prerequisite 未滿足時，量測狀態為 blocked，而非 passed。

## 基線量測

| ID | 重現程序 | 預期 baseline | 記錄單位 |
| --- | --- | --- | --- |
| BL-01 | 搜尋 `AuthenticationController.Private.cs` 與 `ToolUtility/ContactOperations/ContactService.cs` 的 `new_app_pass` 直接相等比較 | 兩個已知 direct-comparison location：controller account verifier 與 ContactService line 約 210 | 命中數、路徑、行號 |
| BL-02 | 追蹤 `ProcessLogin -> InitializeUserSessionAsync -> SetupSystemData` | submitted password 被送入 `_LoginPassword`、Appointments/List/Fee manager，再經下游 contact lookup | sink category 與 call-site count |
| BL-03 | 執行既有 Login claims/response tests | 既有 cookie/JSON tests 通過，但沒有 strict format、migration 或 post-login key contract | passing test count |

```powershell
rg -n --glob '*.cs' 'GetAttributeValue<string>\("new_app_pass"\)\s*==|storedPassword\s*!=|new_app_pass.*==|==.*new_app_pass' .\SpeechMessageProducts.ChurchReport .\ToolUtility
rg -n --glob '*.cs' 'viewModel\.Password|_LoginPassword|m_Password|new_app_pass' .\SpeechMessageProducts.ChurchReport .\ToolUtility
dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~LoginClaimsFactoryTests|FullyQualifiedName~LoginResponseFactoryTests"
```

Baseline evidence 不得擷取 matching source text；只記錄數量、路徑、行號、分類與 exit code。

## 格式與 migration 固定測試矩陣

共 **16** 個必要 cases。每個至少執行一次；如拆為多個 test，報告仍按這 16 個 ID 聚合，不得少報。

| ID | Fixture 類別 | 預期結果 | 必要觀察 |
| --- | --- | --- | --- |
| FMT-01 | strict `B01PH$v1`、600000 iterations 的有效 envelope | 驗證成功 | migration update=0；固定時間比較路徑 |
| FMT-02 | strict current envelope + 不相符 submitted fixture | 一般登入失敗 | update=0 |
| FMT-03 | 空白 credential material | 一般登入失敗 | update=0；不進 legacy |
| FMT-04 | `B01PH` 保留前綴但 segment/encoding/salt/subkey 損壞 | fail closed | update=0；legacy verify=0 |
| FMT-05 | `B01PH` 保留前綴但未知 version/algorithm | fail closed | update=0；legacy verify=0 |
| FMT-06 | strict-looking envelope 但 iteration 小於 600000 或大於 1000000 | fail closed | update=0；legacy verify=0 |
| MIG-01 | 無 `B01PH` 前綴的有效 legacy fixture，CRM row version 可用 | 本次登入成功並 migration completed | conditional update=1；新值只可由 current verifier 驗證 |
| MIG-02 | legacy fixture + 不相符 submitted fixture | 一般登入失敗 | update=0；原值未變 |
| MIG-03 | legacy fixture + conditional update conflict，重讀為 valid current envelope | 本次登入成功 | initial update=1；重讀=1；不覆寫並行值 |
| MIG-04 | legacy fixture + update timeout/ambiguous，但重讀仍為同一 legacy fixture | 本次登入成功、migration deferred | 原值未變；下一次可重試 |
| MIG-05 | legacy fixture 但 CRM 無 row-version conditional-update capability | 本次登入成功、migration disabled | update=0；不無條件寫入 |
| KEY-01 | verified contact id + original account | server-issued B01 key 可嚴格 parse | key 與 submitted fixture 不同；不含 raw submitted bytes |
| KEY-02 | valid key 經 `ToolUtilityClass/ToolUtilityFacade/ContactService` | 找到同一 active account contact | 只使用 contact-id/account query；沒有 password compare |
| KEY-03 | raw/non-key、malformed key、account mismatch、inactive contact | fail closed | 零個 contact result；零次 direct compare |
| PERSIST-01 | 成功 ACCOUNT login 的 fake HTTP/session/CRM flow | 所有持久 sink 不含 submitted fixture | session/claims/response/log/manager/cache/CRM update 各自 pass |
| ROUTE-01 | 成功與失敗 `POST /Authentication/ProcessLogin` fake-store 分支 | route、method、四 JSON 欄位與結果類型不變 | 兩分支 pass；此 case 不取代 runtime probe |

## Raw-password sink 量測

每次修復前後都必須列出下列 source category 的 match count 與「raw / key-only / absent」結果。ACCOUNT 的正確結果只能是 `absent` 或明確允許的 request-local verifier 使用；任何 persistence sink 必須為 `key-only` 或 `absent`。

| 類別 | 必查範圍 | 修復後可接受結果 |
| --- | --- | --- |
| Session APIs | `ISession.Set*`、`SetString`、`Session.Set`、session extension | `_LoginPassword`/相關 key 只含 B01 compatibility key；submitted password=0 |
| Claims/auth ticket | `new Claim`、`LoginClaimsFactory.Build`、`IssueAuthTicketAsync`、`SignInAsync`、authentication properties | ACCOUNT password key 與所有 claims 都不含 submitted password |
| Response/view-model | `Json`、`View`、`LoginResponse*`、`GalleryViewModel` copy/assignment、`ModelState` serialization | response/view-model/validation error 不輸出 submitted password |
| Logging/diagnostics/exceptions | `Debug.WriteLine`、`ILogger/Log*`、`Trace`、exception message/string interpolation | 無 submitted/stored credential；只允許一般化 error code |
| InMemory/cache/manager | `_LoginPassword`、`m_Password`、cache key/value、appointment/list/fee context | 只能保存 B01 compatibility key 或非 credential identity；submitted password=0 |
| CRM update | `new_app_pass` assignment、`SetEntityStringAttribute`、`Entity` update、`UpdateRequest` | 只寫 strict current envelope；不可寫 submitted/legacy raw、null 或半成品 |

`PERSIST-01` 使用一個不會印出的 unique synthetic submitted fixture，對每個 fake sink 的捕獲資料執行 no-leak helper。helper 的 fail message 只輸出 sink category/case id；測試 console logger 只輸出 aggregate count。靜態搜尋僅當作 guard，不可取代 runtime no-leak assertion。

## 執行指令與 evidence 格式

```powershell
dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~AccountCredentialVerificationTests|FullyQualifiedName~AuthenticationCredentialLoginContractTests|FullyQualifiedName~LoginClaimsFactoryTests|FullyQualifiedName~LoginResponseFactoryTests" --logger "console;verbosity=minimal"
dotnet test .\ToolUtility.Tests\ToolUtility.Tests.csproj --filter "FullyQualifiedName~ContactServiceTests|FullyQualifiedName~ToolUtilityFacadeIntegrationTests" --logger "console;verbosity=minimal"
rg -n --glob '*.cs' 'GetAttributeValue<string>\("new_app_pass"\)\s*==|storedPassword\s*!=|new_app_pass.*==|==.*new_app_pass' .\SpeechMessageProducts.ChurchReport .\ToolUtility
git diff --check
git diff --name-only
```

修復結果 evidence 必須包含：時間、commit/diff identifier、命令 exit code、總測試數、16 個 case id、direct-comparison zero count、六類 sink verdict、allowlist diff verdict、runtime prerequisite verdict。不得輸出實際 fixture、hash、salt、credential、account/contact id、key、session/claim value、response body 或 CRM payload。

## Runtime prerequisite 量測

Non-production probe 必須驗證：active QA contact 的 `RowVersion` 可讀、conditional update 成功/衝突可區分、success/invalid account route 均能走過 `SetupSystemData` 的 List/Fee/Appointment 初始化，且由 key 找回同 contact。這個 probe 的 owner、證據格式與缺失時 blocker 定義在 `plans.md`。未完成時只能報 `RUNTIME_PREREQUISITE_BLOCKED`，不能以 16/16 fake tests 宣稱完整登入無回歸。
