# P2-06 公開方法來源盤點與對照

盤點日期：2026-11-08 Asia/Taipei (UTC+08:00)。主方案根 W:\；來源只讀，不改舊方案或其中 .worktrees 副本。

## 搜尋邊界與可重現命令

```powershell
rg -n "class PushUtility|class ReplyUtility|class LineUtilityClass" --glob "*.cs" ToolUtility SpeechMessageProducts.ChurchReport
rg -n "class PushUtility|class ReplyUtility|class LineUtilityClass" --glob "*.cs" --glob "!**/.worktrees/**" --glob "!**/obj/**" --glob "!**/bin/**" --hidden "D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.其他解決方案"
```

主方案 Push 2／Reply 1／LineUtility 1；Dynamics365Line Push 2／Reply 2／LineUtility 1；LineApiWebhook 各 1，合計 5／4／3，與計畫相符。以類別邊界平衡大括號限制公開方法清單，包含多行、expression-bodied、建構式及多載；不把同檔 PriceProcessor／DTO 的方法算進 LineUtilityClass。

## 選基準與差異

- 單一來源都不是全集：主方案 M2/M4 已有必要通知、retry key、Flex、共用 Workflows／RichMenu assignment；W1 有 SendAudio 與非同步範例；W2/D3 有回覆 Buttons；D3 有 Sender 多載；D4 有 Emojis 多載。不能以較新日期直接覆蓋。
- W3 為 CP950，公開送訊多為服務委派的 expression-bodied 方法；僅盤點 class 宣告會漏掉委派後的 sender／richmenu／contact 能力。已列全部委派入口。
- 主方案 M4 群播已刻意逐人 push；M1/W1 則為 multicast HTTP，兩者不是同一黃金。
- 主方案認證 RichMenu 已由 assignment 處理，不能退回 D1/D2/M1 的「create→固定磁碟圖片→link→通知」示範，也不能把 unlink 改回刪共用 menu。
- M1 建構時解析 ToolUtilityFactory；基線以已知 fake 欄位隔離 CRM，搬移後改公開借用資源建構式。AddRichMenuMessage 補操作限定圖片來源，在原 HEAD 方法只替換圖片讀取的接縫下實測四步 HTTP，不能將成功路徑退役。
- M3 EchoImageAsync 下載串流、Image 與 thumbnail 原本沒有 Dispose；本包先保護失敗與成功釋放，再以 processor 內容入口承接，不重做 HTTP。
- D4 的 Emojis 型別在本主方案 SDK 不存在；人類指示原文是「而非一律新增SDK缺少Emojis」，只限制本包不要擅加 SDK 能力，不能推定已核定刪除功能；是否接受此相容缺口待確認。

## 逐份完整方法對照

### M1 — `ToolUtility\PushUtility.cs`

- 來源 SHA-256：`f7e5c044b0765748f90a195cbbe16d580da1fcbc0208890d19c76a57fe22fe45`。
- 解碼：UTF-8（可能有BOM，來源保持原貌）；公開建構式／方法共 20 個。

| 公開方法完整簽章（含多載） | 最終分類／主方案承接入口 | 證據／黃金 |
|---|---|---|
| `public PushUtility(LineMessagingClient LineMessagingClient)` | 設定／建構不送訊；保留原建構形狀（新增 M1 借用資源及圖片來源多載） | 原始來源無 HTTP；注入建構式由每個 P206 案例執行。配置／Factory 建構不連真實服務重播 |
| `public async Task SendMessage(string UserId, List<ISendMessage> MessageToSend)` | 共有；M1 → LineLegacySendWorkflow → processor | `tool.SendMessage(String,List-1).default` |
| `public async Task SendMessage(string UserId, string Message)` | 共有；M1 → LineLegacySendWorkflow → processor | `tool.SendMessage(String,String).default` |
| `public async Task MultiCastTextMessageAsync(IList<string> To, string Message)` | 共有；M1 → LineLegacySendWorkflow → processor | `tool.MultiCastTextMessageAsync(IList-1,String).default` |
| `public async Task SendImage(string UserId, string OriginalContenUrl, string PreviewImageUrl)` | 共有；M1 → LineLegacySendWorkflow → processor | `tool.SendImage(String,String,String).default` |
| `public async Task SendVideo(string UserId, string OriginalContenUrl, string PreviewImageUrl)` | 共有；M1 → LineLegacySendWorkflow → processor | `tool.SendVideo(String,String,String).default` |
| `public async Task SendAudeo(string UserId, string OriginalContenUrl, long Duration)` | 共有；M1 → LineLegacySendWorkflow → processor | `tool.SendAudeo(String,String,Int64).default` |
| `public async Task SendLocation(string UserId, string Title, string Address, decimal Latitude, decimal Longitude)` | 共有；M1 → LineLegacySendWorkflow → processor | `tool.SendLocation(String,String,String,Decimal,Decimal).default` |
| `public async Task SendSticker(string UserId, int PackageId, int StickerId)` | 共有；M1 → LineLegacySendWorkflow → processor | `tool.SendSticker(String,Int32,Int32).default` |
| `public async Task PostSerializedTemplate(string UserId, String AltText, String ThumbnailImageUrl, String Title, String Text, List<ITemplateAction> aITemplateAction)` | 共有；M1 → LineLegacySendWorkflow → processor | `tool.PostSerializedTemplate(String,String,String,String,String,List-1).default` |
| `public async Task PostSerializedConfirm(string UserId, String AltText, String Text, List<ITemplateAction> aITemplateAction)` | 共有；M1 → LineLegacySendWorkflow → processor | `tool.PostSerializedConfirm(String,String,String,List-1).default` |
| `public async Task PostSerializedImageMap(string UserId, string AltText, string ImageUrl, int BaseWidth, int Basehight, List<IImagemapAction> aImagemapAction)` | 共有；M1 → LineLegacySendWorkflow → processor | `tool.PostSerializedImageMap(String,String,String,Int32,Int32,List-1).default` |
| `public async Task<String> AddRichMenuMessage(string UserId)` | 共有；M1 → LineLegacySendWorkflow → processor | `tool.AddRichMenuMessage(String).default`（原 HEAD 方法＋圖片接縫實測；另 source-replay `menu-add`） |
| `public async Task<String> DeleteRichMenuMessage(string UserId)` | 共有；M1 → LineLegacySendWorkflow → processor | `tool.DeleteRichMenuMessage(String).default` |
| `public async Task ConfirmMessageAsync(string UserId)` | 共有；M1 → LineLegacySendWorkflow → processor | `tool.ConfirmMessageAsync(String).default` |
| `public async Task CarouselMessageAsync(string UserId)` | 共有；M1 → LineLegacySendWorkflow → processor | `tool.CarouselMessageAsync(String).default` |
| `public async Task ChurchCarouselMessageAsync(string UserId)` | 共有；M1 → LineLegacySendWorkflow → processor | `tool.ChurchCarouselMessageAsync(String).default` |
| `public void ConfirmMessage(string UserId)` | 共有；M1 → LineLegacySendWorkflow → processor | `tool.ConfirmMessage(String).default` |
| `public void CarouselMessage(string UserId)` | 共有；M1 → LineLegacySendWorkflow → processor | `tool.CarouselMessage(String).default` |
| `public void ChurchCarouselMessage(string UserId)` | 共有；M1 → LineLegacySendWorkflow → processor | `tool.ChurchCarouselMessage(String).default` |

### M2 — `SpeechMessageProducts.ChurchReport\Tools\PushUtility.cs`

- 來源 SHA-256：`60dc90cc8000661de4d3dfad66ab5d35335cda7a6dee3dbe5bb9361b3932ba12`。
- 解碼：UTF-8（可能有BOM，來源保持原貌）；公開建構式／方法共 23 個。

