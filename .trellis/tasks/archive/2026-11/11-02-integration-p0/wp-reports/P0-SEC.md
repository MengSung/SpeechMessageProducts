# P0-SEC　防止 Development 使用正式 LINE token 與非測試組織（完成實作與本機驗證）

## 範圍
- 新增：ChurchReport/Configuration/ChurchReportDevelopmentConfiguration.cs — internal 覆寫層、可注入參數的純環境解析及啟動守門，不保存全域機密或 request 狀態。
- 新增：ChurchReport.MemberInfo.Tests/Contracts/DevelopmentSafetyTests.cs — 25 個假設定／來源契約案例。
- 修改：已盤點的 11 個 ChurchReport LINE 設定讀取點 — 接上覆寫層；原 optional、reloadOnChange、static／Lazy 結構逐一核對未變，詳 source-audit.json。
- 修改：LineMessagingProcessorClass 的預設設定讀取 — Development 只加 optional JSON，不參考 ChurchReport、不加主機 User Secrets；原環境變數來源順序不變，區域組態根讀完 Dispose。
- 修改：Program、ChurchReport.csproj、appsettings.Development.json — 守門先於任何 LINE sender／CRM 服務；固定 UserSecretsId、DevTest 預設別名、兩個正式 token 空白覆寫。
- 修改：Trellis／CCG 執行記錄、3.3 進度 DATA、Development 設定安全規範 — 實際狀態及 3.3 的 P0-12／P0-13 順序；3.2 凍結及已完成 WP 歷史回報不動。
- 未做：DI 重構、17 處 Jesus 退回統一、收件者白名單、基底正式機密移除、付款回呼變更；未修改 Startup 或清理 D365。

## TDD 紀錄
- 原清單全部保留，測試刪除 0。包含 Development 大小寫、Production／Staging／缺省環境逐鍵相同且零新 provider、兩組環境衝突與缺省、11＋1 來源路徑、UserSecretsId、正確／錯誤 LINE／CRM／Embedded 設定與 Production 完全略過守門。
- 追加拒絕測試：把基底 token 移到測試別名、同值 LINE_CHANNEL_ACCESS_TOKEN、非法布林開關，避免已知設定來源的繞過。
- 紅燈：25／25 失敗，功能空殼 NotImplementedException、UserSecretsId 缺少與接點缺少；red-tests.txt 保存方法名稱與訊息。最初測試字串跳脫造成的編譯錯誤已先修正，未冒充功能紅燈。
- 綠燈：25／25；重構僅補精確文件、驗證 fixture 清理路徑與保留基底選項，全方案最新 766／766 通過。
- 測試皆用 PROD-FAKE／PROD-B-FAKE，無實值機密；組態來源測試使用沒有 UserSecretsId 的測試組件，不讀使用者真實 secrets、不改程序環境或目前目錄。
- 黃金檔變更：無。
- 歷史：執行期間出現外部提交 a7604add，已包含測試空殼與紅燈等當時檔案；保留歷史，不 reset／rebase／amend。本次 P0-SEC 提交包含完成實作與綠燈證據，不把空殼提交當完成。

## 驗證
- Release 建置：0 錯誤、76 警告（增量建置；基準 77，既有 CS1572 不一定重報，未加 NoWarn 或降低檢查）。
- Release 全方案：766 通過、0 失敗、0 略過（原 741 加本包 25）。LineMessagingProcessor.Tests 的既有 33 個完整保留。
- Debug tracer：7／7。
- 來源核對：11 個基底 AddJsonFile 選項未變，每處一個 Development 接點；長憑證形狀字面值候選 0。現有方案來源未找到參數為空的 new LineMessagingProcessorClass() 呼叫者；此相容預設入口仍受測試，ChurchReport 實際呼叫者以 token 或既有 client 明確傳入。
- 進度頁 3／3；git diff --check 及暫存差異檢查通過。編碼首次守門發現 a7604add 已帶入的 v3.3 討論紀錄為 LF；本次只轉成 CRLF 並補檔尾 CRLF，逐行文字完全相同，不動 3.2 凍結副本。最終 Changed（Base=73b6e6cf）159 檔、排除 74 檔、0 問題；本次 36 個新增／修改檔均通過逐位元組檢查。
- 新專案覆蓋率：不適用；沒有新增產品專案。
- 沒有啟動 ChurchReport、查詢 D365、呼叫 LINE 或讀取 User Secrets 實值。

## AGENTS.md 檢查
- 新設定層及測試、實際修改的設定欄位與接點均補繁中 XML 文件；說明 Production 不變、fail closed、部署狀態與 request／教會狀態的分界。
- 新增層不建立長生命週期快取、事件或背景工作；既有 static／Lazy 限行程部署設定，新增 provider 不 reload；守門的基底串流與組態根均在當次結束前 Dispose。
- 守門放在例外通知 owner 建立前；失敗沿 Program 原有初始化固定 stderr 路徑處理，不發 LINE、不記錄設定值，也不改通知管線。
- 只比較記憶體中的 token，錯誤僅含鍵名與固定原因；不把 token／密碼／個資帶到日誌、證據或回報。

## 風險與待決
- P0-SEC 提交後、P0-ENV 完成前，Development 因 CRM 仍指向舊組織而拒絕啟動，屬預期 fail closed；不以實際啟動網站驗證。
- 本包保留基底正式機密及舊金流回呼；輪替由使用者處理，P4 前 Development 不得實測付款。
- 3.3 與 DOTNET_ENVIRONMENT 優先順序已核定，先前兩個待決已解除。
- goodshepherdback 清理尚未回報完成；任何 D365／LINE 連線仍禁止。即使清理完成，Codex 只可執行 D24 的不寫入指令。
- 外部審查依最新規則留在 P0-12 後、P0-13 前，使用單模型（使用者指定 ClaudeOnly），P0-SEC 列為重點；本包不另做外部審查。

