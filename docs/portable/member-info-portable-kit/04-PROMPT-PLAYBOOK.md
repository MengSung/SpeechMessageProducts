# 會友資訊可複製 Prompt Playbook

## 使用方式

把 `member-info-portable-kit/` 完整放入另一個教會版本可讀取的位置，從 Prompt 0 開始逐段複製給負責該目標 repo 的 AI。完整遷移依 Prompt 0 → 9 執行；只遷移單一能力時，仍必須先執行 Prompt 0 與 Prompt 1，取得差異報告及使用者核准，再執行對應功能 Prompt 與 Prompt 9。

這十段 Prompt 都把「目前開啟的 Git repository」定義為目標 repo，把 `member-info-portable-kit/` 定義為參考套件。套件中的 source snapshot、patch、檔名、版本號與 endpoint 是證據，不是可盲目覆蓋或直接套用的來源。若套件放置位置不同，AI 應先在目前 repo 內搜尋 `member-info-portable-kit/00-START-HERE.md`，不得猜測本機絕對路徑。

目前權威清單為 11 份 Specs／11 份 Plans；第 10 份欄位／摘要 Spec 明確取代較早欄寬規格中的 `FullName width: 96`、`minWidth: 80` 與舊欄位順序。新增的第 11 份是 `original-specs/2026-07-18-member-info-commitment-type-sorting-design.md` 與 `original-plans/2026-07-18-member-info-commitment-type-sorting.md`，它進一步規定會員身份只依目標 Dynamics 的 `OptionSet.Options` 客製化集合順序排序，不依 raw value、中文 label 或 Sunny 清單。

## Prompt 0：只讀盤點與差異報告

```text
你正在處理「把會友資訊、頭像、區長→小組→會友樹、搜尋、明細及手機操作遷移到另一個教會版本」的工作。目前開啟的 Git repository 是目標 repo；repo 內的 member-info-portable-kit/ 是參考套件。此階段只能盤點，不能修改任何檔案，也不能執行可能寫入工作區、套件、快取、使用者目錄或外部系統的命令。

【目的】
建立可重現的目標版本現況與差異報告，確認哪些能力已存在、哪些缺少、哪些與套件契約衝突，供使用者決定是否進入遷移設計。

【先讀】
以下參考套件路徑均以 repo 內的 `member-info-portable-kit/` 為根目錄。
1. 在目標 repo 內定位並完整閱讀 `00-START-HERE.md`。
2. 完整閱讀 `01-INTEGRATED-SPEC.md`、`02-DEPENDENCY-MATRIX.md`、`05-MIGRATION-RUNBOOK.md`、`06-ACCEPTANCE-CHECKLIST.md`、`reference-implementation/README.md`、`reference-implementation/host-integration/SOURCE-MAP.md` 與 `manifest.json`。
3. 讀取目標 repo 的 AGENTS.md／README／建置文件／套件清單／序列化設定／權限入口／MemberInfo 相關 controller、service、view model、Razor、JavaScript、CSS 與測試。若檔名不同，以功能與 endpoint 搜尋，不可只依 Sunny 檔名判定不存在。
4. 完整閱讀 `verify-package.ps1`。只有在 `manifest.json` 已存在，且已從腳本原始碼確認「不傳 `-GenerateManifest` 的執行路徑」僅讀取現有檔案、比對 metadata 與計算 SHA-256，不會建立或更新任何檔案時，才可執行這個無參數的套件 metadata 驗證；執行前先在回報說明唯讀判斷依據。無法證明完全唯讀時不執行，只記錄原因與後續驗證命令。

【只允許】
- 讀檔、搜尋，以明確唯讀的命令檢視 Git branch／worktree／status／log／diff，並從現有專案檔、lock file、CI 設定與文件判讀專案及套件版本。
- 從現有檔案查明目標 repo 實際使用的 .NET、ASP.NET MVC、DevExtreme、Newtonsoft 或 System.Text.Json、ImageSharp、CRM SDK、LINE 整合、快取與測試框架版本；不執行可能寫入快取或觸發 implicit restore 的套件管理器或 SDK 命令。
- 盤點 CRM contact、list、listmember、connection 與權限來源的欄位或替代 schema；敏感設定只報「鍵是否存在／來源類型」，不得輸出值。
- 只列出目標 repo 已定義的 build、test、lint／Razor JavaScript 檢查與本機啟動命令，及現有 log／report／CI output 的可追溯結果；本階段不執行這些命令。
- 以回覆中的報告形式列出發現，不建立、更新或格式化任何檔案。

【禁止】
- 禁止 edit、產生檔案、套 patch、安裝或還原套件、restore、build、test、lint、format、generate、Commit、merge、切 branch、建立 worktree、啟停程序、變更資料庫或 CRM。
- 禁止執行可能產生 `bin/`、`obj/`、測試結果、編譯輸出、快取、開發者憑證或任何其他檔案的命令；即使工作區看來不變也不可執行。
- 禁止假定 Sunny 的 branch、絕對路徑、連接埠、PID、net10.0、DevExtreme 21.2.7、CRM 欄位、claim 或 LINE 設定可直接沿用。
- 禁止顯示密碼、token、API key、連線字串、真實會友個資或照片。
- 禁止把 reference-implementation 直接複製到目標 application。

【必須驗證】
- 報告 repo root、目前 branch／worktree、dirty status 與既有使用者變更，但不修改它們。
- 建立能力矩陣，至少包含：導覽與 Church／Shepherd 權限、有效小組判定、CRM schema、PascalCase DTO、樹狀 API、樹 UI、完整區小組數、小組時間／地點、無小組、頭像來源與受保護 API、批次照片、上傳、LINE 同步、快取、搜尋狀態機、Loading、會友明細、性別、生日、單一關係目標欄、精確九欄順序、手機字級、單一水平捲軸、手指滑動、固定頭像與姓名、欄寬調整、表頭單欄排序、測試與瀏覽器驗收能力。
- 先從目標 repo 實際載入的 JavaScript／CSS asset、bundle 設定或執行頁面確認 DevExtreme client 精確版本；不得只看資料夾名稱、伺服器 NuGet 或參考套件版本，也不得盲套任何 host-integration patch。
- 對一般小組、無小組及搜尋結果三種 DataGrid 逐一把目標現況與最終參考契約比對並標示差異：`ContactId` 預設 72px、fixed left、`allowResizing: false`、`allowSorting: false`；`FullName` 預設 62px、fixed left，且 application 不得設定 `minWidth`；九欄依序為 `ContactId`、`FullName`、`Phone`、`BirthDate`、`Address`、`SpiritualIdentity`、`MembershipStatus`、`RelationGoals`、`Gender`；`Phone` caption 為「行動電話」且表頭／資料置中；其餘資料欄使用 `widget` resizing；排序為 `single`；無小組 remote grid 的 `RelationGoals` 必須有禁止遠端排序 guard；拖曳表頭分隔線不得觸發排序；fixed rows touch bridge 不得綁定或攔截 headers。此階段只盤點，不可把參考值直接寫入目標版。
- 盤點區／小組摘要資料流：district 的 `GroupCount` 必須由未經前端分頁裁切的完整實際 Groups 計算，獨立 Ungrouped 不計入；CRM list 的 `new_group_time`、`new_group_place` 必須加入既有單次 list query，不得為每組新增查詢；兩值逐項 trim，皆空時只隱藏時間／地點 metadata 與標籤，小組名稱、小組長與人數仍顯示。
- 列出目標 repo 後續可實際執行的 build、test、lint／Razor JavaScript 檢查與本機啟動命令，並分開標示「只從設定或文件識別」與「有現存輸出證據」；本階段不執行、不觸發 restore，也不產生新輸出。
- 標示套件契約與目標現況的每一項差異：相同、可適配、需使用者決策、阻擋。
- 特別辨識早期規格曾拆分「關係／目標」，最終契約已改為單一「關係目標」欄；不得採用過期拆欄行為。

【停止條件】
- 找不到 00-START-HERE.md、套件驗證失敗、目前位置不是可確認的目標 repo、repo 有無法辨識來源的重疊變更、必要 schema／權限來源無法確認、或讀取會暴露秘密時，立即停止。
- 停止時只能回報已知證據、缺少資訊、風險與需要使用者回答的問題；不得開始修正。

【輸出】
依序輸出：1. 目標環境摘要；2. Git／工作區狀態；3. 依賴與版本表；4. CRM／權限／資料契約表；5. 能力差異矩陣；6. 可執行驗證命令；7. 阻擋項與待決策問題；8. 明確聲明「本階段沒有修改檔案」。不要進入實作。
```

## Prompt 1：遷移設計與使用者核准

