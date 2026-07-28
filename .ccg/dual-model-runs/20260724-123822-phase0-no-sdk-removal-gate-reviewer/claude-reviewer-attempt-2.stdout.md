# Phase 0 no-SDK 移除關卡審查報告

## 審查範圍
`.trellis/tasks/07-23-dynamics-connection-compatibility/{prd,design,implement,phase0-inventory,phase0-organization-call-matrix.schema,phase0-organization-call-matrix,phase0-runtime-capacity-adr,phase0-verification}.md/json`、`docs/superpowers/specs/2026-07-23-dynamics-access-gateway-design.md`、`.ccg/tasks/dynamics-connection-compatibility/task.json`。並比對實際檔案：`SpeechMessageProducts.sln`、`ToolUtility/ToolUtility.csproj`、`ToolUtility.Tests/ToolUtility.Tests.csproj`、`PowerPlatform.Dataverse.Client/PowerPlatform.Dataverse.Client.csproj`、`SpeechMessageProducts.ChurchReport/SpeechMessageProducts.ChurchReport.csproj`，並重跑 phase0-inventory.md 內的 `rg` 掃描指令做交叉驗證。

## 五項問題結論

1. **Phase 0 inventory/gate 是否完整代表現有 SDK 參照圖？** 是。實際重跑 `rg` 掃描確認整個 repo 中只有 3 個 csproj 含 SDK 套件參照（`ToolUtility.Tests`、`PowerPlatform.Dataverse.Client`、`SpeechMessageProducts.ChurchReport`），與 `phase0-organization-call-matrix.json` 的 SDK-001~SDK-007 完全對應；SDK-006/SDK-007 的 sln/ProjectReference 邊也逐行核對正確。來源檔掃描總數 165 與矩陣 `sourceCandidateGroups` 加總（98+52+7+3+3+2=165）完全吻合。
2. **最終無 SDK 狀態是否清楚要求刪除/移出可建置原始碼，而非包裝保留？** 是，措辭明確（`phase0-inventory.md:45-51`、`design.md:274-277`、`implement.md:564-579` Phase 6 步驟2-4）：「must not be wrapped, renamed, or retained」「Delete or move…out of buildable source」。
3. **Phase 0 是否避免過早刪除？** 是。`phase0-inventory.md:5-6` 明確聲明唯讀基線、不變更行為；`enforcement.currentMode` = `report-only`、`migratedSourceRoots: []`；未見任何刪除性變更。
4. **是否與既有方案拓撲矛盾（強制新 SpeechMessage.Dynamics.sln）？** 否，一致。`schema.json` 的 `mandatorySeparateDynamicsSolution: const false`，`design.md:6-24`、`implement.md:110-112`、`phase0-runtime-capacity-adr.md:106` 均一致採用「加入既有 `SpeechMessageProducts.sln`」，並實測確認舊「mandatory separate solution」措辭已不存在。
5. **是否缺少 session/memory leak、connection pooling、SDK 移除落實面的 Critical/Warning？** design.md 第7章與 ADR-005（`phase0-runtime-capacity-adr.md:85-100`）在設計層面已涵蓋得很完整（zero-tolerance release gates、soak test、handler/timer/socket 歸零驗證）。但**執行落實面**存在下述具體缺口，詳見 Warning。

---

## Critical 🔴
無。

## Warning 🟡

- **`.trellis/tasks/07-23-dynamics-connection-compatibility/phase0-inventory.md:24`** — 摘要表宣稱「ToolUtility matching source files | 56 files」，但同文件內附的「Reproducible scan」指令（第10-13行）實際重跑結果為 **52** 檔（已用 `rg` 獨立重現驗證），且與具機讀權威性的 `phase0-organization-call-matrix.json` 的 `SRC-GRP-002.candidateFileCount = 52` 一致。也就是說矩陣是對的，`phase0-inventory.md` 敘述性摘要表是錯的/過期的。
  - 風險情境：日後工程師只讀敘述性 inventory.md 做 ToolUtility 遷移範圍評估，會誤信 56 個檔案，與機讀矩陣、CI 未來會用的來源不一致，削弱「Reproducible scan」的可信度。
  - 修復：更新 `phase0-inventory.md` 表格為 52（或改為直接引用矩陣總數），並在文件中加一句「數字以 `phase0-organization-call-matrix.json` 為準」防止未來雙來源漂移。