## 需要使用者做的事
- 無。本包提交後接續 P0-ENV；goodshepherdback 清理完成仍須另行回報，不在本包連線。

---

## 先前停止時的歷史紀錄（原文保留；待決均已由後續核定解除）

# P0-SEC　防止 Development 使用正式 LINE token 與非測試組織（實作前停止）

## 範圍
- 新增：.ccg/tasks/integration-p0-sec/ 下的 task.json、requirements.md、plan.md — 登錄已由使用者核定的獨立安全工作包。
- 新增：research/p0-sec-environment-precedence.md — 本機隔離探針、兩組矛盾與單行修正建議。
- 新增：wp-evidence/P0-SEC/environment-precedence-probe.json — 只含假環境名稱與結果，無機密。
- 修改：CCG／Trellis 任務狀態、implement.md、實作進度.html 的核定 DATA 欄位 — 記錄 P0-SEC 新授權與新停止原因；先前 P0-ENV 停止證據保留。
- 未做與原因：未修改任何產品 .cs、csproj 或 appsettings；未把有矛盾的優先順序寫入 01／03 計畫，未開始設定 TDD。核對即觸發 00 §9.6，故停止且不提交完成。

## TDD 紀錄
- 保留核定的全部測試：Development 各別名取不到基底假 token、Production／未設定逐鍵不變且不加 provider、12 個讀取點來源架構守門、未覆寫 token／錯誤預設別名／錯誤 CRM／正確設定／Production 略過啟動守門。
- 建議增加：兩個環境變數相互衝突的兩組純函式測試。原測試刪除 0 個。
- 紅燈／綠燈：尚未開始產品 TDD；可行性探針的 2／5 不一致不能冒充產品測試紅燈，另 3 組一致也不能冒充本包綠燈。
- 黃金檔變更：無。

## 驗證
- 來源掃描：讀取兩個指定目錄中的 287 個 Git 追蹤 C# 檔；長憑證形狀字串候選 0 個，另外兩個 token/client 字面值命中確認是 GetChannelAccessToken 的 Jesus／JesusBack 別名引數。此為預查，不宣稱完成整包安全稽核。
- 實際組合根：Program.cs:68 使用 WebApplication.CreateBuilder(args)。
- 隔離探針：SDK 10.0.403，建置結束碼 0，五個子程序均結束碼 0；兩組衝突均證明 DOTNET_ENVIRONMENT 優先。
- 沒有啟動 ChurchReport／Host、建立 CRM／LINE client、讀 User Secrets、修改父程序環境或連線任何 D365／LINE。
- 本包未完成，沒有重跑 Release 全方案建置／測試或宣稱全綠。停止紀錄驗證：進度頁 Node 3／3；Changed（Base=73b6e6cf）113 檔、排除 74 檔、0 問題；全部 13 個修改／新增檔通過位元組檢查，JSON 可解析，git diff --check 通過。未追蹤新檔由直接位元組檢查涵蓋，沒有冒稱已納入 Changed。
- 新專案覆蓋率：不適用；沒有新產品專案。

## AGENTS.md 檢查
- 本次只改任務、研究、證據及進度 DATA，沒有新增或修改產品 C#。
- 探針每案獨立子程序，ConfigurationManager 於結束前 Dispose，程序均等候結束；沒有執行網站、建立外部 client、保留使用者／教會資料或機密。
- 未改例外通知管線；安全核對不建立 LINE 告警出口。
- 文件與狀態須維持 UTF-8 無 BOM、CRLF、檔尾 CRLF。

## 風險與待決
- 原核定文字的 ASPNETCORE_ENVIRONMENT 優先，與 net10 WebApplication Host 的實際規則相反；可能在 Host 為 Development 時跳過舊路徑的安全覆寫，也可能改變 Production 的設定來源。
- 建議只改為 DOTNET_ENVIRONMENT 優先，其餘核定內容維持不變。
- goodshepherdback 清理尚未回報完成；所有 D365／LINE 連線仍禁止。後續 P0-12 也須先取得使用者清理完成回報。
- 使用者交付的 Claude 核對屬人工單模型意見；本次未執行外部模型審查，不宣稱雙模型或階段審查完成。

## 需要使用者做的事
- 核定環境判斷改為「先 DOTNET_ENVIRONMENT，再 ASPNETCORE_ENVIRONMENT，皆未設定則 Production」，其餘 P0-SEC 範圍與驗收不變；核定後直接接續 TDD，不需提供機密。

## 環境順序核定後的接續紀錄
- 使用者已明確核定依網站現有規則修正，goodshepherdback 維持開發測試組織；DOTNET_ENVIRONMENT 優先的原待決已解除，不再要求重複確認。
- 接續時 HEAD 為 06487668；保留外部提交歷史，不 reset／rebase／amend。
- 在更新 01 計畫前，原「文件/規劃設計書/實作計畫」已不存在；Git 顯示原八個計畫／進度檔刪除，另有未追蹤「實作計畫-3.2」與「實作計畫-3.3」。這不是本次腳本建立或移動的資料夾。
- 本次腳本在讀取原 01 檔案時即 FileNotFoundError 結束；僅先更新了三個任務 JSON，未寫入任何版本計畫或產品程式，未開始產品 TDD。
- 已詢問使用者採用版本。新版本內容尚未比較，不把資料夾名稱當成核定依據；不還原、移動或覆蓋使用者外部變動。
- 本次不執行依賴原路徑的進度頁測試，也不宣稱完整 WP 驗證通過；等待版本指定後接續。