```text
你要為目前開啟的目標 Git repository 設計會友資訊遷移。repo 內的 member-info-portable-kit/ 是參考套件。此階段只產出目標版遷移設計並等待使用者核准，不能修改 application source。

【目的】
把只讀盤點所得差異轉成符合目標教會版本的分階段設計，先鎖定權限、schema、檔案所有權、測試與回復方式，再由使用者明確核准是否實作。

【先讀】
以下參考套件路徑均以 repo 內的 `member-info-portable-kit/` 為根目錄。
1. 完整閱讀 `00-START-HERE.md`、`01-INTEGRATED-SPEC.md`、`02-DEPENDENCY-MATRIX.md`、`03-PROMPT-HISTORY-VERBATIM.md`、`05-MIGRATION-RUNBOOK.md` 與 `06-ACCEPTANCE-CHECKLIST.md`。
2. 完整閱讀十一份規格：`original-specs/2026-07-15-member-info-loading-animation-design.md`、`original-specs/2026-07-15-member-info-district-group-tree-design.md`、`original-specs/2026-07-16-sort-unassigned-district-last-design.md`、`original-specs/2026-07-16-member-info-session-comments-utf8-design.md`、`original-specs/2026-07-16-member-info-mobile-responsive-typography-design.md`、`original-specs/2026-07-16-member-info-layout-search-design.md`、`original-specs/2026-07-16-member-detail-gender-birthdate-design.md`、`original-specs/2026-07-17-member-info-fixed-identity-columns-design.md`、`original-specs/2026-07-17-member-info-resizable-sortable-columns-design.md`、`original-specs/2026-07-17-member-info-column-order-group-metadata-design.md` 與 `original-specs/2026-07-18-member-info-commitment-type-sorting-design.md`。
3. 完整閱讀十一份計畫：`original-plans/2026-07-15-member-info-loading-animation.md`、`original-plans/2026-07-15-member-info-district-group-tree.md`、`original-plans/2026-07-16-sort-unassigned-district-last.md`、`original-plans/2026-07-16-member-info-session-comments-utf8.md`、`original-plans/2026-07-16-member-info-mobile-responsive-typography.md`、`original-plans/2026-07-16-member-info-layout-search.md`、`original-plans/2026-07-16-member-detail-gender-birthdate.md`、`original-plans/2026-07-17-member-info-fixed-identity-columns.md`、`original-plans/2026-07-17-member-info-resizable-sortable-columns.md`、`original-plans/2026-07-17-member-info-column-order-group-metadata.md` 與 `original-plans/2026-07-18-member-info-commitment-type-sorting.md`。遇到歷史矛盾時，以 `01-INTEGRATED-SPEC.md` 的最終行為與較晚明確修正為準。
4. 完整閱讀 `reference-implementation/README.md`、`reference-implementation/host-integration/SOURCE-MAP.md`，以及 `reference-implementation/host-integration/01-photo-prerequisite.patch` 至 `06-member-info-commitment-type-metadata-order.patch`。六份 patch 都是 `EVIDENCE-ONLY` 輸入，只能用來理解照片前置、既有 MemberInfo 整合、固定身分欄、欄寬／排序、最終欄位／摘要與 metadata 排序的變更脈絡；絕不可直接 `git apply`、盲目複製或取代目標 repo 的設計。
5. 取得本目標 repo 的 Prompt 0 盤點報告；若對話中沒有逐項差異與證據，先停止並要求執行 Prompt 0。

【只允許】
- 讀取目標 source、測試與 Git 現況。
- 在回覆中提出 2 至 3 個適配方案、取捨與推薦方案；設計必須使用目標 repo 的實際 framework、schema、權限與檔案結構。
- 定義階段順序：頭像基礎 → 權限／DTO／樹 API → 樹 UI／批次照片 → 搜尋／Loading → 明細 → 手機 → 會員身份 metadata 排序 → 完整驗收；若目標依賴不同，可調整但要說明依賴理由。
- 列出每階段精確的預計修改檔案、測試檔、資料契約、endpoint、回復點與成功證據。

【禁止】
- 禁止修改 application source、測試、設定、資料庫、CRM、套件內容，禁止 Commit、merge 或啟停程序。
- 禁止以放寬授權、硬編碼角色／OptionSet、複製秘密、逐列 CRM 查詢或直接覆蓋大型 Controller／Startup／csproj 解決差異。
- 禁止把未知 CRM 欄位、未知 LINE 設定、未知序列化大小寫或未知照片儲存方式寫成已確認事實。
- 禁止在使用者核准前進入 Prompt 2 或任何實作。

【必須驗證】
- 設計明確說明 Church 與 Shepherd 的伺服器端範圍、越權回應、有效 list 條件、contact 批次授權、快取隔離與 fail-closed 行為。
- 提供目標版 DTO／JSON 契約，包含樹骨架、無小組、成員列及搜尋列；說明如何保持一致大小寫。
- 提供資料流與狀態圖：樹骨架不含個資／影像、展開才載入成員、渲染後批次補照片、搜尋取消與返回恢復原 browse 狀態。
- 提供前端可用性設計：區長／小組長層級、空牧區不顯示哨兵字、未填區長排在已填區長後與無小組前、全頁搜尋遮罩、單一水平捲軸、原生手勢、固定頭像與姓名，以及 iOS 聚焦防放大。先從目標 repo 的實際載入資產、套件設定或執行頁面確認 DevExtreme 版本；不得因參考套件使用 22.1.6 就盲套 fixed overlay selector 或 touch bridge。
- 提供最終欄位與組織摘要設計：頭像 72px locked；姓名 62px 且無 application `minWidth`；精確九欄順序與「行動電話」置中；區長顯示完整 `GroupCount` 並排除 Ungrouped；小組時間／地點由既有單次 CRM list query 取得，兩項皆空只隱藏 metadata，不隱藏小組名稱、小組長或人數。
- 提供自動測試、負向測試、實際瀏覽器／手機驗收、UTF-8、安全掃描與 git diff 範圍檢查計畫。

【停止條件】
- Prompt 0 有任何「阻擋」尚未解除、權限或 CRM schema 仍有多種會改變實作的可能、目標 repo 重疊變更無法安全保留、或使用者尚未核准推薦設計時，停止在設計階段。
- 回覆結尾必須向使用者要求「核准、要求修改或拒絕」其中一種明確決定；未取得核准不得修改程式。

【輸出】
輸出：1. 盤點結論摘要；2. 方案比較與推薦；3. 目標架構與資料流；4. 權限／DTO／CRM 映射；5. 分階段檔案清單；6. 測試與瀏覽器驗收；7. 回復方式；8. 尚待使用者決定事項；9. 核准問題。明確聲明本階段沒有修改 application source。
```

## Prompt 2：頭像基礎能力

```text
你要在目前開啟的目標 Git repository 實作會友資訊的頭像基礎能力。repo 內的 member-info-portable-kit/ 是參考證據，不是覆蓋來源。開始前，必須在目前對話或目標 repo 的遷移設計文件中找到使用者已明確核准、且列出目標檔案與 schema 的 Prompt 1 設計；找不到就停止。

【目的】
建立受權限保護、可批次載入、可安全降級及可更新的會友頭像能力，供樹狀表格與明細共用。來源優先順序為 CRM 主要照片、有效 LINE 圖片 URL、依性別產生的預設剪影。

【先讀】
以下參考套件路徑均以 repo 內的 `member-info-portable-kit/` 為根目錄。
1. 閱讀 `01-INTEGRATED-SPEC.md` 的照片前置能力、`02-DEPENDENCY-MATRIX.md` 的照片／LINE／快取列、`06-ACCEPTANCE-CHECKLIST.md` 的照片與負向驗收。
2. 完整閱讀 `reference-implementation/README.md`、`reference-implementation/feature-files/ChurchReport/Services/ContactAvatar/ContactAvatarUrl.cs`、`reference-implementation/feature-files/ChurchReport/Services/ContactAvatar/DefaultAvatarSvg.cs`、`reference-implementation/tests/ChurchReport.MemberInfo.Tests/DefaultAvatarSvgTests.cs`、`reference-implementation/host-integration/SOURCE-MAP.md` 與 `reference-implementation/host-integration/01-photo-prerequisite.patch`。patch 只供比對，不能直接執行套用。
3. 目標 repo 現有登入範圍判定、圖片 API、上傳限制、ImageSharp 或替代影像處理、MemoryCache 或替代快取、LINE 設定來源及測試模式。

【只允許】
- 只修改 Prompt 1 已核准的頭像 service、受保護 controller action、必要 view／JavaScript 整合、依賴宣告與對應測試檔。
- 先建立或調整失敗測試，再寫最小實作；遵守目標 repo 既有命名、注入與錯誤處理模式。
- 實作單張與批次取得時，都先做伺服器端 contact 授權；批次回應只含被授權 id。
- 批次查 CRM、產生縮圖並快取；照片更新後使所有相關縮圖、原圖、前端暫存與來源標記失效。
- 若已核准包含上傳與 LINE 重同步，可使用目標 repo 的設定鍵查詢方式，但只能讀鍵值來源，不得把實際 token 寫入程式、輸出或測試。

【禁止】
- 禁止 Commit、merge、建立或切換 branch／worktree。
- 禁止讓會友資訊直接呼叫未做範圍授權的共用圖片 endpoint，禁止信任前端 contactId，禁止混入未授權圖片。
- 禁止把 Base64／image bytes 放進樹或成員 DTO，禁止每一列各打一個 CRM 圖片查詢，禁止把使用者專屬資料放進共用快取。
- 禁止把 LINE token、連線字串、教會專屬值、真實 GUID、姓名、照片或絕對路徑寫入 source／測試／log。
- 禁止未驗證檔案類型與大小就上傳；禁止用放寬權限處理 403／404／逾時。

【必須驗證】
- 主要照片、有效 LINE URL、男性／女性／未知性別剪影的優先順序與來源標記正確；無照片或 LINE 失效不造成破圖。
- 合法與非法 GUID、無權 contact、混合授權批次、空批次、重複 id、圖片解碼失敗、LINE 403／404／逾時都有明確且不洩漏資料的結果。
- 批次取得以有限次 CRM 查詢完成，快取命中不重查；上傳成功後明細大圖與樹縮圖都刷新。
- 上傳拒絕非影像、超過目標版限制與解碼失敗；必要時測試裁切／補正與 MIME 回應。
- LINE 重同步只對 Prompt 1 核准的 Church 管理範圍開放，能處理封鎖、無照片、重加好友與部分失敗，且不在回覆顯示 token。
- 執行目標 repo 的頭像聚焦測試、MemberInfo 測試、build 與 git diff 範圍檢查，記錄命令、退出碼與摘要。

【停止條件】
- 目標版尚未定義可信 contact 範圍、圖片儲存欄位／服務、LINE 設定來源、允許的影像限制或快取隔離方式時，立即停止並提出選項。
- 測試、build、安全掃描或越權測試失敗時，不得宣稱完成，也不得進入下一階段。

【輸出】
輸出修改檔清單、來源優先序、授權與快取設計、endpoint 契約、測試命令與實際結果、效能證據、尚存風險及 git diff 摘要。最後明確寫「未 Commit，等待使用者檢查或允許進入下一階段」。
```

