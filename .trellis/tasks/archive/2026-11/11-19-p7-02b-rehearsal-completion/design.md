# 技術與安全邊界
主session inline實作與檢查，不派implement/check子代理。沿現有Shadow SDK factory/runtime與Live runner；新增命令區域固定原因碼例外，單行有界安全識別，保留原例外給共用notifier與SDK數字碼，正常caller取消不包裝。
所有來源先讀、繁中檔首及成員說明、UTF-8無BOM/CRLF。沿既有alias跳脫規則，不輸出Message/Data/TraceText/ErrorDetails或資料內容。
回復及殘留由既有owner finally/None token處理，診斷不得改寫步驟或放寬metadata。manifest/catalog從switch到rollback保持bytes不變。
先跑自癒雙模型分析；本次Claude可用、Gemini兩次503，ok=false/dualModelComplete=false，非額度降級，不冒稱雙模型完成。實作後雙模型安全審查；最後階段審查按使用者ClaudeOnly，標記「單模型（使用者指定 ClaudeOnly）」。

## 使用者核定的範圍縮減

2026-11-19 09:11 Asia/Taipei (UTC+08:00)：使用者核定的範圍縮減（2026-11-19）。影響07A §5 R8、R5a重演、附錄乙、手冊正式組織章節：R8僅以本次R5b switch-log rollback一次及verify off=0，不與R1 export／export-legacy或流程定義逐位元組比對；R5a不重演，R5b只重新切換一次供R6；不新增或擴充正式組織確認關卡、正式組織手冊章節、附錄乙核定書範例。既有ProductionGuard、--production、stdin組織名確認及rollback程式保持不變；保留P7_組織切換手冊.md與進度契約字串；不改07A。全程只goodshepherdback，不啟用舊程式、不啟動階段8。每模組90分鐘／最多2次實機；準備／環境失敗不得dormant，合法候選及pass證據均R8回復後才登錄。
