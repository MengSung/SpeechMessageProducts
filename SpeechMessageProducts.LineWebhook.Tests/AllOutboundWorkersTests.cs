/*
 * 檔案整體說明
 * 目的與定位：P7真LineWebhook組合根的全部對外待辦開關唯一consumer測試；不Start HTTP或SQL。
 * 主要流程：全開付款、課程QR、聚會QR與代送，真容器應只有三個worker；逐個啟動一次
 * 空候選輪詢，以窄SDK記錄實際查詢allowlist，證明QR共用一個、付款與代送各一個。
 * 故障與斷言：未知SDK呼叫直接失敗，使用決定性信號等待每個worker完成首讀後停止；
 * 斷言三種服務類型各恰一次，不靠descriptor數量冒稱路由正確。
 * 資源與隔離：Host及worker由測試await using／finally停止並Dispose，無外部連線、秘密、
 * 真影像寫入或跨案例資料；固定invalid網址只是啟動純值，不代表實機發布。
 */
using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Hosting.Outbound;
using Xunit;
namespace SpeechMessageProducts.LineWebhook.Tests;

/// <summary>真產品Host的全部開關不能讓同類型工作被兩個worker認領。</summary>
public sealed class AllOutboundWorkersTests
{
    private static readonly int[] ServedTypes = { 100000001, 100000002, 100000003 };
    /// <summary>全部開關啟用後，真worker的查詢allowlist恰好QR／撥款／代送各一次。</summary>
    [Fact]
    public async Task Host_AllOutboundFlagsEnabled_EachServedTypeHasExactlyOneWorker()
    {
        var observed = new ConcurrentQueue<int>(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var id = "11111111-1111-1111-1111-111111111111";
        await using var host = new WebhookTestHost(configuration: new Dictionary<string, string?>
        {
            ["Payment:Outbound:Enabled"] = "true",
            ["Lessons:Qr:Enabled"] = "true",
            ["QrCodes:Qr:Enabled"] = "true",
            ["Messaging:NotifyRelay:Enabled"] = "true",
            ["Lessons:Qr:StorageDirectory"] = Path.GetTempPath(),
            ["Lessons:Qr:PublicBaseUrl"] = "https://qr.invalid/images/",
            [$"Lessons:Qr:Churches:{id}:RegularPrefix"] = "https://qr.invalid/?QrCodeId=",
            [$"Lessons:Qr:Churches:{id}:PollPrefix"] = "https://poll.invalid/?QrCodeId=",
            ["QrCodes:Qr:StorageDirectory"] = Path.GetTempPath(),
            ["QrCodes:Qr:PublicBaseUrl"] = "https://qr.invalid/images/",
            [$"QrCodes:Qr:Churches:{id}:MeetingPrefix"] = "https://qr.invalid/?QrCodeId=",
            [$"QrCodes:Qr:Churches:{id}:PersonalPrefix"] = "https://qr.invalid/?QrCodeId="
        }, configureServices: services => services.AddScoped<IOrganizationService>(_ =>
        {
            var proxy = DispatchProxy.Create<IOrganizationService, QueryProxy>(); ((QueryProxy)proxy).Read = value =>
            { observed.Enqueue(value); if (observed.Count == 3) entered.TrySetResult(); }; return proxy;
        }));
        var workers = host.App.Services.GetServices<IHostedService>().OfType<OutboundTaskWorker>().ToArray(); Assert.Equal(3, workers.Length);
        try
        {
            foreach (var worker in workers) await worker.StartAsync(CancellationToken.None);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(ServedTypes, observed.Order().ToArray());
        }
        finally { foreach (var worker in workers) await worker.StopAsync(CancellationToken.None); }
    }
    /// <summary>合成SDK只提供空待辦候選；未知或無allowlist查詢立即拒絕。</summary>
    public class QueryProxy : DispatchProxy
    {
        internal Action<int>? Read;
        /// <summary>記錄真SDK查詢條件，禁止其他I/O或錯誤欄位默默成功。</summary>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Assert.Equal(nameof(IOrganizationService.RetrieveMultiple), targetMethod!.Name);
            var q = Assert.IsType<QueryExpression>(args![0]); Assert.Equal("new_outboundtask", q.EntityName);
            var condition = Assert.Single(q.Criteria.Conditions); Assert.Equal("new_type", condition.AttributeName);
            Read!((int)Assert.Single(condition.Values)); return new EntityCollection();
        }
    }
}