## Prompt 3：權限、DTO 與樹狀 API

```text
你要在目前開啟的目標 Git repository 實作會友資訊的後端權限、DTO 與區長→小組→會友樹狀 API，包含完整區小組數與小組時間／地點資料。member-info-portable-kit/ 是參考套件。開始前必須確認使用者已核准 Prompt 1 的目標版檔案、CRM schema 與權限設計，且頭像前置能力已通過 Prompt 2 或目標 repo 已有等價證據。

【目的】
建立不含影像的樹骨架、延遲載入小組成員、全教會無小組分頁與授權後搜尋資料契約；區節點攜帶未分頁前的完整實際小組數，小組節點攜帶 CRM list 的時間／地點，同時防止 listId／contactId 越權、結案者 fail-open、N+1 查詢與使用者資料快取外洩。

【先讀】
以下參考套件路徑均以 repo 內的 `member-info-portable-kit/` 為根目錄。
1. 閱讀 `01-INTEGRATED-SPEC.md`、`02-DEPENDENCY-MATRIX.md`、`authoritative-context/requirements.md` 與 `authoritative-context/context.jsonl`。
2. 閱讀 `original-specs/2026-07-15-member-info-district-group-tree-design.md`、`original-specs/2026-07-17-member-info-column-order-group-metadata-design.md`、`original-plans/2026-07-15-member-info-district-group-tree.md` 及 `original-plans/2026-07-17-member-info-column-order-group-metadata.md`；遇到 Tree DTO、搜尋、無小組分頁、Church list 授權、單組展開、完整 `GroupCount` 或小組 metadata 矛盾時，採較新的欄位／摘要 Spec 與 `01-INTEGRATED-SPEC.md` 最終契約。
3. 完整閱讀 `reference-implementation/README.md`、`reference-implementation/feature-files/ChurchReport/Services/MemberInfo/MemberInfoAccess.cs`、`reference-implementation/feature-files/ChurchReport/Services/MemberInfo/MemberInfoAccessResolver.cs`、`reference-implementation/feature-files/ChurchReport/Services/MemberInfo/MemberInfoCurrentContactCounter.cs`、`reference-implementation/feature-files/ChurchReport/Services/MemberInfo/MemberInfoScopeGuard.cs`、`reference-implementation/feature-files/ChurchReport/Services/MemberInfo/MemberInfoTreeSearchBuilder.cs`、`reference-implementation/feature-files/ChurchReport/Services/MemberInfo/RelationGoalFormatter.cs`、`reference-implementation/feature-files/ChurchReport/Services/MemberInfo/DistrictTreeInputs.cs`、`reference-implementation/feature-files/ChurchReport/Services/MemberInfo/DistrictTreeBuilder.cs` 與 `reference-implementation/feature-files/ChurchReport/ViewModels/MemberInfoTree/DistrictTreeViewModels.cs`。
4. 完整閱讀 `reference-implementation/tests/ChurchReport.MemberInfo.Tests/MemberInfoAccessResolverTests.cs`、`reference-implementation/tests/ChurchReport.MemberInfo.Tests/MemberInfoScopeGuardTests.cs`、`reference-implementation/tests/ChurchReport.MemberInfo.Tests/MemberInfoScopeGuardListTests.cs`、`reference-implementation/tests/ChurchReport.MemberInfo.Tests/MemberInfoCurrentContactCounterTests.cs`、`reference-implementation/tests/ChurchReport.MemberInfo.Tests/DistrictTreeBuilderTests.cs`、`reference-implementation/tests/ChurchReport.MemberInfo.Tests/MemberInfoTreeSearchBuilderTests.cs` 與 `reference-implementation/tests/ChurchReport.MemberInfo.Tests/MemberInfoTreeControllerContractTests.cs`。
5. 完整閱讀 `reference-implementation/host-integration/SOURCE-MAP.md`、`reference-implementation/host-integration/02-member-info-2026-07-15-plus.patch` 與 `reference-implementation/host-integration/05-member-info-column-order-group-metadata.patch`；patch 只供比對，不能直接套用。
6. 目標 repo 的權限 claims／職稱、ListManager 或替代來源、CRM paging／chunking、serializer 與既有 error contract。

【只允許】
- 只修改 Prompt 1 核准的 access resolver、scope guard、tree builder、search builder、relation formatter、DTO、controller action、DI／serializer 整合與對應測試。
- 以測試先定義 Church、Shepherd、未知權限、有效 list、越權 list、contact 批次授權、結案 OptionSet 解析失敗、PascalCase、分頁與排序契約。
- 實作 LoadDistrictTree、LoadGroupMembers、LoadUngroupedMembers 及目標版對應的 SearchDistrictTree；action 名稱若因路由慣例不同，保留等價責任並在輸出映射。
- 骨架只含區／組 metadata 與人數；`DistrictNodeViewModel.GroupCount` 或目標版等價欄位由完整 Groups 計算，獨立 Ungrouped 不計入；成員列不得含圖片 bytes。成員資料、授權與 connection 關係文字必須批次取得並支援 CRM PagingCookie／分塊。
- 把 CRM list 的 `new_group_time` 與 `new_group_place` 加入既有單次 list descriptor query，映射到小組 DTO 後逐項 trim；禁止為每個小組另發 Retrieve 或 query。
- Church 完整骨架與 grouped-id snapshot 只有在不含使用者個資時才能短暫共用快取；Shepherd 骨架與搜尋結果不得進共用快取。

【禁止】
- 禁止 Commit、merge、建立或切換 branch／worktree。
- 禁止 Church 對任意非空 list GUID 直接放行；有效小組仍須符合目標版確認的 active、purpose 與 app-named 條件或其核准替代規則。
- 禁止信任前端 listId／contactId，禁止逐列 CRM Retrieve，禁止將搜尋候選資料在批次授權前放入回應。
- 禁止在無法解析「結案」OptionSet 時包含可能已結案者；必須 fail closed 並回傳可診斷錯誤。
- 禁止讓 Shepherd 使用者資料進 Church 共用快取，禁止混用 PascalCase 與 lower camel Tree DTO。
- 禁止把「關係」與「目標」拆成兩個表格欄位；成員列最終契約使用單一 RelationGoals／「關係目標」值。

【必須驗證】
- Church 與 Shepherd 都由伺服器算出的有效可見 list 集合驗證 requested listId；Shepherd 集合是有效 list 與登入者 list 集合的交集。
- Group、Search、Ungrouped 的 contact 都通過 chunked batch authorization；未知 access、越權 list、非 Church 存取 Ungrouped 回 403 或目標版一致的拒絕契約。
- DistrictTree DTO 具有 Districts、Church 才有物件值的 Ungrouped、Scope；成員列具有 ContactId、FullName、Gender、nullable BirthDate、Phone、SpiritualIdentity、Address、MembershipStatus、RelationGoals，且 JSON 大小寫一致。
- 每個 district 的 `GroupCount` 等於未經前端分頁裁切的完整實際小組數且不包含獨立 Ungrouped；小組 DTO 的 `GroupTime`、`GroupPlace` 或目標版等價欄位來自既有單次 CRM list query，沒有 N+1。
- 人數只計在籍非結案且區內跨組去重；全教會無小組永遠存在但可為 0，Shepherd 不出現；無小組在 CRM 條件與排除已分組後先伺服器分頁，再只組裝當頁 row。
- 搜尋只接受核准欄位，回應完整授權後 Rows 與必要的 MatchingListIds／HasUngrouped 相容 metadata；結果去重、穩定排序，零筆與多筆正確。
- 未填區長排在所有已填區長後；Ungrouped 是獨立節點，因此前端會排在未填區長後。空牧區資料不得被後端強制變成「未填牧區」顯示字。
- 執行純邏輯測試、controller／契約測試、完整 MemberInfo 測試、build 與 git diff 範圍檢查，記錄實際輸出。

【停止條件】
- 有效小組條件、結案 OptionSet、Church／Shepherd 權限來源、serializer 大小寫、CRM paging 或無小組定義未獲確認時停止。
- 任一越權負向測試、快取隔離測試、DTO 契約測試、效能批次證據或 build 失敗時停止，不得用前端過濾掩蓋後端問題。

【輸出】
輸出 endpoint／DTO 契約、權限矩陣、CRM 查詢與批次策略、快取鍵與隔離範圍、修改檔清單、測試命令及實際結果、失敗／降級行為與 git diff 摘要。最後聲明「未 Commit」。
```

