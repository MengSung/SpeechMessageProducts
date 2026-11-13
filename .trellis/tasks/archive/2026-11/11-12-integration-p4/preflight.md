# 階段 4 前置缺項清單（核對摘要：2026-11-12 13:52 Asia/Taipei；文件補記：2026-11-12 15:31 Asia/Taipei；呈現修正：2026-11-12 16:32 Asia/Taipei）

此清單依 00 §7.3 與 04A §5 P4-00 編製。設定檢查只記鍵名與是否存在，不在本清單、回報或進度頁保留任何設定值；目前沒有需要使用者補入的永豐測試金鑰。未驗證項目列於下表，P4-06 前由 Codex 再唯讀確認。

| 項目 | 給哪個 WP | 狀態與證據 | 後續處理 |
|---|---|---|---|
| `Payment:Profiles:JesusTest:Credentials:ShopNo`、`Payment:Profiles:JesusTest:Credentials:A1`、`Payment:Profiles:JesusTest:Credentials:A2`、`Payment:Profiles:JesusTest:Credentials:B1`、`Payment:Profiles:JesusTest:Credentials:B2`、`Payment:Profiles:JesusTest:Credentials:XKeyId`；`Payment:Profiles:JesusTest:Endpoints:ApiBaseUrl` | P4-06 | 已有；僅檢查鍵是否有值，未讀取或輸出設定值 | P4-06 以標準輸入移轉至 LineWebhook User Secrets；不改 ChurchReport `appsettings*.json` |
| `Payment:Profiles:JesusTest:Environment` | P4-06 | 鍵存在；值未記錄 | P4-06 執行前再次確認設定環境，不送真實交易 |
| 開發機至永豐沙箱 HTTPS | P4-06 | TLS handshake 成功；未送交易 | P4-06 使用沙箱執行指定測試 |
| 佈署專案唯讀連線 `goodshepherdback` | P4-06 | `verify --org goodshepherdback` 退出碼 1；原始輸出未保留，故不可視為通過 | P4-06 前由 Codex 依 00 §9-7 排查並再次唯讀確認；不做 D365 寫入 |
| 測試教會 A 的正式 GUID 與 `new_timezone` | P4-06 | 未驗證；已封存的 P3-09 回報確認曾使用測試教會 A，但未記錄其 GUID 或 `new_timezone` 讀回值；不依名稱猜測 | P4-06 前由 Codex 從可用本機 evidence 取得或以允許的唯讀方式確認 |
| `LineWebhookP309` 運作狀態 | 僅記錄 | 無簽章 POST 回應 401，符合預期 | 僅記錄；不重新部署、不在本機啟動整台 LineWebhook |

**缺項摘要：** 2 項待確認（D365 `verify` 與教會 A GUID／`new_timezone`）；本次未發現永豐測試金鑰缺漏。離線工作依序繼續；若 P4-06 同一實機步驟失敗兩次，或結構 plan 出現 `new_sinopacprofile` 以外的差異，再依核定條件處理。

**安全界線：** 本次未連正式組織、正式商店或正式 LINE Channel；未建立付款、未對 D365 寫入、未重新部署測試站或啟動本機主機。