- **CI 尚未真正接上 Phase 0 report-only SDK 掃描關卡** — `implement.md:65-69`（Phase 0 步驟2）要求「Add a CI report-only scan for banned SDK DLL paths/packages/types」，並在 Validation commands 區塊（`implement.md:588-615`）明確指名 `eng/Verify-NoDynamicsSdk.ps1`、`eng/no-sdk-source-roots.json`、CI gate matrix 第一列 `Legacy SDK inventory`。但目前 repo 中：
  - `eng/` 目錄不存在（`Glob eng/**` 無結果）。
  - 唯一現行 workflow `.github/workflows/toolutility-tests.yml` 只跑 xunit 測試與覆蓋率，未含任何 SDK 參照掃描步驟。
  - `phase0-verification.md:32-36`「Remaining Phase 0 work」清單也**沒有**把「補上 CI report-only 掃描腳本/workflow」列為待辦。
  - 風險情境：在 Phase 0 → Phase 1 過渡期間，若有人新增一筆 `Microsoft.Xrm.*`／`Microsoft.CrmSdk.*` 參照，或有人誤刪 `PowerPlatform.Dataverse.Client` 的 project reference（違反「不可過早刪除」原則），目前沒有任何自動化機制會攔截，Phase 0 的「gate」目前只是文件承諾，不是可執行的守護。
  - 修復：在本輪 Phase 0 收尾前，至少落地一個最小可行的 report-only CI step（即使先用 PowerShell `Select-String` 掃描 csproj/sln，對照 `bannedSdkReferenceFindings` 清單，僅記錄不失敗），並把此項目補進 `phase0-verification.md` 的 Remaining work，否則建議在 Phase 0 完成判定前不要視為「已完成」。

## Info 🟢

- **`phase0-organization-call-matrix.json` SDK-005** 的 `line: 108` 指向 `SpeechMessageProducts.ChurchReport.csproj` 的 `<Reference Include="Microsoft.Crm.Sdk.Proxy">` 元素起始行，但 evidence 引用的 HintPath 文字實際在第109行。屬於行號精確度的小瑕疵，不影響發現本身的正確性；建議未來產生矩陣時記錄「起訖行」或直接記 HintPath 所在行（109）。

- **`docs/superpowers/specs/2026-07-23-dynamics-access-gateway-design.md`（498行）** 是 `.trellis/.../design.md`（1401行）的精簡改寫版，章節標題與行文完全不同（例如 `## Outcome` vs `## 1. Decision`），已用 rg 驗證兩份文件中「final no-SDK removal」「mandatory separate SpeechMessage.Dynamics.sln」等關鍵措辭都存在且一致，未發現矛盾。但因為它是獨立改寫而非機械同步的摘要，未來 `design.md` 更新時沒有機制保證這份 spec 副本會同步跟進，建議在其中一份加上「本檔案為 X 的摘要／權威版本，異動請先改 X」的指引以降低長期漂移風險。

## 總結
Phase 0 的 inventory/matrix/ADR 內容在**事實正確性**上經過交叉核對（SDK-001~007 行號、來源檔統計、sln/csproj 拓撲）幾乎全部準確，且用詞明確禁止「包裝保留」PowerPlatform.Dataverse.Client、不強制新 sln、未做任何提早刪除的行為變更，符合使用者的四項核心要求。主要落差在於：(1) `phase0-inventory.md` 的一個統計數字與機讀矩陣不一致，屬文件品質問題；(2) implement.md 承諾的 CI report-only SDK 掃描關卡尚未真正落地成可執行的 workflow/script，目前僅為規劃文字，建議在宣告 Phase 0 完成前補上，否則「gate」這個字目前名不副實。建議：先修正 Warning 1（數字更正），並將 Warning 2 列為 Phase 0 正式完成前的硬性阻擋項。

---
SESSION_ID: 17fefca4-c5a0-488e-9e69-f431d90405e9
