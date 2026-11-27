# F3 需求摘要

依 1.3 `01_共通層實作計畫.md` 的 Task F3 完成 Channel 名額池、三產品隔離、名額申請單同步 Plugin、D365 目錄與健康檢查、Webhook 驗簽接線、部署註冊與測試。

## 固定邊界
- 僅修改線 A F3 明列檔案；不實作線 B 工作。
- 離線測試通過不得寫成實機通過；實機工作須等 F3 離線閘門合併後按 02A §5 排序執行。
- 不連任何正式組織（含 8.2）、正式 Channel 或正式金流；複製舊 Channel 不載入、不解密、不呼叫 LINE。
- 不放寬守門；任何跨租戶／產品／Channel 外洩或資源未 drain 均為阻擋。

## 驗收
- 01 F3 的每個 Expected 與指定測試都有明確結果。
- Plugin allocation lock 首次建立使用 D365 alternate-key 原子 Upsert；缺列與競態可安全初始化，重複／型別錯誤 fail closed。
- Webhook 透過產品限定的 Dataverse channel secret resolver 驗簽；錯誤簽章不得入列。
- Release 全方案建置、規定自動測試、文件說明、Changed 編碼、方案檢查與 ClaudeOnly Critical/High 閘門全部執行並如實記錄。
- 90 未通過 Live3P 前標「自動測試通過（實機待補）」；F3 合併後再做 02A §5 實機順序。