## Prompt 4：樹狀 UI 與照片批次載入

```text
你要在目前開啟的目標 Git repository 建立會友資訊的區長→小組→會友樹狀 UI，顯示完整區小組數與條件式小組時間／地點，並整合已授權的批次頭像載入。member-info-portable-kit/ 是參考套件。開始前要確認 Prompt 1 設計已獲使用者核准，Prompt 2 頭像與 Prompt 3 樹 API 已有測試通過證據；若目標 repo 原本就有等價能力，先列出等價證據。

【目的】
以大觸控折疊列呈現區長與小組，只有展開小組或無小組時才建立會友 DataGrid、載入成員並批次補照片，避免頁面初次載入所有個資與圖片。

【先讀】
以下參考套件路徑均以 repo 內的 `member-info-portable-kit/` 為根目錄。
1. 閱讀 `01-INTEGRATED-SPEC.md` 的樹 UI、組織標頭、頭像與排序章節，以及 `02-DEPENDENCY-MATRIX.md` 與 `06-ACCEPTANCE-CHECKLIST.md`。
2. 閱讀 `original-specs/2026-07-15-member-info-district-group-tree-design.md`、`original-specs/2026-07-16-member-info-layout-search-design.md`、`original-specs/2026-07-16-sort-unassigned-district-last-design.md` 與 `original-specs/2026-07-17-member-info-column-order-group-metadata-design.md`。
3. 閱讀 `original-plans/2026-07-15-member-info-district-group-tree.md`、`original-plans/2026-07-16-member-info-layout-search.md`、`original-plans/2026-07-16-sort-unassigned-district-last.md` 與 `original-plans/2026-07-17-member-info-column-order-group-metadata.md`。
4. 完整閱讀 `reference-implementation/README.md`、`reference-implementation/host-integration/SOURCE-MAP.md`、`reference-implementation/host-integration/02-member-info-2026-07-15-plus.patch`、`reference-implementation/host-integration/05-member-info-column-order-group-metadata.patch`、`reference-implementation/feature-files/ChurchReport/ViewModels/MemberInfoTree/DistrictTreeViewModels.cs` 與 `reference-implementation/tests/ChurchReport.MemberInfo.Tests/MemberInfoTreeViewContractTests.cs`；patch 只供比對，不能直接套用。
5. 目標 repo 的 Razor／template、DevExtreme DataGrid 初始化、AJAX、popup 與 avatar cache／來源徽章現況。

【只允許】
- 只修改 Prompt 1 核准的 MemberInfo page、必要 partial／style／script 與 view contract／browser tests。
- 區節點預設展開、小組成員預設收合；整個區列與小組列都可點，並保留足夠大的展開提示及 aria-expanded。
- 整個可見範圍只有一個小組時自動展開該小組，不受 Church 的無小組節點存在與否影響。
- Church 在 Districts 後渲染獨立的無小組節點並預設收合；Shepherd 不渲染無小組。
- 小組展開後才呼叫成員 API、建立共用欄位 DataGrid，完成 row render 後將可見 contactIds 送到受保護的批次頭像 API；上傳後可刷新所有同一 contact 的可見縮圖。
- 區長標頭在「本區 N 人」之前顯示後端提供的完整 `GroupCount + ' 組'`；前端小組分頁複製 district 時保留 `GroupCount`，不得用當頁 `district.Groups.length` 重算。
- 小組標頭逐項 trim `GroupTime`／`GroupPlace`；至少一項有值才依「小組時間、小組地點」順序顯示已有項目，兩項皆空則整列與兩個標籤都不渲染，但小組名稱、小組長與人數必須保留。

【禁止】
- 禁止 Commit、merge、建立或切換 branch／worktree。
- 禁止一次載入所有小組成員或圖片，禁止把每列 img 直接指向會造成逐列 CRM 查詢的 endpoint，禁止把 image bytes 放進 row DTO。
- 禁止讓第三層出現分開的「關係」與「目標」欄；最終九欄依序為頭像、姓名、行動電話、生日、地址、信仰狀態、會員身份、關係目標、性別，且行動電話表頭／資料置中。
- 禁止把空牧區顯示成「(未填牧區)」或其他哨兵；有牧區名稱才渲染。
- 禁止在 Razor／JavaScript 重新破壞後端的未填區長排序；畫面順序必須是已填區長、區長未填、無小組。
- 禁止用 innerHTML 輸出 CRM 文字；使用 textContent 或框架安全文字 binding。

【必須驗證】
- 初次載入只呼叫樹骨架，不取成員與頭像；展開不同小組只載入該組，重複展開不重建或重複請求，除非明確失效。
- District header 顯示可選牧區、較醒目的區長姓名，並依序顯示完整「N 組」與「本區 N 人」；Group header 顯示小組名稱、小組長與緊鄰的「N 人」，並依空值規則顯示小組時間／地點，視覺層級可區分。
- 每個區、小組、空資料與 API 錯誤都能恢復或重試，不會永久停在載入中；重複開合保持 aria-expanded 與 DOM 狀態一致。
- 成員姓名能開啟該 contact 的受保護明細；生日、OptionSet 與關係目標依 DTO 顯示，CRM 文字不會造成 XSS。
- 批次照片只回授權 id，來源徽章與主要照片／LINE／剪影一致，快取命中與上傳後刷新可觀察。
- 執行 View contract、JavaScript 語法、MemberInfo 測試、build，並在桌機瀏覽器驗證多區、多組、單組、空組、無小組與重複展開。

【停止條件】
- Prompt 3 的 DTO／路由／大小寫與目標前端不一致、受保護批次頭像尚未可用、重疊使用者變更無法保留、或任一樹／授權／批次載入測試失敗時停止。
- 不得以硬編碼範例人名、固定 GUID 或跳過授權來做出看似可用畫面。

【輸出】
輸出 UI 結構、資料請求時序、欄位與文字安全策略、批次照片流程、修改檔清單、測試與瀏覽器驗證證據、錯誤狀態、效能觀察及 git diff 摘要。最後聲明「未 Commit」。
```

## Prompt 5：搜尋與 Loading 狀態機

```text
你要在目前開啟的目標 Git repository 實作會友資訊搜尋與 Loading 狀態機。member-info-portable-kit/ 是參考套件。開始前必須確認 Prompt 1 設計已核准，受授權的搜尋 API 與樹 UI 已存在且有測試證據；不得以純前端過濾取代伺服器授權搜尋。

【目的】
讓搜尋有明確的 idle、searching、results、error 狀態，搜尋期間以全頁遮罩及「停止搜尋」提供回饋，完成後用多筆／單筆／零筆結果表格取代樹，取消或返回時完整恢復搜尋前瀏覽狀態；所有長等待入口使用友善 Loading 動畫。

【先讀】
以下參考套件路徑均以 repo 內的 `member-info-portable-kit/` 為根目錄。
1. 閱讀 `01-INTEGRATED-SPEC.md` 的搜尋、Loading、錯誤與無障礙章節，以及 `02-DEPENDENCY-MATRIX.md` 與 `06-ACCEPTANCE-CHECKLIST.md`。
2. 閱讀 `original-specs/2026-07-15-member-info-loading-animation-design.md` 與 `original-specs/2026-07-16-member-info-layout-search-design.md`。
3. 閱讀 `original-plans/2026-07-15-member-info-loading-animation.md` 與 `original-plans/2026-07-16-member-info-layout-search.md`。
4. 完整閱讀 `reference-implementation/README.md`、`reference-implementation/host-integration/SOURCE-MAP.md`、`reference-implementation/host-integration/02-member-info-2026-07-15-plus.patch`、`reference-implementation/tests/ChurchReport.MemberInfo.Tests/MemberInfoTreeViewContractTests.cs`、`reference-implementation/tests/ChurchReport.MemberInfo.Tests/MemberInfoTreeSearchBuilderTests.cs` 與 `reference-implementation/tests/ChurchReport.MemberInfo.Tests/MemberInfoTreeControllerContractTests.cs`；patch 只供比對，不能直接套用。
5. 目標 repo 的 SearchDistrictTree 或等價授權搜尋 endpoint、AJAX abort／cancellation、DataGrid dispose、timeout 與錯誤顯示慣例。

【只允許】
- 只修改 Prompt 1 核准的搜尋 API 適配、MemberInfo page／partial 的 CSS／JavaScript／HTML、共用 Loading 元件與對應 tests。
- 搜尋按鈕置於重新同步 LINE 左側；輸入框 Enter 與按鈕共用同一搜尋入口。
- 搜尋開始前保存目前 browse DOM、頁次、展開與表格狀態；searching 時遮罩覆蓋整個會友資訊頁面，原搜尋按鈕升到遮罩上方並轉成紅色、具圖示的「停止搜尋」。
- 停止搜尋要 abort 目前 request、忽略較晚回來的舊 response、先恢復 UI 再安全 dispose 搜尋 grid；完成後「返回會友資訊」使用同一恢復流程。
- results 直接使用授權後完整 Rows 建立與會友表相同欄位的 DataGrid；一筆、多筆、零筆都是完成狀態。
- 初始樹、小組成員及明細三種等待入口使用共用友善卡片：柔和視覺、三顆依序動態圓點、主文案與說明；超過 6 秒更新文案，表明資料仍在傳送且畫面沒有當掉。

【禁止】
- 禁止 Commit、merge、建立或切換 branch／worktree。
- 禁止用彈窗取代已核准的搜尋結果表格，禁止搜尋結果附加在原樹下方，禁止完成搜尋後重新載入樹而遺失原頁次與展開狀態。
- 禁止只比對區／組標題，禁止在授權前回傳候選 contact，禁止讓舊 response 覆蓋新的搜尋或取消結果。
- 禁止讓遮罩永久留存、在取消時顯示錯誤、或把「停止搜尋」藏在不可點擊遮罩下。
- 禁止新增第三方動畫依賴；禁止讓動畫忽略 prefers-reduced-motion 或讀屏重複朗讀裝飾元素。

【必須驗證】
- idle → searching → results／error → idle 的每條轉換、快速連續搜尋、取消、timeout、返回、重複搜尋與舊 response race 都有測試或可重現證據。
- 搜尋一筆、多筆、零筆、無權限與一般失敗文案正確；零筆明確顯示「沒有搜尋到符合的會友」並可返回。
- 遮罩覆蓋整個會友資訊頁面，「停止搜尋」是搜尋期間唯一可操作的頁面控制項；停止後恢復搜尋前狀態。
- Loading 元件有 role="status"、aria-live="polite"、aria-atomic="true"；裝飾動畫 aria-hidden="true"；reduced-motion 會停止動畫但保留文字；最小高度避免版面跳動。
- 搜尋結果表沿用單一「關係目標」欄、受保護批次照片與姓名明細，不含 adaptive 三點欄。
- 執行搜尋 builder／controller tests、View contract、JavaScript 語法、MemberInfo 測試與 build；在實際瀏覽器節流網路，確認超過 6 秒文案、取消與返回。

【停止條件】
- 搜尋 API 尚未保證授權後完整 Rows、目標框架不支援可取消請求且替代設計未核准、原 browse 狀態無法無損保存、或任何 race／返回／遮罩測試失敗時停止。
- 不得把取消失敗包裝成成功，不得留下半成品 results 或永久 Loading。

【輸出】
輸出狀態轉換表、request token／abort 與 DOM 恢復流程、Loading 無障礙行為、修改檔清單、自動測試與瀏覽器證據、失敗案例及 git diff 摘要。最後聲明「未 Commit」。
```

