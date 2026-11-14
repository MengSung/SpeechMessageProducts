# P5-02 D5-18 判定更正與後續安排

2026-11-14 21:00 Asia/Taipei (UTC+08:00)：依使用者更正，以下現況取代原先把未使用收件識別宣告當成阻擋的判定；舊紀錄保留於下方供追溯，不是現行依據。只記檔名、行號及呼叫形狀，不保存任何 token／LINE ID。

| 入口 | 唯讀核對結果 | 第一輪判定 |
|---|---|---|
| PresentRecordLinePlugIn | PresentRecordLinePlugInClass.cs:75 的 DEVELOPER_LINE_ID 全檔只出現一次，未使用；:205、:230 以三參數且空字串 token 建立 LineUtilityClass | 空 token 必經 LineUtilityClass.cs:108–116 的 SetupChannelAccessToken，:126–143 有寫死正式 token 後備，故不跑第一輪 |
| FeePushLineWorkflow | FeePushLineWorkflowUtility.cs:202 傳入空字串 token | 同上，不跑第一輪 |
| AppointmentSigning | AppointmentSigningWorkflowClass.cs:175 傳入空字串 token | 同上，不跑第一輪 |
| StorLessonSigningLibrary | StorLessonSigningClassLibrary.cs:178、:205、:256、:326 為雙參數呼叫；csproj 指向 LineUtility，但現有 LineUtilityClass.cs:100 只有三參數建構式 | 尚不能證明雙參數執行語意；目前閉包不相容，模組盤點須核對實際二參數版本，確認必走寫死 token 才依 D5-18 跳過；未實跑 |

DEVELOPER_LINE_ID 宣告本身不是拒絕條件；從記錄欄位讀 token 的入口，依 D5-18 第2項同程序填入測試 token 後照常第一輪。若實際推給該識別但使用測試 token，使用者已接受，報告據實記錄。

出席 expected.json 的現行 basis=reading；未執行舊入口及實機第一輪，也沒有 legacy.json。P5-PROP-002 登錄「出席模組在 P7-02 演練時補驗」。使用者另核定在 P5-03 驗收後、P5-05 開始前最多3小時修離線工具：每情境獨立執行域，反射與工作排程可用但必須等待與排空；不可攔截連外 API 與 Process 仍拒絕，Trace／BSUStackTrace／EventLog 寫入轉 scratch。目標為真實 attendance/legacy.json 且 Generated>0；成功才改 legacy-golden，逾時停止並回報。此時間盒尚未開始。

## 歷史紀錄（判定已由上文更正）

# P5-02 D5-18 舊入口盤點與替代基準

2026-11-14 16:35 Asia/Taipei (UTC+08:00)：本紀錄只保存固定檔名、行號、分類與狀態，不保存舊來源內容、Token、LINE ID、商店代號或任何機密值；舊方案仍只讀。

## PresentRecordLinePlugIn

| 檢查項目 | 證據 | 結論 |
|---|---|---|
| 入口 | `PresentRecordLinePlugIn/PresentRecordLinePlugInClass.cs:75` | 原始碼宣告寫死的 LINE 收件識別；值不複製、不回報、不進黃金 |
| 編譯閉包 | `QPayToolkit.cs`、`ToolUtilityClass.cs`、`BSUStackTrace.cs` | 正式 compiler 以 `ExternalExecutionApiRefused`／`ExecutionEscapeRefused` 拒絕，沒有產生組件或執行入口 |
| 可否第一輪實機 | 依 05A D5-18 硬界線 | 不可執行；此入口不得啟用舊步驟或發送第一輪 |
| 替代基準 | D5-3 | 待使用者決定是否將整個 P5-02 改採 `basis=legacy-live-only`；本紀錄不自行宣告已採用 |

## 盤點方法與限制

只讀核定外部舊根，使用固定正規表示式掃描來源，再以正式 net48 compiler 的安全診斷交叉核對；文字掃描是盤點線索，不取代語意編譯。PresentRecord 自身確認一處寫死 LINE 收件識別；未保存其值。未找到可安全產生 `legacy.json` 的編譯結果，因此沒有實機第一輪、沒有第二輪新步驟、沒有正式組織操作，也沒有寫入 `attendance/legacy.json` 或手工補檔。

其他 28 個入口的掃描只作矩陣後續分流參考，不在本文件宣告 D5-18 通關；每一入口仍須以其實際閉包、來源欄位與 `export-legacy` 證據決定。此包不因掃描結果推進 P5-03，也不改現行或封存 v3.4 設計。
