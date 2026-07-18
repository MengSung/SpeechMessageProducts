# 會友資訊跨教會版本驗收清單

## 使用方法

這份清單應在目標教會版本完成實作後使用。每個項目的「證據」必須填入可重現的測試輸出、截圖名稱、Network 回應、log correlation ID、Git diff 或人工操作紀錄；不能只寫「看起來正常」。清單預設全部未勾選。

## A. 環境與依賴

| 完成 | ID | 驗收項目 | 操作／命令 | 預期結果 | 證據 |
|---|---|---|---|---|---|
| [ ] | ENV-01 | 正確 repository、branch、worktree | `git rev-parse --show-toplevel`; `git branch --show-current`; `git status --short` | 路徑與分支符合遷移設計，dirty files 可解釋 | 執行時記錄 |
| [ ] | ENV-02 | 技術版本已比對 | 檢查 target framework、NuGet、實際載入的 DevExtreme client asset/runtime、server package 與 JavaScript 引用 | 記錄 Sunny client 22.1.6 與目標 client/server 差異；版本相容或已有核准的適配方式 | 執行時記錄 |
| [ ] | ENV-03 | 建置基線與完成版可區分 | 執行專案原有 build/test，再執行完成版 build/test | 新增功能沒有掩蓋既有失敗 | 執行時記錄 |
| [ ] | ENV-04 | 套件未混入發佈 | 檢查 csproj、publish profile 與發佈清單 | `docs/portable/member-info-portable-kit` 不進入網站發佈輸出 | 執行時記錄 |
| [ ] | ENV-05 | DevExtreme 版本差異 hard-stop | 目標 client 非 22.1.6 時，先比對 fixed columns、header resize／sort、remote datasource、pointer／touch API 與 DOM | 未取得相容證據前不實作／不宣稱 MI-COLUMN 完成；差異、選項與核准均有記錄 | 執行時記錄 |

## B. 授權與安全邊界

| 完成 | ID | 驗收項目 | 操作／命令 | 預期結果 | 證據 |
|---|---|---|---|---|---|
| [ ] | AUTH-01 | Church 範圍 | 以全教會角色開啟頁面並查詢多區資料 | 僅能看到該教會核准範圍 | 執行時記錄 |
| [ ] | AUTH-02 | Shepherd 範圍 | 以牧養角色查詢自己與非自己名單 | 核准名單可讀，非核准名單拒絕 | 執行時記錄 |
| [ ] | AUTH-03 | 未授權 listId | 直接呼叫 group members API，傳入其他範圍 listId | 回傳拒絕或安全空結果，不洩漏姓名／人數 | 執行時記錄 |
| [ ] | AUTH-04 | 未授權 contactId | 直接呼叫 Detail、圖片與上傳 API | 讀寫都被拒絕，不以「知道 GUID」繞過 | 執行時記錄 |
| [ ] | AUTH-05 | malformed GUID | 對所有 GUID 入口傳空白、非 GUID、超長文字 | 安全錯誤，不拋未處理例外 | 執行時記錄 |
| [ ] | AUTH-06 | 批次授權 | 觀察 CRM 呼叫／測試 contract | 不對每列重查授權，不因快取共用而跨角色洩漏 | 執行時記錄 |

## C. CRM 與資料契約

