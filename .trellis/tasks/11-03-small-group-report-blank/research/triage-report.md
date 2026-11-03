# 小組回報空白：第一階段分流調查

日期：2026-11-03（Asia/Taipei）
分支：`1.0.0.11.Integration.P0`
調查基準：`d42b6d3f`，與 `73b6e6cf` 比較。
結論：尚未判定使用者這次重現的根因；依核定停在分流，不修改產品程式。

## 1. 調查邊界與證據來源

- 使用者已提供：P0 Debug/net10.0、Development、goodshepherdback；19:25 建置；重現沒有新增 Exception.log；畫面連固定欄位標題都未顯示。
- Codex 沒有開啟執行中的網站、沒有連線 D365 或 LINE、沒有跟隨登入重新導向、沒有讀 User Secrets。
- 本報告的環境比較只讀 repo JSON，不能代替執行中 Host 的 User Secrets、環境變數與啟動快照。
- ProbeImport.targets 僅在明確傳入 MSBuild 參數時把研究探測加入既有測試；未修改任何產品或測試專案檔。
- 所有探測只用合成 ID、數值與記憶體 Session，不輸出姓名、電話、LINE ID、帳密或 token。沒有保存瀏覽器畫面中的實際小組名稱。
- P0-12A 保留在使用者提交的 `d42b6d3f`，未改其特徵測試、計畫或進度頁。

## 2. 優先檢查的三個原因

此表為調查優先序，不是已確認的根因，也尚未決定修復分支。

| 優先 | 可能原因 | 已有本機證據 | 最快判別方法 |
|---|---|---|---|
| 1 | 瀏覽器端 Grid 初始化中斷，包括發布防護掛載、模板或格式化錯誤 | 固定標題也空白，不只資料列消失。真實 adapter 在 getDataSource 尚未就緒時會拋出「週報 Grid 缺少既有 WebApi store.load」；已就緒則成功。但尚未證明實際瀏覽器遇到此前置條件。 | F12 第一條紅字與完整堆疊：是否指向 initializeWeeklyReportGridPublicationGuard、模板 ReferenceError、dx.all 的初始化或格式化；同時確認兩個資料請求是否曾送出。若未送出，優先查初始化。 |
| 2 | 靜態資源、文化設定或瀏覽器舊快取造成載入組合不完整 | 35 筆本機 script/CSS 引用有 14 筆找不到實體檔，含 CLDR/Globalize；這些引用在基底頁面就存在。Startup 對已找到的靜態資源設一年 immutable，頁面此處未附內容版本。客戶端仍是既有 22.1.6，不可因此直接判為版本缺陷。 | Network 勾 Disable cache 後重新整理，先看 JS/CSS 404、回應是否其實是 HTML、是否出現 Globalize/Intl/CLDR 格式化錯誤；比較停用快取前後。須確認實際 HTTP 結果，磁碟缺檔不能直接等同線上 404。若證據指向版本落差，先回報，不改版本。 |
| 3 | API 回應不是正常 LoadResult，或測試組織的週報/Session 狀態使資料為空 | 已重現正常 data 空陣列、HTTP 狀態未明確設錯誤的 status=error JSON、授權 401，以及 Session 中介層的 AJAX 302。已存在的週報若出席紀錄為零，不會退回名單查詢。但正常空集合本身不能充分解釋固定標題也消失。 | 看 LoadIntegrate、GetChartDataList 及 LoadNewPersonFollowUp 的狀態碼、Content-Type、大小與遮蔽後的 JSON 結構；分辨 data/totalCount、status=error、401、302→登入 HTML。對照執行中網站主控台與正式站同一流程。 |

## 3. 全域 JS 與載入順序