| 公開方法完整簽章（含多載） | 最終分類／主方案承接入口 | 證據／黃金 |
|---|---|---|
| `public PushUtility(LineMessagingClient LineMessagingClient)` | 設定／建構不送訊；保留原建構形狀（新增 M1 借用資源及圖片來源多載） | 原始來源無 HTTP；注入建構式由每個 P206 案例執行。配置／Factory 建構不連真實服務重播 |
| `public PushUtility(LineMessagingClient LineMessagingClient, ILineNotificationWorkflow? lineNotificationWorkflow)` | 設定／建構不送訊；保留原建構形狀（新增 M1 借用資源及圖片來源多載） | 原始來源無 HTTP；注入建構式由每個 P206 案例執行。配置／Factory 建構不連真實服務重播 |
| `public PushUtility( LineMessagingClient LineMessagingClient, ILineNotificationWorkflow? lineNotificationWorkflow, ILineRichMenuWorkflow? lineRichMenuWorkflow)` | 設定／建構不送訊；保留原建構形狀（新增 M1 借用資源及圖片來源多載） | 原始來源無 HTTP；注入建構式由每個 P206 案例執行。配置／Factory 建構不連真實服務重播 |
| `public PushUtility( LineMessagingClient LineMessagingClient, ILineNotificationWorkflow? lineNotificationWorkflow, ILineRichMenuAssignmentWorkflow? lineRichMenuAssignmentWorkflow)` | 設定／建構不送訊；保留原建構形狀（新增 M1 借用資源及圖片來源多載） | 原始來源無 HTTP；注入建構式由每個 P206 案例執行。配置／Factory 建構不連真實服務重播 |
| `public PushUtility( LineMessagingClient LineMessagingClient, ILineNotificationWorkflow? lineNotificationWorkflow, ILineRichMenuWorkflow? lineRichMenuWorkflow, ILineRichMenuAssignmentWorkflow? lineRichMenuAssignmentWorkflow)` | 設定／建構不送訊；保留原建構形狀（新增 M1 借用資源及圖片來源多載） | 原始來源無 HTTP；注入建構式由每個 P206 案例執行。配置／Factory 建構不連真實服務重播 |
| `public async Task SendMessage(string UserId, List<ISendMessage> MessageToSend)` | 共有；M2 → 既有 notification／assignment | `push.SendMessage(String,List-1).default` |
| `public async Task SendMessage(string UserId, string Message)` | 共有；M2 → 既有 notification／assignment | `push.SendMessage(String,String).default` |
| `public async Task SendMessageOrThrowAsync(string UserId, string Message)` | 共有；M2 → 既有 notification／assignment | `push.SendMessageOrThrowAsync(String,String).default` |
| `public async Task SendMessagesOrThrowAsync(string UserId, IReadOnlyList<ISendMessage> messages)` | 共有；M2 → 既有 notification／assignment | `push.SendMessagesOrThrowAsync(String,IReadOnlyList-1).default` |
| `public async Task SendReliableMessageAsync(string UserId, string Message, string? retryKey)` | 純產品不搬；M4 CRM／action 組裝保持 | `ProductCrmMethod_SyntheticRecord_DoesNotSendHttp(SendReliableMessageAsync)`：結果＋NO HTTP |
| `public async Task SendImage(string UserId, string OriginalContenUrl, string PreviewImageUrl)` | 共有；M2 → 既有 notification／assignment | `push.SendImage(String,String,String).default` |
| `public async Task SendVideo(string UserId, string OriginalContenUrl, string PreviewImageUrl)` | 共有；M2 → 既有 notification／assignment | `push.SendVideo(String,String,String).default` |
| `public async Task SendAudeo(string UserId, string OriginalContenUrl, long Duration)` | 共有；M2 → 既有 notification／assignment | `push.SendAudeo(String,String,Int64).default` |
| `public async Task SendLocation(string UserId, string Title, string Address, decimal Latitude, decimal Longitude)` | 共有；M2 → 既有 notification／assignment | `push.SendLocation(String,String,String,Decimal,Decimal).default` |
| `public async Task SendSticker(string UserId, int PackageId, int StickerId)` | 共有；M2 → 既有 notification／assignment | `push.SendSticker(String,Int32,Int32).default` |
| `public async Task PostSerializedTemplate(string UserId, String AltText, String ThumbnailImageUrl, String Title, String Text, List<ITemplateAction> aITemplateAction)` | 共有；M2 → 既有 notification／assignment | `push.PostSerializedTemplate(String,String,String,String,String,List-1).default` |
| `public async Task PostSerializedConfirm(string UserId, String AltText, String Text, List<ITemplateAction> aITemplateAction)` | 共有；M2 → 既有 notification／assignment | `push.PostSerializedConfirm(String,String,String,List-1).default` |
| `public async Task PostSerializedImageMap(string UserId, string AltText, string ImageUrl, int BaseWidth, int Basehight, List<IImagemapAction> aImagemapAction)` | 共有；M2 → 既有 notification／assignment | `push.PostSerializedImageMap(String,String,String,Int32,Int32,List-1).default` |
| `public async Task<String> AddRichMenuMessage(string UserId)` | 共有；M2 → 既有 notification／assignment | `push.AddRichMenuMessage(String).default` |
| `public async Task<String> DeleteRichMenuMessage(string UserId)` | 共有；M2 → 既有 notification／assignment | `push.DeleteRichMenuMessage(String).default` |
| `public void ConfirmMessage(string UserId)` | 共有；M2 → 既有 notification／assignment | `push.ConfirmMessage(String).default` |
| `public void CarouselMessage(string UserId)` | 共有；M2 → 既有 notification／assignment | `push.CarouselMessage(String).default` |
| `public void ChurchCarouselMessage(string UserId)` | 共有；M2 → 既有 notification／assignment | `push.ChurchCarouselMessage(String).default` |

### M3 — `SpeechMessageProducts.ChurchReport\Tools\ReplyUtility.cs`

- 來源 SHA-256：`3d4cca4f422bea5399031b2ee2e5840c11e00c726612e2058e441fe2f9435303`。
- 解碼：UTF-8（可能有BOM，來源保持原貌）；公開建構式／方法共 13 個。

| 公開方法完整簽章（含多載） | 最終分類／主方案承接入口 | 證據／黃金 |
|---|---|---|
| `public ReplyUtility(LineMessagingClient LineMessagingClient)` | 設定／建構不送訊；保留原建構形狀（新增 M1 借用資源及圖片來源多載） | 原始來源無 HTTP；注入建構式由每個 P206 案例執行。配置／Factory 建構不連真實服務重播 |
| `public ReplyUtility( LineMessagingClient LineMessagingClient, ILineReplyWorkflow? lineReplyWorkflow)` | 設定／建構不送訊；保留原建構形狀（新增 M1 借用資源及圖片來源多載） | 原始來源無 HTTP；注入建構式由每個 P206 案例執行。配置／Factory 建構不連真實服務重播 |
| `public ReplyUtility( LineMessagingClient LineMessagingClient, LineMessagingProcessorClass LineMessagingProcessor)` | 設定／建構不送訊；保留原建構形狀（新增 M1 借用資源及圖片來源多載） | 原始來源無 HTTP；注入建構式由每個 P206 案例執行。配置／Factory 建構不連真實服務重播 |
| `public ReplyUtility( LineMessagingClient LineMessagingClient, LineMessagingProcessorClass LineMessagingProcessor, ILineReplyWorkflow? lineReplyWorkflow)` | 設定／建構不送訊；保留原建構形狀（新增 M1 借用資源及圖片來源多載） | 原始來源無 HTTP；注入建構式由每個 P206 案例執行。配置／Factory 建構不連真實服務重播 |
| `public async Task EchoAsyncProcessor(MessageEvent ev)` | 共有；M3 → 既有 LineReplyWorkflow／processor | `reply.EchoAsyncProcessor(MessageEvent).default`；另 group／room 分支 |
| `public async Task ReplyMessage(string replyToken, List<ISendMessage> MessageToSend)` | 共有；M3 → 既有 LineReplyWorkflow／processor | `reply.ReplyMessage(String,List-1).default` |
| `public async Task ReplyMessageAsync(string replyToken, string TextMessage)` | 共有；M3 → 既有 LineReplyWorkflow／processor | `reply.ReplyMessageAsync(String,String).default` |
| `public Task EchoAsync(string replyToken, string userMessage)` | 共有；M3 → 既有 LineReplyWorkflow／processor | `reply.EchoAsync(String,String).default` |
| `public async Task PostSerializedConfirm(string replyToken, String AltText, String Text, List<ITemplateAction> aITemplateAction)` | 共有；M3 → 既有 LineReplyWorkflow／processor | `reply.PostSerializedConfirm(String,String,String,List-1).default` |
| `public async Task PostSerializedImageMap(string replyToken, string AltText, string ImageUrl, int BaseWidth, int Basehight, List<IImagemapAction> aImagemapAction)` | 共有；M3 → 既有 LineReplyWorkflow／processor | `reply.PostSerializedImageMap(String,String,String,Int32,Int32,List-1).default` |
| `public async Task EchoImageAsync(string replyToken, string messageId, string blobDirectoryName)` | 共有；M3 → 既有 LineReplyWorkflow／processor | `reply.EchoImageAsync(String,String,String).default` |
| `public async Task UploadMediaContentAsync(string replyToken, string messageId, string blobDirectoryName, string blobName)` | 共有；M3 → 既有 LineReplyWorkflow／processor | `reply.UploadMediaContentAsync(String,String,String,String).default` |
| `public async Task ReplyRandomStickerAsync(string replyToken)` | 共有；M3 → 既有 LineReplyWorkflow／processor | `reply.ReplyRandomStickerAsync(String).default` |

### M4 — `SpeechMessageProducts.ChurchReport\Tools\LineUtilityClass.cs`

- 來源 SHA-256：`c690a28204107a1a867c69ac265cba5b60455110484defff1be7af8d2a5aa2c6`。
- 解碼：UTF-8（可能有BOM，來源保持原貌）；公開建構式／方法共 31 個。

