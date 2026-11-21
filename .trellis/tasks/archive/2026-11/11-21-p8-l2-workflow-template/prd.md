# 遺留 L2：修訂 1 範圍

## 核定目標

依 2026-11-21 使用者核定的 08A 修訂 1，以離線縮減範圍結案 L2：保留能編譯、有測試覆蓋且全方案 Release 通過的 `ActivityInputContractReader`、`WorkflowActivityShapeProjector`、strong-name／輸入契約驗證、相關 CLI 與測試。

## 不納入本次驗收

- 不建置或宣稱完成 Workflow XAML 樣板產生、`LIVE-PREP-09`／`10` 新流程及 `LiveActivityProbe` lifecycle。
- 不以合成 shape 取代 goodshepherdback 真實脫敏 golden；停止 workflow shape 實機讀取。
- 不補造 Appointment Workflow 情境或輸入值，不修改七活動 registration 的 tier/evidence。
- 七活動維持 dormant；樣板產生、實機補驗與 `off`／`verify=0`／殘留 0 核對列入好牧人 9.1 上線前作業。
- 未完成或沒有測試覆蓋的 `WorkflowTemplateBuilder.cs` 及其 diff 備份至使用者目錄 evidence，不提交。

## 驗收條件

- 離線保留範圍可編譯且具測試覆蓋。
- 全方案 Release 測試完整通過，保留完整當次輸出。
- 一頁 L2 縮減結案回報、Trellis 父子紀錄、`P8-decisions.md` 與進度頁 DATA 說法一致。
- 只對本單元檔案提交，執行核准完整守門、推送 P8 分支並以 `git ls-remote` 核實 SHA；不連正式組織、不做 workflow shape 實機讀取。
