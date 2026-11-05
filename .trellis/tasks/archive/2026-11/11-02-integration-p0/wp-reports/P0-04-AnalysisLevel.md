# P0-04 補修　net48 套用與 net10 相同的 Recommended 規則

## 範圍
- Directory.Build.targets：嚴格群組在 AnalysisLevel 空白時補 latest，net10 原值與舊專案設定不變。
- Test-AnalyzerProbe.ps1：保留原 20 項，加 net48 CA1707／CA1716 與兩目標分析層級檢查，共 24 項。
- 新增 InterfaceNamingProbe.cs：只建置、不發布的 Get 介面故障注入，完整繁中 XML 文件。
- 同步 01 的 P0-04 與 strict-analyzer-contract.md，記錄實際規則載入契約。
- 本提交不含未追蹤的 P0-08 產品、測試或證據；先前 P0-08 原始紅燈保留。

## TDD 紀錄
- 原 20 項保留，新增 4、刪除 0。
- 紅燈：24 項中 3 失敗。net48 AnalysisLevel／EffectiveAnalysisLevel 空白，兩個命名樣本皆建置成功，未出現應有的 CA1707／CA1716（red.*）。
- **補修前 net48 的 Recommended 規則沒有實際生效。** 先前 net48 的 RS0030／文化禁令探測只證明強制規則正常，不代表完整 Recommended 已載入。
- 綠燈：24 項、0 失敗。net48 兩個樣本分別只有 CA1707／CA1716，兩框架 EffectiveAnalysisLevel 非空；屬性前後值見 levels.json（green.*）。
- 原 net48 RS0030 相容探測仍只有預期錯誤；沒有修改或抑制 Polyfills，也沒有新增 NoWarn。
- 黃金檔：無。

## 驗證
- I18nCheck.Tests 75／75；Release 771／771；Debug tracer 7／7。
- 方案 Release 建置 0 錯誤、77 既有警告，沒有增加警告上限。
- 重複掃描 19 專案 851 檔，群組／新增／過期為 0；I18nCheck verify 通過。
- 最終編碼、進度頁、差異、凍結副本與 P0-08 保留雜湊記錄在 final-gates.json。
- 覆蓋率不適用：本包改建置規則與故障注入，所有案例均真實建置，不執行產品邏輯。

## AGENTS.md 檢查
- 新 C# 故障注入介面及方法使用繁中 XML 註解，明列 Get 必須保留以驗證 CA1716，不是產品 API。
- ps1 新非 ASCII 僅在註解；沿用有界程序、雙串流讀取、逾時終止及 Dispose；每案獨立 fixture 子目錄並在 finally 內驗證清理邊界。
- 沒有跨使用者狀態、網路 client、產品告警或背景工作變更；所有驗證離線，不連 D365／LINE。

## 風險與待決
- 舊的 P0-04／命名補充回報是當時實測紀錄，不改寫；本補修明確補足之前 net48 的分析規則缺口。
- P0-08 的 API 改名已核定，但必須等本獨立提交完成後才做，不能把仍為空殼的來源混入本包。
- AGENTS.md 引用的隔離規範檔在本分支缺少，使用者已確認本次不搬移，依 AGENTS.md 條文執行。

## 需要使用者做的事
- 無；完成獨立提交後接續核定的 GetText／richMenuAlias 與雙目標 TDD。
