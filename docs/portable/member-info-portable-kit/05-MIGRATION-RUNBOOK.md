# 會友資訊跨教會版本遷移操作手冊

## 快速開始：第一次部署照這一頁操作

本套件協助的是「把會友資訊功能遷移到另一個教會或其他版本」，不是把 Sunny 的程式直接覆蓋到正式網站，也不是通用的網站安裝程式。正確流程是先完成 Prompt 0～9、測試與人工驗收，再由操作人員依目標教會原有的 release procedure 決定 Commit、merge 與正式部署。

### 先分清楚兩件事

- **功能遷移：** 在目標 repository 中盤點差異，依該版本自己的 CRM schema、權限、DevExtreme、LINE、照片儲存與檔案結構適配會友資訊功能。
- **正式部署：** 功能遷移全部驗收後，由使用者人工檢查差異、自行 Commit／merge，再使用目標教會原有的 CI/CD、Visual Studio publish 或核准發布流程上線。

portable kit 只負責第一件事的知識、Prompt 與驗收依據，不提供假設所有教會都適用的 IIS、server path、container、publish profile 或正式秘密設定。

### 八步快速流程

| 步驟 | 操作 | 完成判定 | 不得繼續的情況 |
|---:|---|---|---|
| 1 | 在目標 repository 建立隔離 branch／worktree，記錄 repository root、branch、HEAD 與既有 dirty files | 操作位置與基準 commit 可證明，既有變更已列出 | 位於正式發佈資料夾、分支不明或有無法解釋的重疊變更 |
| 2 | 把整個 `member-info-portable-kit/` 放進目標 repository 的文件區，例如 `docs/portable/` | AI 能讀到 `00-START-HERE.md`，套件沒有加入 application build／publish | 只拖入零散檔案、路徑不明或準備直接覆蓋宿主程式 |
| 3 | 執行套件內 `verify-package.ps1` | verifier exit code 0；manifest、SHA-256、strict UTF-8 與 Markdown links 全部通過 | 缺檔、雜湊、編碼或連結失敗 |
| 4 | 貼上下一節的第一個 Prompt，要求 AI 執行 Prompt 0 | AI 只讀盤點並回報版本、檔案、CRM、授權、照片／LINE／快取、測試與阻擋項 | AI 尚未盤點就修改檔案、套 patch 或假設 Sunny 契約相同 |
| 5 | 執行 Prompt 1，審閱目標版本遷移設計 | 使用者明確批准精確檔案、資料流、測試、風險與回復方式 | CRM schema、權限或版本相容性仍未知，或使用者尚未批准 |
| 6 | 依序執行 Prompt 2～8；每一階段先測試、再實作、再驗證 | 每階段的自動測試、`git diff --check` 與人工 gate 都有證據 | 上一階段失敗、範圍擴大、出現權限放寬、N+1、raw OptionSet 誤排序或未處理錯誤 |
| 7 | 執行 Prompt 9 並逐項填寫 `06-ACCEPTANCE-CHECKLIST.md` | 所有適用項目都有可重現的測試輸出、截圖、Network、log 或 diff 證據 | 只寫「看起來正常」、有 Critical 未通過或沒有桌機／手機／跨頁排序證據 |
| 8 | 人工檢查 application diff，再由使用者決定 Commit、merge 與 publish | 套件文件未進 publish、無秘密／個資、正式部署與 rollback 方式來自目標版本 | AI 準備自行提交、使用 Sunny 機器路徑／秘密或直接操作正式環境 |

## 可以直接複製的第一個 Prompt

把整個套件放到目標 repository 後，將以下文字貼給在「目標教會 repository」工作的 AI。若套件路徑不同，只修改第一行的 `KIT_ROOT`：

```text
KIT_ROOT = docs/portable/member-info-portable-kit

請先把 KIT_ROOT 視為本套件在目前 repository 中的實際根目錄，並完整閱讀 KIT_ROOT/00-START-HERE.md。若套件不在預設位置，只修改 KIT_ROOT，不要猜本機絕對路徑。

這是其他教會版本的會友資訊遷移工作。此階段只允許讀取與分析：不可修改檔案、不可套 patch、不可安裝套件、不可 Commit、不可假設 Sunny 的 CRM 欄位、權限、DevExtreme、LINE 或照片儲存方式與本專案相同。

KIT_ROOT/reference-implementation/ 只提供行為與整合證據，不是覆蓋來源；不可直接複製宿主檔案或套用其中 patch。

閱讀完成後，請執行 KIT_ROOT/04-PROMPT-PLAYBOOK.md 的「Prompt 0：只讀盤點與差異報告」，先回報 repository root、branch/worktree、HEAD、dirty files、技術與套件版本、MemberInfo 相關檔案、CRM schema、角色與授權來源、照片／LINE／快取契約、現有測試與阻擋項。本階段不要實作。
```

