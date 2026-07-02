# LINE Messaging API 官方對照矩陣

## 1. 文件目的

本文件以 LINE Messaging API 官方文件為唯一基準，逐項比對目前 `Line.Messaging` SDK 的支援狀態。這份矩陣只做審查與分類，不修正 SDK 程式碼。

官方基準來源：

- https://developers.line.biz/en/reference/messaging-api/

## 2. 狀態值

| 狀態 | 定義 |
| --- | --- |
| `Correct` | SDK 已對應官方規格，host、path、method、payload、response model 沒有已知問題。 |
| `WrongEndpoint` | SDK 方法或類別存在，但 endpoint path 與官方規格不符。 |
| `WrongHost` | SDK 方法或類別存在，但 host 與官方規格不符。 |
| `Missing` | 官方項目存在，但 SDK 沒有對應方法、類別或 enum。 |
| `Partial` | SDK 有部分支援，但 payload、response、欄位、enum 或例外處理不完整。 |
| `NotImplemented` | 介面或方法宣稱存在，但實作仍拋出 `NotImplementedException` 或等同未完成。 |
| `Obsolete` | SDK 使用舊版官方規格、舊 endpoint、舊欄位或過時語意。 |
| `Unsafe` | 存在安全風險，例如硬編碼 Channel Access Token、錯誤 signature 驗證、未保護 secret。 |
| `NeedsOfficialVerification` | 初步看起來可疑，但必須再查官方文件細節才能判斷。 |

## 3. 優先級

| 優先級 | 定義 |
| --- | --- |
| `P0` | 安全風險或目前會打錯 LINE API 的問題。 |
| `P1` | SDK 宣稱支援但實際不完整或未實作。 |
| `P2` | 官方功能缺漏，但不影響最基本傳訊、Webhook、Profile 等核心流程。 |
| `P3` | 進階、方案限制、低使用頻率或可延後實作的官方功能。 |

## 4. 矩陣欄位

| 欄位 | 說明 |
| --- | --- |
| 官方分類 | 官方文件分類。 |
| 官方 endpoint / object | 官方 endpoint path 或 object 名稱。 |
| HTTP method | endpoint 使用的 HTTP method；非 endpoint 類項目填 `N/A`。 |
| host | 官方要求 host；非 endpoint 類項目填 `N/A`。 |
| 官方用途 | 官方功能用途摘要。 |
| 目前 SDK 對應方法/類別 | 目前 SDK 中對應的方法、類別或 enum。 |
| 目前狀態 | 固定狀態值。 |
| 問題類型 | host 錯誤、endpoint 錯誤、缺類別、欄位不完整、安全風險等。 |
| 風險等級 | `P0`、`P1`、`P2`、`P3`。 |
| 建議修正 | 下一階段 SDK 修正方向。 |

## 4.1 SDK 證據來源

本矩陣以官方文件為判斷基準，並以目前 SDK 原始碼作為實作證據。主要檢查來源如下：

- `Line.Messaging/ILineMessagingClient.cs`：確認 SDK 對外宣稱支援的方法。
- `Line.Messaging/LineMessagingClient.cs`：確認實際 host、path、HTTP method、payload 組裝與是否拋出 `NotImplementedException`。
- `Line.Messaging/Webhooks/*.cs`：確認 Webhook request、event、source、message event 與 parser 是否支援官方欄位。
- `Line.Messaging/Messages/*.cs`、`Line.Messaging/Messages/**/*.cs`：確認 message object、action object、quick reply、template、imagemap、flex、rich menu object 是否存在。
- `Line.Messaging/LineObjects/*.cs`：確認 quota、insights、audience、coupon、membership、token、webhook endpoint、bot info 等 response/request model 是否存在。
- `LineMessagingProcessor/LineMessagingProcessorClass.cs`：確認產品層 processor 是否仍有硬編碼 token 或與 SDK 重疊的直接 LINE API 呼叫。

本階段只記錄事實與風險，不修改 SDK 程式碼。任何 `WrongEndpoint`、`WrongHost`、`Unsafe`、`NotImplemented`、`Partial` 都必須附上可追蹤的檔案或方法位置，供下一階段 SDK 修正計畫使用。

## 5. 官方對照矩陣

### 5.1 Client 基礎與安全

| 官方分類 | 官方 endpoint / object | HTTP method | host | 官方用途 | 目前 SDK 對應方法/類別 | 目前狀態 | 問題類型 | 風險等級 | 建議修正 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Client 基礎與安全 | Channel Access Token storage | N/A | N/A | SDK 不應保存產品 token 或 secret。 | `LineMessagingProcessor/LineMessagingProcessorClass.cs:15-45` | `Unsafe` | 產品層 processor 保留多組註解 token，且第 45 行仍有硬編碼 Bearer token。 | `P0` | 移除硬編碼 token，改由 DI/options/secret store 注入，並清除註解中的敏感值。 |
| Client 基礎與安全 | JSON API base host | N/A | `api.line.me` | 大多數 JSON endpoint 使用 `https://api.line.me/v2`。 | `Line.Messaging/LineMessagingClient.cs:64, 88, 97, 103` | `Partial` | SDK 只有單一 `_uri`，無法清楚區分 JSON host 與 data host。 | `P0` | 下一階段拆成 `ApiBaseUri` 與 `ApiDataBaseUri`，由 typed endpoint builder 統一路徑。 |
| Client 基礎與安全 | Binary data API base host | N/A | `api-data.line.me` | Content、rich menu image、file audience upload 使用 data host。 | `GetContent*Async`, `UploadRichMenu*ImageAsync`, `DownloadRichMenuImageAsync` | `WrongHost` | SDK 以 `_uri` 呼叫 data endpoint，會打到 `api.line.me`。 | `P0` | 對 data endpoint 改用 `https://api-data.line.me/v2`。 |
| Client 基礎與安全 | Endpoint version segment | N/A | `api.line.me` | `_uri` 已含 `/v2` 時，方法不得再接 `/v2/bot/...`。 | `LineMessagingClient.cs:2128-2188, 2430-2523` | `WrongEndpoint` | Insights、Coupon、Membership 多處形成 `/v2/v2/bot/...`。 | `P0` | 統一 endpoint path 從 `/bot/...` 開始，並加 URL 組合測試。 |
| Client 基礎與安全 | Product processor direct LINE calls | N/A | `api.line.me` | 通用 SDK 應集中 LINE protocol，產品 processor 不應重複手寫 RestSharp 呼叫。 | `LineMessagingProcessorClass.cs:52-57, 186-235` | `Partial` | Processor 直接用 RestSharp 呼叫 push/profile，與 `Line.Messaging` SDK 邊界重疊。 | `P1` | 下一階段將 processor 縮成產品流程 adapter，LINE API 呼叫改走 SDK interface。 |