## Prompt 6：會友明細、性別、生日與關係目標

```text
你要在目前開啟的目標 Git repository 完成會友唯讀明細、性別、生日與關係目標顯示。member-info-portable-kit/ 是參考套件。開始前必須確認 Prompt 1 設計已核准，contact 授權與樹／搜尋資料契約已有通過證據。

【目的】
讓使用者從樹或搜尋結果點姓名後，可靠地打開該 contact 的受保護明細；基本資料顯示性別與生日，表格及明細都遵守單一「關係目標」概念，並保留既有聚會／裝備紀錄與照片更新能力。

【先讀】
以下參考套件路徑均以 repo 內的 `member-info-portable-kit/` 為根目錄。
1. 閱讀 `01-INTEGRATED-SPEC.md` 的明細、性別／生日、關係目標與權限章節，以及 `02-DEPENDENCY-MATRIX.md` 及 `06-ACCEPTANCE-CHECKLIST.md`。
2. 閱讀 `original-specs/2026-07-16-member-detail-gender-birthdate-design.md` 與 `original-specs/2026-07-15-member-info-district-group-tree-design.md`，以及 `original-plans/2026-07-16-member-detail-gender-birthdate.md` 與 `original-plans/2026-07-15-member-info-district-group-tree.md`；早期拆分關係／目標的文字已被後期需求取代，最終表格只能有單一「關係目標」欄。
3. 完整閱讀 `reference-implementation/README.md`、`reference-implementation/feature-files/ChurchReport/ViewModels/MemberInfoDetailViewModel.cs`、`reference-implementation/feature-files/ChurchReport/Services/MemberInfo/RelationGoalFormatter.cs`、`reference-implementation/feature-files/ChurchReport/ViewModels/MemberInfoTree/DistrictTreeViewModels.cs`、`reference-implementation/tests/ChurchReport.MemberInfo.Tests/MemberInfoDetailContractTests.cs`、`reference-implementation/tests/ChurchReport.MemberInfo.Tests/RelationGoalFormatterTests.cs`、`reference-implementation/host-integration/SOURCE-MAP.md` 與 `reference-implementation/host-integration/02-member-info-2026-07-15-plus.patch`；patch 只供比對，不能直接套用。
4. 目標 repo 的 Detail 授權、CRM OptionSet metadata、birthdate 讀取、connection 雙向關係、popup、聚會／裝備子網格與上傳後 avatar refresh 流程。

【只允許】
- 只修改 Prompt 1 核准的 detail view model、controller Detail／relation query、detail partial、共用 formatter 與對應 tests。
- 在既有 contact Retrieve 欄位加入 gender 與 birthdate，不額外增加可避免的單筆 CRM 往返。
- 性別使用目標 CRM OptionSet metadata 顯示文字，不硬編碼數值；生日使用 nullable 語意，CRM sentinel Year = 1 或其他已確認無效年份要正規化為 null。
- 明細生日固定顯示 yyyy/MM/dd，空性別或生日顯示「（未設定）」；不做不必要的時區轉換。
- relation／connection 以批次或明細單次安全查詢取得，方向與角色映射依目標 CRM 實際 metadata 核對；表格使用單一 RelationGoals 字串，明細可用一組「關係目標」項目呈現。

【禁止】
- 禁止 Commit、merge、建立或切換 branch／worktree。
- 禁止新增性別或生日編輯功能，禁止更改 Detail 既有授權範圍，禁止信任前端 contactId。
- 禁止把 gender OptionSet 整數直接當文字，禁止顯示 CRM Year = 1，禁止對日期做造成前一天／後一天的時區轉換。
- 禁止把列表欄位拆成「關係」與「目標」，禁止因 connection 不可用就讓整個明細失敗或洩漏其他 contact。
- 禁止破壞既有聚會／裝備紀錄只查當前 contact 的範圍；禁止把其他人的子網格資料帶入彈窗。

【必須驗證】
- 有值、空值、未知 OptionSet、Year = 1、一般有效生日、malformed／unauthorized contactId 都有測試；Detail 越權回 403 或目標版一致的拒絕結果。
- ViewModel、CRM ColumnSet／query、映射與 Razor 都包含 Gender、nullable BirthDate；明細顯示位置在基本資料中且手機版不破版。
- 樹、小組、無小組與搜尋結果都只有單一「關係目標」欄，內容去重、穩定、有明確分隔，沒有錯位的角色／對象配對。
- 有／無 connection、雙向 connection、權限不足與查詢失敗能安全降級；錯誤不得吞掉授權問題。
- 反覆開啟 A、B 兩位會友時，基本資料、照片、聚會與裝備紀錄各自獨立，不沿用前一位快取內容。
- 執行 detail／formatter／View contract tests、完整 MemberInfo 測試、build 與實際瀏覽器明細驗收，記錄證據。

【停止條件】
- 目標 CRM 的 gender、birthdate、connection 角色方向或 contact 授權無法確認，或現有明細可編輯行為與核准的唯讀邊界衝突時停止並請使用者選擇。
- 任一 IDOR、日期、關係目標對齊、重複開窗或 build 測試失敗時停止。

【輸出】
輸出欄位映射、日期正規化規則、關係目標格式、授權與安全降級、修改檔清單、測試／瀏覽器證據及 git diff 摘要。最後聲明「未 Commit」。
```

## Prompt 7：手機響應式、最終欄位順序、區／小組摘要、固定欄、調寬、排序與手勢

