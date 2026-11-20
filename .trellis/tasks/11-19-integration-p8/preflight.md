# 階段 8 前置缺項檢查

盤點時間：2026-11-19 20:01 Asia/Taipei (UTC+08:00)。此文件只記錄鍵名、工具狀態、錯誤類型及筆數，不記秘密值、連線字串、使用者資料或個別待辦內容。

| 項目 | 結果 | 依據／後續 |
|---|---|---|
| P8 起點與工作樹 | HEAD `6a37f16ba111943eb023c14dea3c9a006a0f0a5c`；分支正確；除新交付 08A 與既有 Trellis／workspace 未追蹤內容外無已修改追蹤檔 | `git status`、`git log`；P8 分支遠端尚不存在，首次推送依 08A 延至 P8-01a 驗收後 |
| 組織範圍 | `d365/registration/organizations.json` 僅有 `goodshepherdback`，屬本階段核定測試組織；不得連正式組織 | 依 00 §11、08A D8-2 執行核准的 goodshepherdback 實機步驟 |
| Plugin 版本與唯讀 verify | 先前 verify 的 `InvalidDataException` 根因已查明：Plugin DLL 未帶 `SpeechMessageBuildNumber`，FileVersion 為 1.0.0.1，組織版本為 1.0.0.19 | 排除根因後以 `SpeechMessagePackagePlugin=true`、`SpeechMessageBuildNumber=20` 重打包兩個 DLL，通過 `Test-PluginAssembly.ps1`，再唯讀執行 `verify --org goodshepherdback`；此為條件改變的新操作，不是相同條件重試。結果及組織狀態須以新證據記錄 |
| 歷史 D365 紀錄（初始盤點） | 2026-11-19 20:01 當時只見前一日建立的目錄，無可讀取的目前狀態紀錄 | 此為歷史盤點；2026-11-20 12:07 最新唯讀 verify 已在下方續跑紀錄補列 |
| Plugin／Workflow 打包 | 兩個 Release DLL 曾以錯誤 build number 產生；需按上列 build 20 流程重打包並驗證 | 每次 Plugin 內容改變，build number 再加 1；不得把未驗證產物用於組織操作 |
| User Secrets | 僅檢查指定 User Secrets 檔案的 JSON 屬性名稱，未讀值；`TestLineUserId`、`TestChannelToken`、`TestChannelSecret`、`TokenKey`、`ShadowTestChurchId`、`ShadowTestContactId` 未在已檢查的指定 Secret ID 中確認 | 只阻擋需要這些鍵的實機單元；依 08A §7.2 先完成離線工作。ChurchReport／LineWebhook Development 組態可連線性仍未確認 |
| Playwright／Edge | `playwright-core` 目錄存在；`msedge.exe` 不在目前命令搜尋路徑 | Edge 實際安裝位置未確認，P8-04a 前按 P6 README 核對 |
| `C:\smv` | 存在 `guard`、P7 候選／最終 guard 與 `guard-p8`；不碰 `C:\smv\guard` | `guard-p8` 僅含 `.git`，為 `--no-checkout` 狀態；顯示 8,794 筆索引缺檔。此為指定專用 guard 的未 checkout 狀態，尚未執行清理或覆寫 |
| D34、稽核、待辦基線 | 本輪未取得唯讀結果 | 不宣稱通過；按 08A 預先核定方式在對應實機單元重新檢查，未確認前不做組織寫入 |
| 管理員權限 | 目前程序非系統管理員 | 只影響必須重啟服務的步驟；屆時依 08A §7.2 跳過並登錄 |

## 使用者需處理

本階段不另交前置清單要求使用者等待。未確認的外部條件依 08A §7.2 跳過或延後對應實機步驟，離線單元照序執行。

## 2026-11-20 續跑更新

2026-11-20 10:45 Asia/Taipei (UTC+08:00)：以下結果取代上表中「目前狀態尚無紀錄」的歷史盤點文字；不代表 P8-01c 已驗收。

