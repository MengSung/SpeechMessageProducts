# 會友資訊可攜式部署套件設計

## 1. 背景與目標

目前 `Sunny_5.1.2.WorktreeTuneMemberView` 已完成一連串會友資訊改版，但其他教會版本可能仍保留舊版列表，甚至尚未具備頭像、LINE 照片、樹狀分組或手機水平滑動。單純複製目前的 Razor 或 Controller 容易覆蓋目標教會既有權限、CRM 欄位與設定，因此本設計採用已核准的「方案 A：雙層文件套件＋參考程式碼快照」。

套件的成功標準不是讓 AI 原封不動套用 Sunny 版本，而是讓 AI 在收到 ZIP 後能自行完成以下流程：

1. 讀懂完整功能與原始決策歷程。
2. 盤點目標教會版本的技術、資料與權限差異。
3. 依安全的相依順序適配實作。
4. 用自動測試與人工操作逐項證明功能完成。

## 2. 已核准方案與替代方案

### 2.1 採用：雙層文件套件＋參考實作

第一層是給人與 AI 閱讀的整合文件，第二層保存原始 Specs、Plans 與受控的參考實作。整合文件提供明確入口與順序；原始資料則讓 AI 在遇到細節時回查權威來源，不需要依賴單一摘要。

### 2.2 未採用：只封裝 Specs、Plans、Prompts

體積較小，但缺少真實 API、DTO、權限與前端生命週期範例，另一個 AI 容易產出表面相似、資料流卻不相容的版本。

### 2.3 未採用：單一 Master Prompt

拖拉後最容易使用，但單一長提示詞會掩蓋來源、順序與差異，且難以驗證是否漏掉權限、照片快取、手機操作與零筆搜尋等需求。

## 3. 套件資訊架構

套件建立於 `docs/portable/member-info-portable-kit/`，並產生 `docs/portable/member-info-portable-kit.zip`：

```text
member-info-portable-kit/
├─ 00-START-HERE.md
├─ 01-INTEGRATED-SPEC.md
├─ 02-DEPENDENCY-MATRIX.md
├─ 03-PROMPT-HISTORY-VERBATIM.md
├─ 04-PROMPT-PLAYBOOK.md
├─ 05-MIGRATION-RUNBOOK.md
├─ 06-ACCEPTANCE-CHECKLIST.md
├─ 07-PRIVACY-REDACTIONS.md
├─ manifest.json
├─ original-specs/
├─ original-plans/
└─ reference-implementation/
   ├─ README.md
   ├─ feature-files/
   ├─ host-integration/
   └─ tests/
```

### 3.1 `00-START-HERE.md`

這是拖入其他教會專案後唯一需要先指定 AI 閱讀的入口。內容包含使用方式、推薦提示詞、閱讀順序、安全警告、適用與不適用情境，以及套件版本來源。

### 3.2 `01-INTEGRATED-SPEC.md`

合併九份 Specs 的最終行為，並補上「尚未有頭像」版本需要的照片前置能力。涵蓋：

- 區長 → 小組 → 會友三層樹。
- Church／Shepherd 權限、批次授權與避免逐列 CRM 查詢。
- PascalCase DTO 與序列化契約。
- 頭像批次載入、主要照片／LINE／性別剪影來源、上傳、快取失效與重新同步 LINE。
- 「關係目標」單一欄位。
- 活潑 Loading、`prefers-reduced-motion` 與錯誤狀態。
- 搜尋按鈕、全頁遮罩、停止搜尋、多筆／零筆結果、結果取代、返回瀏覽。
- 單一水平捲軸、禁用 adaptive 三點彈窗、手機指頭水平滑動。
- 頭像與姓名固定於左側；固定覆蓋層仍可水平滑動，並保留垂直手勢、普通點擊與防誤點。
- 姓名預設 96px／最小 80px；頭像不可調寬／排序，其餘欄位使用原生 widget 調寬，表頭採單欄正反排序並保留 remote 計算欄防護。
- 區長／小組長層級、人數位置、空牧區與未填區長排序。
- 性別、生日與 CRM `Year = 1` 正規化。
- 響應式字級、至少 48px 觸控區及 iOS／LINE WebView 16px 防自動縮放。
- 深入註解、UTF-8 與防回歸測試。

