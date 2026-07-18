# 會友資訊可攜式部署套件：從這裡開始

## 這個套件能做什麼

這個套件把 `Sunny_5.1.2.WorktreeTuneMemberView` 的會友資訊改版歷程整理成可供人與 AI 閱讀的部署參考。目標是協助其他尚未具備完整頭像、區長→小組→會友樹、搜尋、明細、手機操作，以及固定頭像／姓名、原生調整欄寬與表頭排序、核准欄位順序、區小組數、小組時間／地點摘要與 Dynamics metadata 順位排序的教會版本，先盤點差異，再以該版本自己的 CRM schema、權限、框架與檔案結構逐步適配。

套件包含：

- [最終整合規格](01-INTEGRATED-SPEC.md)與[依賴矩陣](02-DEPENDENCY-MATRIX.md)。
- [本次 session 可見提示詞歷程](03-PROMPT-HISTORY-VERBATIM.md)。
- [可以逐階段複製的 Prompt Playbook](04-PROMPT-PLAYBOOK.md)。
- [遷移操作手冊](05-MIGRATION-RUNBOOK.md)：包含第一次操作快速開始、可直接複製的第一個 Prompt、Prompt 0～9 關卡、功能遷移／正式部署分界與回復方式；完成後再使用[驗收清單](06-ACCEPTANCE-CHECKLIST.md)記錄直接證據。
- [隱私遮罩說明](07-PRIVACY-REDACTIONS.md)：列出為了跨教會攜帶而移除的個資與機器專屬值類型。
- [11 份原始 Specs](original-specs/)與[11 份原始 Plans](original-plans/)。第 10 份是[欄位順序、姓名寬度與區／小組摘要規格](original-specs/2026-07-17-member-info-column-order-group-metadata-design.md)及其[實作計畫](original-plans/2026-07-17-member-info-column-order-group-metadata.md)；它明確取代較早欄寬／排序規格中的姓名寬度、應用程式 `minWidth` 與欄位順序。第 11 份是[會員身份 metadata 順序規格](original-specs/2026-07-18-member-info-commitment-type-sorting-design.md)及其[實作計畫](original-plans/2026-07-18-member-info-commitment-type-sorting.md)，明確禁止依 raw OptionSet 整數、中文 label 或教會硬編碼清單排序。
- [樹狀功能權威 requirements/context](authoritative-context/)。
- [受控參考實作](reference-implementation/)：功能專屬快照、測試契約與宿主檔案的路徑限定歷史 patch；最新增量見 [`06-member-info-commitment-type-metadata-order.patch`](reference-implementation/host-integration/06-member-info-commitment-type-metadata-order.patch)，一律視為 **EVIDENCE-ONLY**。
- [Manifest](manifest.json)與[套件驗證器](verify-package.ps1)。

## 重要安全警告

1. **不要直接覆蓋目標教會的 Controller、Startup／Program、csproj、Razor 或設定。** 參考程式與 patch 是理解資料流和契約的證據，不是更新安裝程式。
2. **先確認目標 CRM logical name、型別、關聯與 OptionSet。** 欄位不明時停止，不可依中文 label 猜欄位或自行建立同名欄位。
3. **先確認 Church／Shepherd 授權來源。** 不可用前端隱藏、allow-all、角色升級、放寬權限或逐列 CRM 查詢代替伺服器端批次授權。
4. **不要複製秘密或教會資料。** LINE token、CRM 密碼、connection string、真實 GUID、姓名、電話、生日與照片都不應出現在 Prompt、程式、測試或 log。
5. **不要盲目執行 patch。** `reference-implementation/host-integration/*.patch` 跨越特定 Sunny Git 歷史，只用於比對；目標版本不同時直接 `git apply` 很可能覆蓋既有功能。
6. **AI 不得自行 Commit、merge、push、關閉 IDE、kill 程序或部署。** 完成測試與人工驗收後，由使用者決定提交與合併。

## 拖入其他教會專案後的第一個 Prompt

把整個 `member-info-portable-kit/` 資料夾拖入目標 repository 可讀取的文件區，然後把下列文字貼給該 repository 中的 AI。這是「拖入後作為參考資料」的工作流：套件不應加入 application build，也不是可覆蓋宿主檔案的更新包；AI 必須先讀文件與比對差異，再把核准行為適配到目標版本。若實際路徑不同，只修改 `KIT_ROOT` 那一行：

