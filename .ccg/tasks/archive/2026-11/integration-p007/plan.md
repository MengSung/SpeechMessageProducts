# P0-07 實作計畫（2026-11-03 核定後接續）

- [x] 讀 AGENTS.md、3.3 README／00／01／附錄 E 及相關規範；使用者明確解除公式不一致停止條件。
- [x] 僅修正 01 的 P0-07 範例、附錄 E.3 與進度 DATA；不改設計文件、3.2 或歷史 WP 回報。
- [x] 新增 tools/I18nCheck 與 tools/I18nCheck.Tests（不進方案，兩者 csproj 本體開啟嚴格模式及 XML）。使用中央 CSharp 5.9.0／既定測試套件；還原失敗即停，不換版本。
- [x] 先以 NotImplementedException 空殼執行原列 12 個測試（偽語系 7 筆 Theory），保存真實紅燈；增加邊界、CLI 及資源清理測試。
- [x] 分批實作 Roslyn／Razor 掃描、方案邊界、專案棘輪、resx key／參數驗證、Rune 偽語系與決定性 C# 產生器；範圍為 scan／--write-baseline／generate／--check／verify。
- [x] 基準僅含計數與相對檔名，不保存原始字串；輸出固定排序、無 BOM、CRLF，暫存檔由單一命令有界持有並釋放；不連外部產品服務。
- [x] 完整 00 §8、工具測試與覆蓋率 >=80%、位元組及凍結副本檢查；回報前 10 名計數與所有設計勘誤位置。
- [x] 更新任務完成清單與進度、依核定格式提交；提交前不得開始 P0-08。

## 檔案範圍
- 新增：tools/I18nCheck/{csproj,掃描與資源處理 C#,baselines/cjk-literals.json,README.md}、tools/I18nCheck.Tests/{csproj,測試與暫存資源 fixture}。
- 修改：上述核定的兩份 3.3 計畫文字與進度 DATA；本包 task／implement／wp-reports／wp-evidence；必要時規範回饋。
- 12 個原測試名稱原樣保留，偽語系為 7 筆 InlineData；增加的測試及理由在回報列出。

## 新停止條件
中央套件已成功還原。測試尚未執行：保留的 12 個方法名稱各觸發 CA1707；必須核定命名規則的測試限定例外才可接續，禁止以移除嚴格開關、改名或略過測試繞過。功能皆為 NotImplementedException 空殼。

## 完成結果
使用者已核定 CA1707 只限 I18nCheck.Tests，真正紅燈 18／58、補充邊界紅燈 4＋DTD 1＋尾端 LF 1，最終 75／75、覆蓋率超過 96%。完整方案與 §8 均通過；本單包任務於 P0-07 提交內歸檔，父任務仍進行中，沒有逐 WP 外部審查。
