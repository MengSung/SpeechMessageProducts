# P7.2 受控定期奉獻付款回傳寫入家族實作計畫

## 前置條件

- 歷史 Slice C 已關閉，任何舊 nonce、ledger、descriptor、fixture 或 CE cycle 都不可使用。
- 本 task 僅可在完成 planning review 並啟動後修改 source；所有 C# 檔案必須 UTF-8 無 BOM、CRLF、
  final CRLF，並含完整繁體中文 XML／lifecycle 文件。
- Gemini／Claude 分析與審查透過 `Start-CcgDualModelRun.ps1` 並行嘗試；每次最多等待 45 秒，逾時立即
  以本機驗證繼續，記錄「雙模型未完成」。

## 實作順序

1. **Call-chain 與重用審計**
   - 讀取 payment-return workflow、existing P72 local decision／plan、fixture ledger/provisioner 及
     authoritative matrix；確認沒有可安全重用的 legacy fixture/descriptor。
   - 將結果寫入 `research/`；更新 parent roadmap 與 P7.4 parent stale next action，但不改其 gate。

2. **RED：cycle admission contract tests**
   - 在 `SpeechMessage.Dynamics.Tests` 新增 focused tests，驗證新 cycle 只有在全新 family binding、
     non-empty nonce、descriptor binding、empty ledger、preflight=go 時才可進入 provision。
   - 驗證歷史 binding、缺 nonce、missing descriptor、any no-go category、preflight unavailable、
     duplicate fixture、timeout／ambiguous、read-back mismatch 與 cleanup uncertainty 都 fail closed，
     並禁止 replay。
   - 以 A/B concurrent test 證明兩個 immutable admission inputs 不共用 mutable state。

3. **GREEN：純本機 cycle admission model**
   - 在 `SpeechMessage.Dynamics.Abstractions/Operations/` 建立獨立且 sealed 的 model，不加入 Data8、
     CRM SDK、network、file I/O 或 feature gate。
   - 使用封閉 enum／immutable record，提供 bounded safe dispositions：`NoGo`、`PreflightRequired`、
     `ProvisionAllowed`、`ReadBackRequired`、`CleanupRequired`、`Completed`。
   - 強制 `OperationExecuted` 在 timeout／ambiguous 後禁止 replay；沒有 evidence 的 terminal state 不可
     被下一個 call 升級為 `go`。

4. **Local contract 整合**
   - 將既有 `P72DonationPaymentLocalPlanBuilder` 的 future plan 與 admission model 對齊，維持
     `CeDispatchAllowed=false`、`ProductConsumerAllowed=false`。
   - 不修改 `RecurringDonationPaymentProcessor`，不加入 runtime executor，也不註冊 feature gate。

5. **新 fresh-fixture family 設計審計**
   - 為下一個 governed child 建立 descriptor／ledger schema、preflight fixed categories、allowlist、
     exact projection、reconcile、cleanup order 與 no-replay contract。這一步只建立本機文件與 tests，
     不讀取或修改 CE。
   - 若 repository-side descriptor control plane 不存在，記錄精確 no-go；不得掃描 CRM 或自行製造 baseline。
   - 已加入 `P72PaymentFreshFixtureControlPlane`，將 payment-specific schema、fresh nonce、descriptor
     digest、empty secure ledger、server-derived distinct owner、single fee-update allowlist、fixed projection
     與 reverse cleanup 明確收斂為 pure-local bootstrap。它只會回傳 read-only preflight 或 no-go；尚未
     實作 CE executor、fixture provision、Data8 request 或任何 mutation。

6. **品質檢查與完成紀錄**
   - 執行 focused P72 tests、P72 相關完整 test group、Release solution build、encoding/CRLF 檢查、
     `git diff --check`、forbidden API scan、scope check 與 CCG final review。
   - 通過後 scope-only commit/archive 本 child；將 CE cycle 状态留為 `evidence-pending` 或精確 no-go，
     而不是宣稱完成 CE 或 P7.5/P8。

## 預計檔案

- 新增：`SpeechMessage.Dynamics.Abstractions/Operations/P72GovernedPaymentCycleAdmission.cs`
- 新增：`SpeechMessage.Dynamics.Tests/P72GovernedPaymentCycleAdmissionTests.cs`
- 可能修改：`SpeechMessage.Dynamics.Abstractions/Operations/P72DonationPaymentLocalPlanBuilder.cs`
- 可能修改：`SpeechMessage.Dynamics.Tests/P72DonationPaymentLocalDecisionTests.cs`
- 修改：本 task artifacts、parent P5–P8 roadmap task metadata 與 CCG record。

## 禁止修改的檔案／路徑

- `RecurringDonationPaymentProcessor` 與任何 production legacy CRM writer。
- 歷史 Slice C fixture/ledger/provisioner 的 IDs、environment variables、evidence 或 archive。
- ChurchReport appsettings feature flags、traffic、CE 8.2、Official Worker、P7.5/P8 deployment code。

## 驗證命令

```powershell
dotnet test .\SpeechMessage.Dynamics.Tests\SpeechMessage.Dynamics.Tests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~P72DonationPaymentLocalDecisionTests|FullyQualifiedName~P72GovernedPaymentCycleAdmissionTests"
dotnet test .\SpeechMessage.Dynamics.Tests\SpeechMessage.Dynamics.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~P72
dotnet build .\SpeechMessageProducts.sln --configuration Release --no-restore
git diff --check
```

對所有本 child 新增或修改的 `.cs` 再做 byte-level UTF-8 no-BOM、CRLF-only、final CRLF 驗證；以
`rg` 確認沒有 `ToolUtility`、`IOrganizationService`、`Entity`、`EntityCollection`、`ExecuteAsync(`、
`GetAwaiter().GetResult()`、Session／HttpContext retained state 或 feature-gate enablement 進入此 contract。
