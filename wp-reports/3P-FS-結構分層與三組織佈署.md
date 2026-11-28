# FS 結構分層與三組織佈署
更新：2026-11-28 15:50 Asia/Taipei (UTC+08:00)
本包原始起點：`601d5056825eec30299432c054e867deb1e6b790`；歷史同步基線：`2da59fac62aab177a05efcba07132b69616b4243`（2026-11-26）；本輪修正起點：`36d4c49724fe90b522304048d3bec051ea710134`；本輪程式原提交 `2e6de8d0e14bc5a3d86423cc33104ef08db5519c`（重基線後 `c4cf53a63`）；最新整合基線 `e2eac3c38587469c9a250362493b2ba3908e102c`；重基線報告提交 `1bfe1a23e945118cd38dc578247a6f16ad087115`。原程式與先前修正提交：`64679a87f`、`9af227811`、`bd22905fc`。
狀態：進行中（goodshepherd20修正後apply1/2已部分寫入並於UI階段失敗；非零寫入、非已完成）。before及唯讀after已保留；7張新平台表與schema操作已到位，UI仍2項待做；construction／association未apply。先唯讀定位根因，剩1次修正後apply額度不可原樣使用。

## 驗收狀態

| 01 FS 條件 | 目前結果與證據 |
|---|---|
| 組織分層、跨層 lookup、欄位歸屬與 ownership | 已補足公開分層讀取及平台三表／產品奉獻類別；construction／association 的離線合併不含四張教會專屬表。legacy 原始 bytes SHA-256 保持 `AE008B2B75F9AB35EAFC78A18197CC9300676AA5A53272D33AC0B05BE3185310`。三份完整 plan 修正後重試 1/2 均成功（詳末段）；新表 ownership、平台／產品範圍尚待合併審查，不直接套用 |
| goodshepherd20 既有 metadata 相容 | 570 表／22,837 欄；metadata SHA-256 `3F7236983F803C68C57C6411B800DBA8884A5A42085EC3DBC601E997E06709F7`。既有兩表的不可變 owner 只在精確組織／metadata ID 相符時沿用；contact.new_lineid 採既有長度500。其他 owner／audit／managed／security／長度漂移仍拒絕 |
| Plugin --org 與 server 身分 | 新三組織 plan／apply 核對精確名稱與非空 OrganizationId；ID 納 plan hash，apply 再讀。null 省略保留 legacy JSON bytes／hash。整合身分及組織回歸70/70，log SHA-256 `CFEE7FB7024E3DDA991FE43C3E2BCE038D10E57535DFB5FB797D60AC68C543F1` |
| 跨工作樹寫鎖與失敗清理 | 部分取得失敗由 finally 依共享→本地清理；真檔案系統 UnauthorizedAccessException 已離線重現後修復，既有互斥與生命週期9/9；最終同步後全方案閘門通過，見下方最終證據 |
| 公開 UI plan／apply／verify、指紋、定向發布 | 已接真正 SDK savedquery、平台 UI 方案、正式 ID／XML hash ledger；UI 精確組織配對30/30。已／未發布定義必須比較，不能將 RetrieveUnpublishedMultiple 的所有 ID 視為待發布。完整來源／續頁及 fixture 已納入最終 Deployment 1,855 項離線測試；實機待補 |
| UI ZIP 有界生命週期及 D26 | 匯入配置前16MiB、最多128成員、單成員8MiB、累計解壓及輸出含中央目錄16MiB；保留account／contact表單剝除。離線正反例及最終回歸已通過；真正匯出入實機待補 |
| quiesce Step 3b | 2026-11-28 08:47 Asia/Taipei (UTC+08:00)依02A §2.6各項一次讀回；待處理流程執行個體0、候選Workflow 0、未分類Plugin 0。H1依停用政策結案；211個信箱仍有個別未確認不投遞，依§2.6記錄且不阻擋FS。goodshepherdback基線一致SHA-256 `227FA949B78311F0A7A32019E65C5D36AED9C63A695A24F56C2513F074B7B91C`；讀回證據SHA-256 `9D23E42C1807EE2BD8124C9F2D6C073802E9CDA48BF5168A143090832AC9FFA0`；零D365寫入。|
| 三組織 plan→受審→apply→verify | 完整 plan 修正後重試 1/2 全數成功：goodshepherd20 164、construction／association 各 216 項；各另有平台 UI changes=3。單排序／雙排序同型故障差分與 RED→GREEN 證據見末段；apply、verify、UI 匯出入仍未執行。|
| 真 Live3P FS、UI export／import | 已新增Category=Live3P、WP=FS真正唯讀驗收；目前僅編譯／列出，尚未執行。部署及quiesce完成前不跑；真正UI匯出入與已發布SDK讀回仍待補 |

## 盤點失敗與未完成項目

完整 F0 inventory 的兩次原始失敗均保留：第一次混合大小寫欄名、第二次 aggregate count 回應形狀；兩份摘要 SHA-256 均為 `1C97615A4B561BC9A84C00A2132ED87F5225331A8B9BAFC48AF23726DD8C7680`。此前離線 107/107 只驗證相容案例，並非現場根因修復。依 02A §1，接續先做逐實體／欄位等唯讀診斷；只有重現同型失敗、修正後測試全綠、回復路徑備妥且持有 test-env.lock，才可修正後重試（每操作最多兩次）。上述為原始故障歷史；2026-11-28 已完成方案內診斷、相容修正及 F0 inventory 唯讀重試，完整 plan 修正後重試 1/2 亦已成功，詳末段。不以更名或拆小寫入繞過限制。

三個產品及T01／T02／LIFF交接狀態以共享交接檔為準。本包未呼叫任何Channel、正式組織或金流。複製帶來的舊Channel永不登錄、載入、解密或呼叫。

## 自動閘門與歷史更正

2026-11-26 07:27記錄的是先前helper測試；當時將FS寫為完成並不代表公開CLI或實機完成，已於10:14更正為進行中。當時的Release建置、Deployment1085項、文件1920/0與編碼22/0僅屬歷史基線，不能當作本次補正的最終閘門。

以下保留歷次守門與修正歷史；最終結論以本報告末段的同步後證據為準。依00 §5.10執行「單模型（使用者指定 ClaudeOnly）」；沒有執行雙模型審查，沒有把免雙模型解讀成免單模型。

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

### 2026-11-26 21:10 Asia/Taipei (UTC+08:00) 最終同步後閘門與完整覆蓋結論

- 正常 rebase 至整合 `2da59fac`；58個差異檔僅90依線別合併，其餘57檔bytes不變；同步前完整備份 manifest SHA-256 `8D84CA882C51751678E95B2942437352BF66A70CD4EA3D077EA17D3CC4042F87`。沒有 stash、reset 或丟棄其他工作樹內容。原歷史SHA保留為歷史，不冒充rebase後提交。
- Release 全方案建置 exit0，65個既有 warning／0 error；SHA-256 `28DA2EE5F834D57AEFF738F0F07B3B9C5FBBDEC03400C4AB6291EFAC96DE1ECE`。02§4原排除式完整一般測試 exit0，26回合、8,819/8,819、0失敗／0略過；Deployment1,855、Hosting270、LineWebhook143、Scheduling37、2.0主機175。測試 log SHA-256 `235D5807475401204DD396751B2D795FDFCA614F95A947DD8A904CD9E6EAB163`；全部為離線證據。
- 文件檢查2,014/0，SHA-256 `9F5D26B9D6004E835672CBD135E08B69B7591AA32B8FB58E0F147B2637C57871`；Changed編碼132/0，SHA-256 `6F7511F71BAB2EBEB1ABE65011E3231CEE68B555A261C13F8726E071C907F734`；方案清單exit0，SHA-256 `D52E9542EC15FFB4495879503C5EC8CEA01265867D0349608EAE34C942B37E0C`；進度頁28/28，SHA-256 `235374A08F79CFD72F6E6C9E2C34D72BA55C2C1DACE2915E80B08AD7BD19C417`。報告及進度最後更新後，文件／編碼／頁面另作交付前重驗。
- 單模型（使用者指定 ClaudeOnly）修正複審確認 Plugin 缺旗標 High 結清、已讀生產修改無Critical／High；SHA-256 `877C93DF67B264E800DD1D4515718D010ED0FA3612548266CE3A79D5D939E4D4`。原未逐行的測試另分 quiesce 4檔、UI 9檔全文補查，兩份均無Critical／High且沒有未讀範圍；SHA-256分別 `21F6A141298C570D6D2DA3B0FCBDA15E663D0443512C7E0BB3F556D3DB03C8F6`、`F758DD15E17D9C8D5B8DDAD37F3181ABDF441FDCB837C3C695E6DDC7C125904B`。低於High觀察已寫入 `wp-reports/3P-之後再說-線A.md`，不新增完成定義。
- 01 FS Step1 的24個指定方法全部存在並納入上述 Deployment 一般測試：MultiTestOrganizationTests 的 Catalog三項、Registration_OrgFlag、WriteLock_TwoRepositoryRoots、Quiesce兩項；SchemaLayerTests 的legacy hash、兩產品無教會表、平台多檔延伸、重複entity／attribute及ownership兩項；ViewDefinitionTests 的白名單／未知欄／第二預設／特殊檢視四項；UiSolutionLayerTests 的白名單、D26、未計畫發布、定向PublishXml、漂移、部分失敗六項。原RED及後續公開CLI／故障增補結果保留在前述歷史，不以 helper 單獨通過宣稱實機成功。
- 實機待補：目標 9.1 信箱投遞欄位實值、goodshepherd20 quiesce apply／verify、三組織 plan 摘要審查→apply→verify、檢視表發布、UI export／import 與 Live3P FS。H1 依 02A §2 的核定停用政策處理，成功 apply／verify 後結案，不再要求逐項行為分類。F0 inventory 與 FS plan 的原兩次失敗仍保留；依 02A §1 先做唯讀根因診斷，只有同型 RED→GREEN、受影響測試全綠、回復路徑備妥且持有 test-env.lock 才可修正後重試，每個操作最多兩次。零 D365 寫入，本輪未連 Channel、正式組織或金流。進度頁目視待補，尊重先前使用者 Escape 取消，不自行重啟電腦操作。

