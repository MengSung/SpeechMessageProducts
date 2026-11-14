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