| 符號/資源 | 定義位置（repo 相對路徑與行號） | 靜態結果 |
|---|---|---|
| collection-load-coordinator.js | Views/Home/IntegrateView.cshtml:24 | 在該頁元件前引用；實體檔存在，與基底內容相同。 |
| initializeWeeklyReportGridPublicationGuard | Views/Home/_GeneralGroupGrids.cshtml:30 | 兩個 Grid 的 OnInitialized（387、610）使用此全域函式；45 行讀取 DataSource，無來源時拋出。 |
| membershipStatusDataSource | 同檔:632 | 初始空陣列；document.ready 同步 AJAX 載入會員身分清單，失敗有固定候選值。 |
| spiritualIdentityDataSource | 同檔:633 | 初始空陣列；document.ready 載入信仰狀態清單，失敗有固定候選值。 |
| visitDataSource | 同檔:634 | 初始空陣列；document.ready 載入探訪清單，失敗維持空陣列。 |
| contactImageCellTemplate | 同檔:867 | 掛在 window，兩個 Grid 都引用。 |
| memberInfoDetailNameCellTemplate | 同檔:872 | 掛在 window，使用共用會友細節 popup host。 |
| openMemberInfoDetailPopup | Views/Shared/_MemberInfoDetailPopupHost.cshtml | 一般小組 partial 透過 PartialAsync 載入此共用 host；全域定義存在。 |
| jQuery、dx.all、dx.aspnet.data、dx.aspnet.mvc | Views/Shared/_Layout.cshtml:252–258 | 在 RenderBody 前引用；上述必要實體檔存在，沒有 Development 才省略的 environment 條件。 |

`inspect-client.cjs` 解析這條頁面鏈的 6 個 inline script 全部成功，在禁止 AJAX 的替身環境中可建立上述全域函式及陣列。刻意不執行 document.ready 的取數工作，也不執行 Razor 產生的 DevExtreme 元件初始化；所以不能宣稱真實 DOM 初始化或網路載入成功。

頁面底端才載入 devextreme-intl 與 zh-tw messages；呼叫 locale 的程式在前。這是既有載入順序，須由瀏覽器堆疊確認是否相關，不能憑靜態順序直接定罪。

### 本機找不到的 14 筆引用

全數來自 `_Layout.cshtml`，完整逐筆結果見 `client-inspection.json`。

- `lib/devextreme/css/dx.common.css`、`lib/devextreme/css/dx.spa.css`
- `css/demo-template.css`
- `lib/devextreme/js/cldr.min.js`
- `lib/devextreme/js/cldr/event.min.js`、`supplemental.min.js`、`unresolved.min.js`
- `lib/devextreme/js/globalize.min.js`
- `lib/devextreme/js/globalize/number.min.js`、`currency.min.js`、`date.min.js`、`message.min.js`
- `lib/jszip/dist/jszip.js`
- `lib/devextreme/js/vectormap-data/usa.js`

這 14 個位置在基底 Git 樹也沒有檔案，但無法由 Git 推知過去部署機是否另行安裝了未追蹤的 lib。未向任何網站發送靜態資源請求。

### 與 P0 基底的差異

比較時只正規化 Git LF 與工作區 CRLF，避免把換行差異誤報為程式變更。以下 7 檔的文字內容均與 `73b6e6cf` 相同：

`_Layout.cshtml`、`IntegrateView.cshtml`、`_GeneralGroupGrids.cshtml`、`_MemberInfoDetailPopupHost.cshtml`、`dx.all.js`、`dx.aspnet.mvc.js`、`collection-load-coordinator.js`。

SmallGroup 控制器亦沒有出現在 `73b6e6cf..HEAD` 的產品差異清單中。本次調查未新增其差異。這只能排除這些檔案被 P0 直接修改，不能排除環境設定、資料或既有問題在新組合下被觸發。

## 4. 圖表的資料與文化契約

