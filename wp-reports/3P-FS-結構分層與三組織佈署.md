# FS 結構分層與三組織佈署

更新：2026-11-26 20:49 Asia/Taipei (UTC+08:00)
本包起點：`601d5056825eec30299432c054e867deb1e6b790`；目前基線含 F2 與 F1 LIFF 必要補正：`474669ddd57563ab3e657cc4098dbbe140e88ab0`
狀態：進行中；修正後完整離線自動閘門已通過，Plugin 缺旗標 High 經 ClaudeOnly 複審結清；新增測試的分組覆蓋補查及同步最新整合分支後驗證仍待完成。實機部署未完成。

## 驗收狀態

| 01 FS 條件 | 目前結果與證據 |
|---|---|
| 組織分層、跨層 lookup、欄位歸屬與 ownership | 已補足公開分層讀取及平台三表／產品奉獻類別；construction／association 的離線合併不含四張教會專屬表。legacy 原始 bytes SHA-256 保持 `AE008B2B75F9AB35EAFC78A18197CC9300676AA5A53272D33AC0B05BE3185310`。真正三份 plan 尚未成功產生 |
| goodshepherd20 既有 metadata 相容 | 570 表／22,837 欄；metadata SHA-256 `3F7236983F803C68C57C6411B800DBA8884A5A42085EC3DBC601E997E06709F7`。既有兩表的不可變 owner 只在精確組織／metadata ID 相符時沿用；contact.new_lineid 採既有長度500。其他 owner／audit／managed／security／長度漂移仍拒絕 |
| Plugin --org 與 server 身分 | 新三組織 plan／apply 核對精確名稱與非空 OrganizationId；ID 納 plan hash，apply 再讀。null 省略保留 legacy JSON bytes／hash。整合身分及組織回歸70/70，log SHA-256 `CFEE7FB7024E3DDA991FE43C3E2BCE038D10E57535DFB5FB797D60AC68C543F1` |
| 跨工作樹寫鎖與失敗清理 | 部分取得失敗由 finally 依共享→本地清理；真檔案系統 UnauthorizedAccessException 已離線重現後修復，既有互斥與生命週期9/9；最終全方案閘門待整合完成 |
| 公開 UI plan／apply／verify、指紋、定向發布 | 已接真正 SDK savedquery、平台 UI 方案、正式 ID／XML hash ledger；UI 精確組織配對30/30。已／未發布定義必須比較，不能將 RetrieveUnpublishedMultiple 的所有 ID 視為待發布。新的完整來源／續頁測試及 fixture 正在整合 |
| UI ZIP 有界生命週期及 D26 | 匯入配置前16MiB、最多128成員、單成員8MiB、累計解壓及輸出含中央目錄16MiB；保留account／contact表單剝除。離線正反例已通過，仍須接回最終回歸與真匯出入 |
| quiesce Step 3b | 2026-11-26 13:15 Asia/Taipei (UTC+08:00) 真來源補讀成功：7562候選、等待workflow0、active workflow146、信箱136、active Plugin7280。流程及方案均managed；正式solution／publisher ID齊全，88筆Complete、58筆MetadataIncomplete（內建publisher缺prefix，不假填）。Plugin全部Complete、managed、Microsoft簽署公開識別31bf3856ad364e35。清冊SHA-256 `347C485962C2E24996101C5D9871BD43BDBD8BBCACA080D73547FB3B2DB1E744`；保留政策僅草案，尚未審完或apply，不能宣稱已停用 |
| 三組織 plan→受審→apply→verify | 首次plan各exit1／InvalidCastException，第二次各exit1／FaultException。第二次固定摘要SHA-256均為 `3EAD97E3ADACA8B06523033B50F0133E0B173F100090DA38367489501B89E991`。零寫入、鎖已釋放；依02 §10不做第三次完整plan，不以改名繞過。三份真正摘要審查仍未完成 |
| 真 Live3P FS、UI export／import | 已新增Category=Live3P、WP=FS真正唯讀驗收；目前僅編譯／列出，尚未執行。部署及quiesce完成前不跑；真正UI匯出入與已發布SDK讀回仍待補 |

## 盤點失敗與未完成項目

