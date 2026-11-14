# P5-02a 局部驗證與 PresentRecord 阻擋紀錄

2026-11-14 15:50 Asia/Taipei (UTC+08:00) 更新。本文件是開發機離線紀錄，不是舊入口黃金、實機第一輪或整包驗收。

## 已完成的小段

- 移除使用者撤回的六份 attendance-*-legacy.json／attendance-*-expected.json 及伴隨說明；保留 scenarios.json，沒有手寫 legacy.json 或 expected.json 補數。
- 修正來源切片草稿的 CA1861、合成初始化把 void 指派至 object、缺少 this() 目標建構子及空 namespace 預期錯誤；沒有把切片接入正式 compiler，也沒有擴充反射支援。
- 工具、CLI、假服務、矩陣及來源切片局部測試 157/157，無略過。此數字包括 b 段既有測試，不能據此聲稱 b 已驗收。
- 進度頁兩個指定入口合計 38/38；全庫檔案說明檢查 1519 檔、0 失敗。維護來源已核對 UTF-8 無 BOM、CRLF 及末尾 CRLF。
- 綠燈小段提交 37e54bf8，未推送。覆蓋率 80%、出席黃金與固定 guard 尚未完成。

## 正式 generate 嘗試

2026-11-14 15:46 Asia/Taipei (UTC+08:00)：以正式 CLI、核定唯讀舊根、module=PresentRecordLinePlugIn 與既有四情境 scenarios.json 執行 generate；退出 1，固定錯誤 CompileFailed，候選 legacy.json 不存在。沒有執行舊入口、真實 LINE 或 D365 寫入。

後續 CompileProbe 只讀診斷依序為：

- HarnessApiRefused:LineUtilityClass.cs
- ExternalExecutionApiRefused:QPayToolkit.cs
- ExecutionEscapeRefused:QPayToolkit.cs
- ExternalExecutionApiRefused:ToolUtilityClass.cs
- ExternalExecutionApiRefused:BSUStackTrace.cs

現有完整編譯閉包包含反射、外部設定與實體 CRM 入口；另外 LineUtility 的固定 guard 拒絕尚須釐清，不能把全部診斷說成原始碼錯誤。沒有以放寬 guard、移除原始來源、手改黃金或將空結果判為成功來繞過。

## 後續邊界

原定本輪最晚判定點為 2026-11-14 17:25 Asia/Taipei (UTC+08:00)，尚未到期；目前提前確認照既有管線不能產生黃金，後續需另行變更 harness 的支援範圍。依使用者第 5 點，停下 PresentRecord 擴充並交使用者決定是否整包改走 D5-3，不將其說成時間盒已耗盡，也不自行驗收 a。

a 仍為安全工具加 PresentRecord 至少三情境的真實黃金；b 為 36 專案／29 入口矩陣與查詢補強，排除零來源 Dynamics365Line 殼，P5-13 維持 36。若改走 D5-3，仍須先核對 D5-18；實際必經寫死 token 的入口不跑實機第一輪，依規劃處理替代基準並揭露未實跑。

兩處設計 v3.4 均保留且未修改；歷史未改寫；沒有中途呼叫外部審查。後續推送必須該包驗收及固定 guard 通過，再以 git ls-remote 核對。階段末才做外部審查。
