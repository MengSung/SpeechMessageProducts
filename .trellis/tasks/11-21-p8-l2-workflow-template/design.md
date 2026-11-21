# L2 設計：Workflow 活動樣板

## 範圍與信任邊界

只延伸 `SpeechMessage.Dynamics.Deployment` 與 `SpeechMessage.Dynamics.Deployment.Tests`。部署 CLI 的既有 catalog、ProductionGuard、User Secrets、唯一 SDK lease 及命令生命週期維持唯一連線入口。新增唯讀 shape 擷取只允許 catalog 中的 `goodshepherdback`，且只投影停用流程中核准的新舊自訂活動 ActivityReference 節點；原始 XAML 僅在單一同步呼叫內記憶體處理，禁止寫入、回顯、記錄或放入例外本文。

## 方案

1. 以有界 XML reader 建立結構投影器，禁止 DTD／外部資源、限制文件大小與深度。投影保留 namespace、元素／屬性名稱及固定值、實體欄位兩類繫結的節點形狀；所有 ID、型別識別、名稱、字串值及非白名單屬性替換為固定佔位符。只在唯一候選且確認停用時輸出精簡 XML golden。
2. `WorkflowTemplateBuilder` 對傳入的 activity identity、primary entity、輸入 map 與 metadata catalog 做完整 ordinal 驗證。catalog 由部署 DLL 的 `PEReader`／`MetadataReader` 取得；不 `Assembly.Load`，不執行 Plugin／Activity 程式碼，並驗證 assembly name、版本、public key token、完整型別及 `[Input]` 屬性名稱／CLR 型別。
3. Builder 將已驗證情境值映射成經 golden 結構核實的 XAML input expressions，產生單一自訂 ActivityReference、單一步驟、on-demand、全部自動觸發關閉的 draft XML。不得把任意 caller 字串當 XAML 片段。
4. 將 `LiveActivityProbe` 改為本輪流程 owner：先驗證活動、輸入與記錄 ID，再預配 Workflow ID；Create 前設定 attempted，僅用該 ID Create／查詢／停用／刪除。Execute 回傳正式 `asyncoperation` ID，60 秒 deadline 有界輪詢作業狀態；取消只中止等待，finally cleanup 用 `CancellationToken.None`。清理失敗不得覆蓋原失敗，且不得按名稱刪除或接觸任何既有流程。
5. CLI 準備階段以情境 trigger inputs 對照新 DLL metadata；完整才將活動模板交給 round，缺少、額外或錯型輸入仍回 `LIVE-PREP-09`。樣板建構／啟用錯誤映射 `LIVE-PREP-10`，不變更持久原因碼。

## 測試策略

先建立並執行 08A 明列的十個失敗測試。Golden shape fixture 由唯讀實機投影取得後，離線測試驗精確 XAML 結構、型別與版本、輸入正反例、PREP-09 前進／拒絕，以及執行生命週期、timeout、SDK fault、建立回應遺失和取消清理。假 SDK 記錄每個正式 ID 與取消 token；所有離線測試均不得聲稱平台接受 XAML。

## 主要風險與控制

- XAML 結構不符 9.1：只以唯讀觀察的已脫敏 shape golden 驗證；實機拒絕最多依 08A 修正一次。
- DLL metadata 的屬性解碼不完整：使用 PE metadata，不反射載入；對未知 constructor/type/property shape fail closed，並以產生的 net462 package assembly 測試。
- 非預期舊流程執行：不沿用舊 XAML 作為 draft，不對來源 workflow 做任何 Update／SetState／Delete；只建立本輪新流程。
- 取消／回應遺失造成孤兒流程：預配主鍵、write-ahead attempted、以 None cleanup、回讀精確 ID；無法證明零殘留不報成功。
- 跨組織／洩漏：只走現有 catalog 授權和 SDK lease；golden、輸出與報告不保存原始 XAML、流程名、資料值或秘密。