完整 F0 inventory 兩次都被讀取器拒絕，未發布完整清冊：第一次混合大小寫欄名、第二次 aggregate count 回應形狀。第二次實機根因未確認；離線107/107相容案例不能冒充實機修復。兩份固定摘要SHA-256均為 `1C97615A4B561BC9A84C00A2132ED87F5225331A8B9BAFC48AF23726DD8C7680`。依02 §10不做第三次相同完整盤點；其餘受控只讀管理metadata步驟續做，不能改名稱繞過限制。

三個產品及T01／T02／LIFF交接狀態以共享交接檔為準。本包未呼叫任何Channel、正式組織或金流。複製帶來的舊Channel永不登錄、載入、解密或呼叫。

## 自動閘門與歷史更正

2026-11-26 07:27記錄的是先前helper測試；當時將FS寫為完成並不代表公開CLI或實機完成，已於10:14更正為進行中。當時的Release建置、Deployment1085項、文件1920/0與編碼22/0僅屬歷史基線，不能當作本次補正的最終閘門。

本次尚須執行02 §4指定的全方案Release建置、完整排除式一般測試、文件0失敗、Changed編碼0失敗、方案清單，以及28項進度頁測試。依00 §5.10仍執行「單模型（使用者指定 ClaudeOnly）」；免雙模型不等於免單模型。完整守門與審查未完成前不提交合併本包補正。

### 2026-11-26 13:27 Asia/Taipei (UTC+08:00) 續跑證據

- Schema CLI漏接既有安全數字錯誤碼診斷，已先見2項RED再修復至30/30；保持原例外上報、先釋放lease、最多四碼，不讀錯誤本文。RED SHA-256 `6D1C40930BAED08613EE92EE94B4A8C786D113FBD445D797C6D25C75AAC5B78A`；GREEN `F2568030899B232EAAECD1469EC6852CB95F0919B220F348218591F335A3D17C`。
- UI讀兩來源重用查詢，第二次重複主鍵排序，已先見2項RED，再修復至相關89/89。RED SHA-256 `EE1ADF807AE2BD774DE313BEE2A2F33C9DD57A64DA7BA11627EC36767FAC53C7`；GREEN `F2A1E03278081744A98A8A2D3AFD6DA38E320C602D5187D8842AC0B7E430A5B3`。這只證明查詢組裝缺陷，不代表已證明實機FaultException的根因。
- 最新全方案Release建置exit0、65個既有warning、0error，log SHA-256 `C502EEF3CA75B64EE75BA1C54CEBD129C0564AE03F614C904043619155B3DB7B`；當時文件1959/0、Changed編碼51/excluded1/issues0、方案清單exit0、進度頁28/28。後續修改仍須對應重驗。
- 正式完整排除式一般測試尚未結束，Deployment已回報28失敗／1614通過。失敗集中於新UI metadata護欄對SDK非公開DataMember的拒絕，合法匯入控制亦失敗；正在根因修復，不能拿代理的先前定向結果替代正式守門。代理未完成正式交接與本包ClaudeOnly。
- 兩次獨立PowerShell取證失敗：首次程序基址不在repo，尚未Connect；第二次assembly resolver在無runspace的WCF背景執行緒遞迴失敗，未進UI查詢、程序已結束、自持鎖已清理。不做第三次該取證腳本，不將其結果視為實機驗收。

### 2026-11-26 13:46 Asia/Taipei (UTC+08:00) 安全審查與補正進度