```text
KIT_ROOT = docs/portable/member-info-portable-kit

請先把 KIT_ROOT 視為本套件在目前 repository 中的實際根目錄，並完整閱讀 KIT_ROOT/00-START-HERE.md。若套件不在預設位置，只修改上面的 KIT_ROOT，不要猜本機絕對路徑。

這是其他教會版本的會友資訊遷移工作。此階段只允許讀取與分析：不可修改檔案、不可套 patch、不可安裝套件、不可 Commit、不可假設 Sunny 的 CRM 欄位、權限、DevExtreme、LINE 或照片儲存方式與本專案相同。

KIT_ROOT/reference-implementation/ 只提供行為與整合證據，不是覆蓋來源；不可直接複製宿主檔案或套用其中 patch。

閱讀完成後，請執行 KIT_ROOT/04-PROMPT-PLAYBOOK.md 的「Prompt 0：只讀盤點與差異報告」，先回報 repository root、branch/worktree、dirty files、技術與套件版本、MemberInfo 相關檔案、CRM schema、角色與授權來源、照片/LINE/快取契約、現有測試與阻擋項。本階段不要實作。
```

只有 Prompt 0 的盤點證據完整，且你核准 Prompt 1 的目標版遷移設計後，AI 才能開始修改 application source。

## 閱讀順序

### 給實際操作遷移的人員

1. 先讀[遷移操作手冊](05-MIGRATION-RUNBOOK.md)最前面的快速開始與第一個 Prompt。
2. 把套件放進目標 repository 並完成 verifier 後，依手冊指示讓目標 AI 讀本文件及執行 Prompt 0。
3. Prompt 1 核准後才進入實作；Prompt 9 完成時以[驗收清單](06-ACCEPTANCE-CHECKLIST.md)記錄直接證據。
4. 功能遷移完成不代表已正式上線；Commit、merge、publish、smoke test 與 rollback 使用目標版本自己的核准流程。

### 給第一次接手的 AI

1. 本文件。
2. [Prompt Playbook](04-PROMPT-PLAYBOOK.md) 的 Prompt 0。
3. [整合規格](01-INTEGRATED-SPEC.md)。
4. [依賴矩陣](02-DEPENDENCY-MATRIX.md)。
5. [隱私遮罩說明](07-PRIVACY-REDACTIONS.md)、[遷移手冊](05-MIGRATION-RUNBOOK.md)與[驗收清單](06-ACCEPTANCE-CHECKLIST.md)。
6. 只在需要追溯決策時讀 [提示詞歷程](03-PROMPT-HISTORY-VERBATIM.md)、[原始 Specs](original-specs/)與[原始 Plans](original-plans/)。
7. 只在確定目標差異後讀 [參考實作說明](reference-implementation/README.md)及[宿主來源索引](reference-implementation/host-integration/SOURCE-MAP.md)。

### 規格優先順序

遇到歷史文件差異時，依序採用：

1. 目標教會已核准的遷移設計與安全限制。
2. 本套件的 `01-INTEGRATED-SPEC.md` 最終行為。
3. 日期較晚且明確修正舊決策的 original spec／plan。
4. 歷史提示詞與參考 patch，只用來說明需求如何演進。

例如早期 Plan 曾描述「關係」與「目標」兩欄，後來使用者明確修正為單一「關係目標」欄；必須採用整合規格的最終單欄契約。較早的 resizable/sortable 原始規格所記姓名 96px／應用程式 `minWidth: 80` 也已由第 10 份規格明確取代；最終值是 `width: 62`、fixed left，且應用程式不得設定 `FullName.minWidth`。第 11 份規格再明確修正會員身份排序：`customertypecode` raw 整數只是識別碼，唯一權威是 `PicklistAttributeMetadata.OptionSet.Options` 的客製化集合順序。

## 三種使用方式

### 方式 A：完整分階段遷移（推薦）

依 Prompt 0→9 執行，適合尚未具備完整頭像與新版會友資訊的教會。每一階段先鎖定依賴與測試，再修改最小範圍；上一階段未通過就不進下一階段。

