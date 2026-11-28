/*
檔案整體說明
目的與定位：F11分產品安全告警的離線行為回歸，保護固定部署產品前綴與目的地優先序，不存取真實LINE、User Secrets或任何組織。
主要功能與流程：反射查新增optional產品API使未實作時為可編譯RED；成功後由真ExceptionDiagnostics產生安全JSON並送至合成sink，斷言前綴及無原錯誤秘密。另以自有HTTP handler捕捉synthetic目標，驗證產品設定與舊管理者後備。
使用邊界與維護：每案唯一擁有configuration、sender及diagnostics，通知排空後才Dispose傳輸，沒有長命callback、全域可變狀態或真憑證。測試資料全為canary；故障回傳及LINE接受不等於實機送達。
*/
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using ToolUtilityNameSpace.Diagnostics;
using Xunit;

namespace SpeechMessage.Hosting.Tests.Observability;

/// <summary>對真診斷owner及傳輸邊界作斷言，不以替身自行組訊息冒充產品輸出。</summary>
public sealed class ProductAlertTests
{
    /// <summary>安全元資料加固定產品前綴，exception原文及秘密canary都不可出現在通知。</summary>
    [Fact]
    public async Task Alert_IncludesProduct_NoSecrets()
    {
        var constructor = typeof(ExceptionOutputOptions).GetConstructor(new[] { typeof(bool), typeof(bool), typeof(string) });
        Assert.NotNull(constructor);
        var options = Assert.IsType<ExceptionOutputOptions>(constructor.Invoke(new object[] { false, true, "GoodShepherd2" }));
        await using var diagnostics = new ExceptionDiagnostics(Path.GetTempPath(), outputOptions: options);
        var delivered = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        diagnostics.StartNotifications((message, _) => { delivered.TrySetResult(message); return Task.CompletedTask; });
        Assert.True(diagnostics.Report(new InvalidOperationException("synthetic-secret-must-not-appear"), "Tests.Alert"));
        var actual = await delivered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.StartsWith("[GoodShepherd2] ", actual, StringComparison.Ordinal);
        Assert.DoesNotContain("synthetic-secret-must-not-appear", actual, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(actual["[GoodShepherd2] ".Length..]);
        Assert.Equal("Tests.Alert", json.RootElement.GetProperty("Operation").GetString());
    }

    /// <summary>兩個目的地皆為合成值，產品非空白時優先，空白回原管理者，不從訊息推導產品。</summary>
    [Theory]
    [InlineData("product-recipient", "product-recipient")]
    [InlineData(" ", "legacy-recipient")]
    [InlineData(null, "legacy-recipient")]
    public async Task Alert_ProductTarget_OrLegacyFallback(string? target, string expected)
    {
        var constructor = typeof(LineExceptionSender).GetConstructor(new[]
            { typeof(IConfiguration), typeof(string), typeof(HttpMessageHandler), typeof(string) });
        Assert.NotNull(constructor);
        using var config = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["LINE_CHANNEL_ACCESS_TOKEN"] = "synthetic-token",
            ["ExceptionNotifications:AdminLineUserId"] = "legacy-recipient",
            ["ExceptionNotifications:Line:Targets:GoodShepherd2"] = target
        }).Build();
        using var transport = new Capture();
        using var sender = Assert.IsType<LineExceptionSender>(constructor.Invoke(new object?[] { config, null, transport, "GoodShepherd2" }));
        await sender.SendAsync("safe-metadata", default);
        Assert.Equal(expected, transport.Target);
    }

    /// <summary>只截取合成JSON的目的地，不保存token、request或response；sender負責Dispose本handler。</summary>
    private sealed class Capture : HttpMessageHandler
    {
        internal string? Target { get; private set; }
        /// <summary>所有輸入來自本案配置，完成後Dispose文件；不開socket、不讀環境或重試。</summary>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Target = json.RootElement.GetProperty("to").GetString();
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