- 上次全方案正式一般測試最終 exit1，Deployment 28失敗／1614通過、Hosting208/208；失敗不可當環境限制。SDK反射已確認 EnumAttributeMetadata.ParentEnumAttributeLogicalName 是非公開 DataMember。護欄沿繼承鏈收集正式契約，保留未知資料及容量拒絕；真wire回歸先2項RED、再UI相關92/92。RED SHA-256 `F2BCC3D41A4BFBBEF2A87A0A440994E9D296915A01E40C5A8BBD6DCA72802AF4`；GREEN `82E5D20101CBEBDF9D09670A1F8498552C06E21E7B469345E4DF4907B261BD07`。
- 上述修復後正式Deployment一般篩選1644/1644、exit0，log SHA-256 `5F536EB3DE4FD8B12C0573D93CA4F5B2D8756217F00873905607D4CB8C5E7B2E`。這不取代後續安全修復完成後的全方案最終閘門。
- 本包首輪單模型（使用者指定 ClaudeOnly）有可用輸出，無Critical、1 High：Schema Apply的UI-only路徑只比較規劃投影，仍可能一併發布未審label／cascade。現由線A補完整metadata前置檢查及發布前漂移驗證，尚未複審。輸出SHA-256 `C127464991E3EB9CD5CA60DA932F0E5600E9224715E5BB5D0A42DB39C7E268F2`；這不是三份真正plan摘要審查。
- quiesce政策另經靜態安全核對，有3 High：全部managed來源不足以證明可保留；ACT None官方參照值為2而非0，仍須目標9.1版本證據；信箱三欄原值及真正apply同一候選尚未先落檔flush。程式兩項正在TDD補正，政策行為分類仍未知，草案不apply、不放行其他FS寫入。審查SHA-256 `B930F777D73C2D452634A66719C7E2EA0A408F98CE11F3E47FFC728E0C5DFEF6`。
- 舊信箱136是delivery條件候選：135筆state/status=1/2、1筆0/1；原清冊未保留三欄原值，不能稱136個啟用投遞信箱。沒有新增任何實機寫入或第三次完整plan／inventory。

### 2026-11-26 14:03 Asia/Taipei (UTC+08:00) 整合與第二輪審查準備

- F1 LIFF精確分類已獨立完整守門7769/7769、ClaudeOnly無Critical／High，d82c7a72／474669dd合併推送且遠端SHA一致。FS原樹完整保留detached b6da17e2；50個一般檔逐位元組複製至目前cl/lane-a，3個共用紀錄保留雙方：決策全歷史、90只採F0／FS／F3來源列，線A資料保留F1並合併所有log。移轉manifest SHA-256 `7BC795E1F63E0C238CE56FABA1663F684A7A0C46BE7A4F627289211B6376A8FC`；沒有stash／reset或丟棄來源內容。
- Schema UI補正真17 RED→17 GREEN，相關定向101/101，log SHA-256 `6B7EED88A0A2725FDD2BAA9B42455E213970C023ACDEB17270C6AA4E019B3FA9`。既有需寫UI表先完整檢查；發布前基準比較精確涵蓋schema未改的UI-only表。新表不RetrieveEntity；同表合法schema新增只作初始完整檢查，後段沿原schema投影，沒有宣稱所有同表schema修改期間完整metadata漂移都受保護。此限制已明列第二輪審查，不能拿限制文字直接放行原High。
- 信箱ACT 0／1／2及incoming合法3的selector／寫入／回讀已一致；同一候選原像、nullable三欄與預期0／0／2先經主檔及SHA側檔flush／關閉才第一筆Update。缺owner、落檔故障、部分更新／回應遺失、取消、按保存ID對帳與來源可回收均有案例，相關189/189，log SHA-256 `266A9B85384B389D93130B8E4A7D5426200358E18C97DD73C4C8F055722DF3C4`。CapturedAtUtc另改既有ChurchClock，受影響1/1。沒有伺服器rowversion／整批交易／自動回復的宣稱，目標9.1選項仍未核。
- 最新完整Release建置exit0、65既有warning／0error；文件1961/0、Changed編碼74/0（以本包起點計入已合併F2／F1來源）、方案清單exit0、進度頁28/28。完整一般測試仍在跑，尚未取得最終exit；第二輪ClaudeOnly尚未通過，FS繼續進行中。

### 2026-11-26 14:20 Asia/Taipei (UTC+08:00) 正式守門失敗與續查