### 5.2 Message API

| 官方分類 | 官方 endpoint / object | HTTP method | host | 官方用途 | 目前 SDK 對應方法/類別 | 目前狀態 | 問題類型 | 風險等級 | 建議修正 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Message API | `/v2/bot/message/reply` | POST | `api.line.me` | 回覆 webhook reply token。 | `ReplyMessageAsync`, `ReplyMessageWithJsonAsync`; `LineMessagingClient.cs:359, 426` | `Partial` | endpoint 正確，但 send message model 缺 Text v2、mention、quoteToken、sender 等新版欄位。 | `P1` | 先補 message object，再將 validate/send 共用同一組 model。 |
| Message API | `/v2/bot/message/push` | POST | `api.line.me` | 主動推送給 user/group/room。 | `PushMessageAsync`, `PushMessageWithJsonAsync`; `LineMessagingClient.cs:483, 509` | `Partial` | endpoint 正確，但缺 retry key 參數與新版 message 欄位。 | `P1` | 增加 options 物件支援 retry key 與 notificationDisabled 等官方欄位。 |
| Message API | `/v2/bot/message/multicast` | POST | `api.line.me` | 對多個 user ID 推送。 | `MultiCastMessageAsync`, `MultiCastMessageWithJsonAsync`; `LineMessagingClient.cs:577, 597` | `Partial` | endpoint 正確，但缺 retry key 與完整 model 驗證。 | `P1` | 與 push/reply 共用 request options 與 message validator。 |
| Message API | `/v2/bot/message/narrowcast` | POST | `api.line.me` | 依 audience recipient/filter 群發。 | `NarrowcastMessageAsync`; `LineMessagingClient.cs:711` | `Partial` | 方法使用 `object recipient/filter/limit`，缺強型別 narrowcast condition model。 | `P1` | 建立 recipient/filter/limit 強型別模型，避免呼叫端手寫匿名物件。 |
| Message API | `/v2/bot/message/broadcast` | POST | `api.line.me` | 對所有好友廣播。 | `BroadcastMessageAsync`; `LineMessagingClient.cs:651` | `Partial` | endpoint 正確，但缺 retry key 與新版 message 欄位。 | `P1` | 與 push/multicast 共用 message request options。 |
| Message API | `/v2/bot/message/progress/narrowcast` | GET | `api.line.me` | 查詢 narrowcast 處理進度。 | `GetNarrowcastProgressAsync`; `LineMessagingClient.cs:745` | `Correct` | 無已知 endpoint 問題。 | `P2` | 保留並補測 query string encoding。 |
| Message API | `/v2/bot/chat/loading/start` | POST | `api.line.me` | 在聊天畫面顯示 loading animation。 | `ShowLoadingAnimationAsync`; `LineMessagingClient.cs:817-818` | `Correct` | endpoint 與 payload 欄位符合目前官方語意。 | `P2` | 補 loadingSeconds 範圍測試。 |
| Message API | `/v2/bot/chat/markAsRead` | POST | `api.line.me` | 用 markAsReadToken 將訊息標記為已讀。 | `MarkAsReadAsync`; `LineMessagingClient.cs:776-781` | `WrongEndpoint` | SDK 呼叫 `/bot/message/markAsRead` 且 payload 是 `chatId`，官方 endpoint 是 `/bot/chat/markAsRead` 並使用 `markAsReadToken`。 | `P0` | 改 API 簽章為 token-based，保留舊 chatId overload 時標為 obsolete 或移除。 |
| Message API | `/v2/bot/message/quota` | GET | `api.line.me` | 取得本月訊息配額。 | `GetMessageQuotaAsync`; `LineMessagingClient.cs:2394` | `Correct` | 無已知 endpoint 問題。 | `P2` | 補 response model 測試。 |
| Message API | `/v2/bot/message/quota/consumption` | GET | `api.line.me` | 取得本月已送訊息數。 | `GetMessageQuotaConsumptionAsync`; `LineMessagingClient.cs:2418` | `Correct` | 無已知 endpoint 問題。 | `P2` | 補 response model 測試。 |
| Message API | `/v2/bot/message/delivery/reply` | GET | `api.line.me` | 取得 reply message 送出數。 | `GetNumberOfSentReplyMessagesAsync`; `LineMessagingClient.cs:2300` | `Correct` | 無已知 endpoint 問題。 | `P2` | 補 date 格式與 status model 測試。 |
| Message API | `/v2/bot/message/delivery/push` | GET | `api.line.me` | 取得 push message 送出數。 | `GetNumberOfSentPushMessagesAsync`; `LineMessagingClient.cs:2328` | `Correct` | 無已知 endpoint 問題。 | `P2` | 補 date 格式與 status model 測試。 |
| Message API | `/v2/bot/message/delivery/multicast` | GET | `api.line.me` | 取得 multicast message 送出數。 | `GetNumberOfSentMulticastMessagesAsync`; `LineMessagingClient.cs:2356` | `Correct` | 無已知 endpoint 問題。 | `P2` | 補 date 格式與 status model 測試。 |
| Message API | `/v2/bot/message/delivery/broadcast` | GET | `api.line.me` | 取得 broadcast message 送出數。 | `GetNumberOfSentBroadcastMessagesAsync`; `LineMessagingClient.cs:2272` | `Correct` | 無已知 endpoint 問題。 | `P2` | 補 date 格式與 status model 測試。 |
| Message API | `/v2/bot/message/validate/reply` | POST | `api.line.me` | 驗證 reply message objects。 | `ValidateReplyMessageAsync`; `LineMessagingClient.cs:2545` | `Partial` | endpoint 正確，但受限於 SDK message model 不完整。 | `P1` | message model 補齊後共用 serializer 測試。 |
| Message API | `/v2/bot/message/validate/push` | POST | `api.line.me` | 驗證 push message objects。 | `ValidatePushMessageAsync`; `LineMessagingClient.cs:2561` | `Partial` | endpoint 正確，但受限於 SDK message model 不完整。 | `P1` | message model 補齊後共用 serializer 測試。 |
| Message API | `/v2/bot/message/validate/multicast` | POST | `api.line.me` | 驗證 multicast message objects。 | `ValidateMulticastMessageAsync`; `LineMessagingClient.cs:2577` | `Partial` | endpoint 正確，但受限於 SDK message model 不完整。 | `P1` | message model 補齊後共用 serializer 測試。 |
| Message API | `/v2/bot/message/validate/narrowcast` | POST | `api.line.me` | 驗證 narrowcast message objects。 | `ValidateNarrowcastMessageAsync`; `LineMessagingClient.cs:2593` | `Partial` | endpoint 正確，但受限於 SDK message model 不完整。 | `P1` | message model 補齊後共用 serializer 測試。 |
| Message API | `/v2/bot/message/validate/broadcast` | POST | `api.line.me` | 驗證 broadcast message objects。 | `ValidateBroadcastMessageAsync`; `LineMessagingClient.cs:2609` | `Partial` | endpoint 正確，但受限於 SDK message model 不完整。 | `P1` | message model 補齊後共用 serializer 測試。 |

