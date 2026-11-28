# F6　LIFF 共用骨架與租戶身分


## 2026-11-28 14:29 Asia/Taipei (UTC+08:00)｜02A公開部署補正及實機結果

目前狀態：自動測試通過（實機待補）。本輪程式提交 `6ede902e1d88198cc1885d65e1d14e303480c5e3`；P0／F2 SQL及公開live／ready先前已打通，F2證據已合併推送72b401258並核對遠端SHA。本節取代舊公開頁面／遠端SQL前置待補，歷史證據保留。

| 驗收 | 結果與範圍 |
|---|---|
| 根因與補正 | F2封裝重產開發JSON時遺漏repo核定LIFF三個公開識別。只從64KiB內publish JSON取LoginChannelId／MessagingChannelKey／LiffId，驗完整三鍵、string、ASCII界限與app/login關係後逐值建立獨立候選；其餘區段不複製，不攜出秘密，Strict pin與CRM環境owner不變。 |
| 離線先紅後綠 | 原函式拒絕新參數，新增正向案先RED（25／26）；修後PS7／PS5各26／26，涵蓋來源後續變更不改候選、空值／缺項／錯型別／越界／app不符／未知Token鍵皆拒絕。 |
| 完整守門 | 同一程式樹全方案Release65警告／0錯誤，一般9516／9516、28組件、0失敗0略過；文件2093／0、Changed4檔0問題（另1原工具排除），三個程式檔另逐位元組驗UTF8無BOM／CRLF／末尾CRLF，方案54項、進度頁32／32。建置與一般測試期間未publish同一bin。 |
| 外部審查 | 單模型（使用者指定ClaudeOnly），ok=true、無Critical／High，沒有雙模型。只靜態讀diff與新測試，未跑測試、未核對報告；非阻擋觀察已寫之後再說。 |
| 雙鎖部署 | 先確認整合72b401258仍為祖先，D365→SQL雙鎖下部署固定D365APP01／speechmessage.com.tw、GoodShepherd2Test、9443；RunId `20261128T062710Z-b294aab3904f430bb8471c1868d7c024`，LivenessPassed。原release與精準IIS回復紀錄保留，沒有CRM／443／信任庫變更，finally反向釋放鎖與credential。 |
| F6唯讀Live3P | 首次1通過1失敗確實抓到空app；修後明確Category=Live3P&WP=F6 **2／2、0略過**。whoami.html／liff-router.js與本線SHA完全相同，公開config只有核定app及Development；匿名whoami與合成租戶ping均401、無Set-Cookie。Host已完成正式LiffSelectionSchemaStartup才開始HTTP，代表以真平台SQL初始化／驗表成功，未冒稱另有獨立SQL metadata讀回。 |
| 剩餘外部相依 | FS／F3尚未完成測試租戶、Channel及角色資料，T01 webhook/test仍401；真實ID Token、本人租戶／角色、跨租戶實機與M1手機未驗。不以匿名401稱過期或aud不符實機通過，不呼叫產品或複製舊Channel。 |

原始log在repo外evidence，SHA-256：PS RED `531ED490E844BD55C3767A4F4CBFAA4F1C9ABC172D48EB11B2EB43F740BCA545`；PS7／PS5 GREEN同為 `A3F4DAEC250C9768B690134919A86445FD9A603117C160006448E1041C660543`；首次Live3P RED `53958B7CC72DB6307255DECE419C853E5B83BE3447ED39A9DCC016A708D1D6D3`；build `D90B47A24F82A2678B796DE5DA6A1E30634F784C280F9DD8AC17D5DFB9D40651`；tests `3357C97ED9C20152D413004A0FA9C1B6DC8B3D132CCB75223B7561AF16780EF6`；Claude stdout `43AA1C5EA5AA992C6530B2D2371DF5660E6010C5E231C1A4A09A657EF3B84130`、summary `137545382AABE4BBCAF406DE67E3B6ABDAE17CC910DE6F3985F68F2DC792D54A`；deploy `E2F841F35326CCB8F339194CE4BC31739302685B600930953F605D9A8A23517A`；Live3P GREEN `AFC156FB742B0AB8C120C53F07E58F02662E2334CB24FA0BB5A915B1F4D316C5`。

回復使用本RunId的前release及GoodShepherd2Test專用欄位快照，先持D365／SQL鎖、核目前release仍本輪目標，再停止／排空及精準回復；不整站Restore，不覆蓋其他線或後續設定。新增line_current_selection是正式F6表，不當暫存刪除；未建立真使用者選擇資料。本次不增加手機流程，M1仍由線A負責。

以下是歷史驗收與當時狀態；舊「完成（自動測試通過）」不代表目前已達02A五項完成定義。

---

