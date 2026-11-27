# F3 Channel 名額池驗收報告

更新：2026-11-27 12:12 Asia/Taipei (UTC+08:00)。本包起點 `872be9cd1ae82bb56711914653bdde24027b05f4`；目前進行中，正式完整閘門失敗修正後正在重跑，尚未合併，沒有 F3 實機成功宣稱。

已實作三產品共用純名額規則、同步交易 Plugin、分層 schema／精確三步註冊、獨立 net462 打包、固定產品目錄／健康／一次 token 輪替重解，以及好牧人真實 Webhook 接收器。既有 LineWebhook 與 2.0 委派同一接收器。未配置時拒絕，不保留 F2 的無條件 200。

| 01 Expected／測試 | 結果與證據 |
|---|---|
| Step1 四個 Choose 規則、精確 ID／同名不同 ID | `Choose_PrefersLowestPoolOrderThenLowestSlot`、`Choose_IgnoresNonOperatingChannels`、`Choose_OnlyOwnProductChannels`、`Choose_NoAvailable_ReturnsPoolFull` 及重複／同名正反例已 RED→GREEN。規則 GREEN SHA `F03C89426CA49F517EE7C2EC95542725F533077650A1E15E3F313B87C6499B9B` |
| Step1 二十並行、產品不符、最後名額、未知組織、非租戶、建立名額 | Plugin 62/62 離線通過；另驗首次鎖缺失二十競爭、原例外／rollback、A/B 重用、重試、釋放清理及精確 Update PreImage。SHA `98DC34BB9EE3D90475BA74D41F1466A0F2854C1389CB1D7750CFF9DF512B4E6C`。產品不符持久共用告警補正進行中，故本包仍未放行；替身鎖不是實機 SQL 證據 |
| Step3 三產品 schema、平台組件與註冊 | 真正 repo manifest 的 Update 完整型別曾 RED，修復後 F3 註冊及 schema 59/59；SHA `DE194E436474387ADDBF9749E9EC9D08EE03606575B78E48127D3D609F95943D`。只公開兩個 IPlugin 入口的真組件檢查曾 RED，已將 helper internal。部署 plan／apply／verify 實機待補 |
| Step4 Directory／secret／refresh／空產品／health 401／輪替兩次 | 定向 Channels 26/26，另加 SDK metadata→密文、精確產品／版本 CAS、取消淘汰及歸還測試。真來源外產品固定告警補正中；最終完整閘門待跑 |
| Step5 好牧人 Webhook 接線 | 新 Channels 39 案加既有命中共42/42，SHA `E1DA12280B4316180738177EFD9FB061DB39FC922851BE9F4208BFF7A8DCCF1A`；真 Kestrel 錯 secret/T01→T02 401、raw bytes 正控制、queue0／合法事件、32並行、64 admission、缺錯設定與 drain/key清零。主機12/12 SHA `8BA4460F1A2CAB5C8554C8FAF178D7FE3286C94D984276E8083EB14B797FEFDA`；舊HTTP回歸56/56 SHA `12A1C3EF21FC0F2629E60F4EA95B1ED0664FF74B3E2DCAC6BA12A27ED62A59F8` |
| Step6 登錄、Webhook API、二十並行與 construction 配發清理 | 實機待補（可自行處理）：先依02A完成F3離線合併→goodshepherd20 quiesce→F0／FS診斷與三組織打通，再做本步。Live入口／清理協調者離線48/48；不能當實機通過 |
| Step7 完整閘門、審查、提交與推送 | 待完成；以下最終守門未取得前不得合併 |

## 最終自動閘門

- 全方案 Release 建置、指定完整排除式一般測試、文件說明、Changed 編碼（包含先加入索引的新檔）、方案登錄：待跑。
- Access 精確共用層分類及正反例78/78，SHA `881AF37FA515D2C13055B2012B8342F2952A1B1DECDB1DA8B3418F5CDC1BE3DA`；不改線B實作。
- 02A進度狀態 renderer 2 RED→4 GREEN，SHA `26001791C5FD592140B8DC7F141656F951A4C86626CEB629307EB8B2A0F419E6`；待補不計入完成、非法值仍警告。歷史進度入口28項待本次正式跑。
- 正式審查須「單模型（使用者指定 ClaudeOnly）」；目前待跑。前段代理仍呼叫過 Gemini 設計分析／側審且為503，沒有取得雙模型完成，不計為正式閘門；本輪已禁止後續外部代理呼叫，由主代理統一正式入口。

## 實機打通與回復