Prompt 0 的證據不完整時，不要要求 AI「先做一版看看」。先補齊未知依賴，再進 Prompt 1。

## Prompt 0～9 操作關卡表

完整可複製內容位於 [Prompt Playbook](04-PROMPT-PLAYBOOK.md)。操作人員應一次只核准一個階段：

| 階段 | 目的 | AI 可執行事項 | 操作人員批准點 | 必要證據 |
|---|---|---|---|---|
| Prompt 0 | 只讀盤點與差異報告 | 讀取 repository、版本、MemberInfo、CRM、權限、照片／LINE／快取與測試；不可改檔 | 確認盤點沒有重要未知項，或要求補證據 | root／branch／HEAD／dirty files、版本來源、logical names、授權來源、基線測試與阻擋項 |
| Prompt 1 | 目標版遷移設計 | 提出保留行為、精確檔案、資料流、實作順序、測試與回復方案；不可實作 | **使用者必須明確批准書面設計** | 檔案對應、CRM／DTO／權限／前端差異、風險、測試矩陣與 rollback |
| Prompt 2 | 頭像基礎能力 | 實作核准的主要照片、LINE fallback、性別剪影、批次、上傳與快取失效 | 確認照片來源、安全限制與錯誤分類 | 正向／負向測試、批次呼叫、上傳拒絕、快取與 fallback 證據 |
| Prompt 3 | 權限、DTO 與樹狀 API | 實作 Church／Shepherd scope、批次授權、DTO 與樹狀資料 API | 確認非核准 list／contact 無法讀寫 | 授權正負向、PascalCase JSON、malformed GUID、批次查詢與資料契約 |
| Prompt 4 | 樹狀 UI 與照片批次載入 | 實作區長→小組→會友、無小組、展開／收合與照片佇列 | 確認多區、多組與 Ungrouped 都能操作 | 多組展開、重複展開、分頁、無永久 Loading、照片批次 Network 證據 |
| Prompt 5 | 搜尋與 Loading 狀態機 | 實作搜尋按鈕、全頁遮罩、停止、返回、多筆／零筆與錯誤狀態 | 確認取消／返回可回到搜尋前狀態 | 多筆、單筆、零筆、取消、重複搜尋競態、慢速與 API error 證據 |
| Prompt 6 | 會友明細 | 實作正確 contact 明細、關係目標、性別、生日與照片更新 | 確認快速切換不顯示舊資料 | 不同 contact、快速重複開啟、Year=1、未填值、關係目標單欄與上傳更新 |
| Prompt 7 | 手機、欄位及區／小組摘要 | 實作 responsive 字級、固定頭像姓名、九欄順序、調寬、排序、手勢、`GroupCount`、時間／地點 | 確認三種 grid 與真機矩陣全部符合最終規格 | 桌機及 320／390／430／640px、單一水平捲軸、touch、resize／sort、無 N+1 與 metadata 空值證據 |
| Prompt 8 | 會員身份 metadata 順序 | 讀取目標 `OptionSet.Options` configured sequence，實作 rank DTO、local／search sort 與 Ungrouped segments／paging | 確認三種表格只依目標系統客製化順序，Unknown／Empty 正反向置底 | metadata export、provider／sort／count tests、Network selector、25／50／100 跨頁與真實正反向畫面 |
| Prompt 9 | 完整測試與交付報告 | 執行 build、tests、browser／mobile、metadata 排序、隱私、效能與 diff 稽核 | **所有適用驗收有直接證據後，使用者才決定提交與部署** | 已填寫的 `06-ACCEPTANCE-CHECKLIST.md`、測試輸出、截圖、Network、log、diff 與殘餘風險 |

任一階段測試失敗或出現規格外變更時，停在該階段修正；不可用下一階段的新修改掩蓋目前失敗。

## 功能遷移完成不等於正式部署

### 功能遷移完成的條件

- Prompt 9 的所有適用項目都有直接證據。
- 目標版本完整 build、MemberInfo tests 與 `git diff --check` 通過；warning／skip 已分類。
- 桌面與手機完成樹、搜尋、明細、照片、fixed／resize／sort／touch 的人工操作。
- Church／Shepherd 權限正向與負向邊界成立，無逐列授權或逐小組／逐會友 N+1。
- diff 沒有秘密、個資、權限放寬、未核准檔案或 portable kit 文件進入 application publish。

