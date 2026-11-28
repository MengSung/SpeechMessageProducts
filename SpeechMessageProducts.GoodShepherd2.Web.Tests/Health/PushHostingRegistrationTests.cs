/*
檔案整體說明
目的與定位：F11 真 Program 組合根的推播描述子契約，確保已配置 F3 的主機能解析唯一 F5 入口，未配置時維持封閉。
主要功能與流程：以獨立 Development builder 與合成組織／金鑰字串建置真 WebApplication，不 Start、不解析 SDK 或 HTTP owner；只查 IServiceProviderIsService，避免以測試直接 new adapter 掩蓋產品未接線。
使用邊界與維護：測試未讀 User Secrets，不連任何外部環境；每案 await using 主機並在建置失敗時關閉組態。合成 Base64 key 無實際用途，不輸出或存進證據。此描述子測試不冒稱 Live3P 發送或 VM 已部署。
*/
using LineMessagingProcessor.Core.Gateway;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd2.Web.Tests.Health;

/// <summary>真組合根的已配置／未配置分支，不啟動 queue、CRM 或 LINE。</summary>
[Collection("F2Host")]
public sealed class PushHostingRegistrationTests
{
    /// <summary>完整 F3 設定才發布發送服務；沒有 Channel 前置不得建立無憑證或跨產品後備的 gateway。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Program_ConfiguredChannelsPublishesGatewayOnlyWhenConfigured(bool configured)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        { EnvironmentName = Environments.Development, ApplicationName = typeof(PushHostingRegistrationTests).Assembly.FullName });
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["CrmConnection:ServerUrl"] = "https://goodshepherd20.example/XRMServices/2011/Organization.svc",
            ["GoodShepherd2:AllowedDevelopmentOrganizations:0"] = "goodshepherd20",
            ["ExceptionNotifications:WriteExceptionLog"] = "false", ["ExceptionNotifications:SendLine"] = "false",
            ["ConnectionStrings:Platform"] = "Server=unopened.example;Database=GoodShepherd2Platform;Integrated Security=true;Encrypt=true"
        });
        if (configured)
        {
            builder.Configuration["Channels:OrganizationId"] = Guid.NewGuid().ToString("D");
            builder.Configuration["LineChannels:TokenKey"] = Convert.ToBase64String(new byte[64]);
        }
        WebApplication? app = null;
        try
        {
            app = Program.BuildApplication(builder);
            Assert.Equal(configured, app.Services.GetRequiredService<IServiceProviderIsService>().IsService(typeof(ILinePushGateway)));
        }
        finally
        {
            if (app is not null) await app.DisposeAsync();
            else ((IDisposable)builder.Configuration).Dispose();
        }
    }
}