| 公開方法完整簽章（含多載） | 最終分類／主方案承接入口 | 證據／黃金 |
|---|---|---|
| `public void Dispose()` | 產品 owner／設定不搬；保留既有 client 釋放及切換 | 來源 no-HTTP；既有 `LineUtilityClassWorkflowTests`／資源測試（見主方案呼叫點） |
| `public LineUtilityClass( ToolUtilityClass aToolUtilityClass)` | 設定／建構不送訊；保留原建構形狀（新增 M1 借用資源及圖片來源多載） | 原始來源無 HTTP；注入建構式由每個 P206 案例執行。配置／Factory 建構不連真實服務重播 |
| `public LineUtilityClass( ToolUtilityClass aToolUtilityClass, ILineNotificationWorkflow? lineNotificationWorkflow)` | 設定／建構不送訊；保留原建構形狀（新增 M1 借用資源及圖片來源多載） | 原始來源無 HTTP；注入建構式由每個 P206 案例執行。配置／Factory 建構不連真實服務重播 |
| `public LineUtilityClass( ToolUtilityClass aToolUtilityClass, ILineNotificationWorkflow? lineNotificationWorkflow, ILineReplyWorkflow? lineReplyWorkflow)` | 設定／建構不送訊；保留原建構形狀（新增 M1 借用資源及圖片來源多載） | 原始來源無 HTTP；注入建構式由每個 P206 案例執行。配置／Factory 建構不連真實服務重播 |
| `public LineUtilityClass( ToolUtilityClass aToolUtilityClass, LineMessagingClient lineMessagingClient, ILineNotificationWorkflow? lineNotificationWorkflow)` | 設定／建構不送訊；保留原建構形狀（新增 M1 借用資源及圖片來源多載） | 原始來源無 HTTP；注入建構式由每個 P206 案例執行。配置／Factory 建構不連真實服務重播 |
| `public LineUtilityClass( ToolUtilityClass aToolUtilityClass, LineMessagingClient lineMessagingClient, ILineNotificationWorkflow? lineNotificationWorkflow, ILineReplyWorkflow? lineReplyWorkflow)` | 設定／建構不送訊；保留原建構形狀（新增 M1 借用資源及圖片來源多載） | 原始來源無 HTTP；注入建構式由每個 P206 案例執行。配置／Factory 建構不連真實服務重播 |
| `public void SetupChannelAccessToken(ref IOrganizationService aCrmService)` | 產品 owner／設定不搬；保留既有 client 釋放及切換 | 來源 no-HTTP；既有 `LineUtilityClassWorkflowTests`／資源測試（見主方案呼叫點） |
| `public async Task ReplyMessage(string ReplyToken, List<ISendMessage> MessageToSend)` | 共有；M4 → 既有 notification／assignment | `line.ReplyMessage(String,List-1).default` |
| `public async Task ReplyTextMessage(string ReplyToken, string Message)` | 共有；M4 → 既有 notification／assignment | `line.ReplyTextMessage(String,String).default` |
| `public async Task SendMessage(string UserId, List<ISendMessage> MessageToSend)` | 共有；M4 → 既有 notification／assignment | `line.SendMessage(String,List-1).default` |
| `public async Task SendMessageAsync(string UserId, string Message)` | 共有；M4 → 既有 notification／assignment | `line.SendMessageAsync(String,String).default` |
| `public async Task MultiCastTextMessageAsync(IList<string> To, string Message)` | 共有；M4 → 既有 notification／assignment | `line.MultiCastTextMessageAsync(IList-1,String).default` |
| `public void SendMessage(string UserId, string Message)` | 共有；M4 → 既有 notification／assignment | `line.SendMessage(String,String).default` |
| `public async Task SendImage(string UserId, string OriginalContenUrl, string PreviewImageUrl)` | 共有；M4 → 既有 notification／assignment | `line.SendImage(String,String,String).default` |
| `public async Task ReplyImage(string ReplyToken, string OriginalContenUrl, string PreviewImageUrl)` | 共有；M4 → 既有 notification／assignment | `line.ReplyImage(String,String,String).default` |
| `public async Task SendVideo(string UserId, string OriginalContenUrl, string PreviewImageUrl)` | 共有；M4 → 既有 notification／assignment | `line.SendVideo(String,String,String).default` |
| `public async Task SendAudeo(string UserId, string OriginalContenUrl, long Duration)` | 共有；M4 → 既有 notification／assignment | `line.SendAudeo(String,String,Int64).default` |
| `public async Task SendLocation(string UserId, string Title, string Address, decimal Latitude, decimal Longitude)` | 共有；M4 → 既有 notification／assignment | `line.SendLocation(String,String,String,Decimal,Decimal).default` |
| `public async Task SendSticker(string UserId, int PackageId, int StickerId)` | 共有；M4 → 既有 notification／assignment | `line.SendSticker(String,Int32,Int32).default` |
| `public async Task PostSerializedTemplate(Entity aLetterEntity, string UserId, String AltText, String ThumbnailImageUrl, String Title, String Text, List<ITemplateAction> aITemplateAction)` | 共有；M4 → 既有 notification／assignment | `line.PostSerializedTemplate(Entity,String,String,String,String,String,List-1).default` |
| `public async Task PostSerializedTemplate(string UserId, String AltText, String ThumbnailImageUrl, String Title, String Text, List<ITemplateAction> aITemplateAction)` | 共有；M4 → 既有 notification／assignment | `line.PostSerializedTemplate(String,String,String,String,String,List-1).default` |
| `public async Task PostSerializedFlex(string UserId, FlexMessage aFlexMessage)` | 共有；M4 → 既有 notification／assignment | `line.PostSerializedFlex(String,FlexMessage).default` |
| `public async Task PostSerializedConfirm(string UserId, String AltText, String Text, List<ITemplateAction> aITemplateAction)` | 共有；M4 → 既有 notification／assignment | `line.PostSerializedConfirm(String,String,String,List-1).default` |
| `public async Task PostSerializedImageMap(string UserId, string AltText, string ImageUrl, int BaseWidth, int Basehight, List<IImagemapAction> aImagemapAction)` | 共有；M4 → 既有 notification／assignment | `line.PostSerializedImageMap(String,String,String,Int32,Int32,List-1).default` |
| `public async Task<String> AddRichMenuMessage(string UserId)` | 共有；M4 → 既有 notification／assignment | `line.AddRichMenuMessage(String).default` |
| `public async Task<String> DeleteRichMenuMessage(string UserId)` | 共有；M4 → 既有 notification／assignment | `line.DeleteRichMenuMessage(String).default` |
| `public void SetupActionList(Entity aLetterEntity, ref TemplateMessageClass aTemplateMessageClass)` | 純產品不搬；M4 CRM／action 組裝保持 | `ProductCrmMethod_SyntheticRecord_DoesNotSendHttp(SetupActionList)`：結果＋NO HTTP |
| `public String ConvertActionType(Entity aLetterEntity, String FieldName)` | 純產品不搬；M4 CRM／action 組裝保持 | `ProductCrmMethod_SyntheticRecord_DoesNotSendHttp(ConvertActionType)`：結果＋NO HTTP |
| `public Entity GetLineSender(Entity aLetterEntity)` | 純產品不搬；M4 CRM／action 組裝保持 | `ProductCrmMethod_SyntheticRecord_DoesNotSendHttp(GetLineSender)`：結果＋NO HTTP |
| `public void GetLineIdAndContactFullNameOfSender(Entity aLetterEntity, ref String DisplayedLineId, ref String LineId, ref String ReplyToken, ref String ContactFullName)` | 純產品不搬；M4 CRM／action 組裝保持 | `ProductCrmMethod_SyntheticRecord_DoesNotSendHttp(GetLineIdAndContactFullNameOfSender)`：結果＋NO HTTP |
| `public string GetContactPartyFullName(Entity aContactParty, ref String LineId)` | 純產品不搬；M4 CRM／action 組裝保持 | `ProductCrmMethod_SyntheticRecord_DoesNotSendHttp(GetContactPartyFullName)`：結果＋NO HTTP |

### D1 — `.其他解決方案/Dynamics365Line/ToolUtility/PushUtility.cs`

- 來源 SHA-256：`9903ad315c4d28900eee09c558afa2d7979b5e3b01d0993805ea59bd0ab968fd`。
- 解碼：UTF-8（可能有BOM，來源保持原貌）；公開建構式／方法共 16 個。

| 公開方法完整簽章（含多載） | 最終分類／主方案承接入口 | 證據／黃金 |
|---|---|---|
| `public PushUtility(LineMessagingClient LineMessagingClient)` | 純產品／設定不搬；不復刻外部舊方案 Factory、token 或 contact manager 建構形狀 | 來源 no-HTTP；主方案 owner 既有測試，不宣稱舊方案組件已執行 |
| `public async Task SendMessage(string UserId, List<ISendMessage> MessageToSend)` | 共有；M1.SendMessage | `tool.SendMessage(String,List-1).default`；同名多載見主方案表 |
| `public async Task SendMessage(string UserId, string Message)` | 共有；M1.SendMessage | `tool.SendMessage(String,List-1).default`；同名多載見主方案表 |
| `public async Task SendImage(string UserId, string OriginalContenUrl, string PreviewImageUrl)` | 共有；M1.SendImage | `tool.SendImage(String,String,String).default` |
| `public async Task SendVideo(string UserId, string OriginalContenUrl, string PreviewImageUrl)` | 共有；M1.SendVideo | `tool.SendVideo(String,String,String).default` |
| `public async Task SendAudeo(string UserId, string OriginalContenUrl, long Duration)` | 共有；M1.SendAudeo | `tool.SendAudeo(String,String,Int64).default` |
| `public async Task SendLocation(string UserId, string Title, string Address, decimal Latitude, decimal Longitude)` | 共有；M1.SendLocation | `tool.SendLocation(String,String,String,Decimal,Decimal).default` |
| `public async Task SendSticker(string UserId, int PackageId, int StickerId)` | 共有；M1.SendSticker | `tool.SendSticker(String,Int32,Int32).default` |
| `public async Task PostSerializedTemplate(string UserId, String AltText, String ThumbnailImageUrl, String Title, String Text, List<ITemplateAction> aITemplateAction)` | 共有；M1.PostSerializedTemplate | `tool.PostSerializedTemplate(String,String,String,String,String,List-1).default` |
| `public async Task PostSerializedConfirm(string UserId, String AltText, String Text, List<ITemplateAction> aITemplateAction)` | 共有；M1.PostSerializedConfirm | `tool.PostSerializedConfirm(String,String,String,List-1).default` |
| `public async Task PostSerializedImageMap(string UserId, string AltText, string ImageUrl, int BaseWidth, int Basehight, List<IImagemapAction> aImagemapAction)` | 共有；M1.PostSerializedImageMap | `tool.PostSerializedImageMap(String,String,String,Int32,Int32,List-1).default` |
| `public async Task<String> AddRichMenuMessage(string UserId)` | 已有其他入口承接；主 M1 保留 legacy 次序，M2/M4 既有 assignment 不倒退 | `tool.AddRichMenuMessage(String).default`／`tool.DeleteRichMenuMessage(String).default`；`push/line` 同名黃金。外部來源未重建成獨立 API |
| `public async Task<String> DeleteRichMenuMessage(string UserId)` | 已有其他入口承接；主 M1 保留 legacy 次序，M2/M4 既有 assignment 不倒退 | `tool.AddRichMenuMessage(String).default`／`tool.DeleteRichMenuMessage(String).default`；`push/line` 同名黃金。外部來源未重建成獨立 API |
| `public void ConfirmMessage(string UserId)` | 共有；M1.ConfirmMessage | `tool.ConfirmMessage(String).default` |
| `public void CarouselMessage(string UserId)` | 共有；M1.CarouselMessage | `tool.CarouselMessage(String).default` |
| `public void ChurchCarouselMessage(string UserId)` | 共有；M1.ChurchCarouselMessage | `tool.ChurchCarouselMessage(String).default` |

### D2 — `.其他解決方案/Dynamics365Line/LineUtility/PushUtility.cs`

- 來源 SHA-256：`5fbf25eeecb6e4169c2b7ff2438e6fc8f0989d5d93af95d5c7e58b2e7d09157f`。
- 解碼：UTF-8（可能有BOM，來源保持原貌）；公開建構式／方法共 16 個。

