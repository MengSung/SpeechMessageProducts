# P7.4 未分組承諾 aggregate 讀取邊界

## 目標與使用者價值

將權威 matrix 的 `ORG-CALL-00024`（`memberinfo.contact.count.ungrouped.commitment`）從
MemberInfoController 直接使用 CRM aggregate FetchXML 的方式，改為一條獨立、預設關閉、
server-owned 的 Package02 ProductClient count 路徑。這降低一個實際 production capability
對 ToolUtility／CRM SDK 的直接依賴，同時不把其他尚未遷移的未分組查詢誤報為完成。

## 已確認事實

1. immutable authoritative matrix 對 `ORG-CALL-00024` 記錄 Registry、Data8 executor 與
   `IPackage02ContactProfileClient.CountUngroupedCommitmentAsync` 已實作；CE 9.1 是既有 evidence，
   consumer 尚未遷移，Embedded／Dedicated 仍是 `evidence-pending`。
2. ProductClient operation 只接受 deployment-owned profile/workload 與 optional bounded search，
   Data8 connector 自行擁有 closed-status、active small-group、current membership、grouped contact
   排除和 aggregate FetchXML；結果是 bounded non-empty raw OptionSet value/count DTO。
3. `LoadUngroupedMembers` 在 commitment sort 時仍有不同 capability：option metadata ordering、
   empty commitment count、per-segment contact paging、contact authorization 與 relation projection。
   此 child 不把它們 rehydrate 成 SDK entity，也不將它們標為 migrated。
4. P7.4 enablement capacity audit 尚為 no-go；所有 checked-in gates 必須維持 false。此 child
   只能交付 local-only implementation、test、rollback boundary 和明確 external evidence gap。
5. P7.2 歷史 Slice C cycle 為 closed/no-go，與本 read-only child 無關且不得重試或復用。

## 需求

1. 新增獨立 deployment-owned `Package02UngroupedCommitmentReadEnabled` gate；只有它與既有
   Package02 base gate 都為 true 才允許 typed count。任一 gate false 時，必須在 session/access、
   typed client、process host、handler、Data8 pool 和任何 outbound I/O 前使用既有 legacy count。
2. `LoadUngroupedMembers` 必須先維持既有 Church scope、server-derived access、contact authorization
   與完整 legacy response contract。僅在 commitment sorting 已選定時，選擇 `ORG-CALL-00024` 的
   authoritative count implementation；不能將 browser input 用作 profile、connector、credential、
   owner、query 或 authorization。
3. gate=true 時，non-empty aggregate count 只能經 `IPackage02ContactProfileClient`，以固定
   deployment profile 和固定 workload subject 呼叫，精確轉交 `HttpContext.RequestAborted`。
   不得在 typed fault、cancellation 或不完整 DTO 後 request-time fallback 至 legacy aggregate。
4. typed result 的 value/count 必須是 bounded、非負、unique 的 request-local scalar projection。
   duplicate、negative count、空集合不符或 client fault 必須 fail closed；不得把 partial count
   寫入 session、static、cache、view model 或 response。
5. 空 commitment count、metadata ordering、contact page segment retrieve 與 relation projection
   仍由其各自 matrix owner 負責；本 child 文件與測試必須明確區分它們和 `ORG-CALL-00024`，避免
   將 coexistence 說成 legacy fallback 或 full page migration。
6. 新／修改的 C# 與測試須完整繁體中文文件、UTF-8 無 BOM、CRLF、final CRLF，並證明 A/B
   interleaving 不會重用 result、profile、workload、cancellation 或任何 resource。

## 明確不在範圍

- CE request/mutation、feature gate enablement、traffic switch、ToolUtility removal、P7.5、P8、
  Central Gateway、Official Worker、push 或 PR。
- empty commitment count、metadata provider cache、contact page projection、memberships、owner、
  attendance、financial write 及其他 matrix row 的遷移。
- 遷移後對 authoritative archived matrix 的改寫；該 matrix 維持 immutable evidence。

## 驗收條件

- [ ] 兩份 checked-in appsettings 皆有獨立 gate 且為 `false`；base/sub-gate 任何一個 false 時
      不建立 typed graph 或 outbound I/O。
- [ ] gate=true 時 controller 的 `ORG-CALL-00024` 只使用 typed ProductClient request，固定
      profile/workload，並傳遞 request cancellation；沒有 CRM aggregate request、SDK rehydration、
      retry 或 fault fallback。
- [ ] typed count service 對 duplicate、negative、null／incomplete DTO 與 fault fail closed，且
      不發布 partial result；A/B fake client interleave tests 證明無 cross-request/profile leakage。
- [ ] legacy empty-value count、metadata、paging 和 authorization 仍保持既有 contract，並被測試
      明確列為不同 capability，而非本 child 的 fallback 或完成證明。
- [ ] focused tests、relevant Dynamics／ChurchReport tests、Release build、encoding/CRLF、
      `git diff --check`、scope check 與限時 CCG analysis/review evidence 均完成；若 dual model
      未在 45 秒完成，必須記錄為「雙模型未完成」而不拖延工作。