| 項目 | 本次續跑結果 | 後續 |
|---|---|---|
| 組織範圍 | 依使用者更正及 00 §11、08A D8-2，`goodshepherdback` 為本階段核准測試組織；catalog 僅列此組織，沒有正式組織連線 | 只對 `goodshepherdback` 執行 08A 明列操作 |
| Plugin DLL 與 verify | build 20 Plugins／Workflows 通過 `Test-PluginAssembly.ps1`（兩者 `ok=true`）；2026-11-20 10:32 唯讀 `verify --org goodshepherdback` 結束碼 0，輸出符合 catalog 預期 | FileVersion 根因已修正；這不是第三次相同條件重試，不沿用舊「不得重試」結論 |
| 開啟中的舊項目 | 2026-11-20 10:36 `export-legacy --org goodshepherdback` 唯讀結果：啟用舊步驟 0、舊流程 0 | 不代表 D34、測試殘留或本單元結尾清理已確認 |
| 遷移備份與 metadata 衝突 | 2026-11-20 10:42 ID-only 備份 97 筆（收費單 96、預約 1）；migration plan 因舊 choice 同值異標籤 fail closed，未讀業務資料、未 apply | 不猜標籤、不執行 apply；P8-01c 保持進行中，詳見 `wp-reports/P8-01c-migration.md` |
| 守門目錄 | 2026-11-20 10:12 起 `C:\smv` 僅允許 `guard-p8` 與 `guard-p8-evidence` | 單一 guard clone checkout 各新 SHA 重用；證據只放 evidence；不碰 `C:\smv\guard`；終點刪除 `guard-p8` |

2026-11-20 11:18 Asia/Taipei (UTC+08:00)：`export-metadata --org goodshepherdback` 唯讀成功。metadata snapshot 語系 1028；new_fee.new_category RequiredLevel=None、24 值；new_dedication_booking.new_dedication_category RequiredLevel=None、17 值，共享 17 值。此匯出格式不保留 labels，SHA-256 為 `E0F4498B97A70A2D33174AD1ADD77903115667DAC2E192F63ECC89DC63060A8B`。一般遷移 plan 已因共享值標籤衝突 fail closed，未 apply；P8-01c 仍進行中。metadata export 是唯讀且沒有建立 SMV-TEST 資料或改變組織狀態。

2026-11-20 12:08 Asia/Taipei (UTC+08:00)：使用者修正後續跑已依 `SpeechMessageBuildNumber=20` 完成唯一核准測試組織 `goodshepherdback` 的唯讀 verify；新執行成功，狀態符合 catalog 的 `off`。新建立 labels 黃金檔 SHA-256 為 `C49F889CF922E89B7FB1D22D72F9596BED8089E037AFCB2FE029A69A6D712B9C`；兩欄 `RequiredLevel=None`，17 個共用選項值中 5 個標籤不同。D34 唯讀探針兩次 `InvalidOperationException`、`writes=0`，沒有用相同條件第三次重試；SMV-TEST 數量尚未知。結論：P8-01c 未驗收；未套用 lookup 結構或奉獻資料，按 P8-PROP-008 保持 fail-closed 並依 08A §7.2 繼續不受影響單元。

2026-11-20 12:34 Asia/Taipei (UTC+08:00)：新會話依 §0.4 核對部署工具 User Secrets 必要鍵存在，並在記憶體內確認 endpoint 僅符合 `goodshepherdback` allowlist；未顯示或保存秘密值。唯讀 verify 結束碼 0，狀態符合 off，證據 `C:\smv\guard-p8-evidence\p8-01c-resume-verify-20261120.log`。P8-01c 仍缺 D34、SMV-TEST 殘留基線及遷移資料驗收，不宣稱單元結尾清理完成。

2026-11-20 12:16 Asia/Taipei (UTC+08:00)：一次性探針 DLL／原始碼檔仍位於忽略的 `artifacts/p8`；清理命令被工作區執行政策拒絕，未以其他執行器繞過。`.trellis/workspace/--help` 路徑已不存在。探針產物不加入提交；其清理狀態仍未完成。