| 公開方法完整簽章（含多載） | 最終分類／主方案承接入口 | 證據／黃金 |
|---|---|---|
| `public PushUtility(LineMessagingClient LineMessagingClient)` | 純產品／設定不搬；不復刻外部舊方案 Factory、token 或 contact manager 建構形狀 | 來源 no-HTTP；主方案 owner 既有測試，不宣稱舊方案組件已執行 |
| `public async Task SendMessage(string UserId, List<ISendMessage> MessageToSend)` | 共有；M1.SendMessage | `tool.SendMessage(String,List-1).default`；同名多載見主方案表 |
| `public async Task SendMessage(string UserId, string Message)` | 共有；M1.SendMessage | `tool.SendMessage(String,List-1).default`；同名多載見主方案表 |
| `public async Task SendImage(string UserId, string OriginalContenUrl, string PreviewImageUrl)` | 共有；M1.SendImage | `tool.SendImage(String,String,String).default` |
| `public async Task SendVideo(string UserId, string OriginalContenUrl, string PreviewImageUrl)` | 共有；M1.SendVideo | `tool.SendVideo(String,String,String).default` |
| `public async Task SendAudeo(string UserId, string OriginalContenUrl, long Duration)` | 共有；M1.SendAudeo | `tool.SendAudeo(String,String,Int64).default` |
| `public async Task SendLocation(string UserId, string Title, string Address, decimal Latitude, decimal Longitude)` | 共有；M1.SendLocation | `tool.SendLocation(String,String,String,Decimal,Decimal).default` |
| `public async Task SendSticker(string UserId, int PackageId, int StickerId)` | 共有；M1.SendSticker | `tool.SendSticker(String,Int32,Int32).default` |
| `public async Task PostSerializedTemplate(string UserId, String AltText, String ThumbnailImageUrl, String Title, String Text, List<ITemplateAction> aITemplateAction)` | 共有；M1.PostSerializedTemplate | `tool.PostSerializedTemplate(String,String,String,String,String,List-1).default` |
| `public async Task PostSerializedConfirm(string UserId, String AltText, String Text, List<ITemplateAction> aITemplateAction)` | 共有；M1.PostSerializedConfirm | `tool.PostSerializedConfirm(String,String,String,List-1).default` |
| `public async Task PostSerializedImageMap(string UserId, string AltText, string ImageUrl, int BaseWidth, int Basehight, List<IImagemapAction> aImagemapAction)` | 共有；M1.PostSerializedImageMap | `tool.PostSerializedImageMap(String,String,String,Int32,Int32,List-1).default` |
| `public async Task<String> AddRichMenuMessage(string UserId)` | 已有其他入口承接；主 M1 保留 legacy 次序，M2/M4 既有 assignment 不倒退 | `tool.AddRichMenuMessage(String).default`／`tool.DeleteRichMenuMessage(String).default`；`push/line` 同名黃金。外部來源未重建成獨立 API |
| `public async Task<String> DeleteRichMenuMessage(string UserId)` | 已有其他入口承接；主 M1 保留 legacy 次序，M2/M4 既有 assignment 不倒退 | `tool.AddRichMenuMessage(String).default`／`tool.DeleteRichMenuMessage(String).default`；`push/line` 同名黃金。外部來源未重建成獨立 API |
| `public void ConfirmMessage(string UserId)` | 共有；M1.ConfirmMessage | `tool.ConfirmMessage(String).default` |
| `public void CarouselMessage(string UserId)` | 共有；M1.CarouselMessage | `tool.CarouselMessage(String).default` |
| `public void ChurchCarouselMessage(string UserId)` | 共有；M1.ChurchCarouselMessage | `tool.ChurchCarouselMessage(String).default` |

### D3 — `.其他解決方案/Dynamics365Line/ToolUtility/ReplyUtility.cs`

- 來源 SHA-256：`7d02d6fddaa53c8a22dfe51096cec466fb1fb89868fd6b129a399dedb566e4ea`。
- 解碼：UTF-8（可能有BOM，來源保持原貌）；公開建構式／方法共 11 個。

| 公開方法完整簽章（含多載） | 最終分類／主方案承接入口 | 證據／黃金 |
|---|---|---|
| `public ReplyUtility(LineMessagingClient LineMessagingClient)` | 純產品／設定不搬；不復刻外部舊方案 Factory、token 或 contact manager 建構形狀 | 來源 no-HTTP；主方案 owner 既有測試，不宣稱舊方案組件已執行 |
| `public async Task EchoAsyncProcessor(MessageEvent ev)` | 共有；M3.EchoAsyncProcessor | `reply.EchoAsyncProcessor(MessageEvent).default`；同名多載見主方案表 |
| `public async Task ReplyMessage( string replyToken, List<ISendMessage> MessageToSend)` | 共有；M3.ReplyMessage | `reply.ReplyMessage(String,List-1).default` |
| `public async Task ReplyMessageAsync(string replyToken, string userMessage, Sender aSender = null )` | 已由其他入口承接；TextMessage.Sender → M3.ReplyMessage(list) | source-replay `reply-sender`；差異 E4：保留顯示值，不復刻舊建構式／可空 Sender 多載 |
| `public Task EchoAsync(string replyToken, string userMessage)` | 共有；M3.EchoAsync | `reply.EchoAsync(String,String).default` |
| `public async Task PostSerializedConfirm(string replyToken, String AltText, String Text, List<ITemplateAction> aITemplateAction)` | 共有；M3.PostSerializedConfirm | `reply.PostSerializedConfirm(String,String,String,List-1).default` |
| `public async Task PostSerializedImageMap(string replyToken, string AltText, string ImageUrl, int BaseWidth, int Basehight, List<IImagemapAction> aImagemapAction)` | 共有；M3.PostSerializedImageMap | `reply.PostSerializedImageMap(String,String,String,Int32,Int32,List-1).default` |
| `public async Task PostSerializedTemplate(string replyToken, String AltText, String ThumbnailImageUrl, String Title, String Text, List<ITemplateAction> aITemplateAction)` | 已由其他入口承接；TemplateMessage(Buttons) → M3.ReplyMessage(list) | source-replay `reply-buttons`；原舊方法只是組裝＋reply，不另新增同功能 helper |
| `public async Task EchoImageAsync(string replyToken, string messageId, string blobDirectoryName)` | 共有；M3.EchoImageAsync | `reply.EchoImageAsync(String,String,String).default` |
| `public async Task UploadMediaContentAsync(string replyToken, string messageId, string blobDirectoryName, string blobName)` | 共有；M3.UploadMediaContentAsync | `reply.UploadMediaContentAsync(String,String,String,String).default` |
| `public async Task ReplyRandomStickerAsync(string replyToken)` | 共有；M3.ReplyRandomStickerAsync | `reply.ReplyRandomStickerAsync(String).default` |

### D4 — `.其他解決方案/Dynamics365Line/LineUtility/ReplyUtility.cs`

- 來源 SHA-256：`034f670d4af73b4230816aff92caebd5702da79f25c806be740e7dd885293529`。
- 解碼：UTF-8（可能有BOM，來源保持原貌）；公開建構式／方法共 11 個。

| 公開方法完整簽章（含多載） | 最終分類／主方案承接入口 | 證據／黃金 |
|---|---|---|
| `public ReplyUtility(LineMessagingClient LineMessagingClient)` | 純產品／設定不搬；不復刻外部舊方案 Factory、token 或 contact manager 建構形狀 | 來源 no-HTTP；主方案 owner 既有測試，不宣稱舊方案組件已執行 |
| `public async Task EchoAsyncProcessor(MessageEvent ev)` | 共有；M3.EchoAsyncProcessor | `reply.EchoAsyncProcessor(MessageEvent).default`；同名多載見主方案表 |
| `public async Task ReplyMessage(string replyToken, List<ISendMessage> MessageToSend)` | 共有；M3.ReplyMessage | `reply.ReplyMessage(String,List-1).default` |
| `public async Task ReplyMessageAsync(string replyToken, string TextMessage)` | 共有；M3.ReplyMessageAsync | `reply.ReplyMessageAsync(String,String).default` |
| `public async Task ReplyMessageAsync(string replyToken, string TextMessage, Emojis[] aEmojis)` | 待確認的舊能力缺口；本包未搬，本主 SDK 無該型別，未取得明確廢止核定 | 差異 E1：LINE Emojis 多載未保留，不冒稱一般 Unicode emoji 等價 |
| `public Task EchoAsync(string replyToken, string userMessage)` | 共有；M3.EchoAsync | `reply.EchoAsync(String,String).default` |
| `public async Task PostSerializedConfirm(string replyToken, String AltText, String Text, List<ITemplateAction> aITemplateAction)` | 共有；M3.PostSerializedConfirm | `reply.PostSerializedConfirm(String,String,String,List-1).default` |
| `public async Task PostSerializedImageMap(string replyToken, string AltText, string ImageUrl, int BaseWidth, int Basehight, List<IImagemapAction> aImagemapAction)` | 共有；M3.PostSerializedImageMap | `reply.PostSerializedImageMap(String,String,String,Int32,Int32,List-1).default` |
| `public async Task EchoImageAsync(string replyToken, string messageId, string blobDirectoryName)` | 共有；M3.EchoImageAsync | `reply.EchoImageAsync(String,String,String).default` |
| `public async Task UploadMediaContentAsync(string replyToken, string messageId, string blobDirectoryName, string blobName)` | 共有；M3.UploadMediaContentAsync | `reply.UploadMediaContentAsync(String,String,String,String).default` |
| `public async Task ReplyRandomStickerAsync(string replyToken)` | 共有；M3.ReplyRandomStickerAsync | `reply.ReplyRandomStickerAsync(String).default` |

### D5 — `.其他解決方案/Dynamics365Line/LineUtility/LineUtilityClass.cs`

- 來源 SHA-256：`7c6f8b6d349f4e16f0445b9a3a559eb6ea30dc29ff5337ae098774c72c434dfe`。
- 解碼：UTF-8（可能有BOM，來源保持原貌）；公開建構式／方法共 34 個。