### 5.3 Content API

| 官方分類 | 官方 endpoint / object | HTTP method | host | 官方用途 | 目前 SDK 對應方法/類別 | 目前狀態 | 問題類型 | 風險等級 | 建議修正 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Content API | `/v2/bot/message/{messageId}/content` | GET | `api-data.line.me` | 下載使用者傳送的圖片、影片、音訊或檔案。 | `GetContentStreamAsync`, `GetContentBytesAsync`; `LineMessagingClient.cs:872, 905` | `WrongHost` | SDK 使用 `_uri`，預設會打到 `api.line.me`。 | `P0` | 改用 `ApiDataBaseUri`，保留 stream/bytes 兩種讀取方式。 |
| Content API | `/v2/bot/message/{messageId}/content/transcoding` | GET | `api-data.line.me` | 查詢影片或音訊是否可下載。 | `VerifyContentPreparationAsync`; `LineMessagingClient.cs:948` | `WrongEndpoint` | SDK path 是 `/content/verify`，官方 path 是 `/content/transcoding`，且 host 也錯。 | `P0` | 改名或保留方法但修正 path，response 依官方 `status` model 建強型別。 |
| Content API | `/v2/bot/message/{messageId}/content/preview` | GET | `api-data.line.me` | 取得圖片或影片預覽圖。 | `GetContentPreviewAsync`; `LineMessagingClient.cs:1009` | `WrongHost` | SDK 使用 `_uri`，預設會打到 `api.line.me`。 | `P0` | 改用 `ApiDataBaseUri`。 |

### 5.4 User / Bot / Group / Room

| 官方分類 | 官方 endpoint / object | HTTP method | host | 官方用途 | 目前 SDK 對應方法/類別 | 目前狀態 | 問題類型 | 風險等級 | 建議修正 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| User / Bot / Group / Room | `/v2/bot/profile/{userId}` | GET | `api.line.me` | 取得 user profile。 | `GetUserProfileAsync`; `LineMessagingClient.cs:1056` | `Correct` | 無已知 endpoint 問題。 | `P2` | 補 response model 欄位測試。 |
| User / Bot / Group / Room | `/v2/bot/info` | GET | `api.line.me` | 取得 bot basic info。 | `GetBotInfoAsync`; `LineMessagingClient.cs:1102` | `Correct` | 無已知 endpoint 問題。 | `P2` | 補 bot info 欄位測試。 |
| User / Bot / Group / Room | `/v2/bot/followers/ids` | GET | `api.line.me` | 取得好友 user ID 清單。 | None | `Missing` | SDK 無對應方法。 | `P2` | 新增 follower IDs 方法與 continuation token model。 |
| User / Bot / Group / Room | `/v2/bot/group/{groupId}/summary` | GET | `api.line.me` | 取得 group summary。 | `GetGroupSummaryAsync`; `LineMessagingClient.cs:1282` | `Correct` | 無已知 endpoint 問題。 | `P2` | 補 response model 測試。 |
| User / Bot / Group / Room | `/v2/bot/group/{groupId}/member/{userId}` | GET | `api.line.me` | 取得 group member profile。 | `GetGroupMemberProfileAsync`; `LineMessagingClient.cs:1142` | `Correct` | 無已知 endpoint 問題。 | `P2` | 補 response model 測試。 |
| User / Bot / Group / Room | `/v2/bot/group/{groupId}/members/ids` | GET | `api.line.me` | 取得 group member user IDs。 | `GetGroupMemberIdsAsync`; `LineMessagingClient.cs:1196` | `Correct` | 無已知 endpoint 問題。 | `P2` | 補 continuation token 測試。 |
| User / Bot / Group / Room | `/v2/bot/group/{groupId}/members/count` | GET | `api.line.me` | 取得 group 人數。 | `GetGroupMemberCountAsync`; `LineMessagingClient.cs:1310` | `Correct` | 無已知 endpoint 問題。 | `P2` | 補 response model 測試。 |
| User / Bot / Group / Room | `/v2/bot/group/{groupId}/leave` | POST | `api.line.me` | 離開 group。 | `LeaveFromGroupAsync`; `LineMessagingClient.cs:1341` | `Correct` | 無已知 endpoint 問題。 | `P2` | 補 status code 測試。 |
| User / Bot / Group / Room | `/v2/bot/room/{roomId}/member/{userId}` | GET | `api.line.me` | 取得 room member profile。 | `GetRoomMemberProfileAsync`; `LineMessagingClient.cs:1381` | `Correct` | 無已知 endpoint 問題。 | `P2` | 補 response model 測試。 |
| User / Bot / Group / Room | `/v2/bot/room/{roomId}/members/ids` | GET | `api.line.me` | 取得 room member user IDs。 | `GetRoomMemberIdsAsync`; `LineMessagingClient.cs:1428` | `Correct` | 無已知 endpoint 問題。 | `P2` | 補 continuation token 測試。 |
| User / Bot / Group / Room | `/v2/bot/room/{roomId}/members/count` | GET | `api.line.me` | 取得 room 人數。 | `GetRoomMemberCountAsync`; `LineMessagingClient.cs:1495` | `Correct` | 無已知 endpoint 問題。 | `P2` | 補 response model 測試。 |
| User / Bot / Group / Room | `/v2/bot/room/{roomId}/leave` | POST | `api.line.me` | 離開 room。 | `LeaveFromRoomAsync`; `LineMessagingClient.cs:1526` | `Correct` | 無已知 endpoint 問題。 | `P2` | 補 status code 測試。 |
| User / Bot / Group / Room | `/v2/bot/user/{userId}/linkToken` | POST | `api.line.me` | 產生 account link token。 | `IssueLinkTokenAsync`; `LineMessagingClient.cs:2234` | `Correct` | 無已知 endpoint 問題。 | `P2` | 補 response 欄位測試。 |

### 5.5 Webhook

