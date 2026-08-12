# Check 進度：2026-08-12

## 已完成的檢查

- `P72ContinuationOperationIdsTests`：5 passed。覆蓋 D–H 13 個 operation ID、coverage call-site mapping、禁止 routing/CRM authority input、固定輸出分類、週報政策與 snapshot immutability。
- `Data8ProfileOperationExecutorTests.Execute_async_rejects_slice_d_to_h_local_only_operations_before_admission`：1 passed。13 個 local-only operation 在 admission、lease、client 前皆回傳 `operation.not-supported`。
- `SpeechMessage.Dynamics.Tests`：550 passed，7 skipped。skip 項目是明示的 live SQL coordinator tests，未把 skip 當作實機成功。
- `ChurchReport.MemberInfo.Tests` 的 Slice C live evidence filter：1 passed，4 skipped；skip 是未明確啟動 CE evidence 的安全預設，沒有 CE mutation。
- 已對本輪變更的 C# 檔做 UTF-8 無 BOM、CRLF-only、final CRLF byte-level check。

## 尚待執行

- ChurchReport Release build。
- PowerShell Slice C offline contract 與 fresh fixture contract 的重新執行。
- 完整 scope / `git diff --check` / encoding gate。
- CCG reviewer（Gemini + Claude）與 Trellis Check。
- 新 Slice C 唯讀 preflight；只有 go 才能進入一次新的 fresh-fixture CE cycle。

## 已知非本輪程式碼阻塞

`ToolUtility.Tests/ToolUtility.Tests.csproj` 目標為 `net8.0`，但其 project reference 的 `ToolUtility` 已為 `net10.0`。`dotnet test --no-restore` 因 `NU1201` 無法開始。此問題不能被當作本輪通過或忽略；需要獨立處理 test target framework 相容性，且不能為了通過測試而降低 ToolUtility target 或跳過隔離測試。

## 本輪追加 Check：2026-08-12

- 以 TDD 驗證三個 dynamic-list façade overload 曾錯誤地丟棄傳入 service、改查共享
  `_organizationService`；紅燈後以最小 forwarding 修正轉綠。7 個
  `DownloadListManagerIsolationTests` 全數通過，證明 marker、query 與結果不會跨 operation
  service 路由。
- 以 TDD 驗證 catalog builder 原本接受 `accessToken`、`organizationAlias`、`profileAlias`；
  三個紅燈後補上 authority guard，三個 regression 均轉綠。D–H executor regression 仍證明
  operation 在 admission、lease、client 前 fail closed。
- 嘗試讓既有 `ToolUtility.Tests` 升至 net10.0 以解鎖測試，發現該 suite 除 target mismatch
  外還有多項既有 constructor／interface API drift，與本輪 service forwarding 無關。已完全撤回
  target 變更，沒有以部分失敗的 suite 宣稱全綠；可執行的 ChurchReport ownership regression
  繼續作為本輪證據。
- `ListManager.SetupIntegrateData` 仍委派給直接持有 Factory ToolUtility 的
  `DownloadIntegrateData`；因其 service、converter 與多個 partial read/write path 尚未具
  request-local lease propagation，不能進入 P7.4/P7.5。這是切流 blocker，不是本機 RC 的
  runtime failure。
- ChurchReport Release build 為 0 warnings／0 errors；相關 targeted tests 36 passed；
  UTF-8 無 BOM、CRLF、final CRLF 與 `git diff --check` 均通過。

## Gateway 負向啟動測試檢查：2026-08-12

- 變更範圍僅限 `GatewayWorkloadBoundaryTests` 與
  `GatewayRequestBodyBoundaryTests`。預期 startup failure 改以直接驗證正式 startup validator；
  所有正向 HTTP／TestHost／Kestrel integration cases 保留，未改動 production Gateway。
- targeted 指令
  `dotnet test .\SpeechMessage.Dynamics.Tests\SpeechMessage.Dynamics.Tests.csproj --no-restore --filter "FullyQualifiedName~GatewayWorkloadBoundaryTests|FullyQualifiedName~GatewayRequestBodyBoundaryTests" --logger "console;verbosity=minimal"`
  結果為 58 passed、0 failed、0 skipped。
- 完整指令
  `dotnet test .\SpeechMessage.Dynamics.Tests\SpeechMessage.Dynamics.Tests.csproj --no-restore --logger "console;verbosity=minimal"`
  結果為 553 passed、0 failed、7 skipped；7 項都是明示 live SQL coordinator 相依條件，沒有被視為 CE 或
  Gateway 成功證據。
- 新增 helper 與測試註解明示 configuration snapshot、Host／provider 不建立、無 reload subscription、
  無外部 I/O 與 deterministic resource ownership；byte-level check 證明兩個修改 C# 為 UTF-8 無 BOM、
  CRLF-only、final CRLF。`git diff --check` 通過。
- CCG reviewer run `20260812-103840-p7-2-gateway-negative-startup-test-lifecycle-review-reviewer`
  為雙模型未完成：Gemini timeout（仍留可讀正向 review，無 Critical／Warning），Claude session quota
  無輸出。依 45 秒規則不重試，審查狀態明確降級為本機驗證＋部分 Gemini 輸出，不可稱完整雙模型審查。
- 規格已補入 `dynamics-gateway-hosting-version-routing.md` 的 deterministic negative deployment
  validation scenario，禁止為測試競態修改 `Program`、接受 `ObjectDisposedException`、關閉全域平行化或重試。