| 公開方法完整簽章（含多載） | 最終分類／主方案承接入口 | 證據／黃金 |
|---|---|---|
| `public void Dispose()` | 純產品／設定不搬；不復刻外部舊方案 Factory、token 或 contact manager 建構形狀 | 來源 no-HTTP；主方案 owner 既有測試，不宣稱舊方案組件已執行 |
| `public LineUtilityClass(IOrganizationService aCrmService, String aOrganizationName, String aChannelAccessToken)` | 純產品／設定不搬；不復刻外部舊方案 Factory、token 或 contact manager 建構形狀 | 來源 no-HTTP；主方案 owner 既有測試，不宣稱舊方案組件已執行 |
| `public void SetupChannelAccessToken(ref IOrganizationService aCrmService)` | 純產品／設定不搬；不復刻外部舊方案 Factory、token 或 contact manager 建構形狀 | 來源 no-HTTP；主方案 owner 既有測試，不宣稱舊方案組件已執行 |
| `public async Task ReplyMessage(string ReplyToken, List<ISendMessage> MessageToSend)` | 共有；M3.ReplyMessage | `reply.ReplyMessage(String,List-1).default` |
| `public async Task ReplyTextMessage(string ReplyToken, string Message)` | 已由其他入口承接（命名別名不另搬）；M3.ReplyMessageAsync | `reply.ReplyMessageAsync(String,String).default` |
| `public async Task ReplyTextMessage(string ReplyToken, string Message, Emojis[] aEmojis)` | 待確認的舊能力缺口；本包未搬，本主 SDK 無該型別，未取得明確廢止核定 | 差異 E1：LINE Emojis 多載未保留，不冒稱一般 Unicode emoji 等價 |
| `public async Task SendMessage(string UserId, List<ISendMessage> MessageToSend)` | 共有；M1.SendMessage | `tool.SendMessage(String,List-1).default`；同名多載見主方案表 |
| `public async Task SendMessageAsync(string UserId, string Message)` | 共有；M4.SendMessageAsync | `line.SendMessageAsync(String,String).default` |
| `public async Task MultiCastTextMessageAsync(IList<string> To, string Message)` | 共有；M1.MultiCastTextMessageAsync | `tool.MultiCastTextMessageAsync(IList-1,String).default`；主 M4 原有 split-push 差異保留 |
| `public void SendMessage(string UserId, string Message)` | 共有；M1.SendMessage | `tool.SendMessage(String,List-1).default`；同名多載見主方案表 |
| `public async Task SendImage(string UserId, string OriginalContenUrl, string PreviewImageUrl)` | 共有；M1.SendImage | `tool.SendImage(String,String,String).default` |
| `public async Task ReplyImage(string ReplyToken, string OriginalContenUrl, string PreviewImageUrl)` | 共有；M4.ReplyImage | `line.ReplyImage(String,String,String).default` |
| `public async Task SendVideo(string UserId, string OriginalContenUrl, string PreviewImageUrl)` | 共有；M1.SendVideo | `tool.SendVideo(String,String,String).default` |
| `public async Task SendAudeo(string UserId, string OriginalContenUrl, long Duration)` | 共有；M1.SendAudeo | `tool.SendAudeo(String,String,Int64).default` |
| `public async Task SendLocation(string UserId, string Title, string Address, decimal Latitude, decimal Longitude)` | 共有；M1.SendLocation | `tool.SendLocation(String,String,String,Decimal,Decimal).default` |
| `public async Task SendSticker( string UserId, int PackageId, int StickerId )` | 共有；M1.SendSticker | `tool.SendSticker(String,Int32,Int32).default` |
| `public async Task PostSerializedTemplate(Entity aLetterEntity, string UserId, String AltText, String ThumbnailImageUrl, String Title, String Text, List<ITemplateAction> aITemplateAction)` | 共有；M1.PostSerializedTemplate | `tool.PostSerializedTemplate(String,String,String,String,String,List-1).default` |
| `public async Task PostSerializedTemplate(string UserId, String AltText, String ThumbnailImageUrl, String Title, String Text, List<ITemplateAction> aITemplateAction)` | 共有；M1.PostSerializedTemplate | `tool.PostSerializedTemplate(String,String,String,String,String,List-1).default` |
| `public async Task PostSerializedFlex( string UserId, FlexMessage aFlexMessage)` | 共有；M4.PostSerializedFlex | `line.PostSerializedFlex(String,FlexMessage).default` |
| `public async Task PostSerializedFlex(Entity aLetterEntity, string UserId, String ThumbnailImageUrl, String Title, String Text, List<ITemplateAction> aITemplateAction)` | 純產品組裝不搬；已組好的 Flex 由 M4.PostSerializedFlex／processor 承接 | 差異 E3；`line.PostSerializedFlex(String,FlexMessage).default` 只證明傳輸，不假稱 CRM 佈局相同 |
| `public FlexMessage CreateFlexMessage(Entity aLetterEntity, string UserId, String ThumbnailImageUrl, String Title, String Text, List<ITemplateAction> aITemplateAction)` | 純產品不搬；CRM party／收件者／Flex 組裝不是共用傳輸 | 差異 E3：僅保留主方案現有 CRM 方法及 SDK Flex 傳輸；不宣稱外部查詢已搬入 |
| `public async Task PostSerializedConfirm(string UserId, String AltText, String Text, List<ITemplateAction> aITemplateAction )` | 共有；M1.PostSerializedConfirm | `tool.PostSerializedConfirm(String,String,String,List-1).default` |
| `public async Task PostSerializedImageMap(string UserId, string AltText, string ImageUrl, int BaseWidth, int Basehight, List<IImagemapAction> aImagemapAction)` | 共有；M1.PostSerializedImageMap | `tool.PostSerializedImageMap(String,String,String,Int32,Int32,List-1).default` |
| `public async Task<String> AddRichMenuMessage(string UserId)` | 已有其他入口承接；主 M1 保留 legacy 次序，M2/M4 既有 assignment 不倒退 | `tool.AddRichMenuMessage(String).default`／`tool.DeleteRichMenuMessage(String).default`；`push/line` 同名黃金。外部來源未重建成獨立 API |
| `public async Task<String> DeleteRichMenuMessage(string UserId)` | 已有其他入口承接；主 M1 保留 legacy 次序，M2/M4 既有 assignment 不倒退 | `tool.AddRichMenuMessage(String).default`／`tool.DeleteRichMenuMessage(String).default`；`push/line` 同名黃金。外部來源未重建成獨立 API |
| `public String GetThumbnailImageUrl(ref Entity aLessonEntity)` | 純產品不搬；CRM party／收件者／Flex 組裝不是共用傳輸 | 差異 E3：僅保留主方案現有 CRM 方法及 SDK Flex 傳輸；不宣稱外部查詢已搬入 |
| `public void SetupActionList( Entity aLetterEntity, ref TemplateMessageClass aTemplateMessageClass)` | 純產品不搬；CRM party／收件者／Flex 組裝不是共用傳輸 | 差異 E3：僅保留主方案現有 CRM 方法及 SDK Flex 傳輸；不宣稱外部查詢已搬入 |
| `public String ConvertActionType( Entity aLetterEntity , String FieldName)` | 純產品不搬；CRM party／收件者／Flex 組裝不是共用傳輸 | 差異 E3：僅保留主方案現有 CRM 方法及 SDK Flex 傳輸；不宣稱外部查詢已搬入 |
| `public Entity GetLineSender(Entity aLetterEntity)` | 純產品不搬；CRM party／收件者／Flex 組裝不是共用傳輸 | 差異 E3：僅保留主方案現有 CRM 方法及 SDK Flex 傳輸；不宣稱外部查詢已搬入 |
| `public void GetLineIdAndContactFullNameOfSender(Entity aLetterEntity, ref String DisplayedLineId, ref String LineId, ref String ReplyToken, ref String ContactFullName)` | 純產品不搬；CRM party／收件者／Flex 組裝不是共用傳輸 | 差異 E3：僅保留主方案現有 CRM 方法及 SDK Flex 傳輸；不宣稱外部查詢已搬入 |
| `public string GetContactPartyFullName(Entity aContactParty, ref String LineId)` | 純產品不搬；CRM party／收件者／Flex 組裝不是共用傳輸 | 差異 E3：僅保留主方案現有 CRM 方法及 SDK Flex 傳輸；不宣稱外部查詢已搬入 |
| `public Entity GetLineReciever(Entity aLetterEntity)` | 純產品不搬；CRM party／收件者／Flex 組裝不是共用傳輸 | 差異 E3：僅保留主方案現有 CRM 方法及 SDK Flex 傳輸；不宣稱外部查詢已搬入 |
| `public void GetLineIdAndContactFullNameOfReciever(Entity aLetterEntity, ref String DisplayedLineId, ref String LineId, ref String ReplyToken, ref String ContactFullName)` | 純產品不搬；CRM party／收件者／Flex 組裝不是共用傳輸 | 差異 E3：僅保留主方案現有 CRM 方法及 SDK Flex 傳輸；不宣稱外部查詢已搬入 |
| `public async Task SetDemoRichMenu(String DisplayLineId, string UserId)` | 外部未使用 demo 不搬；功能改由現有 catalog／assignment／provisioning 管理 | 差異 E2：不復刻依顯示帳號掃除／固定圖片佈建；主方案 menu 黃金獨立 |

### W1 — `.其他解決方案/LineApiWebhook/LineUtility/PushUtility.cs`

- 來源 SHA-256：`59da3387f19b2e7fd9a499636a8e2192ce2da308250dd83fa7fe1ed4bc892c39`。
- 解碼：UTF-8（可能有BOM，來源保持原貌）；公開建構式／方法共 22 個。

