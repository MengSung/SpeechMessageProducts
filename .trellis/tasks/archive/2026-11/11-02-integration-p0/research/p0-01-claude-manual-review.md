# P0-01 外部審查與使用者處理指示

審查性質：Claude 單模型人工審查（使用者交付）。未經自癒入口，沒有 Gemini 結果。
審查對象：.ccg/dual-model-runs/p01-review-packet.md。
來源差異 SHA256：9098504f6265f682fb32334239d8ca3be21da41c7b18aef8dd627e44fa510418；本次修改前重新計算完全相符。
Critical：無。使用者明確授權按下列要求修正後本機驗證及提交，本次不再外部複審。

| 項目 | 處理 | 結果 |
|---|---|---|
| W1 | 已修 | 新增 reviewMode／dualModelComplete，保留 ok 的要求完成語意；JSON、Markdown、AGENTS 指定段落、指南與 00 §6.4 一致。PowerShell 6 案例紅轉綠。 |
| W2 | 已修 | 00 §8 每包驗證與 01 P0-11 CI 步驟都加入 Debug tracer 命令；本次 Debug 7／7，之後 P0-11 必須實際納入 workflow。 |
| W3 | 已修 | manifest helper 先定位最近的 SpeechMessageProducts.sln，只在該根目錄查檔，缺檔直接失敗；View defaults 移除舊方案後備標記。 |
| I1 | 僅記錄／需求已列 | 原 Invoke 腳本 BOM 移除符合 repo 編碼規則；PowerShell 5.1 可能將非 ASCII 註解顯示為亂碼。P0-05 已追加 ps1 非 ASCII 字元僅可出現在註解的 token 檢查需求。 |
| I2 | 已修 | diagnostic 保留 wrapper 自身 stderr 診斷，另記 probeDiagnostic 與 probe 路徑；獨立 probe 成功不代表原呼叫成功。 |
| I3 | 僅記錄 | 反射查詢區分大小寫且固定命名空間；QpayManager 不涵蓋 QPayManager，現有原始碼掃描補防。組件錨點重複，主 manager 存在性由強型別錨點保證；Controller 斷言仍有意義。本次不重構。 |
| I4 | 已修 | 兩個重複案例的 XML remarks 已互相標註，保留全部歷史案例與原有測試數。 |
| I5 | 已修 | 四個類別摘要依 DTO、Controller、依賴組件邊界、解析／序列化／驗證／分類及服務委派等實際內容分別重寫。 |
| I6 | 已修 | 同一 fixture 第二次配置路徑立即丟 InvalidOperationException；清理最多 5 次、等待最多 4×100ms，逾限明確失敗。新增 3 案例先紅後綠。 |
| I7 | 已處理提交範圍 | 不提交任何 .trx 原始檔，只保存 md／json／txt 摘要與文字證據。P0-02 依使用者指示再加入附繁中註解的 *.trx 忽略規則。 |

後續每個 WP 以本機驗證為準；只在 P0-11 完成後、P0-12 之前做一次階段外部審查。入口失敗或逾時時才準備審查包並停下。
