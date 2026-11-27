# F3 執行計畫（依 01 Task F3）

1. 修復首次 allocation lock 初始化：先保留已失敗的 `SlotRequest_MissingAllocationLock_InitializesAtomically`，補原子 alternate-key Upsert 與同步測試替身，再加入 20 並行首次初始化案例；驗證 RED→GREEN 與相關 Plugin 測試。
2. 盤點並完成 SlotAllocationRule、兩個平台 Plugin、三產品 schema/registration/packaging、DataverseChannelDirectory、ChannelHealthChecker、Webhook 正式接線及架構測試；補全 Channel product、secret、refresh、health、alert 與 lifecycle 測試。
3. 新增 `SpeechMessage.Dynamics.Deployment.Tests/Live/ChannelPoolLiveTests.cs` 的 `Category=Live3P`、`WP=F3` 案例及嚴格的測試組織／測試資料前置與清理；離線閘門不執行該類別。
4. 完成報告、決策紀錄、90 線 A 列及進度資料；在 Live3P 未跑前標示「自動測試通過（實機待補）」並改 F3 備註為「等 FS 完成後開始」。
5. 執行 02 §4 全部離線閘門；按計畫以 ClaudeOnly 只檢 Critical/High。失敗必修，不把失敗視為環境限制。
6. 提交 F3 後依 02 §5.2 無鎖 rebase、完整守門、取得 merge lock 再核對整合分支，於整合工作樹快轉合併並推送；不動整合樹既有非衝突修改。若有重疊他人修改則依 02 §7 停止條件處理。
7. F3 離線合併後才取得 test-env.lock，嚴格依 02A §5 執行線 A 實機順序；每操作開始前重查鎖與 Channel/組織身分，原始證據留本機並只在報告記安全摘要與 SHA。
