# P3-06／08 事件移植唯讀證據

2026-11-09 14:20 Asia/Taipei (UTC+08:00)：收回唯讀代理Jason調查並關閉。未修改來源、未讀設定／Secrets、未連D365／LINE／SQL，以下不代表實機現況，也不代表P3-06／08已開工。

舊來源根為上一層整合工作樹 `.其他解決方案/LineApiWebhook/`，以下舊路徑相對該根；Solution路徑相對本工作樹。以原檔行號追溯，移植前再由負責代理讀完整檔案及黃金資料。

## 十二事件

| 事件 | 舊來源與行號 | 可證明的既有行為 |
|---|---|---|
| message.text | LineApiWebhook/Handlers/LineBotMessageHandler.cs:97–116 | WebhookMessageProcessor→IncomingMessageProcessor.RouteMessageAsync直接執行業務；未統一先寫letter |
| message.image | 同檔154–157 | 註解no-op，未下載／回覆／寫CRM |
| message.audio | 同檔159–162 | 註解no-op，未呼叫GetMessageContent；04明列的語音鏈須另接通 |
| message.location | 同檔139–147 | 註解no-op；writer存在不代表入口已用 |
| message.sticker | 同檔149–152 | 註解no-op |
| follow | LineApiWebhook/LineBotApp.FollowEvents.cs:19–83、115–129 | 查contact、取profile，建立或更新profile／狀態並回覆；缺完整頻道／教會授權入口 |
| unfollow | 同檔89–103 | 查contact後更新new_line_status=100000002 |
| join | LineApiWebhook/LineBotApp.GroupEvents.cs:19–38、106–170 | 以Source.Id查contact，群組摘要後建立／更新並回覆；仍須驗群組scope |
| leave | 同檔51–56 | 僅Log，無CRM寫入；不可補造刪除行為 |
| postback | LineApiWebhook/LineBotApp.PostbackHandlers.cs:17–78、91–161 | 報名／繳費／兩種簽核／選單／日期時間；可更新fee、stor lesson、appointment |
| accountLink | LineApiWebhook/LineBotApp.DeviceAndLinkEvents.cs:34–42 | 按結果回覆，無CRM綁定寫入；不得改成不安全自動綁定 |
| memberJoined | LineApiWebhook/LineBotApp.GroupEvents.cs:68–75、196–211 | 群組／成員歡迎回覆，無CRM寫入 |

文字業務不是letter logging：`LineMessagePlugIn/Attendance/SundayPresent.cs:493、621–683、749–809`建立／更新new_present_record及週報；`PrayItem.cs:176–261`追加new_explanation；`ClassAttendanceHandler.cs:158–166`更新new_stor_lessons簽到及出席。

`LineApiWebhook/LineBotApp.cs:75–133`的簡式建構子未建立Dynamics processor，完整建構子還依賴LIFF／payment processor；部分postback直接解參考、部分空值時靜默跳過，不能統稱已接通。付款業務屬階段4，不在P3擅加。

## Solution 重用界線

- GoodShepherd.Core的Attendance／Appointments／Lessons／Messaging目前無C#業務實作，不能把底層CRUD說成已完成業務。
- `ToolUtility.Core/EntityOperations/EntityCrudService.cs:41–68`：`Guid CreateEntity(Entity)`、`void UpdateEntity(Entity)`只借入IOrganizationService，沒有church／contact／principal授權參數。
- `ToolUtility.Core/ContactOperations/IContactService.cs:49`的RetrieveByLineId、MeetingStatisticsOperations/IMeetingStatisticsService.cs:50的RetrieveBySundayDateAndContact、LessonsOperations/ILessonsService.cs:51的RetrieveStorLessons均只是查詢，不替呼叫端完成授權。
- `LineMessagingProcessor.Workflows/LineReplyWorkflow.cs:35、89`的ReplyAsync／ReplyOrThrowAsync可重用，但LineReplyRequest只含ReplyToken／Messages／Metadata，沒有church／contact／CancellationToken。上層須按可信scope組裝正確channel、字典、語系與sender。
- LineTemplateActionFactory.Postback只建按鈕，不解析入站、不授權、不寫CRM。

## 語音與附件

舊 `Crm2011Processor/Dynamics365ProcessorClass/Dynamics365ProcessorClass.MessageStore.cs:403–529`鏈為下載→RawBytes→CreateLineMessage建letter→Retrieve→UploadAnAttachment建annotation。421行Authorization直接放token，缺Bearer前綴；487–499行只辨識audio/x-m4a。缺下載容量／取消／明確RestClient清理，非成功HTTP靜默略過；附件失敗可能留下部分完成letter。

可重用 `Line.Messaging/LineMessagingClient.cs:978–980、2849–2859`的GetContentStreamAsync；ChannelHttpTransport.cs:48、73已逐request設Bearer。ContentStream.cs:198–220由caller Dispose，response owner釋放stream與response。`ToolUtility.Core/AttachmentOperations/AttachmentService.cs:134–168`的UploadAttachment借入SDK與caller bytes，預設單檔5 MiB；下載端仍須先有界讀，且此入口不含事件去重或取消。

## 不能憑空宣稱的保證

1. 舊MessageStore.cs:95、199、309設定directioncode=false，但舊LineMessagePlugIn/Core/LineMessagePlugInCore.cs:134–145正把false送入ProcessIncomingLine。**只設false不能證明D19不觸發Plugin。** 必須找到可驗的入站隔離／繞過契約，實際權限留指定實機包核對，不先宣稱成功。
2. 已讀letter writers未保存webhookEventId／LINE message ID；location／sticker的MessageId未用，媒體ID只用下載。現有D365OutboundEnqueueStore.cs:49–76的new_idempotencykey屬new_outboundtask，不能當letter欄位。事件唯一性不得猜schema。
3. 舊MessageStore.cs:83–85、231、351寫入reply／channel token；移植不得保存憑證欄位。
4. LineApiWebhook/SigningProcessorClass.cs:143–156以姓名授權，StorLessonSigningProcessor.cs:103–124依傳入ID更新且未見關係驗證；須用伺服器核實正式ID、教會、角色及記錄關係，不能照搬。
5. Crm2011Processor/LessonProcessorClass/LessonProcessorAndListClass.cs:569–592未await JoinToList／RemoveFromList；多處catch吞錯；LineBotApp.cs:47–50的static queue保留原始PostbackEvent。不可原樣移植。

上述缺口先由相應離線包以黃金與契約實作；無法證明的業務差異列提案，不用固定回覆／空委派代替真實業務，也不將靜態調查或mock當實機結果。
