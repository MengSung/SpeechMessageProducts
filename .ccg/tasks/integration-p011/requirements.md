# P0-11 範圍
依實作計畫 3.3 的 01 P0-11，建立 Windows GitHub CI，包含完整方案雙目標測試、Debug tracer、全部本機守門與新專案行覆蓋率 ≥80%。不推送，故不宣稱 GitHub 實跑。
沒有 secret 時明確跳過正式打包；有 secret 僅寫 runner 暫存目錄，獨立 always 步驟清除。不得建立 GitHub secret、輸出或上傳金鑰。
所有測試本機離線執行，不連 D365／LINE；既有測試保留。
