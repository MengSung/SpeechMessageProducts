# P1 結構部署與只讀metadata契約

## 適用範圍與命令

P1-11延伸既有Deployment工具，只有明確`--schema`或`export-metadata`進入新路由。
原P0 registration清單、Plugin DLL驗證與plan/apply/verify/export/rollback/smoke保持原樣，不能藉結構指令略過原62案。

```text
export-metadata --org goodshepherdback
plan --org goodshepherdback --schema d365/schema/speechmessagecore.schema.json
apply --org goodshepherdback --schema <repo內schema> --plan <該組織artifacts內plan>
verify --org goodshepherdback --schema <repo內schema>
```

新模式不需要Plugin DLL。共用單一User Secrets固定HTTPS connector，每命令一個獨占lease，
apply使用既有OrganizationWriteLock，先Dispose client再釋放寫鎖；不開另一個pool或讀環境變數憑證。

## 唯一定義與嚴格輸入

`d365/schema/speechmessagecore.schema.json`記6個受管實體、37欄（P1 36、P4 1）與4個唯一鍵。
Account的五個語系欄之外，附錄A.1的LINE lookup屬P1；`new_sinopacprofile`保留phase4，不提前apply。
既有transactioncurrencyid/new_lineid/statecode沿用；A.7舊業務表lookup等P8盤點，不猜表名。

- schema最多1MiB、plan最多4MiB、JSON深度32；open後重驗大小，拒未知／重複屬性。
- 固定SpeechMessageCore、清單指定publisher、new前綴；表／欄／key邏輯身份唯一且有界。
- 新表主名稱必須在P1字串欄存在；key欄須存在且不重複；lookup僅指向已宣告表。
- 欄位支援string/memo/integer/choice/lookup/datetime；日期時間點UserLocal/DateAndTime存UTC，沒有資料轉換入口。
- 加密token/secret欄啟用IsSecured；本工具不建立角色、帳號或FieldSecurityProfile擴權。
- 新localizedtext/offeringcategory開entity及field audit；outbound必須具樂觀並行。
  SDK IsOptimisticConcurrencyEnabled是readonly，不能反射／直接設定；官方說明custom table預設true，建立後重讀核對，不符驗收失敗。

## 完整metadata與真實身份

`SchemaMetadata.Read`只取server組織ID/uniqueName/版本/基礎LCID、完整entity metadata及
solution/publisher/component/filter/step定義，不讀業務列、configuration本文、trace或credential。
基礎語言用server LCID建立label，不以本機文化或假設語系替代。

- entity最多4096、每表2048欄、總欄100000；正式metadata ID與同集合邏輯身份皆須唯一。
- D365多型lookup可指向數百表。2026-11-06只讀真組織566表/22678欄，最大379個目標；
  完整metadata目標預算為4096，保留全部不同目標，仍拒重複／非法／4097超量。P1正式單目標lookup仍exact比對。
- 內建publisher可沒有custom prefix，唯讀投影保留其正式身份與空值；空值不代表符合new。
- prefix不是publisher身份，多個舊publisher可合法共用new。沒有本solution時只用清單指定正式publisher名稱；
  該名稱不存在就規劃自己的publisher，不猜選其中一個舊產品publisher。既有solution仍核對其實際publisher與new前綴。
- metadata註冊集合可超5000列；只使用Core單頁驗證作十頁/50000列的專屬聚合，
  跨頁重複ID、空續頁、壞/重複cookie及超量整體拒絕。**Core業務查詢5000限制不變**。
- 投影只保存純值與不可變集合，全部完成才回snapshot；來源mutation不得改已回結果，沒有跨命令cache或背景Task。

## 完整計畫與只新增套用

plan含真orgID、publisherID、UTC時刻、schema hash、完整metadata fingerprint、有序操作、pending keys與plan hash。
hash是漂移／完整性檢查，不是授權或數位簽章。apply重新讀server並完整重算plan後才可寫。

1. 全部既有表/欄/key及solution政策衝突先拒絕：型別、長度、lookup目標、選項值、日期behavior/format、required、security、audit、owner、concurrency、managed與key欄位。
2. 建立順序publisher→solution→所有新表→一般欄→lookup→key→Include元件，不能依schema列順序碰巧滿足依賴。
3. 若涉及既有表，先比較published/unpublished的可投影功能約束；有差異停止，不順帶發布未審核結構。
4. `before-metadata.json`必須CreateNew、flush(true)、close成功，才發第一個SDK寫入；metadata snapshot不是資料庫備份。
5. 只Create及AddSolutionComponent；新ID必須由SDK回應，不能用臨時Guid／request token替代。
6. 只定向PublishXml受影響表，不PublishAll；再讀已發布metadata。仍有新增差異為失敗，pending key為未完成。
7. 失敗保留before與原Exception，不Delete舊元件、不改日期／權限、不自動rollback VM。
   重新讀取及審核plan後可接續剩餘新增；不得重放失效plan繞過hash。

JSON/Markdown輸出共用單一writer：操作專屬temp CreateNew→flush/close→禁止覆寫File.Move，UTF-8無BOM/CRLF，最多100MiB。
路徑必須限制repo schema／該組織artifacts，逐層拒link/junction，不只比較字串前綴。

## 結果與例外矩陣

| 條件 | 結果 |
| --- | --- |
| 白名單／選項／路徑拒絕 | 2，Connect前停止 |
| metadata有差異 | verify=1，不自動修改 |
| key Pending/InProgress | 3，不能當Active或重建 |
| key Failed或既有功能約束衝突 | 失敗，須審核修復，不Delete/Update掩蓋 |
| 完整一致且全部key Active | 0 |
| 明確caller token取消 | 130，不告警；已入場同步SDK仍drain |
| 內部timeout／SDK故障 | 原Exception交Program共用ExceptionDiagnostics，安全摘要不印本文 |
| 通知器本身故障 | 固定stderr，不覆蓋原故障、不遞迴 |
| 同組織兩apply用同plan | 第一個新增，第二個取得寫鎖後重新驗plan並拒絕漂移，無重複元件 |

## 必要驗證與證據限制

`SchemaDefinitionTests`獨立核對附錄A、strict JSON與schema hash；`SchemaMetadataTests`驗完整頁／scope／
實測多型lookup、publisher空prefix、不同server ID千次並行、來源mutation與百快照弱參照全死亡。
`SchemaPlannerTests`逐條故障約束與漂移；`SchemaEngineTests`真SDK訊息→合成metadata DB→重新plan，
驗before先落檔、四日期/四key/兩安全欄與第二次零寫入；`SchemaCliTests`真路由與真共用日誌、取消／路徑／寫鎖。

新部署摘要只經正式Localization resx，生成檔由I18nCheck產生，原英文六命令與未發布政策不變。
SystemTextCatalogTests獨立列完整24鍵，不能以固定原12鍵阻擋合法資源，也不能以被測Count反推oracle。
Deployment與Localization各目標獨立coverage≥80%、strict、完整方案、00§8及打包／套件／duplicates／編碼守門都須通過。

P1-11的離線套用與真正只讀export/plan不等於P1-12。VM用途、隔離、適當備份與可驗證復原未取得前，
不apply、不建測試業務列、不還原checkpoint。P1-12另須真apply、verify=0、後台元件、Active索引拒第二筆與自身測試資料清理證據。