| 公開方法完整簽章（含多載） | 最終分類／主方案承接入口 | 證據／黃金 |
|---|---|---|
| `public PushUtility(LineMessagingClient lineMessagingClient)` | 純產品／設定不搬；不復刻外部舊方案 Factory、token 或 contact manager 建構形狀 | 來源 no-HTTP；主方案 owner 既有測試，不宣稱舊方案組件已執行 |
| `public PushUtility(LineMessagingClient lineMessagingClient, bool enableStatistics)` | 純產品／設定不搬；不復刻外部舊方案 Factory、token 或 contact manager 建構形狀 | 來源 no-HTTP；主方案 owner 既有測試，不宣稱舊方案組件已執行 |
| `public async Task SendMessage(string userId, List<ISendMessage> messageToSend)` | 共有；M1.SendMessage | `tool.SendMessage(String,List-1).default`；同名多載見主方案表 |
| `public async Task SendMessage(string userId, string message)` | 共有；M1.SendMessage | `tool.SendMessage(String,List-1).default`；同名多載見主方案表 |
| `public async Task MultiCastTextMessageAsync(IList<string> userIds, string message)` | 共有；M1.MultiCastTextMessageAsync | `tool.MultiCastTextMessageAsync(IList-1,String).default`；主 M4 原有 split-push 差異保留 |
| `public async Task SendImage(string userId, string originalContentUrl, string previewImageUrl)` | 共有；M1.SendImage | `tool.SendImage(String,String,String).default` |
| `public async Task SendVideo(string userId, string originalContentUrl, string previewImageUrl)` | 共有；M1.SendVideo | `tool.SendVideo(String,String,String).default` |
| `public async Task SendAudio(string userId, string originalContentUrl, long duration)` | 已由其他入口承接（命名別名不另搬）；M1.SendAudeo | `tool.SendAudeo(String,String,Int64).default` |
| `public async Task SendAudeo(string userId, string originalContentUrl, long duration)` | 共有；M1.SendAudeo | `tool.SendAudeo(String,String,Int64).default` |
| `public async Task SendLocation(string userId, string title, string address, decimal latitude, decimal longitude)` | 共有；M1.SendLocation | `tool.SendLocation(String,String,String,Decimal,Decimal).default` |
| `public async Task SendSticker(string userId, int packageId, int stickerId)` | 共有；M1.SendSticker | `tool.SendSticker(String,Int32,Int32).default` |
| `public async Task PostSerializedTemplate(string userId, string altText, string thumbnailImageUrl, string title, string text, List<ITemplateAction> actions)` | 共有；M1.PostSerializedTemplate | `tool.PostSerializedTemplate(String,String,String,String,String,List-1).default` |
| `public async Task PostSerializedConfirm(string userId, string altText, string text, List<ITemplateAction> actions)` | 共有；M1.PostSerializedConfirm | `tool.PostSerializedConfirm(String,String,String,List-1).default` |
| `public async Task PostSerializedImageMap(string userId, string altText, string imageUrl, int baseWidth, int baseHeight, List<IImagemapAction> actions)` | 共有；M1.PostSerializedImageMap | `tool.PostSerializedImageMap(String,String,String,Int32,Int32,List-1).default` |
| `public async Task<string> AddRichMenuMessage(string userId)` | 已有其他入口承接；主 M1 保留 legacy 次序，M2/M4 既有 assignment 不倒退 | `tool.AddRichMenuMessage(String).default`／`tool.DeleteRichMenuMessage(String).default`；`push/line` 同名黃金。外部來源未重建成獨立 API |
| `public async Task<string> DeleteRichMenuMessage(string userId)` | 已有其他入口承接；主 M1 保留 legacy 次序，M2/M4 既有 assignment 不倒退 | `tool.AddRichMenuMessage(String).default`／`tool.DeleteRichMenuMessage(String).default`；`push/line` 同名黃金。外部來源未重建成獨立 API |
| `public async Task ConfirmMessageAsync(string userId)` | 共有；M1.ConfirmMessageAsync | `tool.ConfirmMessageAsync(String).default` |
| `public async Task CarouselMessageAsync(string userId)` | 共有；M1.CarouselMessageAsync | `tool.CarouselMessageAsync(String).default` |
| `public async Task ChurchCarouselMessageAsync(string userId)` | 共有；M1.ChurchCarouselMessageAsync | `tool.ChurchCarouselMessageAsync(String).default` |
| `public void ConfirmMessage(string userId)` | 共有；M1.ConfirmMessage | `tool.ConfirmMessage(String).default` |
| `public void CarouselMessage(string userId)` | 共有；M1.CarouselMessage | `tool.CarouselMessage(String).default` |
| `public void ChurchCarouselMessage(string userId)` | 共有；M1.ChurchCarouselMessage | `tool.ChurchCarouselMessage(String).default` |

### W2 — `.其他解決方案/LineApiWebhook/LineUtility/ReplyUtility.cs`

- 來源 SHA-256：`1b7f49810c5d24837ebe9d35517cbac4c0837caa4bc2724717d5e99adc5888c7`。
- 解碼：UTF-8（可能有BOM，來源保持原貌）；公開建構式／方法共 11 個。

| 公開方法完整簽章（含多載） | 最終分類／主方案承接入口 | 證據／黃金 |
|---|---|---|
| `public ReplyUtility(LineMessagingClient lineMessagingClient)` | 純產品／設定不搬；不復刻外部舊方案 Factory、token 或 contact manager 建構形狀 | 來源 no-HTTP；主方案 owner 既有測試，不宣稱舊方案組件已執行 |
| `public async Task EchoAsyncProcessor(MessageEvent ev)` | 共有；M3.EchoAsyncProcessor | `reply.EchoAsyncProcessor(MessageEvent).default`；同名多載見主方案表 |
| `public async Task ReplyMessage(string replyToken, List<ISendMessage> messageToSend)` | 共有；M3.ReplyMessage | `reply.ReplyMessage(String,List-1).default` |
| `public async Task ReplyMessageAsync(string replyToken, string textMessage)` | 共有；M3.ReplyMessageAsync | `reply.ReplyMessageAsync(String,String).default` |
| `public Task EchoAsync(string replyToken, string userMessage)` | 共有；M3.EchoAsync | `reply.EchoAsync(String,String).default` |
| `public async Task PostSerializedConfirm(string replyToken, string altText, string text, List<ITemplateAction> actions)` | 共有；M3.PostSerializedConfirm | `reply.PostSerializedConfirm(String,String,String,List-1).default` |
| `public async Task PostSerializedImageMap(string replyToken, string altText, string imageUrl, int baseWidth, int baseHeight, List<IImagemapAction> actions)` | 共有；M3.PostSerializedImageMap | `reply.PostSerializedImageMap(String,String,String,Int32,Int32,List-1).default` |
| `public async Task PostSerializedTemplate(string replyToken, string altText, string thumbnailImageUrl, string title, string text, List<ITemplateAction> actions)` | 已由其他入口承接；TemplateMessage(Buttons) → M3.ReplyMessage(list) | source-replay `reply-buttons`；原舊方法只是組裝＋reply，不另新增同功能 helper |
| `public async Task EchoImageAsync(string replyToken, string messageId, string blobDirectoryName)` | 共有；M3.EchoImageAsync | `reply.EchoImageAsync(String,String,String).default` |
| `public async Task UploadMediaContentAsync(string replyToken, string messageId, string blobDirectoryName, string blobName)` | 共有；M3.UploadMediaContentAsync | `reply.UploadMediaContentAsync(String,String,String,String).default` |
| `public async Task ReplyRandomStickerAsync(string replyToken)` | 共有；M3.ReplyRandomStickerAsync | `reply.ReplyRandomStickerAsync(String).default` |

### W3 — `.其他解決方案/LineApiWebhook/LineUtility/Core/LineUtilityClass.cs`

- 來源 SHA-256：`db9019d3a2c5867d1b762f5a2ba7488153944ea163031356b421b454171c9e50`。
- 解碼：CP950，嚴格解碼且逐位元組往返成功；來源保持原貌；公開建構式／方法共 58 個。

