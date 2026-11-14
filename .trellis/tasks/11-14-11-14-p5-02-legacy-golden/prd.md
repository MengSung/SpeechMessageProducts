# P5-02 舊程式黃金資料產生器

## Goal
依 05A P5-02 與 D5-3/D5-13，建立可重現、離線、不可連網且不暴露秘密的舊程式盤點／黃金資料工具，供 P5-04 起逐模組比對。

## Requirements
- 來源只讀工作樹外 `.其他解決方案\Dynamics365Line` 的第一層舊專案；不讀 `.worktrees`、backup、bin、obj、packages 原始碼。
- 建立 net48 `tools/LegacyGolden/PluginHarness` 並立即加入 `SpeechMessageProducts.sln`。
- 舊入口逐專案編譯結果分類為 Generated、CompileFailed、BlockedByNetworkApi；不可把固定 token、LINE ID、密鑰或個資寫入輸出。
- 示範出席情境至少三筆 legacy/expected/declaredDifferences 所需黃金資料，輸出掃描與同名穩定性測試必須通過。
- 真實舊來源若無法在 harness 安全執行，依 D5-3 使用實機第一輪；若 D5-18 例外則只做離線黃金並於回報列明。

## Acceptance Criteria
- [ ] PluginHarness 建置成功並列入方案一次，新增檔案具繁中檔首說明、UTF-8 無 BOM、CRLF/final CRLF。
- [ ] 測試覆蓋不連網、輸出秘密掃描、失敗不發布、同名情境穩定性及至少三筆 attendance 黃金資料。
- [ ] 36 個第一層舊專案的編譯／阻擋結果表完成，未複製舊來源進 repo。
- [ ] 產物與 report 通過固定 guard；只提交本包範圍，並推送本階段分支後核對 SHA。