| 完成 | ID | 驗收項目 | 操作／命令 | 預期結果 | 證據 |
|---|---|---|---|---|---|
| [ ] | DATA-01 | logical names 正確 | 對照目標 CRM metadata 與查詢欄位 | contact/list/listmember 及自訂欄位全部存在且型別相符 | 執行時記錄 |
| [ ] | DATA-02 | PascalCase DTO | 檢查樹、成員、搜尋 API JSON | `Districts`, `Groups`, `ContactId` 等 casing 與前端一致 | 執行時記錄 |
| [ ] | DATA-03 | 選填欄位缺少 | 用沒有牧區、區長、LINE、性別或生日的資料測試 | 留白／剪影／未知值按規格顯示，頁面不中斷 | 執行時記錄 |
| [ ] | DATA-04 | CRM Year=1 日期 | 用 CRM 最小日期或等價測試資料 | 視為未填生日，不顯示 `0001` | 執行時記錄 |
| [ ] | DATA-05 | 失效關聯 | listmember 指向停用／不存在 contact 的測試 | 跳過或安全標記，不使整棵樹失敗 | 執行時記錄 |
| [ ] | DATA-06 | 小組時間／地點 query | 檢查 `FetchSmallGroupDescriptors` 的 list ColumnSet、mapping 與 CRM 呼叫計數 | 既有單一 query 一併取得 `new_group_time`、`new_group_place`，映射到 `GroupTime`／`GroupPlace`；沒有逐小組 N+1 | 執行時記錄 |
| [ ] | DATA-07 | 區／組摘要 DTO | 檢查 tree JSON 與 builder tests | `GroupCount`、`GroupTime`、`GroupPlace` 為 PascalCase；metadata 已 trim，空白字元輸出空字串 | 執行時記錄 |
| [ ] | DATA-08 | 會員身份 configured order | 以 metadata export／`RetrieveAttributeRequest` 檢查 `contact.customertypecode` | 有直接證據顯示 `OptionSet.Options` 的集合順序；沒有依 raw value 或中文 label 猜順位 | 執行時記錄 |
| [ ] | DATA-09 | 會員身份排序 DTO | 檢查一般小組、搜尋、Ungrouped JSON 與可見欄位 | `MembershipStatus` 顯示目標 label；`MembershipStatusOrder` 為 metadata rank；`HasMembershipStatusValue` 可區分 Unknown／Empty；raw value 不成為可見欄位 | 執行時記錄 |
| [ ] | DATA-10 | 未知舊值與空白 | 建立 metadata 未列出的 non-null value 與真正 null 資料 | Unknown 資料不遺失，顯示可診斷 label／fallback；Empty 留白，兩者可被排序器分開 | 執行時記錄 |

## D. 區長、小組、會友樹

| 完成 | ID | 驗收項目 | 操作／命令 | 預期結果 | 證據 |
|---|---|---|---|---|---|
| [ ] | TREE-01 | 三層資料 | 展開多個區長、多個小組與多位會友 | 每個小組皆能載入，不只有第一組 | 執行時記錄 |
| [ ] | TREE-02 | 單組自動展開 | 開啟只有一組的區長 | 唯一小組按規格自動展開且可再次操作 | 執行時記錄 |
| [ ] | TREE-03 | 重複展開 | 同一組連續展開、收合、再展開 | 無重複列、永久 Loading 或失效 click handler | 執行時記錄 |
| [ ] | TREE-04 | 無小組 | 開啟無小組節點並換頁 | 直接顯示會友且分頁正確 | 執行時記錄 |
| [ ] | TREE-05 | 區長排序 | 建立已填區長、區長未填與無小組資料 | 已填區長在前，區長未填在其後，無小組最後 | 執行時記錄 |
| [ ] | TREE-06 | 牧區顯示 | 比較有牧區與未填牧區 | 有名稱才顯示括號；未填不出現「未填牧區」 | 執行時記錄 |
| [ ] | TREE-07 | 人數與視覺層級 | 桌面與手機檢查區長、小組長標頭 | 人數緊鄰姓名；區長字大且顏色不同 | 執行時記錄 |
| [ ] | TREE-08 | 完整小組數 | 使用超過 50 組的區跨前端小組分頁，另建立 Ungrouped | 區長標頭先顯示完整「N 組」，再顯示「本區 N 人」；N 不隨當頁 Groups 減少，Ungrouped 不計入 | 執行時記錄 |
| [ ] | TREE-09 | 小組 metadata 獨立顯示 | 分別測試時間＋地點、只有時間、只有地點、兩者空白 | 小組名稱、小組長、會友數永遠顯示；有值項目依時間→地點順序獨立顯示；兩者皆空時無 metadata row／空標籤 | 執行時記錄 |

