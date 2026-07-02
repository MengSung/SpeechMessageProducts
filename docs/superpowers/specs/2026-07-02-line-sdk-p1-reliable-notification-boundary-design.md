# LINE SDK P1 Reliable Notification Boundary Design

Date: 2026-07-02
Branch: Jesus_5.1.6.WorktreeRefactorLine
Status: Approved for implementation planning

## Goal

P1 第一輪只處理 ChurchReport 現有 LINE 通知流程會直接受益的 SDK 能力：可靠通知與邊界整理。重點不是一次補完 LINE 官方所有 P1/P2 功能，而是讓繳費、奉獻、通知類流程在重送時可以避免重複訊息，並讓 LINE protocol 細節集中在 `Line.Messaging` SDK 內。

## Scope

### Include

- 為 `PushMessageAsync`、`MultiCastMessageAsync`、`BroadcastMessageAsync` 補官方 `X-Line-Retry-Key` header 支援。
- 保留現有公開方法簽名，新增可帶 retry key 的 overload 或最小 options 形式，避免破壞 ChurchReport 既有呼叫點。
- 在 `Line.Messaging` 內集中處理 retry-key header，不讓 `LineMessagingProcessor` 或 ChurchReport controller 自己組 LINE header。
- 擴充 `Line.Messaging.Tests`，用 request-capturing handler 驗證 URL、body、header 與舊 overload 行為。
- 盤點訊息模型 `quoteToken`、`sender`、mention/common base，但第一輪只把結果納入 plan 或後續項目；除非非常低風險，不在第一輪大改 message class hierarchy。

### Exclude

- 不做 P2 項目。
- 不動 `LinePayCSharp/`。
- 不實作 Audience / Narrowcast API，除非後續證明 ChurchReport 現有流程有直接使用需求。
- 不重構 CRM、付款、奉獻流程。
- 不在 `Line.Messaging/` SDK 引入 ChurchReport、CRM、DbContext 或產品相依。

## Current Usage Evidence

目前 ChurchReport 與 LineMessagingProcessor 實際使用面集中在：

- `LineMessagingClient`
- `LineMessagingProcessorClass`
- `PushUtility`
- `ReplyUtility`
- `SendMessage`
- `PushMessage`
- 少量 `Multicast` / `Broadcast`

`Narrowcast` 與 Audience 沒有明顯產品使用面，因此不應成為 P1 第一輪主體。

## Recommended Design

### SDK Boundary

`Line.Messaging` 擁有 LINE Messaging API 的 endpoint、payload、header、serialization 規則。新增 retry key 時，SDK 內部應提供單一 helper，例如：

```csharp
private static void ApplyRetryKeyHeader(HttpRequestMessage request, string retryKey)
```

此 helper 只做一件事：當 `retryKey` 有值時加入 `X-Line-Retry-Key`。空字串或 null 不送 header。這避免每個傳訊方法各自處理特殊情況。

### Public API Compatibility

既有方法維持可用：

- `PushMessageAsync(string to, IList<ISendMessage> messages)`
- `MultiCastMessageAsync(IList<string> to, IList<ISendMessage> messages)`
- `BroadcastMessageAsync(IList<ISendMessage> messages)`

新增可帶 retry key 的 overload：

- `PushMessageAsync(string to, IList<ISendMessage> messages, string retryKey)`
- `MultiCastMessageAsync(IList<string> to, IList<ISendMessage> messages, string retryKey)`
- `BroadcastMessageAsync(IList<ISendMessage> messages, string retryKey)`

舊 overload 委派到新 overload 並傳入 `null`，確保行為不變。

### Processor Boundary

`LineMessagingProcessorClass` 是產品 adapter，只負責把產品輸入轉成 SDK 呼叫。它不應直接知道 LINE retry header 名稱，也不應自己組 endpoint。若 ChurchReport 需要可靠通知，processor 可以接收 retry key 或由上層流程產生 retry key，再傳給 SDK overload。

### Data Flow

ChurchReport payment/donation notification flow:

1. 產品流程產生通知內容與接收者 LINE user id。
2. 若該流程有可穩定識別的業務鍵，例如 fee id、donation id、payment transaction id，則用它產生 retry key。
3. Processor/adapter 呼叫 SDK 的 retry-key overload。
4. SDK 將 retry key 寫入 `X-Line-Retry-Key` header。
5. LINE API 用 retry key 處理重送去重。

資料流必須單向：產品業務鍵 -> adapter 參數 -> SDK header helper -> HTTP request。不得藏在 static mutable global state。

## Error Handling

- retry key 為 null 或空白：不加 header，保留舊行為。
- retry key 格式驗證：第一輪不主動限制字串格式，避免 SDK 比官方更嚴格；若要限制，應以官方規格為準並在測試中鎖定。
- HTTP failure：沿用現有 `EnsureSuccessStatusCodeAsync` 行為，不在 P1 混入重試迴圈或背景任務。

## Testing Plan

新增或擴充 `Line.Messaging.Tests`：

- Push with retry key sends `X-Line-Retry-Key` header.
- Push without retry key does not send the header.
- Multicast with retry key sends the header and keeps existing JSON body.
- Broadcast with retry key sends the header and keeps existing JSON body.
- Existing overloads continue to call the same official endpoint and do not add retry header.
- Empty/whitespace retry key does not add header.

## Design Rationale

這個範圍符合 Linus-style maintenance 原則：

- 少特殊情況：單一 helper 處理 retry header。
- 資料流清楚：retry key 從產品流程一路傳到 SDK request，不藏全域狀態。
- 一個東西只做一件事：SDK 管 LINE protocol，processor 管產品 adapter。
- 保持相容：舊 API 不破壞，新增 overload 支援可靠通知。
- YAGNI：不因官方矩陣有 P1/P2 缺口就全補，只做現有產品流程會用到的可靠性能力。

## Approval

User approved P1 first-round direction on 2026-07-02: reliable notification and SDK boundary cleanup first, with no P2 and no LinePayCSharp changes.