### 2026-11-28 01:02 Asia/Taipei (UTC+08:00) quiesce 狀態轉換修正與離線閘門

- Workflow 與 Plugin step 的狀態轉換改用 SetStateRequest；asyncoperation 與 mailbox 欄位更新維持 Update。RED 以 FaultException<OrganizationServiceFault>／0x80045002 注入舊 Workflow Update 路徑，舊基線確實失敗；RED SHA-256 86A9F011E9E4DE00B6FDBFF348B5CB51FC4C430DDB760A45A9412A08602F240C。修正後 quiesce 定向測試 255/255 通過，GREEN SHA-256 F4DDFAAD82D3F8000954CC5DF31B7A15E9FA3565E0360DD61068C3B4E1ECDB58。這是離線合成證據，不是目標 9.1 實機結果。
- 02 §4 離線閘門：Release 全方案建置 0 警告／0 錯誤，SHA-256 408ECC574B85C531488F94C8082A34ECC2046217079742A79C5DF5EB7F0E9701；一般測試 9,472/9,472、0 失敗／0 略過，SHA-256 F062547B5A5A060ACEC7491C7DA9FE2CD7561254D8A47B954AB2E0717A725B24；文件檢查 2,086/0，SHA-256 11C97668DE9964485C4A64B1D0E50220C652BF435F7E1C6F7FBC16555658EBF5；Changed 編碼 5 檔檢查、1 排除、0 問題，SHA-256 443125B65701E4B1E17F6B770F71CE6FA1B8DFBF78622BA226D986C98F232F15；方案清單 exit 0，SHA-256 91B4F1D249BD45625A6A146C3D01E51705E6A0D7AEAF644A763E70B0A514BA2D。
- 本次閘門後仍會更新本報告、90 與進度資料，因此交付前須再跑適用的文件、編碼及進度頁檢查。ClaudeOnly 審查與重基線後的正式守門尚未完成，不能據此宣稱 FS 完成或可合併。
- 截至 2026-11-28 01:02，沒有執行 D365、Channel、金流或手機實機操作。當時 test-env.lock 由線 B 持有（P0-2④）；2026-11-28 01:18 唯讀複查時該鎖已不存在，線 A 本輪仍未操作測試環境。H1 的行為不再依賴逐項推測：依 02A §2 已核定停用政策，待 goodshepherd20 quiesce apply／verify 後結案。

### 單模型（使用者指定 ClaudeOnly）審查與覆蓋補查

- 2026-11-28 01:04 run `20261128-010429-3p-fs-lane-a-reviewer`：runner `ok=true`、`reviewMode=claude-only`；審查輸出 SHA-256 `49576E12C43566BCEBA3F7B9B62CFD2A1FB38A5C945FDA1D18E89FD640A9BFF2`。初輪明確只讀生產 diff，未讀測試與報告；因此另做範圍補查，不把初輪單獨視為全差異審查。
- 2026-11-28 01:05 run `20261128-010544-3p-fs-lane-a-coverage-reviewer`：逐檔閱讀兩個測試及兩份報告差異，並交叉核對生產 diff；runner `ok=true`、`reviewMode=claude-only`，輸出 SHA-256 `650733AAE74BC9763527C1FDCD36C869CFC89896D837D360FD26B274DC9CC5A5`。結論：無未修 Critical／High。非阻擋 Info 僅指出目標 9.1 的實際 Workflow 狀態轉換及取消個案要由後續 apply／verify 確認；此項仍明確列為實機待補。

### 2026-11-28 01:19 Asia/Taipei (UTC+08:00) 續跑與 02A 實機順序確認

- 已核對使用者指定的整合分支本機與 `git ls-remote` 均為 `e2eac3c38587469c9a250362493b2ba3908e102c`；F3 離線修正／報告提交 `5b8c74b03` 是整合 HEAD 的祖先，故 F3 離線閘門已合併，不重做已通過的守門。線 A 的 FS 狀態轉換修正提交為 `2e6de8d0e14bc5a3d86423cc33104ef08db5519c`，其重基線及基線後 02 §4 守門尚待完成。
- 兩輪單模型（使用者指定 ClaudeOnly）審查與覆蓋補查已完成，無未修 Critical／High；本輪尚未執行 D365、Channel、正式組織、金流或手機操作。2026-11-28 01:18 唯讀檢查時 test-env.lock、integration-merge.lock 與三個 D365 寫入鎖均不存在。
- F3 的依賴備註按 02A §5 保持「等 FS 完成後開始」；具體 F3 Step 6 實機仍須在 FS 三組織 plan／apply／verify、檢視表／UI 與 Live3P 通過後才開始。離線通過不等於實機完成。
### 2026-11-28 01:43 Asia/Taipei (UTC+08:00) 最新整合基線後離線閘門

- 為保留原工作樹未提交的其他任務 `.ccg/tasks/line-token-config-separation/.turns.json`，在獨立 detached 暫存工作樹重基線；原檔 SHA-256 前後均為 `84EFB7338518A651C18115AA89C2507C5FF7AFA41821FE8AA3B13D7573173A42`。由整合 `e2eac3c38587469c9a250362493b2ba3908e102c` 重放本包兩個提交，程式提交為 `c4cf53a63`、文件提交為 `1bfe1a23e`；未 stash、reset 或覆寫其他修改。原 `cl/lane-a` 分支指標尚未移動。
- 02 §4 同步後閘門全部 exit 0：Release 全方案建置 65 個既有警告／0 錯誤，log SHA-256 `C0FE0D3AAD51130FAD3F894936A0F548479398F939556533328213907E933757`；完整一般測試 28 回合、9,472/9,472、0 失敗／0 略過，SHA-256 `555EC51DCB8D4EB1C8B989F80ED8D5BB21E2FA228247C85EC3C87870BE442A96`；檔案說明 2,086/0，SHA-256 `11C97668DE9964485C4A64B1D0E50220C652BF435F7E1C6F7FBC16555658EBF5`；Changed 編碼 12 檔、0 問題／0 排除，SHA-256 `33E0E0BF65DE3B613072AB91633448C9358DFB60940D35DD33B255803BE132E1`；方案清單 54 個專案、exit 0，SHA-256 `91B4F1D249BD45625A6A146C3D01E51705E6A0D7AEAF644A763E70B0A514BA2D`。以上全為離線結果。
- 本輪沒有連線或寫入 D365、Channel、正式組織、正式金流，也沒有手機實機驗收。F3 的離線修正已在整合歷史，不重跑已通過閘門；目前仍自動測試通過（實機待補）。
- 2026-11-28 01:55 Asia/Taipei (UTC+08:00) 進度頁離線檢查（最終重驗時間 2026-11-28 02:02 Asia/Taipei (UTC+08:00)；通用 log SHA-256 BAE864611016ABC3005A901BE2B45AA09BFC8387F998ED6AE1161E9079D1DE17；現行 1.3 log SHA-256 3C11FA9BFC7238BECA28CC9AE566CB641D7E9C3D6CAB612751BCF2BA65403C81）：AGENTS 指定的 node --test tools/RepoQuality/Tests/Test-ProgressPage.cjs 是 28/28 歷史 3.5 資料契約檢查；現行 1.3 專用 Test-ThreeProductsProgress.cjs 4/4，含真實兩線快照無警告。依先前使用者 Escape 取消，本輪未重新開啟瀏覽器，故只報 renderer 結果、不宣稱頁面目視通過。
### 2026-11-28 02:04 Asia/Taipei (UTC+08:00) 最終文件與進度 renderer 複核