- 前輪正式一般全方案最終 exit1，24個目標回合、8134通過／1失敗；Deployment1687/1687、Hosting236/236均通過。唯一失敗是產品QR的 `Publication_CleanupFailure_DoesNotReplaceOriginalTimeout`，在原本注入交易逾時之前的 `Directory.Move` 先收到IOException。完整log SHA-256 `2525F972D3FBA048B948E74A4C0319DDF5654AD169E89D10E5E75161DED72AFF`。不能把它當環境限制或沿用先前綠燈。
- 未修改QR程式或原測試斷言，定向單案1/1與產品全137/137目前未重現；後者log SHA-256 `603B2DBFA6FBC64731D87E711FFF09ED3640A3C563A359AD45A64C8125CC50E6`。逐案GUID目錄彼此獨立，圖片FileStream於每次迴圈末端await Dispose，沒有查到本次FS變更觸及該路徑。實際失敗的handle／原因尚未確認，不能宣稱QR缺陷已修復；後續FS修改完成仍須正式完整閘門。
- 原生目錄鎖取證指令由自動核准檢查拒絕，只回覆 `blocked by policy`，沒有詳細原因；沒有執行成果，不以其他語法或工具繞過。這不是正式守門，也不更改任何驗收結果。
- 2026-11-26 14:30 Asia/Taipei (UTC+08:00) 更正：曾誤將Plan的未發布讀取需求套到Live Verify。新增合成測試的4 RED→10 GREEN只證明新假設，並非現有Live缺陷：真正Verify明確publishedOnly=true，不呼叫該未發布訊息。已精確撤回本輪新增能力、具體參數／可見性變更及新測試，Live原檔與原FS來源bytes SHA-256相同（`417E7462EC9D6F8B556822DBFFA9E419D683284D7E3D5D73A2DC1EE815D05820`）；未修改其他FS內容。上述RED/GREEN原始log留作調查歷史，不算本包修復或正式閘門。
- 線B的F6已合併推送fbd4fd77並開始F4；本線保留所有FS內容，完成離線補正後再依02整合，不能把尚未同步F6的守門當最終結果。
### 2026-11-26 14:41 Asia/Taipei (UTC+08:00) 同表新增期間的完整 metadata 護欄

- 已核對三檔差異與實際來源 SHA，原完整 baseline 只涵蓋 UI-only 表的缺口已擴至所有需寫 UI 的既有表；寫前 SDK 一般新欄候選先脫離純值，再綁本次正式回應 ID，發布前分別對兩來源核對完整原基準與精確新增。未重新信任寫後來源，也未略過舊或新子樹；新表仍不送完整 RetrieveEntity。
- 真 RED 為17通過／11失敗，log SHA-256 `0A025C0857DE2E02F13D2412925B2B55CA254444B98C160450079E6E7622BBDB`；最終離線定向286/286，log SHA-256 `AA817C4EE6FC2C878829E622B6925054981DC86C050953D976C72008FBD4E367`。涵蓋原欄及新欄 label、cascade、兩來源相同漂移、取消 token／原 Exception、before 關閉及合法一般新增控制。定向結果不取代同步 F6 後的正式完整閘門。
- 精確限制：CreateLookup／CreateKey／AddOptionValue 及伺服器額外預設尚無完整增量規則，仍會保守拒絕；不宣稱全部合法 schema 增量或目標9.1相容已驗收。Plan→Apply 沒有完整 metadata 版本契約，最後比對與 Publish 間仍須維護獨占；schema-only 表仍沿原邊界。這些範圍將明列第二輪 ClaudeOnly，不能只用限制文字結清 High。
- 已讀線 B 新增的 F4 架構協調事項；由線 A 在獨立乾淨工作樹只補精確 Scheduling 架構分類與正反測試，不替 B 實作排程。原 FS 與所有舊工作樹內容保留。來源綁定補正尚在完成回歸，FS 狀態維持進行中。

### 2026-11-26 14:49 Asia/Taipei (UTC+08:00) 來源綁定與本機檢查點

- quiesce policy 新增嚴格大寫64位 sourceFingerprint。公開 dry-run 可無政策產生版本化 canonical 管理來源指紋；apply 強制在同次完整候選重讀 step→handler→type→assembly 及 workflow→solution→publisher 管理純值，核對後才交 beforeWrite。缺／錯指紋、來源漂移、缺失及查詢失敗皆不發布 before 且零 Update。指紋只證明已投影來源穩定，不是 managed／Complete 可安全保留的證明，H1 現場分類仍未知、不 apply。
- 公開 CLI 與直接 Run 的原漏洞真 RED2/2：同 step ID 替換 handler 後，舊 CLI 仍 exit0並更新信箱。原始 log SHA-256 `C1AD92B5A5DC312AEBF874F74D0F4D45DB545986410D170C136FBDA490F353AF`。新增及既有相關定向245/245；該代理定向曾使用 TEMP restore config／NuGetAudit=false，只作定向調查歷史，不作02正式守門；repo套件設定未改。
- 主代理另以未加 restore／audit 調整的既定完整排除式，正式 Deployment 一般測試1750/1750、exit0，log SHA-256 `AC76813E094ECCB0537FE7FF2375A4D032407E0411442F85FCF476AD731C2F7D`；solution Release build exit0、0 warning／0 error（增量建置），log SHA-256 `D90801A8A36FD64392AC7CFB114453FB5D4203A6AB1B6AB514759B2ACDD9849C`。文件1962/0、Changed74/0；新增檔及最後文件在提交前再核對。這些不取代同步整合後的全方案一般測試與複審。
- 選擇將已可建置的 FS 草稿保存為本機檢查點，再正常 rebase 已合併 F6；只提交明列 FS 路徑，保存原始 bytes／manifest，避免在 dirty 樹 rebase、stash、reset 或反覆搬移。檢查點不代表 FS 完成，不合併、不推送，整包最終閘門與 ClaudeOnly 仍待完成。原 QR 失敗及所有實機限制保留。

