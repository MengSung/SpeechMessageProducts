# P7.4 authorized fee contact read consumer

## Goal

將唯一的 `ORG-CALL-00005`（`fee.dedication.retrieve.by.contact`）遷移為由伺服器
授權、request-local、DTO-only 的 Package01 讀取 consumer，同時保持既有 feature flag 預設關閉。
本 task 不執行 CE request／mutation、feature enablement、流量切換、P7.5 或 P8。

## Requirements

1. `DedicationAuditController.GetFeesByContactId` 必須在處理 browser-supplied contact GUID 前，重新建立
   現有 request 的 session context，並從伺服器已解析的 login contact 判斷既有「會計」奉獻稽核權限。
   不得將 browser contact GUID 作為身分、角色或 profile 的來源。
2. login contact 缺失、沒有有效 ID、缺失職稱或不具會計權限時，必須在任何 target contact lookup、
   Package01 dispatch 或 legacy fee query 前 fail closed。
3. `Package01FeeReadsEnabled=false` 時，授權成功後維持既有 legacy query 與 JSON 相容結果；本 task
   不得改變任何 deployment setting。
4. `Package01FeeReadsEnabled=true` 時，必須使用既有
   `IPackage01FeeReadClient.RetrieveDedicationFeesByContactAsync`；不得讀取 browser-selected target 的
   CRM `Entity`、不得將 DTO rehydrate 成 `Entity`、不得在 typed fault 或 cancellation 後 fallback 到
   ToolUtility。
5. typed branch 必須回傳新的 request-local rows 與 total，不得寫入、重用或保留
   `DonationPaymentFormModel`、CRM entity、profile、DTO 或 cancellation state。金額超出既有 `Int32`
   model 範圍時必須 fail closed。
6. cancellation 必須原樣傳到 typed client；任何 manager semaphore 或 legacy drain lease 的既有 owner
   必須仍由 `finally`／`await using` 釋放。
7. 所有 C# 變更必須遵守 AGENTS.md：完整繁體中文文件、UTF-8 無 BOM、CRLF、final CRLF；並有
   server authorization、false-gate 相容、true-gate no-Entity/no-fallback、cancellation、atomic result
   與 A/B request-local isolation 的測試。

## Acceptance Criteria

- [x] 既有 accounting-role 規則僅以伺服器 login-contact snapshot 評估；不可信的目標 GUID 不可取得權限。
- [x] 無權限或無 login snapshot 的請求在呼叫 target lookup 或 Package01 前以固定去識別化失敗回應結束。
- [x] false-gate 使用 legacy path；true-gate 只使用 typed `fee.dedication.retrieve.by.contact` path。
- [x] true-gate result 是 immutable/request-local DTO projection；不修改 `DonationPaymentFormModel`，也不含
      target `Entity` lookup、DTO-to-Entity rehydration 或 legacy fallback。
- [x] 所有新增／修改的 focused tests 通過，並通過相稱的 ChurchReport suite、Release build、encoding/CRLF、
      `git diff --check`、scope check 與 CCG review。
- [x] 所有 Package01 feature flags 維持 false；無 CE、traffic、P7.5、P8、push 或 PR 操作。

## Notes

- authoritative matrix：`ORG-CALL-00005` 為 executor/client 已實作、CE 9.1/Embedded read evidence succeeded、
  consumer 尚未遷移的唯一可設計 fee read candidate；Dedicated evidence 仍 pending。
- Gemini architect 完成可用分析；Claude 在 45 秒上限未完成。此為降級分析，不能宣稱完成雙模型審查。
- `ORG-CALL-00064`（付款寫入相鄰）與 `ORG-CALL-00066`（SDK Entity/fee editor）維持 temporary-legacy，
  不在本 task 範圍。
- 最終 reviewer run：Gemini 在 45 秒界限前完成可用輸出，沒有 Critical 或 Warning；Claude 未在上限前
  產出可用結果而被停止。這是 Gemini-only 降級 review／「雙模型未完成」，不得稱為完整雙模型審查。
- 本 task 的 migration 是本機、disabled-by-default 證據：它不會把 authoritative matrix 的 CE 9.1
  evidence 或 host evidence 升級為 Dedicated cutover、feature enablement、P7.5 zero-reference 或 P8
  deployment 證據。