- 依 AGENTS 指定入口 `node --test tools/RepoQuality/Tests/Test-ProgressPage.cjs` 通過 28/28，log SHA-256 `BAE864611016ABC3005A901BE2B45AA09BFC8387F998ED6AE1161E9079D1DE17`；現行 1.3 `node --test tools/RepoQuality/Tests/Test-ThreeProductsProgress.cjs` 通過 4/4、真實兩線快照無資料警告，log SHA-256 `3C11FA9BFC7238BECA28CC9AE566CB641D7E9C3D6CAB612751BCF2BA65403C81`。這些是離線 renderer 測試，不代表瀏覽器目視或實機驗收。
- 最終文件說明檢查 2,086/0，log SHA-256 `11C97668DE9964485C4A64B1D0E50220C652BF435F7E1C6F7FBC16555658EBF5`；Changed UTF-8／換行檢查 12 檔、0 問題／0 排除，log SHA-256 `33E0E0BF65DE3B613072AB91633448C9358DFB60940D35DD33B255803BE132E1`；`git diff --check` exit 0。四個變更檔逐位元組確認 UTF-8 無 BOM、CRLF、末尾 CRLF。既有 Release 建置與 9,472/9,472 一般測試證據適用於相同程式 HEAD `c4cf53a63`；本次只改報告、90、線 A 進度與決策文件，故不重跑未受影響的程式閘門。
- 現行測試環境鎖由線 B 的 `P0-2-4` 唯讀診斷持有；線 A 沒有接觸或移除該鎖，待鎖釋放後再依 02A §5.1 取得自己的鎖。未進行瀏覽器目視、D365、Channel、正式環境、金流或手機操作。

### 2026-11-28 02:26 Asia/Taipei (UTC+08:00) 02A policyVersion 文件一致性修正與完整離線重驗

- 語意複核發現 `ClonedOrganizationQuiesce.cs` 檔首仍寫「policyVersion=1／缺版」，與核定 02A v2 及 `CurrentPolicyVersion=2`／apply guard 相矛盾。只把該檔首說明改為「policyVersion=2／缺版及舊版於 SDK 前拒絕」；canonical source fingerprint 的獨立內容版本仍為 1，未改任何可執行 token 或停用政策。
- 修正後 02 §4 五道離線閘門全通過：Release 全方案建置 exit 0、65 warnings／0 errors（SHA-256 `CD09758FCFBB463DE3EA9EDE237EB4C19CDC2A23D09D2E449FCBE9F2C4E9B3DB`）；一般測試 28 回合、9,472/9,472、0 fail／0 skip（SHA-256 `001AFA21FCD6C161824C817AF63054E472B5AF89D0C808E3026F7B0912DA198F`）；文件檢查 2,086/0（`11C97668DE9964485C4A64B1D0E50220C652BF435F7E1C6F7FBC16555658EBF5`）；Changed 編碼 12 檔、0 問題／0 排除（`33E0E0BF65DE3B613072AB91633448C9358DFB60940D35DD33B255803BE132E1`）；方案清單 54 個專案（`91B4F1D249BD45625A6A146C3D01E51705E6A0D7AEAF644A763E70B0A514BA2D`）。離線結果不代表 Live3P。
- 此次只修正過時檔案說明，未連線或寫入 D365、Channel、正式環境、金流或手機；線 B 仍持有 `test-env.lock`。瀏覽器目視仍待補，不以 renderer 代替目視。

### 2026-11-28 03:12 Asia/Taipei (UTC+08:00) 最終進度 renderer 與實機鎖狀態核對

- `node --test tools/RepoQuality/Tests/Test-ProgressPage.cjs` 通過 28/28、0 失敗／略過，log SHA-256 `D2225C67CDB91E0EB49AB71AFD3636BAC2FF60D2E729E83D8C6ADF71CD80A8F8`；`node --test tools/RepoQuality/Tests/Test-ThreeProductsProgress.cjs` 通過 4/4、真實兩線快照無資料警告，log SHA-256 `630F048E6F498300032179BDCA9AC1765B5C89AC5EC52E722E63A51426CFE590`。均為離線 renderer 測試，不是瀏覽器目視或Live3P。
- test-env.lock於更新時不存在；線A尚未取鎖（唯讀確認時間 2026-11-28 03:12 Asia/Taipei）；本輪沒有取得鎖，尚未連線或寫入 D365／Channel／金流。依 02A §5，待本包文件合併後才取鎖開始 quiesce。

### 2026-11-28 03:26 Asia/Taipei (UTC+08:00) 最終 02 §4 離線閘門重驗

- 更新進度／報告後再次依 02 §4 執行五道離線閘門：Release 建置 exit 0、65 warnings／0 errors，SHA-256 `16BC627ADF6D9F831E934BA870A8CAB9DB7C4EA51323D180CB4EEA94B64F896F`；一般測試 28 回合、9,472/9,472、0 失敗／略過，SHA-256 `9B7066377DC78B82846AC19CB197FE90F1A3445AE96609C34C147729A60A1841`。
- 文件說明 2,086/0，SHA-256 `11C97668DE9964485C4A64B1D0E50220C652BF435F7E1C6F7FBC16555658EBF5`；Changed 編碼 12 檔、0 問題／0 排除，SHA-256 `33E0E0BF65DE3B613072AB91633448C9358DFB60940D35DD33B255803BE132E1`；方案清單 54 個專案、每個路徑恰列一次，SHA-256 `91B4F1D249BD45625A6A146C3D01E51705E6A0D7AEAF644A763E70B0A514BA2D`。所有結果均為離線證據，不代表 Live3P。
- test-env.lock於此時間不存在；線A尚未取鎖（唯讀核對時間 2026-11-28 03:26 Asia/Taipei）；本輪尚未執行 quiesce 或任何 D365／Channel／金流操作。待FS文件提交並合併後才取得test-env.lock，按02A §5開始goodshepherd20 quiesce。

- 2026-11-28 03:35 Asia/Taipei (UTC+08:00) 最終報告／進度更新後文件說明檢查 2,086/0、Changed 編碼 12 檔／0 問題、git diff --check exit 0；四個變更檔逐位元組驗證 UTF-8 無 BOM、全 CRLF、末尾 CRLF。最終檢查 log SHA-256：文件 11C97668DE9964485C4A64B1D0E50220C652BF435F7E1C6F7FBC16555658EBF5、編碼 33E0E0BF65DE3B613072AB91633448C9358DFB60940D35DD33B255803BE132E1。

### 2026-11-28 08:47 Asia/Taipei (UTC+08:00) goodshepherd20 quiesce 讀回（02A §2.6）

- 依新版政策完成本步各項一次讀回；本次僅唯讀，沒有執行 D365 寫入。待處理流程執行個體 0、需停用 Workflow 候選 0、未分類 Plugin 0。7145 個啟用 Plugin step 中，7106 個屬 `Microsoft.*`，另 39 個符合 ActivityFeeds 保留條件；依 §2 保留規則無須停用，沒有把未知項目當成通過。
- 郵件設定仍有 211 個信箱未確認符合「不投遞」，狀態計數為 `0/0/0:75`、`1/1/0:135`、`2/2/0:1`。既存錯誤資料不足以逐信箱判定原因；依 §2.6 如實記錄個別未完成，不盲目重試，亦不阻擋 FS plan/apply。**不宣稱信箱已全部停用。**
- `goodshepherdback` 基線與本次讀回的候選狀態簽章及檔案 SHA 相同，SHA-256 `227FA949B78311F0A7A32019E65C5D36AED9C63A695A24F56C2513F074B7B91C`。
- 唯讀證據檔 `C:\ProgramData\SpeechMessage\evidence\lane-a-fs-quiesce-readback-20261128-0847.log`，SHA-256 `9D23E42C1807EE2BD8124C9F2D6C073802E9CDA48BF5168A143090832AC9FFA0`。依 §2.6，H1 以停用政策結案；未連正式組織、正式 Channel 或金流，未呼叫複製帶來的舊 Channel。
- 本次程式內容未變；按 02A §10.3 執行文件／進度層檢查：檔案說明 2,086/0（SHA-256 `11C97668DE9964485C4A64B1D0E50220C652BF435F7E1C6F7FBC16555658EBF5`）、Changed 編碼 3 檔／0 問題（SHA-256 `43BE4F22D091EEFE443BFFCDAFF1D78AE23507A6FAEA572582FAECF81376AD14`）、進度頁測試 32/32（SHA-256 `612C8074AA2BE5FA8711FC8BE59282FD93BFA8B8320F3D258849CFA4BE8C1515`）、`git diff --check` exit 0（空輸出 SHA-256 `E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855`）。首次 Changed 編碼檢查指出本報告缺末尾 CRLF，已修正並以後續通過結果為準；失敗證據 SHA-256 `8A5006570BB111F101BC431ADDC1B03B7CE7F18640F976B6C22EBC01F0965198`。
- 沿用最近一次全方案 Release 建置及一般測試基線（2026-11-28 03:26 Asia/Taipei：建置 0 error，SHA-256 `16BC627ADF6D9F831E934BA870A8CAB9DB7C4EA51323D180CB4EEA94B64F896F`；一般測試 9,472/9,472，SHA-256 `9B7066377DC78B82846AC19CB197FE90F1A3445AE96609C34C147729A60A1841`）。本輪無程式變更，未重跑建置與一般測試；合併前依 §10.3 核對程式內容是否需要重跑。