### 2026-11-26 17:45 Asia/Taipei (UTC+08:00) 同步後完整離線守門與第二輪複審補正

- 依 02 §4 原命令完成本輪 Release 全方案建置：exit 0、0 warning／0 error；log SHA-256 `B57A98C73A5ADD63EF836C7FF4D9A879B14588941C1F3F105688339D9B047C29`。
- 指定排除式全方案一般測試完成 25 個測試回合、8,705/8,705 通過、0 失敗／0 略過（含 Deployment 1,847、Hosting 270）；log SHA-256 `7E0A878BB36D0D66B9D67A7A557DCA4F1804A6D34E944F061731617B0877A084`。測試排除 Live3P，結果全屬離線證據。
- 檔案說明 1,990/0（SHA-256 `69607C847A8DDC13A1705216E9F8ED5BEACE002AC0F0E9A3BAF8F7FFBA780B8E`）；Changed 編碼 101/0（SHA-256 `E0D3E80DA4365FE21DCBCFB21ACE79BA1167DFA5CE80D898DEF32E6A655EB212`）；方案清單通過（SHA-256 `845249C1806921C8C34F07D1E4683C4E7FE2CF91E80E4EA62A53F7EBA85A9E82`）；進度頁自動測試 28/28（SHA-256 `AD42168CECF67C5454F8D86BF5A48B5051BD12574F23824736E94CD6282DBA51`）。
- 第二輪單模型（使用者指定 ClaudeOnly）無 Critical、提出 1 High：schema-only 的既有表雖會納入定向 PublishXml，完整 metadata 檢查卻只涵蓋 UI 寫入表，因而可能連帶發布未審 label／cascade。後續候選修正將 full metadata baseline 擴至所有 schema／UI 發布範圍的既有表，並於 before 與首筆 SDK 寫入之前檢查；發布前仍與初始純值基準比對，只允許以正式 SDK 回應身分表示的明確增量。無 views 時亦保護 systemform／savedquery 原始範圍。新增／補強 `Apply_SchemaOnlyPendingFullMetadata_RefusesBeforeEvidenceAndEveryWrite`、`Apply_SchemaOnlyApprovedAttribute_PublishesExactDelta`、`Apply_SchemaOnlyMetadataDrift_RefusesBeforePublish` 等真引擎合成回歸，涵蓋 schema-only、標籤／cascade pending、單／雙來源漂移與合法欄位增量。修正尚待第三輪 ClaudeOnly 獨立複審，不先宣稱外部 High 已結清。
- H1 行為分類仍未知；未執行 quiesce apply、schema apply、UI import／publish 或其他 D365 寫入。三組織完整 plan 與 inventory 已各兩次失敗，依 02 §10 不做第三次相同操作；三組織實機及 UI export／import、Live3P 仍待補。進度頁目視待補且不以自動測試替代。
## 手機待驗

FS無手機步驟；手機畫面集中M1。目前進度頁目視亦待補：先前電腦操作被使用者Escape停止，本輪不再呼叫，不宣稱頁首無警告。


### 2026-11-26 15:47 Asia/Taipei (UTC+08:00) 同步 F1 與 H1 證據追查