| 公開方法完整簽章（含多載） | 最終分類／主方案承接入口 | 證據／黃金 |
|---|---|---|
| `public LineUtilityClass(string channelAccessToken)` | 純產品／設定不搬；不復刻外部舊方案 Factory、token 或 contact manager 建構形狀 | 來源 no-HTTP；主方案 owner 既有測試，不宣稱舊方案組件已執行 |
| `public LineUtilityClass(string channelAccessToken, System.Net.Http.IHttpClientFactory httpClientFactory, Microsoft.Extensions.Caching.Memory.IMemoryCache? memoryCache, LineUtility.Core.Abstractions.ILineContactManager contactManager, string? clientName = null)` | 純產品／設定不搬；不復刻外部舊方案 Factory、token 或 contact manager 建構形狀 | 來源 no-HTTP；主方案 owner 既有測試，不宣稱舊方案組件已執行 |
| `public async Task SendTextMessageAsync(string userId, string message)` | 已由其他入口承接（命名別名不另搬）；M1.SendMessage | `tool.SendMessage(String,List-1).default`；同名多載見主方案表 |
| `public void SendMessage(string userId, string message)` | 共有；M1.SendMessage | `tool.SendMessage(String,List-1).default`；同名多載見主方案表 |
| `public async Task SendMessagesAsync(string userId, List<ISendMessage> messages)` | 已由其他入口承接（命名別名不另搬）；M1.SendMessage | `tool.SendMessage(String,List-1).default`；同名多載見主方案表 |
| `public async Task MultiCastTextMessageAsync(IList<string> userIds, string message)` | 共有；M1.MultiCastTextMessageAsync | `tool.MultiCastTextMessageAsync(IList-1,String).default`；主 M4 原有 split-push 差異保留 |
| `public async Task SendImageAsync(string userId, string originalUrl, string previewUrl)` | 已由其他入口承接（命名別名不另搬）；M1.SendImage | `tool.SendImage(String,String,String).default` |
| `public async Task SendVideoAsync(string userId, string originalUrl, string previewUrl)` | 已由其他入口承接（命名別名不另搬）；M1.SendVideo | `tool.SendVideo(String,String,String).default` |
| `public async Task SendAudioAsync(string userId, string originalUrl, long duration)` | 已由其他入口承接（命名別名不另搬）；M1.SendAudeo | `tool.SendAudeo(String,String,Int64).default` |
| `public async Task SendLocationAsync(string userId, string title, string address, decimal latitude, decimal longitude)` | 已由其他入口承接（命名別名不另搬）；M1.SendLocation | `tool.SendLocation(String,String,String,Decimal,Decimal).default` |
| `public async Task SendStickerAsync(string userId, int packageId, int stickerId)` | 已由其他入口承接（命名別名不另搬）；M1.SendSticker | `tool.SendSticker(String,Int32,Int32).default` |
| `public async Task SendFlexMessageAsync(string userId, FlexMessage flexMessage)` | 已由其他入口承接（命名別名不另搬）；M4.PostSerializedFlex | `line.PostSerializedFlex(String,FlexMessage).default` |
| `public async Task ReplyMessagesAsync(string replyToken, List<ISendMessage> messages)` | 已由其他入口承接（命名別名不另搬）；M3.ReplyMessage | `reply.ReplyMessage(String,List-1).default` |
| `public async Task ReplyTextMessageAsync(string replyToken, string message)` | 已由其他入口承接（命名別名不另搬）；M3.ReplyMessageAsync | `reply.ReplyMessageAsync(String,String).default` |
| `public void ReplyTextMessage(string replyToken, string message)` | 已由其他入口承接（命名別名不另搬）；M3.ReplyMessageAsync | `reply.ReplyMessageAsync(String,String).default` |
| `public Task ReplyTextMessage(string replyToken, string message, bool _)` | 已由其他入口承接（命名別名不另搬）；M3.ReplyMessageAsync | `reply.ReplyMessageAsync(String,String).default` |
| `public async Task ReplyImageAsync(string replyToken, string originalUrl, string previewUrl)` | 已由其他入口承接（命名別名不另搬）；M4.ReplyImage | `line.ReplyImage(String,String,String).default` |
| `public async Task SendButtonsTemplateAsync(string userId, string altText, string thumbnailUrl, string title, string text, List<ITemplateAction> actions)` | 已由其他入口承接（命名別名不另搬）；M1.PostSerializedTemplate | `tool.PostSerializedTemplate(String,String,String,String,String,List-1).default` |
| `public async Task SendConfirmTemplateAsync(string userId, string altText, string text, List<ITemplateAction> actions)` | 已由其他入口承接（命名別名不另搬）；M1.PostSerializedConfirm | `tool.PostSerializedConfirm(String,String,String,List-1).default` |
| `public async Task SendImageMapAsync(string userId, string altText, string imageUrl, int baseWidth, int baseHeight, List<IImagemapAction> actions)` | 已由其他入口承接（命名別名不另搬）；M1.PostSerializedImageMap | `tool.PostSerializedImageMap(String,String,String,Int32,Int32,List-1).default` |
| `public FlexMessage CreateFlexMessage(Entity letterEntity, string userId, string thumbnailUrl, string title, string text, List<ITemplateAction> actions)` | 純產品不搬；CRM party／收件者／Flex 組裝不是共用傳輸 | 差異 E3：僅保留主方案現有 CRM 方法及 SDK Flex 傳輸；不宣稱外部查詢已搬入 |
| `public BubbleContainer CreateBubbleContainer(Entity letterEntity, string lineId, string thumbnailUrl, string title, string text, List<ITemplateAction> actions)` | 純產品不搬；CRM party／收件者／Flex 組裝不是共用傳輸 | 差異 E3：僅保留主方案現有 CRM 方法及 SDK Flex 傳輸；不宣稱外部查詢已搬入 |
| `public Entity? GetLineSender(Entity letterEntity)` | 純產品不搬；CRM party／收件者／Flex 組裝不是共用傳輸 | 差異 E3：僅保留主方案現有 CRM 方法及 SDK Flex 傳輸；不宣稱外部查詢已搬入 |
| `public Entity? GetLineReceiver(Entity letterEntity)` | 純產品不搬；CRM party／收件者／Flex 組裝不是共用傳輸 | 差異 E3：僅保留主方案現有 CRM 方法及 SDK Flex 傳輸；不宣稱外部查詢已搬入 |
| `public void GetSenderInfo(Entity letterEntity, out string displayedLineId, out string lineId, out string replyToken, out string contactFullName)` | 純產品不搬；CRM party／收件者／Flex 組裝不是共用傳輸 | 差異 E3：僅保留主方案現有 CRM 方法及 SDK Flex 傳輸；不宣稱外部查詢已搬入 |
| `public void GetReceiverInfo(Entity letterEntity, out string displayedLineId, out string lineId, out string replyToken, out string contactFullName)` | 純產品不搬；CRM party／收件者／Flex 組裝不是共用傳輸 | 差異 E3：僅保留主方案現有 CRM 方法及 SDK Flex 傳輸；不宣稱外部查詢已搬入 |
| `public async Task<List<Line.Messaging.UserProfile>?> GetUserProfileAsync(string userId)` | 已由其他入口承接；processor.GetUserProfileAsync，產品回傳 List 包裝不復刻 | 既有 `LineMessagingProcessorIdentityProfileTests` HTTP／profile 特性 |
| `public async Task SetRichMenuAsync(string displayLineId, string userId)` | 外部未使用 demo 不搬；功能改由現有 catalog／assignment／provisioning 管理 | 差異 E2：不復刻依顯示帳號掃除／固定圖片佈建；主方案 menu 黃金獨立 |
| `public async Task DeleteRichMenuAsync(string displayLineId, string userId)` | 外部未使用 demo 不搬；功能改由現有 catalog／assignment／provisioning 管理 | 差異 E2：不復刻依顯示帳號掃除／固定圖片佈建；主方案 menu 黃金獨立 |
| `public async Task DeleteAllRichMenusAsync(string displayLineId, string userId)` | 外部未使用 demo 不搬；功能改由現有 catalog／assignment／provisioning 管理 | 差異 E2：不復刻依顯示帳號掃除／固定圖片佈建；主方案 menu 黃金獨立 |
| `public async Task<string> AddRichMenuMessageAsync(string userId)` | 已有其他入口承接；主 M1 保留 legacy 次序，M2/M4 既有 assignment 不倒退 | `tool.AddRichMenuMessage(String).default`／`tool.DeleteRichMenuMessage(String).default`；`push/line` 同名黃金。外部來源未重建成獨立 API |
| `public async Task<string> DeleteRichMenuMessageAsync(string userId)` | 已有其他入口承接；主 M1 保留 legacy 次序，M2/M4 既有 assignment 不倒退 | `tool.AddRichMenuMessage(String).default`／`tool.DeleteRichMenuMessage(String).default`；`push/line` 同名黃金。外部來源未重建成獨立 API |
| `public async Task ReplyMessage(string replyToken, List<ISendMessage> messages)` | 共有；M3.ReplyMessage | `reply.ReplyMessage(String,List-1).default` |
| `public async Task SendMessage(string userId, List<ISendMessage> messages)` | 共有；M1.SendMessage | `tool.SendMessage(String,List-1).default`；同名多載見主方案表 |
| `public async Task SendImage(string userId, string originalUrl, string previewUrl)` | 共有；M1.SendImage | `tool.SendImage(String,String,String).default` |
| `public async Task ReplyImage(string replyToken, string originalUrl, string previewUrl)` | 共有；M4.ReplyImage | `line.ReplyImage(String,String,String).default` |
| `public async Task SendVideo(string userId, string originalUrl, string previewUrl)` | 共有；M1.SendVideo | `tool.SendVideo(String,String,String).default` |
| `public async Task SendAudeo(string userId, string originalUrl, long duration)` | 共有；M1.SendAudeo | `tool.SendAudeo(String,String,Int64).default` |
| `public async Task SendLocation(string userId, string title, string address, decimal latitude, decimal longitude)` | 共有；M1.SendLocation | `tool.SendLocation(String,String,String,Decimal,Decimal).default` |
| `public async Task SendSticker(string userId, int packageId, int stickerId)` | 共有；M1.SendSticker | `tool.SendSticker(String,Int32,Int32).default` |
| `public async Task PostSerializedTemplate(string userId, string altText, string thumbnailUrl, string title, string text, List<ITemplateAction> actions)` | 共有；M1.PostSerializedTemplate | `tool.PostSerializedTemplate(String,String,String,String,String,List-1).default` |
| `public async Task PostSerializedTemplate(Entity letterEntity, string userId, string altText, string thumbnailUrl, string title, string text, List<ITemplateAction> actions)` | 共有；M1.PostSerializedTemplate | `tool.PostSerializedTemplate(String,String,String,String,String,List-1).default` |
| `public async Task PostSerializedFlex(string userId, FlexMessage flexMessage)` | 共有；M4.PostSerializedFlex | `line.PostSerializedFlex(String,FlexMessage).default` |
| `public async Task PostSerializedFlex(Entity letterEntity, string userId, string thumbnailUrl, string title, string text, List<ITemplateAction> actions)` | 純產品組裝不搬；已組好的 Flex 由 M4.PostSerializedFlex／processor 承接 | 差異 E3；`line.PostSerializedFlex(String,FlexMessage).default` 只證明傳輸，不假稱 CRM 佈局相同 |
| `public async Task PostSerializedConfirm(string userId, string altText, string text, List<ITemplateAction> actions)` | 共有；M1.PostSerializedConfirm | `tool.PostSerializedConfirm(String,String,String,List-1).default` |
| `public async Task PostSerializedImageMap(string userId, string altText, string imageUrl, int baseWidth, int baseHeight, List<IImagemapAction> actions)` | 共有；M1.PostSerializedImageMap | `tool.PostSerializedImageMap(String,String,String,Int32,Int32,List-1).default` |
| `public async Task<string> AddRichMenuMessage(string userId)` | 已有其他入口承接；主 M1 保留 legacy 次序，M2/M4 既有 assignment 不倒退 | `tool.AddRichMenuMessage(String).default`／`tool.DeleteRichMenuMessage(String).default`；`push/line` 同名黃金。外部來源未重建成獨立 API |
| `public async Task<string> DeleteRichMenuMessage(string userId)` | 已有其他入口承接；主 M1 保留 legacy 次序，M2/M4 既有 assignment 不倒退 | `tool.AddRichMenuMessage(String).default`／`tool.DeleteRichMenuMessage(String).default`；`push/line` 同名黃金。外部來源未重建成獨立 API |
| `public async Task SetDemoRichMenu(string displayLineId, string userId)` | 外部未使用 demo 不搬；功能改由現有 catalog／assignment／provisioning 管理 | 差異 E2：不復刻依顯示帳號掃除／固定圖片佈建；主方案 menu 黃金獨立 |
| `public async Task DeleteDemoRichMenu(string displayLineId, string userId)` | 外部未使用 demo 不搬；功能改由現有 catalog／assignment／provisioning 管理 | 差異 E2：不復刻依顯示帳號掃除／固定圖片佈建；主方案 menu 黃金獨立 |
| `public async Task DeleteAllDemoRichMenu(string displayLineId, string userId)` | 外部未使用 demo 不搬；功能改由現有 catalog／assignment／provisioning 管理 | 差異 E2：不復刻依顯示帳號掃除／固定圖片佈建；主方案 menu 黃金獨立 |
| `public List<Line.Messaging.UserProfile>? GetUserProfile(string userId)` | 已由其他入口承接；processor.GetUserProfileAsync，產品回傳 List 包裝不復刻 | 既有 `LineMessagingProcessorIdentityProfileTests` HTTP／profile 特性 |
| `public void GetLineIdAndContactFullNameOfSender(Entity letterEntity, out string displayedLineId, out string lineId, out string replyToken, out string contactFullName)` | 純產品不搬；CRM party／收件者／Flex 組裝不是共用傳輸 | 差異 E3：僅保留主方案現有 CRM 方法及 SDK Flex 傳輸；不宣稱外部查詢已搬入 |
| `public void GetLineIdAndContactFullNameOfReciever(Entity letterEntity, out string displayedLineId, out string lineId, out string replyToken, out string contactFullName)` | 純產品不搬；CRM party／收件者／Flex 組裝不是共用傳輸 | 差異 E3：僅保留主方案現有 CRM 方法及 SDK Flex 傳輸；不宣稱外部查詢已搬入 |
| `public string GetThumbnailImageUrl(ref Entity? lessonEntity)` | 純產品不搬；CRM party／收件者／Flex 組裝不是共用傳輸 | 差異 E3：僅保留主方案現有 CRM 方法及 SDK Flex 傳輸；不宣稱外部查詢已搬入 |
| `public void SetupActionList(Entity letterEntity, ref Models.TemplateMessageClass templateMessageClass)` | 純產品不搬；CRM party／收件者／Flex 組裝不是共用傳輸 | 差異 E3：僅保留主方案現有 CRM 方法及 SDK Flex 傳輸；不宣稱外部查詢已搬入 |
| `public string ConvertActionType(Entity letterEntity, string fieldName)` | 純產品不搬；CRM party／收件者／Flex 組裝不是共用傳輸 | 差異 E3：僅保留主方案現有 CRM 方法及 SDK Flex 傳輸；不宣稱外部查詢已搬入 |
| `public void Dispose()` | 純產品／設定不搬；不復刻外部舊方案 Factory、token 或 contact manager 建構形狀 | 來源 no-HTTP；主方案 owner 既有測試，不宣稱舊方案組件已執行 |