### 2026-11-28 09:49–10:15 Asia/Taipei (UTC+08:00) F0 inventory 與 FS plan 唯讀診斷

- F0 inventory 修正後重試 exit 0，13個aggregate逐表讀回；離線定向測試109/109通過。log及產物SHA-256見`3P-F0-基線矩陣.md`，沒有D365寫入。
- 正式分段診斷（只連三個測試組織）：goodshepherd20 identity／views讀回通過、`SchemaMetadata.Read` 570 entities、`SchemaPlanner.Create` 164 operations、`UiViewDeployment.Plan` 3 changes；log SHA-256 `86D63EE54AC0CD91E04B09F311F1F2566F992C6EB8C0E10EFC9E9DBBC65109FD`。construction及association各為identity／views通過、550 entities、216 operations、3 changes；log SHA-256 `44BB63482755039AC7CB794D4F55F70D7FE607360D5FA6163482687EC73BEFF0`。
- 另一次臨時隔離腳本以production未使用的逐實體`RetrieveEntityRequest`遇16個`FaultException`，並因反射引數錯誤令4個`ReadRows`呼叫出現`TargetParameterCountException`；此腳本未重現正式`SchemaMetadata.Read`路徑，不視為產品根因。正式實作使用`RetrieveAllEntitiesRequest`與已通過的讀取階段。
- 原始完整plan報告只保留例外類型與摘要hash，未找到呼叫位置／stack。依02A §1，不以同型名稱相同但操作不同的診斷代替根因；不重跑完整plan、不執行apply／verify／UI匯入發布。
### 2026-11-28 11:09 Asia/Taipei (UTC+08:00) 進度狀態白名單檢查

- 進度資料首次採用非白名單狀態時 renderer 按契約顯示警告；改為允許的`進行中`並保留pending細節後，進度頁測試32/32通過，未放寬任何檢查。失敗log SHA-256 `4261B9D6DE2A2F960BB6DF5C727292965D270E61506510CD0455530CE097BFAC`；通過log SHA-256 `2BD15F379FF5B5FD6D65A62176CCFB7163A485EA6BF6C66EC4B2771D78833D8B`。


### 2026-11-28 11:50 Asia/Taipei (UTC+08:00) F0修正守門與完整plan歷史追查

- inventory相容修正的完整離線守門通過：Release 65警告／0錯誤（SHA-256 `ABC063097C6CE81E2C7E3FA6203A98244122DF250C482E17134B15CBAEA24AB7`）；一般9,474/9,474、0失敗／略過（`88398C82F794642BCD601609EE5E136655A1C4E9F4139BB40589404E67E9662A`）。其後文件更動沿用2026-11-28 11:31–11:44同程式內容結果；文件／編碼／進度另驗，不放寬篩選。
- 離線合成 `Run_SchemaLifecycle_IsIndependentFromPluginAndReturnsEveryLease` 1/1，SHA-256 `5229D9598A05AE9130CA4780D4EF80D9319FB67544433132D995B95E20A25FA6`；它僅證明CLI生命週期，不是實機plan或歷史錯誤重現。
- 在舊public-contract執行目錄找到兩份真正CLI診斷：Release log SHA-256 `F93F4D91DA8D206196C4E8D9E5F66BB2967FED3BFCB753F89E64D91B4BD8D018`含2026-11-26 12:25–12:27三筆InvalidCastException，安全stack為SDK GetAttributeValue／nullable unbox；FsMetadataLive log SHA-256 `5967946A7A40ADE1F7FA2D13D8A242F33A31FA2012032B8570FEDB00C6A70758`含12:57–12:59三筆FaultException，但安全stack只到ServiceChannel，未保存Dataverse error code／訊息。前文「未找到stack」更正為「找到有界stack，但仍無法定位第二次根因」。第一型歷史對話指向systemform.formactivationstate的Picklist誤讀int?，現已使用OptionSetValue；第二型仍不得由重複排序假設直接判為已修。
- 已讀整合064122f56的00／02A全文及§10.4：後續實機診斷僅用方案內已測試命令／唯讀Live3P，不再用PowerShell臨時載SDK。未重試完整plan、未apply；D365鎖已釋放。

### 2026-11-28 12:18 Asia/Taipei (UTC+08:00) 方案內唯讀對照與第一次修正後完整plan重試前置

- 新增測試專屬最小Probe與`Category=Live3P, WP=FSQueryDiagnostics`；不走完整plan、不寫D365。離線輸出／型別／取消／ID／回應上限10案先紅後綠（RED SHA-256 `33CF56F7BB26EE60464582189D80601F48C02493B847F695CEF845BB75F3459E`，GREEN `4F1571292104802C602FFB1A990533F9F0B6049A5CE4844E174E866E2B5A6D8B`）。
- 首次診斷3案失敗，原因為Probe誤以為systemform只回一欄；真正SDK自動帶objecttypecode／type。失敗SHA-256 `0784406821F87691E80080281F99661B089C3B6976FB3D0133CC1A7E8C43295D`，最小形狀證據 `D43AB7D8F682956D5E79F997C71B89A57534E4BA4D666778B7A9150D7029CCFA`。先以同形狀離線案例重現1 fail／16 pass（`848E2D07225C23062C7FC825AD3CCCB821381E56C80EC096667EC1C5E1384D40`），再只接受三欄成對且精確account與OptionSetValue，正式ID仍必須一致；受影響89/89通過（`692EC473967B2D2B1C1DB5C5F4D0792C1C2AC8445514409B44CD59AAB21876C6`）。不是環境限制，未放寬正式reader或測試守門。
- 修正後三個測試組織對照3/3：兩種UI表的單排序全部成功，雙排序均FaultException，碼鏈80044150→80044150→80040216；SHA-256 `E592FAAAC3111C5A98541F109744E1C17E857F4F0C4970A5444CC12F6CDFEDC7`。只查account範圍UI主鍵一列；無XML／業務值／Channel讀取，D365鎖已立即釋放。
- 舊FsMetadataLive DLL的ReadRows確實無條件追加同一主鍵排序；離線合成SDK對同形狀拋FaultException，正式`ReadPublicationScope`單排序通過。現行排序／Picklist修正均已在`64679a87f`。這證明可重現的相容性缺陷，**不宣稱已還原缺少SDK碼的舊完整plan fault detail**。
- 依02A §1規劃第一次「修正後完整plan重試」：①同型FaultException已離線重現，且三組織小查詢差分一致；②既有修正下受影響89/89已綠；③完整plan只寫本機唯一artifact，D365零寫入，失敗保留證據、不清理舊檔，任何後續apply須另備before及審查；④執行前取得test-env-d365.lock。本次只plan，未授權直接apply；每個組織記為修正後重試1/2，不混入舊原樣兩次。

### 2026-11-28 12:42 Asia/Taipei (UTC+08:00) 完整 plan 修正後重試 1/2 結果

- 依 02A §1 四項前置、使用既有修正 `64679a87f`，三組織依序完整 plan 全部 exit 0。持鎖只執行 plan，沒有 D365 寫入；最後已由 owner finally 釋放 `test-env-d365.lock`。以下為真正 CLI 結果，不是分段診斷。

| 測試組織 | schema operations／UI changes | 完整 plan log SHA-256 | schema-plan.json bytes SHA-256 |
|---|---:|---|---|
| goodshepherd20 | 164／3 | `8AEAADEA88B7555912A4F4B8858312C62967FED61A09F7FDC66630DA215C55E5` | `A805365AC62E7D744AA1F5D9EB8EE5B32F9B655756B1B6EA0F651043A20D851B` |
| construction | 216／3 | `57856D117B69B0A4879E07A13E744FE958BE9E00EDD667ED58AD03C0954743A3` | `1EFDF4AF24B4BE82364710A4B9B464A3126DAE356607011E465A8034057B738C` |
| association | 216／3 | `57856D117B69B0A4879E07A13E744FE958BE9E00EDD667ED58AD03C0954743A3` | `4D170BF3262B47DACC25A31BBC71CD799B2FC31B333DBB5E99F5750902F586AE` |

- 產物均在本輪工作樹 `artifacts/d365/<org>/<唯一operation>/`，保留原始檔；兩份空白組織的固定輸出 log bytes 相同而 plan 組織身分／hash 不同，未共用計畫。
- 每組織尚有第二次「修正後重試」額度，但本次已成功，不能為了重跑而浪費；後續 apply 是另一個操作，須三份 plan 合併審查、回復證據及新取得 D365 資源鎖。
- 先匯出三組織 metadata 作附加 before 證據；metadata 不是資料庫備份，不宣稱可單憑它還原資料庫。正式 apply 引擎仍須核對計畫指紋、未發布變更、完整 publication scope，並在首寫前 flush 自己的 before-metadata／before-ui-plan；不跳過或放寬任何守門。

### 2026-11-28 12:55 Asia/Taipei (UTC+08:00) 本輪合併前完整守門與三份 plan 合併審查

