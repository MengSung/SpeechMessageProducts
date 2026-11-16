# NewPersonCare 的 PresentRecord 可達副作用

2026-11-16 10:24 Asia/Taipei (UTC+08:00)。`basis=reading`，來源只讀，未執行退役 DLL；下列驗證皆為合成 SDK，`live=deferred`、`differences=null`。不記錄 token、LINE ID、電話或真實會員內容。

## 舊來源與實際結果

來源根：`../../.其他解決方案/Dynamics365Line/`。

- `NewPersonCareWorkflow/NewPersonCareUtility.cs:161-297`：ExecuteWorkflow 與 Update 可達 `PresentRecordClassLibrary.CreatePresentRecord`，Create 只走分組；PresentRecord 呼叫位於 257、289 行。活動接線由主代理負責。
- `NewPersonCareWorkflow/PresentRecordClassLibrary.cs:29,59-80`：期限偏移為 0、7、14、21、28、35、42、49；只有 contact 的主要小組 lookup 指向名稱含「關懷」的 list 才處理。名稱僅作分類，不能作授權。
- 同檔 `83-104,140`：每輪使用偏移後日期求 DayOfWeek，主日卻用未偏移的 DateTime.Now 計算。八個偏移都是 7 的倍數，穩定日期下主日完全相同；今天週日用今天，週一至週六用下一個週日。不能把註解中的八週想像當成可達行為。
- 同檔 `156-198`：依名單與主日查週報；恰一筆則使用原正式 ID，查無建立週報，再建立目前 contact 的出席。舊多筆當查無的行為會增生衝突，新實作明確拒絕。
- 同檔 `247-286,291-363`：新週報映射組長、上代、區牧、門徒小組、地點、時間、週日／小組日期及四個零值；owner 取組長。新實作在 Create 內帶 owner，保留 team，不再 Create→Retrieve→Assign。
- 同檔 `368-375,385-417`：並非全名單成員批次，只有輸入 contact；按 contact 與正式週報查出席，已有便不寫。因此八輪可達新增結果是一週報加一出席，期限為第一輪今天，而不是八筆不同期限。
- 同檔 `425-495`：出席映射正式 contact、週報、名單與領袖，主日／小組日期、地點時間、出席整數零、比例 double 零、固定「未填」、行動電話、關懷期限及 `new_not_display=false`；出席 owner 實際取會員（408 行），不是註解所稱組長。
- 同檔 `419-422,499-503`：舊 catch 使用 throw e 會重設堆疊。新服務不 catch SDK 例外，保留原例外物件、型別及來源，交活動外殼共用告警處理。

## 既有服務重用與最小新增邊界

- `WeeklyReports/WeeklyReportGenerationService.cs` 的 Eligible 明確排除關懷名單，Generate 讀整批合格名單且只產生本週／下週，不能直接呼叫它來處理單一關懷會員。
- `Attendance/WebhookAttendanceHandler.cs` 只更新既有出席，查無時回 AtomicPresentAndWeeklyCreationRequired；沒有可用的 Create API，不能把 deferred 當成功。
- 新增 `Care/NewPersonPresentRecordService.cs`，API 為 `new NewPersonPresentRecordService(IChurchClock).Execute(IOrganizationService, Guid contactId)`；另有無參數建構式。正式服務借入平台 SDK，無組織／秘密／通知傳輸依賴。
- 沿用 `WeeklyReportGenerationRule.ReportId(church,list,localSunday)` 與 `AttendanceId(report,contact)`，直接作資料庫 Create 正式主鍵，沿用既有產品文字鍵、正式欄位及 ChurchClock，沒有複製 ToolUtility 或新增 schema。
- contact.parentcustomerid(account) 是教會唯一來源；讀正式 list 的組長、上代、區牧及可選副／共同領袖，每個正式 contact 都須與原 contact 同教會。錯型別、錯表、空 ID 或缺必要組長均拒絕。listname 不提供授權。
- 缺週報與出席時一次 ExecuteTransaction 兩個 Create；既有週報缺出席時一次單 Create 交易；既有出席不改值。所有讀取／映射先完成，只有一個發布點。
- 同事件跨程序 Create 由資料庫主鍵唯一約束裁決；競爭失敗原樣傳外殼，重試後讀同一正式結果零寫入。不以應用程式鎖或 Upsert 覆蓋已存在資料來假裝冪等。
- 精確查詢 TopCount=2，零／一筆接受，多筆或 MoreRecords 拒絕；讀回仍核對表、正式 ID、lookup 及主日 UTC 區間。候選最多兩個 Create；同步執行無 cache、Task、timer、訂閱或 SDK owner，SDK 由外殼借用及排空。

## metadata 與有證據的差異

快照：`d365/snapshots/goodshepherdback/20261114-000145-9528323-580561c7a9804d428d6b9a9e73b25b4c/metadata.json`。

- `new_care_expire_date` 已存在（211574 行），UserLocal／DateOnly；`new_not_display` 已存在（212374 行）。不需新增 schema。
- 舊 `new_familyhead_list`、`new_familyhead_present_record`、`new_contact_weekly_report_parents` 在快照不存在；沿用 P5-05 不寫缺 metadata 家長欄位的邊界，不臆造替代欄位。本服務已完成其餘確定可寫部分。
- 日期改為正式教會 `new_timezone` 的當地日期，再將午夜轉 UTC，以吻合 UserLocal／DateOnly；缺時區 fail-closed，不回退伺服器時間。這是有意修正，不宣稱與舊伺服器時分秒逐位元一致。
- 舊既有週報分支不呼叫 SetupCommonParameter，可能留下空領袖／地點；新服務每次重新讀正式 list／領袖，避免依分支遺失映射或跨呼叫狀態。
- 不新增推播：這個可達路徑沒有通知呼叫。生日與年資、活動註冊／Shadow、黃金與接線均屬主代理範圍。

## 驗證

- 先加入測試，net48 Build 紅燈為缺少 NewPersonPresentRecordService（CS0246）；其後才加入服務。
- 使用既有 SDK 11.0.100/MSBuild.dll，未安裝 SDK：Core.Tests net48（產品 net462）及 net10.0 Debug Build 均成功，嚴格分析器開啟。
- 兩目標 VSTest `FullyQualifiedName~NewPersonPresentRecordTests` 均 8/8 通過：可達舊欄位與重跑、既有隨機正式週報與同名不同會員、跨教會領袖、重複週報、未知故障回滾／重試、非關懷與缺教會、重複出席、兩執行同事件競爭／重試排空。
- 交易及唯一性用合成 SDK 模擬，不宣稱正式 D365 併行、權限或效能驗收；新舊 Live 步驟皆不得因本測試而啟用。

- 關聯回歸：同時執行 NewPersonPresentRecordTests、WeeklyReportGenerationTests 與 WebhookAttendanceContractTests，net48 與 net10.0 各 15/15 通過。
- 文件守門 python tools/RepoQuality/check_file_documentation.py：checked=1711、failures=0；指定三檔 UTF-8 無 BOM／純 CRLF／末尾 CRLF 位元組檢查通過，限定路徑 git diff --check 通過。
