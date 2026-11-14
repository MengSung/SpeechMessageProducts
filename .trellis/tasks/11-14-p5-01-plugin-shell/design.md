# P5-01 技術設計

## 邊界與資料流

Plugin context → `PluginShellBase`（驗證深度與設定）→ `PluginInput` → `PluginServices` → 規則 → `RecordingOrganizationService`／`ShadowPushRecorder`（Shadow）或借用的 SDK service（Live）→ bounded trace。Core 通知鏈由 `MemberNotificationService.NotifyComposedAsync` 統一組合訊息與目標檢查；推播 client 由 Host 借用；Core 擁有每次 HTTP request／response 並在傳輸完成後 Dispose，不處置 Host client。

## 不變條件

- Shadow 禁止寫入、解密及推播，只記錄最小必要摘要；Live 不是本包的實機驗收路徑。
- 每次 Execute 的輸入、追蹤與結果互不共享；不把 HttpContext、Session、token、credential、SDK service 或可變集合存入 plugin singleton/static。
- 深度政策與設定驗證先於規則及任何 I/O；錯誤 fail closed，未知查詢語法不回空資料。
- `FakeOrganizationService` 的實體表、查詢結果與 recording 都由測試案例擁有，測試結束清理；chunk 與設定長度有界。

## 相容與失敗處理

保留既有公開契約與 `DiagnosticsPingPlugin` 行為。業務例外轉成帶事件 ID 的標準 Plugin 例外；未知例外保留原實例與堆疊重新拋出。兩個教會目標的 mismatch 必須在 Shadow／Live 相同拒絕，不洩漏原始秘密或設定內容。
