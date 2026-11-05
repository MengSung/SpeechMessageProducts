# P1-01 刪除前引用證據

範圍：目前工作樹 Git 追蹤檔；有效專案／方案及 C# 共 960 檔，不跨父工作樹。
檢查每個候選名稱及 Trace 專用型別；候選本身的自我參考不當作外部使用。

## Trace/
搜尋：Trace.csproj, Trace_Fixed.csproj, Trace_Net10.csproj, BSUStackTrace, BSUTextWriterTraceListener
結果：0 個外部參考。

## LinePayCSharp/
搜尋：LinePayCSharp
結果：0 個外部參考。

## Line.Messaging/Line.Messaging_Net10.csproj
搜尋：Line.Messaging_Net10.csproj
結果：0 個外部參考。

## LineMessagingProcessor/LineMessagingProcessor_Net10.csproj
搜尋：LineMessagingProcessor_Net10.csproj
結果：0 個外部參考。

## SpeechMessage.Dynamics.Crm82Worker
搜尋：SpeechMessage.Dynamics.Crm82Worker
結果：0 個外部參考。

## SpeechMessage.Dynamics.Crm91Worker
搜尋：SpeechMessage.Dynamics.Crm91Worker
結果：0 個外部參考。

SpeechMessage.Dynamics.Crm82Worker：實際僅 packages.lock.json。
SpeechMessage.Dynamics.Crm82Worker.Tests：實際僅 packages.lock.json。
SpeechMessage.Dynamics.Crm91Worker：實際僅 packages.lock.json。
SpeechMessage.Dynamics.Crm91Worker.Tests：實際僅 packages.lock.json。

四個 worker 目錄在刪除前各只有鎖定檔，沒有 csproj／原始碼／方案項目；本次不是保留仍存活的 worker 專案，也沒有把其他 untracked 資料當成孤兒遞迴刪除。

待刪除追蹤檔：51
- Line.Messaging/Line.Messaging_Net10.csproj
- LineMessagingProcessor/LineMessagingProcessor_Net10.csproj
- LinePayCSharp/LinePayCSharp.csproj
- LinePayCSharp/LinePayClient.cs
- LinePayCSharp/Models/AuthorizationInfo.cs
- LinePayCSharp/Models/AuthorizationResponse.cs
- LinePayCSharp/Models/Capture.cs
- LinePayCSharp/Models/CaptureInfo.cs
- LinePayCSharp/Models/CaptureResponse.cs
- LinePayCSharp/Models/Confirm.cs
- LinePayCSharp/Models/ConfirmInfo.cs
- LinePayCSharp/Models/ConfirmPayInfo.cs
- LinePayCSharp/Models/ConfirmResponse.cs
- LinePayCSharp/Models/ConfirmUrlType.cs
- LinePayCSharp/Models/CreditCardBrand.cs
- LinePayCSharp/Models/Currency.cs
- LinePayCSharp/Models/LanguageCode.cs
- LinePayCSharp/Models/PayInfo.cs
- LinePayCSharp/Models/PayMethod.cs
- LinePayCSharp/Models/PayStatus.cs
- LinePayCSharp/Models/PayType.cs
- LinePayCSharp/Models/PaymentInfo.cs
- LinePayCSharp/Models/PaymentResponse.cs
- LinePayCSharp/Models/PaymentUrl.cs
- LinePayCSharp/Models/PreApprovedPay.cs
- LinePayCSharp/Models/PreApprovedPayInfo.cs
- LinePayCSharp/Models/PreApprovedPayResponse.cs
- LinePayCSharp/Models/Refund.cs
- LinePayCSharp/Models/RefundInfo.cs
- LinePayCSharp/Models/RefundList.cs
- LinePayCSharp/Models/RefundResponse.cs
- LinePayCSharp/Models/Reserve.cs
- LinePayCSharp/Models/ReserveAddFriend.cs
- LinePayCSharp/Models/ReserveExtras.cs
- LinePayCSharp/Models/ReserveInfo.cs
- LinePayCSharp/Models/ReserveResponse.cs
- LinePayCSharp/Models/ResponseBase.cs
- LinePayCSharp/Models/TransactionType.cs
- SpeechMessage.Dynamics.Crm82Worker.Tests/packages.lock.json
- SpeechMessage.Dynamics.Crm82Worker/packages.lock.json
- SpeechMessage.Dynamics.Crm91Worker.Tests/packages.lock.json
- SpeechMessage.Dynamics.Crm91Worker/packages.lock.json
- Trace/AssemblyInfo.cs
- Trace/BSUStackTrace.cs
- Trace/BSUTextWriterTraceListener.cs
- Trace/Trace.csproj
- Trace/Trace.csproj.new
- Trace/Trace.xml
- Trace/Trace_Fixed.csproj
- Trace/Trace_Net10.csproj
- Trace/packages.config

基準：26 projects、879 sourcefiles、0 duplicate groups/new/stale；全方案1125pass，ToolUtility孤兒63pass。
