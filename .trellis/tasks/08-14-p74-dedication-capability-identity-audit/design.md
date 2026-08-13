# P7.4 奉獻能力對應與隔離稽核設計

## 決策

本 child 採 source-only audit，而非新增 Gateway capability。它以權威 matrix、registry、Data8/ProductClient、ChurchReport consumer 與 legacy call chain 為唯一證據來源，產出兩個彼此獨立的結論。

1. `ORG-CALL-00059` 只在與 `ORG-CALL-00041` 的固定資料語意、template、input、output 和 consumer boundary 均相同時去重；其舊 ID 不會被當成新的 operation ID。
2. `ORG-CALL-00060` 必須先有在任何 legacy cache、Session mutation、manager state、ToolUtility/CRM I/O 前建立的 authenticated-principal、server-derived、immutable scope。若不存在，僅記錄 no-go；不建立 contact resolver、registry、executor、ProductClient 或 consumer partial migration。

## 對應資料流

```text
00059: ToolUtility fixed active-booking FetchXML
  -> matrix de-duplication note
  -> 00041 fixed typed booking operation
  -> local disabled boundary only

00060: controller/browser locator or Line ID
  -> DonationPaymentManager mutable form/session chain
  -> DonationDedicationFeeFormService
  -> ToolUtility contact Entity read
  -> fee/model projection
```

第二條鏈尚未提供可證明的 request-local authorization boundary。browser locator 和 Line ID 只能是定位器，不能選擇 contact、profile、connector、endpoint、credential、organization 或 authorization scope；current manager/form/Entity 也不可跨 request 作為 typed response 或 authorization cache。

## 失敗封閉與恢復

若 `00059` 有任一語意差異，保留分離且不升級 matrix；若 `00060` 查出缺少 boundary、共享 mutable state 或 response budget，立即 no-go。兩者都不重試 CE，也不修改 legacy consumer。

未來要恢復 `00060`，需先由獨立 child 建立：

- server 在任何 cache、profile/client composition 或 CRM I/O 前，從 authenticated principal 建立 immutable request scope；
- 固定、最小化且有界的 contact DTO operation，輸入只允許已授權 locator；
- A/B authorization/profile isolation、cancellation/fault lease cleanup、DTO parity、CE 9.1、Embedded/Dedicated parity、rollback evidence；
- 不可使用 request-time legacy fallback、Entity rehydration、mutable manager/form 或 caller-supplied profile/credential。

此設計不授權 enablement；所有 checked-in gate 維持 false。
