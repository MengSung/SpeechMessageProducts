# .NET 11 專案效能對照研究 Implementation Plan

執行方式：主會話 inline；不 dispatch implement/check 子代理。需求依 requirements.md；不變更正式目標框架、SDK、業務邏輯與安全政策。

1. 分析：讀取實際呼叫鏈與官方效能文章，雙模型檢查公平性、可量測範圍及潛在誤判。
2. 快照：以獨立 artifacts path 還原／建置既有 Webhook 專案 Release，記錄相關來源與二進位 hash，不建立新 csproj；保留原產品輸出。
3. 量測工具：以既有 SDK csc 編譯 task snapshot 的離線 harness，參考上述真實產品組件；用明確 runtimeconfig 選擇 10.0.15／11.0.0，同一份 IL 做 runtime-only 對照。
4. 正確性：用合成頻道／不同使用者及錯誤簽章／重複身分驗證輸出、隔離、拒絕及 registration owner 排空；micro checksum 必須一致。
5. 微基準：實際驗簽、事件解析、Newtonsoft Flex template 及多語／資料整理；先暖機，再多程序 ABBA 配對，保存 ns/op、B/op、GC 與所有樣本，限制迴圈／記憶體／總時間。
6. HTTP：真實產品 endpoint + 明載合成 dedup／queue；loopback Kestrel，固定 client runtime；0ms 及合成延遲情境，保存吞吐、延遲百分位、CPU、配置與 owner 基準。每輪停止服務並檢查 process 清理。
7. 分析：配對比值與變異區間、不抹除退步，Amdahl 情境推估明示本地占比假設；檢查套件釘版本與 Framework 路徑不適用性。
8. 審查：雙模型核對方法、程式與報告，修正可重現問題；文件／語法／正確性／位元組檢查，封存 task 並僅提交本次研究檔案，不推送 P5 分支。

檔案歸屬：task.json、requirements.md、plan.md、harness/*.cs、run_research.py、analyze_results.py、report.md、結果摘要及 raw evidence companion 說明；原始量測輸出位於忽略 artifacts/net11-performance-research。
