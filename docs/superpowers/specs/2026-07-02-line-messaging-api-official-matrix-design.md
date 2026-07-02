# LINE Messaging API 官方對照矩陣設計規格

## 1. 背景

目前專案已將 LINE 相關程式抽離到兩個專案：

- `Line.Messaging/Line.Messaging.csproj`
- `LineMessagingProcessor/LineMessagingProcessor.csproj`

其中 `Line.Messaging` 目標是成為可重用的 LINE Messaging API SDK；`LineMessagingProcessor` 則仍帶有較多產品流程與舊式 RestSharp 呼叫痕跡。這一階段不直接修正 SDK 程式，而是先建立一份以 LINE 官方文件為基準的完整對照矩陣，找出目前 SDK 寫錯、不完整、缺少、過時或不安全的地方，作為後續實作計畫的唯一依據。

官方基準來源：

- https://developers.line.biz/en/reference/messaging-api/

## 2. 目標

建立一份完整、可追蹤、可分階段執行的 LINE Messaging API 官方對照矩陣。

矩陣必須能回答以下問題：

- 官方 Messaging API 有哪些 endpoint、object、webhook event、message object、action object。
- 目前 SDK 是否有對應方法或類別。
- 對應方法或類別是否真的正確實作，而不是只有介面宣告或文件宣稱完成。
- API host 是否正確，例如 `api.line.me` 與 `api-data.line.me` 是否分流正確。
- endpoint path 是否正確，例如是否出現 `_uri` 已含 `/v2` 卻又再接 `/v2/bot/...` 的錯誤。
- 哪些問題屬於安全風險、立即執行會打錯 LINE API、或只是後續補齊項目。
- 下一階段應該依什麼優先順序修正 SDK。

## 3. 非目標

這一階段不做以下工作：

- 不修改 `Line.Messaging` 或 `LineMessagingProcessor` 的 SDK 程式碼。
- 不補齊任何 LINE API 實作。
- 不處理 LIFF SDK。
- 不處理 LINE Login。
- 不以 ChurchReport 現有流程縮小官方 API 範圍。
- 不把目前用不到、進階、方案限制或新功能排除在矩陣外。

## 4. 矩陣交付物

後續 writing-plans 階段應規劃產出下列文件：

- `Line.Messaging/文件/LINE_Messaging_API_官方對照矩陣.md`

該文件是後續 SDK 修正計畫的來源。SDK 修正不得只依直覺或現有文件宣稱進行，必須回到矩陣逐項確認。

## 5. 矩陣欄位

矩陣每一列代表一個官方 API endpoint、官方 object、webhook event、message object 或 action object。

欄位定義如下：

| 欄位 | 說明 |
| --- | --- |
| 官方分類 | 例如 Message API、Webhook、Rich Menu、Audience、Insights。 |
| 官方 endpoint / object | 官方文件中的 endpoint path 或 object 名稱。 |
| HTTP method | endpoint 使用的 HTTP method；非 endpoint 類項目填 `N/A`。 |
| host | 官方要求的 host，例如 `api.line.me`、`api-data.line.me`；非 endpoint 類項目填 `N/A`。 |
| 官方用途 | 用一句話描述官方功能用途。 |
| 目前 SDK 對應方法/類別 | 對應到 `Line.Messaging` 目前的方法、類別或 enum；找不到則填 `None`。 |
| 目前狀態 | 使用第 6 節定義的固定狀態值。 |
| 問題類型 | 例如 host 錯誤、endpoint 錯誤、缺類別、欄位不完整、安全風險。 |
| 風險等級 | 使用第 7 節定義的 P0-P3。 |
| 建議修正 | 下一階段應採取的具體修正方向。 |

## 6. 狀態值定義

矩陣必須使用固定狀態值，避免「已完成」、「待確認」這類模糊描述。

| 狀態 | 定義 |
| --- | --- |
| `Correct` | SDK 已對應官方規格，host、path、method、payload、response model 都沒有已知問題。 |
| `WrongEndpoint` | method 或類別存在，但 endpoint path 與官方規格不符。 |
| `WrongHost` | method 或類別存在，但應使用的 host 錯誤，例如應走 `api-data.line.me` 卻走 `api.line.me`。 |
| `Missing` | 官方項目存在，但 SDK 沒有對應方法、類別或 enum。 |
| `Partial` | SDK 有部分支援，但 payload、response、欄位、enum 或例外處理不完整。 |
| `NotImplemented` | 介面或方法宣稱存在，但實作仍拋出 `NotImplementedException` 或等同未完成。 |
| `Obsolete` | SDK 使用舊版官方規格、舊 endpoint、舊欄位或過時語意。 |
| `Unsafe` | 存在安全風險，例如硬編碼 Channel Access Token、錯誤 signature 驗證、未保護 secret。 |
| `NeedsOfficialVerification` | 初步看起來可疑，但必須再查官方文件細節才能判斷。 |

## 7. 優先級定義

| 優先級 | 定義 |
| --- | --- |
| `P0` | 安全風險或目前會打錯 LINE API 的問題，例如硬編碼 token、錯 host、錯 endpoint。 |
| `P1` | SDK 宣稱支援但實際不完整或未實作，例如 interface 有方法但實作是 `NotImplementedException`。 |
| `P2` | 官方功能缺漏，但不影響最基本傳訊、Webhook、Profile 等核心流程。 |
| `P3` | 進階、方案限制、低使用頻率或可延後實作的官方功能。 |

