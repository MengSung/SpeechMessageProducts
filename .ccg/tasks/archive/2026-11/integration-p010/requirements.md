# P0-10 要求

依實作計畫 3.3 的 01 P0-10 建立 net48 DiagnosticsPingPlugin；原七個測試及兩個已核定例外測試全部保留。
外掛只保存不可變步驟設定，執行內容／結果／追蹤皆為操作區域狀態；Core 是唯一 HTTP 出口，沒有 notifier、檔案日誌或額外管理者 LINE。
合併 Localization、Core、Newtonsoft 並內部化；Xrm SDK 不併入。由環境變數指定的正式金鑰簽章，固定 AssemblyVersion=1.0.0.0。
打包守門以 System.Reflection.Metadata 讀取、不執行 DLL，檢查六項契約。所有測試離線，正式金鑰內容不讀取、不複製、不輸出。
保留舊金鑰本機檔案、移除指定兩檔的 Git 追蹤，不動 nsspi key。