### 正式部署的條件

- 使用者人工檢查全部 application diff，確認目標版本既有功能與設定被保留。
- 由使用者自行 Commit，並依團隊流程合併至目標發布分支。
- 使用該教會既有 CI/CD、Visual Studio publish 或已核准 release procedure；不要複製 Sunny 的 server path、publish profile 或機器設定。
- 正式 secrets 只由目標環境既有安全管道提供，不從 Prompt、portable kit、測試或 log 複製。
- 上線後執行該版本核准的 smoke test；若失敗，使用該版本既有 rollback procedure，不臨時發明破壞性命令。

## 操作人員完成檢查表

- [ ] 已確認正確 repository、branch／worktree、基準 HEAD 與既有 dirty files。
- [ ] portable kit verifier exit 0，manifest、SHA-256、strict UTF-8 與 links 全部通過。
- [ ] Prompt 0 只讀盤點已完成，CRM、權限、照片／LINE、DevExtreme 與測試沒有未處理未知項。
- [ ] Prompt 1 書面遷移設計已由使用者明確批准。
- [ ] Prompt 2～8 每一階段都有自動測試、`git diff --check` 與人工 gate，沒有用後續修改掩蓋前一階段失敗。
- [ ] Prompt 9 已完成，[驗收清單](06-ACCEPTANCE-CHECKLIST.md)所有適用項目都有直接證據。
- [ ] application diff 無秘密、個資、權限放寬、N+1、範圍外檔案或套件內容混入 publish。
- [ ] 正式 Commit／merge／publish、secrets、smoke test 與 rollback 均採用目標版本自己的核准流程。

### 中途失敗時

停止繼續累加修補，保存失敗測試、Network／console、伺服器 log 與 `git diff`，回到最早失敗的 Prompt 階段修正設計。不要自行執行 `git reset --hard`、未驗證路徑的遞迴刪除、固定 PID 強制終止，或直接修改正式 CRM／正式伺服器。

## 1. 這份手冊要解決什麼

這份手冊教你把 `member-info-portable-kit.zip` 拖入另一個尚未具備完整會友資訊樹狀檢視或頭像能力的教會專案，並讓 AI 依序完成「只讀盤點 → 遷移設計 → 分階段實作 → 測試與人工驗收」。套件是參考資料，不是可以直接覆蓋目標專案的更新包。

每個教會版本都可能有不同的 CRM logical name、角色 claim、LINE 設定、DevExtreme 版本與既有頁面，因此任何實作前都必須先取得差異報告。沒有完成盤點，就不要要求 AI「直接照 Sunny 版套上去」。

## 2. 開始前的安全條件

- 確認你操作的是正確的目標教會 repository，不是正在服務正式環境的發佈資料夾。
- 讓 AI 回報目前 repository root、branch、worktree 與 `git status --short`。
- 建立專用 branch 或 linked worktree；保留目標版本原本可正常執行的基準 commit。
- 記錄原有建置與測試命令，先跑一次基線；基線失敗時先區分既有失敗與新問題。
- 不把任何正式 CRM 密碼、LINE token、連線字串、會員資料或照片貼進 Prompt 或套件。
- 不允許 AI 自動 Commit、merge、push、關閉 IDE、終止服務或放寬權限。

## 3. 拖入與驗證套件

### 3.1 建議放置方式

把 ZIP 拖到目標 repository 的文件區，例如 `docs/portable/`，解壓後應看到：

```text
docs/portable/member-info-portable-kit/
├─ 00-START-HERE.md
├─ 01-INTEGRATED-SPEC.md
├─ 02-DEPENDENCY-MATRIX.md
├─ 03-PROMPT-HISTORY-VERBATIM.md
├─ 04-PROMPT-PLAYBOOK.md
├─ 05-MIGRATION-RUNBOOK.md
├─ 06-ACCEPTANCE-CHECKLIST.md
├─ 07-PRIVACY-REDACTIONS.md
├─ original-specs/              # 11 份
├─ original-plans/              # 11 份
├─ manifest.json
└─ reference-implementation/
```

如果解壓工具產生另一層同名資料夾，可以保留；重點是把 `00-START-HERE.md` 的實際相對路徑告訴 AI，不要猜路徑。

### 3.2 驗證套件本身

