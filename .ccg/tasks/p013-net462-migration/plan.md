# P0-13 net462 正式變更（使用者 2026-11-04 核定）

## 核定與邊界

- 正式採用 net462，取代先前「只做隔離試做」核定。
- 根因高度可信：伺服器事件指向 `TargetFrameworkVersionValidator.ValidateInternal()`，但 net462 能否註冊仍須使用者重跑 D365；Codex 不執行線上指令。
- 保留測試執行器可用的 net48 測試目標；只把會進沙箱的產品組件改為 net462。
- 不讀取或輸出 User Secrets、簽章金鑰或 Six Labors 授權內容；不推送、合併或改寫歷史。

## 執行清單

- [x] 已完成隔離相容試驗；122 個測試通過，不等於實機註冊成功。
- [x] 三個產品專案已改為 Plugins `net462`、Core／Localization `net462;net10.0`；net48 測試專案保留並檢查引用的實際組件。
- [x] Framework 條件已由 `NET48` 改用 `NETFRAMEWORK`；實際載入測試確認 Core 的 net462 Framework 專用建構式可用。
- [x] 打包檢查器只接受 net462，且以反向樣本拒絕 net48；四個合併輸入另有 v4.6.2 上限檢查。
- [x] 修正 PluginPackaging 子建置先 Restore 再 Build，並用 Release 打包實證，不依賴方案還原留下的工具資產。
- [x] 還原三個產品專案並盤點 net462 的每個套件編譯／執行資產；記錄四個合併輸入的框架版本及 facade 判斷，不合併 facade。
- [x] 同步 00／D21、01～04、90、Trellis 套件規範、驗收步驟、進度頁與調查報告第 8 節；歷史事件與已提交決策保留為當時紀錄。
- [x] 跑 Plugins、Core、Localization、Deployment 相關測試、PluginPackaging 測試、Release 簽章打包及檢查。
- [x] 跑完整本機 00 §8：全方案 Release build/test（含 ChurchReport）、Debug tracer、編碼、重複碼、I18n、套件與分析器守門。
- [x] 依既有規定以自癒入口完成第 10 份 ClaudeOnly 初審與 High 修正獨立複審；修正後重跑相關及完整驗證，無未處理 Critical／High。
- [ ] 驗證、審查通過後以核定訊息提交；把重建、重新 plan、apply、verify、smoke 與收尾交由使用者操作。

## 審查輸入

Claude 獨立查核 net462 遷移差異，特別檢查：D365 目標框架拒絕的根因推論是否有界線、條件編譯是否走 Framework 分支、net48 測試是否引用 net462 產品輸出、NuGet 資產與 facade 是否被誤併入、打包目標是否可在乾淨還原狀態執行，以及文件是否區分 Plugin 目標與伺服器 mscorlib 4.8.x。
