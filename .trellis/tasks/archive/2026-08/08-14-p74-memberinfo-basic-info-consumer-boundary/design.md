# P7.4 MemberInfo basic-info consumer boundary 設計

## 決策

本 child 的結果是 **consumer migration no-go**。`UpdateContactInfo` 是四欄 legacy CRM mutation，既有
typed capability 是二欄 read-back-confirmed operation。只替換 phone/address 會使同一 request 混用
Gateway 與 ToolUtility；略過兩個 OptionSet 則會改變既有語意。兩條路徑都沒有共同 transaction、deadline、
完整 read-back、reconciliation、補償順序或 single rollback owner，因此不接線、不碰 CRM。

## 未來恢復條件

另立 P7.2 write-family child，先建立四欄 DTO-only contract、server authorization、OptionSet metadata 與
valid-value policy、idempotency、逐欄 read-back/reconciliation、task-owned fixture cleanup、rollback owner、
CE/host/parity evidence，才可重新評估 consumer migration。timeout、ambiguous、partial 或 cleanup
uncertainty 一律 fail closed 且不得重試。

## 生命週期與範圍

本 child 只讀取 bounded repository/task evidence；不建立 ProductClient、Data8 pool、host、CRM service、
session、cache、timer、fixture 或 CE request。所有未來 DTO、snapshot、exception、cancellation 都必須
request-local；不得輸出 CRM ID、姓名、endpoint、token、cookie、secret 或 raw exception。