### 方式 B：只適配單一能力

可只做頭像、搜尋、手機、固定身分欄、欄寬／排序、會員身份 metadata 排序、欄位順序、區／小組摘要或明細，但仍必須先執行 Prompt 0 與 Prompt 1，證明該能力的 CRM、權限與前端依賴。固定身分欄不能只加入兩個 `fixed` 屬性，欄寬／排序也不能只複製 DataGrid options；區／小組摘要不能以逐組 CRM 查詢或前端當頁 `Groups.length` 拼湊；metadata 排序不能把 raw 整數或 Sunny label 清單當順位。必須證明固定覆蓋層的手機水平手勢、表頭原生 resize／sort、完整 `GroupCount`、同一筆 list query 的 `GroupTime`／`GroupPlace` 資料流，以及三種表格的 metadata rank／Unknown／Empty／跨頁契約。完成對應 Prompt 後執行 Prompt 9 中相關的完整正向、負向與回歸驗收。

### 方式 C：只讀稽核比較

只執行 Prompt 0，或要求 AI 用整合規格和驗收清單比較現況。此模式永不修改檔案，適合先估算工作量、確認版本差異或審查既有實作。

## 何時必須停止並詢問

AI 遇到以下任一情況必須停止該階段，列出已知證據、未知項目、方案與風險，等待使用者決定：

- CRM entity、logical name、關聯方向、OptionSet 語意或資料型別無法確認。
- Church／Shepherd claim、名單歸屬、contact 讀寫 policy 或快取隔離無法確認。
- 主要照片 storage、上傳權限、快取失效或 LINE token provider 契約不明。
- 目標 DevExtreme／ASP.NET／serializer 版本與參考實作不同，或 fixed-column overlay、表頭 resize／sort 的 DOM／pointer／touch 行為不同，且 API 相容性尚未驗證。
- 需要資料庫／CRM schema、正式秘密、跨模組 API 契約或權限治理的變更。
- 目標 repository 有無法安全保留的重疊變更，或基線 build/test 已失敗。
- 實作似乎需要整份覆蓋大型宿主檔案或大幅超出核准 scope。

「先做一個能跑的寬鬆版本」不是可接受的替代方案。

## 如何驗證套件本身

在套件所在 repository root 執行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File docs/portable/member-info-portable-kit/verify-package.ps1
```

驗證器會檢查 manifest 檔案集合、SHA-256、位元組數、strict UTF-8、U+FFFD 與 Markdown 相對連結。預期 exit code 為 0；任何缺檔、雜湊差異、非法路徑或編碼錯誤都必須先解決。

若套件被放在不同位置，也可以從套件目錄直接執行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\verify-package.ps1
```

## 如何完成遷移

1. 在目標專案建立隔離 branch/worktree，記錄基準 HEAD 與既有 dirty files。
2. 驗證本套件，執行 Prompt 0 取得差異報告。
3. 執行 Prompt 1，審閱並核准目標版遷移設計。
4. 依 Prompt 2～8 的相依順序實作；每階段跑測試、`git diff --check` 與人工操作。
5. 執行 Prompt 9，逐項填寫 `06-ACCEPTANCE-CHECKLIST.md` 的直接證據。
6. 人工檢視所有 application diff，確認沒有秘密、個資、權限放寬或套件輸出進入發佈。
7. 由使用者在實際瀏覽器與手機確認後，自行 Commit、merge 與部署。

完整操作與回復方式請依 [遷移操作手冊](05-MIGRATION-RUNBOOK.md)。

## 來源與版本

- 套件 ID：`member-info-portable-kit`
- 文件範圍起日：2026-07-15（含）
- 來源 branch：`Sunny_5.1.2.WorktreeTuneMemberView`
- 來源 commit：`2406b126e989cc980e8cada9da0e07a2ede1e08d`
- 套件格式版本：1
- 文字編碼：strict UTF-8

精確檔案大小與 SHA-256 以 [manifest.json](manifest.json) 為準。Manifest 不包含自己的雜湊，以避免自我參照；外層 ZIP 的 SHA-256 會記錄在 CCG review 證據中。
