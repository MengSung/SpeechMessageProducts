# ORG-CALL-00052 技術設計與判定邊界

## 權威能力

`ORG-CALL-00052` 的矩陣 operation 是 `contact.current.group.retrieve`，來源方法為
`ContactService.GetContactCurrentGroup`。矩陣描述的語意是：讀取聯絡人的多對多名單，
只投影目前的 app-named 小組；產品服務不得接收 CRM SDK 物件。

## 目前來源資料流

```text
AddContactToListAsync(Entity existingContact, targetGroupName, accountPasswordData)
  -> GetContactCurrentGroup(existingContact)
       -> ToolUtility.QueryListOfContactManyToMany(contact.Id)
       -> foreach Entity list; first new_app_named == true
  -> Add/Remove membership
  -> CreatePresentRecord
  -> contact lookup update
  -> AssignOwner
  -> LINE notifications
```

這不是孤立 read consumer。`Entity` 是呼叫端可攜入的可變 CRM 物件；查詢沒有在
`GetContactCurrentGroup` 內建立 authenticated-principal 到 immutable authorization scope
的證明；而「第一筆 app-named list」也沒有 duplicate/ambiguous policy。讀取結果立即
影響後續 membership、出席、contact update、Owner assignment 與通知等多個 mutation。

## 安全設計門檻

只有同時滿足以下條件，未來才可建立獨立 DTO-only capability：

1. 先由伺服器驗證 principal 並建立 request-local immutable scope，再驗證 contact locator；
   browser/contact input 不能選 Profile、Connector、Endpoint、Credential 或 Owner。
2. Data8 executor 使用固定 server-owned query/template，只回傳 bounded
   `CurrentGroupRecord`（例如核准的 list identifier/name），不得外洩 Entity、EntityReference
   或 EntityCollection。
3. 零筆回傳固定 `none`；一筆回傳 `found`；多筆 app-named 結果回傳固定 `ambiguous`，
   絕不取第一筆或猜測目前小組。
4. read capability 與 membership transfer、present-record、contact update、AssignOwner、
   LINE notification 完全分離；不得把新 read 接回現有 `AddContactToListAsync` 作為部分遷移。
5. 具備 cancellation、bounded response、A/B profile/request isolation、fault/timeout
   cleanup、rollback owner 與 gate=false zero-I/O 測試。

## 預期判定

若 source audit 不能證明上述門檻，結果必須是 `source-only-local-design-no-go`，只交付
原因、證據與 recovery path，不建立 registry、executor、ProductClient、consumer route 或
CE fixture。

## 限時外部分析交叉核對

以專案 self-healing runner 執行的 Gemini architect 也判定
`SOURCE_ONLY_LOCAL_DESIGN_NO_GO`：它確認缺少 request-local authorization、第一筆回傳
非確定、SDK Entity 耦合與強寫入鄰接。Claude 在兩次 runner attempt 都無 usable output；
此狀態只能稱為「雙模型未完成」，不能取代本機 source trace 或宣稱完整雙模型審查。
Gemini 提出的「加入排序、傳入 loginContactId」只是未來恢復設計的局部元素；在 immutable
server authorization、ambiguous fail-closed 與完整交易 command 尚未建立前，不能據此進行
partial read cutover。