| 官方分類 | 官方 endpoint / object | HTTP method | host | 官方用途 | 目前 SDK 對應方法/類別 | 目前狀態 | 問題類型 | 風險等級 | 建議修正 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Webhook | `/v2/bot/channel/webhook/endpoint` | PUT | `api.line.me` | 設定 webhook endpoint URL。 | `SetWebhookEndpointAsync`; `ILineMessagingClient.cs:327`, `LineMessagingClient.cs:1564` | `Correct` | endpoint、method 與 payload 均符合官方 set webhook endpoint 規格。 | `P2` | 保留現有實作，補 URL payload 與狀態碼 regression test。 |
| Webhook | `/v2/bot/channel/webhook/endpoint` | GET | `api.line.me` | 取得目前 webhook endpoint 資訊。 | `GetWebhookEndpointAsync`; `ILineMessagingClient.cs:334`, `LineMessagingClient.cs:1601` | `Correct` | endpoint 與 method 符合官方 get webhook endpoint 規格。 | `P2` | 保留現有實作，補 `WebhookEndpoint` response model 測試。 |
| Webhook | `/v2/bot/channel/webhook/test` | POST | `api.line.me` | 測試 webhook endpoint 是否可被 LINE 平台呼叫。 | `TestWebhookEndpointAsync`; `ILineMessagingClient.cs:342`, `LineMessagingClient.cs:1641` | `Correct` | endpoint、method 與 optional endpoint payload 均符合官方 test webhook endpoint 規格。 | `P2` | 保留現有實作，補 empty body 與指定 endpoint 兩種 payload regression test。 |
| Webhook | `X-Line-Signature` verification | N/A | N/A | 驗證 webhook request 是否由 LINE platform 發送。 | `WebhookRequestMessageHelper.GetWebhookEventsAsync`, `VerifySignature`; `WebhookRequestMessageHelper.cs:29-68` | `Correct` | 已讀取 `X-Line-Signature`，使用 channel secret 做 HMAC-SHA256，並以 constant-time compare 驗證。 | `P2` | 保留驗章流程，補正確簽章、錯誤簽章、缺 header 的 parser 測試。 |
| Webhook | Webhook request `destination` | N/A | N/A | 驗證 webhook 目標 bot user ID。 | `WebhookRequestMessageHelper.cs:39` | `Partial` | helper 會比對 destination，但沒有公開保留完整 request envelope。 | `P1` | 建立 `WebhookRequest` model，保留 destination 與 events。 |
| Webhook | Webhook request `events` | N/A | N/A | 承載 webhook event 陣列。 | `WebhookEventParser`, `WebhookApplication` | `Partial` | parser 直接輸出 event list，request-level metadata 不完整。 | `P1` | 建立 request envelope parser。 |
| Webhook | `webhookEventId` | N/A | N/A | webhook event 唯一 ID。 | None | `Missing` | `WebhookEvent` 只有 Type/Source/Timestamp。 | `P1` | 在 base event model 加入 nullable `WebhookEventId`。 |
| Webhook | `deliveryContext` | N/A | N/A | 標示 event 是否為 redelivery。 | None | `Missing` | SDK 不保留 deliveryContext。 | `P1` | 加入 `DeliveryContext` model。 |
| Webhook | `mode` | N/A | N/A | active/standby bot mode。 | None | `Missing` | SDK 不保留 mode。 | `P1` | 在 base event model 加入 `Mode` enum/string。 |
| Webhook | `replyToken` | N/A | N/A | 可回覆事件的 reply token。 | `ReplyableEvent.cs:5-10` | `Correct` | replyable event 有保留 replyToken。 | `P2` | 保留並補序列化測試。 |
| Webhook | `markAsReadToken` | N/A | N/A | 官方 mark-as-read endpoint 使用的 token。 | None | `Missing` | SDK 沒有解析 markAsReadToken，導致 `MarkAsReadAsync` 只能用錯誤 chatId 語意。 | `P0` | webhook event model 與 mark-as-read API 同步改為 token-based。 |
| Webhook | `source` | N/A | N/A | user/group/room source。 | `WebhookEventSource.cs`, `WebhookEvent.cs:38` | `Correct` | 基本 source model 存在。 | `P2` | 補 source type 與 id 欄位測試。 |
| Webhook | `timestamp` | N/A | N/A | event 發生時間。 | `WebhookEvent.cs:23-31` | `Correct` | 基本 timestamp 存在。 | `P2` | 保留。 |
| Webhook | message event | N/A | N/A | 使用者傳訊 event。 | `MessageEvent.cs`, `EventMessage.cs` | `Partial` | 支援基本 message event，但缺新版 common fields。 | `P1` | base event 補欄位後共用。 |
| Webhook | follow/unfollow/join/leave/member joined/member left/postback/beacon/account link/things event | N/A | N/A | 官方舊核心 event families。 | `WebhookEventType.cs`, `WebhookEvent.cs:49-99` | `Correct` | 舊核心 event family 均有 parser 分支。 | `P2` | 補 parser regression tests。 |
| Webhook | unsend event | N/A | N/A | 使用者收回訊息 event。 | None | `Missing` | `WebhookEventType` 無 unsend。 | `P1` | 新增 unsend event model。 |
| Webhook | video viewing complete event | N/A | N/A | 影片觀看完成 event。 | None | `Missing` | `WebhookEventType` 無 video viewing complete。 | `P1` | 新增 video viewing complete event model。 |
| Webhook | membership event | N/A | N/A | 會員狀態相關 event。 | None | `Missing` | `WebhookEventType` 無 membership。 | `P1` | 新增 membership event model。 |

### 5.6 Message Objects

