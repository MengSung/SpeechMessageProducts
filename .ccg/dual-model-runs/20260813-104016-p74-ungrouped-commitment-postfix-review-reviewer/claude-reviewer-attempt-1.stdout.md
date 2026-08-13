## CCG Review

### Review Scope
- `SpeechMessageProducts.ChurchReport/Services/DonationDynamicsAccessBootstrap.cs`（新增 `IsPackage02UngroupedCommitmentReadEnabled` / `TryCreatePackage02UngroupedCommitmentReadClient`）
- `SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs`（`LoadUngroupedMembers` 改為 async、cache bypass、typed dispatch）
- `SpeechMessageProducts.ChurchReport/Services/MemberInfo/Package02UngroupedCommitmentReadService.cs`（新檔）
- `SpeechMessageProducts.ChurchReport/appsettings.json`、`appsettings.Development.json`（新 gate，維持 `false`）
- `ChurchReport.MemberInfo.Tests/DonationDynamicsAccessBootstrapLifecycleTests.cs`、`MemberInfoTreeControllerContractTests.cs`、`Controllers/MemberInfoControllerPackage02UngroupedCommitmentContractTests.cs`（新檔）、`Services/Package02UngroupedCommitmentReadServiceTests.cs`（新檔）

### Critical
- **`ChurchReport.MemberInfo.Tests/DonationDynamicsAccessBootstrapLifecycleTests.cs:260`** — 測試 `Package02_ungrouped_commitment_read_allows_only_a_reviewed_base_and_sub_gate_combination`（宣稱驗證 base+sub-gate 都開啟時的 ORG-CALL-00024 factory 行為）實際呼叫的是既有 `TryCreatePackage02ContactProfileClient(configuration, injected)`，而非本次新增的 `TryCreatePackage02UngroupedCommitmentReadClient(configuration, injected)`。
  - 兩者行為不同：`TryCreatePackage02ContactProfileClient`（`DonationDynamicsAccessBootstrap.cs:239-257`）不呼叫 `EnsureNonEmptyProductProfile`，僅檢查 base gate 就回傳 injected client；而新方法 `TryCreatePackage02UngroupedCommitmentReadClient`（`DonationDynamicsAccessBootstrap.cs:267-291`）必須先驗證非空 `ProfileAlias`。該測試的設定並未提供 `DynamicsAccess:ProfileAlias`，若把方法呼叫換成正確的新方法，此測試會直接拋出 `InvalidOperationException` 而失敗——顯示這不是單純打字誤植，而是新方法的「gate=true 且 profile 存在時回傳 injected client」happy path 完全沒有被任何 runtime 測試覆蓋。
  - 檔案內唯一呼叫到 `TryCreatePackage02UngroupedCommitmentReadClient` 的地方只有 `DonationDynamicsAccessBootstrapLifecycleTests.cs:280-281`（僅測 profile 為空時拋例外），沒有測試涵蓋「兩個 gate 皆 true + profile 非空 → 回傳 injected client」與「gate=false → 回傳 null」這兩個關鍵分支。
  - 影響：這是本次 review 要求明確查核的「A/B isolation is tested」與「specialized Package02 factory validates … before host/provider/pool resolution」項目之一，目前的測試給出錯誤的安全感——若未來 `TryCreatePackage02UngroupedCommitmentReadClient` 的 gate 檢查邏輯被破壞（例如漏檢查 sub-gate、profile 驗證順序錯亂），此測試仍會通過，無法攔截這類 fail-closed 邊界的回歸。
  - 建議修正：把第 260 行改呼叫 `TryCreatePackage02UngroupedCommitmentReadClient(configuration, injected)`，並在該測試設定中補上 `DynamicsAccess:ProfileAlias`，同時建議新增一個「gate=false 時回傳 null」的獨立案例。

### Warning
- 無（本次未發現非阻斷性但需修正的問題）

### Info
- **`SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs:2151-2158`** — `LoadUngroupedCommitmentCountsAsync` 的 typed 分支先透過 `TryCreatePackage02UngroupedCommitmentReadClient(configuration)` 內部呼叫 `BindOptions(configuration)` 驗證 profile，隨後又額外呼叫 `DonationDynamicsAccessBootstrap.BindOptions(configuration).ProfileAlias` 重新繫結一次設定。屬單純重複繫結（非 I/O、非資源），不影響正確性，僅為可讀性/微幅效率上的觀察，非必須修正項目。
- 其餘查核項目經比對程式碼與 Gemini 先前報告，確認一致且正確：兩個 checked-in gate（`appsettings.json`、`appsettings.Development.json`）皆為 `false`，且 `IsPackage02UngroupedCommitmentReadEnabled` 先檢查 base gate 再檢查 sub-gate，fail-closed 成立；`TryCreatePackage02UngroupedCommitmentReadClient` 確實在 `CreatePackage02Executor`（host/provider/pool 解析）之前呼叫 `EnsureNonEmptyProductProfile`；`Package02UngroupedCommitmentReadService` 使用固定 `WorkloadSubjectId`/`ProfileAlias`、cancellation token 原樣傳遞、無 catch/retry/legacy fallback；`GetChurchGroupedCurrentIds` 新增的 `bypassCache` 只在 `useTypedUngroupedCommitmentCount && usesCommitmentSort` 時略過讀寫既有 3 分鐘 `IMemoryCache`，未引入新快取、Session 或資源持有；`Package02UngroupedCommitmentReadResult` 對外部/內部字典皆做防禦性複製，`RetrieveAsync` 對 null/duplicate/negative 皆 fail closed；`Package02UngroupedCommitmentReadServiceTests.cs` 的 A/B（`profile-a`/`profile-b`）交錯測試與取消傳遞測試完整涵蓋 service 層邊界；`LoadUngroupedMembers` 的 XML 文件與新測試檔皆以繁體中文明確標註 local-only candidate，未宣稱 CE 寫入、切流、P7.5 或 P8。

### Verification Notes
- 本次為 dual-model 執行的一部分：Gemini backend 已在 `.ccg/dual-model-runs/20260813-104016-p74-ungrouped-commitment-postfix-review-reviewer/gemini-reviewer-attempt-1.stdout.md` 產出報告（結論為 Passed、全部 Info，未發現上述測試錯配問題）；本回應為同一 run 中 Claude backend（attempt 1）的獨立審查結果，由 self-healing entrypoint（`Start-CcgDualModelRun.ps1` → `Invoke-CcgDualModelWithSelfHealing.ps1`）派送，未直接呼叫 Gemini/Claude CLI。
- 本次審查以直接讀取 `git diff` 與原始碼進行人工驗證，未執行 `dotnet test`；上述 Critical 發現是透過交叉比對 `TryCreatePackage02ContactProfileClient` 與 `TryCreatePackage02UngroupedCommitmentReadClient` 兩者實作差異（後者多出 `EnsureNonEmptyProductProfile` 檢查且測試設定未提供 `ProfileAlias`）而確認，屬可重現的靜態分析結果。
- 兩個 backend 皆產出可用輸出（無 quota/session 阻斷），因此本次為完整 dual-model review，非降級單模型 fallback。
- 依既有規則，來自任一成功 backend 的 Critical 發現仍須被驗證與修正，不因另一 backend 未回報而略過；建議在合併前修正上述測試錯配問題後再次跑本地測試套件確認。

---
SESSION_ID: 2b1da0c4-1b1d-4c87-b576-247f1aa928ab
