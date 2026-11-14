# 實作計畫

1. 讀取本任務 PRD、05A P5-02、backend deployment/shadow/file-documentation/encoding 規範，盤點舊方案第一層專案。
2. 建立 PluginHarness net48、加入方案，先寫測試骨架：同名、秘密掃描、不連網、失敗不發布、三筆 attendance。
3. 實作 bounded source reader、Roslyn compile/classification、network/write interception、golden writer 與安全摘要。
4. 實跑 36 個第一層舊專案的編譯矩陣；產生 attendance 示範黃金，掃描輸出與 repo 邊界。
5. 執行測試、文件／位元組檢查、固定 guard；寫 wp-reports/P5-02.md、更新進度 DATA，提交並推送驗收分支。

## 2026-11-14 13:18 Asia/Taipei (UTC+08:00) 驗收結果
PluginHarness 已加入方案並建置成功；完整方案 Release build 0 warning/0 error。實際舊方案第一層為37個csproj（計畫文字36個，差異保留），分類0 Generated、6 CompileFailed、31 BlockedByNetworkApi。三筆attendance合成黃金、同名不同穩定ID、兩次輸出SHA相同及秘密／網路安全掃描通過。舊來源未複製、未執行；詳細回報wp-reports/P5-02.md與research/old-project-matrix.md。