## 核定邊界與明確差異（不視為未實作的搬移）

- 使用者已授權本包獨佔 ToolUtility.csproj，XML 專案圖檢查 processor／Workflows 無返回 ToolUtility 的路徑，新增參考沒有循環。
- 正式會友主鏈已由 P2-04 Core 承接；legacy arbitrary／group／多則／multicast／reply／download 留 net10。沒有偽造 church/contact ID、反射讀 token、Core/SDK 修改或第二套 HTTP。
- **E1**：D4、D5 的 Emojis 多載不搬；這是外部舊能力與主 SDK 型別的明確差異。人類原文「而非一律新增SDK缺少Emojis」並非廢止授權，是否接受此未搬缺口仍待確認；本包不自行修改 SDK 補洞。
- **E2**：W3/D5 外部 demo 的 displayLineId 掃描／全選單刪除／固定檔案佈建不復活。**不代表可退役主方案公開方法**：M1 Add/Delete 均保留成功與失敗次序，M2/M4 已有 assignment 保持不變。
- **E3**：D5/W3 的 CRM Flex builder、receiver/thumbnail/contact manager 等只屬產品，不搬共用。主 M4 現有五個 CRM-only 方法已逐項 NO HTTP＋結果驗證。
- **E4**：D3 的 Sender 多載與 D3/W2 Buttons reply 由目前 SDK 模型＋M3 generic list 承接，補直接 SDK 來源重播黃金；不增加重複 API。Sender 是既有顯示欄位，不是新增授權或注入 sender。
- W1 SendAudio／W3 SendAudioAsync 與舊拼字 SendAudeo 由同一 AudioMessage payload 承接；同步／async 命名包裝不複製第二套傳輸。

## 主方案呼叫點與保留路徑

| 呼叫族群 | 主方案入口 → 唯一共用入口 | 本包處理 |
|---|---|---|
| ToolUtility 舊文字／媒體／樣板／同步範例 | M1 → LineLegacySendWorkflow.SendAsync → processor.SendMessagesAsync → SDK | 去除直接 SDK 傳輸副本；保留產品文案、統計及原 best-effort／重拋 |
| ToolUtility 多播 | M1 → LineLegacySendWorkflow.MultiCastAsync → processor.MultiCastMessagesAsync → SDK | 單次 multicast、順序及重複收件者保持 |
| ToolUtility 主方案 RichMenu | M1 → 新 legacy workflow 方法 → 既有 processor RichMenu／SDK | create/upload/link/notify 及 query/unlink/delete 保留；有限圖片 stream 唯一 using owner |
| ChurchReport Controllers／Donation／Payment／Tools | M2/M4 → 既有 LineNotificationWorkflow → processor | 不重寫 HTTP；必要通知與 retry key 保持 |
| Webhook 回音／產品 reply | M3/M4 → 既有 LineReplyWorkflow → processor | profile user/group/room 及 reply token 本文不變 |
| ChurchReport 媒體回音 | M3 → processor.UseContentAsync → SDK content | stream/response 與 image/thumbnail 在成功／故障均確定性釋放；舊 blob no-op 保持 |
| ChurchReport 認證 menu | M2/M4 → 既有 assignment → RichMenus adapter → processor | 不退回 ToolUtility demo 或建立第二套 HTTP |

可重現呼叫搜尋：`rg -n "new (PushUtility|ReplyUtility|LineUtilityClass)|SendReliableMessageAsync|AddRichMenuMessage|DeleteRichMenuMessage" ToolUtility SpeechMessageProducts.ChurchReport --glob "*.cs"`。舊類本身標 Obsolete，其他呼叫端無需為本包進行大規模命名改寫；它們已經由薄殼匯入上述入口。

## 黃金與故障證據的誠實邊界

1. `P206-pre-migration.http.golden` 原 66 案例在任何產品修改前執行全綠（p206-baseline-fixed.trx）。後續只對 random sticker 正規化「先驗正確 Content-Length 後遮罩隨機 ID 及其衍生長度」，其餘 URL／標頭／body 不放寬。
2. M1 Add 原先無注入接縫：後補第 67 案，以原 HEAD 的 Add 方法只將固定 File.ReadAllBytes/MemoryStream 改為注入有限 stream 後實跑；create/upload/link/notify 與黃金相同。原邏輯的成功及 upload fault 皆未 Dispose stream，兩個 lifecycle 紅燈已記錄 p206-original-menu-red.trx。最終委派用 using 修復，不改 HTTP。
3. `P206-source-replay.http.golden` 五案是原直接 SDK 演算法的合成重播（非舊組件二進位實測），同時比較直接 SDK、目前相容入口與唯讀 fixture：group push、含重複收件 multicast、Buttons reply、Sender reply、menu-add。二進位 PNG 完整 Base64 比對，不使用雜湊隱藏 body。
4. EchoImage 解碼失敗的原 response 未釋放紅燈；最終成功／故障均已新增釋放測試。兩頻道並行 40 push＋20 download 驗證 token／本文隔離及媒體 drain，不冒稱長時間實機壓測。
5. 所有存續主方案送訊方法、同步別名、多載及 no-op 均有黃金；CRM-only 五方法以結果＋NO HTTP 斷言。部署／Factory 建構與 Setup/Dispose 無 HTTP，保留既有 owner 測試及來源檢查；不為跑建構式而讀正式 token 或連 CRM。
6. 曾誤將 M1 RichMenu 退役成 NotSupportedException，使用者指出屬未核定決策後已撤回；p206-contract-red 中兩個退役測試不是核定需求，不計入完成證據，原始紀錄保留不竄改。
7. **沒有刻意新增 retry key 或 sender**。既有 reliable key 的 golden 保持，補充 sender case 只是證明原顯示值可透過 generic list 保留。清理 ErrorString／throw e 僅將原真實 Exception 交共用診斷並保留堆疊，best-effort 不改拋出策略。

尚待主代理乾淨複本全方案守門（含 I18n／重複／encoding）；本研究不是整包已驗收或外部舊方案已全部搬入的聲明。

## 與「推播類全改 Core」仍存在的落差

03 P2-06 第2點原文要求推播類方法改呼叫 Core。本包依人類後續原文「舊 arbitrary group/multi/reply/download確實應保留net10」及「可表達的正式會友推播主鏈已在P2-04新增，legacy相容用net10 processor，不從private token反射」保留 legacy；這是明確的執行邊界，但**計畫原文尚未被本包改版，不能聲稱已逐字完全符合**。

| 尚未直接改 Core 的範圍 | 實際入口／原因 | 尚待主代理裁定 |
|---|---|---|
| M1 SendMessage(text/list)、media/location/sticker/template、同步樣板及 multicast | Legacy workflow→net10 processor→SDK；舊 SDK client 沒有公開 token，arbitrary/group 不具正式會友 scope | legacy 邊界是否足以驗收原計畫第2點，或需要使用者計畫修正 |
| M2/M4 SendMessage／必要通知／reliable／media/template/Flex／split-push | 保留既有 notification workflow→net10 processor；必要 key 不另補造，沒有正式 contact/church/channel 輸入 | 即使文字能表示為 Core text，也不能只憑 userId 偽造正式授權；此批產品呼叫的正式 scope 改造不在本包授權範圍 |
| M1 RichMenu 成功通知與舊範例通知 | 仍是原 SDK 多則 push；只移傳輸入口，不改公開業務行為 | 不能以「不復活外部 demo」作為退役主方案成功方法授權 |
| D4/D5 Emojis | 目前 SDK 不支援舊型別，未搬；功能未取得廢止核定 | 是否接受缺口待確認，不冒稱已保留或已核定排除 |

Reply／download／多則留 net10 與原計畫一致；正式會友 Core 主鏈是 P2-04 既有成果，本包未更改也未用假 ID 重測該授權鏈。以上落差不以額外功能或新測試矩陣擴張處理。