- Release exit 0：65 個既有 warning／0 error，log SHA-256 `E7A68E0E891A05B8378015785D773D1BE43F4909779055A59875E74C2AE40BB6`；完整一般測試 **9,491/9,491、0 失敗／略過**，log SHA-256 `7D776DA2A5D718FA052EF8308380967E136E4E3E724886109E02DA89AFF82D48`，包含 Deployment 2,009 項。篩選依 02 §4 原文；Live3P 未混入一般測試。
- 文件 2,089／0，SHA-256 `7265A5B8C026710F67ED3FA3219C33848772EBAE387AA2C4C13B4BB1D6A432B6`；Changed 編碼 20 檔／0 問題（另 1 既有排除），SHA-256 `57457CE2AF2C7A50F8848B66F0733FDB8998A8E43D5326C773DB6976AA8A947B`；54 個方案專案且恰好各一次，清單 SHA-256 `91B4F1D249BD45625A6A146C3D01E51705E6A0D7AEAF644A763E70B0A514BA2D`；進度 32/32，SHA-256 `193D6D7690A4FECF95FD020CDBA968769DFEC40AF1EF2B1ECC8BA52CA79A9DD6`。三個新增 C# 檔另逐位元組驗 UTF-8 無 BOM、純 CRLF、末尾 CRLF；diff check 通過。
- **單模型（使用者指定 ClaudeOnly）**：新診斷及三份完整 plan 一起審查，無 Critical／High；產出 SHA-256 `B1028DA9F8CA39B644FAFA4AC7A2776D2CCABD98372EC8861474F53E56A9A6E1`，summary SHA-256 `E6712773A96628AA821B74B54AFF2ACAAF1E8792D7E80AF915885521F2548199`；reviewMode=claude-only、dualModelComplete=false、degradedFallback=false，不冒稱雙模型。
- 審查確認三份計畫各自組織身分／hash、不含 Delete／Update、UI 僅平台公用檢視；construction／association 無四張教會表。goodshepherd20 兩個 retained owner 僅在精確組織 ID 及正式 metadata ID 白名單沿用，其他新租戶表仍 UserOwned。此審查未宣稱已做 apply／發布；apply 路徑仍使用既有已測試的完整 guard，不放寬來源／指紋／未知變更拒絕。
- 同步前發現整合新增純規則 `01ea6a69f`（02A §11 事先核准保全自己的未提交檔），已讀增量。以本輪提交為檢查點後乾淨 rebase；因僅文件差異，依 02A §10.3 沿用本次相同程式內容的完整建置／一般測試，不重跑。
- 回復準備：以既有部署 User Secrets 的授權身分透過 WinRM 先確認 D365APP01 及三個精確測試資料庫，唯讀確認成功；本機未提高權限／未更改SQL權限。準備獨立 COPY_ONLY／CHECKSUM 備份與 VERIFYONLY；尚未完成前不宣稱已有資料庫回復證據。無正式組織／Channel／金流連線。

### 2026-11-28 13:16 Asia/Taipei (UTC+08:00) 三個測試資料庫 before 備份與 apply 前回復路徑

- 已用既有部署授權身分透過 WinRM 核對主機 D365APP01，並逐個以正式 OrganizationId 比對精確測試 DB 的 OrganizationBase，全部一致後才備份。只對三個指定測試 DB 執行 **COPY_ONLY／CHECKSUM 全備份及 RESTORE VERIFYONLY WITH CHECKSUM**，各 exit 0；未變更 SQL 執行個體設定、權限或服務，未執行 RESTORE DATABASE。D365 鎖已在 finally 放開；SQL／其他資料庫與正式組織未操作。備份日誌 SHA-256 `F7A7B9B6D0E1504CBE40CC4865E67A7EC221C843477445E2C78B93FA471255C0`。

| 測試組織 | D365APP01 上的完整備份路徑 | bytes | SHA-256 |
|---|---|---:|---|
| goodshepherd20 | `D:\SQLDir\Backups\3P-FS-goodshepherd20-20261128-125953.bak` | 4451401728 | `0216633009F4330261ECA18A9BDBCF71A47B83CAD1DCAAF9C167D0318AA67372` |
| construction | `D:\SQLDir\Backups\3P-FS-construction-20261128-125953.bak` | 2399318016 | `23BCD8728306C0029205BD541C0E5D2E704288D169E96D2F9BE99119DA7586F3` |
| association | `D:\SQLDir\Backups\3P-FS-association-20261128-125953.bak` | 2397220864 | `F1F039EF189DC8563760ADEDB16E1B8D233E4875C21CDC699CF9F62858EC9CB4` |

- `VERIFYONLY` 證明備份組可讀且 checksum 通過，**不是實際還原演練或資料／應用完整性驗收**。若後續故障：先停止該組織本次部署，不再原樣重試；保留原 plan、首寫前 before-metadata／before-ui-plan、目前差異及備份。新增結構不擅自刪除、既有資料不覆蓋。必要還原僅在同一個明確測試 DB 的停用維護範圍，以對應原備份還原後再經 D365 核身及 verify；不得把整台 VM 或別的 DB 回復到舊狀態，也不得自動發送 Channel／金流。
- 三組織另有唯讀完整 metadata 匯出：goodshepherd20 SHA-256 `4140427C63C805831967491962C427FE75123F5936EB771DC8A8E1C2F557F913`、construction `C9E8A7E3518B5780921934DD20F98FF5A9B2C49003819CC2BC12DE9438F13817`、association `11FF6135A124374C7E12F1269ABE163890CFB365E9B7E50992C10D1DB8B05A37`；只作結構證據，不當資料庫備份。
- goodshepherdback 真正唯讀 `plan --org goodshepherdback --schema speechmessagecore.schema.json` exit 0、operations=0、pendingKeys=0；log SHA-256 `4C3F299F9B7BD5872FA447AB9616D42EDE2453F5820D5201AB01C6ADD979C8D7`，plan bytes SHA-256 `99A85FD729294417747C8D9E3C7BB94DF4DC2DD31AD58B617D19D3BE354B52FB`。沒有寫入該組織。
- 本輪診斷及三份 plan 審查已快轉合併推送 `d8fc8c22ea6fb8cc23ee4f190e101fd446677608`，遠端 SHA 等於整合 HEAD；保留整合樹他人的 `.ccg` 修改。重基線只有規則文件變更，文件 2089/0、編碼 23/0、進度 32/32 重驗通過，沿用同程式內容的 9491/9491 建置／一般測試結果。
- 以上前置齊全；接著重新取得 `test-env-d365.lock`，按已審查檔案 SHA 精確綁定三份 plan，依序各 apply→verify。任何失敗立即停止本步並保留證據，不把 guard 失敗當作環境限制。

### 2026-11-28 13:26 Asia/Taipei (UTC+08:00) 首次 schema apply 由計畫外未發布 UI 守門拒絕

- goodshepherd20 第一次 apply exit 1：`InvalidDataException`；log SHA-256 `A06278CCCFAF9E5590D126EF6A3D9B6B0DEADE5D8AF34D221E48E14F1C25AC4E`，共用安全 Exception.log SHA-256 `B0CA12945C26952C84C8AC2F9CA3A388E879289F9FC1E3A361002D57B0AA7B7F`。stack 明確為 `UiPublishScope.Check:51 → UiViewDeployment.Validate:109 → SchemaDeploymentEngine.Apply:149`。
- 停止於首寫前計畫外 UI 差異檢查；本次唯一 operation 無 before-metadata／before-ui-plan／after 產物，而正式 before 寫入及首個 Create 在此檢查之後。因此本次沒有 schema/UI 寫入、沒有 Publish。construction／association apply 未開始；owner finally 已釋放 D365 鎖。這是 guard 真的拒絕，**不是環境限制，不放寬檢查，也不原樣重試**。
- 計畫指紋重算已過而 pending 未過，接著用方案內新 `WP=FSPendingDiagnostics` 唯讀測試觀察同一 schema 範圍的已／未發布正式元件。投影只輸出正式管理 ID、白名單型別／實體及固定差異欄位名稱，不貼名稱、XML 或業務值；結果非零仍只表示診斷成功，不是 FS 驗收。
- 診斷投影離線先 RED 6/6（SHA-256 `9DD4112970A4BE9B9ECD8E4BE5F7625B9A70A48CDAAAE4866D483D57857DCB70`）再 GREEN；受影響診斷／pending 回歸 55/55，SHA-256 `ABEDA3507F7F582BF9B39FC2953DDECC73EE5341C21F5631C085D1EF9F0952B4`。此新測試程式尚待下一次合併前全方案守門，不能沿用舊 9491/9491 冒充新程式已完整驗證。

## 下一步

先完成新發現的連線池追蹤守門修復、完整測試與補審，再整合推送。之後重新產生三份完整 plan、一起受審，持 D365 鎖進行修正後 apply；三組織 verify、平台已發布 SDK 讀回、UI export/import 與正式 FS Live3P 依序補齊，F3 等 FS 完成。

### 2026-11-28 14:17 Asia/Taipei (UTC+08:00) 原始表單來源修正（尚未重試 apply）