## E. 頭像、LINE 與上傳

| 完成 | ID | 驗收項目 | 操作／命令 | 預期結果 | 證據 |
|---|---|---|---|---|---|
| [ ] | AVATAR-01 | 主要照片優先 | 同一 contact 同時具 CRM 照片與 LINE URL | 顯示主要照片及正確來源徽章 | 執行時記錄 |
| [ ] | AVATAR-02 | LINE fallback | 無 CRM 照片但有可用 LINE 圖片 | 顯示 LINE 圖片且不重複逐列請求 | 執行時記錄 |
| [ ] | AVATAR-03 | 性別剪影 | 無任何照片，分別測試性別值與未知值 | 顯示對應或中性剪影，不顯示破圖 | 執行時記錄 |
| [ ] | AVATAR-04 | 批次載入 | 展開含大量會友的小組並觀察 Network | contact IDs 合理批次，無 N+1 圖片風暴 | 執行時記錄 |
| [ ] | AVATAR-05 | 重複 contact | 同一 contact 出現在搜尋與樹或多節點 | 共用結果／快取且各處來源標記一致 | 執行時記錄 |
| [ ] | AVATAR-06 | 上傳成功 | 上傳允許的 JPEG/PNG/GIF 且小於限制 | 儲存成功、彈窗與所有可見縮圖立即更新 | 執行時記錄 |
| [ ] | AVATAR-07 | 上傳拒絕 | 上傳錯誤 MIME、副檔名、空檔與超大檔 | 伺服器拒絕且不保存，顯示可理解錯誤 | 執行時記錄 |
| [ ] | AVATAR-08 | 快取失效 | 上傳新圖後重新開啟明細、搜尋與樹 | 不再顯示舊圖，其他 contact 快取不受影響 | 執行時記錄 |
| [ ] | AVATAR-09 | LINE 403/404 | 模擬未加好友、封鎖或無照片 | 清楚分類，使用剪影，不把預期狀況報成整批失敗 | 執行時記錄 |
| [ ] | AVATAR-10 | LINE timeout/5xx | 模擬逾時與暫時錯誤 | 不清除仍可能有效的資料，UI 可恢復並可重試 | 執行時記錄 |
| [ ] | AVATAR-11 | 同步權限 | 以無全教會權限角色呼叫 LINE resync | 操作不可用且 API 拒絕 | 執行時記錄 |

## F. 明細與欄位

| 完成 | ID | 驗收項目 | 操作／命令 | 預期結果 | 證據 |
|---|---|---|---|---|---|
| [ ] | DETAIL-01 | 明細可開啟 | 從不同小組與搜尋結果依序開啟多位會友 | 每次顯示正確 contact，不是空白或前一位內容 | 執行時記錄 |
| [ ] | DETAIL-02 | 重複快速開啟 | 快速點擊不同聯絡人並在載入中再次點擊 | 舊回應不覆蓋新選擇，沒有永久遮罩 | 執行時記錄 |
| [ ] | DETAIL-03 | 關係目標 | 檢查列表與明細 | 表格只有「關係目標」單欄，內容格式與去重正確 | 執行時記錄 |
| [ ] | DETAIL-04 | 性別 | 測試已填與未填性別 | 已填顯示正確文字，未填安全留白 | 執行時記錄 |
| [ ] | DETAIL-05 | 生日 | 測試一般生日、未填及 Year=1 | 一般日期格式正確；未填與 Year=1 留白 | 執行時記錄 |

## G. 搜尋、Loading 與錯誤狀態