在目標 repository root 執行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File docs/portable/member-info-portable-kit/verify-package.ps1
```

只有在雜湊、UTF-8、manifest 集合與 Markdown 連結全部通過後才進入盤點。若防毒或公司原則禁止執行 PowerShell，請 AI 以唯讀命令逐項重做 manifest 的檔案大小與 SHA-256 比對，不要因此跳過驗證。

## 4. 給 AI 的第一個 Prompt

把下列文字貼給在「目標教會 repository」工作的 AI，並把路徑改成實際位置：

```text
這是一個尚未實作完整會友資訊樹狀檢視與頭像功能的教會版本。

請先完整閱讀：
docs/portable/member-info-portable-kit/00-START-HERE.md

接著執行 04-PROMPT-PLAYBOOK.md 的「Prompt 0：只讀盤點與差異報告」。

本階段只能讀取與分析，不可修改檔案、不可 Commit、不可套用 patch、不可假設 CRM 欄位或權限與 Sunny 版本相同。請先回報 repository root、branch/worktree、技術版本、相關檔案、CRM schema、角色/授權來源、照片儲存方式、LINE 設定契約、既有測試，以及阻擋項。
```

AI 若在沒有差異報告的情況下直接改檔，請停止它並重新下 Prompt 0。

## 5. 階段一：只讀盤點

要求 AI 至少提供以下證據：

1. 目標 branch、worktree、HEAD 與 dirty files。
2. .NET target、ASP.NET Core、DevExtreme client/server、Newtonsoft、Dataverse SDK、ImageSharp 與測試框架版本；DevExtreme client 必須從 layout 實際載入的 asset／runtime 取證，不能用 server NuGet 代替（Sunny client 為 22.1.6）。
3. 目標 MemberInfo Controller、View、ViewModel、照片服務、Startup/Program、專案檔與測試檔路徑。
4. CRM `contact`、`list`、`listmember` 及牧區、區長、小組、LINE 照片、性別、生日等 logical names 和資料型別；另確認既有小組 descriptor query 是否可在同一筆 list 查詢加入 `new_group_time`、`new_group_place`，以及目標 SDK／權限能否讀取 `contact.customertypecode` 的 `PicklistAttributeMetadata.OptionSet.Options` configured sequence，不得逐小組另查或以 raw value 猜順序。
5. Church／Shepherd 身分來源、可讀名單來源、聯絡人授權邊界與批次查詢方式。
6. 照片來源優先序、上傳位置、快取、預設剪影與 LINE channel token lookup 契約。
7. 現有搜尋、一般小組／Ungrouped／搜尋結果三種 DataGrid、fixed columns、header resize／sort、local／remote datasource、adaptive 行為、水平捲動、pointer／touch、手機字級與詳細彈窗。
8. 既有建置與測試基線，包括失敗項目。

盤點結果應填入 [依賴矩陣](02-DEPENDENCY-MATRIX.md) 的「目標版本必查」與「驗證證據」。缺少證據不代表依賴存在。

## 6. 階段二：遷移設計核准

執行 [Prompt 1](04-PROMPT-PLAYBOOK.md#prompt-1遷移設計與使用者核准) 後，AI 應列出：

- 保留的目標版本既有行為。
- 要新增或修改的精確檔案。
- Sunny 參考檔與目標檔的對應方式。
- CRM schema、DTO、授權與前端契約差異。
- 實作順序、每階段測試、人工驗收與回復方式。
- 需要你決定的選項及其影響。

只有在你明確核准遷移設計後，才可執行 Prompt 2。`reference-implementation/host-integration/*.patch` 只能協助理解歷史差異，不能直接 `git apply`。

最終欄位與摘要行為以[第 10 份 Spec](original-specs/2026-07-17-member-info-column-order-group-metadata-design.md)及其 Plan 為準；會員身份排序再以[第 11 份 Spec](original-specs/2026-07-18-member-info-commitment-type-sorting-design.md)及[第 11 份 Plan](original-plans/2026-07-18-member-info-commitment-type-sorting.md)為準。第 11 份明確禁止依 raw `customertypecode` 整數、label 或 Sunny 清單排序。[Patch 05](reference-implementation/host-integration/05-member-info-column-order-group-metadata.patch)與[Patch 06](reference-implementation/host-integration/06-member-info-commitment-type-metadata-order.patch)均僅為 **EVIDENCE-ONLY**，不得直接套用。

## 7. 階段三：依相依順序實作

依 [Prompt Playbook](04-PROMPT-PLAYBOOK.md) 的 Prompt 2～8 執行，不要把所有需求一次交給 AI：

1. **Prompt 2—頭像基礎能力：** 先建立主要照片、LINE、性別剪影、批次載入、上傳與快取失效的可靠路徑。
2. **Prompt 3—權限、DTO 與樹狀 API：** 建立 Church／Shepherd 範圍、批次授權、PascalCase DTO、區長／小組／會友 API。
3. **Prompt 4—樹狀 UI 與照片批次載入：** 建立三層樹、預設展開、整列點擊、無小組與照片佇列。
4. **Prompt 5—搜尋與 Loading：** 建立可取消搜尋、全頁遮罩、多筆／零筆結果、返回瀏覽及動畫狀態。
5. **Prompt 6—會友明細：** 整合關係目標、性別、生日、照片上傳與重複開啟穩定性。
6. **Prompt 7—手機、欄位與區／小組摘要：** 完成自適應字級、48px 觸控、16px 輸入框、`ContactId` 72px 與 `FullName` 62px fixed left、無應用程式 `FullName.minWidth`、精確九欄順序、Phone「行動電話」置中、Gender 最後、完整 `GroupCount` 與 trim 後獨立 `GroupTime`／`GroupPlace`、DevExtreme 原生 `widget` resize、單欄表頭排序、單一水平捲軸、指頭滑動與取消三點 adaptive popup。
7. **Prompt 8—會員身份 metadata 順序：** 讀取目標 `OptionSet.Options` sequence，建立 `MembershipStatusOrder`／`HasMembershipStatusValue`、Configured／Unknown／Empty 與 Ungrouped aggregate counts／segment slices；三種表格正反向一致，visible 欄仍顯示目標 label。

每階段都要：

- 先寫或確認會失敗的契約測試。
- 只修改核准設計列出的檔案。
- 跑該階段測試及 `git diff --check`。
- 回報實際變更與證據，讓你測試後再進下一階段。
- 不 Commit、不 merge、不 push。

### 7.1 欄位互動的人工 gate

欄位實作不能只以靜態 contract 或桌機畫面宣稱完成。一般小組、Ungrouped、搜尋結果三種 grid 都必須留下以下直接證據：

1. 在桌機以滑鼠、在 320／390／430／640px 真機以單指拖曳姓名及其他資料欄表頭分隔線；`FullName` 初始 62px、可由原生引擎繼續縮放且沒有應用程式 `minWidth`，`ContactId` 頭像保持 72px 且不可調。
2. 檢查三種 grid 的精確順序皆為 `ContactId`、`FullName`、`Phone`、`BirthDate`、`Address`、`SpiritualIdentity`、`MembershipStatus`、`RelationGoals`、`Gender`；Phone 顯示「行動電話」並置中，Gender 最後。輕點資料欄表頭可在 asc／desc 間切換，點另一欄後只保留單欄排序；`RelationGoals` 在一般小組／搜尋可排序，Ungrouped remotePaging 不提供該計算欄排序。
3. 拖曳分隔線不觸發排序，表頭 resize／sort 不誤開姓名明細；點資料列姓名仍開啟正確 contact。
4. 頭像與姓名在調寬後仍 fixed left；fixed rows touch bridge 的 selector 不處理 headers，資料列水平手勢可用且頁面 vertical gesture 不被鎖住。
5. 每個可見 grid 只有一個水平捲軸，`columnHidingEnabled`／adaptive dots 維持關閉；不得啟用 reordering、`nextColumn` 或第二套 header drag。
6. 欄寬與排序欄／方向都只屬目前 grid instance；本 scope 不啟用 DevExtreme `stateStoring`，也不寫入 `localStorage`、`sessionStorage`、server preference／mapping，或跨 grid／page／device 同步。先調寬並排序，再逐一 rebuild、remount、reload：欄寬須回到 72px 頭像、62px 姓名、無應用程式 `FullName.minWidth` 及其他 factory 預設值，先前 sort column／direction 須清除並回到 datasource 核准初始順序。
7. 使用超過 50 個小組的區驗證前端分頁：區長標頭先顯示完整「N 組」，再顯示「本區 N 人」；`N 組` 不隨當頁 `Groups` 減少，獨立 Ungrouped 不計入。
8. 小組名稱、小組長與會友人數無論 metadata 是否缺值都顯示；時間／地點各自 trim 後獨立顯示，只有一項時不留另一個空標籤，兩項皆空白時整個 metadata row 與標籤都不存在。
9. 以 query contract 或呼叫計數證明 `new_group_time`、`new_group_place` 加入既有 `FetchSmallGroupDescriptors` 單一 list query，並經 `GroupTime`／`GroupPlace` descriptor、viewmodel、builder 傳遞；不得出現逐小組 N+1。

## 8. 必須停止並詢問的情況

遇到下列任一情況，AI 應停止該階段並回報選項，而不是自行補值：

- 找不到或無法確認 CRM logical name、型別、關聯方向或選項值。
- 無法確認 `customertypecode` metadata collection order、metadata read privilege、aggregate FetchXML／remote paging 行為，或有人提議以 raw value／中文 label／Sunny 清單代替 configured order。
- Church／Shepherd 的 claim、名單或聯絡人授權來源與 Sunny 不同。
- 目標版本沒有 LINE channel token lookup，或只有正式秘密值而無安全設定介面。
- 照片不是存於 CRM `entityimage`／LINE URL，或既有快取與上傳流程衝突。
- 瀏覽器實際載入的 DevExtreme client 版本無法確認，或不是 Sunny 22.1.6，且 fixed columns、header resize／sort、remote datasource、pointer／touch 與 scrolling／adaptive API 相容性尚未取得桌機及真機證據。
- 必須改資料庫、CRM schema、API 契約、正式設定或跨模組權限才能繼續。
- 基線測試、建置或頁面原本就失敗，無法判斷新舊問題。
- 需要覆蓋 Controller、Startup、Program、設定檔或大量未列入 scope 的程式。

## 9. 完整驗收

執行 Prompt 9，並逐項填寫 [驗收清單](06-ACCEPTANCE-CHECKLIST.md)。畫面看起來正常不是充分證據；至少要同時具備：

- 自動測試與建置結果。
- 權限正向與負向測試。
- 多筆、零筆、取消與返回等搜尋證據。
- 主照片、LINE、剪影、上傳、同步與錯誤情況。
- 一般小組、Ungrouped、搜尋結果在桌機及 320／390／430／640px 的滑鼠／單指欄寬拖曳、頭像不可調、表頭 asc／desc、拖曳不排序、姓名明細不誤開。
- 三種 grid 精確九欄順序、Phone「行動電話」置中、Gender 最後、`FullName` 62／無應用程式 `minWidth`，以及 reload 後回到這些 factory 預設。
- 目標 Dynamics metadata configured sequence、raw value 與 rank 不同的正向案例、Unknown／Empty 正反向置底，以及一般小組、搜尋、Ungrouped 25／50／100 分頁跨 segment 無重複／遺漏。
- 區長完整小組數先於本區人數、跨前端分頁不減少且不含 Ungrouped；小組標題／長／人數常駐，時間／地點 trim 後獨立顯示，兩者皆空時無 metadata row。
- CRM list descriptor 同一查詢帶回 `new_group_time`／`new_group_place`，沒有逐小組 N+1。
- fixed rows touch bridge 不處理 header、單一水平捲軸、資料列水平滑動、頁面 vertical gesture、adaptive false、欄寬與排序狀態在 rebuild／remount／reload 後均不保存，以及 reduced-motion。
- Network／console／伺服器診斷中沒有未處理錯誤。
- `git diff` 僅包含核准的目標應用程式變更，套件本身不混入發佈輸出。

## 10. 回復與重新開始

若遷移結果不符合預期：

1. 停止繼續累加修補。
2. 保存測試輸出、錯誤、Network 回應與 `git diff` 供診斷。
3. 在隔離 branch/worktree 中，用一般 Git revert 或放棄該隔離分支的方式回到基準；實際命令應由 AI 先顯示將影響的路徑並取得你同意。
4. 修正遷移設計，再從最早失敗的 Prompt 階段重做。

不要使用 `git reset --hard`、未驗證路徑的遞迴刪除、固定 PID 的強制終止或直接刪除目標教會資料。

## 11. 完成後移除套件

套件是開發參考資料，不需要隨網站發佈。完成、驗收且自行 Commit 後：

- 先用 `git status --short` 分辨套件檔與應用程式變更。
- 只移除你當初拖入的 ZIP 與 `member-info-portable-kit` 文件目錄。
- 移除前讓 AI 回報解析後的實際路徑，確認它位於目標 repository 的文件區，且不包含應用程式 source。
- 若希望保留稽核歷程，可將套件存放在 repository 外的內部文件庫；不得連同會友資料或秘密值打包。

最終 Commit、merge 與部署時間均由你決定。