- F1 Scheduling及QR必要補正已合併推送`347fa2636174d2bdf079b212711fdc2736517d67`，遠端與整合HEAD相同。F1正式完整一般7943/7943、ClaudeOnly無Critical／High；其真鎖修復不是原FS現場owner辨識，原FS8134/1失敗證據保留。
- 乾淨FS檢查點正常rebase到此整合，三份共用文件逐欄保留：決策全部不同歷史、90只採F0／FS／F3來源列且保留F1與線B、線A資料合併全部log。其餘51檔對原CRLF備份逐位元組不變；manifest SHA-256 `76D60FCF47C12D084CDD60191672AD20B999275FB7983ABC808A486499AEDEE8`。目前程式HEAD `ecd83b94eb55266d2d243c896e9a3d7ddfb77b55`，分支恢復cl/lane-a；舊樹與所有備份保留，不stash/reset。
- 純唯讀H1追查報告SHA-256 `01AF62361FDF0080A215C57DC337F8AEE4B440F28244B74F19EA7905DBB1EE7A`。完整正式ID來源能證明身分，不能證明7280 Plugin及146流程均可keep；EmailPlugins五筆不同type均維持unknown。缺目標版本觸發／相依及可信行為／安全停用契約，未讀組件content、workflow本文或設定；不能由managed／簽署／名稱或sourceFingerprint冒充行為證據。H1尚未結清，舊policy不apply，其他FS寫入也不放行。
- sourceFingerprint補正已完成：嚴格大寫64位，apply同一次候選核來源後才beforeWrite／首Update。正式Deployment1750/1750是前輪證據，不是這次完整方案結果。目標9.1三欄投遞選項實值仍待補；不把離線0／0／2當實機核對。
- 同步後02原命令Release建置及完整一般測試執行中；第二輪ClaudeOnly將補齊原FS34檔基礎提交與此次全部最終來源，不漏舊檔，不把實機待補或H1未知寫成已完成。


### 2026-11-26 19:48 Asia/Taipei (UTC+08:00) UI ZIP RootComponent 邊界補正與離線驗證

- 沿 UiSolutionDeployment.Import 確認整份 ZIP 在發布範圍檢查後直接送入 ImportSolution；先前只收集表單／檢視 ID 並不能限制匯入 ZIP 的其他根元件。未允許的 RootComponent type="62" 負向案例與 type 61 正向控制已保留。
- Microsoft Learn RootComponentBehavior 定義 0=IncludeSubcomponents、1=DoNotIncludeSubcomponents、2=IncludeAsShellOnly。依本包 UI-only 信任邊界，初次解析只接受 Entity、Saved Query、System Form、Web Resource 四種根類型與穩定身分；D26 移除共用 account/contact 表單後，最終送 SDK 候選的 Entity 根必須精確為 behavior=2。完整子元件、僅根元件及缺少 shell 行為皆在 ImportSolution 前拒絕；只有已移除表單的 account/contact 根才由篩選器轉成 shell-only。
- UiImport_EntityRootIncludesSubcomponents_RefusedBeforeImport 的預期 RED 已重現（未拋 InvalidDataException）；shell-only 正向控制通過。最小修正後 UiSolutionArchiveTests 28/28，Deployment 專案正式排除式測試 1852/1852；兩者均為合成離線 SDK 證據，不代表實機 ZIP 匯入。檔案說明 1990/0、Changed 編碼 101/0、進度頁測試 28/28 亦通過；整方案 §4 閘門與第三輪 ClaudeOnly 仍待執行。
- docs/file-purpose-companions.json 已把 organizations.json 說明由單一組織修正為目前四個 test 組織。線 A 的進度資料 JS 與 90 的 F3 待辦文字均為「等 FS 完成後開始」，並保留 F2 已於 b6da17e2 合併的證據。
- 未連線任何 D365、LINE Channel 或金流，沒有執行 plan/apply/import/publish；H1 行為分類仍 unknown，三組織完整 plan 與 inventory 各兩次失敗後依 02 §10 不重試第三次。FS 狀態維持進行中，所有實機驗收仍如實待補。
### 2026-11-26 20:26 Asia/Taipei (UTC+08:00) 第三輪 ClaudeOnly 發現與 Plugin 註冊修正

