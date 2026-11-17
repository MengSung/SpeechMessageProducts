# P7-01c Live檢查、觀察與教會預設值

依07A §4.6、D7-23與§5 P7-01c；全部離線假SDK，不連任何組織、不執行舊程式。P7-01b已驗收推送37f81bd1，固定guard57ab3f62八項通過。



- [ ] 先寫測試並確認紅燈：`LiveCheck_ProductionOrganization_Rejected`、`LiveCheck_StateNotLiveForModule_Rejected`、`LiveCheck_DormantItems_SkippedAndReportedAsNotChecked`、`LiveCheck_ObservedWritesDifferFromExpected_ReportsDifference`、`LiveCheck_PushCountMismatch_ReportsDifference`、`LiveCheck_FailureMidRun_DeletesSeedAndLeavesStateUnchanged`、`LiveCheck_TaskCreatedWithExpectedPayload_CountsAsEvidenceThenDeleted`、`Observe_CountsOnly_NoRecordValuesInOutput`、`Observe_ProductionAllowed_ReadOnlyNoWrites`、`Observe_TraceUnavailable_ReportsNoDataNotZero`、`ChurchDefaults_OnlyFillsBlank_NeverOverwrites`、`ChurchDefaults_TwiceIdempotent`、`ChurchDefaults_AccountWithoutChannel_Untouched`。
- [ ] `live-check --org goodshepherdback --module <代號> --scenario-root <資料夾>`（只允許 `kind=test`）：
  - 前提：該模組要檢查的步驟目前是啟用且 Live（由先前的 `switch --plan` 做到；`live-check` 自己不改步驟狀態）；D34 當場確認（地雷 8）。分級（套用 `tierOverrides` 之後）是 `dormant` 的步驟與活動，它們的情境略過，在報告列為「未檢查」，不算通過也不算差異。
  - 對每個標了 `liveShadow` 的情境：記下測試 Channel 的已用則數 → 建立 `seed` → 送出 `trigger`（步驟：對測試記錄做 Create／Update；活動：依地雷 9 建立測試流程並對測試記錄執行）→ 等對應的系統作業結束（最多 60 秒）→ 讀回情境 `observe` 指定的記錄與欄位、以及這次建立的待辦 → 與 `expected.json` 比對（正規化只有 05A 的 D5-5 那兩種）→ 再讀一次已用則數，增加量要等於期望的推播則數 → 刪除本情境建立的記錄、待辦、測試流程。
  - 讀回與比對沿用 `Shadow\` 既有的投影與比對程式（`ShadowSdkProjection`、`ShadowExternalObserver`、`ShadowComparer`），不另寫一套。
  - 需要主機處理才看得到結果的情境（只有代送）：由操作者先啟動 P7Live 的主機組合；其餘待辦類型依 D7-20 核對內容後刪除。
  - 輸出 `artifacts\live\<日期>-<模組>.md`；差異 0 而且殘留 0 才回 0。
- [ ] `observe`（§4.6）。
- [ ] `church-defaults --org <組織>`（只讀，產生 plan）與 `church-defaults --org <組織> --apply --plan <檔> --plan-sha256 <雜湊>`（D7-23）：只處理「已經指到 Channel 的教會」（以附錄 A.1 的 Channel 查閱欄判定）；四個欄位各自只在空白時補值；plan 只列教會的識別碼與要補哪幾欄，不列名稱。
- [ ] 文件：手冊留到 P7-02 再寫。附錄 G.1 的指令表不改（計畫文件不動，差異已列在 `P7-PROP-005`）；新指令寫進佈署專案的 `--help` 與 `.trellis\spec` 裡對應的規格。

**跑（三段都一樣）：** `dotnet test SpeechMessage.Dynamics.Deployment.Tests -c Release`；`dotnet test SpeechMessage.Hosting.Tests -c Release --filter "FullyQualifiedName~Architecture"`

**P7-01 完成：** a、b、c 三段都驗收；05 檔 P7-01「先寫的測試」四項都有對應的同名測試（回報附對照）；整個單元沒有連過任何 D365。最後的提交 `P7-01 新增 Live 實機檢查、觀察與教會預設值指令`。