| 完成 | ID | 驗收項目 | 操作／命令 | 預期結果 | 證據 |
|---|---|---|---|---|---|
| [ ] | SEARCH-01 | 多筆結果 | 搜尋可命中多位會友的文字 | 結果表格直接取代瀏覽樹並顯示全部結果 | 執行時記錄 |
| [ ] | SEARCH-02 | 單筆結果 | 搜尋唯一姓名 | 顯示單筆且仍可開啟明細 | 執行時記錄 |
| [ ] | SEARCH-03 | 零筆結果 | 搜尋不存在的文字 | 明確顯示沒有搜尋到，不留舊結果 | 執行時記錄 |
| [ ] | SEARCH-04 | 搜尋期間狀態 | 在慢速網路執行搜尋 | 全頁遮罩可見；按鈕改色、改圖示並顯示「停止搜尋」 | 執行時記錄 |
| [ ] | SEARCH-05 | 取消搜尋 | 搜尋尚未完成時按停止 | request/結果被忽略或取消，回到搜尋前瀏覽狀態 | 執行時記錄 |
| [ ] | SEARCH-06 | 返回會友資訊 | 搜尋完成後按返回 | 恢復原樹、展開與工具列狀態，不需重整整頁 | 執行時記錄 |
| [ ] | SEARCH-07 | 重複搜尋競態 | 連續執行不同關鍵字 | 只有最新有效搜尋能更新畫面 | 執行時記錄 |
| [ ] | LOAD-01 | 初始 Loading | 清除快取後開啟頁面並模擬 10 秒延遲 | 動畫持續可見且不讓人誤判當機 | 執行時記錄 |
| [ ] | LOAD-02 | reduced-motion | 啟用作業系統減少動態效果 | 保留狀態資訊但停止非必要動畫 | 執行時記錄 |
| [ ] | LOAD-03 | API error | 模擬 400/403/500 或網路中斷 | 遮罩結束、顯示可診斷錯誤並可重試 | 執行時記錄 |

## H. 桌面與手機操作

