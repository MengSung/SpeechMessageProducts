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
