# 會友資訊跨教會／跨版本部署操作手冊強化設計

## 1. 背景

`member-info-portable-kit` 已包含完整的 `05-MIGRATION-RUNBOOK.md`，內容涵蓋套件驗證、環境盤點、Prompt 0～8、權限、CRM、前端、手機、測試、回復與移除套件。然而目前手冊從技術背景開始，第一次操作的人仍需要自行從長文整理出「第一步做什麼、何時批准、何時才能正式上線」。

使用者已核准方案 A：不新增內容重複的第二份手冊，而是強化既有 `05-MIGRATION-RUNBOOK.md`，使它同時具備一頁式快速操作與既有技術細節。

## 2. 目標

- 讓未參與 Sunny 版本開發的人，可依單一操作手冊完成套件放置、驗證、AI 差異盤點、遷移設計核准、Prompt 2～7 分階段實作、Prompt 8 驗收、人工確認與正式部署決策。
- 清楚區分：
  - **功能遷移**：在目標 repository 中分析差異並適配會友資訊功能。
  - **正式部署**：完成測試、人工驗收、Commit／merge 後，依目標版本原有 publish／release 流程上線。
- 提供一段可以直接複製的第一個 Prompt，避免操作人員自行簡化後漏掉只讀限制、差異盤點、安全邊界或禁止盲套 patch。
- 保留既有技術深度，不移除 CRM、權限、照片、DevExtreme、手機、回復與驗收細節。

## 3. 非目標

- 不修改任何 application source、測試或執行期設定。
- 不建立自動覆蓋目標版本的 installer、script 或 patch applicator。
- 不讓 ZIP、reference snapshots 或 host patches 直接進入網站 publish output。
- 不假設不同教會的 CRM logical names、權限、LINE、照片儲存或 DevExtreme 版本與 Sunny 相同。
- 不由 AI Commit、merge、push、部署、關閉 IDE 或終止程序。

## 4. 核准的資訊架構

### 4.1 手冊最前段新增「先看這一頁」

在原有背景與詳細步驟前新增快速區塊，依序包含：

1. 本手冊處理的是功能遷移，不是直接正式上線。
2. 操作前必備條件。
3. 八步快速流程：建立隔離工作區、放置套件、驗證、貼第一個 Prompt、核准 Prompt 1、依序執行 Prompt 2～7、執行 Prompt 8、人工決定 Commit／merge／publish。
4. 每一步的完成判定與停止條件。
5. 明確指出不可直接覆蓋宿主檔案或 `git apply` reference patches。

快速區塊只提供操作順序；詳細技術條件仍連結到手冊後續章節與 `06-ACCEPTANCE-CHECKLIST.md`，避免內容分裂。

### 4.2 提供可直接複製的第一個 Prompt

手冊內加入與 `00-START-HERE.md` 語意一致的 Prompt，包含：

- `KIT_ROOT` 可調整相對路徑。
- 完整閱讀 `00-START-HERE.md`。
- 本階段只允許讀取與分析。
- 禁止修改、套 patch、安裝套件、Commit，以及假設 Sunny schema／權限／前端版本相同。
- 要求執行 Prompt 0 並回報 repository、branch/worktree、dirty files、技術版本、MemberInfo 檔案、CRM schema、授權、照片／LINE／快取、測試與阻擋項。

`00-START-HERE.md` 繼續是拖入後的入口；`05-MIGRATION-RUNBOOK.md` 是人員操作的完整權威手冊。兩份 Prompt 的安全語意必須同步，不得一份允許寫入、另一份要求只讀。

### 4.3 新增 Prompt 0～8 操作關卡表

表格欄位固定為：

| 階段 | 目的 | AI 可執行事項 | 操作人員批准點 | 必要證據 |
|---|---|---|---|---|

各階段定義：

- Prompt 0：只讀盤點，不可修改。
- Prompt 1：提出遷移設計，必須取得使用者明確核准。
- Prompt 2：頭像基礎能力。
- Prompt 3：權限、DTO 與樹狀 API。
- Prompt 4：樹狀 UI 與照片批次載入。
- Prompt 5：搜尋與 Loading 狀態機。
- Prompt 6：會友明細、性別、生日、關係目標。
- Prompt 7：手機、欄位、固定欄、調寬、排序、手勢及區／小組摘要。
- Prompt 8：完整測試、瀏覽器／手機驗收與交付報告。

Prompt 0 未完成不得進 Prompt 1；Prompt 1 未核准不得修改 application；Prompt 2～7 任一階段測試未通過不得進下一階段；Prompt 8 未具直接證據不得宣稱遷移完成。

### 4.4 新增「功能遷移完成」與「正式部署」分界

手冊需要明確列出兩個不同完成點：

**功能遷移完成：**