### 3.3 `02-DEPENDENCY-MATRIX.md`

以「能力、Sunny 來源、目標版本檢查、適配方式、失敗風險、驗證證據」六欄描述依賴。至少涵蓋 .NET／DevExtreme 版本、CRM 欄位、權限 claims、`ListManager`、Newtonsoft PascalCase、ImageSharp、LINE 設定、照片儲存、MemoryCache、Razor／jQuery／DevExtreme DataGrid 與測試專案。

### 3.4 `03-PROMPT-HISTORY-VERBATIM.md`

按時間整理本次會友資訊 session 的使用者提示詞；除姓名／組織／機器專屬值的明確安全遮罩外，保留原始文字與意圖，不把後來的修正改寫回早期提示詞。分為：

- 功能與錯誤回報：可供其他版本理解演進。
- 視覺與互動決策：可供 AI 重建需求。
- Git／worktree／重啟／DLL lock：僅作歷程附錄，醒目標示不可直接複製到其他教會。
- Commit 偏好：保留「測試後自行 Commit」的範圍限制。

若平台未提供可匯出的完整 transcript，文件必須明示來源範圍，不得把摘要偽裝成逐字稿；可從本 session 已顯示的使用者訊息逐條收錄。若做安全遮罩，必須明示「除安全遮罩外逐字」，且不得保存原值對照表。

### 3.5 `04-PROMPT-PLAYBOOK.md`

提供可以逐段複製的提示詞，而非一個過長的 Master Prompt：

1. 盤點與差異報告。
2. 遷移設計與風險核准。
3. 頭像基礎能力。
4. 後端權限、DTO 與樹狀 API。
5. 前端樹狀檢視與照片批次載入。
6. 搜尋與 Loading 狀態機。
7. 手機響應式與水平手勢。
8. 會友明細、性別與生日。
9. 完整測試、實際瀏覽器驗收與差異報告。

每段 Prompt 都包含目的、必讀來源、允許修改的範圍、禁止事項、輸出格式、測試命令與停止條件。未通過前一階段時，不得假設成功並繼續覆蓋後續檔案。

### 3.6 `05-MIGRATION-RUNBOOK.md`

教導使用者如何把 ZIP 拖入另一個專案並與 AI 合作。Runbook 以備份／分支 → 解壓／拖拉 → 盤點 → 確認 schema／權限 → 分階段適配 → 測試 → 人工驗收 → 使用者自行 Commit 的順序執行。

### 3.7 `06-ACCEPTANCE-CHECKLIST.md`

提供可勾選的功能、資料、權限、照片、搜尋、Loading、手機、無障礙、效能、UTF-8 與安全驗收。每項要求記錄「測試方式」及「通過證據」，避免只看畫面就宣稱完成。

### 3.8 `manifest.json`

Manifest 使用相對 POSIX 路徑，記錄套件格式版本、來源 branch／commit、文件日期範圍，以及每個檔案的角色、來源路徑、位元組數、SHA-256 與 UTF-8 狀態。為了讓相同內容可重現出 byte-identical manifest，不寫入每次執行都會改變的產生時間；來源日期範圍與 commit 已足以定位版本。Manifest 自身不列入自己的雜湊清單，避免自我參照；ZIP 也不列入套件內 manifest。

### 3.9 `07-PRIVACY-REDACTIONS.md`

說明 Specs／Plans、提示詞、測試與 patch 何以需要一致遮罩，以及泛化 fixture／機器 token 的用途。`sourcePath` 只代表 lineage；經遮罩的交付檔不得宣稱與來源 byte-for-byte 相同，也不得保存可逆的原值 mapping。

## 4. 權威來源清單

### 4.1 Original Plans（9）

