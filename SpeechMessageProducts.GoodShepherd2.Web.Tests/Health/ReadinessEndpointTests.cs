/*
檔案整體說明
目的與定位：F11健康端點的真HTTP離線回歸，以Kestrel及四個合成probe檢查D365故障不影響live，ready回應只含固定失敗項目。
主要功能與流程：每案清空所有外部組態來源，只使用合成測試組織及明確關閉通知；注入真正ReadinessChecks但不注入外部連線，讓D365 probe拋帶秘密canary的合成錯誤，SQL／queue／Channel由本案固定健康結果提供。
使用邊界與維護：每案獨立暫存Host及診斷目錄；finally先Stop，await using再釋放容器，HttpClient、訊息及回應均確定Dispose，最後刪本案專用目錄。不讀User Secrets、不實機、不把四個合成probe寫成VM驗收。collection沿用F2Host，避免程序診斷owner並行。
*/
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SpeechMessage.Hosting.Observability;
using ToolUtilityNameSpace.Diagnostics;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd2.Web.Tests.Health;

/// <summary>以真HTTP證明路由實際使用四項彙總，而非只測未接入的輔助類別。</summary>
[Collection("F2Host")]
public sealed class ReadinessEndpointTests
{
    /// <summary>D365故障回503及固定d365名稱，live仍200且不受SDK故障／例外內容污染。</summary>
    [Fact]
    public async Task Health_D365Down_ReadyFails_LiveOk()
    {
        var directory = Path.Combine(Path.GetTempPath(), "3p-f11-health-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            { EnvironmentName = Environments.Development, ContentRootPath = directory });
            builder.Configuration.Sources.Clear();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CrmConnection:ServerUrl"] = "https://goodshepherd20.example/XRMServices/2011/Organization.svc",
                ["GoodShepherd2:AllowedDevelopmentOrganizations:0"] = "goodshepherd20",
                ["ExceptionNotifications:WriteExceptionLog"] = "false", ["ExceptionNotifications:SendLine"] = "false",
                ["ConnectionStrings:Platform"] = @"Server=(localdb)\MSSQLLocalDB;Database=GoodShepherd2Platform;Integrated Security=true;Encrypt=true"
            });
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddSingleton(provider => new ReadinessChecks(_ => throw new IOException("synthetic-secret-canary"),
                _ => Task.FromResult(true), _ => Task.FromResult(true), _ => Task.FromResult(true),
                provider.GetRequiredService<ExceptionDiagnostics>()));
            await using var app = Program.BuildApplication(builder);
            await app.StartAsync();
            try
            {
                var server = app.Services.GetRequiredService<IServer>();
                using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(server.Features.Get<IServerAddressesFeature>()!.Addresses)) };
                using var live = await client.GetAsync(new Uri("/health/live", UriKind.Relative));
                Assert.Equal(HttpStatusCode.OK, live.StatusCode);
                using var ready = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));
                Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
                var body = await ready.Content.ReadAsStringAsync();
                Assert.DoesNotContain("canary", body, StringComparison.Ordinal);
                using var json = JsonDocument.Parse(body);
                Assert.Equal("d365", Assert.Single(json.RootElement.GetProperty("failed").EnumerateArray()).GetString());
            }
            finally { await app.StopAsync(); }
        }
        finally { Directory.Delete(directory, true); }
    }
}
