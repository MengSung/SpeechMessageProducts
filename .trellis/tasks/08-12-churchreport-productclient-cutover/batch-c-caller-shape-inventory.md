# P7.4 Batch C：Package01 Fee Caller-Shape Inventory

## 判定原則

`Data8 executor=implemented`、`ProductClient=implemented` 與 CE 9.1 Embedded 唯讀 evidence
只證明 capability 層可用，不等於 ChurchReport consumer 已遷移。P7.4 consumer 只有在完整的
server-validated authorization、request-local DTO boundary、cancellation/fault handling、false-gate rollback，
以及與 write/idempotency orchestration 分離均獲證明時，才可標為 `migrated-disabled`。

本檔僅盤點 `ORG-CALL-00005`、`ORG-CALL-00064`、`ORG-CALL-00066`；不改寫 archived
authoritative matrix，亦不執行 CE、feature gate、流量、P7.5 或 P8 操作。

## ORG-CALL-00005：fee.dedication.retrieve.by.contact

`IPackage01FeeReadClient.RetrieveDedicationFeesByContactAsync` 已存在，但目前 ChurchReport 沒有直接
使用它的純 DTO consumer。最近似的稽核 AJAX 路徑為：

```text
DedicationAuditController.GetFeesByContactId
  -> DonationPaymentManager.GetDedicationFeesByContactIdAsync
  -> DonationDedicationFeeFormService.GetFeesByContactIdAsync
  -> ToolUtility.RetrieveEntity(contact, browser-supplied id)
  -> FillFromContactAsync
  -> DonationFeeQueryService.FillFeeListAsync
  -> ORG-CALL-00006 date-range capability / legacy date-range FetchXML
```

它實際走的是 `ORG-CALL-00006`，不是 00005；而且 action 直接接受瀏覽器傳入的 contact ID，未在該
action 邊界以目前登入 subject/authorization scope 重新驗證。不可直接改接 ProductClient，否則會把
caller-controlled CRM ID 當成金融資料的存取權限。

後續須建立 server-side selected-contact authorization：由伺服器端同名查詢流程產生、綁定目前
session/subject 與有效期限的選取證明，或由 action 以目前受權範圍重新解析 contact。AJAX response
還須改為 request-local DTO，不能讀寫共享 `DonationPaymentManager.m_DonationPaymentFormModel`。
這是一個獨立的高風險 identity/financial-read sub-task；完成前 00005 不得標示為 migrated。

## ORG-CALL-00064：fees.retrieve.by.dedication.period

direct consumer 位於 `Tools/RecurringDonationPaymentProcessor.HandlePaymentReturn`：先讀取
dedication booking，再以 legacy `RetrieveFeeByFetchXml(..., "001")` 判定首期 fee 是否存在；同一條
synchronous payment-return chain 隨後會建立 fee、更新 booking，且可能更新 contact/card 資料。

雖有 `RetrieveFeesByDedicationPeriodAsync` typed API，這仍是 financial write/idempotency/reconciliation
的控制條件。直接改為 async client 會迫使整個 payment-return orchestration 重新定義 cancellation、
timeout-after-dispatch、read-back 與 no-retry policy。故 00064 維持 temporary-legacy，交由具有
`payments.dedication.*` write evidence 的 owner task 一併拆分；不得用同步等待或 SDK rehydration 偽裝成
純 read migration。

## ORG-CALL-00066：fees.editor.load.by.disciplelesson

實際路徑是：

```text
FeeList.SetupPresentFeeList
  -> FeeDownUpLoader.GetPresentFeeList / SetPresentFeeList
  -> ProcessDiscipleLesson
  -> RetrieveEntity(new_disciple_lessons)
  -> StorLessonQueryService.GetEntityCollectionByDiscipleLesson
  -> per-row Entity / EntityCollection projection
  -> fee editor update / create / assign-owner adjacent paths
```

`RetrieveFeeEditorRowsByDiscipleLessonAsync` 是 bounded DTO API，但此 consumer 仍依賴 CRM Entity、
formatted values 與可變 `FeeList`，並連到費用更新、建立、指派流程。P7.4 不可把 DTO rehydrate 為 SDK
entity，也不可同時把 read path 偽裝成 write migration。後續 owner task 應先拆出 request-local fee-editor
read projection，再處理 `fees.editor.update.by.storlesson`、`fees.create.from.storlesson` 與 owner
assignment 的 write idempotency/read-back/rollback evidence。

## CCG 與下一步

本 inventory 已以核准 CCG self-healing runner 發起 architect analysis。Gemini 在限時內有可用輸出；
Claude 未在使用者授權的 45 秒內完成。本輪為「雙模型未完成」，以本檔列出的 repository call-chain
為最終依據，且不得重試等待。

三個 rows 均維持 `temporary-legacy`／`consumer-not-migrated`。下一步是盤點 P7.3 typed
special-resource capability 的實際 ChurchReport consumers；只有完整符合上述安全條件者才能形成新的
local-only P7.4 batch。