1. `docs/superpowers/plans/2026-07-15-member-info-district-group-tree.md`
2. `docs/superpowers/plans/2026-07-15-member-info-loading-animation.md`
3. `docs/superpowers/plans/2026-07-16-member-detail-gender-birthdate.md`
4. `docs/superpowers/plans/2026-07-16-member-info-layout-search.md`
5. `docs/superpowers/plans/2026-07-16-member-info-mobile-responsive-typography.md`
6. `docs/superpowers/plans/2026-07-16-member-info-session-comments-utf8.md`
7. `docs/superpowers/plans/2026-07-16-sort-unassigned-district-last.md`
8. `docs/superpowers/plans/2026-07-17-member-info-fixed-identity-columns.md`
9. `docs/superpowers/plans/2026-07-17-member-info-resizable-sortable-columns.md`

### 4.2 Original Specs（9）

1. `docs/superpowers/specs/2026-07-15-member-info-district-group-tree-design.md`
2. `docs/superpowers/specs/2026-07-15-member-info-loading-animation-design.md`
3. `docs/superpowers/specs/2026-07-16-member-detail-gender-birthdate-design.md`
4. `docs/superpowers/specs/2026-07-16-member-info-layout-search-design.md`
5. `docs/superpowers/specs/2026-07-16-member-info-mobile-responsive-typography-design.md`
6. `docs/superpowers/specs/2026-07-16-member-info-session-comments-utf8-design.md`
7. `docs/superpowers/specs/2026-07-16-sort-unassigned-district-last-design.md`
8. `docs/superpowers/specs/2026-07-17-member-info-fixed-identity-columns-design.md`
9. `docs/superpowers/specs/2026-07-17-member-info-resizable-sortable-columns-design.md`

### 4.3 補充權威資料

- `.ccg/tasks/archive/2026-07/implement-member-info-district-group-tree/requirements.md`
- `.ccg/tasks/archive/2026-07/implement-member-info-district-group-tree/context.jsonl`
- 2026-07-15（含）後的相關 Git commits、既有套件基準 `320ab43851c8`、固定身分欄來源 `b3c50550deefb9cb7031ea938fce592366459448`，以及包含姓名欄調寬／排序的 9／9 套件來源 `b238d96871fdd490a2a0493e27869753e86baae8`。
- 2026-07-15 前已存在但為「尚未有頭像」版本所必需的 ContactAvatar 與照片 API。

## 5. 參考實作邊界

### 5.1 可完整收錄、但須做一致隱私遮罩的功能專屬檔案

- `ChurchReport/Services/MemberInfo/*.cs`
- `ChurchReport/Services/ContactAvatar/ContactAvatarUrl.cs`
- `ChurchReport/Services/ContactAvatar/DefaultAvatarSvg.cs`
- `ChurchReport/ViewModels/MemberInfoDetailViewModel.cs`
- `ChurchReport/ViewModels/MemberInfoTree/*.cs`
- `ChurchReport.MemberInfo.Tests/*.cs`
- `ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj`

快照建立時可先以 SHA-256 證明來源一致；交付前若 fixture 與提示詞中的可能真實姓名／組織重疊，必須在 Specs、Plans、tests 與 patches 中同步泛化。最終 manifest 驗證的是 sanitized derivative，不再宣稱來源 SHA 相同。

### 5.2 只收錄 patch、方法索引或必要摘錄的宿主檔案

- `ChurchReport/Controllers/MemberInfoController.cs`
- `ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml`
- `ChurchReport/Views/MemberInfo/_MemberDetailPopup.cshtml`
- `ChurchReport/Startup.cs`
- `ChurchReport/ChurchReport.csproj`

`MemberInfoController.cs` 目前超過三千行，且包含 LINE token 與 CRM 密碼的設定取用位置；即使沒有硬編碼值，整份複製也會把 Sunny 專屬整合方式誤帶到其他教會。宿主檔案必須附上「不可直接套用」警告，並以方法清單、來源行號、累積 patch 與依賴說明輔助 AI 適配。

### 5.3 永不收錄

- `appsettings*.json`、使用者秘密、環境變數值、publish profiles。
- `bin/`、`obj/`、`.vs/`、執行期 log、dump 與 CRM 匯出資料。
- 真實會友姓名、電話、生日、照片或 LINE ID。
- 任何僅適用目前機器的絕對路徑、連接埠、PID 或 DLL lock 操作指令。

## 6. 遷移資料流