| 完成 | ID | 驗收項目 | 操作／命令 | 預期結果 | 證據 |
|---|---|---|---|---|---|
| [ ] | MOBILE-01 | 單一水平捲軸 | 在一般小組、Ungrouped、搜尋結果表格寬於 viewport 時檢查 DOM 與畫面 | 每個可見表格只有一條 DevExtreme 水平捲軸，沒有 host／外層第二捲軸 | 執行時記錄 |
| [ ] | MOBILE-02 | 指頭水平滑動 | 在 320／390／430／640px 真機於右側資料區及 fixed rows 區左右滑 | 可看右側欄位，不觸發 adaptive popup；頭像／姓名維持 fixed left | 執行時記錄 |
| [ ] | MOBILE-03 | 禁止三點 adaptive | 使用 320／390／430／640px 檢查一般小組、搜尋結果、Ungrouped 表格 | `columnHidingEnabled`／adaptive 維持 false，不出現三點欄位選單，欄位保留在水平內容中 | 執行時記錄 |
| [ ] | MOBILE-04 | 工具列單列 | 320px、360px、390px 寬檢查搜尋與重新同步 LINE | 同列可操作，不把同步按鈕換到下一列 | 執行時記錄 |
| [ ] | MOBILE-05 | 防輸入自動放大 | 在窄 viewport 量測搜尋輸入框 computed font-size，再於 iOS Safari／LINE WebView 點擊輸入 | 手機樣式 computed font-size 至少 16px；viewport 不突然放大，右側搜尋按鈕仍可見 | 執行時記錄 |
| [ ] | MOBILE-06 | 流動字級 | 檢查 320px、390px、768px 與桌面 | 字級隨解析度適配且正文仍易讀 | 執行時記錄 |
| [ ] | MOBILE-07 | 觸控尺寸 | 量測主要按鈕、節點與互動列 | 主要觸控區至少約 48px，無難以點擊的重疊 | 執行時記錄 |
| [ ] | MOBILE-08 | 垂直頁面捲動 | 在三種 grid 的 fixed rows 與右側資料區上下滑並混合斜向手勢 | vertical gesture 仍捲動頁面，水平表格與 fixed rows touch bridge 不鎖死手勢 | 執行時記錄 |
| [ ] | COLUMN-01 | 三種 grid／五種寬度矩陣 | 一般小組、Ungrouped、搜尋結果逐一在桌機及 320／390／430／640px 開啟 | 三種 grid 的固定欄、欄寬、排序與捲動契約一致；每個矩陣格都有直接證據 | 執行時記錄 |
| [ ] | COLUMN-02 | 固定欄預設尺寸 | 初載、重建 grid 與重新整理後量測 `ContactId`／`FullName`，檢查 FullName column options | 頭像 72px fixed left 且不可 resize／sort；姓名 62px fixed left，應用程式未設定 `FullName.minWidth` | 執行時記錄 |
| [ ] | COLUMN-03 | 精確欄位順序與文案 | 檢查一般小組、Ungrouped、搜尋結果的 dataField／caption／alignment | 三種 grid 依序為 `ContactId`, `FullName`, `Phone`, `BirthDate`, `Address`, `SpiritualIdentity`, `MembershipStatus`, `RelationGoals`, `Gender`；Phone caption「行動電話」且置中；Gender 最後 | 執行時記錄 |
| [ ] | COLUMN-04 | 桌機滑鼠調寬 | 以滑鼠拖曳姓名、行動電話、生日、地址、信仰狀態、會員身份、關係目標、性別的表頭分隔線，再嘗試頭像欄 | 資料欄採 DevExtreme `widget` resize，只改目前欄與 grid 寬度；姓名沒有應用程式 `minWidth` 限制；頭像不可調，下一欄不被 `nextColumn` 壓縮 | 執行時記錄 |
| [ ] | COLUMN-05 | 真機手指調寬 | 在 320／390／430／640px 真機以單指拖曳可調欄分隔線，並嘗試頭像欄 | 可調欄能改寬；姓名不受應用程式 `minWidth` 限制；頭像保持 72px；header gesture 不被 fixed rows touch bridge 攔截 | 執行時記錄 |
| [ ] | COLUMN-06 | 單欄 asc／desc 排序 | 三種 grid 逐欄輕點表頭兩次，再點另一欄；檢查排序提示與資料順序 | 同欄在 asc／desc 間切換，換欄後只保留一欄排序；頭像不可排序 | 執行時記錄 |
| [ ] | COLUMN-07 | local／remote 關係目標 | 在一般小組／搜尋點 `RelationGoals`，再於 Ungrouped 檢查同欄與 Network payload | local rows 可排序；Ungrouped remotePaging 的計算欄不可排序且不送入 remote query，姓名／生日等實體欄仍可 remote sort | 執行時記錄 |
| [ ] | COLUMN-08 | 拖曳與點擊隔離 | 在表頭分隔線完成多次短／長拖曳，輕點表頭，再點資料列姓名 | 拖曳不觸發排序；表頭 resize／sort 不誤開明細；資料列姓名只開正確 contact | 執行時記錄 |
| [ ] | COLUMN-09 | 不保存欄寬 | 調寬後逐一 rebuild、remount、reload，並檢查 DevExtreme `stateStoring`、`localStorage`、`sessionStorage`、Network／server preference／mapping | 每次都回到 72px 頭像、62px 姓名、無應用程式 `FullName.minWidth` 及其他 factory 核准值；跨 grid／page／device 沒有欄寬 state storage | 執行時記錄 |
| [ ] | COLUMN-10 | 禁止欄位捷徑 | 檢查 DataGrid options、DOM 與三種 grid 操作 | reordering、`nextColumn`、adaptive dots、自訂 header drag 與第二水平捲軸皆未啟用 | 執行時記錄 |
| [ ] | COLUMN-11 | 不保存排序狀態 | 選擇排序欄與 asc／desc 後逐一 rebuild、remount、reload，並檢查 DevExtreme `stateStoring`、`localStorage`、`sessionStorage`、Network／server preference／mapping | 先前 sort column／direction 均清除並回到 local／remote datasource 核准初始順序；跨 grid／page／device 沒有排序 state storage | 執行時記錄 |
| [ ] | COLUMN-12 | metadata 預設升冪 | 選擇 raw value 與 configured position 不一致的真實 options，初次開啟三種 grid | 系統客製化第一個 option 排第一；raw 整數較小者不會搶到前面 | 執行時記錄 |
| [ ] | COLUMN-13 | 會員身份正／反向 | 在一般小組、搜尋結果、Ungrouped 各連點「會員身份」表頭兩次 | Configured ranks 正向／反向切換；Unknown 仍在其後，Empty 永遠最後；visible cell 持續顯示中文 label | 執行時記錄 |
| [ ] | COLUMN-14 | local／remote selector | 檢查 local sort-value、Ungrouped Network payload 與 Controller mapping | local 使用 rank／has-value；remote selector 為 `MembershipStatusOrder`，沒有 `useraworderby` 或 raw `customertypecode` AddOrder | 執行時記錄 |
| [ ] | COLUMN-15 | 跨 segment 遠端分頁 | 用 25、50、100 page size，讓頁面跨 Configured→Unknown→Empty 邊界 | totalCount 正確，前後頁沒有重複／遺漏；同類型內 fullname／contactid 穩定 | 執行時記錄 |
| [ ] | COLUMN-16 | metadata 暫時失敗 | 模擬 RetrieveAttribute 失敗或移除 metadata privilege 的安全測試環境 | 不改用 raw integer 冒充順位、不拋未處理例外；短期 failure cache、診斷與 capability 未完成／降級狀態可查 | 執行時記錄 |