- 正式 formid `6ddf2ea1-0406-46ec-9030-ffff6cfbc3ab` 的唯讀 SQL 只見 ComponentState=0 一筆，沒有 unpublished row；一般兩來源卻因語系投影出現 52／56 節點差異。SQL 診斷 SHA-256 `5C922CDA89A6128AE1E0A08A4F37846496CD55FF0F4E2A2F58BF925A0BE27054`。
- 兩來源同一 FetchXML alias 的 XML 原文完全相同，UTF-16LE 3,710 bytes、SHA-256 `EC754FF58508C2997701773B2B6CD068E19B06922DA893EF3C6C30E6F515C660`，精確等於 SQL 原始欄。alias 證據 log SHA-256 `5FB2144DB2150669A23038773D1BC34E2ECA7B34314A8052A338F55D2C9D7995`。未忽略 label、未正規化 XML、未擴大核准元件清單。
- 先以兩個真 reader 重現 InvalidDataException，再改委派共用 UiFormXmlReader。初始 RED 10 fail／1 pass，SHA-256 `15DA0BE9150BF52EAD4BE8A19E573ACFEE3B8747C7FAC35E75425B33AD26E2D0`；第一輪 GREEN 11/11，SHA-256 `B3BA84C591A32DFB30ED9FB3CE7F230654AABA4726EB6EFEA550B191EFF65FB5`。
- 實機發現 QueryExpression 的 EntityName 字串不能直接當 FetchXML 的整數 objecttypecode：fault `0x80040203`，固定分類為 Int32／format／convert／type。失敗 log SHA-256 `32639257561ABEBD758778D87513D4F910B9FA5124FD599C18594A82A7896C9B`。改由同組織官方 QueryExpressionToFetchXml 唯讀轉換，已實機證實 numericScope=True（SHA-256 `C88DF3341B31889E1EB70C58840C3E36BE7A1E809BE81AEBCA0E88603743770E`）；沒有猜 ObjectTypeCode 或跨組織快取。
- 數字範圍 RED 2/2，SHA-256 `C8258CA5A100FE78A99C50A663AA9B75BBC58B1E8F945270C40D9C183B3DF170`；修正後 reader／安全診斷 44/44，SHA-256 `060FF6CD15AB01AE29E726E07ED0AC688B22A7C091CD681169488CB5E31382FD`。之後新增轉換故障與取消保護，最終閘門另列，不沿用這個中途數字冒稱最終通過。
- 正式 reader 的單表唯讀實機診斷：三個表單原文一致，pending=0，SHA-256 `ECA4A76C4ABE96795C304C3000BBE04E266CDDB67FA85DE8B1CE089016835652`。這是 WP=FSPendingDiagnostics，不是 WP=FS 驗收，也沒有 schema/UI 寫入或 Publish。
- 固定欄位、scope、唯一排序與 cookie 保真；每頁轉換 XML ≤64KiB、頁面≤256列、≤16頁、cookie≤16KiB、表單單欄≤1MiB／頁面≤16MiB，原跨頁／兩來源完整預算仍照舊。錯 alias／身份／編碼／缺欄一律拒絕整頁；選項與字典為本次候選獨立複本。SDK 及其釋放仍由外層 lease owner 負責。
- 三份 DB 備份與 VERIFYONLY 證據仍保留；未執行資料庫還原演練。合併新 reader 後須重新產生三份完整 plan、合併受審，取得 D365 鎖及最新整合基線後才進行 apply 的修正後重試；本輪尚未使用該重試額度。

### 2026-11-28 14:55 Asia/Taipei (UTC+08:00) 原始 reader 完整覆蓋與合併前新守門失敗

- 同步 `72b401258` 的程式版本 `60c6f5f96`：Release 建置 65 個既有警告／0 錯誤；一般測試 9,565/9,565、28 組件、0 失敗／0 略過。建置 log SHA-256 `EAB5F69615E4FBAC6998EACACFA0BA8F4DEB4E611FE281D18894C4B3077D72C5`；測試 log SHA-256 `3DB67AA02CE8E7D117BCDC44F4621B9ADF015B2049BDF45D16C24058AE4F7FF9`。
- 三個測試組織完整 schema 範圍，以正式原始 reader 做唯讀診斷，pending 均為 0、3/3 通過，log SHA-256 `8168F25B2A063DB87D51C462B51C62812AE56E6FA169150625607761592F9DAB`。這是 `WP=FSPendingDiagnostics`，不是正式 `WP=FS` 驗收。未重新 plan、未使用修正後 apply 重試額度、未做 schema/UI 寫入或 Publish。
- 單模型（使用者指定 ClaudeOnly）：第一輪完整 helper、兩個正式呼叫端、adapter 與 schema 契約無 Critical／High；審查輸出 SHA-256 `238F845CB9C5624E3D23A26BFDCA3B5356B9F242C569D0C0D79291963D3474FF`。第一輪明列未涵蓋部分，再送補充審查，剩餘 16 檔完整差異全部覆蓋，無 Critical／High；輸出 SHA-256 `8A6A4A6F9A0604F94663A520709871F9282D335A63A80228BC1974555CFC0ED5`。合計 21 檔已覆蓋，沒有將抽樣稱全包完成。
- 取得本線合併鎖並同步整合 `d4f576d10df81dc55d38589942246c83205051b0` 後，HEAD `431ca2e147ef555ec683159bdcc66d099d1513fc`；原 21 檔 bytes 全部未變。因同步帶入線 B 新程式，依 02A §10.3 重跑完整守門：建置 65 警告／0 錯誤（SHA-256 `26081A4D66B9748E46D10FF6C13A6DD2CD5BD47AF9EDD9197EB0A91A38B1E2CC`）；一般 9,564 通過／1 失敗、0 略過、28 組件（SHA-256 `5D3D0B74E93399715645162E5D35F51CF5732E8AD94C542F370DE9FF94F6F6BC`）。Hosting 347/347 正常完成，未中斷或放寬。
- 唯一失敗為 `DataverseTraceTests.End_to_end_trace_proves_pool_lease_isolation_and_fault_eviction`。`BoundedClientPool.Return` 在 `pool.return` 事件前就將健康 client 放回 idle queue；仍有名額的並行借用者可先讀到 client，讓追蹤順序出現兩段租期重疊。這是必修守門，不能當環境限制或刪除斷言。線 A 正補正歸還與重新發布順序，尚未放行整合或實機寫入。

### 2026-11-28 15:11 Asia/Taipei (UTC+08:00) 連線池必要修復與受影響驗證

- `BoundedClientPool.Return` 先清理身分並提交 `pool.return`，再以既有子池短鎖重新發布健康 client；Trace 不在鎖內，shutdown 不再把 client 放回無主 queue，finally 保留發布／淘汰及 permit 清理責任。無新公共 API、跨租戶共享或全域鎖。
- 原端對端租期不重疊斷言完全保留，補每 lease 唯一配對、二十次借用／十九次健康歸還／一次 fault、零 dropped，以及所有 client／子池排空。三十二輪有界壓力回歸在修前真 RED（87/115 → 112/127），log SHA-256 `7C2C3EBE8D96F4BCED356BDB70F143A8ABEED0C9D197029D4818289D2E1DD431`；修後受影響專案 80/80，TRX SHA-256 `ACC3D2364BE0F0E2A616D43DF8B9CF9FB0DDC4CA4721FB00BD1CE24073CF6D78`。壓力測試不是決定性排程證明。
- 主線僅採核定 ClaudeOnly，補審待完整守門後執行。受派代理自行誤觸 CCG 雙模型入口，Gemini 回遠端 503；該次不是完整雙模型，也不作核定閘門依據，後續不重試 Gemini。此流程偏差不影響上述本機測試證據，亦不允許省掉主線單模型審查。
- 新修復程式已凍結 SHA-256：BoundedClientPool `11CDAB701FD7539C960EA0988BFD2C98611019ADC7FDD57EC55C2CEDEFEE322D`，DataverseTraceTests `8D5360BEA4C33EC7A023B7650DA52BFF07255DD21F3DFDB1D5980BA7751D162F`。完整方案守門執行中，尚不宣稱全部通過。

### 2026-11-28 15:21 Asia/Taipei (UTC+08:00) 修後完整守門與審查可用性