| 官方分類 | 官方 endpoint / object | HTTP method | host | 官方用途 | 目前 SDK 對應方法/類別 | 目前狀態 | 問題類型 | 風險等級 | 建議修正 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Message Objects | Text message | N/A | N/A | 傳送文字。 | `TextMessage.cs` | `Partial` | 基本 `text` 有支援，但無 `emojis`、`quoteToken`、`sender`。 | `P1` | 補 Text v1 optional fields。 |
| Message Objects | Text message v2 | N/A | N/A | 新版文字訊息與 decorators。 | None | `Missing` | SDK 無 Text v2 model。 | `P2` | 新增 Text v2 model，避免與舊 TextMessage 混用。 |
| Message Objects | Sticker message | N/A | N/A | 傳送 sticker。 | `StickerMessage.cs` | `Partial` | 基本 packageId/stickerId 有支援，但缺官方新版 optional 欄位檢查。 | `P2` | 補官方欄位與 validate tests。 |
| Message Objects | Image message | N/A | N/A | 傳送圖片。 | `ImageMessage.cs` | `Partial` | 基本 URL 有支援，但無 quoteToken/sender。 | `P1` | 透過 common send message base 補 quoteToken/sender。 |
| Message Objects | Video message | N/A | N/A | 傳送影片。 | `VideoMessage.cs` | `Partial` | 基本 URL 有支援，但無 trackingId、quoteToken、sender。 | `P1` | 補 video-specific optional fields。 |
| Message Objects | Audio message | N/A | N/A | 傳送音訊。 | `AudioMessage.cs` | `Partial` | 基本 URL/duration 有支援，但無 quoteToken/sender。 | `P1` | 透過 common send message base 補 quoteToken/sender。 |
| Message Objects | Location message | N/A | N/A | 傳送位置。 | `LocationMessage.cs` | `Partial` | 基本欄位有支援，但無 quoteToken/sender。 | `P1` | 透過 common send message base 補 quoteToken/sender。 |
| Message Objects | Imagemap message | N/A | N/A | 傳送 imagemap。 | `ImagemapMessage.cs`, `Messages/Imagemap/*.cs` | `Partial` | 物件存在，但需要依官方最新欄位重新核對。 | `P2` | 補欄位完整性測試。 |
| Message Objects | Template message | N/A | N/A | 傳送 buttons/confirm/carousel/image carousel template。 | `TemplateMessage.cs`, `Messages/Template/*.cs` | `Partial` | 核心 template 存在，但受 action object 欄位完整性影響。 | `P2` | 補 template/action 組合測試。 |
| Message Objects | Flex message | N/A | N/A | 傳送 flex bubble/carousel。 | `FlexMessage.cs`, `Messages/Flex/*.cs` | `Partial` | 基本 container 存在，但需核對最新版 Flex schema。 | `P2` | 將 Flex schema 分層成可測 model。 |
| Message Objects | Quick reply | N/A | N/A | 附加 quick reply buttons。 | `QuickReply.cs`, `QuickItem.cs` | `Partial` | 基本 items 存在，但 action 欄位仍需補完整。 | `P2` | 與 action object 補測。 |
| Message Objects | Sender | N/A | N/A | 自訂 sender name/icon。 | None | `Missing` | send message interface 無 sender common property。 | `P1` | 建立 common send message base 或 composition model。 |
| Message Objects | Mention | N/A | N/A | Text message mention metadata。 | None | `Missing` | 無 mention model。 | `P1` | 補 mention/emojis 與 text decorator model。 |
| Message Objects | Emoji | N/A | N/A | LINE emoji metadata。 | None | `Missing` | 無 emoji model，只能傳 Unicode text。 | `P2` | 補 emojis 陣列 model。 |
| Message Objects | Quote token | N/A | N/A | 引用訊息 token。 | None | `Missing` | send message model 無 quoteToken。 | `P1` | 在可引用 message model 補 `QuoteToken`。 |
| Message Objects | File message event object | N/A | N/A | Webhook file event message。 | `FileEventMessage.cs` | `Correct` | 基本 webhook file event model 存在。 | `P2` | 補 parser 測試。 |

### 5.7 Action Objects

| 官方分類 | 官方 endpoint / object | HTTP method | host | 官方用途 | 目前 SDK 對應方法/類別 | 目前狀態 | 問題類型 | 風險等級 | 建議修正 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Action Objects | Postback action | N/A | N/A | 回傳 postback data。 | `PostbackTemplateAction.cs` | `Partial` | 物件存在，但需核對 displayText/inputOption/fillInText 等新版欄位。 | `P2` | 補完整欄位與序列化測試。 |
| Action Objects | Message action | N/A | N/A | 送出固定文字。 | `MessageTemplateAction.cs` | `Correct` | 基本 action 存在。 | `P2` | 補序列化測試。 |
| Action Objects | URI action | N/A | N/A | 開啟 URI。 | `UriTemplateAction.cs` | `Partial` | 物件存在，但需核對 altUri 等欄位。 | `P2` | 補官方欄位。 |
| Action Objects | Datetime picker action | N/A | N/A | 開啟日期時間選擇器。 | `DateTimePickerTemplateAction.cs`, `DateTimePickerMode.cs` | `Correct` | 基本 action 存在。 | `P2` | 補 mode/date format 測試。 |
| Action Objects | Camera action | N/A | N/A | quick reply 開相機。 | `CameraTemplateAction.cs` | `Correct` | 基本 action 存在。 | `P2` | 補序列化測試。 |
| Action Objects | Camera roll action | N/A | N/A | quick reply 開相簿。 | `CameraRollTemplateAction.cs` | `Correct` | 基本 action 存在。 | `P2` | 補序列化測試。 |
| Action Objects | Location action | N/A | N/A | quick reply 開位置選擇。 | `LocationTemplateAction.cs` | `Correct` | 基本 action 存在。 | `P2` | 補序列化測試。 |
| Action Objects | Rich menu switch action | N/A | N/A | 切換 rich menu alias。 | `RichMenuSwitchTemplateAction.cs` | `Correct` | 基本 action 存在。 | `P2` | 補序列化測試。 |
| Action Objects | Clipboard action | N/A | N/A | 複製文字到剪貼簿。 | `ClipboardTemplateAction.cs` | `Correct` | 基本 action 存在。 | `P2` | 補序列化測試。 |

### 5.8 Rich Menu

