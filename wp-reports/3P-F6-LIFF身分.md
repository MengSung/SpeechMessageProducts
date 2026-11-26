# F6　LIFF 共用骨架與租戶身分

更新：2026-11-26 14:11 Asia/Taipei (UTC+08:00)。線 B；本包起點 `b6da17e2b6ddf88dc22d5ceb5f5551039c6315a9`。狀態：完成（自動測試通過）；程式原提交 `162caf44ec97af736e1ce58113449c1dbd0e1833` 同步整合 `474669ddd57563ab3e657cc4098dbbe140e88ab0` 後重寫為 `44f6df89380e8b81d9e681c557dab09f629b4282`，程式樹逐 byte 不變；全方案守門通過，依02§5接續合併推送。公開站台、遠端SQL及手機結果仍未通過，不宣稱測試環境完成。

| 01 驗收條件與指定測試 | 目前結果 |
|---|---|
| 偽造／過期／aud 不符及網路逾時均拒絕；`Verify_ExpiredToken_Rejected`、`Verify_AudienceMismatch_Rejected`、`Verify_NetworkTimeout_FailsClosed` | verifier 真正紅 76 案後綠 76 案；完整共用離線 111/111，另 DI 2/2。固定官方端點、五秒期限、64 KiB 上限、無 Cookie／redirect；所有 response、stream、request、取消登錄及緩衝有界釋放。 |
| `Api_TenantAUserRequestsTenantB_Returns403`；不寫 Session／Cookie | 真實 Program＋loopback Kestrel 接線由紅 4 案／綠 1 案到全綠；最終本包產品 23/23，整個主機 106/106。三輪各 64 個 A/B HTTP 流程只回本人租戶，跨租戶全 403，每輪 128 份 SDK 租約全部歸還。 |
| `Resolve_UserInTwoChurchesSameChannel_UsesCurrentChurch`、`Resolve_TwoChurchesNoCurrent_ReturnsNull`、`Resolve_StaleSelection_ReturnsNull`、`Selection_SetForOtherPersonsContact_Rejected` | 每次從固定產品／Messaging Channel 完整查候選；多筆無有效選擇回 null，不任取或合併同名。SQL 用 product／channel／user 的 BIN2 唯一鍵，只存 Contact ID；切換前重查本人候選。離線 22/22；獨立 LocalDB 12/12。 |
| `Resolve_TenantSwitch_RebuildsScopeAndCache` | 角色與候選每次重建；F8 前預設空權限。重複 ID、第二頁故障、SDK 呼叫內取消及歸還故障均拒絕部分發布。名稱與 actor 同批、只活本次 scope，名稱 257 字元先重現前後端不一致後修成共同上限 256。 |
| `router_doesNotTouchLiffParamsBeforeInit` | JS 91/91；只在 await init 後讀參數及 token，精確本機白名單拒絕外部路由。40 個並行 A/B 頁面排空 timer、listener、可見身分及 pending fetch。 |
| LIFF app 與公開頁面 | 13:36 持鎖，以已交接測試 Login Channel 建立 `2012805181-VBoYPPTz` 並重新 GET 核對固定測試 URL、openid／profile；沒有保存 access token。13:40 外部 whoami 頁面唯讀檢查逾時，未部署、未重試第三次 F2 Apply；公開 200 尚未通過。 |

本機 SQL 結果只代表 MSSQLLocalDB：真實 64 路競爭、完整鍵大小寫隔離、故障回滾、鎖等待取消及重試已驗，48 份累積案例清理證據皆為資料庫與檔案零殘留；不代表遠端 SQL 憑證鏈或平台部署。程式與正式 SQL 腳本均驗精確四欄 schema；設定不相容不自動遷移。

02 §4 守門：同步線 A 的精確 LIFF 分類後，Release 全方案建置 65 警告／0 錯誤；完整固定篩選一般測試 25 個目標組件、7905/7905、0 失敗／0 略過（Hosting236、LineWebhook143、LIFF113、主機106）。文件 1967 檔／0 失敗；Changed 編碼 33 檔／0 問題，另對 35 個本包檔案逐 byte 核對 UTF-8 無 BOM、CRLF 與末尾 CRLF；方案含兩個新專案各一次。先前兩個 UnknownProject 已由線 A 精確分類解除；轉碼案例沒有改程式、測試或斷言，本次完整命令原樣通過，不宣稱已找出單次失敗原因。單模型（使用者指定 ClaudeOnly）審查沒有 Critical／High；外部審查重新讀主要正式路徑，沒有逐行讀所有測試與獨立 SQL 腳本，後兩者由本機執行及人工核對。13:55 已在 Edge 開啟線 B 工作樹固定總覽，頁首無紅色警告，最新即時欄位可見；這是瀏覽器檢查，不代表公開測試站或手機驗收。

證據 SHA-256（原始 log 不入 repo）：產品 23 案 `f39f44c2e5bd43a549a88be500f34f274535840a0af5dce8e16b92ddd420abcb`；SQL 12 案 `fec44b9b34100fe80ab2635b8e95ec87971c32975c6511cd877a7558abb01161`；DI 2 案 `e8e17d74dd2665a8a8f6e386a5fb988d7a3815f293ce5deb53da3697c2496ce4`；LIFF app API `e47d56ce57cea5eb6fb4a2bdf3f4f96526fd8f6cd94943e42862c5c451dc2dcd`；公開頁面逾時 `4388c02ac140dd9966df62bca3b8ca5c10c437b07159f872e1829fe2adc19ee9`；同步後 Release 建置 `317bc1a52044072e5bbb571161c211a1e192c6756dd17e37bf29866844137689`；完整一般測試 `9cc5bd9f711421b038331ca712c2dbca7afeeb40e8378dcf6d18d623a1786207`。

審查輸出 SHA-256：`9e7021764b01a11094425ad4df3d5d16a2416e53dd4ab6cd82d54431a132f0e8`。

## 手機待驗

M1 確認真實 LINE 登入、本人教會名稱與角色，以及 Development `?probe=<另一測試租戶ID>` 的 403。先決條件是 F2 公開站台與嚴格 TLS 平台 SQL 就緒、FS／F3 測試欄位及候選資料已部署。尚未以手機操作，不把合成 token、LocalDB 或本機 HTTP 成功寫成外部實機結果。
