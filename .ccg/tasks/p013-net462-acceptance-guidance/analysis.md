# P0-13 verify 欄位故障分析與修復計畫

## 證據與根因
使用者 net462 apply 成功，verify 隨後回報兩層 0x80041103 與 exit 1。Microsoft 定義 QueryBuilderNoAttribute，官方步驟安全設定查閱欄位為 sdkmessageprocessingstepsecureconfigid；部署程式在 8 處讀寫誤用 secureconfigid。首次 plan/apply 沒有既有型別/步驟可查，trace 建立又不寫安全查閱，故首次成功後 verify 才觸發。現場根因以使用者修正後重試為準。

來源：
- https://learn.microsoft.com/en-us/power-apps/developer/data-platform/reference/web-service-error-codes （0x80041103）
- https://learn.microsoft.com/en-us/power-apps/developer/data-platform/reference/entities/sdkmessageprocessingstep （SdkMessageProcessingStepSecureConfigId）

## 最小修復計畫
1. 假 IOrganizationService 依獨立固定的官方步驟欄位集合，對查詢欄位/條件、Create/Update、Retrieve 拒絕未知名稱；不從產品常數衍生 schema。
2. 先紅：實際 Plan/Apply 後 verify 比對、既有官方 secure lookup 的匯出 presence/設定轉換/原列刪除；既有秘密寫入斷言使用官方名稱。
3. 後綠：DeploymentEngine 用單一有繁中契約的常數，改正所有查閱欄位讀寫；secureconfig 內容欄位不變，不讀安全設定實值，不改 Plugin 或清單。
4. 離線測試、覆蓋率、全方案 §8、ClaudeOnly 新審查，不冒稱第 10 份已涵蓋。
5. 同步驗收文件、調查報告與 Deployment spec；提交後只請使用者重建 Deployment、重試 verify，不重打 Plugin 或重複 apply。

## 不變邊界
Codex 不連線 D365/LINE；不讀 appsettings、User Secrets、export、金鑰或授權內容；不推送、不合併、不改寫歷史。Trellis 主任務繼續等待 P0-13 實機與收尾，不能封存為完成。

## TDD 與分析
4 個欄位邊界案例及真正 CLI 生命周期案例已在修正前取得 5 個功能紅燈：Plan/Export 的未知欄位 FaultException、CreateStep 未知欄位拒絕，CLI 首次 verify 預期 0 實得 1。CLI 測試使用獨立暫存方案及未簽章生成 DLL，隔離集合禁止並行，finally 恢復程序目錄，不讀真實設定。
ClaudeOnly 分析於 20261104-201612-p013-verify-schema-analysis-analyzer 完成：reviewMode=claude-only、dualModelComplete=false、quotaBlocked=false、ok=true。確認全部 8 處錯字，已提出 Critical（推播寫入）/High（既有步驟查詢）由同一欄位修正處理；真實重新驗證仍由使用者操作。