| 官方分類 | 官方 endpoint / object | HTTP method | host | 官方用途 | 目前 SDK 對應方法/類別 | 目前狀態 | 問題類型 | 風險等級 | 建議修正 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Rich Menu | `/v2/bot/richmenu` | POST | `api.line.me` | 建立 rich menu。 | `CreateRichMenuAsync`; `LineMessagingClient.cs:1733` | `Correct` | 無已知 endpoint 問題。 | `P2` | 補 rich menu object 欄位測試。 |
| Rich Menu | `/v2/bot/richmenu/validate` | POST | `api.line.me` | 驗證 rich menu object。 | `ValidateRichMenuAsync`; `LineMessagingClient.cs:1776` | `Correct` | 無已知 endpoint 問題。 | `P2` | 補欄位測試。 |
| Rich Menu | `/v2/bot/richmenu/{richMenuId}/content` | POST | `api-data.line.me` | 上傳 rich menu image。 | `UploadRichMenuJpegImageAsync`, `UploadRichMenuPngImageAsync`; `LineMessagingClient.cs:2081, 2093` | `WrongHost` | SDK 使用 `_uri`，預設會打到 `api.line.me`。 | `P0` | 改用 `ApiDataBaseUri`。 |
| Rich Menu | `/v2/bot/richmenu/{richMenuId}/content` | GET | `api-data.line.me` | 下載 rich menu image。 | `DownloadRichMenuImageAsync`; `LineMessagingClient.cs:2068` | `WrongHost` | SDK 使用 `_uri`，預設會打到 `api.line.me`。 | `P0` | 改用 `ApiDataBaseUri`。 |
| Rich Menu | `/v2/bot/richmenu/list` | GET | `api.line.me` | 取得 rich menu list。 | `GetRichMenuListAsync`; `LineMessagingClient.cs:2103` | `Correct` | 無已知 endpoint 問題。 | `P2` | 補 response model 測試。 |
| Rich Menu | `/v2/bot/richmenu/{richMenuId}` | GET | `api.line.me` | 取得 rich menu。 | `GetRichMenuAsync`; `LineMessagingClient.cs:1679` | `Correct` | 無已知 endpoint 問題。 | `P2` | 補 response model 測試。 |
| Rich Menu | `/v2/bot/richmenu/{richMenuId}` | DELETE | `api.line.me` | 刪除 rich menu。 | `DeleteRichMenuAsync`; `LineMessagingClient.cs:1808` | `Correct` | 無已知 endpoint 問題。 | `P2` | 補 status code 測試。 |
| Rich Menu | `/v2/bot/user/all/richmenu/{richMenuId}` | POST | `api.line.me` | 設定 default rich menu。 | `SetDefaultRichMenuAsync`; `LineMessagingClient.cs:1960` | `Correct` | 無已知 endpoint 問題。 | `P2` | 補 status code 測試。 |
| Rich Menu | `/v2/bot/user/all/richmenu` | GET | `api.line.me` | 取得 default rich menu ID。 | `GetDefaultRichMenuIdAsync`; `LineMessagingClient.cs:1970` | `Correct` | 無已知 endpoint 問題。 | `P2` | 補 response model 測試。 |
| Rich Menu | `/v2/bot/user/all/richmenu` | DELETE | `api.line.me` | 清除 default rich menu。 | `CancelDefaultRichMenuAsync`; `LineMessagingClient.cs:1980` | `Correct` | 無已知 endpoint 問題。 | `P2` | 補 status code 測試。 |
| Rich Menu | `/v2/bot/user/{userId}/richmenu/{richMenuId}` | POST | `api.line.me` | 將 rich menu link 到 user。 | `LinkRichMenuToUserAsync`; `LineMessagingClient.cs:1990` | `Correct` | 無已知 endpoint 問題。 | `P2` | 補 status code 測試。 |
| Rich Menu | `/v2/bot/richmenu/bulk/link` | POST | `api.line.me` | 將 rich menu link 到多個 users。 | `LinkRichMenuToUsersAsync`; `LineMessagingClient.cs:2000` | `Correct` | 無已知 endpoint 問題。 | `P2` | 補 request model 測試。 |
| Rich Menu | `/v2/bot/user/{userId}/richmenu` | GET | `api.line.me` | 取得 user 的 rich menu ID。 | `GetRichMenuIdOfUserAsync`; `LineMessagingClient.cs:1950` | `Correct` | 無已知 endpoint 問題。 | `P2` | 補 response model 測試。 |
| Rich Menu | `/v2/bot/user/{userId}/richmenu` | DELETE | `api.line.me` | 解除 user rich menu。 | `UnLinkRichMenuFromUserAsync`; `LineMessagingClient.cs:2012` | `Correct` | 無已知 endpoint 問題。 | `P2` | 補 status code 測試。 |
| Rich Menu | `/v2/bot/richmenu/bulk/unlink` | POST | `api.line.me` | 解除多個 users rich menu。 | `UnLinkRichMenuFromUsersAsync`; `LineMessagingClient.cs:2022` | `Correct` | 無已知 endpoint 問題。 | `P2` | 補 request model 測試。 |
| Rich Menu | `/v2/bot/richmenu/batch` | POST | `api.line.me` | 批次控制 rich menu。 | `RichMenuBatchOperationAsync`; `LineMessagingClient.cs:2034` | `Correct` | 無已知 endpoint 問題。 | `P2` | 補 request model 測試。 |
| Rich Menu | `/v2/bot/richmenu/progress/batch` | GET | `api.line.me` | 取得批次控制進度。 | `GetRichMenuBatchProgressAsync`; `LineMessagingClient.cs:2046` | `WrongEndpoint` | SDK path 是 `/bot/richmenu/batch/{requestId}`，官方是 progress query endpoint。 | `P0` | 修正 path 與 requestId query 參數。 |
| Rich Menu | `/v2/bot/richmenu/validate/batch` | POST | `api.line.me` | 驗證批次控制 request。 | `ValidateRichMenuBatchRequestAsync`; `LineMessagingClient.cs:2056` | `WrongEndpoint` | SDK path 是 `/bot/richmenu/batch/validate`，官方 path 順序不同。 | `P0` | 修正 path 並加 URL 組合測試。 |
| Rich Menu | `/v2/bot/richmenu/alias` | POST | `api.line.me` | 建立 rich menu alias。 | `CreateRichMenuAliasAsync`; `LineMessagingClient.cs:1842` | `Correct` | 無已知 endpoint 問題。 | `P2` | 補 request model 測試。 |
| Rich Menu | `/v2/bot/richmenu/alias/{richMenuAliasId}` | DELETE | `api.line.me` | 刪除 rich menu alias。 | `DeleteRichMenuAliasAsync`; `LineMessagingClient.cs:1869` | `Correct` | 無已知 endpoint 問題。 | `P2` | 補 status code 測試。 |
| Rich Menu | `/v2/bot/richmenu/alias/{richMenuAliasId}` | POST | `api.line.me` | 更新 rich menu alias。 | `UpdateRichMenuAliasAsync`; `LineMessagingClient.cs:1898` | `Correct` | 無已知 endpoint 問題。 | `P2` | 補 request model 測試。 |
| Rich Menu | `/v2/bot/richmenu/alias/{richMenuAliasId}` | GET | `api.line.me` | 取得 rich menu alias。 | `GetRichMenuAliasAsync`; `LineMessagingClient.cs:1930` | `Correct` | 無已知 endpoint 問題。 | `P2` | 補 response model 測試。 |
| Rich Menu | `/v2/bot/richmenu/alias/list` | GET | `api.line.me` | 取得 rich menu alias list。 | `GetRichMenuAliasListAsync`; `LineMessagingClient.cs:1940` | `Correct` | 無已知 endpoint 問題。 | `P2` | 補 response model 測試。 |

### 5.9 Audience / Narrowcast Conditions