- `IntegrateView.cshtml:94–114` 使用 GetChartDataList，LoadParams 的 WeeklyReportId 實際帶入可見小組 ListId。
- Controller `SmallGroupController.DataApi.cs:194` 與 Grid 共用 EnsureAndGetIntegrateDetachedRead，因此兩者會受到同一小組可見範圍及候選驗證影響。
- ArgumentField 為 SundayDate，Series 欄位為 SundayNumber、SmallNumber，Key 為 WeeklyReportEntityId。Startup.cs:451–454 使用 DefaultContractResolver，保留 PascalCase；假圖表序列化已驗證這四個欄位存在。
- `DownloadIntegrateData.Setup.cs:222–249` 取得最近兩個月週報。日期經 ToLocalTime().ToShortDateString() 變成字串，依伺服器當時 CultureInfo 格式；人數為非負 int。
- Razor 未指定 ArgumentAxis 日期型別或專用日期 formatter；Series 標籤指定 FixedPoint/Precision(0)，ValueAxis 只設定標題與位置，沒有自訂數值 formatter。
- 頁面 locale 固定設 zh-tw，且在其後引用 intl/zh-tw 訊息腳本。Globalize/CLDR 引用是否載入、當時 formatter 選擇及錯誤需由 F12 驗證。
- 沒有查詢真實日期或統計資料；合成圖表只證明正常回應欄位，不證明畫面軸標籤可繪製。

## 5. API 與 Session 回應矩陣

| 條件 | 本機探測結果 | 解讀限制 |
|---|---|---|
| 無本週週報、候選為兩筆名單預覽，無歷史統計 | LoadIntegrate 為 LoadResult，data 2 筆、totalCount=2；圖表 data 空陣列、totalCount=0 | 借用既有 ListManager loader seam 注入候選；只驗證 API 邊界，不宣稱完整 CRM 名單查詢已驗證。 |
| 週報已存在，但該週沒有出席紀錄；假服務備有名單成員 | 真實 GetAllMemeberDataList 只查一次 new_present_record，零筆即返回，不查 contact/listmember；API 正常 data 空陣列、totalCount=0 | 與上一列是不同情境；資料層的空白可能合理，但不證明本次畫面就是這種情況。 |
| 注入一筆歷史統計 | 圖表 LoadResult 有一筆資料，四個 PascalCase 欄位保留 | 不執行客戶端格式化。 |
| loader timeout，AJAX | 兩支 API 均回 JsonResult，status=error，有 message/timestamp、沒有 data；JsonResult.StatusCode 未設定，HttpContext 預設仍 200 | 直接呼叫 action，未啟動完整 HTTP Host；不能把 200 當正常資料成功。 |
| Session/可見小組清單遺失，action 已獲放行 | EnsureCorrectUserData 並非授權器；後續可見範圍驗證失敗，兩支 API 同樣回錯誤 JSON | 只用清空的假 Session 與假可見清單；不恢復真實身分。 |
| 無 Cookie/Session 身分，GlobalAuthorizationFilter 啟用 | AJAX 為 401；一般導覽 redirect 到 Authentication/Login；action 不執行 | repo JSON 的 EnforceGlobalAuthorization 實際為 false，且由基底繼承；不能把啟用時的 401 規則誤當目前所有請求的必然結果。 |
| 非 LINE 的 User-Agent 與 Session 不一致 | SessionValidationMiddleware 對 AJAX 也回 302 Location=/Login，清空 Session，不執行下游 | 瀏覽器可能跟隨後拿到登入 HTML；本探測不跟隨，不主張已觀測使用者的登入 HTML。 |

`BaseChurchController.HandleError:380` 會先把真實 Exception 交給 ExceptionReporting.Report。Program.cs:74–76 在 Host 啟動時建立 Logs owner 並 Attach；repo JSON 的 WriteExceptionLog/SendLine 均為 true。因此若走到此 catch，且執行中的 owner、開關與檔案寫入都正常，應新增 Exception.log。沒有新紀錄使「初始化/授權短路」值得優先查，但不能單憑沒有紀錄排除 API 問題。

HandleError 僅用 X-Requested-With 判 AJAX；沒有此標頭會轉到 DisplayErrorView，一般瀏覽器跟隨後可能得到 HTML。研究中的 owner 刻意不掛載，會看到固定 OwnerNotAttached 診斷，不寫真實 Exception.log、不發 LINE。