更新：2026-11-26 17:00 Asia/Taipei (UTC+08:00)。線 B；本包起點 `b6da17e2b6ddf88dc22d5ceb5f5551039c6315a9`。狀態：完成（自動測試通過）；本次獨立平台租戶choice補正程式提交 `97e93413bd398f1f63998d17784a7c2a465f3ed8`，全方案守門及第三輪單模型複審通過；程式原提交 `162caf44ec97af736e1ce58113449c1dbd0e1833` 同步整合 `474669ddd57563ab3e657cc4098dbbe140e88ab0` 後重寫為 `44f6df89380e8b81d9e681c557dab09f629b4282`，程式樹逐 byte 不變；全方案守門通過，依02§5接續合併推送。公開站台、遠端SQL及手機結果仍未通過，不宣稱測試環境完成。

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

## 後續必要補正：平台租戶旗標 choice（2026-11-26 17:00 Asia/Taipei (UTC+08:00)）

後續對照線 A 的 `d365/schema/platform/00-core.schema.json` 時發現 `account.new_istenant` 實際為 choice（否 `100000000`、是 `100000001`），舊resolver與當時合成fixture都用 bool，故早先測試未覆蓋真實消費契約。保留原完成紀錄及其當時證據；本節補上缺口與修正，不把原結果改寫成曾驗證choice。

| 補正驗收 | 結果與決定性證據 |
|---|---|
| 正向choice與alias來源 | 測試直接讀本工作樹平台JSON之唯一choice並建立SDK `AliasedValue`／`OptionSetValue`，確認resolver發布原Contact／Tenant、角色只在租約歸還後讀取；切換只在歸還後寫入固定product/channel/user及Contact。 |
| Query契約 | 單獨檢查實際inner join的`new_istenant`等於平台「是」整數值；空SDK回應避免候選檢查掩蓋查詢仍傳bool的錯誤。 |
| 錯值及隔離 | 「否」、未知choice、bool true／false、裸int、字串、缺值、null、錯誤aliased entity／attribute各自通過actor解析及selection兩入口；角色讀取、selection讀寫皆為零。拒絕時先fault後release，未發布actor；無額外型別轉換或容錯。 |
| RED→GREEN | `Resolve_PlatformTenantChoice_UsesAuthoritativeOption`、`Selection_PlatformTenantChoice_ValidatesBeforeWrite`、`Resolve_QueryFiltersTenantByPlatformChoice`、`Resolve_InvalidTenantChoice_FailsClosed` 先以原始bool程式重現 **5失敗／19通過（24總案，filter另選入既有F4一案）**；修正後本產品 **175／175**。 |
| 全方案守門 | Release建置 **65警告／0錯誤**；固定一般測試 **8049／8049、0失敗／0略過（26個組件）**；檔案說明 **1991／0**；Changed編碼 **3／0**；方案列舉及`git diff --check`通過。這三檔不新增專案或改方案。 |
| ClaudeOnly複審 | 單模型（使用者指定 ClaudeOnly），**0 Critical／0 High**。審查為讀碼，未執行建置或測試；測試數據以上述本線log為準。 |

修正提交：`97e93413bd398f1f63998d17784a7c2a465f3ed8`。正式碼只把查詢條件改用平台choice數字，且只接受entity=`account`、attribute=`new_istenant`的OptionSetValue「是」；測試fixture一併改成choice。未改FS平台schema、F3或共用LIFF。原始log均在工作樹忽略目錄，未進repo：`solution-build.log` SHA-256 `9440D82DE0B61475715EF99210CC064DFF664C99CD0BE9FA34464B7033C3BFBB`；`solution-tests.log` SHA-256 `805D0B9642428AF761C95EB303436D5A5EA82B64C2A8968AFBF31DA262E33517`；RED TRX `20EF508B771293B9EDB4DE2088483B2C52E1E04AD92BC5E4CD03C7498699C96F`；產品GREEN TRX `3AE61BB653E3669278F81C321887D2BEB3CC69DE03558FFF95A5BBDD91969F00`；ClaudeOnly輸出 `C2AC4E61E77748DF9EBB72EF60C6B41A7E5419C02A788ADB8FD9E9CC73DFA214`。

此為離線schema消費及合成SDK契約，不代表D365實機、VM SQL或公開站台驗證。F2公開live／ready、嚴格TLS平台SQL、FS／F3實體資料與手機M1仍待補；全階段更新版進度頁未另作瀏覽器目視，前次F4 Esc取消後沒有恢復UI。F6補正完成後依序等待FS後才開始F8，不修改線A工作。

補記2026-11-28 14:30 Asia/Taipei (UTC+08:00)：最終文件2093／0、Changed編碼7檔0問題、進度頁32／32及JS語法、diff check通過。SHA-256：docs `06FA7444456000A4F2C305CE1BB4F5494952B148B9310D1160CA5A70E8D1CF3E`、encoding `8EDFFC49DE6CED783F6E8521DE4A4FB4591273EC3383B2A27E18808429CBAEDE`、progress `96DECEBDA9F65475CC5C4B32D6E238E0B61C7DDA914CE5C10B546C8B43BA8486`。只有文件改動，依02A§10.3沿用本輪同程式樹9516案守門；新增進度頁未取得瀏覽器畫面，不宣稱目視。
