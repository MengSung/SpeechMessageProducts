# P7.4 認證聯絡人唯讀安全邊界設計

## 邊界

資料流固定為：

`server-owned operation ID → Data8 executor → immutable wire record → typed ProductClient DTO`

本 child 不接入既有登入 controller。Future consumer 必須先完成 credential policy、
server-side authorization 與 session initialization 的獨立設計，才可引用此 API。

## 契約

- `auth.contact.retrieve.by.account`：輸入為已正規化的帳號 lookup value；不接受
  password 作為回傳值或 routing authority。結果只含 contact ID、account locator、
  display name、active 狀態及固定結果分類。
- `auth.contact.retrieve.by.lineid`：輸入為正規化 LINE ID；固定 active 條件與
  bounded cardinality；zero 或 duplicate 均回傳固定 `not-found`／`ambiguous` 分類。
- 所有欄位在 wire/DTO mapping 時嚴格驗證；任何 CRM alias 型別錯誤、secret 欄位出現、
  超界長度或多筆結果都 fail closed。

## 安全與生命週期

- Deployment profile/workload 由設定與 registry 決定；不得由 request、session、query
  或 DTO 指定。
- Gate false 時在任何 bootstrap、host、pool、handler、client 或 I/O 前返回 disabled。
- Gate true 只允許注入已建立的 typed client 或 deployment-owned host；不得 request-time
  建立 provider；cancellation 原樣傳遞。
- 不保存帳號、LINE ID、contact DTO、password、claims 或 HttpContext 到 static、cache、
  singleton、timer、queue 或背景工作。

## 失敗策略

`invalid-input`、`not-found`、`ambiguous`、`secret-present`、`profile-unavailable`、
`cancelled` 是固定分類；輸出不得含 CRM ID、名稱、端點、token、credential、原始例外
或原始回應。任何 timeout/transport fault 不 retry，並令 client/session 不可回收。

## 相容與 rollback

此 child 的 rollback 是保持 gate=false 並移除未啟用的 typed registration；legacy consumer
保持原狀。不得用 request-time fallback 把 typed failure 導回 legacy，亦不得宣稱 parity。

## 呼叫鏈調查結論

目前所有識別到的 legacy callers 都在取得 contact 後建立 session/claims 或進入讀寫混合
商業流程；它們不是本 child 的 consumer。既有 `AuthenticationController` 自行使用 SDK
查詢並讀取 `new_app_pass`，因此本 child 不得宣稱已遷移 account login。此設計選擇
避免把缺少 credential policy 的 read API 接到登入入口，造成密碼、session 或 A/B identity
跨越未證明的隔離邊界。
