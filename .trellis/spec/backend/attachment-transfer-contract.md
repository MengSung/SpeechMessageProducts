# 附件 Core 容量與格式契約

## 1. 範圍／觸發

修改 `ToolUtilityNameSpace.AttachmentOperations`、附件公開委派或容量政策時適用。
僅處理 annotation SDK 邊界，不取代 host 的組織／使用者／parent 授權、連線或傳輸限制。

## 2. 簽章

- `AttachmentService(object logger, IOrganizationService organizationService)`：舊簽章保留且啟用有界預設。
- 額外三參數建構式接收 `AttachmentTransferLimits`；三個正 int、getter 無 setter。
- `DownloadAttachment(ref IOrganizationService, Guid)`：完整驗證單頁後回傳原候選，分頁由 caller 擁有。
- `UploadAttachment(ref IOrganizationService, string, string?, string?, string?, string?, byte[], Guid)`：七欄、一次 Create、void。

## 3. 契約

- 保持原 namespace；服務只保存 limits，不保存 ctor logger／SDK、候選、byte[] 或本文。
- 實際只用每次 ref SDK，不替換、不 Dispose；呼叫期間 caller 不得改 ref 或可變輸入。
  host 擁有授權、逾時與同步操作 drain，禁止把此服務當作授權機制。
- 預設 raw 5 MiB／整頁本文 32 Mi 字元／5000 列，是本地預算，**不是已查證的 D365 組織上限**。
  SDK 反序列化配置不能事後撤銷，host 仍須限制 transport。
- 下載重用 `EntityQueryService` 的根表／正式 ID／重複 ID 驗證，再驗 objectid 與容量；不部分發布。
  objectid 必須為有表名且 GUID 符合請求的 EntityReference。無／null documentbody 是合法文字附註。
- Base64 掃描不配置解碼副本；接受空字串與四種空白 space/tab/CR/LF，計算確切 rawbytes，
  不是只算編碼長度。整頁字元計入空白；long 計數避免設定大值溢位。
- Framework BCL 接受部分非法多餘 padding（實證 tCo==），net10 拒絕；產品兩目標均按標準拒絕。
  caller 擁有後續解碼與資料，不得在驗證後被其他執行緒改寫；不為相容舊 BCL bug 放寬標準。
- 上傳先驗 raw 長度再 Base64／I/O，合法零bytes與四個null metadata保留；空Create ID拒絕、不盲目重試。
- 固定英文 wrapper 保留同一真 InnerException；真正故障由 host 共用告警邊界處理，Core 不建立 notifier／LINE。

## 4. 驗證矩陣

|輸入／故障|結果|
|---|---|
|同名不同ID、無本文附註|完整保留|
|重複／空ID、錯表或錯parent|整頁拒絕、不裁剪或補ID|
|單檔／單頁／列數超限|拒絕；上傳不得有SDK I/O|
|raw恰四bytes的八字元Base64|通過；同為八字元的五／六bytes拒絕|
|非法padding／特殊非標準空白|兩目標一致拒絕|
|SDK自身ArgumentException／Timeout|固定wrapper、原InnerException、一次操作|
|不同ref SDK並行|無ctor回退、跨scope資料或外部Dispose|

## 5. 正常／基本／錯誤例

- 正常：兩個同主旨附註使用不同annotationid，兩者均返回。
- 基本：空bytes產生空documentbody，七個欄位都存在。
- 錯誤：以Base64長度相同就放過超限rawbytes，或把缺本文附註刪除。

## 6. 必跑測試

雙目標 `ToolUtility.Tests`：`AttachmentCharacterizationTests`、`AttachmentSafetyTests`、
`CoreTargetFrameworkTests`；net10另跑 `AttachmentCallerRouteTests` 真實公開委派。
保留512組1536候選還原／拒絕、1000 A/B並行與active=0、ctor弱參照、borrowed Dispose=0。
每目標獨立新coverage目錄，恰一份報告；完整00§8及自癒review仍必要。

## 7. 錯誤／正確

- 錯誤：只驗 `body.Length <= 8` 即宣稱 rawbytes <= 4，或依Framework寬鬆解碼放行 tCo==。
- 正確：先整頁字元預算，再掃描符號／padding並計算 `symbols / 4 * 3 - padding`，兩target相同拒絕。