```text
你要在目前開啟的目標 Git repository 完成會友資訊的最終九欄順序、62px 姓名欄、區／小組摘要，以及 320 至 640 CSS px 手機與窄螢幕的字體、觸控、工具列及表格水平操作；水平捲動後頭像與姓名仍可見，頭像以外欄位可調寬且表頭可作單欄排序。member-info-portable-kit/ 是參考套件。開始前必須確認 Prompt 1 設計已核准，而且 Prompt 3／4 的樹 DTO、CRM list metadata 與 UI 已存在或有等價證據。

【目的】
依可用寬度平滑調整字級、行高、內距與觸控目標，維持工具列同列，讓完整會友表格只有一條水平捲軸並可直接用手指左右滑動；一般小組、無小組與搜尋結果三種 DataGrid 共用精確九欄 factory，只把頭像與姓名固定在左側，頭像維持 72px locked、姓名採 62px 且不設 application `minWidth`，並提供原生 `widget` 欄寬調整與 `single` 排序。區長顯示完整小組數，小組依空值規則顯示時間／地點；不得出現 DevExtreme adaptive 三點彈窗，也不得因聚焦搜尋框而自動放大。

【先讀】
以下參考套件路徑均以 repo 內的 `member-info-portable-kit/` 為根目錄。
1. 閱讀 `01-INTEGRATED-SPEC.md` 的手機、字體、表格與無障礙章節，以及 `02-DEPENDENCY-MATRIX.md` 及 `06-ACCEPTANCE-CHECKLIST.md`。
2. 閱讀 `original-specs/2026-07-16-member-info-mobile-responsive-typography-design.md`、`original-specs/2026-07-16-member-info-layout-search-design.md`、`original-specs/2026-07-17-member-info-fixed-identity-columns-design.md`、`original-specs/2026-07-17-member-info-resizable-sortable-columns-design.md` 與 `original-specs/2026-07-17-member-info-column-order-group-metadata-design.md`，以及 `original-plans/2026-07-16-member-info-mobile-responsive-typography.md`、`original-plans/2026-07-16-member-info-layout-search.md`、`original-plans/2026-07-17-member-info-fixed-identity-columns.md`、`original-plans/2026-07-17-member-info-resizable-sortable-columns.md` 與 `original-plans/2026-07-17-member-info-column-order-group-metadata.md`。最新欄位／摘要 Spec 取代舊 `FullName width: 96`、`minWidth: 80` 與舊欄位順序。
3. 完整閱讀 `reference-implementation/README.md`、`reference-implementation/host-integration/SOURCE-MAP.md`，以及 `reference-implementation/host-integration/01-photo-prerequisite.patch`、`reference-implementation/host-integration/02-member-info-2026-07-15-plus.patch`、`reference-implementation/host-integration/03-member-info-fixed-identity-columns.patch`、`reference-implementation/host-integration/04-member-info-resizable-sortable-columns.patch`、`reference-implementation/host-integration/05-member-info-column-order-group-metadata.patch` 與 `reference-implementation/tests/ChurchReport.MemberInfo.Tests/MemberInfoTreeViewContractTests.cs`。五份 patch 都是 `EVIDENCE-ONLY` 輸入，只供比對照片前置、既有 MemberInfo 整合、固定身分欄、欄寬／排序及最終欄位順序／區小組摘要契約；絕不可直接 `git apply`、盲目複製或取代依目標 DevExtreme client 版本核准的最小實作。
4. 目標 repo 的 Bootstrap 根字級、viewport meta、DevExtreme scrolling／column hiding、page overflow、LINE WebView 與既有 mobile media query。必須從實際載入的 DevExtreme JavaScript／CSS、package／bundle 設定或執行頁面確認精確版本；不能從資料夾名稱或參考套件版本推定，也不能未驗證就套用 22.1.6 的 DOM selector 與事件轉接方式。

【只允許】
- 只修改 Prompt 1 核准的 MemberInfo CSS／Razor／JavaScript 與對應 View／browser tests；桌機資料與 API 行為不變。
- 把行動版規則限制在 max-width: 640px 或目標版核准的等價範圍，優先使用 rem 上下限搭配 clamp() 與少量 vw，保留瀏覽器文字縮放。
- 區長字級約由 18px 平滑到 20px，小組名稱／小組長約 16px 到 18px，DataGrid 列約 15px 到 16px、表頭約 16px 到 17px；最終值依目標 repo 根字級計算並以實機可讀性驗證。
- 搜尋／同步／返回控制項 min-height 48px、展開箭頭至少 44×44px；區長與小組列使用 min-height，不用固定高度截斷 200% 文字。
- 工具列 flex-wrap: nowrap，讓搜尋框承擔縮減；搜尋與「重新同步LINE」不換行。搜尋 input 的 computed font-size 在手機必須至少 16px；若 Bootstrap 3 將 html 設為 10px，不可用 1rem 假裝達到 16px。
- 先從目標 repo 實際載入的 JavaScript／CSS asset、bundle 設定或執行頁面確認 DevExtreme client 精確版本，再依該版本文件、DOM 與實際 pointer／touch 行為選擇等價設定；不可從資料夾名稱、伺服器 NuGet 或參考套件版本推定。
- 三種 DataGrid 使用同一個真正水平 scrolling owner；DevExtreme 採 `columnHidingEnabled: false`、`columnAutoWidth: false`、無 `hidingPriority`、`useNative: true`、`scrollByContent: true` 或目標版本等價設定。
- 一般小組、無小組與搜尋結果三種 DataGrid 必須共用同一欄位工廠，九欄順序精確為：`ContactId` 頭像、`FullName` 姓名、`Phone` 行動電話、`BirthDate` 生日、`Address` 地址、`SpiritualIdentity` 信仰狀態、`MembershipStatus` 會員身份、`RelationGoals` 關係目標、`Gender` 性別。`Phone` caption 必須是「行動電話」且表頭／資料置中；`Gender` 必須最後。
- `ContactId` 頭像為 72px、`fixed: true`、`fixedPosition: 'left'`、`allowResizing: false`、`allowSorting: false`；`FullName` 姓名為 62px、`fixed: true`、`fixedPosition: 'left'`，不得設定 application `minWidth`。頭像以外資料欄可依原生 `widget` 模式調寬；除頭像與姓名外其他欄位不得 fixed。
- 區長摘要使用後端提供、由完整 Groups 計算的 `GroupCount`，顯示在「本區 N 人」之前，獨立 Ungrouped 不計入且前端分頁不得用當頁 Groups 重算。小組時間／地點必須從 CRM list 的 `new_group_time`、`new_group_place` 經既有單次 list query 映射，禁止 N+1；兩值逐項 trim，至少一項有值才顯示已有項目的標籤與值，兩項皆空時只隱藏 metadata 列，小組名稱、小組長與人數仍顯示。
- 三種 DataGrid 都啟用 `allowColumnResizing: true`、`columnResizingMode: 'widget'` 與 `sorting: { mode: 'single' }` 或經核准的目標版本等價設定；維持禁止欄位 reordering。點同一資料欄表頭在升冪／降冪間切換，拖曳表頭分隔線只調寬、不得觸發排序。
- 一般小組與搜尋結果可排序 `RelationGoals`；無小組 remote grid 必須保留等同 `allowSorting: !remotePaging` 的 guard，不得把不存在的 CRM 計算欄送往遠端排序。
- 若目標實際版本確認為 DevExtreme 22.1.6，固定資料列覆蓋層必須使用限定範圍的 touch bridge：固定儲存格 `touch-action: pan-y`；單指位移未達 6px 不判向；只有水平位移大於垂直位移時才對同一個 DataGrid `getScrollable()` 執行水平 `scrollBy`；垂直手勢不呼叫 `preventDefault()`，保留頁面垂直操作。
- DevExtreme 22.1.6 的固定覆蓋層在水平手勢後抑制合成 click 350ms，避免滑動姓名時誤開明細；未形成水平手勢的普通點擊必須照常開啟明細。每個覆蓋層只能綁定一次，DataGrid 重繪時由三種 grid 共用的 ready handler 對新覆蓋層重新檢查，不累積 handler。
- fixed touch bridge 只能綁定 rows view 的固定資料列覆蓋層，不得綁定或攔截 headers；表頭 hover／touch separator、拖曳完成與排序方向提示交由 DevExtreme 原生行為處理。
- 若目標 DevExtreme 不是 22.1.6，先以該版本文件、實際 DOM 與觸控實驗確認 fixed column 與 scrolling API，再提出等價最小實作並取得核准；不得盲套 22.1.6 selector、`getScrollable()` 呼叫或事件模型。

【禁止】
- 禁止 Commit、merge、建立或切換 branch／worktree。
- 禁止直接套用 `host-integration/*.patch` 或大段複製參考 Razor／JavaScript；patch 只供比對，必須依已確認的 DevExtreme client 版本與目標 repo 現況做最小適配。
- 禁止隱藏欄位、壓縮到難以閱讀、重新啟用 adaptive「…」欄、對整個表格攔截 touchmove 或製造第二個水平 overflow owner；只有在確認 DevExtreme 22.1.6 固定覆蓋層確實攔截橫向手勢後，才可使用前述限定範圍的 touch bridge。
- 禁止為了工具列同列把搜尋 input 降到 16px 以下，禁止用固定高度截斷長姓名、地址、關係目標或 200% 文字縮放。
- 禁止修改桌機字級、搜尋權限、API、CRM schema 或既有遠端資料排序契約；若 `new_group_time`／`new_group_place` 尚未經既有單次 list query 進入 DTO，停止並回到 Prompt 3／4 完成核准資料流，絕不可在此新增逐組 query。此 Prompt 只允許加入核准的最終欄位／摘要呈現、原生單欄表頭排序及 `RelationGoals` remote guard。
- 禁止只看 scrollbar 截圖就宣稱手機手勢通過；必須在表格內容上實際左右滑動。

【必須驗證】
- 320、390、430、640px viewport 下，工具列同列、按鈕可點、區長明顯大於小組長、表頭與列文字可讀、長內容不遺失。
- 全教會、小組成員、無小組與搜尋結果表都沒有「…」欄，只有一條水平捲軸，從資料列上用手指左右滑動可看到最右欄；頁面仍可垂直滑動。
- 一般小組、無小組與搜尋結果的欄位順序都精確為頭像、姓名、行動電話、生日、地址、信仰狀態、會員身份、關係目標、性別；行動電話置中、性別最後。滑到最右側後頭像與姓名仍固定可見；`ContactId` 維持 72px 且不可調寬／排序，`FullName` 初始 62px 且無 application `minWidth`，只有這兩欄 fixed left。三者共用欄位工廠與 ready handler，`columnHidingEnabled: false` 保持生效。
- 每個 district 顯示未分頁前的完整實際 `GroupCount` 且不包含獨立 Ungrouped；小組時間／地點來自既有單次 CRM list query。兩項皆空時看不到 metadata 列與標籤，但小組名稱、小組長與人數仍清楚可見；僅一項有值時只顯示該項。
- 在三種 DataGrid 以滑鼠及手指拖曳姓名至關係目標的表頭分隔線，確認 `widget` resizing 只改目前欄與 grid 內容寬度；頭像不可調寬，拖曳完成不觸發排序，fixed rows touch bridge 不攔截 headers。
- 在三種 DataGrid 輕點同一資料欄表頭，確認 `single` sorting 在升冪／降冪間切換，點另一欄即改用該欄；無小組 remote grid 的姓名／生日等排序正確，`RelationGoals` 沒有遠端排序操作。
- 在頭像、姓名及右側可捲動欄位分別起手測試：6px 內細微移動不誤判，橫向滑動驅動同一 scrollable，固定區垂直滑動仍捲動頁面；滑動姓名不開明細，普通點擊姓名會開啟正確明細，反覆重繪／dispose 不累積事件。
- 搜尋 input 聚焦前後 viewport 不自動放大，搜尋按鈕仍可見；在 iOS Safari 與可用的 LINE WebView／等價模擬環境記錄 computed font-size 與畫面證據。
- 48px 主要控制項、44px 展開區、200% 文字縮放、長地址／關係目標換行、reduced-motion 與鍵盤 focus 可用性通過。
- 以 DOM／computed style 確認只有一個 overflow-x scrolling owner，不是靠隱藏其中一條 scrollbar；反覆建立／dispose 搜尋 grid 後仍只有一條。
- 執行 View contract、JavaScript 語法、MemberInfo 測試、build 與實際觸控瀏覽器驗收，記錄 viewport、瀏覽器與結果。

【停止條件】
- 找不到實際 scrolling owner、無法確認目標 DevExtreme client 精確版本、目標版本的 fixed column／native scrolling／column resizing／sorting 行為尚未查證、input computed font-size 低於 16px、出現雙捲軸／三點欄、固定區不能正確判向／點擊、表頭分隔線拖曳會誤排序，或任何欄位無法觸控查看時停止。
- 不得用隱藏內容或停用縮放來掩蓋可用性問題。

【輸出】
輸出 responsive token／media query 摘要、實際 DevExtreme client 版本證據、唯一 scrolling owner、精確九欄順序、62px／無 `minWidth` 姓名設定、`GroupCount` 與小組 metadata 資料流、固定欄與 touch bridge 契約、resizing／sorting／remote guard 設定、三種 grid 的滑鼠與觸控證據、修改檔清單、自動測試與各 viewport／實機證據、已知瀏覽器差異及 git diff 摘要。最後聲明「未 Commit」。
```

## Prompt 8：會員身份依 Dynamics metadata 客製化順序排序

```text
你要在目前開啟的目標 Git repository，讓一般小組、搜尋結果與無小組遠端分頁三種會友表格，都依目標 Dynamics／Dataverse 的 `contact.customertypecode` 系統客製化選項順序排序。member-info-portable-kit/ 是參考證據，不是可覆蓋來源。開始前必須確認 Prompt 1 設計已核准，Prompt 3 的授權／DTO／樹 API 與 Prompt 7 的最終九欄 DataGrid 已存在或有等價證據。

【目的】
以 `PicklistAttributeMetadata.OptionSet.Options` 集合位置建立 metadata rank，讓 raw value 較大但在系統客製化中排第一的選項仍顯示在最上方；Configured、metadata 未知舊值與真正空白分開處理，正向／反向都讓 Unknown 與 Empty 置底。一般小組與搜尋用本機 rank，無小組在 CRM 計數、分段後做正確跨頁排序，不把全教會載入記憶體。

【先讀】
以下路徑均以 repo 內的 `member-info-portable-kit/` 為根目錄。
1. 閱讀 `01-INTEGRATED-SPEC.md` 的 MI-DATA-011、MI-ORDER、MI-GUARD-008，`02-DEPENDENCY-MATRIX.md` 的 STOP-OPTION-ORDER、DEP-OPTION-METADATA、DEP-OPTION-PAGING、DEP-DX-REMOTE-SORT，以及 `06-ACCEPTANCE-CHECKLIST.md` 的會員身份排序項目。
2. 完整閱讀 `original-specs/2026-07-18-member-info-commitment-type-sorting-design.md` 與 `original-plans/2026-07-18-member-info-commitment-type-sorting.md`。
3. 閱讀 `reference-implementation/feature-files/ChurchReport/Services/MemberInfo/MemberInfoCommitmentTypeMetadataProvider.cs`、`MemberInfoCommitmentTypeSort.cs`、`MemberInfoCommitmentTypeCountQuery.cs`、DTO／SearchBuilder snapshots，以及六份相關 tests。
4. 閱讀 `reference-implementation/host-integration/06-member-info-commitment-type-metadata-order.patch` 與 `SOURCE-MAP.md`。Patch 只供理解整合點，禁止直接 `git apply`。
5. 從目標 repo 證明：entity／attribute logical name、Picklist metadata read privilege、SDK 的 RetrieveAttribute／QueryExpressionToFetchXml／aggregate FetchXML 支援、現有 `IMemoryCache` 隔離與 DevExtreme remote sort payload。

【只允許】
- 只修改 Prompt 1 核准的 MemberInfo metadata provider、共用排序／分段 helper、DTO mapping、Controller 的三種資料路徑、DataGrid 欄位工廠與對應 tests。
- 直接保留 `OptionSet.Options` 原始 collection sequence，以集合 index 產生零起始 rank；raw value 只作 dictionary／segment identity。
- DTO 保持可見 `MembershipStatus` label，另提供 nullable rank 與 has-value flag 區分 Unknown／Empty；不得新增可見 raw value 欄。
- 一般小組在批次授權與列 mapping 完成後呼叫共用 sorter；搜尋先依 allowed IDs 過濾與 ContactId 去重，再套同一 sorter。
- 無小組保留既有在籍、搜尋、排除 grouped IDs 與 authorization 邊界；先用 SDK base query 轉 aggregate FetchXML 計各 non-null raw value 筆數，null 另計，再依 metadata 建 Configured／Unknown／Empty segments，將全域 skip／take 投影成只需查詢的 slices。
- 每個 segment 內依 fullname、contactid 穩定排序；反向只反轉 Configured ranks，Unknown／Empty 仍固定最後。
- visible 欄仍是 `MembershipStatus`；local 使用方向感知 sort-value，remote selector 使用 `MembershipStatusOrder`。預設 metadata 升冪，表頭反覆點擊可切換正／反。
- metadata 成功與暫時失敗採不同有限 TTL；只快取 schema，不含會友個資。多 organization 共用 process 時，cache key 必須依核准 identity 隔離。

【禁止】
- 禁止 Commit、merge、push、建立或切換 branch／worktree。
- 禁止依 raw `customertypecode` 整數、中文 label 字典序、Sunny 專屬 options 清單或 FetchXML `useraworderby` 排序。
- 禁止 metadata 失敗時默默改用 raw integer；應保留 Unknown／Empty 穩定行為、記錄診斷並把 MI-ORDER 標為未完成或核准降級。
- 禁止先取一頁再於前端排序、把全部 Ungrouped 載入記憶體、逐列 metadata request、逐列授權或移除 Unknown 舊資料。
- 禁止修改既有九欄順序、固定頭像／姓名、欄寬、單一捲軸、搜尋 lifecycle、關係目標或授權規則。

【必須驗證】
- Provider test 使用故意不依數值大小的 options sequence，例如 raw value 較大的第一個 option 必須得到 rank 0；同時驗證 label fallback、無 value option、成功 cache 與 metadata failure。
- 共用 sorter 驗證升冪、降冪、同 rank 姓名／ContactId、Configured／Unknown／Empty、重複 configured values、負 count、zero take、skip beyond total 與跨 segments slices。
- Count query 驗證原 filters／link-entity 保留，page／count／order 移除，group-by／countcolumn 正確，OptionSetValue／int aliases、重複 counts、null／malformed XML 正確處理。
- Controller／SearchBuilder contract 證明授權後才排序、raw sort selector／`useraworderby` 已從目前三種表格路徑移除，Ungrouped 先 counts／segments 再 paging，段內只 fullname／contactid。
- View contract 與 JavaScript 語法證明九個 visible fields 不變，會員身份 local／remote selector 正確，Unknown／Empty 在正反向都置底。
- 在目標 Dynamics 真實資料中，選一個 raw value 與 configured position 不同的 option 驗證它依系統客製化順位顯示；一般小組、搜尋結果與無小組 25／50／100 分頁一致，跨 segment 邊界沒有重複或遺漏。
- 再驗固定頭像／姓名、欄寬、表頭排序、單一水平捲軸、手機觸控、搜尋返回與授權負向案例沒有回歸。

【停止條件】
- 無法確認 metadata collection order、metadata read privilege、目標 SDK aggregate／paging 行為、remote sort selector，或 base query filters 在轉換後是否保留時停止；不得猜測或硬編碼。
- 任一頁出現重複／遺漏、Unknown 被丟棄、空白反向跑到前面、raw value 決定順序、未授權列參與回應或全量載入時停止並回到設計。

【輸出】
輸出：目標 metadata 來源與 options sequence 證據、DTO／cache／local／search／remote segment 資料流、修改檔清單、focused 與完整測試、build、Network sort payload、25／50／100 跨頁結果、真實畫面正反向驗收、回歸結果、git diff 摘要與殘餘風險。最後聲明「未 Commit」。
```

## Prompt 9：完整測試、瀏覽器驗收與交付報告

```text
你要對目前開啟的目標 Git repository 中「會友資訊、頭像、樹、搜尋、Loading、明細、手機操作與會員身份 metadata 排序」做完整驗收與交付報告。member-info-portable-kit/ 是驗收基準與參考證據。此階段不代替使用者 Commit；只有證據完整且無未解決高風險問題時，才可報告可供使用者驗收。

【目的】
以自動測試、build、靜態契約、安全負向測試、實際瀏覽器／手機操作、UTF-8 與 git diff 證據，逐項證明目標版遷移正確，並把 Commit 決定留給使用者。

【先讀】
以下參考套件路徑均以 repo 內的 `member-info-portable-kit/` 為根目錄。
1. 完整閱讀 `00-START-HERE.md`、`01-INTEGRATED-SPEC.md`、`02-DEPENDENCY-MATRIX.md`、`05-MIGRATION-RUNBOOK.md`、`06-ACCEPTANCE-CHECKLIST.md` 與 `manifest.json`。
2. 閱讀 `03-PROMPT-HISTORY-VERBATIM.md`，使用最終修正作驗收，不能把 [Image #] marker 當成可見的畫面證據。
3. 完整閱讀十一份規格，包含 `original-specs/2026-07-18-member-info-commitment-type-sorting-design.md`；遇到矛盾以 `01-INTEGRATED-SPEC.md` 與較晚明確修正為準。
4. 完整閱讀十一份計畫，包含 `original-plans/2026-07-18-member-info-commitment-type-sorting.md`。
5. 完整閱讀 `reference-implementation/README.md`、`reference-implementation/host-integration/SOURCE-MAP.md`、六份 host patches、`reference-implementation/feature-files/**` 與 `reference-implementation/tests/**`。所有 patch 都是 `EVIDENCE-ONLY`，絕不可直接 `git apply` 或把參考版本當成目標實作。另完整閱讀目標版 Prompt 1 已核准設計；無法定位就停止。
6. 檢視目標 repo 全部本次 git diff、既有使用者變更、測試專案、啟動方式與可用瀏覽器環境；不得假定 Sunny 的路徑、連接埠、PID 或 branch。

【只允許】
- 執行目標 repo 既有或本次新增的測試、build、lint／Razor JavaScript 檢查、嚴格 UTF-8／U+FFFD 掃描、秘密掃描、git diff／status／diff --check 與不改資料的瀏覽器驗收。
- 只有發現明確回歸且修正仍在 Prompt 1 核准範圍內時，才可先新增失敗測試再做最小修正，並重新跑所有受影響驗證；所有額外修改要在報告逐檔列出。
- 使用安全的測試帳號或 mock／fixture 驗證角色；真實 CRM 驗收只能讀取使用者已授權範圍，不輸出個資。
- 以 06-ACCEPTANCE-CHECKLIST.md 每列的「操作／命令、預期結果、證據」格式建立目標版完成矩陣，不預先勾選未執行項。

【禁止】
- 禁止 Commit、merge、push、建立或切換 branch／worktree，禁止刪除或覆蓋既有使用者變更。
- 禁止為了測試停止使用者 IDE／app、殺程序、使用固定 PID／連接埠、改 production 設定、寫入真實 CRM 或輸出秘密。
- 禁止把編譯通過當成瀏覽器通過，禁止把靜態 selector 存在當成手機手勢通過，禁止把外部 reviewer 失敗記成通過。
- 禁止在測試失敗、瀏覽器無法驗證、高風險負向案例未跑、diff 超出核准範圍或文件含無效 UTF-8 時宣稱完成。

【必須驗證】
- 套件自身 verifier 通過；目標版全部 MemberInfo tests、solution／project build、Razor JavaScript 語法、View contract 與 git diff --check 通過，並記錄命令、退出碼、passed／failed／skipped 數量。
- 先從目標 repo 實際載入的 JavaScript／CSS asset、bundle 設定或執行頁面記錄 DevExtreme client 精確版本，再判斷 fixed columns、`widget` resizing、`single` sorting 與 touch bridge 的預期行為；不可由資料夾名稱、伺服器 NuGet 或參考套件版本推定，也不可盲套 `host-integration/*.patch`。
- 權限：Church、Shepherd、未知角色；越權 list、越權 contact、混合圖片批次、非 Church 無小組／LINE 操作、malformed GUID 都被拒絕且不洩漏資料。
- 資料：有效小組、在籍非結案、人數去重、PascalCase、無小組分頁、空牧區、未填區長排序、完整 `GroupCount` 且排除獨立 Ungrouped、CRM list `new_group_time`／`new_group_place` 經既有單次 query 映射、單一關係目標、性別 OptionSet、生日 Year = 1 正規化。
- 頭像：主要照片、LINE、三種性別剪影、批次與快取、上傳 type／size 拒絕、上傳後刷新、LINE 403／404／逾時／部分失敗；回覆與 log 無 token。
- 樹與明細：多區、多組、區長完整小組數、唯一小組自動展開、重複展開、重複開啟不同明細、小組時間／地點逐項 trim 與條件式顯示、兩項皆空仍保留小組名稱／小組長／人數、聚會／裝備只屬當前 contact、錯誤與空狀態可恢復。
- 搜尋與 Loading：一筆、多筆、零筆、取消、快速連續搜尋、舊 response、timeout、返回瀏覽、超過 6 秒文案、reduced-motion 與讀屏屬性。
- 手機：320、390、430、640px；工具列同列、input 至少 16px 且聚焦不放大、48px 控制項、單一捲軸、無三點欄、資料列上手指左右滑動、頁面垂直滑動、200% 文字不遺失。
- 三種 DataGrid：一般小組、無小組與搜尋結果都共用欄位工廠；九欄順序精確為頭像、姓名、行動電話、生日、地址、信仰狀態、會員身份、關係目標、性別，行動電話表頭／資料置中且性別最後；`ContactId` 頭像必須是 72px、fixed left、`allowResizing: false`、`allowSorting: false`，`FullName` 姓名必須是 62px、無 application `minWidth`、fixed left，其他資料欄不得 fixed 且可用 `widget` resizing；`columnHidingEnabled: false` 與單一 scrolling owner 持續生效。
- 會員身份 metadata 排序：真實 `OptionSet.Options` configured sequence 是唯一權威；一般小組、搜尋與無小組 remote paging 的正向／反向一致，Unknown／Empty 置底，visible 欄仍是中文 label，remote selector 是 rank 而不是 raw `customertypecode`，25／50／100 分頁跨 segments 無重複或遺漏。
- 表頭互動：三種 DataGrid 都使用原生 `allowColumnResizing: true`、`columnResizingMode: 'widget'` 與 `sorting: { mode: 'single' }` 或目標版本核准等價設定；以滑鼠及手指拖曳各資料欄分隔線可調寬且不觸發排序，點同一表頭在升冪／降冪間切換，點另一欄改用該欄。無小組 remote grid 的 `RelationGoals` 必須保有禁止遠端排序 guard。
- fixed rows touch bridge 不得綁定或攔截 headers。若 DevExtreme client 版本為 22.1.6，另驗證從固定區起手的 6px 判向、水平 `scrollBy`、垂直保留、350ms click 抑制、普通姓名點擊與單次綁定；其他版本必須驗證經核准的等價行為，不得盲套 22.1.6 DOM／API。
- 安全與品質：所有新文字檔嚴格 UTF-8、U+FFFD 為 0、無秘密／連線字串／絕對機器路徑／真實個資；git diff 只含核准檔案。
- 交付報告分開統計 application source／HTML／CSS／JavaScript／tests 的新增與修改行數，以及本次規格／計畫／遷移文件行數；說明統計命令與 merge／binary／generated file 的處理規則。

【停止條件】
- 任一 Critical 安全、授權、資料正確性、搜尋恢復、手機可操作性問題未解；任一必要測試／build 失敗；實際瀏覽器環境不可用而需求必須實測；或 diff 含未核准檔案時，停止並回報 BLOCKED 或 NOT READY。
- 若 Visual Studio 或 app 鎖住輸出，優先使用目標 repo 內經確認的替代輸出路徑重跑；未獲使用者允許不得停止程序。
- 只有所有必要證據直接可查時才可報告 READY FOR USER VERIFICATION；這不等同已 Commit。

【輸出】
輸出：1. 結論 READY FOR USER VERIFICATION／NOT READY／BLOCKED；2. 逐命令結果；3. 06-ACCEPTANCE-CHECKLIST 對應證據矩陣；4. 瀏覽器與手機環境、操作及截圖／錄影索引；5. 安全與 UTF-8 掃描；6. 修改檔與行數統計；7. 未解風險；8. git status／diff 摘要；9. 明確聲明「沒有 Commit，請使用者檢查後自行 Commit」。
```
