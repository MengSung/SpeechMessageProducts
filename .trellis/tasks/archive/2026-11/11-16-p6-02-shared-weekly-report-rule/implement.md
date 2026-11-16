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

## 2026-11-16 20:06 Asia/Taipei (UTC+08:00) 驗收與固定守門

- 程式提交 `9bf222edc50c598794987621d31980db46bbf190` 的第一次完整 guard 在全方案測試第 2 項失敗：圖片 renderer 案例讀取 ImageSharp 全行程未釋放配置計數，另一個預設可平行的真 PNG 發布案例在觀察期間配置了 21 個資源。原始失敗保留於 `guard-9bf222ed-full.log`，不得視作該提交已通過。
- 修正提交 `8859c6981146b9537454e5c28d673cc3ade88e90` 僅將 renderer 計數案例加入既有 `QR image lifecycle` 非平行 collection，維持逐次繪圖、PNG／Image 確定性 Dispose、原始零增量斷言及取消後同一基線斷言。未改產品程式、未放寬期待、未使用 GC/finalizer；collection 只隔離全行程診斷觀察時段，沒有停用案例內的 A/B 併行。
- 修正後 `LineMessagingProcessor.RichMenus.Tests` 114/114 通過；固定 guard 對修正提交八項完整通過：Release 建置 0 錯誤（既有 115 警告）、全方案 Release 測試 7,158/7,158、Debug tracer 7/7、編碼檢查 53 檔 0 問題、重複碼新增 0、I18nCheck 0、檔案說明 1,750 檔 0 失敗、進度頁 28/28。
- guard 準備記錄 `guard-8859c698-prepare.log` SHA-256：`213ec4e16697bcfe7a51fce7d26a0ea6f6541ec82fea96b5563d8fa7ef3be1bd`；完整記錄 `guard-8859c698-full.log` SHA-256：`0c60f4cdc6b8e2acb82f9b64d13759913ddc14cca3b5f65f42d5cda796711537`。
- 五項完成定義：指定測試與既有回歸通過；固定 guard 八項通過；無新專案，覆蓋率不適用；本包未要求實機，頁面實機集中於階段末；本一頁回報與 DATA 更新完成。頁面驗收尚未進行，不以本包離線結果代替。
- P6-02 已於上述時間驗收；程式提交與修正提交將一併推送，並另以 `git ls-remote` 記錄確認結果。P6-04 型別轉送衝突與 D34 前置核定仍待使用者處理，未套用 P6-04 stash；接續按既定順序處理可獨立的 P6-03。

## 2026-11-16 19:58 Asia/Taipei (UTC+08:00) 完整守門失敗與最小修正

9bf222ed 的 Release 建置通過，但完整方案測試出現1紅：RichMenuImageRendererTests第45行全行程配置計數預期0、實際21，因此守門停在第2項，尚未驗收、未推送。

調查完整renderer與三個圖片測試：本案using均在斷言前離開，單案1綠、原整組114綠；LessonQrPublicationTests的Registration_RealFactoryAndPublisher_DrainJobScope會在另一個預設collection產出93張真PNG。ImageSharp全域計數在配置時增加、釋放時減少，非本案專屬。其餘觀察全域計數的QR測試已用DisableParallelization=true的QR image lifecycle collection，本案漏掛。

修正僅將本類別納入既有collection並補原因說明；不改產品、不放寬任何期望、不用GC或等待掩蓋，不關閉案例內A/B平行。原失敗證據保留，修正提交後重新完整八項；不能把原9bf222ed列為通過。