## 6. Development 與基底設定差異

此比較使用 Microsoft JSON configuration provider，未載入 User Secrets 或環境變數。基底 JSON 代表 repo 預設，不能直接代表正式站有效設定。

| 節點 | Development 合併後 | 與本路徑的關係 |
|---|---|---|
| CrmConnection | Organization 為 goodshepherdback；ServerUrl 為 HTTPS、測試主機、完整 Organization.svc 路徑；帳密值全部不輸出 | 會影響真實 ToolUtility 資料來源；沒有建立連線驗證資料內容。 |
| DynamicsAccess | Package01FeeReadsEnabled 覆寫為 false；其他節點繼承基底 | 小組讀取直接走 ListManager/ToolUtility，未在兩支 API 找到此費用讀取開關。繼承的其他端點不代表本調查會連線。 |
| Dataverse:Pool | MinSize=2、MaxN=4、AcquireTimeout=10 秒、IdleTimeout=4 秒、HealthInterval=10 秒 | 與基底不同，可能影響實際取數等待；須看請求時序與伺服器輸出，不能以此解釋固定標題消失。 |
| LineMessaging | DefaultOrganization 為 DevTest，基底 channel token 全部被空值遮罩；DevTest 實值來源仍是 User Secrets | 兩個資料 API 的正常讀取不靠 LINE 推播；本調查不讀測試 token，也不發送。 |
| Security | EnforceGlobalAuthorization=false、AllowSessionIdentityFallback=true，均由基底繼承 | 決定前述授權回應矩陣在實際 repo 設定下的適用分支；未更動任何開關。 |
| ExceptionNotifications | WriteExceptionLog=true、SendLine=true，均繼承 | 只比較設定；探測不啟動 Host 或告警 owner。 |

頁面及共用 partial 本身沒有依 Development/Production 省略上述模板或核心腳本的條件。Culture、User Secrets、瀏覽器快取及執行中程序仍屬待觀測項。

## 7. 驗證紀錄與限制

- 新增的唯讀分流探測：11/11 通過。
- 研究探測退出後，已不帶匯入重新建置並通過原有相關測試 30/30；列出測試確認正常 Debug 組件已不含研究 probe。
- 相同探測加既有 ListManager 發布、snapshot 隔離、context 快取隔離、授權與 Session 測試：41/41 通過（含前述 11 項）。
- inline script：6/6 語法解析及禁止網路環境的定義建立成功；document.ready AJAX 與 Razor 元件初始化未執行。
- 生命週期條件探測：DataSource 未就緒會被既有 adapter 拒絕；已就緒可掛載並確定釋放 coordinator。這是條件測試，不是實際瀏覽器重現。
- 沒有做全方案回歸、沒有升級 ImageSharp、沒有執行階段審查；本階段僅研究，不能回報產品已修復。
- 研究探測編譯曾遇到 IConfigurationRoot 的 using 型別問題，僅在研究檔轉為 ConfigurationRoot 後通過；不是產品功能紅燈。大型 JS 基底比較的 Node buffer 上限亦僅修正研究工具。
- 沒有任何產品程式、設定、DevExtreme 版本或 P0-12A 檔案修改。

## 8. 停止點與待使用者提供

第一階段到此停止。請依序提供：

1. F12 Console 第一條紅字及展開後的堆疊。
2. LoadIntegrate、GetChartDataList（及 LoadNewPersonFollowUp）的狀態碼、Content-Type、大小、重新導向鏈與前幾行已遮蔽內容；請保留 JSON 鍵名，移除所有會友資料。
3. JS/CSS 的 404 清單；勾 Disable cache 後結果是否改變。
4. 點擊當下網站主控台的相關輸出，先移除個資與機密。
5. 正式站同一流程是否正常；Codex 不自行連線正式站或測試組織。

收到證據後再區分：P0 退化、P0-ENV 設定/資料差異、既有正式缺陷。若是既有正式缺陷或 DevExtreme 版本問題，先由使用者決定分支或後續做法，不直接修。
