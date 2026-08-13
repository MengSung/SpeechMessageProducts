# P7.1 認獻單強型別讀取能力實作計畫

## 前置條件

- 已讀取 AGENTS.md、parent roadmap、權威 matrix、P7.1 fee-read 垂直切片、P7.2 payment-return boundary，
  與 backend isolation/hosting/review specs。
- 先透過 CCG self-healing runner 執行 45 秒上限的 architect 分析；無可用 output 時記錄「雙模型未完成」，
  以本機 evidence 繼續。
- 所有 feature gate 維持 false；不執行 CE、fixture、consumer cutover、P7.5 或 P8。

## TDD 順序

1. 在 `SpeechMessage.Dynamics.Tests/Package01OperationRegistryTests.cs` 寫 RED：新 operation 必須有唯一的
   operation/template/parameter/response/boundary policy；未知或混合 response branch 仍被拒絕。
2. 在 `SpeechMessage.Dynamics.Tests/Package01DedicationBookingReadClientTests.cs` 寫 RED：client 轉送固定
   operation、deployment profile/workload、typed contact ID 及 exact cancellation token；錯誤 branch、
   operation mismatch、mutable source 與 A/B interleaving 不能發布或交叉重用 DTO。
3. 在 `SpeechMessage.Dynamics.Tests/Data8ProfileOperationExecutorTests.cs` 與現有 connector 測試寫 RED：
   capability 是 allowlisted、錯參數 pool 前失敗、正確 response 僅有 dedication-booking branch；並以
   source contract 保護 Data8 path 不含逐筆 `Retrieve`。
4. 僅在 RED 確認後，依序新增 abstractions record/response branch/registry、Data8 fixed projection、
   ProductClient interface/implementation、DI registration。每一層完成後執行對應 focused tests 至 GREEN。
5. 不修改 ChurchReport consumer；若任何必要變更要求 `Entity` rehydrate、browser authorization、session
   cache 或 legacy fallback，停止並把它拆為 P7.4 child，而不是在 P7.1 偷渡 consumer 變更。

## 完成檢查

6. 依序執行 Package01 registry/client/Data8 focused Release tests、所有 Dynamics Release tests、
   ChurchReport contract suite、solution Release tests 與 solution Release build。
7. 對本 child 新增/修改的 `.cs`、`.md`、`.json` 做 UTF-8 no-BOM/CRLF/final-CRLF 位元組檢查，執行
   `git diff --check`、scope scan 及 forbidden API scan（不得新增 `Entity`、`EntityCollection`、
   `ToolUtility`、`IOrganizationService`、`Retrieve(`、`GetAwaiter().GetResult()`、retry 或 gate=true）。
8. 對 diff 以 CCG self-healing runner 執行 45 秒上限的 Gemini+Claude reviewer；timeout/quota 僅記為
   `雙模型未完成`，不可重複等待或宣稱完整雙模型審查。
9. 更新 parent P7/P8 task metadata/roadmap，僅記錄實際 matrix dimension；執行 scope-only commit 並 archive
   child。下一步再依 immutable matrix 選取獨立 capability；P7.5/P8 gate 不得提前解除。
