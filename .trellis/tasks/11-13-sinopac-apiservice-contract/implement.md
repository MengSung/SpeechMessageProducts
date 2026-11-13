# 執行清單

1. [x] 核對 Trellis 工作樹與 P4 分支狀態；建立此任務規劃並啟動任務。
2. [x] 先在 `SinopacProviderTests` 新增封包捕捉測試，執行並確認目前數字 `APIService` 造成預期紅燈。
3. [x] 新增全部 enum wire mapping 非數字及未知值 fail-fast 測試，確認正確失敗原因。
4. [x] 修正 provider 對照、C# 檔案說明／XML／行為註解，以及 AllotQuery 的兩處 `"7"` 期待值；不改其他封包向量。
5. [ ] 依序執行：
   - `dotnet test .\SpeechMessage.Payments.Tests -c Release --filter "FullyQualifiedName~SinopacProviderTests"`
   - `dotnet test .\SpeechMessage.Payments.Tests -c Release --filter "FullyQualifiedName~SinopacAllotQueryTests"`
   - 固定 `C:\smv\guard` 的完整 00 §8.1。
6. [x] 逐位元組確認修改的 `.cs` 為 UTF-8 無 BOM、CRLF、末尾 CRLF；檢查 diff 僅含核准範圍且 Sign／Message／內層 JSON 黃金值未變。
7. [ ] 離線守門全過後，只跑一次 `Live_AllotQuery_SandboxRoundTrip`，保留原始 TRX／log 到本機 evidence，摘要只記分類、階段碼與不含秘密的狀態。
8. [ ] 依分類處理：A 更新 P4-06 為已驗收、解除 P4-PROP-008 並新增 P4-PROP-009；B 僅對 E3100–E3103 依指示修窗口、先紅後綠及完整守門後再跑唯一允許的第二次；C/D/E 停止沙箱重試並維持條件式狀態，補記 P4-PROP-008。
9. [ ] 更新稽核任務／P4-06 回報及進度 DATA（僅依分類允許的變更），執行進度頁測試及檔案說明檢查。
10. [ ] 依工作包提交規則提交並執行所需輕量／完整檢查；僅在驗收規則允許時推送 P4 分支並以 `git ls-remote` 核對 SHA。不推 main、不開 PR、不啟動 P5。

2026-11-13 依序完成 provider tests 20/20、AllotQuery tests 11/11。階段起點編碼檢查先發現 4 份既有稽核 Markdown 的 LF／缺結尾 CRLF，僅正規化換行後，141 個變更檔均通過 UTF-8／CRLF 檢查；完整 guard 與沙箱補驗尚未執行。
