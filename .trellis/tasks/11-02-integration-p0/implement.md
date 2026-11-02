# 階段 0 執行清單

依原計畫執行；一次只開始一個 WP，前一包驗證及提交後才開始下一包。

- [x] P0-01 開分支、Trellis 任務、保存 SDK／建置／測試基準與已授權修復；驗證已通過。
- [x] P0-02 gitignore 與 global.json；設定及完整驗證已通過。
- [x] P0-03 CPM 與版本統一；套件守門紅燈 113 項、綠燈 0 項，完整驗證通過。
- [x] P0-04 新專案分析器與禁用 API；原 13 項守門加 3 項 XML 文件檢查，共 16 項，Release 與 Debug tracer 全數通過。
- [x] P0-05 編碼工具；編碼測試 21／21（PS 5.1／7）、進度頁 3／3、All 基準及全階段 Changed 驗證通過，已提交 ba565a55。
- [x] P0-SEC 已提交 24cca555；25 紅→綠、Release 766／766、Debug tracer 7／7。
- [x] P0-ENV 已提交 aa1de29d；五個設定紅→綠、Release 771／771、Debug tracer 7／7。
- [x] P0-06 已提交 18e901b1；兩引擎各 14／14，19 專案 851 檔、重複基準 0。
- [x] P0-07 保留十二個方法與七筆核定資料；75／75、行覆蓋率 96.73%、CLI 與完整方案驗證通過，提交收尾。
- [ ] P0-08 Localization 骨架；先跑雙目標取字、不可變與併行配置測試。
- [ ] P0-09 單人推播骨架；先跑原計畫全部假 handler、黃金檔及並行憑證隔離測試。
- [ ] P0-10 Plugin、合併與簽章；先跑 Plugin 契約及打包檢查測試。
- [ ] P0-11 Windows CI；逐步執行與 workflow 相同的驗證。
- [ ] P0-12 佈署專案最小版與離線測試；完成後 ClaudeOnly 階段審查。
- [ ] P0-13 清理回報及審查通過後，寫佈署專案驗收步驟，停止等使用者執行。

## 每包固定守門
1. 重讀 AGENTS.md、00、該包與相關附錄，確認現況與檔案；列出檔案及測試清單。
2. 先跑紅燈，再實作與重構、跑綠燈；P0-01 原計畫明訂不適用。
3. 執行 dotnet build SpeechMessageProducts.sln -c Release -warnaserror:NU1605。
4. 執行 dotnet test SpeechMessageProducts.sln -c Release --no-build。
5. 工具建立後加入編碼 Changed（Base=73b6e6cf409daa7559fd3c11d482e3efc542c0d7）、重複程式、I18nCheck verify。
6. 依附錄 F 記錄結果；通過後提交，標題為 WP 編號及繁中摘要，結尾 Co-Authored-By: Codex。

## 審查與停止
M+ 分析／審查使用 Start-CcgDualModelRun.ps1 自癒入口；依使用者最新授權只執行 Claude，記錄實際結果。
本環境指定 inline 模式，實作與檢查由主會話直接執行。
遇 §9 停止條件、NuGet 網路或 net48 建置限制即保存原始錯誤並停止該包。
目前完成 P0-01～P0-05；每包提交後才依序啟動下一包，尚無產品外部系統操作。

## P0-01 已授權的前置測試修復
- [x] 保存原始 24 個失敗；重跑 Release 紅燈。
- [x] 以實際產品組件修復正向與負向型別查詢，對齊方案及產品來源路徑。
- [x] 保留既有四個 tracer 案例，分別驗證 Debug 寫檔釋放與 Release 無檔案副作用。
- [x] §8 完整 Release 建置／測試、Debug tracer 測試、編碼與範圍檢查全部通過。
- [x] Claude 人工審查與 P0-01 回報完成，已提交 90799969；接續 P0-02。