## 8. 官方分類區塊

矩陣應依以下順序分區。順序代表檢查與後續修正的建議順序。

1. Client 基礎與安全
   - Token、host 分流、HTTP client、錯誤處理、signature 驗證、硬編碼密鑰。

2. Message API
   - Reply、Push、Multicast、Narrowcast、Broadcast、Validate、Quota、Delivery count、Loading、Mark as read。

3. Content API
   - Message content、preview、transcoding / preparation status，並特別標記 `api-data.line.me` host。

4. User / Bot / Group / Room
   - Profile、bot info、group summary、member profile、member IDs、leave、member count。

5. Webhook
   - Event type、message type、source、delivery context、mode、webhookEventId、replyToken、signature、destination。

6. Message Objects
   - Text、Text v2、image、video、audio、file、location、sticker、imagemap、template、flex、quick reply、sender、mention、emoji、quote token。

7. Action Objects
   - Postback、message、URI、datetime picker、camera、camera roll、location、rich menu switch、clipboard。

8. Rich Menu
   - Rich menu CRUD、image upload/download、default、per-user、bulk、batch、alias。

9. Audience / Narrowcast Conditions
   - Audience group CRUD、upload、click/impression audience、shared audience、authority level、recipient/filter/limit models。

10. Insights / Statistics
    - Delivery、followers、demographic、message event、aggregation unit。

11. Coupon / Membership
    - 官方列出的 coupon 與 membership API。

12. OAuth / Token
    - Channel access token issue、revoke、verify、key IDs、stateless token；若官方 Messaging API 文件列出，必須納入矩陣。

## 9. 初步已知問題

目前初步檢視已觀察到以下風險。這些項目必須在矩陣中列為候選問題，再依官方文件逐項確認。

| 問題 | 初步分類 | 初步優先級 | 備註 |
| --- | --- | --- | --- |
| `LineMessagingProcessorClass.cs` 有硬編碼 Channel Access Token。 | `Unsafe` | `P0` | 抽離成通用 LINE 模組後不應保留產品 token。 |
| `LineMessagingClient.cs` 的 Audience methods 仍是 `NotImplementedException`。 | `NotImplemented` | `P1` | 目前介面宣稱支援，但實作未完成。 |
| `_uri` 預設已含 `https://api.line.me/v2`，部分方法又接 `/v2/bot/...`。 | `WrongEndpoint` | `P0` | 可能形成 `https://api.line.me/v2/v2/bot/...`。 |
| Content 與 Rich Menu content 類 API 需要檢查是否應使用 `api-data.line.me`。 | `WrongHost` 或 `NeedsOfficialVerification` | `P0` | 官方 Messaging API 對 binary content 常使用不同 host。 |
| Webhook model 可能缺新版欄位與事件。 | `Partial` 或 `Missing` | `P1` | 需確認 `webhookEventId`、`deliveryContext`、`mode` 等欄位。 |
| 既有文件宣稱完整，但程式仍有未實作與待驗證項目。 | `Partial` | `P1` | 後續文件應以官方矩陣取代泛稱「完整」。 |
| `LineMessagingProcessor` 仍直接用 RestSharp 呼叫 LINE API。 | `Partial` | `P1` | 後續需判斷它應縮小為產品層 processor，或改用 `Line.Messaging` SDK。 |

## 10. 檢查方法

後續 writing-plans 應明確規劃下列檢查步驟：

1. 逐節讀取 LINE Messaging API 官方文件。
2. 將官方項目列入矩陣，不因目前用不到而略過。
3. 讀取 `ILineMessagingClient.cs`、`LineMessagingClient.cs`、`Line.Messaging/Webhooks`、`Line.Messaging/Messages`、`Line.Messaging/LineObjects` 的目前實作。
4. 對每個官方項目填入狀態與優先級。
5. 對 `WrongEndpoint`、`WrongHost`、`Unsafe`、`NotImplemented` 的項目附上具體程式位置。
6. 對 `NeedsOfficialVerification` 的項目附上待查官方章節，不允許直接當成完成。
7. 完成矩陣後，再依矩陣產出 SDK 修正實作計畫。

## 11. 驗收標準

本設計階段完成後，應符合以下條件：

- 設計規格已寫入 `docs/superpowers/specs/2026-07-02-line-messaging-api-official-matrix-design.md`。
- 規格明確限定範圍為 LINE Messaging API 官方文件。
- 規格明確排除 LIFF、LINE Login 與 ChurchReport 專用流程。
- 規格定義完整矩陣欄位、狀態值、優先級與官方分類區塊。
- 規格記錄目前已知高風險問題，但不直接進行 SDK 實作。
- 下一步必須進入 `writing-plans`，規劃如何產出官方對照矩陣。

## 12. 後續流程

1. 使用者審閱並同意本設計規格。
2. 進入 `superpowers:writing-plans`。
3. 寫出產生官方對照矩陣的實作計畫。
4. 依計畫產出矩陣文件。
5. 完成矩陣後，再從矩陣導出 SDK 修正與補齊計畫。
