# P7.2 定期定額奉獻付款回傳寫入邊界實施計畫

## 交付順序

1. 完成 legacy call-chain 與 archived P7.2 local-only contract 的只讀研究，將 mutation family、
   ownership、read-back、reconcile、rollback 與 cleanup 條件寫入 `design.md`。
2. 先在 `SpeechMessage.Dynamics.Tests/P72DonationPaymentLocalDecisionTests.cs` 增加失敗測試：
   所有 payment-return local plan 必須保持 `CeDispatchAllowed=false`、`ProductConsumerAllowed=false`；
   input 不得包含 caller authority；A/B interleaving 不能共用 plan input dictionary。
3. 只在 RED 已確認後，最小修改 `P72DonationPaymentLocalDecision`／`P72DonationPaymentLocalPlanBuilder`
   或其直接測試，使 immutable、bounded、local-only contract 轉綠；不可新增 connector、SDK、
   ToolUtility、`GetAwaiter().GetResult()`、HTTP/CE I/O、feature gate、DI consumer 或寫入 executor。
4. 對所有 payment-return local-only catalog definition 驗證固定 operation、input allowlist、
   exact read-back policy、no-replay timeout policy、reverse-known-key cleanup policy，並保留
   `CeExecutorEnabled=false` 與 `ConsumerEnabled=false`。
5. 執行 focused tests、相關 abstractions test project、Release build、encoding/CRLF、
   `git diff --check`、forbidden API/source scan 與 A/B isolation check。
6. 在 code/config/documentation 變更超過 30 行或涉及金融一致性時，透過
   `Start-CcgDualModelRun.ps1` 進行雙模型 review；單次最多等待 45 秒。逾時或 quota 時記錄
   `雙模型未完成`，改做本機 review，不將其稱為完整雙模型結果。
7. 將測試與 review 證據寫入 check artifact；完成 scope-only commit 後 archive child。若沒有
   新 governed fixture family 的全部前置條件，將 CE track 標為 pending/no-go，而非重試舊 cycle。

## 預期檔案範圍

- 修改：`SpeechMessage.Dynamics.Abstractions/Operations/P72DonationPaymentLocalDecision.cs`
- 修改：`SpeechMessage.Dynamics.Abstractions/Operations/P72DonationPaymentLocalPlanBuilder.cs`
- 修改：`SpeechMessage.Dynamics.Tests/P72DonationPaymentLocalDecisionTests.cs`
- 修改：`.trellis/tasks/08-05-gateway-purpose-and-positioning/{prd.md,design.md,implement.md,roadmap-p5-p7.md,task.json}`
- 新增／修改：`.trellis/tasks/08-13-p72-dedication-payment-return-write-boundary/` 下的 task artifacts。

不得修改 `RecurringDonationPaymentProcessor` 的真實 CRM 呼叫鏈，除非後續另一份已核准的 design 能
完整定義 server authorization、typed ProductClient、single writer、fresh fixture/ledger、read-back、
reconcile、rollback 與 cleanup；該條件目前不成立。

## 驗證命令

```powershell
dotnet test .\SpeechMessage.Dynamics.Tests\SpeechMessage.Dynamics.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~P72DonationPaymentLocalDecisionTests
dotnet test .\SpeechMessage.Dynamics.Tests\SpeechMessage.Dynamics.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~P72
dotnet build .\SpeechMessageProducts.sln --configuration Release --no-restore
git diff --check
```

此外以 PowerShell 位元組檢查所有本 child 修改的 `.cs`／`.md`／`.json`：UTF-8 無 BOM、
僅 CRLF 且 final CRLF；並用 `rg` 確認新 abstraction 沒有 `ToolUtility`、`Entity`、
`IOrganizationService`、`GetAwaiter().GetResult()`、`ExecuteAsync(` 或 feature-gate enablement。
