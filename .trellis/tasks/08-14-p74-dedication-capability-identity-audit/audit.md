# P7.4 奉獻能力對應與隔離稽核結果

## 結論摘要

本 child 只完成來源層稽核與任務紀錄；沒有變更任何產品程式、設定、feature gate、權威 matrix、CE、流量、P7.5 或 P8。

`ORG-CALL-00059` 是 `ORG-CALL-00041` 既有產品服務所使用的舊底層 FetchXML helper，屬同一個「依 contact 讀取啟用中認獻單」能力家族。它不應再取得第二個 registry、Data8 executor、ProductClient 或 consumer migration。

`ORG-CALL-00060` 則不是認獻單讀取。它是奉獻表單的 contact resolve／identity hydration 流程；現行 caller 在可證明的 immutable、request-local、server-derived authorization boundary 以前便使用 Session、`InMemoryContext`、可變 manager/form 與 CRM SDK `Entity`。直接建立 DTO-only Gateway capability 會造成錯誤授權或 state bridge，故本 family 判定為 source-only local design no-go。

## ORG-CALL-00059：與 00041 的能力去重

### 已證實的同一性

- `DonationBookingService.FillBookingList` 是目前產品端對 `RetrieveDedicationBookingByFetchXml` 的實際呼叫者。它用 contact fullname／ID 取得 active `new_dedication_booking` 候選列，再逐列讀取完整 Entity 並映射付款表單。
- `FetchXmlQueryService.RetrieveDedicationBookingByFetchXml` 的固定條件是 contact lookup、`new_dedication_booking_status=100000001` 與 `statecode=0`；這正是 `ORG-CALL-00041` phase-0 matrix 所描述的 legacy entry point。
- 已存在的 `payments.dedication.retrieve.by.contact` Data8 operation 以 contact GUID 為唯一查詢 locator，固定 `new_dedication_booking`、active status、active state、排序、page／item／byte 上限，並將 CRM Entity 立即投影為封閉 scalar record。
- `DonationBookingService.MapBooking` 的 consumer 契約需要 booking ID、category、status、每期金額、總期數、認獻總額、付款週期、已付金額、開始日與結束日。既有 typed `DedicationBookingRecordDto`／`DonationBookingReadRow` 已完整提供這些欄位。legacy helper 的 `new_name` 初始投影沒有被該 consumer 使用，因而不是額外 response contract。
- phase-0 matrix 對 00059 已明定「與 product service row 維持同一 capability family，registry 稍後去重」；現有 source 與 typed projection 已提供該去重所需的 consumer 欄位證據。

### 不能升級的完成宣稱

- 00041 的同步 `FillBookingList` 仍是 temporary-legacy；目前 `DonationBookingReadService`／adapter 是預設關閉的本機 DTO boundary，未接入生產 consumer。
- 00059／00041 沒有 CE 8.2、CE 9.1、Embedded、Dedicated、容量、流量、rollback drill 或 ToolUtility removal 證據。
- 去重只表示不應重複建造資料層 operation；它不移除公開 ToolUtility helper，也不把 legacy `top` 行為、N+1 Entity read 或任何其他可能的產品呼叫端自動視為已遷移。

## ORG-CALL-00060：local design no-go

### 已追蹤的舊資料流

```text
browser id 或 Line ID
  -> DedicationAuditController / DonationPaymentManager
  -> EnsureCorrectUserData（Session + InMemoryContext + ListManager）
  -> DonationDedicationFeeFormService
  -> RetrieveContactByLineId 或 RetrieveEntity(contact, id)
  -> 可變 DonationPaymentFormModel / DedicationFeeList
  -> AJAX rows 或 View
```

- `DonationDedicationFeeFormService.FillFromLineIdAsync` 以 Line ID 執行 contact read，然後直接寫入付款表單 identity 與 fee list。
- `GetFeesByContactIdAsync` 接受 string GUID、以 ToolUtility `RetrieveEntity("contact", id)` 讀取 target，再將資料寫入 caller 提供的 `DonationPaymentFormModel`。
- `DonationPaymentManager` 以 `_feeRefreshLock` 將上述工作與其 session-owned `m_DonationPaymentFormModel` 序列化；這是 legacy state lifecycle，不是可交給 typed client 的 authorization 或 DTO response owner。
- `DedicationAuditController.GetFeesByContactId` 的 gate=true 路徑已能讀取 fee DTO，卻仍在 `EnsureCorrectUserData` 之後從 `InMemoryContext.PersonalInfomationModel` 取 CRM `Entity` snapshot 判定角色。它沒有使用 00060 的 contact resolver，故不得把此已存在的 fee-read branch 宣稱為 00060 consumer migration。
- `EnsureCorrectUserData` 在 capability policy 前讀取／寫入 Session、比對 mutable ListManager password、使用 static validation cache，並可重新初始化 `InMemoryContext.ListManager`。它不構成可在 profile/client composition、cache 或 CRM I/O 前使用的 immutable authorization scope。

### 判定與風險界線

現有程式確實會拒絕缺少會計職稱的登入 snapshot，且瀏覽器 `id` 被標示為 locator；然而 source 並未證明 target-contact scope 在上述 mutable chain 之前已由已驗證 principal 產生。會計角色是否可讀所有 target contacts 是產品授權政策，不能由 browser GUID、表單、Session、`Entity` 或 legacy manager 自行推導。

因此不能把 `GetFeesByContactIdAsync` 的 raw `Entity` bridge、`FillFromLineIdAsync` 的 Line lookup、或 gate=true fee read 重接成 `payments.contact.resolve.for.dedication.form`。也不能用 request-time legacy fallback、Entity rehydration、共享 form list 或 exception-to-empty-list 掩蓋 authorization／transport fault。

### 恢復前置條件

未來須先建立獨立、專責的 authorization-boundary child，並完成下列事項後才能重新評估 00060：

1. 在任何 Session、`InMemoryContext`、ListManager、cache、profile/client composition 或 CRM I/O 前，由已驗證 principal 產生 immutable request scope；scope 至少含 subject、server-derived role/policy、允許的 target-contact 規則與 deployment profile/generation，且不保存 CRM `Entity`、credential 或 mutable form。
2. 將「本人 direct contact」、「已驗證 Line principal 對應 contact」與「具明確 server policy 的稽核 target contact」分成各自的授權規則。Line ID 不可作為裸 query 參數；browser target GUID 只可在 policy 已授權後作 locator。無、重複、過期或 ambiguous identity 一律 fail closed。
3. 另行設計固定欄位、bounded、DTO-only 的 contact projection，僅包含 identity hydration 真正需要的 scalar；不回傳 `Entity`、raw query、profile、connector、endpoint、credential、form 或 session graph。
4. 以獨立測試證明 A/B subject/profile isolation、target authorization、Line identity failure、cancellation/fault lease cleanup、response budget、無 partial publication、CE 9.1、Embedded/Dedicated parity 與 rollback evidence。任何 enablement 仍必須另有容量與流量前置證據。

## 雙模型與範圍紀錄

依 45 秒上限透過 CCG self-healing runner 啟動 architect review。Gemini 輸出可讀，但 Claude 在限制內未完成；本 child 記錄為「雙模型未完成，採本機驗證」，不是完整 dual-model review。Gemini 對 00060 建議把 Session 當成安全 contact 來源的部分，不符合本專案 immutable server-derived authorization contract，未被採用。

本次沒有 CE request、fixture、ledger、nonce、資料建立／修改／刪除、feature flag、traffic switch、P7.5 removal 或 P8 操作；歷史 Slice C cycle 亦未讀取、重試或復用。
