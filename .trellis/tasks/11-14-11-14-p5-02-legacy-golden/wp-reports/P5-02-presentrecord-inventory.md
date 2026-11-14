# P5-02 PresentRecordLinePlugIn 盤點（D5-3 前置）

2026-11-14 16:43 Asia/Taipei (UTC+08:00)：本頁只記錄舊來源的結構與行號，不保存寫死的 LINE 收件識別值、Token、完整訊息或舊資料；來源位於核定外部只讀根。這是 D5-3 前置盤點，不是實機結果、黃金檔或 P5-02 驗收。

## 入口與觸發

| 項目 | 證據 | 盤點結果 |
|---|---|---|
| 入口類型 | `PresentRecordLinePlugIn/PresentRecordLinePlugInClass.cs:class PresentRecordLinePlugInClass` | 舊入口實作含 `IServiceProvider`、`IPluginExecutionContext` 與 `ITracingService` 的三參數 Execute；目前完整閉包未通過安全編譯 |
| 支援訊息 | 同檔 `Execute` 的 Update／Create 分支 | 只進入 `Update` 或 `Create`；其他訊息沒有業務處理 |
| 目標實體 | `:36`、`:377`、`:407` | `new_present_record`；非該 logical name 的 Target 直接返回 |
| 必要映像 | `:398`–`:406` | 讀 `PreEntityImages["PresentRecordLineImage"]`；缺少映像會進例外路徑，不應猜測或補造 |
| 深度／遞迴 | `Execute`、`MajorExecute` | 來源沒有可證明的 depth guard；D5-3 應將此列為相容性與風險觀察，不能自行宣稱安全 |

## Update 行為

| 位置 | 讀取／條件 | 寫入／對外效果 |
|---|---|---|
| `:236`–`:251` | 讀 `new_paster_reply`，與前映像同欄位比較 | 不同才組訊息、查 `new_contact_new_present_record`、讀 contact 的 `new_lineid`，呼叫 multicast LINE；相同時不推播 |
| `:253`–`:271` | 讀 `new_explanation`、contact lookup 顯示名稱與回覆 | 組合代禱事項及牧者回應文字；完整訊息不進本 repo／報告 |
| `:265`–`:271` | lookup 欄位 `new_contact_new_present_record` | 目前只加該 contact 的 LINE 收件識別；名單及其他領袖路徑是註解，不列為實際效果 |
| `:474` | 工具物件可更新 retrieved entity | 目前主流程未呼叫 `SetupSavedFlag`；不能把註解或未呼叫方法列為實際寫入 |

## Create 行為

| 位置 | 讀取／條件 | 實際效果 |
|---|---|---|
| `:216`–`:224` | 讀 `directioncode` | true／false 分支的推播與入站處理均為註解呼叫；隨後返回 |
| `:204`–`:211` | 先讀回 `new_present_record` | 仍會建立 CRM service、建立 ToolUtility／LineUtility、查詢目標；完整閉包拒絕前不作執行推論 |

## 例外、資源與 D5-18

- 多數 catch 建立含型別、時間與例外全文的字串但不輸出，之後以 `throw e` 重拋；D5-3 只能記固定例外型別，不得保存例外全文或時間。
- `Dispose` 會嘗試釋放 ToolUtility／LineUtility；catch 內吞 Dispose 失敗並保留 disposed flag。這是舊行為觀察，不代表符合新資源契約。
- `:75` 宣告寫死 LINE 收件識別。其值已在 `p5-02-d5-18-disposition.md` 中刻意省略；依 D5-18 不得跑舊步驟第一輪，也不得以其作為實機基準。
- 正式 compiler 的完整閉包仍因 `QPayToolkit.cs`、`ToolUtilityClass.cs`、`BSUStackTrace.cs` 的安全拒絕而沒有組件、沒有入口執行、沒有 `attendance/legacy.json`。

## 後續決策邊界

若使用者核定整包採 D5-3，下一步才可依上述行號建立不含舊執行結果的 `expected.json`，每筆替代差異必須引用舊檔名與行號，並以 `basis=legacy-live-only` 明確標示；在核定前不建立該替代黃金、不推送、不進 P5-03。若不核定，仍需另行核准安全支援範圍，不能為產生檔案而放寬反射／外部 CRM／寫死收件識別的 guard。
