# P7.2 Slice A operator handoff

目前本機程式、測試與 preflight runner 已就緒；唯一尚缺的是 task-owned contact fixture descriptor。

## 使用者只需要做一件事

1. 在 Codex 保留的 sunnyvalechback 登入頁，用你平常的方式完成登入。
2. 登入後確認畫面已進入實際 Dynamics CE 首頁，而不是停在 AD FS 登入頁。
3. 回覆「已登入」，不要貼帳號、密碼、cookie、token、GUID 或畫面中的完整例外。

Codex 會在已登入頁面上進行唯讀查詢，尋找帶有 `p7.2-contact-basic-info` fixture marker 的 task-owned contact；如果找不到，會先停下來請你確認一筆測試會員，絕不猜測或寫入非 task-owned 記錄。找到並驗證 owner 後，Codex 才會在 Lenovo 的 `%LOCALAPPDATA%\SpeechMessage\Dynamics\P7.2\contact-basic-info-fixture.json` 建立本機 descriptor（該檔案不進 repository、不進 chat、不進 TRX）。

## 現在不要做的事

- 不要執行 `-ExecuteFixture`。
- 不要啟用 `Package02ContactBasicInfoUpdatesEnabled` 或任何 ChurchReport 流量。
- 不要重新建立 P6.2 profile、修改 Credential Manager、啟動 Official Worker 或執行 CE 8.2 write。

## 安全 preflight（可選）

登入完成後，如需確認本機狀態，可執行既有的預設 preflight；它不啟動 dotnet、不執行 CE operation：

```powershell
$root = 'D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File `
  "$root\docs\scripts\Invoke-Package02Data8ContactBasicInfoWriteEvidence.ps1" `
  -RepositoryPath $root `
  -Json
```

在 descriptor 尚未建立前，預期仍是固定的 `fixture-input-required` 與 `operationExecuted=false`。

## 目前阻塞紀錄（2026-08-08）

- Task／Step：P7.2 Slice A fixture discovery，尚未進入 live write。
- 根因：Codex 可控制的 Chrome 分頁連續確認為 AD FS `登入` 頁，沒有可用的已登入 CE 首頁；Edge 擴充功能目前也沒有可供 Codex 接管的分頁。
- 已嘗試：重新列舉 Chrome 分頁、保留登入 handoff、檢查本機與 Trellis 歷史是否有可安全重用的 fixture GUID；均沒有建立 descriptor，也沒有啟動 CE operation。
- 證據：目前 preflight 仍為 `outcome=no-go`、`reason=fixture-input-required`、`operationExecuted=false`；feature flag 未變更。
- 唯一下一項使用者資料：在保留的 Chrome 登入分頁完成 sunnyvalechback 登入，確認顯示實際 Dynamics CE 首頁後回覆 `已登入`。不要貼帳號、密碼、cookie、token 或 GUID。
