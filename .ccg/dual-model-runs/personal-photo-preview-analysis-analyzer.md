# CCG analyzer Task: personal-photo-preview-analysis

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.6.DesignNewArchitector.Worktree

## Request
# 任務：修正個人相關資料照片選取後未立即顯示新照片

## 使用者需求

個人相關資料頁面選取新的照片後，畫面沒有立即顯示更新的照片。

## 已完成的根因調查

- 頁面：`SpeechMessageProducts.ChurchReport/Views/Personal/PersonalInfomationViewWithImage.cshtml`
- 上傳 action：`SpeechMessageProducts.ChurchReport/Controllers/PersonalController.ImageUpload.cs`
- 前端 `FileReader` 會先把本地預覽設到 `#profileImage`。
- 上傳成功後前端改用回傳的 `response.imageUrl`。
- `GetContactImage` 以 `contact-image-full:{contactId}` 與 `contact-image-thumb:{contactId}:{size}` 快取影像。
- `UploadContactImage` 更新 CRM 後沒有移除這些快取，因此回傳 URL 雖有 timestamp，仍可能從相同的伺服器快取鍵讀到舊圖。
- 已新增失敗測試：`ChurchReport.MemberInfo.Tests/Personal/PersonalContactImageCacheTests.cs`，要求完整圖與所有支援縮圖尺寸在更新後失效。

## 請求

請從各自角度審查建議的最小修正：在個人照片上傳成功、CRM 更新完成後，集中清除該 Contact 的完整圖與 32..256 像素縮圖快取，並保留前端立即預覽與既有使用者隔離。必要時指出是否還要調整前端成功回呼的 cache-busting 行為。請特別檢查：快取一致性、競態、跨使用者資料洩漏、記憶體／資源生命週期、效能與測試充分性。

## 輸出

請輸出：
1. Root cause 是否成立
2. 必要修改檔案與最小實作建議
3. 可能的 Critical / Warning / Info
4. 驗證建議


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.