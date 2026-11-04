# 小組回報空白問題修復

## 目標與最初範圍

依使用者 2026-11-03 核定，第一階段僅做唯讀分流調查；完成三個最可能原因與判別方法後停止，不修改產品程式、不調整 DevExtreme 版本。沿用本任務骨架；P0-12A 暫停在已提交的 d42b6d3f，待本任務結束再接續。

目前狀態（2026-11-04）：使用者確認小組回報恢復，階段 0 第 ⑧ 批與第 09 份 ClaudeOnly 複審通過，無未處理 Critical／High，依核定收尾封存。前次兩次 Claude 逾時保留為歷史；圖表未逐項驗證，正式線移植與 JSZip 匯出風險待使用者決定。完整結果見 research/fix-report.md 最後一節；以下第一階段紀錄保留其當時範圍。

## 研究紀錄：使用者提供的線索（尚未由 Codex 觀測執行中的網站）

- 執行 P0 工作區 Debug/net10.0、19:25 建置，環境 Development，組織 goodshepherdback。
- Exception.log 僅有 12:21 缺少 Dataverse Username 的舊例外；本次重現未新增伺服器例外。
- 73b6e6cf..HEAD 的 ChurchReport 變更屬設定載入與專案設定；SmallGroup 控制器、IntegrateView、_GeneralGroupGrids、wwwroot JS 及 DevExtreme 版本未修改。
- 伺服器 DevExtreme 23.1.5、用戶端 22.1.6 的落差為既有情況，不能據此直接歸因或升降版本。
- 畫面固定欄位標題未顯示；圖表有刻度但無日期與數字，標題及圖例正常。優先分辨腳本初始化、格式化或 API 回應問題，不可直接視為沒有成員資料。

## 調查要求

1. 追蹤全域模板、lookup data source、腳本載入順序及 Development/Production 差異。
2. 追蹤 LoadIntegrate、圖表 API、Session/授權中介層與 HandleError 的回應與通知路徑。
3. 以既有替身及純假資料確認成員存在但該週無點名、Session 遺失、API 錯誤的回應；不得連線 D365/LINE。
4. 比較 CrmConnection、DynamicsAccess、Dataverse Pool 與 LineMessaging DevTest 的有效設定差異；只能記錄白名單欄位、型別或布林結果，不輸出帳密/token/個資。
5. 回報三個優先原因與各自判別方式；瀏覽器證據到齊前不宣告根因。

## 驗收條件

- [x] 每項推論附可定位的本機原始碼或本機假資料重現證據，區分使用者線索、已驗證事實與待確認假設。
- [x] 列出全域 JS 定義、載入順序、圖表欄位/文化設定與 API 回應矩陣。
- [x] 比較必要的環境設定，輸出不包含機密或會友個資。
- [x] 三個最可能原因均有可操作的判別方法。
- [x] 產品程式、設定、DevExtreme 版本及 P0-12A 已提交內容完全未變更。
- [x] 結束時停下等待使用者提供已遮蔽的瀏覽器證據及決定修復歸屬。

## 待使用者提供

F12 紅字、LoadIntegrate/圖表請求狀態碼與回應格式/大小、靜態資源 404、點擊當下網站主控台輸出、正式站同小組是否正常。回應內容須先移除所有會友個資與機密。

## 修復歸屬（尚未進入修復）

P0 退化在本任務修復；測試資料/Development 設定歸 P0-ENV；既有正式環境缺陷需使用者決定分支。所有修復另依判定取得授權，不放寬守門。

## 固定限制

禁止 Codex 連線 D365/LINE；不讀正式金鑰、不推送、不合併、不改寫歷史；只用合成假資料，不將真實姓名、電話、LINE ID 或機密寫入任何研究產物。

## 第一階段結果

研究報告：research/triage-report.md；可重現探測：research/ApiResponseProbes.cs 與 ProbeImport.targets；結構化結果：research/results.json。三個優先原因尚待瀏覽器證據確認；未進入修復。

## 追加核定：進入修復（2026-11-03）

使用者提供 Console 截圖後，明確要求「你自己去找錯誤，並解決」。本任務沿用目前 P0 分支實作最小修正，取代第一階段唯讀限制；P0-12A 仍暫停。使用者另回報同日早上正常，須區分 Git 原始碼引入時間與執行中實際載入版本，不能把既有程式缺陷直接說成 P0 退化。

已確認截圖有兩次 `this.store(...).load(...).done is not a function`；lookup 三支載入成功，並有 CLDR/Globalize/JS/CSS 404。截圖中的小組名稱與實際 ID 不記錄。Git blame 顯示 coordinator 的原生 Promise 於 c9a1f7db53（2026-09-09）加入，Grid adapter 於 527ae45bb8 同日接上；早上實際載入的程式版本與快取狀態尚未取得。

本機瀏覽器工具回報 Local browser use is disabled in Work settings。不得繞過設定或改用真實 D365/LINE；以實際產品腳本、現有 jQuery/DevExtreme 及假資料驗證。不能宣稱已驗證真實網站全部恢復。

### 修復驗收

- 先以實際 Razor adapter 與現有 jQuery Deferred 重現 `.done` 契約紅燈，再修正轉綠。
- 保留分頁 totalCount、summary 等多個完成參數，不能只修方法存在而丟失資料來源中繼資料。
- 保留 generation、abort、refresh 合併、元件 dispose 與跨 owner 隔離；晚到成功及錯誤均不得發布。
- 核對缺失靜態資源及圖表問題；只有取得可重現證據才做對應修改，不改 DevExtreme 版本。
- 既有相關測試、建置及編碼守門通過；不混入 P0-12A、不連線 D365/LINE、不推送、不合併、不改寫歷史。
