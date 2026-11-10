# P3-09 執行與驗證清單

1. [x] 依使用者 prompt 完整讀取 3.5 00/04、階段父任務 PRD/preflight/缺項文件及 P3-09 操作手冊。
2. [x] 確認本機程序為系統管理員；核對遠端 VM、IIS、憑證、SQL、DNS/TLS 與 Hosting Bundle 初始狀態。
3. [x] 將 P3-08 三個既有紀錄檔正規化為 UTF-8 無 BOM、CRLF、結尾 CRLF；只改換行，依 §8.3 通過後以 `P3-08 chore:` 提交。
4. [x] 更新前置紀錄，完成 User Secrets 鍵存在性/格式核對（不讀取或輸出值）；2026-11-10 11:43 Asia/Taipei (UTC+08:00) 核對 Hosting Bundle 10.0.14、ANCM v2、CRM site/app pool、goodshepherdback HTTPS 與兩份暫存檔清除，紀錄見 `evidence/hosting-bundle-install.md`。
5. [x] 建立 appcmd 備份及 IIS/CRM/SQL 操作前基線；Hosting Bundle 缺少且安裝檔驗證通過。
6. [ ] 先唯讀檢查 applicationHost.config ACL；建立獨立平台測試 DB 與 A.8 資料表、獨立 Webhook IIS site/app pool，安全部署並設定專用 AppPool 環境變數。新增 hostname+SNI+既有憑證後立即核對 goodshepherdback HTTP 302/原憑證、CRM site/app pool Started、測試 hostname 新站回應；不符即移除 binding，CRM 仍異常才還原備份。
7. [ ] 執行 04 P3-09 第 1～7 步及 P3-06 三項 Warning 實機核對；記錄確切結果。
8. [ ] 清理並驗證測試資料/暫存資源為零、CRM 設定與資料庫無變更、秘密無外洩。
9. [ ] 產出驗收步驟、一頁操作紀錄、一頁 P3-09 回報與階段 3 審查；更新進度頁。
10. [ ] 按適用的 §8.1 或 §8.3 驗證，提交紀錄，推送 P3 分支並以 `git ls-remote` 核對 SHA；停止，不開始階段 4。

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
