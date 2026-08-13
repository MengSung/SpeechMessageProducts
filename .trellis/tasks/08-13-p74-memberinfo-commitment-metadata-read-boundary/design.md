# P7.4 MemberInfo 承諾類型 metadata 讀取邊界設計

## 範圍與前置條件

本設計只擁有 `ORG-CALL-00040` 的 MemberInfo consumer overlay。P7.3 已經擁有
Data8 metadata executor、profile/generation/locale-bounded runtime cache 與 Package03
typed DTO；本 child 不重做它們，也不宣稱已有 CE、capacity、parity 或 enablement evidence。

現有 Package03 image base gate 不能成為 metadata 的 enablement：
`Package03SpecialResourcesEnabled` 只是一個 Package03 composition base gate，
`Package03MemberInfoCommitmentMetadataReadEnabled` 是這個 consumer 的獨立 rollback boundary。
兩者均 false 時，圖片與 metadata 都維持 legacy；base=true/sub=false 時，metadata 仍不得組成 typed client。

## 資料流與所有權

```text
MemberInfo action
  -> deployment configuration
     -> Package03 base + metadata sub-gate
        -> false: legacy provider + legacy search mapping
        -> true : bootstrap factory
                  -> DI/process-host owned executor generation
                     -> Package03 fixed option-set operation
                        -> request-local metadata service result
                           -> action-local sort/search/row projection
```

1. `DonationDynamicsAccessBootstrap` 只讀 deployment configuration。它先驗證 gate，
   再 bind 非空 ProfileAlias，最後才可碰 process host；factory 不接受 HTTP caller 值。
2. `Package03MemberInfoCommitmentMetadataReadService` 只借用 stateless typed client 與
   immutable profile scalar。它不建 provider、handler、pool、cache、timer、subscription、
   cancellation registration 或 background work，也不 Dispose client；process host 是唯一 reusable
   resource owner。
3. service 以固定 workload `church-report-memberinfo-commitment-metadata-read` 與固定 target
   發出一次 operation，把 upstream DTO 驗證並複製成新的 read-only options。結果不保存 CRM graph、
   locale、profile、token、client 或 exception。
4. action 取得 snapshot 後僅在本 request 使用：文字搜尋從該 options 對照，configured segments 和
   member rows 使用同一份 option list。true branch 遇到未知 value 顯示空字串，而非 call legacy metadata；
   false branch 保留既有 `ResolveOptionSetText` fallback。

## 相容性與取消

- `SearchDistrictTree`、`LoadGroupMembers` 因 true branch 必須 await typed client，改為 async MVC
  actions；route、authorization、JSON shape 與 false-gate legacy data flow 不變。
- `LoadUngroupedMembers` 已為 async；其 Package02 aggregate sub-gate 與本 metadata sub-gate 完全獨立。
  metadata gate=true 不會開啟 Package02，也不改變 empty count、page retrieve、relation 或 authorization owner。
- `OperationCanceledException` 不進 generic `HandleError`；其他 typed exceptions 也不 fallback。
  未完成 operation 的 transport/lease cleanup 仍由下游 executor/process host 擁有。

## 驗證與 fail-closed matrix

| 條件 | 行為 |
| --- | --- |
| base/sub gate 不完整 | 只走 legacy，無 typed composition/I/O |
| gate true 且 ProfileAlias 空白 | host resolution 前拒絕 |
| client null、typed fault、timeout | action failure，不回落 legacy |
| 取消 | 原樣傳遞，不重試 |
| null/超量/重複 value/order/非連續 order/空白或過長 label | 不發布 snapshot |
| true branch unknown raw choice value | 顯示空字串，不 legacy metadata lookup |
| false branch | 保留 legacy provider、locale fallback 和既有 mapping |

## rollback

deployment owner 將 metadata sub-gate 設回 false 即可令後續 request 使用舊行為；
不在 request 中切換 profile、connector、CE version 或 image gate。此設定 rollback 只屬 local
candidate，尚未證明與 legacy 共用 capacity 或完成實機 drain，因此所有 checked-in flags 保持 false。
