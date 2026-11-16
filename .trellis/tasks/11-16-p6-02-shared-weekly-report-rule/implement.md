# P6-02 執行清單

設計完全依 06A：純分類判斷向下集中到 StatisticsRule，計算旗標取固定 zh-TW 原文；
原登入者即時建立與 D365 主鍵、日期、owner／副作用均不搬移。

1. 先新增四個指定同名測試與純方法骨架，取得有效紅燈；既有前綴 accessor 案直接綠亦據實記錄。
2. 逐一暫改方法名稱建置全方案（含測試），保存錯誤／成功證據，再依結果刪除或委派。
3. 最小實作分類方法，Evaluate 委派；六處正式旗標替換，保留已註解的舊行。
4. 更新檔首與 README，跑三個指定測試集合與兩 DLL 打包檢查；期望黃金不改。
5. 編碼／文件／DATA 檢查後程式提交；固定 C:/smv/guard 完整 00 §8.1。
6. 一頁回報與五項驗收、封存及紀錄提交輕量三項；明確推送後 ls-remote 核對。

已完整讀 WeeklyReportProcessor、三個 QR 工具、StatisticsRule、WeeklyReports README；
修改測試前再確認其完整內容。所有新增實作無 I/O、無可變共享狀態或資源 owner。
前包 P6-01 完整守門已通過，不重跑。

## 2026-11-16 19:46 Asia/Taipei (UTC+08:00) 紅綠與呼叫者證據

- Core 分類案例在兩目標各 1 紅，既有前綴各 1 綠；來源測試修正缺少 Roslyn namespace 的編譯問題後，取得有效 2 紅。實作後四個指定名稱共 6 個實例全綠。
- 三個方法依序暫改宣告後，以完整 Release 方案（含測試）建置：GetValidMemberNumber、IsValidMember、IsValidContact 各 exit 0。每次恢復名稱後才刪該無呼叫方法，再做下一個探針；沒有用搜尋找呼叫者。
- 刪除三方法及只供 IsValidContact 使用的 EMPTY_VALUE；ConvertIndexToClearIdentity 仍在建立出席單時使用，因此保留。建立流程未改。
- 六處正式旗標改用固定 zh-TW accessor，原已註解行不改；Core 兩宿主各 371、Plugins 615、ChurchReport 736 全綠。
- 舊黃金期望檔未修改；新規則無 I/O、資源 owner 或跨操作可變狀態，沒有新增需另立規格的架構模式。

本機原始輸出（artifacts/p6，不進版控）：
- `p6-02-core-red.log`：`14527d1c6ff6f708f1541f9d9ce77ab321212646d934e3df65d887b2604ce872`
- `p6-02-source-red.log`：`b9d1e34726ba38108775d0b8fa7d31afea6467ef1370d318025df7176d424342`
- `p6-02-core-green.log`：`9d60cbaa6bca3abccd4e2898a35bf2b9e1585eec926fa9efb56e5c2d55c503a7`
- `p6-02-source-green.log`：`9e2b19be369deb88c1d8896c34dd5bbbd0fa2c43e08879436253bc044569119e`
- `p6-02-compile-probe-GetValidMemberNumber.log`：`77e7de673e387562a4e6141d816fe87b09b82314d9f79ae83173749593cca41c`
- `p6-02-compile-probe-IsValidMember.log`：`7cfdc8d5999a1560ee4954a9e6a0ca57bd0fc00921bc5a97ee1294f7fbeb158b`
- `p6-02-compile-probe-IsValidContact.log`：`d207ac0282043f78aec5104e44ac4fc4dc44e61d2a825ea252e2c2a586f09029`

## 2026-11-16 19:47 Asia/Taipei (UTC+08:00) 預提交確認

- 兩個沙箱 DLL 各打包一次；Test-PluginAssembly.ps1 回 0，兩筆 ok=true，net462／強式名稱／參考／facade／大小皆通過，沒有連 D365。
- 編碼51檔、文件1750檔、進度頁28案及 git diff --check 全通過。期望黃金與 NoWarn 未改。
- 下一步程式提交後固定 guard；本包尚未驗收或推送。
- `p6-02-package-plugins.log`：`a58baa5f3c417f8336b507340aa2e1b00d4dfd4aa6d8243362a62c64231b329a`
- `p6-02-package-workflows.log`：`9fc9bd71b5a372a255fab19e496bd0d6c33168bb54df5562262087f0d7fb68d0`
- `p6-02-package-metadata.log`：`a0f56db6c3f7104eed02967e0dee4bc5e8a295ee7ce9011ea748dddbea60a72e`