| 官方分類 | 官方 endpoint / object | HTTP method | host | 官方用途 | 目前 SDK 對應方法/類別 | 目前狀態 | 問題類型 | 風險等級 | 建議修正 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Audience / Narrowcast Conditions | `/v2/bot/audienceGroup/upload` | POST | `api.line.me` | 建立 upload audience group。 | `CreateUploadAudienceGroupAsync`; `LineMessagingClient.cs:2623` | `NotImplemented` | 介面存在，實作拋 `NotImplementedException`。 | `P1` | 實作 JSON upload audience endpoint。 |
| Audience / Narrowcast Conditions | `/v2/bot/audienceGroup/upload/byFile` | POST | `api-data.line.me` | 用檔案建立 upload audience group。 | `CreateUploadAudienceGroupByFileAsync`; `LineMessagingClient.cs:2632` | `NotImplemented` | 介面存在，實作拋 `NotImplementedException`，且需 data host。 | `P1` | 實作 multipart/file upload 並使用 `ApiDataBaseUri`。 |
| Audience / Narrowcast Conditions | `/v2/bot/audienceGroup/upload` add | PUT | `api.line.me` | 對 audience group 新增 audience。 | `AddAudienceToGroupAsync`; `LineMessagingClient.cs:2641` | `NotImplemented` | 介面存在，實作拋 `NotImplementedException`。 | `P1` | 實作 add audience endpoint。 |
| Audience / Narrowcast Conditions | `/v2/bot/audienceGroup/upload/byFile` add | PUT | `api-data.line.me` | 用檔案新增 audience。 | `AddAudienceToGroupByFileAsync`; `LineMessagingClient.cs:2650` | `NotImplemented` | 介面存在，實作拋 `NotImplementedException`，且需 data host。 | `P1` | 實作 file add endpoint。 |
| Audience / Narrowcast Conditions | `/v2/bot/audienceGroup/click` | POST | `api.line.me` | 建立 click audience。 | `CreateClickAudienceGroupAsync`; `LineMessagingClient.cs:2659` | `NotImplemented` | 介面存在，實作拋 `NotImplementedException`。 | `P1` | 實作 click audience endpoint。 |
| Audience / Narrowcast Conditions | `/v2/bot/audienceGroup/imp` | POST | `api.line.me` | 建立 impression audience。 | `CreateImpAudienceGroupAsync`; `LineMessagingClient.cs:2668` | `NotImplemented` | 介面存在，實作拋 `NotImplementedException`。 | `P1` | 實作 impression audience endpoint。 |
| Audience / Narrowcast Conditions | `/v2/bot/audienceGroup/{audienceGroupId}/updateDescription` | PUT | `api.line.me` | 更新 audience group 描述。 | `UpdateAudienceGroupDescriptionAsync`; `LineMessagingClient.cs:2677` | `NotImplemented` | 介面存在，實作拋 `NotImplementedException`。 | `P1` | 實作 update description endpoint。 |
| Audience / Narrowcast Conditions | `/v2/bot/audienceGroup/{audienceGroupId}` | DELETE | `api.line.me` | 刪除 audience group。 | `DeleteAudienceGroupAsync`; `LineMessagingClient.cs:2686` | `NotImplemented` | 介面存在，實作拋 `NotImplementedException`。 | `P1` | 實作 delete endpoint。 |
| Audience / Narrowcast Conditions | `/v2/bot/audienceGroup/{audienceGroupId}` | GET | `api.line.me` | 取得 audience group。 | `GetAudienceGroupAsync`; `LineMessagingClient.cs:2695` | `NotImplemented` | 介面存在，實作拋 `NotImplementedException`。 | `P1` | 實作 get endpoint。 |
| Audience / Narrowcast Conditions | `/v2/bot/audienceGroup/list` | GET | `api.line.me` | 取得 audience group list。 | `GetAudienceGroupsAsync`; `LineMessagingClient.cs:2704` | `NotImplemented` | 介面存在，實作拋 `NotImplementedException`。 | `P1` | 實作 list endpoint。 |
| Audience / Narrowcast Conditions | `/v2/bot/audienceGroup/authorityLevel` | GET | `api.line.me` | 取得 authority level。 | `GetAudienceGroupAuthorityLevelAsync`; `LineMessagingClient.cs:2713` | `NotImplemented` | 介面存在，實作拋 `NotImplementedException`。 | `P1` | 實作 authority endpoint。 |
| Audience / Narrowcast Conditions | `/v2/bot/audienceGroup/authorityLevel` | PUT | `api.line.me` | 更新 authority level。 | `ChangeAudienceGroupAuthorityLevelAsync`; `LineMessagingClient.cs:2722` | `NotImplemented` | 介面存在，實作拋 `NotImplementedException`。 | `P1` | 實作 authority update endpoint。 |
| Audience / Narrowcast Conditions | Narrowcast recipient/filter/limit objects | N/A | N/A | narrowcast 條件模型。 | `NarrowcastMessageAsync(... object recipient, object filter, object limit ...)` | `Partial` | 方法接受 `object`，沒有 official strong types。 | `P1` | 建立 recipient/filter/limit model 與 serializer tests。 |

### 5.10 Insights / Statistics

| 官方分類 | 官方 endpoint / object | HTTP method | host | 官方用途 | 目前 SDK 對應方法/類別 | 目前狀態 | 問題類型 | 風險等級 | 建議修正 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Insights / Statistics | `/v2/bot/insight/message/delivery` | GET | `api.line.me` | 取得訊息送達統計。 | `GetMessageDeliveryAsync`; `LineMessagingClient.cs:2128` | `WrongEndpoint` | `_uri` 已含 `/v2` 又接 `/v2/bot/...`。 | `P0` | 改 path 為 `/bot/insight/message/delivery`。 |
| Insights / Statistics | `/v2/bot/insight/followers` | GET | `api.line.me` | 取得 followers 統計。 | `GetFollowerStatisticsAsync`; `LineMessagingClient.cs:2138` | `WrongEndpoint` | `_uri` 已含 `/v2` 又接 `/v2/bot/...`。 | `P0` | 改 path 為 `/bot/insight/followers`。 |
| Insights / Statistics | `/v2/bot/insight/demographic` | GET | `api.line.me` | 取得好友 demographic。 | `GetFriendDemographicsAsync`; `LineMessagingClient.cs:2148` | `WrongEndpoint` | `_uri` 已含 `/v2` 又接 `/v2/bot/...`。 | `P0` | 改 path 為 `/bot/insight/demographic`。 |
| Insights / Statistics | `/v2/bot/insight/message/event` | GET | `api.line.me` | 取得 user interaction statistics。 | `GetUserInteractionStatisticsAsync`; `LineMessagingClient.cs:2158` | `WrongEndpoint` | `_uri` 已含 `/v2` 又接 `/v2/bot/...`。 | `P0` | 改 path 為 `/bot/insight/message/event`。 |
| Insights / Statistics | `/v2/bot/insight/message/event/aggregation` | GET | `api.line.me` | 取得 aggregation unit statistics。 | `GetStatisticsPerUnitAsync`; `LineMessagingClient.cs:2168` | `WrongEndpoint` | `_uri` 已含 `/v2` 又接 `/v2/bot/...`。 | `P0` | 改 path 為 `/bot/insight/message/event/aggregation`。 |
| Insights / Statistics | `/v2/bot/message/aggregation/info` | GET | `api.line.me` | 取得 aggregation info。 | `GetAggregationInfoAsync`; `LineMessagingClient.cs:2178` | `WrongEndpoint` | `_uri` 已含 `/v2` 又接 `/v2/bot/...`。 | `P0` | 改 path 為 `/bot/message/aggregation/info`。 |
| Insights / Statistics | `/v2/bot/message/aggregation/list` | GET | `api.line.me` | 取得 aggregation unit names。 | `GetAggregationUnitNameListAsync`; `LineMessagingClient.cs:2188` | `WrongEndpoint` | `_uri` 已含 `/v2` 又接 `/v2/bot/...`。 | `P0` | 改 path 為 `/bot/message/aggregation/list`。 |

