# P0-09　共用例外通知的相依與責任缺口

## 核對基準
- 分支：1.0.0.11.Integration.P0；HEAD：92cfa180。
- 這是離線閱讀與屬性求值，沒有執行 LINE 或 D365 請求。
- P0-08 已完成。本研究未改產品程式或 3.3 的核定計畫文字。

## 可重現的現況
1. AGENTS.md 的「永久例外 LINE 通知規則」要求：失敗 catch 要呼叫可注入共用 notifier；傳真實 Exception，不可只傳 ToString；內部 timeout 不能當正常 caller 取消略過。
2. 01 的 P0-09 明定 timeout 轉成 LinePushResult 的 Retryable；目前沒有原始例外傳遞或 notifier 接入規格。
3. 00 §3.4：雙目標專案不能參考只有 net10 的專案，也不能參考 Microsoft.Extensions.*。
4. 實際入口在 ToolUtility/Diagnostics/ExceptionReporting.cs，轉接至 ExceptionDiagnostics.Report；ToolUtility/ToolUtility.csproj 只有 net10.0。以 `dotnet msbuild ToolUtility/ToolUtility.csproj -getProperty:TargetFramework -getProperty:TargetFrameworks` 求值得到 net10.0／空值。
5. ExceptionReporting.cs 的類別摘要要求「最終失敗邊界」上報；可恢復重試不在此上報。.trellis/spec/backend/error-handling.md 同樣排除成功恢復的 retry。
6. ExceptionDiagnostics.cs 依賴 System.Text.Json、System.Threading.Channels；沒有找到現成的雙目標共用 notifier 介面。不能只讓 Core 的 net10 分支有通知，net48 靜默。
7. P2-01 的 SpeechMessage.Hosting 為 net10，該包另有共用通知搬移與完整生命週期驗證，不能拿「之後會有」當現在已接入。
8. P0-10 計畫使用 LinePushClient 並將 Retryable 包成 InvalidPluginExecutionException，未定義跨框架共用 owner。此處需要一併定義實際責任，不能只在 Core 增加一個可選且從未接入的 callback。

## 停止依據
00 §9.6：「發現本計畫與 v3.3 設計、或與程式現況有矛盾。」本次只確認計畫與現有共用入口的相依／責任缺口，沒有宣稱設計原檔有新的錯誤。

## 建議核定的修正範圍
先補 P0-09／P0-10 的跨框架共用例外回報契約，必要時建立雙目標的最小介面，明定：
- 原始 Exception 如何跨結果傳遞、最長生命週期及不洩露訊息／個資；
- 可恢復重試何時不通知、最終失敗由誰恰好上報一次；
- 正常 caller 取消與內部 timeout／網路失敗的區分；
- net10 現有 owner 與 net48 Plugin 的實際接入位置及輸出契約；
- 通知管道自己的 LINE 故障不得遞迴通知，也不得取代原始失敗；
- 注入依賴的所有權、無全域使用者狀態、可用假實作在兩個目標本機測試。

這些項目待使用者核定／Claude 核對，尚未選定 API 名稱或修改計畫。不直接搬 P2-01、放寬分析器、建立第二套 LINE 告警實作，或設置 no-op 預設。

## 核定後接續
先同步 3.3 計畫及本包執行清單，再寫原 11 個測試與必要的例外／取消／重試防遞迴測試；兩目標都取得功能紅燈後實作。既有 net10 的 33 個案例完整保留，不連線任何外部組織。

## 使用者核定解除（2026-11-03）
上述是前次停止時的歷史分析，已由使用者核對設計並糾正：這不是新架構，設計 10.5／10.9 與計畫 04 §5.4 已有完整分工，只是本 WP 未重述。依既有分工做：Core 無 notifier、以 TransportException 保留原始傳輸例外；Plugin 追蹤後拋出；net10 最終邊界及階段 5 彙總器才通知。沒有採用本研究先前建議的新雙目標回報介面。
後續先查 Word 第十章及 02～04，再判斷是否真有矛盾，避免把分階段安排誤判為缺少架構。本次同步 01 與 error-handling 契約後，已接續雙目標 TDD，結果見 P0-09 回報。
