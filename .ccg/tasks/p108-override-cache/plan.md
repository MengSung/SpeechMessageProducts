# P1-08 實作計畫

沿用使用者完整自主授權；既有隔離worktree、inline，不派子代理。主會話自審不可冒稱第二人。

## 設計與取捨
- 來源不保存D365連線：注入Core lease factory，逐次Open(context)由host授權並提供組織/產品/教會匹配lease；using終止，不Dispose別人的共享SDK。同步SDK延用CrmAsyncExtensions完整分頁，不Task.Run偽非同步；host lease提供實際transport timeout，owner取消後仍drain入場I/O。
- validator放Localization，重用NamedTextFormatter的參數文法及既有Snapshot身份契約，來源/快取不複製解析器；錯誤不輸出文字。
- 快取先非同步LoadAsync(context,UTC stamp)，再由同步GetSnapshot供TextCatalog純記憶體取字；冷未載入明確拒絕，不默默假裝無覆寫。
- 10分鐘是重讀期限，非刪除舊正確資料；MemoryCache閒置30分，失敗保留舊快照但不更新已成功stamp/loaded時間。變更stamp不可倒退。
- 每scope entry有可變控制殼但不暴露，snapshot不可變；短鎖僅保護入場/發布，不在鎖內I/O/通知/取消callback。flight至多64，不排無界admission queue；滿載明確拒絕。
- Disable及dispose使entry/flight世代失效；不保存無界停用tombstone，host重新授權啟用後方可重新Load。

## 實作步驟
- [ ] 來源契約/完整頁/lease生命週期/錯scope/故障/取消紅綠，兩宿主驗實載net462。
- [ ] 候選鍵/參數/字長/schema/正式ID/不完整候選紅綠。
- [ ] MemoryCache與singleflight，fake time、stamp/invalidate、取消、disable/dispose race紅綠。
- [ ] A/B1000、容量、故障注入、回收與catalog實際流程，依真失敗修正。
- [ ] strict、獨立coverage、完整方案及適用守門；主會話自審與重驗。
- [ ] 更新一般資訊/驗收矩陣/證據manifest、本地commit與子任務封存。主P1續11/12，不宣告整P1完成。
