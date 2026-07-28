# Phase 0 no-SDK removal gate 審查報告

審查範圍：`.trellis/tasks/07-23-dynamics-connection-compatibility/{prd,design,implement,phase0-inventory,phase0-verification,phase0-runtime-capacity-adr}.md`、`phase0-organization-call-matrix.{schema.json,json}`、`docs/superpowers/specs/2026-07-23-dynamics-access-gateway-design.md`、`.ccg/tasks/dynamics-connection-compatibility/task.json`，並逐條比對實際 repo 內的 `SpeechMessageProducts.sln`、`ToolUtility/ToolUtility.csproj`、`ToolUtility.Tests/ToolUtility.Tests.csproj`、`PowerPlatform.Dataverse.Client/PowerPlatform.Dataverse.Client.csproj`、`SpeechMessageProducts.ChurchReport/SpeechMessageProducts.ChurchReport.csproj`。

## Critical 🔴

無。目前 Phase 0 產出物未修改任何 production 原始碼/專案參照/方案檔，`enforcement.currentMode` 為 `report-only` 且 `migratedSourceRoots` 為空陣列，符合「Phase 0 不得因刪除舊專案而破壞現有建置」的要求。SDK-006/SDK-007 的 disposition 均為 `final-removal-required`（而非 `temporary-legacy`），最終無 SDK 狀態的措辭在 prd.md「SDK-removal end state」、design.md §12.1/§12.3、implement.md Phase 6、phase0-inventory.md「Final removal rule」四處完全一致，且都明確要求「移除 sln 項目 + 移除 ProjectReference + 刪除/移出可建置原始碼，不得包裝(wrap)或改名保留」。未發現矛盾。

## Warning 🟡

- **`.trellis/tasks/07-23-dynamics-connection-compatibility/implement.md:65-66`（Phase 0 步驟2）與 `.github/workflows/toolutility-tests.yml`**
  文件宣稱 Phase 0 要「Add a CI report-only scan for banned SDK DLL paths/packages/types」，`phase0-inventory.md` 與 `design.md:1363-1367`、`implement.md:589-615` 都把 `eng/Verify-NoDynamicsSdk.ps1` 與 CI gate matrix 當作既有機制描述。但實際檢查 repo：**不存在 `eng/` 目錄、不存在 `Verify-NoDynamicsSdk.ps1`、不存在 `eng/no-sdk-source-roots.json`**，現有唯一的 workflow `toolutility-tests.yml` 完全沒有任何 SDK 掃描步驟。`phase0-verification.md` 裡「Commands run」也只是人工在本機跑的 `rg`/`ConvertFrom-Json` 一次性指令，並未固化成可重跑、可在 PR 上自動觸發的腳本。
  - 影響：目前沒有任何自動化機制能阻止新的 commit 再引入 `Microsoft.Xrm*`/`Microsoft.CrmSdk*`/HintPath，"gate" 目前只是文件承諾，尚未真正「掛閘」。與任務名稱 `phase0-no-sdk-removal-gate` 的字面期望有落差。
  - 建議：在本 Phase 0 收尾前，至少新增一個 report-only 的 CI 步驟（PowerShell 或 GitHub Actions job）執行 `phase0-inventory.md` 中列出的三段 `rg` 掃描（或建立 `eng/Verify-NoDynamicsSdk.ps1` 骨架），把結果與 `phase0-organization-call-matrix.json.bannedSdkReferenceFindings` 對照，先以 report-only 模式跑起來，之後才逐步升級為 mandatory gate（如 implement.md 自己規劃的路徑）。

## Info 🟢

- **`phase0-organization-call-matrix.json` SDK-005（line 56）**：`"line": 108` 對應的是 `ChurchReport.csproj:108` 的 `<Reference Include="Microsoft.Crm.Sdk.Proxy">` 開始行，實際 `<HintPath>` 文字在第 109 行。其餘 6 筆 finding（SDK-001/002/003/004/006/007）的行號經逐一比對均與檔案內容精確吻合。此為極小的行號指向誤差（指向區塊起始行而非 evidence 引用文字所在行），不影響判定，僅建議未來產生器直接指向 HintPath 所在行以利精確定位。
- **`ToolUtility.Tests` 未被納入 `SpeechMessageProducts.sln`**：驗證屬實（sln 內僅有 18 個 Project 項目，`ToolUtility.Tests` 不在其中），SDK-001 標記為 `test-only-temporary-legacy` 與 design.md:1289-1291 的說明一致，屬正確揭露而非遺漏。
- **`normalizedCallSites: []`**：目前尚未有任何列被正規化，`phase0-verification.md`「Remaining Phase 0 work」已誠實標注此為未完成項目，符合 Phase 0 漸進式盤點的定位，非阻塞問題。

## 逐項結論

1. **SDK 參照圖是否完整**：是。針對 `*.csproj/*.vbproj/*.fsproj/packages.config` 與 `*.props/*.targets` 的實測掃描結果與 `bannedSdkReferenceFindings`（SDK-001~007）完全對應，無漏項、無多報。
2. **最終無 SDK 終態是否明確要求刪除/移出而非包裝**：是，四份文件用詞一致且明確（"must not be wrapped, renamed, or retained"）。
3. **Phase 0 是否避免過早刪除**：是，`enforcement.currentMode = report-only`，未改動任何 build 相關檔案，已用 Bash 驗證 repo 中的 SDK 參照仍原封不動存在。
4. **是否與既有方案拓撲矛盾（新增 Dynamics 專案到 SpeechMessageProducts.sln vs 強制新 sln）**：無矛盾。prd.md、design.md §1/§3、implement.md Phase 1、`phase0-runtime-capacity-adr.md` follow-up #3、matrix schema 的 `solutionTopology.mandatorySeparateDynamicsSolution: const false`、docs spec 均一致要求加入既有 `SpeechMessageProducts.sln`，且 schema 層面已用 `const false` 鎖死該欄位，防止未來文件漂移。
5. **是否遺漏 session/記憶體洩漏、連線池、SDK 移除執行面的 Critical/Warning**：session/記憶體/連線池相關的守則在 design.md §7、§9-11 與 ADR-005 已描述得非常詳盡（因尚無實際 runtime 程式碼，Phase 0 無可稽核的執行面問題）；唯一實質缺口是上述「SDK 移除執行面（CI gate）尚未實作掛接」的 Warning。

## 總結

Phase 0 產出物在**盤點完整性**與**最終無 SDK 終態的措辭一致性**上表現良好，且已用實際檔案交叉驗證無誤；`SpeechMessageProducts.sln` 拓撲決策沒有內部矛盾。唯一需要在正式結案前補上的是：把文件中承諾的 report-only CI 掃描實際落地為可執行、可在 PR 觸發的自動化腳本，否則「gate」名不符實。建議：**approve with follow-up**（不阻塞 Phase 0 收尾，但應在進入 Phase 1 前補上 CI 腳本）。

---
SESSION_ID: 347582cc-59f3-4e49-8685-8d0c91ded5b6