## I. 效能、診斷、編碼與交付

| 完成 | ID | 驗收項目 | 操作／命令 | 預期結果 | 證據 |
|---|---|---|---|---|---|
| [ ] | QUAL-01 | 自動測試 | 執行目標版本核准的 MemberInfo test command | 全部測試通過且無意外 skip | 執行時記錄 |
| [ ] | QUAL-02 | 完整建置 | 執行目標 solution/project build | 0 errors；warning 已分類 | 執行時記錄 |
| [ ] | QUAL-03 | CRM 查詢數量 | 以代表性資料觀察診斷或 mock 次數 | 授權與照片採批次；小組時間／地點來自既有 descriptor list query；OptionSet metadata 使用共用有限期快取；呼叫不隨每列、每組或每頁線性增加 | 執行時記錄 |
| [ ] | QUAL-04 | 前端錯誤 | 完成樹、搜尋、明細、上傳流程後檢查 console/network | 無未處理例外、404 資源或永久 pending request | 執行時記錄 |
| [ ] | QUAL-05 | UTF-8 | 對所有修改文字檔嚴格 UTF-8 decode 並掃描 U+FFFD | 無解碼錯誤、U+FFFD 或新亂碼 | 執行時記錄 |
| [ ] | QUAL-06 | 秘密與個資 | 掃描 diff 中的 token、密碼、connection string、真實資料 | 無硬編碼秘密或測試個資 | 執行時記錄 |
| [ ] | QUAL-07 | Diff 範圍 | `git diff --check`; `git status --short`; 人工檢視 diff | 只有核准檔案，無格式破壞或套件輸出混入 app | 執行時記錄 |
| [ ] | QUAL-08 | 使用者驗收 | 使用者在實際 VS／瀏覽器／手機完成核心流程 | 使用者確認後才自行 Commit 或合併 | 執行時記錄 |

## 完成判定

只有當所有適用項目都有直接證據時才能宣稱遷移完成。若某項因目標教會確實不使用該能力而不適用，必須記錄「不適用的原因、核准者與替代驗證」，不可直接略過。Critical 權限、資料契約、照片安全或零筆／取消／錯誤狀態若未通過，整體仍是未完成。