- Prompt 8 適用項目有直接證據。
- build、tests、`git diff --check` 與人工瀏覽器／手機操作通過。
- 無秘密、個資、權限放寬、N+1 或套件輸出混入 application publish。

**正式部署：**

- 由使用者人工檢視 application diff。
- 使用者自行 Commit 與合併至目標發布分支。
- 使用目標教會既有 CI/CD、Visual Studio publish 或核准 release procedure。
- 正式環境設定與秘密由目標環境既有安全管道提供，不從 portable kit 複製。
- 上線後執行 smoke test，失敗時依目標版本既有 rollback procedure 回復。

portable kit 不定義所有教會共用的 publish 指令，因為 publish profile、server、IIS／container、CI/CD 與正式設定均可能不同。

### 4.5 新增操作人員完成／回復檢查

快速檢查至少包含：

- 正確 repository、branch、worktree 與基準 HEAD。
- 套件 verifier exit 0。
- Prompt 0 差異盤點完整。
- Prompt 1 已由使用者核准。
- Prompt 2～7 每階段都有測試與人工確認。
- Prompt 8 與 `06-ACCEPTANCE-CHECKLIST.md` 有直接證據。
- application diff 無套件文件、秘密、個資與範圍外變更。
- 正式部署與 rollback 流程來自目標版本，不使用 Sunny 的機器路徑、port、PID 或秘密。

失敗時應停止累加修補、保存輸出與 diff、回到最早失敗階段修正設計；禁止未經確認執行 `git reset --hard`、遞迴刪除或固定 PID 強制終止。

## 5. 檔案範圍

實作只允許修改或重建：

- `docs/portable/member-info-portable-kit/05-MIGRATION-RUNBOOK.md`
- `docs/portable/member-info-portable-kit/00-START-HERE.md`（只同步手冊定位、閱讀順序與 Prompt 安全語意）
- `docs/portable/member-info-portable-kit/manifest.json`
- `docs/portable/member-info-portable-kit.zip`
- `.ccg/tasks/enhance-member-info-deployment-manual/*`

不修改 `04-PROMPT-PLAYBOOK.md`、`06-ACCEPTANCE-CHECKLIST.md` 或 application source，除非實作時發現直接矛盾；若有矛盾，必須先回到使用者核准，不自行擴大範圍。

## 6. 一致性規則

- `00-START-HERE.md` 負責「拖入後從哪裡開始」。
- `05-MIGRATION-RUNBOOK.md` 負責「操作人員從準備到正式部署決策如何執行」。
- `04-PROMPT-PLAYBOOK.md` 負責每個 Prompt 的完整可複製內容。
- `06-ACCEPTANCE-CHECKLIST.md` 負責逐項證據記錄。
- 歷史 Specs、Plans 與 patches 只供追溯，不得凌駕 `01-INTEGRATED-SPEC.md` 的最終契約。

若相同內容出現在多份文件，安全限制必須一致；詳細技術驗收不在快速區塊重複展開，而以相對連結導向權威章節。

## 7. 驗證設計

完成手冊強化後必須執行：

1. 套件 verifier：檔案集合、bytes、SHA-256、strict UTF-8、U+FFFD 與 Markdown links。
2. 手冊結構檢查：快速開始、第一個 Prompt、Prompt 0～8 表、功能遷移／正式部署分界、完成／回復清單皆存在。
3. Prompt 安全一致性檢查：`00` 與 `05` 都必須包含只讀盤點、禁止盲套 patch、禁止假設 Sunny schema／權限相同。
4. 隱私掃描：真實姓名、教會名稱、hostname、絕對路徑、固定 port／PID、token／password 值不得新增。
5. `git diff --check`。
6. 重建 manifest 與 ZIP，解壓 ZIP 後執行 ZIP 內 verifier。
7. 確認 application source、tests 與 reference patches 沒有變更。

## 8. 驗收標準

- 第一次使用者可在手冊開頭直接找到完整操作順序與第一個 Prompt。
- 手冊明確要求 Prompt 0 只讀、Prompt 1 核准後才能實作。
- Prompt 0～8 每階段都列出目的、批准點及證據。
- 手冊不把功能移植成功誤寫成已正式上線。
- 正式部署只引用目標版本既有 release procedure，不提供假設所有教會通用的危險 publish 指令。
- 既有技術章節、停止條件、人工 gate、回復方式與套件移除規則仍保留。
- Manifest、ZIP、strict UTF-8、Markdown links、隱私掃描與 `git diff --check` 全部通過。

## 9. 外部分析狀態

已依 CCG M 複雜度規則平行呼叫 Gemini 與 Claude：Gemini API 回傳 403「餘額不足」，Claude wrapper exit 1；兩者均未產出分析內容，不記為分析通過。此設計依目前 repository 中 `00`、`04`、`05`、`06` 的直接比對完成。
