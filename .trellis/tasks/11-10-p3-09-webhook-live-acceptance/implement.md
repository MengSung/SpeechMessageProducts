# P3-09 執行與驗證清單

1. [x] 依使用者 prompt 完整讀取 3.5 00/04、階段父任務 PRD/preflight/缺項文件及 P3-09 操作手冊。
2. [x] 確認本機程序為系統管理員；核對遠端 VM、IIS、憑證、SQL、DNS/TLS 與 Hosting Bundle 初始狀態。
3. [x] 將 P3-08 三個既有紀錄檔正規化為 UTF-8 無 BOM、CRLF、結尾 CRLF；只改換行，依 §8.3 通過後以 `P3-08 chore:` 提交。
4. [x] 更新前置紀錄，完成 User Secrets 鍵存在性/格式核對（不讀取或輸出值）；2026-11-10 11:43 Asia/Taipei (UTC+08:00) 核對 Hosting Bundle 10.0.14、ANCM v2、CRM site/app pool、goodshepherdback HTTPS 與兩份暫存檔清除，紀錄見 `evidence/hosting-bundle-install.md`。
5. [x] 建立 appcmd 備份及 IIS/CRM/SQL 操作前基線；Hosting Bundle 缺少且安裝檔驗證通過。
6. [ ] 使用者在 `goodshepherdback` 建立專用測試教會 A/B 與各自 Contact，A 綁定 User Secrets 的 TestLineUserId；以唯讀 WhoAmI 及正式 GUID 完成 mapping。依核准限制，不用自訂 CRM probe 或 SQL 寫 CRM。
7. [ ] 建立／沿用隔離測試 DB/A.8、獨立 Webhook IIS site/app pool、部署及專用 AppPool 環境變數；新增 hostname+SNI 後立即核對 goodshepherdback 302／原憑證、CRM site/pool Started、測試 hostname 新站回應，不符即回復。
8. [ ] 執行 04 P3-09 第 1～7 步、LINE 官方 endpoint test、HMAC 模擬事件、重送、401 及 P3-06 三項 Warning；清理測試資料並確認殘留 0。
9. [x] 2026-11-10 21:25 Asia/Taipei (UTC+08:00)：驗收步驟、一頁夜間操作紀錄、一頁 P3-09 回報、階段自審、一頁階段 3 總回報、早上待辦與正式秘密保存計畫提案已建立；進度頁標示 P3-09 `you`。沒有宣稱實機完成。
10. [x] 2026-11-10 22:45 Asia/Taipei (UTC+08:00)：紀錄收尾提交 `23a6379c57e0082bb16af49cfc20dbab45a424b2` 在固定 guard 完整通過 §8.1，已推送 P3 並以 `git ls-remote` 核對遠端 SHA 相同；此守門不代表實機驗收完成。
11. [ ] P3-09 實機七步、P3-06 Warning 核實及本次測試資料殘留 0；CRM UI 測試資料尚待使用者建立。P3-09 未完成前不標記階段 3 done。

## 2026-11-10 夜間續作狀態

2026-11-10 21:25 Asia/Taipei (UTC+08:00)：P3-09 暫停點從「mapping 不明」更正為「使用者已核定來源，但 CRM 測試 Account／Contact 尚未建立」。本機 Computer Use 控制端無可用 app/browser；既有 Deployment CLI 不提供 CRM Account／Contact CRUD，`LineRecipientQueries` 為唯讀。不得改走自訂 CRM probe 或直接 SQL。部署基礎腳本 Parser=0，未執行；CRM/IIS/SQL/LINE 無本輪寫入。P3-09 改列使用者收尾，其他文件、自審與推送收尾繼續完成。

2026-11-10 21:58 Asia/Taipei (UTC+08:00)：依使用者核定，mapping來源不再是阻塞；OrganizationId由唯讀WhoAmI取得、ChurchId使用測試教會A正式GUID、TimeZone固定`Asia/Taipei`。唯一必要前置仍是CRM UI建立A/B及各自Contact，A Contact綁定秘密中的TestLineUserId。重新以PowerShell Parser檢查部署基礎腳本，錯誤0；操作紀錄檔存在且SHA-256與回報相同。未執行部署或寫入CRM／IIS／SQL／LINE。

## 停止點（2026-11-10）

- [x] 2026-11-10 11:26 Asia/Taipei (UTC+08:00)：依 00 §9-7，同一 Hosting Bundle 安裝啟動操作連續兩次遭本機工具政策拒絕；停止，不使用替代執行器。
- [x] 2026-11-10 11:26 Asia/Taipei (UTC+08:00)：本機與遠端兩份暫存安裝檔清理均遭工具政策拒絕；如實列出殘留，沒有宣稱資源殘留為 0。
- [x] 2026-11-10 11:26 Asia/Taipei (UTC+08:00)：目前沒有安裝、IIS site/binding/app pool 變更、SQL 寫入或 LINE API 呼叫；P3-09 與階段 3 均未完成。
- [x] 2026-11-10 11:26 Asia/Taipei (UTC+08:00)：人工續接及回復步驟見同目錄 `P3-09-執行受阻回報.md`。

## 已通過檢查

- `Test-TextEncoding.ps1 -Scope Changed -Base 12cfde72`：checked 197，filesWithIssues 0。
- `check_file_documentation.py`：checked 1438，failures 0。
- `Test-ProgressPage.cjs`：28/28 通過。
- CRLF 修正提交：`35749d78`，`git diff --ignore-space-at-eol` 確認三個目標檔案內容沒有行為/文字差異。
- CCG analyzer：Claude 有輸出，Gemini 403/503 無輸出，未達 dual-model completion。

## 2026-11-11 一次性 evidence 工具方案例外

2026-11-11 12:12 Asia/Taipei (UTC+08:00)：依使用者核定，`evidence/P309-TestDataTool.csproj` 是不進版控的一次性驗收工具，不是產品專案；其來源位於 `.git/info/exclude` 排除的 evidence 目錄。先前將它及 `.trellis/tasks/evidence` 方案資料夾加入 `SpeechMessageProducts.sln`，造成乾淨 clone 的方案指向不存在專案。已從方案移除該工具、相關方案資料夾及其組態映射；工具留在本機 evidence，以 `dotnet run --project` 單獨執行。此為僅適用於本次 ignored evidence 工具的核定例外，不變更一般「新增產品專案須立即加入方案」規則。
