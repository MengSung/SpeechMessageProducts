# 階段 8 前置缺項檢查

盤點時間：2026-11-19 20:01 Asia/Taipei (UTC+08:00)。此文件只記錄鍵名、工具狀態、錯誤類型及筆數，不記秘密值、連線字串、使用者資料或個別待辦內容。

| 項目 | 結果 | 依據／後續 |
|---|---|---|
| P8 起點與工作樹 | HEAD `6a37f16ba111943eb023c14dea3c9a006a0f0a5c`；分支正確；除新交付 08A 與既有 Trellis／workspace 未追蹤內容外無已修改追蹤檔 | `git status`、`git log`；P8 分支遠端尚不存在，首次推送依 08A 延至 P8-01a 驗收後 |
| 組織範圍 | `d365/registration/organizations.json` 僅有 `goodshepherdback`，`switchState=off` | 不包含正式組織 |
| 唯讀 verify | 2026-11-19 20:01 執行一次；結束碼 1，工具回報 `InvalidDataException`，細節經敏感資訊防護不輸出 | 連線階段失敗；未取得 off、verify 差異、D34 或殘留狀態；依續跑規則不重試 |
| 最新 D365 紀錄 | `artifacts/d365/goodshepherdback/` 只見 2026-11-19 建立的目錄，無可讀取的目前狀態紀錄 | P7 摘要中的較早 off／verify 證據不能代替目前狀態 |
| Plugin／Workflow 打包 | 兩個 Release DLL 已打包；`Test-PluginAssembly.ps1` 均成功 | 依上一輪產物摘要；Plugin 子集建置有 16 個既有 CS1574 警告、0 錯誤 |
| User Secrets | 僅檢查指定 User Secrets 檔案的 JSON 屬性名稱，未讀值；`TestLineUserId`、`TestChannelToken`、`TestChannelSecret`、`TokenKey`、`ShadowTestChurchId`、`ShadowTestContactId` 未在已檢查的指定 Secret ID 中確認 | 只阻擋需要這些鍵的實機單元；依 08A §7.2 先完成離線工作。ChurchReport／LineWebhook Development 組態可連線性仍未確認 |
| Playwright／Edge | `playwright-core` 目錄存在；`msedge.exe` 不在目前命令搜尋路徑 | Edge 實際安裝位置未確認，P8-04a 前按 P6 README 核對 |
| `C:\smv` | 存在 `guard`、P7 候選／最終 guard 與 `guard-p8`；不碰 `C:\smv\guard` | `guard-p8` 僅含 `.git`，為 `--no-checkout` 狀態；顯示 8,794 筆索引缺檔。此為指定專用 guard 的未 checkout 狀態，尚未執行清理或覆寫 |
| D34、稽核、待辦基線 | 本輪未取得唯讀結果 | 不宣稱通過；按 08A 預先核定方式在對應實機單元重新檢查，未確認前不做組織寫入 |
| 管理員權限 | 目前程序非系統管理員 | 只影響必須重啟服務的步驟；屆時依 08A §7.2 跳過並登錄 |

## 使用者需處理

本階段不另交前置清單要求使用者等待。未確認的外部條件依 08A §7.2 跳過或延後對應實機步驟，離線單元照序執行。
