# Plugin 外殼與影子投影契約

## 1. 適用範圍與觸發

P5-01 新增的 Plugin Infrastructure、Core 通知及後續模組沿用本契約；計畫以
`文件/規劃設計書/實作計畫-3.5/05A_階段5_執行細則.md` 為準。基礎型別保持 internal，
唯一 public abstract 外殼實作 IPlugin，符合 R2。不得把離線測試寫成實機驗收。

## 2. 介面

- `private protected PluginShellBase(string unsecureConfig, string secureConfig, DepthPolicy depth)`
- `internal abstract void ExecuteRule(PluginInput input, PluginServices services)`
- `DeterministicGuid.Create(string purpose, params object[] parts)`
- `MemberNotificationService.NotifyComposedAsync(..., byte[] tokenKey, CancellationToken ct)`
- `MemberNotificationService.NotifyComposedAsync(..., IShadowPushSink shadowSink, CancellationToken ct)`
- `LinePushClient.ValidateRecipient(MemberRecipient recipient, Guid sourceChurchId)`
- `ShadowRecordWriter.Write(RecordingOrganizationService recording, ShadowPushRecorder pushes)`

## 3. 資料與生命週期

外殼只保留不可變註冊設定及固定文字表；context、輸入、recorder、sink、服務組合均屬
單次 Execute。平台 service 只借用，外殼不 Dispose；兩個 recorder 在 finally 排空及
Dispose，規則失敗也輸出安全意圖。追蹤失敗不得取代原例外，未知例外保持原實例。
業務例外依具名資源格式化；未知鍵／缺參數使用固定後備文字並保留 CorrelationId。

PluginInput 擷取與讀出皆深層複製；保留前後映像、Depth、OrganizationName，Delete
參照正規化成目標 Entity 身分。每列最多 128 欄、2048 值節點、65536 字元／位元組單位，
overlay 最多 64 列，超量明確拒絕。映像與查詢只取模組需要的欄位，不註冊全部欄位。

Shadow 不寫真服務、不解密、不推播。Create／同次新建列 Update／Retrieve 經 overlay；
既有列 Update 只記錄，RetrieveMultiple 不合併 overlay。未知 Execute、批次、Upsert、
非空 RelatedEntities 均明確拒絕。讀取允許具 AliasedValue 的 `alias.field`，逐段驗證；
寫入仍只接受真正 schema 欄位。Fake 的主鍵對照明列 account、contact、appointment、
task 及合成 region；活動用 activityid，未知表具名拒絕，擴充須據實際 metadata 加測試。

影子行為 `SMV-SHADOW|1|` 加精簡 JSON，含 seq、operation、entity、id、fields；
推播投影只記 contactId、templateKey、retryKey、messageCount、contentSha256。
總長含前綴及 CRLF 不超過 8000 字元；截斷必有 overflow／total，最後必有 end／count。
保留摘要有容量上限，不因分次 Trace 無界保留資料。

Token、LINE ID、訊息原文不進投影；個資及長字串只記長度／SHA-256。自訂身分證
new_personal_id、手機 new_cell_hpone、公司電話 company 及帳號欄位須明列，不能只靠
英文片段猜測。SHA-256 用於比較，不宣稱加密或匿名化。投影 JSON 往返明確使用
`DateParseHandling.None`、`FloatParseHandling.Decimal`，保留日期 offset 與金額精度。

收件者檢查兩模式共用 ValidateRecipient；Shadow 摘要沿用 Live 的有界訊息凍結及
server sender，忽略全域 JSON defaults。ShadowRecorded 回 Rejected 且 Success=false，
不冒充 HTTP 成功。範本代號採固定 ASCII 識別字；在首個通知模組前統一入口與 sink 的
格式限制並補雙模式測試，不能把目前兩者的可接受集合說成完全一致。

## 4. 驗證與失敗矩陣

| 條件 | 結果 |
| --- | --- |
| Depth 等於門檻 | 執行；超過才依無例外／Create不擋／只擋Update政策判斷 |
| Shadow 有 secure 設定、重複欄位、非法 mode、設定超過4096字元 | 固定繁中錯誤，不回顯輸入 |
| 合法 QueryExpression／FetchXML 連結別名 | 深層 detached 回應，不放寬寫入 schema |
| 未知寫入或相關實體圖 | 具名拒絕，零真寫入及零候選發布 |
| 影子紀錄超量 | overflow，後續比對不可宣稱零差異 |
| 教會錯配／未綁定／畸形 LINE ID | 兩模式同樣拒絕，先於組訊息、解密及推播 |

## 5. 正常、基本與錯誤案例

正常：同一外殼 A→B→A，每次只留下自己的安全意圖。基本：Shadow Create 後 Update
再讀取，得到獨立的最新候選，真服務零寫入。錯誤：把 related 圖靜默丟掉後仍寫 end，
或把高精度 decimal 解析成 double 再聲稱與期望相同。

## 6. 必要測試

保留 05A P5-01 全部同名測試。回歸須涵蓋 `Recording_CustomPersonalFields_HashedAtAllDepths`、
`Recording_LinkAliases_ReadDetached`、`Recording_RelatedEntityWrites_RejectBeforePublication`、
`Recording_DecimalAndOffset_PreserveProjectionValues`、活動主鍵兩種查詢格式、
`NotifyComposed_InvalidRecipient_SameInBothModes`。GUID 固定向量必須在兩個宿主都過，
不得比較同一函式呼叫充當跨目標證據。Release、架構、打包檢查及固定 guard 仍各自驗證。

## 7. 錯誤與正確作法

錯誤：`JObject.Parse(projection)` 依預設解析日期及 double；Clone 遇未知可變物件就
保留原參照；從 logicalName 猜活動主鍵。

正確：有界讀取器明確保留 decimal／日期原文；SDK 深層複製遇未知型別 fail closed；
根查詢、link、投影及衝突驗證共用經 metadata 核對的主鍵對照。