- 修後 Release 全方案增量建置 0 警告／0 錯誤，SHA-256 `F9B6634C360F35C1DEE3B7D9EB7EB9F2CBF76CFB5DEBC94A78C9062E6F309643`；完整一般 9,566/9,566、28 組件、0 失敗／0 略過，SHA-256 `70242CB6ECC55C2C1602F550B1B8B5549C08F1684FB3E35C825B70D64B0530AC`，Hosting 347/347。這次才是 pool 新修正的完整通過結果；原 9,564/1 保留為故障歷史。
- 文件 2,099/0（SHA-256 `6B34DD6366A6A818D765CBFC00B2E0C91214FEC60BB751DCAC5EC66D280841EB`）、Changed 編碼 53/0／排除1（`C977B2AB1D86BFEE8ED90146551A6C2DB60AB09EDF59AE7E1C1F93509E401EEF`）、進度32/32（`E70A634C556CDEEED2B12DB3083AB17E470B32EEBAB9A1C04DEA2A77CFAB1DB1`）、方案54項且未增減。此後只有文件變更，依02A §10.3沿用本次程式相同的建置及一般測試，不重跑。
- 主線單模型（使用者指定 ClaudeOnly）補審入口 exit3，`quotaBlocked=true`，內部兩次均回 weekly limit，沒有可用輸出，**本次未取得外部審查**。runner log SHA-256 `AB47F7617D28BC120A28A1FE2CF787BAF3D649A742AB0F9B37533AA0F3C2139F`；不第三次、不切 Gemini。依02 §4入口失敗規則做永久隔離／資源護欄自審，未發現未修Critical／High；本地完整清單 SHA-256 `B012C5E394D1D5B6E39608777EF499B2C5597D5955182CB3E12C6DBA91E52419`。02指定的同名跨使用者審查指南在本樹不存在，沒有假稱已讀，而以AGENTS永久規則及00 §2.4逐項核對。終點整體審查仍須涵蓋新pool修復與F0補充文件。

### 2026-11-28 15:34 Asia/Taipei (UTC+08:00) 三份新 plan 共同核對與 apply 修正後重試前置

- `goodshepherd20` 新plan `artifacts/d365/goodshepherd20/20261128-072632-5028737-fabc0357ba2f470ba5b69c533f28e2c8/schema-plan.json`：schema operations=164、UI changes=3，檔案SHA-256 `540DBA9875777D7BE17A2B5E3396377A1B5DA5D5A88B55F5074C87BA00E47646`。
- `construction` 新plan `artifacts/d365/construction/20261128-072756-1972210-576202e5b9ab4bf0b83ad747da646d21/schema-plan.json`：schema operations=216、UI changes=3，檔案SHA-256 `756A4AF6D2495B2247F4C5DD73AA7632384E3821B637CAE396A0C9A3A239A9EB`。
- `association` 新plan `artifacts/d365/association/20261128-072908-8947917-ba6e1f90f5124288b05939dc65350b7c/schema-plan.json`：schema operations=216、UI changes=3，檔案SHA-256 `9158D7298B84E2C418C8869100185FD43DF37D710BABB3AACE33E6549E8AD17D`。
- 三份與前次已受審plan逐值完整差分，僅 `createdUtc`、`sha256`、`ui.fingerprint` 變動；所有schema operations、organizationId、schemaHash、retainedOwnership及UI定義／正式ID／目標不變。先前舊指紋不再使用。摘要SHA-256 `E1DAFCA67D5E46B9D2D9229333E0C2CB7B1DB055C4D89858B77884004F967A2E`。本機交叉檢查首次因漏列允許的CreatePublisher/CreateSolution而拒絕，查明這兩類與舊plan完全相同、均純新增後修正本機摘要清單；未更動產品守門或plan。
- 三份一起送單模型（使用者指定ClaudeOnly）入口，內部兩次仍quota無輸出，**新plan未取得外部審查**，不第三試；runner SHA-256 `89AE5937F6EB310021AE544A2029F13A7E82CB2A9EEF7C41ADCA1BFCDBD8337E`。依02 §4入口失敗規則共同本地補審（含全部新entity ownership、精確保留ID、產品隔離及UI定向發布），無未修Critical／High，證據SHA-256 `3C7DE55BD5D239544684630C55C88EDC3F98CA8ECD49DFCA4FE0B5B1F0BBD0BC`，終點需涵蓋；不是把quota稱審查成功。
- 已驗真遠端主機D365APP01，再次讀回三個12:59 before DB備份SHA全相同；唯讀回核log SHA-256 `9B7E6B35CB8DA442536ADB3AC29CB05FDD43B76ADB4DCE2B76BD99DDA05B6A94`，PSSession finally清理。原COPY_ONLY/CHECKSUM及VERIFYONLY通過，沒有還原演練。本輪不重做備份、不改SQL服務或正式資料庫。
- 回復路徑：失敗先停、保留同operation的before/after與完整差分；若真的需要DB還原，只能另核對同一測試DB/backup及目前組織身份，同持D365→SQL鎖停止相依寫入後規劃該單DB RESTORE，不自動還原整台VM或其他組織。未確認部分套用時不原樣重試。接著將以這三份新plan做goodshepherd20修正後apply重試1/2，construction／association為首次apply；尚未執行。

### 2026-11-28 15:50 Asia/Taipei (UTC+08:00) 修正後 apply 1/2 失敗與部分套用唯讀確認