一個歷史案例由 Legacy_qpay_manager_remains_as_compatibility_alias 改名為 Legacy_qpay_manager_does_not_remain_as_compatibility_alias；案例數不變。
原案例因大量改名而把 manager 與自己比較；本次依既有移除相容包裝的決策與同專案唯一 manager 測試，改為確認舊 QpayManager 不存在。

## 最新人工審查處理規則
- P0-01 已收到 Claude 單模型人工審查；指定修正不再外部複審。
- W1-W3、I2/I4/I5/I6 已修，I1/I3 記錄，I7 的 *.trx 忽略規則在 P0-02 完成。
- P0-05 的 ps1 非 ASCII 只能在註解；P0-11 必須有 Debug tracer 及 CCG 摘要契約測試。
- WP 之間只跑本機驗證，P0-12 後、P0-13 前才做一次階段審查；失敗或逾時才準備包並停下。

## P0-04 載入時點修正（已核定）
P0-03 已提交 d1c92d83。P0-04 在 Directory.Build.props 載入時讀不到 csproj 內的 SpeechMessageStrictCode 啟用值；已保存隔離 MSBuild 證據，使用者已核定修正。
修正提案：將受開關控制的嚴格設定放到 Directory.Build.targets，其他範圍及驗收不變；已先更新權威計畫，接續原 TDD 流程。

## P0-04 文件產生補核修正
使用者已核定 Claude 單模型人工補充核對；初版 13 項漏驗 XML 產出，已先留 4 項紅燈、修正為 16／16，另做 P0-04 提交保留 1cf66341 歷史。P0-05 只有 16 項紅燈與未實作空殼先保留，本次不提交。

## P0-ENV 安全調查停止
P0-05 已提交 ba565a55。LineUtilityClass 靜態設定只讀 appsettings.json，忽略 Development 與 User Secrets；只改設定不足以保證不取用基底 token。依使用者 P0-ENV(c) 停止，未改產品程式／設定，未連線任何 D365，未開始 P0-06。詳見 wp-reports/P0-ENV.md。

## P0-SEC 使用者核定與新停止原因
使用者已明確核定先做 P0-SEC，再接續 P0-ENV；舊安全範圍待決已有授權。新的本機隔離探針證明 WebApplication 在兩個環境變數同時存在時優先採 DOTNET_ENVIRONMENT，與核定文字相反。依 00 §9.6 停止，等待僅修正這一行優先順序；未改產品程式，未連線外部系統。詳見 wp-reports/P0-SEC.md。

## 最新 3.3 接續狀態
使用者已核定環境順序與 3.3 計畫版本，原兩項停止條件均解除。文件改版已提交 3f8db349；P0-SEC 本機實作及完整測試全綠，編碼及範圍收尾後提交。

## P0-07 命名分析器停止（2026-11-03）
偽語系公式與 Rune 定義已獲核定；12 個原測試名稱保留，偽語系已改為 7 筆 Theory。中央 CSharp 5.9.0／xunit 2.6.6 成功還原，但新嚴格測試專案的 Recommended 分析器產生 12 個 CA1707，要求刪除方法名稱底線，與原計畫及本次不改名指示衝突。另有一個 Assert.EndsWith 需指定 Ordinal 的 CA1307，屬普通測試程式修正，不需要規則例外。依 00 §9.5／§9.6 停止，未停用檢查、未開始功能實作或宣稱功能紅燈，未進入 P0-08。詳見 P0-07 回報。

## P0-07 兩項核定解除與驗證
偽語系公式及僅 I18nCheck.Tests 的 CA1707 例外均已由使用者明確核定。原測試 18 個功能紅燈，邊界 58 個紅燈後分批轉綠，補充路徑／DTD／尾端 LF 回歸後 75／75；Release 771／771、Debug tracer 7／7、建置 77 既有警告。CJK 基準 11 專案 817 檔，共 3895；其餘結果見 P0-07 回報。