### 5.11 Coupon / Membership

| 官方分類 | 官方 endpoint / object | HTTP method | host | 官方用途 | 目前 SDK 對應方法/類別 | 目前狀態 | 問題類型 | 風險等級 | 建議修正 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Coupon / Membership | `/v2/bot/coupon` | POST | `api.line.me` | 建立 coupon。 | `CreateCouponAsync`; `LineMessagingClient.cs:2430` | `WrongEndpoint` | `_uri` 已含 `/v2` 又接 `/v2/bot/...`。 | `P0` | 改 path 為 `/bot/coupon`。 |
| Coupon / Membership | `/v2/bot/coupon/{couponId}/close` | PUT | `api.line.me` | 關閉 coupon。 | `CloseCouponAsync`; `LineMessagingClient.cs:2444` | `WrongEndpoint` | `_uri` 已含 `/v2` 又接 `/v2/bot/...`。 | `P0` | 改 path 為 `/bot/coupon/{couponId}/close`。 |
| Coupon / Membership | `/v2/bot/coupon` | GET | `api.line.me` | 取得 coupon list。 | `GetCouponListAsync`; `LineMessagingClient.cs:2455` | `WrongEndpoint` | `_uri` 已含 `/v2` 又接 `/v2/bot/...`。 | `P0` | 改 path 為 `/bot/coupon`。 |
| Coupon / Membership | `/v2/bot/coupon/{couponId}` | GET | `api.line.me` | 取得 coupon 詳細資料。 | `GetCouponAsync`; `LineMessagingClient.cs:2468` | `WrongEndpoint` | `_uri` 已含 `/v2` 又接 `/v2/bot/...`。 | `P0` | 改 path 為 `/bot/coupon/{couponId}`。 |
| Coupon / Membership | `/v2/bot/membership/subscription/{userId}` | GET | `api.line.me` | 取得 user membership subscription。 | `GetMembershipSubscriptionAsync`; `LineMessagingClient.cs:2480` | `WrongEndpoint` | `_uri` 已含 `/v2` 又接 `/v2/bot/...`。 | `P0` | 改 path 為 `/bot/membership/subscription/{userId}`。 |
| Coupon / Membership | `/v2/bot/membership/{membershipId}/users/ids` | GET | `api.line.me` | 取得 membership plan user IDs。 | `GetMembershipUserIdsAsync`; `LineMessagingClient.cs:2506` | `WrongEndpoint` | `_uri` 已含 `/v2` 又接 `/v2/bot/...`。 | `P0` | 改 path 為 `/bot/membership/{membershipId}/users/ids`。 |
| Coupon / Membership | `/v2/bot/membership/list` | GET | `api.line.me` | 取得 membership plan list。 | `GetMembershipPlansAsync`; `LineMessagingClient.cs:2523` | `WrongEndpoint` | `_uri` 已含 `/v2` 又接 `/v2/bot/...`。 | `P0` | 改 path 為 `/bot/membership/list`。 |

### 5.12 OAuth / Token

| 官方分類 | 官方 endpoint / object | HTTP method | host | 官方用途 | 目前 SDK 對應方法/類別 | 目前狀態 | 問題類型 | 風險等級 | 建議修正 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| OAuth / Token | `/oauth/accessToken` | POST | `api.line.me` | 發行 channel access token。 | `IssueChannelAccessTokenAsync`; `LineMessagingClient.cs:173-175` | `Obsolete` | 使用舊 endpoint 語意；官方目前列出 v2.1 與 stateless token。 | `P1` | 明確區分 legacy、v2.1、stateless token 方法。 |
| OAuth / Token | `/oauth/revoke` | POST | `api.line.me` | revoke channel access token。 | `RevokeChannelAccessTokenAsync`; `LineMessagingClient.cs:229-231` | `Partial` | 有 revoke 方法，但未涵蓋 stateless revoke 與 token key ID 流程。 | `P1` | 補 v2.1/stateless revoke API。 |
| OAuth / Token | Verify channel access token | GET | `api.line.me` | 驗證 token。 | None | `Missing` | SDK 無 verify token 方法。 | `P2` | 新增 verify token endpoint 與 response model。 |
| OAuth / Token | Get all valid channel access token key IDs | GET | `api.line.me` | 取得有效 key IDs。 | `ChannelAccessTokenKeyIds` model only | `Missing` | model 存在但 client 無方法。 | `P2` | 新增 key IDs client method。 |
| OAuth / Token | Issue stateless channel access token | POST | `api.line.me` | 發行 stateless token。 | `StatelessChannelAccessTokenRequest` model only | `Missing` | model 存在但 client 無方法。 | `P2` | 新增 stateless issue method。 |
| OAuth / Token | Revoke stateless channel access token | POST | `api.line.me` | revoke stateless token。 | None | `Missing` | SDK 無 stateless revoke method。 | `P2` | 新增 stateless revoke method。 |

## 6. 風險統計

| 優先級 | 數量 | 主要問題 |
| --- | ---: | --- |
| `P0` | 27 | 硬編碼 token、data host 錯誤、`/v2/v2` endpoint、mark-as-read 與 rich menu batch endpoint 錯誤。 |
| `P1` | 43 | SDK 宣稱支援但未完成、Webhook common fields 缺漏、message common model 缺欄位、OAuth/token 流程不完整。 |
| `P2` | 69 | 需要補齊測試、response model、進階 endpoint 或官方完整欄位。 |
| `P3` | 0 | 目前沒有列為可延後的官方 Messaging API 項目。 |

## 7. 下一階段修正順序

1. 先修 `P0`：硬編碼 token、錯 host、錯 endpoint、mark-as-read 語意錯誤。
2. 再修 `P1`：SDK 宣稱支援但實際未完成或模型缺關鍵欄位的項目。
3. 接著補 `P2`：官方功能缺漏、進階 API、完整序列化與 response model 測試。
4. `P3` 只在官方方案限制或產品需要時補齊。

## 8. SDK 修正計畫輸入

下一份 SDK 修正 plan 應以本矩陣為唯一輸入來源，順序如下：

1. 先列出所有 `P0`，並逐一建立 regression test。
2. 再列出所有 `P1`，優先修正 `NotImplemented` 與 common model 缺欄位。
3. `P2` 依 endpoint family 拆小批實作，不把 message object、webhook、audience、rich menu 混在同一個大改動。
4. 若矩陣列為 `NeedsOfficialVerification`，必須回到官方文件確認後才能寫程式。