- goodshepherd20 operation `20261128-073637-2986119-a59114a6100c407182ebe7775585225a`，apply exit1、`FaultException`1`／0x80040216；log SHA-256 `CAE82FAA240235862CD9F12A79B8E37EA355C110D6BC05E5D5DA5C6E0A8706A5`。共用安全Exception.log記下同事件真實型別與stack，SHA-256 `3FD42EB1116DBAD0550FC0540E0A86994A133E1BA2B601B282D74BBA7F6971C5`。原始stack截於OnPremiseClient.Create，不能直接假稱全部寫入失敗。
- owner finally已釋放D365鎖，後兩組織apply未開始。這次已使用goodshepherd20修正後apply1/2；不原樣重試、不分拆寫入繞過、不先做F3。
- `before-metadata.json` SHA-256 `4140427C63C805831967491962C427FE75123F5936EB771DC8A8E1C2F557F913`（與原metadata一致）、`before-ui-plan.json` SHA-256 `2B50FDE82D005CD7EC735A8948564ADB64D8826C7FEB55D9EFCC744C067D4606`已持久保存，沒有after自動完成證據。
- 失敗後用方案既有唯讀export-metadata及plan（診斷不算重試）成功：新snapshot `20261128-074535-0813683-7b427a0e08c3458d80c5f6b70be893d0` SHA-256 `DD245E69D6977209EDD1F89588D203B26146373628DDB0BAE3FB400465718B64`；實體570→577、欄位22837→23133，新增七張new_auditentry／channelslot／role／roleassignment／rolepermission／slotallocationlock／slotrequest。**已部分schema寫入，不能繼續寫零寫入。**
- 新唯讀診斷plan `20261128-074646-9228054-d7af682e894642879c48e5f9eab1e2dd` SHA-256 `EBC92294A7B20FB08875AAAA26F54C4D5C52622C0C9B8F7887D6E83131E59ECD`，schema operations0、pendingKeys0、UI changes2；UI方案已有正式ID，目標view ID仍空、needsWrite=true。與程式Create階段對照，疑似savedquery Create故障，仍須唯讀診斷確認根因，不冒稱已定位。export及plan log SHA-256分別`1BAF9E8CD3E0C5F825E4C84CB4E51731C3D6A618130CE4A66655DF318452F85E`／`4C3F299F9B7BD5872FA447AB9616D42EDE2453F5820D5201AB01C6ADD979C8D7`。
- VM原生Application事件只投影型別／錯碼／固定關鍵字，無任意message：SHA-256 `903B77E099E6C70AAE73607A3210DA8F5CBC9FC4AD2FFB03F86FC61F52A03DC7`；目前只見既有監控SQL例外，沒有本次savedquery的可歸因根因，不把它當修復方向。接續增加方案內只讀ValidateSavedQuery診斷，先離線測試再連測試組織。

### 2026-11-28 16:16 Asia/Taipei (UTC+08:00) 執行期版面根因與已核定退路

- 以 WinRM 再驗 D365APP01，唯讀複製已安裝供應商二進位至 repo 外 evidence；沒有載入／執行供應商 DLL，沒有啟用全機 tracing，PSSession finally 清理。Microsoft.Crm.ObjectModel.dll SHA-256 `5D5A884CF8854FD11020870DA59E1FE30A0148C60ADFD57A050A22CA0FBA94C4`；Microsoft.Crm.Core.dll `821EA346569A4CDD7017607033E3A2CA8ED9AE63EC4EF26F74F1BFFA00128665`。
- 已安裝 ildasm 的靜態輸出證實 SavedQueryService.Create 先呼叫 ValidateSavedQuery，再呼叫 LayoutXmlValidator.ValidateLayoutXml；後者內嵌 runtime XSD 明列 grid icon／jump 必填，目前產生器正好缺這兩項。callback 拋 CrmException(string)，其已安裝實作固定錯碼 0x80040216，與實際 apply 相同；不是根據一般匯出 XSD 的 optional 猜測。抽出原始 runtime XSD SHA-256 `9481B46BC20C12693911FAB607257A035FBD3092CA6EBD8A51CE3A1018ECF15A`。
- 真正取得 Microsoft Learn 官方 Create 範例（含 jump／icon）與匯出 XSD 作交叉參考；HTML SHA-256 分別 `F9F6BFF15DBBD4085B03E1EE56E65ACBF7C0C28432A1135447892B9FCC03BE8B`／`8DB5699F4311B695FF85D6E70851664279E26DBD0F253781C9AA79FDAD7D5896`。web 工具回空不能算取到頁面，以上是實際 HTTPS 200 保存的官方內容；runtime 與匯出契約不可混用。
- 獨立合成 runtime 子集先在真產生器重現同型 FaultException<OrganizationServiceFault>：1失敗／6通過，RED log SHA-256 `A5933953672A0EB2722D76A2A6930998BBFCF0C42130272F98E2DFDD27FC8997`；僅新增 icon=1 與 jump=第一個已核准顯示欄位後，受影響生成器／公開CLI／診斷116/116，GREEN SHA-256 `41C124498C6865ACA48E484336D3A68F5DDF6AF7398AEE7E7B405595DA0A4AE1`。合成 fault不是新的實機 Create 證據；沒有改Fetch、scope、pending守門或寫入額度。
- 新方案內唯讀診斷已實機驗證正式與最小 Fetch 皆成功；整案仍因額外版面讀取錯把「末頁仍帶 cookie」當續頁而失敗，不能稱整案通過。安全布林細分證實 page-cookie=True、page-more=False、page-overflow=False，log SHA-256 `35F19FEB0AAB0AC67E6FF8D46F345DD8F47DF7B571022E40A5F91F2746AAEEF3`。只會修正末頁cookie協定並維持16KiB UTF-8上限、單頁16列、身份／scope／重複拒絕，修前新測試已RED；不是把失敗當環境限制或略過診斷。
- 已立即從整合工作樹讀取使用者新核定02A §12。剩餘一次修正後apply若成功照原驗收；若只在畫面失敗且結構讀回一致，才重產無畫面新plan、持鎖備份做三組織結構plan/apply/verify及結構Live3P，以完成（縮減）並將畫面延G1第一週。現階段尚未觸發，不先刪除UI方案、不擅自標完成、不先F3。
- 子代理所交付唯讀診斷3檔已凍結及關閉；代理再次自行誤觸雙模型（Gemini403），不是原使用者指定ClaudeOnly核定閘門，也不把那次稱成功。後續主線遵守當下較高層作業要求並如實記錄實際模式與結果，外審尚待最終程式守門。

### 2026-11-28 16:21 Asia/Taipei (UTC+08:00) 唯讀診斷修正與實機排除對照

- 末頁cookie原協定誤判已同型RED→GREEN：保留MoreRecords=false、單頁16列、完整身份／scope／投影／重複拒絕。cookie先限字元再計UTF-8，最多16KiB、不重送、不輸出；另驗MoreRecords=true且小cookie仍拒絕。RED log SHA-256 `5DFD59120C40D1C12723B412ECB20CFECB53ABB357D70E6DDDBB394EBD3B1CE6`；最終相關119/119，SHA-256 `6D7447905A8FFF9762912AB5DB54FE2EC19280E75C34E52B66FD8ADC72A2A74A`。
- 方案內固定goodshepherd20唯讀診斷最終1/1，正式Fetch／含主鍵最小Fetch均Validate成功，既有同表兩個公用view版面都具icon／jump；log SHA-256 `45832E3DE90E17567D660ED0656F51573017D95B58D59DBBA7EBA1B43DE37005`。這不驗證Layout Create，不冒充已建立、已發布或正式FS通過。
- 新增runtime測試檔說明標題未符合既有機械契約，文件守門先報1失敗；只將資源段標明「資源所有權與隔離」後重驗，不更動檢查器或排除清單。上一輪一般9606/9606僅舊程式中途結果，不能沿用給後來新增的末頁cookie程式；最終凍結五檔後新全方案守門執行中。
- 主線外部分析入口本輪同時請求兩端，Claude有可用輸出、無Critical／High，但只讀生成器diff及Probe，不冒稱涵蓋測試／Live owner；Gemini兩次遠端503，未完成雙模型、不是quota降級成功。最終安全審查仍待完整守門後覆蓋全部五檔及新plan。

### 2026-11-28 16:26 Asia/Taipei (UTC+08:00) 最後修正後 apply 前的新三份計畫

- goodshepherd20 `20261128-082039-4841868-6878de7769e949e5a87bbe2052f1c09c`：schema0／UI2，plan檔SHA-256 `6498FF50CB20514D060ABEAE4A2CBBD0B0A9C6E16E45973B615D5275E720255E`。這是部分套用後的新plan，未重用原164操作舊plan。
- construction `20261128-082213-5347245-5a0b260208bd46edb587bd339e4c5ef8`：schema216／UI3，SHA-256 `BA7F5B2596808EE10EBDA0CD101CA4CD293E8D60B78361D94AAE3F658B89EFE9`。
- association `20261128-082349-1625462-05dc3bed43a14d6888c398e9847e2718`：schema216／UI3，SHA-256 `3791D139FFF2CA84F36FC52DF1A85AA3C092947FE01932808F85BCCE368DC241`。
- 三份皆唯讀成功，未使用額外apply額度。逐值核對與部分套用後／上次受審版的schema operations、organizationId、schemaHash、retainedOwnership欄位存在性及值、UI定義與正式ID完全相同；UI指紋是現況元件集合的ScopeHash；目標view尚未存在，plan不保存新XML本身，不能聲稱指紋已反映新XML。新XML另外以凍結生成器／測試manifest及實機唯讀摘要綁定。共同摘要SHA-256 `6642918251BAF8D62EE70C8AB08D84A910DF0946DBADCDF3CDBAEEA546C7AD70`。本機摘要最初假定所有組織都有retainedOwnership而KeyError，改為同時比較存在性和值；未改plan或產品驗證。
- 三份將與凍結程式修正一起送最後安全審查。完整守門尚未結束，不提前放行；備份3/3及VERIFYONLY仍保留。整合工作樹已有02A§12草稿，遠端當時仍dadcf8b19，尚未把草稿稱已推送；使用者本輪直接授權亦已保存。

### 2026-11-28 16:36 Asia/Taipei (UTC+08:00) 最終凍結程式完整守門

- 最終五檔與凍結manifest逐位元組SHA相符（manifest SHA-256 `C1F02118DDAB1484C71BFE44572D1150D54E242B95FE4600EE45C10A7DBF349D`）。Release build exit0，log SHA-256 `B8AC9839F1DA1E91601FE4DBAF5F15FDD617E2A41C438524266BFCEC0AEB39B7`；完整一般測試28組件、9609通過／0失敗／0略過，log SHA-256 `7F33DF55E8C224E075514AE78C641326F95F58060B5B855997D27B0A5943585F`。
- 文件說明2103／0，log SHA-256 `6A81E6F2B0AD5F31E654ACD3AF6CB8570C9E9F02BBC6EBDDBB8BC3FC3DDEDB34`；Changed編碼54檔、排除1、問題0，SHA-256 `E05714B875E76F992A8522D46F396345E9B7DEC149B9E5BB738C0EACB5A18F7B`；進度32／32，SHA-256 `C104CF7BCAF9184FF2E553777597205849369AD31166AA475E9D968C27C43433`；solution list exit0，SHA-256 `91B4F1D249BD45625A6A146C3D01E51705E6A0D7AEAF644A763E70B0A514BA2D`。本輪沒有新增csproj。
- 依使用者免雙模型及02§4指定ClaudeOnly入口，送五檔完整安全審查、三份新plan共同核對，並要求補審既有pool修正；審查尚未結束，尚未放行apply或稱FS完成。後續若只有文件類變更，依02A§10.3沿用本次build／一般測試，仍重驗文件、編碼與進度。

### 2026-11-28 16:37 Asia/Taipei (UTC+08:00) 最後安全審查與三計畫共同核對

- 單模型（使用者指定 ClaudeOnly）第一輪可用，Critical 0／High 0；summary SHA-256 `ED26EB1B4870B6D697D9D3CDFF2F52F18540B0A05428023139B469C3D79197B6`；完整審查輸出 SHA-256 `1CFB6745D5C286B47F9B42EB0E3EEAFB4C6297039082871D958957403AD74902`。不是雙模型成功。
- 審查明列五程式檔全部完整讀取且與凍結manifest相符；三份完整schema-plan逐份讀取且SHA相符；補審6558f5911的BoundedClientPool／DataverseTraceTests完整diff及Return／RemoveAndDispose周邊，未讀這兩檔整檔，不冒稱pool整體完整審查。另讀UiViewDeployment.Generate與SchemaPlanner ownership邊界。
- 排除：vendor巨型IL dump、schema JSON整檔（僅grep ownership）、plan.md與RED／GREEN／全守門log全文；沒有重跑測試或實機。測試結果由主線原始log及凍結manifest另核對，不能把外審引用數字當獨立執行。
- 新runtime測試是獨立子集不是完整vendor XSD；真XSD差分與正式Create仍各需自己的證據。診斷僅驗Fetch／結構觀察，不代表建立或發布。既有三份plan綁定現況、凍結generator另綁新XML，審查沒有把兩者混淆。
- 合併前再做僅文件變更守門；完整build／一般測試沿用16:32凍結程式結果。apply前仍須重新核對遠端與整合、精確plan SHA、D365鎖、測試身分及備份；沒有提前使用最後一次重試。
