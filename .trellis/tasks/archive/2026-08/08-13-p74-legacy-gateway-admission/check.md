# P7.4 legacy Gateway admission boundary 規劃審查紀錄

## 2026-08-13 CCG architect 審查

- 執行方式：專案 `Start-CcgDualModelRun.ps1` self-healing runner；等待預算 45 秒。
- 結果：Gemini 有可用 architect output；Claude 遭遇 session/quota limit，沒有可用 output。
- 結論：`degradedFallback=true`，本次是 Gemini 單模型降級審查加本機核對，**不是**完整雙模型審查。
- 原始 artifacts：`.ccg/dual-model-runs/20260813-033116-p74-legacy-admission-design-review-architect/`（不納入 task commit）。

## 已驗證並納入設計的 Critical findings

1. 同步 legacy `IOrganizationService.RetrieveMultiple` 不接受 cancellation；controller 的 lease
   deadline 或 release 不能證明遠端 SDK I/O 已停止。因此 overrun 一律是 unknown/no-go。
2. 單一 fee path 的 operation-level meter 無法證明全 Organization 的 legacy ingress 均已排空；
   未納管入口一律是 `legacy-coverage-unproven`。
3. per-host in-memory state 無法取代跨 host durable coordination；必須由 deployment owner 以
   canonical Organization、namespace、epoch、configuration digest 和 durable coordinator 的 read-back
   證明，或完成可讀回的 drain-first non-overlap 演練。

## 實作前決議

- 本 child 僅交付 fail-closed local control-plane、contract tests、固定分類 validator 與 runbook。
- `DynamicsAccess:Package01FeeReadsEnabled` 保持 false；不做 CE mutation、流量切換、P7.5 或 P8。
- 實作與測試必須覆蓋 double-dispose、acquire/stop race、drain timeout、shutdown、A/B isolation、
  cleanup baseline 及同步 overrun 的 no-go 分類。

## 2026-08-13 實作審查與本機品質閘門

- 實作審查已透過 `Start-CcgDualModelRun.ps1` 執行。Gemini 完成可用 reviewer output；Claude 因
  provider session/quota limit 沒有 output。依 45 秒規則停止等待，記錄為「雙模型未完成，採本機驗證」；
  這是 Gemini 單模型降級審查，不是完整雙模型審查。
- Gemini 沒有 Critical finding；Warning 為終端轉碼顯示 mojibake。已以 byte-level audit 驗證本 child
  範圍的 C#、PowerShell、Markdown、JSON/JSONL 均為 UTF-8 無 BOM、CRLF-only 且 final CRLF。
  Gemini 提醒的 optional-controller compatibility path 已在設計中明確標示為非 deployment proof，沒有
  被當成 feature enablement 證據。
- 完整 solution 初次測試捕捉到 `OfficialWorkerControlPlaneAdmissionTests` 的測試檔案競態：Worker PID
  evidence 已建立但 writer 尚未釋放 Windows handle 時，reader 可能收到 sharing violation。已先加入
  failing regression，確認舊 `launchSettings` DedicatedGateway profile 仍將 Package01 flag 覆寫為 true，
  隨後改為 false 並通過；PID reader 則只對 Windows 32/33 sharing/lock violation 在既有五秒 bounded
  deadline 內重試，其他 I/O 立即失敗。此修正不影響產品 runtime、CE、Gateway 流量或外部部署。

### 新鮮驗證證據

- P7.4 control-plane focused suite：25/25 passed。
- Official Worker control-plane/profile 與 PID-reader regression：44/44 passed。
- DedicatedGateway launch profile disabled regression：1/1 passed；checked-in appsettings、Development 與
  DedicatedGateway launch profile 均保持 `Package01FeeReadsEnabled=false`。
- 完整 `dotnet test .\SpeechMessageProducts.sln --configuration Release --no-restore --nologo`：exit 0；
  ChurchReport 548 passed/14 skipped，Dynamics 736 passed/7 skipped。跳過者皆為明確 live CE/SQL evidence
  fixture，不是本機失敗或 feature enablement。
- `dotnet build .\SpeechMessageProducts.sln --configuration Release --no-restore --nologo`：exit 0，0 warnings，0 errors。
- validator 正例輸出 `GO: all-required-evidence-proven`；缺一 switch 輸出 `NO-GO: sync-overrun`，未知輸入
  輸出 `NO-GO: invalid-input`；它沒有 network、CRM、SQL、feature flag 或檔案 mutation。
- `python .\.trellis\scripts\task.py validate 08-13-p74-legacy-gateway-admission` 及 `git diff --check` 均通過。

## 2026-08-13 最終 CCG review

- 最終 reviewer run 的 45 秒等待預算到期；Gemini 已在期限內產出可用結果，判定 Critical=0、Warning=0、
  Info=3、verdict=PASS。三項 Info 分別重申 UTF-8/CRLF audit、PID reader 僅重試 32/33、以及
  DedicatedGateway launch profile 旗標已回復 false。
- Claude 在上限前未產出 output；依使用者規則停止等待且不重試，紀錄為「雙模型未完成，採本機驗證」。
  本結果不可稱為完整雙模型審查。
- 原始 artifact：`.ccg/dual-model-runs/20260813-042150-p74-legacy-admission-final-review-reviewer/`
  （不納入 task commit）。Gemini 沒有提出需修正的 Critical 或 Warning。

## 結論與 handoff

本 child 的 repository-side local control-plane、runbook、validator、測試與 disabled configuration 已完成，
可以封存為 P7.4 的本機安全邊界成果；它**不**是 CE evidence、durable coordinator 證據、legacy coverage
證據或任何 feature/traffic enablement 授權。下一個 P7.4 child 必須繼續依 authoritative 70-row matrix
遷移獨立 consumer，所有 flags 保持 false。P7.5/P8 繼續受 zero-reference 與 immutable handoff gate 保護。