本輪尚未執行 D365／LINE 實機寫入。依02A順序與test-env鎖逐步做，使用repo外先flush原值與SHA側檔；新建項目故障保持停止，僅依正式ID及本輪所有權清理，不以顯示名刪資料。Plugin／schema／Channel 登錄／並行清理結果及原始log SHA將在本節追加。H1只有停用與驗收完成才寫「依02A §2結案」。

## 已知事件與限制

代理在09:06附近誤執行Deployment全套未排除Live3P，結果1866通過／48失敗，原始log SHA `8021AE8281C8B32705D19F040EF7AFB60B7BCE220EB807C8ED165E04B39DD474`；不可當通過證據。後續核對原始Exception.log SHA `69007EDAA03D5F7DA5FF5C0CA3A1C9D3F9A604A3954A814EBAABA842E8057D5A`：失敗於FS-LIVE-04。ThreeProductSchemaLiveTests第107行前已越過測試組織身分核對；第301行ReadOnlySchemaService拒絕所有CRUD／關聯，Execute只放行三種唯讀metadata訊息。程式路徑無轉送寫request入口；原始log沒有逐request網路稽核，不宣稱網路層零寫入已證明。後續一般測試一律完整列出五個排除類別；未發現正式環境連線證據。

## 手機待驗

本包先以T01／T02 webhook/test及伺服器結果驗證；手機畫面集中M1一次確認。尚未進行任何手機驗收。

## 停線紀錄

2026-11-27 10:42 Asia/Taipei (UTC+08:00)：整合工作樹的 `實作進度.html` 有他人未提交的頁首時間分流修改（10:27修改；HEAD仍16e1b3626），與本線必要02A狀態相容修正重疊，依02 §7第4項停線。沒有取得合併鎖、沒有stash／reset／覆寫、沒有提交或合併。等待現有修改先由其owner提交後，保留雙方內容正常同步。lane-A及需要你協助已記錄，指定即時更新腳本exit0；所有代理已要求安全停止並回報草稿。最終閘門仍未通過，本包與整體目標均未完成。

停線草稿補記：Channels foreign-product 探測補修只讀檔尚未實作；Live helper 仍為13案中12案 NotImplementedException 的TDD RED，不可提交或放行。所有原始草稿保留。

代理均已安全停止並關閉。ProductMismatch新10案初次5通過／5斷言失敗（SHA `B2CE5DDFCA0ABF91DB26092CF8E0B46E4DAF535BEADD20968771858ADC2B14B2`）；最後產品回歸71案68通過／3失敗（SHA `4EAB0767FD741526AE977BE06FE7052C4202D1F4607CB6F1C73D212564C717A5`，檔名含green仍不是通過）。UTC解析已保存但未重測，新測試檔LF待修；共用receiver仍缺，High未解除，不得放行。

## 2026-11-27 11:12 續跑結果

10:42停線已於10:48由使用者標記已處理：Claude自行備份還原當時頁首草稿，線A不再等待。後續整合新增的已提交進度頁改動將於正常rebase保留。以下為當前結果；前節失敗／停線記錄保留作歷史證據，不代表仍在停線。

| 驗證 | 本輪結果 | SHA-256 |
|---|---|---|
| Plugin 完整定向 | 72/72 | `9958504F6923CEB8000B7F0D183ACFA416ADAE6B0A1281752F8A8BACFFCFFFFD` |
| Host Channels | 45/45 | `8F15F18ADA9F2AEF558894DFC86D85BA6B7559076F976132B653D15C11DEF7CB` |
| ToolUtility Channels／receiver | 49/49（net10；net48無此類別） | `91F49E76CD4E5DD864699BDB6BED03DF35291A7C22B6F8EF300C24E1143011AB` |
| 方案 Release 建置 | exit 0，0錯誤、39既有警告 | `8F759A2C12C74C4DA9F1B555FC302E8BE24DEE3BE426E1FD7FF5A152B13AC5DD` |

ProductMismatch已補：Plugin隨申請單交易提交六欄安全封套，主機每分鐘以最多16筆、固定組織與正式RowVersion CAS領取，經共用ReportPersistedFailure先flush日誌再通知；保留incident及原UTC，明標Reconstructed，不假稱原沙箱堆疊或exactly-once。接收器12案、主機pump3案包含在上表。共用入口Debug／Release各50/50的原始SHA見下列證據。

- 共用入口RED：`86C0C820054925D4B60BC12180E1D07511C1D3146A19BD78E076439D18784EB3`。
- Debug：`1115120BBC9642F376A82840EA8B4DC47AE101B2BC99AACD6C2AD2D1C274177B`；Release：`7A7EFE312077A02860E126A7E2F52857AAF0A6ECE28893CE16240398C3C27413`。

