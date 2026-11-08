/*
 * 檔案整體說明
 * 目的與定位：保護PROP-005的唯一legacy任意推播流程，不把正式會友Core與舊SDK入口混為一談。
 * 主要功能與流程：反射檢查通知流程只持有共用Legacy流程，再用真processor及假HTTP驗證原Retry Key、
 * 多則與群組收件者不被重寫，確認HTTP response在完成後已釋放；另以實際CLR載入保護
 * legacy實作下移processor後的唯一型別與舊Workflows組件限定名稱相容，避免重建同層反向參考。
 * 使用邊界與維護：所有識別與內容均為合成值，不連LINE或CRM；案例擁有SDK、HttpClient及handler，
 * using在await排空後反向Dispose，不保存跨案例憑證、訊息或活動；反射不讀private token。
 */
using System.Net;
using System.Reflection;
using Line.Messaging;
using Newtonsoft.Json.Linq;
using Xunit;

namespace LineMessagingProcessor.Workflows.Tests;

/// <summary>以精確架構斷言與真序列化保護唯一委派和原wire，沒有替代產品授權。</summary>
public sealed class LegacyWorkflowRouteTests
{
    /// <summary>
    /// 保護既有已編譯呼叫端的型別身分與相依方向：實作必須屬於較低層processor，
    /// 舊Workflows限定名稱須透過真實型別轉送解析為同一型別；沒有建立client或外部I/O。
    /// 若誤把實作放回Workflows、複製第二份或移除轉送，精確組件及Same斷言必須失敗。
    /// </summary>
    [Fact]
    public void LegacyWorkflow_LowerLayerImplementation_OldAssemblyNameResolvesSameType()
    {
        Assert.Equal("LineMessagingProcessor", typeof(LineLegacySendWorkflow).Assembly.GetName().Name);
        var oldType = Type.GetType(
            "LineMessagingProcessor.Workflows.LineLegacySendWorkflow, LineMessagingProcessor.Workflows",
            throwOnError: true);
        Assert.Same(typeof(LineLegacySendWorkflow), oldType);
        Assert.Contains(typeof(LineLegacySendWorkflow), typeof(LineNotificationWorkflow).Assembly.GetForwardedTypes());
    }

    /// <summary>通知workflow不得再直接持有processor，避免legacy有兩條平行推播入口。</summary>
    [Fact]
    public void Notification_HoldsSingleLegacyWorkflow_NotParallelProcessor()
    {
        var fields = typeof(LineNotificationWorkflow).GetFields(BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.Single(fields, field => field.FieldType == typeof(LineLegacySendWorkflow));
        Assert.DoesNotContain(fields, field => field.FieldType == typeof(LineMessagingProcessorClass));
    }

    /// <summary>從現有通知入口驗證群組、多則與來源Retry Key原樣保留；HTTP由案例owner等待排空。</summary>
    [Fact]
    public async Task Notification_GroupAndMultipleMessages_PreservesWireAndRetryKey()
    {
        using var handler = new Handler();
        using var http = new HttpClient(handler);
        using var sdk = new LineMessagingClient(http, "synthetic-token", "https://api.line.me/v2");
        var workflow = new LineNotificationWorkflow(new LineMessagingProcessorClass(sdk));
        var result = await workflow.SendAsync(new LineNotificationRequest
        {
            Recipient = LineNotificationRecipient.Group("C-synthetic"),
            Content = LineNotificationContent.SdkMessagesList(new List<ISendMessage> { new TextMessage("first"), new TextMessage("second") }),
            RetryKey = "synthetic-source-key"
        });
        Assert.True(result.Succeeded);
        var body = JObject.Parse(handler.Body!);
        Assert.Equal("C-synthetic", (string?)body["to"]);
        Assert.Equal(new[] { "first", "second" }, body["messages"]!.Select(item => (string?)item["text"]));
        Assert.Equal("synthetic-source-key", handler.RetryKey);
        Assert.Equal(1, handler.DisposedResponses);
    }

    /// <summary>只記錄本案合成wire，回應所有權交SDK；fixture處置時清掉暫存本文。</summary>
    private sealed class Handler : HttpMessageHandler
    {
        internal string? Body;
        internal string? RetryKey;
        internal int DisposedResponses;
        /// <summary>只截取序列化結果，不保留request／header物件或建立外部連線。</summary>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            RetryKey = request.Headers.GetValues("X-Line-Retry-Key").Single();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new Content(this) };
        }
        /// <summary>不讓fixture快照跨案例保留；不代替SDK處置response。</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing) Body = RetryKey = null;
            base.Dispose(disposing);
        }
        /// <summary>只由SDK確定性關閉時增加計數，不能靠fixture預先Dispose製造綠燈。</summary>
        private sealed class Content(Handler owner) : StringContent("{}")
        {
            /// <summary>回報真response釋放，沒有額外背景工作或全域計數。</summary>
            protected override void Dispose(bool disposing)
            {
                if (disposing) owner.DisposedResponses++;
                base.Dispose(disposing);
            }
        }
    }
}
