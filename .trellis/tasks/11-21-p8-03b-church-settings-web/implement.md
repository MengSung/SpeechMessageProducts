# P8-03b 實作計畫

## 固定依據

- 執行清單：`文件/規劃設計書/實作計畫-3.5/08A_階段8_執行細則.md` §4.4、§5 P8-03b、§8 與 D8-12、D8-16、D8-19、D8-25、D8-26。
- 先完成 P8-03b，再依固定順序進入遺留 L1；不與其他工作包平行。
- 每個程式單元採紅 → 綠 → 重構；指定測試名稱不得改名。測試只用離線 fake／合成資料。
- 單元內不跑 CCG 雙模型分析或外部審查；階段結束只跑一次 `-ClaudeOnly`。

## 執行步驟

1. 對續跑 verify、父任務 JSON 修正與既有證據完成決策紀錄；保留父子 Trellis 關聯。
2. 先建立並逐一執行以下十個 `ChurchReport.MemberInfo.Tests` 測試，記錄因缺少 P8-03b 行為而紅燈的結果：
   - `Save_NonAdmin_Forbidden`
   - `Save_ChurchIdFromRequestIgnored`
   - `Save_ScriptInput_EncodedOnOutput`
   - `Get_NonAdmin_Forbidden`
   - `Get_LoginWithoutChurch_Forbidden`
   - `Post_WithoutAntiforgery_Rejected`
   - `ChurchSettings_QpsPloc_AllVisibleTextFromResource`
   - `ChurchSettings_ProductionRejectsQpsPloc`
   - `OfferingCategories_PageShowsOnlyThisChurch`
   - `Audit_PageShowsOnlyThisChurch`
3. 先實作可重複呼叫且無跨 request 狀態的 `ChurchSettingsAccess`，每次 action 都以伺服器登入 contact 欄位呼叫 `MemberInfoAccessResolver`，只承認 `Church`，並從該 contact 的 account parent reference 取得教會。輸入教會 ID 不得參與授權或資料查詢。
4. 實作 Area Controller 與三頁：
   - Terms 白名單、系統原文／教會覆寫／預覽、存檔與還原。
   - OfferingCategories 該教會新增／改名／啟停／排序／會計科目／舊選項值。
   - Audit 本教會最近 200 筆、修改者只顯示 D365 display name。
   - 每個 POST antiforgery；Razor 動態值自動編碼；JSON 不手工拼接。
5. 新增 `ChurchSettingsText.resx` 的繁中資源，再執行 `dotnet run --project tools/I18nCheck -- generate --check`／`verify`；不得手改 generated `.g.cs`。依實際資源配置完成 qps-ploc 可見文字與 Production 拒絕測試。
6. 以服務端權限結果在既有 Layout／navigation 呈現本地化入口；不將導覽可見性當授權。
7. 將所有新增／實質修改的 `.cs`、`.cshtml` 加完整繁中檔案整體說明、成員文件；保持 UTF-8 無 BOM、CRLF 與尾端 CRLF；核對 JSON 伴隨說明是否需要更新。
8. 逐步跑指定十個測試；確認全綠後執行：
   - `dotnet test ChurchReport.MemberInfo.Tests -c Release`
   - `dotnet test SpeechMessageProducts.GoodShepherd.Core.Tests -c Release`
   - `dotnet test SpeechMessageProducts.LineWebhook.Tests -c Release`
   - `dotnet test SpeechMessage.Hosting.Tests -c Release --filter "FullyQualifiedName~Architecture"`
   - `dotnet run --project tools/I18nCheck -- verify`
   - `python tools/RepoQuality/check_file_documentation.py`
   - 必要的 `Test-TextEncoding.ps1 -Scope Changed -Base <階段8起點>` 及 `.trellis` 記錄文件檢查。
9. 依 00 §8.3 做提交前紀錄型檢查；程式提交訊息以 `P8-03b` 開頭。提交後只用既有 `C:\smv\guard-p8` checkout 新 SHA 重用並執行完整 00 §8.1，證據只寫 `C:\smv\guard-p8-evidence`。
10. 通過守門後寫一頁 `wp-reports/P8-03b-教會設定網頁.md`，核對本階段新增 diff 不含秘密，更新父任務 implement 與進度頁 DATA、標記單元驗收；推送 P8 分支並用 `git ls-remote` 核對 SHA。

## 主要風險與回復點

- 授權不得依賴 Session 正向快取、ViewBag、route 或 request church ID；若伺服器登入 contact / `parentcustomerid` 來源無法確定，先沿現有登入資料路徑確認，不能以猜測的欄位或角色放行。
- 教會作用域必須同時貫穿 service context、資料 store 和稽核查詢；測試以 A/B 資料斷言非本教會內容不會到達 view。
- 頁面輸入不得進入 raw HTML 或手工 JSON；測試以含 script 標記的合成值檢查實際輸出編碼。
- 修改範圍限 P8-03b 直接相關的 ChurchReport、測試、資源、任務紀錄、回報及進度 DATA；不碰 08A 計畫正文。
- 若需修正程式，只追加 `P8-03b fix:` 提交並重新執行唯一 guard-p8 完整守門；不得改寫已推送歷史。