真SDK來源完整刷新新增同一WhoAmI租約的外產品標記觀察：TopCount=1，只投影new_product；本產品metadata／秘密查詢仍精確過濾，外產品不讀路由或密文，同一owner只告警一次。主機真SDK替身同步驗證精確觀察條件，未放寬本產品查詢。

Live helper仍在完成離線RED→GREEN；完整方案一般測試、最終文件／編碼與ClaudeOnly尚待。因此本包仍進行中，尚未提交／合併／推送，沒有任何新增實機結果。

2026-11-27 11:27 Asia/Taipei (UTC+08:00)：schema新增名額／申請單樂觀並行前置契約，三組織預期true實際null先3 RED（SHA `31CE4646EF1B401341484D167CACCC7659769217C318D2C14F185943EC090E4C`），兩張F3自有新表明列optimisticConcurrency=true後，正式分層回歸42/42（SHA `3CA6F172BD0FC457C59A4D8FF17B90C00D53F458DFCF3E9250BF81EC4D722F3D`）。只要求建立後server回讀核對，不修改唯讀SDK欄位，不放寬未知／false。32項進度頁測試及方案清單通過；平台兩新專案各恰好一次。

## 2026-11-27 12:12 正式閘門失敗修復

首次完整守門建置成功，但一般測試有三項失敗，沒有放行：原子export遇Windows拒絕存取、三組件來源進舊切換路由、架構測試以短型別名誤驗精確公開契約。原始失敗log保留於本機；下列定向結果不能取代正在重跑的全方案閘門。

- 原子export以真Windows讀鎖及唯讀屬性重現：先完整寫入／flush／關閉同目錄tmp，再發布；僅精確Windows暫態HRESULT有界等待一秒，失敗保留第一個原例外並清tmp，不改ACL／屬性、不先刪舊檔。11/11定向通過；RED SHA `6EDFAE2E248138F00746C09E3F38E4C85A6F27A9B6F46C86C5F6E420438F1BA3`，GREEN SHA `3137AA0BA8E866EFDC8BBE9C3D2646803B4E98769B0A913F1EEC8DDFA0EE612C`。歷史占用程序身分未知，沒有宣稱已找到外部owner。
- 完整來源先驗證再按組織投影：goodshepherdback／goodshepherd20／construction／association分別2／3／1／1組件；核心Plan／export仍拒不適用候選，legacy switch／shadow／live明確排除平台組件。四組件、重複、非法但將被投影排除的來源仍拒絕。
- 新增三組織真CLI合成生命週期：verify差異→export→plan→apply→verify一致→rollback，construction／association只提供平台DLL，避免意外讀取教會組件。首次apply真RED揭露平台設定沒有教會mode，修正為平台精確契約不解析mode、不解析秘密且禁止secure設定；教會原路徑保留。舊FS測試改成證明教會候選跨產品拒絕，平台正控制由新生命週期覆蓋；不是放寬產品檢查。定向74/74，SHA `44CC4D94772C23A50F01F0001A52214D9C0CA4D1FBDFE919CA34A1427703CB68`。
- 架構測試改用完整namespace驗證三個型別，原ArchitectureRules未放寬；先1 RED再1 GREEN。
- Live helper離線48/48，SHA `4937A85E00DCAA1D4350E0299C7A8EF8AC41A036FEF28C38A836715E106FBF11`。二十並行先排空，再按正式ID／本輪marker清理並CAS恢復基線；「好牧人」登錄中不配發、不要求池容量。真失敗走file-only共用診斷，不呼叫Channel作告警。

本節只有離線結果；F0、FS、F3實機及M1手機均未新增通過證據。

## 2026-11-27 12:25 一般測試身分案例補正

12:11完整一般測試最終exit 1，Deployment 21失敗、其餘回合無失敗；原始SHA `BEE75E9ECB4052F921832CE0DCD8347FC22B6948357D16C69752531ABB289A56`，未放行。根因為舊身分fixture對construction／association提供教會組件，現在被正確產品邊界拒絕，尚未進入指定身分故障注入。改為重用F3合法平台候選、映像及metadata服務，舊組織與好牧人保留原診斷候選；所有身分、雜湊、metadata前拒絕、零export／寫入及正常往返斷言保留。new_product合成metadata同步採schema的字串型別。身分與F3平台註冊定向118/118、exit 0，SHA `756E521FCA78A84C3D65F9E8BE2C808324EDF5C31F83EBFD0880FD1E560499B7`。同步整合後仍須完整守門及ClaudeOnly。
