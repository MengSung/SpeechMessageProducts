# P7.1 認獻單強型別讀取能力

## 目標

完成權威 70-row matrix 的 `ORG-CALL-00041`：建立一項資料量有界、伺服器擁有查詢語意、
DTO-only 的 `payments.dedication.retrieve.by.contact` 讀取 capability。它將取代未來 consumer
所需的「先以 ToolUtility FetchXML 取得清單、再逐筆 `RetrieveEntity`」資料層能力，但本 child
絕不切換 ChurchReport consumer、絕不啟用 feature gate、絕不進行 CE 寫入或雲端部署。

## 權威事實與範圍

- matrix 將 `ORG-CALL-00041` 定義為 ChurchReport `DonationBookingService.FillBookingList`：依 contact
  取得 `new_dedication_booking` 的金融讀取；現行 legacy shape 是 FetchXML collection 加逐筆 entity
  retrieve，屬於 N+1 與 CRM `Entity` 外洩風險。
- 這不是 `ORG-CALL-00005` 的 `fee.dedication.retrieve.by.contact`，也不是 `ORG-CALL-00064` 的付款
  回傳 dedup read；不得複用 fee DTO、fee template、payment-return plan 或其金融寫入權限。
- 實作必須沿用 P7.1 的 Data8 generation-owned pool、Gateway/Embedded 共用 executor、封閉 registry
  與 ProductClient boundary；新能力只接受 `contactId` 與可選、非 authority 的 `contactName`。
- 所有 checked-in feature flag 必須保持 `false`；本 child 不修改 `DonationBookingService`、
  `DonationPaymentManager`、controller、ViewModel 或任一 legacy route，因此既有產品流量與 rollback
  path 不變。
- 本 child 不執行 CE 8.2/9.1、Official Worker、fixture、write、traffic、P7.4 consumer cutover、P7.5
  ToolUtility removal、P8 或 push/PR。CE read evidence 在本機品質閘門完成後，仍須由另一個明確
  deployment-owned evidence cycle 決定，不能由單元測試升格。

## 需求

1. 新增固定 operation ID `payments.dedication.retrieve.by.contact`、固定 server-owned template、固定
   response discriminator、固定 page/byte/item 上限與唯一的 `contactId` 必填參數；不得接受 FetchXML、
   QueryBase、attribute map、entity name、endpoint、credential、connector、profile 或任意 query。
2. Data8 connector 必須為此 capability 使用單一、明確欄位投影查詢 `new_dedication_booking`，以一次有界
   `RetrieveMultiple` 完成結果；不得逐筆 `Retrieve`、不得返回 CRM `Entity`、OData annotation、
   continuation URL、原始例外、payment/card value 或未列入 DTO 的欄位。
3. 定義獨立的 immutable wire record 與 ProductClient DTO。公開 DTO 只包含後續 ChurchReport 認獻單
   畫面所需的 allowlisted scalar：booking ID、category/status option value、各金額、期數、開始/結束日與
   必要的顯示字串；不得以 `FeeRecordDto`、`Entity` 或 dictionary 代替。
4. 建立獨立的 `IPackage01DedicationBookingReadClient`，使產品僅能提交 deployment-owned profile/workload、
   typed contact locator 與 cancellation token。client 必須驗證 operation ID、response kind 與 selected branch，
   每次映射均建立新 DTO 集合，不保存 client response、request、session、cache、connection、lease、
   principal、token 或 profile mutable state。
5. executor 的 request parameter 必須在 pool/connector allocation 前驗證並防禦性複製；取消、timeout、
   connector fault、projection fault 或大小上限違反時既有 lease/fault eviction/permit cleanup contract 不得
   改變。不可新增 retry、同步阻塞或 shared cache。
6. 以 TDD 建立 registry、Data8 query/projection、ProductClient mapping、A/B interleaving、wrong branch、
   cancellation 與 bounded-response regression tests。測試只能使用 fake executor/fake Data8 service，不能建立
   CRM fixture、連線、背景工作或真實 CE 呼叫。
7. 每個新增或實質修改的 C# 檔案均須有完整、維護得當的繁體中文 XML/implementation/lifecycle 文件，並以
   UTF-8 無 BOM、CRLF、final CRLF 儲存。完成前執行 targeted tests、Dynamics Release tests、solution
   Release build、encoding scan、`git diff --check`、scope/forbidden-pattern scan 與 CCG 雙模型限時 review。

## 非目標

- 不修改 `DonationBookingService` 或將它切到 typed client；這是後續 P7.4 child，且要先有 server-side
  contact authorization、deployment gate、legacy/Gateway admission 與 rollback evidence。
- 不新增 generic Dynamics read API、SDK bridge、caller-supplied filter、request-time fallback 或雙讀影子流量。
- 不執行任何 CE mutation 或重試已封存 P7.2 Slice C；不讀取或復用其 nonce、ledger、fixture、descriptor。
- 不宣稱 consumer migrated、CE evidence succeeded、Dedicated evidence succeeded、P7.5 ready 或 P8 ready。

## 驗收標準

- [ ] registry、Data8 executor、wire response、ProductClient client 與其 tests 對 `ORG-CALL-00041` 形成
      單一封閉能力；輸入、template、response branch 與 bounded policy 都可由 tests 精確證明。
- [ ] Data8 projection 不含 N+1 `Retrieve`，不把 CRM SDK `Entity`／`EntityCollection` 越過 connector；
      ProductClient 不公開 CRM/OData/connection/credential/response stream。
- [ ] A/B interleaving、wrong operation/branch、cancelled/faulted request 與 mutable-source defensive-copy
      tests 證明沒有跨 user/profile/request state 或 partial response reuse。
- [ ] 所有 checked-in gate 維持 false；沒有 CE request/mutation、fixture、traffic、consumer cutover、P7.5、P8、
      push 或 PR；matrix 只可在 child 結案時如實更新為 registry/executor/client local completion，不能升格為
      consumer 或 CE evidence。
- [ ] 本 child 的 Trellis/CCG artifacts、品質檢查、限時雙模型狀態、scope-only commit 與 archive 均完成。

## Requirements

- TBD

## Acceptance Criteria

- [ ] TBD

## Notes

- Keep `prd.md` focused on requirements, constraints, and acceptance criteria.
- Lightweight tasks can remain PRD-only.
- For complex tasks, add `design.md` for technical design and `implement.md` for execution planning before `task.py start`.