```text
拖入 ZIP
  → AI 只讀 00-START-HERE
  → 盤點目標 repo／branch／技術版本／CRM schema／權限
  → 產出差異矩陣與阻擋項
  → 使用者核准遷移設計
  → 依 Prompt Playbook 分階段實作
  → 每階段測試與 git diff 範圍檢查
  → 瀏覽器與手機驗收
  → 使用者自行 Commit／合併
```

AI 若發現目標版本缺少必要 CRM 欄位、權限來源或 LINE 設定，不得以硬編碼或放寬授權補過；必須停止該階段，列出缺少項目、可選適配方案與影響，等待使用者決定。

## 7. 錯誤處理與回復

- 套件來源缺檔：產生流程立即失敗，不建立看似完整的 ZIP。
- UTF-8 或 U+FFFD 驗證失敗：列出檔案並停止封裝。
- Manifest 與實際檔案不一致：刪除暫存 ZIP 後重新產生；不得交付不一致檔案。
- 目標版本差異不明：Prompt 要求 AI 只產出差異報告，不先修改程式。
- API／CRM 權限不足：顯示可診斷錯誤，不改成全員可讀或逐列繞過授權。
- 前端請求取消：回到搜尋前的瀏覽狀態，不能留下永久遮罩或半成品結果。
- 遷移失敗回復：保留獨立 branch／worktree，透過一般 Git revert／放棄該分支處理，不提供破壞性的 reset 指令。

## 8. 驗證設計

### 8.1 套件結構驗證

- 9 Specs 與 9 Plans 均存在、來源 lineage 明確；隱私遮罩清單完整，manifest SHA-256 對應的是最終 sanitized 交付檔。
- 所有 manifest 檔案存在，所有實際套件檔案均被 manifest 覆蓋（manifest 自身除外）。
- ZIP 解壓結果與來源資料夾的相對路徑、大小及 SHA-256 一致。
- 所有 Markdown 相對連結可解析，不含工作機器絕對路徑。

### 8.2 編碼與安全驗證

- 以嚴格 UTF-8 解碼所有文字檔。
- 掃描 U+FFFD、疑似密鑰、連線字串、真實資料與絕對路徑。
- `git diff --check` 通過。

### 8.3 內容覆蓋驗證

- Integrated Spec 對應九份 Specs 與頭像基礎附錄。
- Prompt History 覆蓋本 session 可取得的所有使用者會友資訊提示詞。
- Prompt Playbook 每階段都有必讀來源、允許範圍、禁止事項、測試與停止條件。
- Acceptance Checklist 覆蓋功能、權限、資料、照片、搜尋、手機、無障礙、效能及錯誤狀態。

### 8.4 參考實作驗證

- 功能專屬快照在初始複製 checkpoint 與 worktree 來源 SHA-256 一致；隱私遮罩後須證明只改泛化 fixture／機器值，並由 manifest 驗證最終 bytes。
- 宿主 patch／摘錄能定位對應方法，但不包含實際秘密值。
- 測試快照可以讓目標 AI 看懂契約；是否可直接執行由依賴矩陣判定，不假設所有教會專案結構相同。

## 9. 外部模型狀態

依 CCG L+ 流程已平行重試 Gemini 與 Claude 分析：Gemini CLI 在 API 呼叫時回傳 HTTP 403；Claude wrapper 產生空的 `--setting-sources` 後以狀態 1 退出。這些結果代表外部交叉分析目前不可用，不得記錄成通過。後續 review 階段會再次平行嘗試；若仍失敗，`review.md` 必須明確標示未取得外部模型意見，並以本機可重現驗證補強，但不能假稱符合雙模型審查。

## 10. 已定案事項

- 採方案 A，包含受控參考程式碼快照。
- 宿主檔案不得直接覆蓋其他教會版本。
- 頭像前置能力即使早於 2026-07-15，也納入部署說明與必要參考。
- 原始文件、提示詞與參考實作對可能識別人員／組織及機器專屬值做一致安全遮罩；不保存原值對照表。
- 套件與 ZIP 放在 `docs/portable/`，所有新文件使用 UTF-8。
- 不代替使用者 Commit。