- 專案 CCG 自我修復入口以 ClaudeOnly 成功完成第三輪 FS 審查。首份輸出雖寫「無 Critical／High」，同時承認未讀兩個新增測試及多數未覆蓋路徑，不能作完整放行結論；補充覆蓋讀取主要未審生產程式及部分測試後發現 1 項 High：DeploymentEngine.IsExplicitlyDisabled 只把明確 false 視為不註冊，旗標缺漏時仍對 goodshepherd20、construction、association 規劃 CreateStep。
- 已以 Registration_NewTestOrganization_UnlistedStep_IsNotPlanned 做 RED：三個新測試組織均失敗，實際計畫包含 CreateStep；證據為該定向測試之真實失敗輸出。最小修正改用 ShouldCreateStep：非 legacy 組織只接受明確 true；缺漏或 false 均不建立步驟／映像。goodshepherdback 的舊缺漏旗標「建立後停用」相容語意保持不變，明確 false 仍不新增。
- GREEN 驗證：Registration_NewTestOrganization_UnlistedStep_IsNotPlanned 三組織、Registration_OrgFlag_OnlyEnabledStepsRegistered 及既有 Apply_NewStep_DefaultDisabledUnlessManifestEnables 正反控制合計 6/6 通過；Release Deployment 測試專案定向執行成功。全方案完整自動閘門尚未在此修正後重跑。
- 修正後複審仍待執行；覆蓋補充輸出也列有尚未逐檔讀完的測試與設定，故目前不宣稱 ClaudeOnly 全範圍審查完成，會在放行前補齊覆蓋並明確保留所有未讀清單。
- 未連線任何 D365、正式組織、LINE Channel 或金流，未做任何實機寫入。H1 行為分類仍 unknown；quiesce policy 不 apply；完整 plan 與 inventory 各兩次失敗，依 02 §10 不做第三次。F3 註記維持「等 FS 完成後開始；F2 已於 b6da17e2 合併」，JS pending 維持「等 FS 完成後開始」。
### 2026-11-26 20:49 Asia/Taipei (UTC+08:00) 修正後完整離線閘門與 High 複審

- Plugin 缺旗標修正已獨立提交 `386f1d74e2f40e1072eb87563915c5c25c726347`；受影響 6/6、完整 Release 建置 exit0（0 warning／0 error），SHA-256 `68E0E654CFCE9B75A7BE4BC5D1B7C6F59F92EBB81F23A7A2A7BC67F5B2C8F0A8`。
- 02 §4 原排除式全方案一般測試完成 25 回合、8,713/8,713、0失敗／0略過、exit0；Deployment1855、Hosting270、LineWebhook143。log SHA-256 `DA54116EFC74704A92B0B4F52A39421A3B8803F080CB7DA66C0B02F578ED9086`；全部是離線結果。
- 文件說明1990/0（SHA-256 69607C847A8DDC13A1705216E9F8ED5BEACE002AC0F0E9A3BAF8F7FFBA780B8E）、Changed編碼101/0（E0D3E80DA4365FE21DCBCFB21ACE79BA1167DFA5CE80D898DEF32E6A655EB212）、方案清單exit0（845249C1806921C8C34F07D1E4683C4E7FE2CF91E80E4EA62A53F7EBA85A9E82）、進度頁28/28（5E799A59A3718C26CD9081C73FBFBFAD71A5217527A25578C5200B4E12168944）。報告曾有7個LF已只正規化CRLF，文字未更動。
- 單模型（使用者指定 ClaudeOnly）修正複審確認最新 High 已結清，已讀生產修改區段無Critical／High；輸出SHA-256 `877C93DF67B264E800DD1D4515718D010ED0FA3612548266CE3A79D5D939E4D4`。但約6000行新增測試仍只模式掃描，已拆為兩組同一Claude模型的限定唯讀補查，未呼叫Gemini，不把模式掃描冒充全文審查。
- 最新安全修正已存本機提交 `0d5515fcd076b4b78dcf3760c16c23b6479defbc`；來源及未提交文件備份manifest SHA-256 `FE047C90551CC8553B4CC53686B3C720FA7BC15FC0037CD900422775756B2ADD`。這不是FS完成或放行，尚未合併／推送。最新整合HEAD2da59fac含線B的F4／F6，正常同步並重驗後才能合併。H1 unknown、不apply、零D365寫入、實機待補及兩次失敗限制不變。

## 下一步

先完成第三輪覆蓋補充並複審上述 High 修正；更新後重跑 02 §4 全方案守門。若覆蓋完整且沒有未修 Critical／High，再依 02 §5 鎖內同步、必要重驗、快轉合併並核對遠端。所有 H1 分類、Step 3b、三組織 plan／apply／verify、UI export／import 與 Live3P 均如實標「實機待補」，不做第三次完整 plan／inventory，不進行 D365 寫入。
