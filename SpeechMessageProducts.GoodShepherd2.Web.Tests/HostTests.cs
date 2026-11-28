/*
檔案整體說明
目的與定位：驗證 F2/F3/F11 真實 ASP.NET Core 組合根、D365 相容方法與四項就緒端點的離線契約，不連線任何外部組織、SQL 或 LINE。
主要功能與流程：每案使用獨立暫存 content root、合成 SDK 與真實 Kestrel loopback 埠；F11啟停案例以正式D365 probe及明確未就緒的SQL／queue／Channel合成probe隔離外部I/O，驗四項ready的503不影響live。以 WhoAmI 正常／失敗、取消及 Dispose 故障注入，確認只回安全狀態、故障先標記再歸還租約。Webhook 使用合成原始本文與無效簽章，確認 F3 移除事先注入的替身並使用真正 verifier／拒絕 resolver，必須回 401、不得回 200 或進入事件佇列；原始 bytes 與正確 secret 的正控制由 Channels 測試保護。重複啟停證明程序級診斷 owner 能解除後重新綁定。Main 案例以測試擁有的 listener 占用 loopback 埠，在獨立子程序重現啟動失敗，驗證原始例外不被已釋放容器的錯誤蓋掉。
使用邊界與維護：測試 collection 禁止與其他會綁定靜態診斷 owner 的案例並行；Host、HttpClient、組態與暫存資料由當案 finally／await using 清理。例外訊息使用合成 canary，實際 Exception.log 必須不含它。此測試只證明本機合成依賴及資源釋放，不代表實機 WhoAmI 或 LINE webhook/test 成功。
*/
using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Xrm.Sdk;
using Microsoft.PowerPlatform.Dataverse.Client;
using SpeechMessage.Hosting.Observability;
using Microsoft.Crm.Sdk.Messages;
using LineMessagingProcessor.AspNetCore.Webhooks;
using LineMessagingProcessor.AspNetCore.Webhooks.Signatures;
using SpeechMessage.Tenancy;
using Moq;
using SpeechMessageProducts.GoodShepherd2.Web.Health;
using ToolUtilityNameSpace.Dataverse;
using ToolUtilityNameSpace.Diagnostics;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd2.Web.Tests;

/// <summary>每個真實主機依序啟停，避免程序相容診斷入口被兩個測試同時擁有。</summary>
[CollectionDefinition("F2Host", DisableParallelization = true)]
public sealed class F2HostGroup;

/// <summary>透過真正路由及短期租約確認 F2 安全回應與確定清理，所有外部 SDK 由合成替身接管。</summary>
[Collection("F2Host")]
public sealed class HostTests
{
    /// <summary>真實 Main 使用的唯一生命週期在正常停止後關閉全部 owner；連續兩輪沒有殘留程序診斷綁定。</summary>
    [Fact]
    public async Task MainLifetime_NormalStop_DrainsAndDisposesOnceAcrossRestarts()
    {
        for (var iteration = 0; iteration < 2; iteration++)
        {
            var directory = Path.Combine(Path.GetTempPath(), "3p-f2-lifetime-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            WebApplication? app = null;
            Task? operation = null;
            try
            {
                var probe = new HostFailureProbe(null, false);
                var builder = Builder(directory);
                builder.Services.AddSingleton<IHostedService>(_ => probe);
                app = Program.BuildApplication(builder);
                var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
                var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                using var registration = lifetime.ApplicationStarted.Register(
                    static value => ((TaskCompletionSource)value!).TrySetResult(), started);
                operation = RunMainLifetime(app);
                await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
                lifetime.StopApplication();
                await operation.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal(1, probe.Stopped);
                Assert.Equal(1, probe.Disposed);
                Assert.Throws<ObjectDisposedException>(() => app.Services.GetService<IHostApplicationLifetime>());
            }
            finally
            {
                if (app is not null && operation is { IsCompleted: false })
                {
                    app.Services.GetRequiredService<IHostApplicationLifetime>().StopApplication();
                    await operation.WaitAsync(TimeSpan.FromSeconds(10));
                }
                if (operation is null && app is not null) await app.DisposeAsync();
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    /// <summary>注入啟動、停止及 Dispose 三重故障；原始啟動例外必須維持同一實例，且其他清理仍各執行一次。</summary>
    [Fact]
    public async Task MainLifetime_StartAndCleanupFail_PreservesPrimaryAndReleasesContainer()
    {
        var directory = Path.Combine(Path.GetTempPath(), "3p-f2-lifetime-fault-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var failure = new InvalidOperationException("synthetic-startup-sensitive-canary");
            var probe = new HostFailureProbe(failure, true);
            var builder = Builder(directory);
            builder.Services.AddSingleton<IHostedService>(_ => probe);
            var app = Program.BuildApplication(builder);
            var actual = await Assert.ThrowsAsync<InvalidOperationException>(
                () => RunMainLifetime(app).WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Same(failure, actual);
            Assert.Equal(1, probe.Stopped);
            Assert.Equal(1, probe.Disposed);
            Assert.Throws<ObjectDisposedException>(() => app.Services.GetService<IHostApplicationLifetime>());
            var log = await File.ReadAllTextAsync(Path.Combine(directory, "Logs", "Exception.log"));
            Assert.Contains("System.InvalidOperationException", log, StringComparison.Ordinal);
            Assert.DoesNotContain("synthetic-startup-sensitive-canary", log, StringComparison.Ordinal);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    /// <summary>直接執行 Main 的同一私有 owner，避免為測試擴張主機公開 API；沒有在測試複製生命週期實作。</summary>
    /// <param name="app">由產品 owner 接管清理的真實 WebApplication。</param>
    /// <returns>產品方法的原 Task，例外身分不經 reflection 的同步包裝。</returns>
    private static Task RunMainLifetime(WebApplication app)
    {
        var method = typeof(Program).GetMethod("RunOwnedApplicationAsync", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return Assert.IsAssignableFrom<Task>(method.Invoke(null, new object[] { app }));
    }

    /// <summary>單案故障注入服務；不持有連線或背景資源，容器負責唯一 Dispose 並保留各階段決定性計數。</summary>
    /// <param name="startFailure">要由 StartAsync 原樣傳回的合成例外；null 表示正常啟動。</param>
    /// <param name="cleanupFails">是否在 Stop 與 Dispose 各注入一個不同的合成清理故障。</param>
    private sealed class HostFailureProbe(Exception? startFailure, bool cleanupFails) : IHostedService, IAsyncDisposable
    {
        /// <summary>目前 owner 的 Stop 次數，沒有 static 或跨測試計數。</summary>
        public int Stopped { get; private set; }
        /// <summary>容器嘗試唯一清理的次數，清理故障也必須只算一次。</summary>
        public int Disposed { get; private set; }
        /// <summary>只注入指定的合成啟動結果，不建立工作或其他需清理資源。</summary>
        /// <param name="cancellationToken">主機啟動取消；本測試不以其模擬內部逾時。</param>
        public Task StartAsync(CancellationToken cancellationToken) =>
            startFailure is null ? Task.CompletedTask : Task.FromException(startFailure);
        /// <summary>記錄產品曾嘗試停止；即使拋錯，產品仍必須進入容器 Dispose。</summary>
        /// <param name="cancellationToken">主機停止期限，不產生額外等待。</param>
        public Task StopAsync(CancellationToken cancellationToken)
        {
            Stopped++;
            return cleanupFails ? Task.FromException(new IOException("synthetic-stop")) : Task.CompletedTask;
        }
        /// <summary>記錄容器最終清理；合成故障不能取代先前由 StartAsync 產生的原始失敗。</summary>
        public ValueTask DisposeAsync()
        {
            Disposed++;
            return cleanupFails ? ValueTask.FromException(new IOException("synthetic-dispose")) : ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// 真正 Main 遇到占用埠必須保留原始 bind 失敗、落下安全共用告警，並完整關閉子程序。
    /// 只在子程序指定 Development 與合成組織，不改父程序環境、不連外；stderr 有六十四 KiB 硬上限。
    /// 若重新使用會先 Dispose 主機的 RunAsync，再從容器解析 diagnostics，本案將因 ObjectDisposedException 失敗。
    /// </summary>
    [Fact]
    public async Task Main_BindFailure_PreservesOriginalExceptionAndWritesSafeLog()
    {
        var directory = Path.Combine(Path.GetTempPath(), "3p-f2-main-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var testAssembly = typeof(HostTests).Assembly.Location;
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = directory
        };
        // 子程序只繼承載入 runtime 所需的系統路徑；部署憑證、hosting startup 與父程序設定不越過本案邊界。
        start.Environment.Clear();
        foreach (var name in new[] { "PATH", "SystemRoot", "windir", "DOTNET_ROOT", "ProgramFiles", "ProgramFiles(x86)", "TEMP", "TMP" })
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (value is not null) start.Environment[name] = value;
        }
        // Development 的預設 User Secrets provider 只能看到本案空目錄，不能讀取使用者真實設定檔。
        start.Environment["APPDATA"] = directory;
        foreach (var argument in new[]
        {
            "exec", "--runtimeconfig", Path.ChangeExtension(testAssembly, ".runtimeconfig.json"),
            "--depsfile", Path.ChangeExtension(testAssembly, ".deps.json"), typeof(Program).Assembly.Location,
            "--contentRoot", directory, "--urls", "http://127.0.0.1:" + port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--CrmConnection:ServerUrl", "https://goodshepherd20.example/XRMServices/2011/Organization.svc",
            "--GoodShepherd2:AllowedDevelopmentOrganizations:0", "goodshepherd20",
            "--ExceptionNotifications:WriteExceptionLog", "true", "--ExceptionNotifications:SendLine", "false",
            "--ConnectionStrings:Platform", "Server=(localdb)\\MSSQLLocalDB;Database=GoodShepherd2Platform;Integrated Security=true;Encrypt=true",
            "--LineWebhook:Queue:Capacity", "8", "--LineWebhook:Queue:PartitionCount", "1"
        })
            start.ArgumentList.Add(argument);
        start.Environment["DOTNET_ENVIRONMENT"] = Environments.Development;
        start.Environment["ASPNETCORE_ENVIRONMENT"] = Environments.Development;
        using var process = new Process { StartInfo = start };
        Task? stdout = null;
        Task<string>? stderr = null;
        var started = false;
        try
        {
            Assert.True(process.Start());
            started = true;
            stdout = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null, 4096);
            stderr = ReadBoundedErrorAsync(process.StandardError.BaseStream);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await process.WaitForExitAsync(deadline.Token);
            await stdout;
            var error = await stderr;
            Assert.NotEqual(0, process.ExitCode);
            Assert.DoesNotContain("ObjectDisposedException", error, StringComparison.Ordinal);
            Assert.Contains("System.IO.IOException", error, StringComparison.Ordinal);
            var logPath = Path.Combine(directory, "Logs", "Exception.log");
            Assert.True(File.Exists(logPath));
            var log = await File.ReadAllTextAsync(logPath);
            Assert.Contains("System.IO.IOException", log, StringComparison.Ordinal);
            Assert.DoesNotContain(directory, log, StringComparison.Ordinal);
        }
        finally
        {
            try
            {
                if (started && !process.HasExited) process.Kill(entireProcessTree: true);
                if (started)
                {
                    using var drain = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    await process.WaitForExitAsync(drain.Token);
                }
                if (stdout is not null) await stdout;
                if (stderr is not null) await stderr;
            }
            finally
            {
                listener.Stop();
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    /// <summary>單案借用子程序 stderr，最多持有六十四 KiB；不關閉借入串流，程序 owner 等待排空後 Dispose。</summary>
    /// <param name="stream">目前測試子程序的唯一 stderr 串流。</param>
    /// <returns>僅合成啟動錯誤的有界文字，超限直接使測試失敗並由外層終止子程序。</returns>
    private static async Task<string> ReadBoundedErrorAsync(Stream stream)
    {
        using var bytes = new MemoryStream();
        var buffer = new byte[4096];
        int count;
        while ((count = await stream.ReadAsync(buffer)) != 0)
        {
            if (bytes.Length + count > 65536) throw new InvalidOperationException("Synthetic host stderr limit exceeded.");
            bytes.Write(buffer, 0, count);
        }
        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    /// <summary>連續三個Host以正式D365 probe及其餘未就緒合成probe確認live200、ready503與無效簽章401，shutdown解除診斷owner且SDK歸還。</summary>
    [Fact]
    public async Task Host_HealthAndInvalidWebhook_RestartsWithoutRetainedOwner()
    {
        for (var iteration = 0; iteration < 3; iteration++)
        {
            var directory = Path.Combine(Path.GetTempPath(), "3p-f2-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var body = "synthetic webhook body";
                const string signatureValue = "synthetic-invalid-signature";
                var verifier = new Mock<IWebhookSignatureVerifier>(MockBehavior.Strict);
                var deduplicator = new Mock<IWebhookEventDeduplicator>(MockBehavior.Strict);
                var queue = new Mock<IWebhookEventQueue>(MockBehavior.Strict);
                var lease = new Mock<IClientLease>(MockBehavior.Strict);
                var service = new Mock<IOrganizationServiceAsync2>(MockBehavior.Strict);
                service.Setup(value => value.ExecuteAsync(It.IsAny<WhoAmIRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(Identity());
                lease.SetupGet(value => value.Service).Returns(service.Object);
                lease.Setup(value => value.Dispose());
                var manager = new Mock<IDataverseConnectionManager>(MockBehavior.Strict);
                manager.Setup(value => value.Acquire(It.IsAny<CancellationToken>())).Returns(lease.Object);
                var builder = Builder(directory);
                builder.Services.AddSingleton(verifier.Object);
                builder.Services.AddSingleton(deduplicator.Object);
                builder.Services.AddSingleton(queue.Object);
                builder.Services.AddSingleton(manager.Object);
                builder.Services.AddSingleton(provider => new ReadinessChecks(ct =>
                {
                    var type = typeof(Program).Assembly.GetType("SpeechMessageProducts.GoodShepherd2.Web.Health.GoodShepherd2Readiness")!;
                    return Assert.IsAssignableFrom<Task<bool>>(type.GetMethod("CheckD365Async", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(
                        null, new object[] { manager.Object, provider.GetRequiredService<ExceptionDiagnostics>(), Guid.Empty, ct }));
                }, _ => Task.FromResult(false), _ => Task.FromResult(false), _ => Task.FromResult(false),
                    provider.GetRequiredService<ExceptionDiagnostics>()));
                await using var app = Program.BuildApplication(builder);
                await app.StartAsync();
                try
                {
                    var server = app.Services.GetRequiredService<IServer>();
                    var address = Assert.Single(server.Features.Get<IServerAddressesFeature>()!.Addresses);
                    using var client = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(10) };
                    using var live = await client.GetAsync(new Uri("/health/live", UriKind.Relative));
                    Assert.Equal(HttpStatusCode.OK, live.StatusCode);
                    using var ready = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));
                    Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
                    Assert.Equal("{\"failed\":[\"sql\",\"queue\",\"channels\"]}", await ready.Content.ReadAsStringAsync());
                    using var content = new StringContent(body, Encoding.UTF8, "application/json");
                    using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/webhook/1234567890", UriKind.Relative))
                    {
                        Content = content
                    };
                    request.Headers.TryAddWithoutValidation("X-Line-Signature", signatureValue);
                    using var webhook = await client.SendAsync(request);
                    Assert.Equal(HttpStatusCode.Unauthorized, webhook.StatusCode);
                    Assert.IsType<LineWebhookSignatureVerifier>(app.Services.GetRequiredService<IWebhookSignatureVerifier>());
                    var resolver = app.Services.GetRequiredService<IWebhookChannelSecretResolver>();
                    Assert.Empty(resolver.ConfiguredChannelIds);
                    Assert.False(resolver.TryResolveSecret("1234567890", out var secret));
                    Assert.Null(secret);
                    verifier.VerifyNoOtherCalls();
                    deduplicator.Verify(value => value.TryRegisterAsync(It.IsAny<WebhookEnvelope>(), It.IsAny<CancellationToken>()), Times.Never);
                    queue.Verify(value => value.TryEnqueueAsync(
                        It.IsAny<WebhookEnvelope>(), It.IsAny<IWebhookEventRegistration>(), It.IsAny<CancellationToken>()), Times.Never);
                    lease.Verify(value => value.Dispose(), Times.Once);
                    lease.Verify(value => value.MarkFaulted(), Times.Never);
                }
                finally { await app.StopAsync(); }
            }
            finally { Directory.Delete(directory, recursive: true); }
        }
    }

    /// <summary>SDK 真正故障時先淘汰再歸還，安全 503 與共用日誌均不得複製原始 canary 訊息。</summary>
    [Fact]
    public async Task Ready_SdkFailure_MarksBeforeReleaseAndWritesSafeLog()
    {
        var directory = Path.Combine(Path.GetTempPath(), "3p-f2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await using var diagnostics = new ExceptionDiagnostics(directory, outputOptions: new ExceptionOutputOptions(true, false));
            var order = new List<string>();
            var lease = new Mock<IClientLease>(MockBehavior.Strict);
            var service = new Mock<IOrganizationService>(MockBehavior.Strict);
            service.Setup(value => value.Execute(It.IsAny<WhoAmIRequest>())).Throws(new InvalidOperationException("synthetic-sensitive-canary"));
            lease.SetupGet(value => value.Service).Returns(service.Object);
            lease.Setup(value => value.MarkFaulted()).Callback(() => order.Add("fault"));
            lease.Setup(value => value.Dispose()).Callback(() => order.Add("release"));
            var manager = new Mock<IDataverseConnectionManager>(MockBehavior.Strict);
            manager.Setup(value => value.Acquire(It.IsAny<CancellationToken>())).Returns(lease.Object);
            var result = HealthEndpoints.CheckReady(manager.Object, diagnostics, CancellationToken.None);
            Assert.Equal(StatusCodes.Status503ServiceUnavailable, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
            Assert.Collection(order,
                value => Assert.Equal("fault", value),
                value => Assert.Equal("release", value));
            var text = await File.ReadAllTextAsync(Path.Combine(directory, "Exception.log"));
            Assert.Contains("GoodShepherd2.Health.Ready", text, StringComparison.Ordinal);
            Assert.DoesNotContain("synthetic-sensitive-canary", text, StringComparison.Ordinal);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    /// <summary>SDK 回應缺少真實身分或租約歸還失敗，都不得將候選 ready 發布為成功。</summary>
    /// <param name="releaseFails">是否在歸還注入錯誤；否則注入缺少身分的 SDK 回應。</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ready_IncompleteIdentityOrReleaseFailure_Returns503(bool releaseFails)
    {
        await using var diagnostics = new ExceptionDiagnostics(Path.GetTempPath(), outputOptions: new ExceptionOutputOptions(false, false));
        var lease = new Mock<IClientLease>(MockBehavior.Loose);
        var service = new Mock<IOrganizationService>(MockBehavior.Strict);
        service.Setup(value => value.Execute(It.IsAny<WhoAmIRequest>())).Returns(releaseFails ? Identity() : new WhoAmIResponse());
        lease.SetupGet(value => value.Service).Returns(service.Object);
        if (releaseFails) lease.Setup(value => value.Dispose()).Throws(new IOException("synthetic-release"));
        var manager = new Mock<IDataverseConnectionManager>(MockBehavior.Strict);
        manager.Setup(value => value.Acquire(It.IsAny<CancellationToken>())).Returns(lease.Object);
        var result = HealthEndpoints.CheckReady(manager.Object, diagnostics, CancellationToken.None);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        lease.Verify(value => value.Dispose(), Times.Once);
    }

    /// <summary>呼叫端已取消時不取得租約、不宣稱 ready，也不把正常取消寫成基礎設施告警。</summary>
    [Fact]
    public async Task Ready_CallerCancelled_DoesNotAcquireOrNotify()
    {
        await using var diagnostics = new ExceptionDiagnostics(Path.GetTempPath(), outputOptions: new ExceptionOutputOptions(false, false));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var manager = new Mock<IDataverseConnectionManager>(MockBehavior.Strict);
        Assert.Throws<OperationCanceledException>(() => HealthEndpoints.CheckReady(manager.Object, diagnostics, cancellation.Token));
        manager.Verify(value => value.Acquire(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>同步 SDK 正在執行時 caller 取消不能提前歸還租約；SDK 真正返回後才淘汰、釋放並傳回正常取消。</summary>
    [Fact]
    public async Task Ready_CancelDuringSdk_DrainsBeforeLeaseRelease()
    {
        await using var diagnostics = new ExceptionDiagnostics(Path.GetTempPath(),
            outputOptions: new ExceptionOutputOptions(false, false));
        using var cancellation = new CancellationTokenSource();
        using var entered = new ManualResetEventSlim();
        using var finish = new ManualResetEventSlim();
        var released = 0;
        var lease = new Mock<IClientLease>(MockBehavior.Strict);
        var service = new Mock<IOrganizationService>(MockBehavior.Strict);
        service.Setup(value => value.Execute(It.IsAny<WhoAmIRequest>())).Returns(() =>
        {
            entered.Set();
            if (!finish.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Synthetic SDK drain deadline.");
            return Identity();
        });
        lease.SetupGet(value => value.Service).Returns(service.Object);
        lease.Setup(value => value.MarkFaulted());
        lease.Setup(value => value.Dispose()).Callback(() => Interlocked.Increment(ref released));
        var manager = new Mock<IDataverseConnectionManager>(MockBehavior.Strict);
        manager.Setup(value => value.Acquire(It.IsAny<CancellationToken>())).Returns(lease.Object);
        var operation = Task.Run(() => HealthEndpoints.CheckReady(manager.Object, diagnostics, cancellation.Token));
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
            await cancellation.CancelAsync();
            Assert.False(operation.IsCompleted);
            Assert.Equal(0, Volatile.Read(ref released));
        }
        finally
        {
            finish.Set();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await operation.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        Assert.Equal(1, Volatile.Read(ref released));
        lease.Verify(value => value.MarkFaulted(), Times.Once);
    }

    /// <summary>F3 前誤放任何產品 Channel 或啟用例外 LINE 必須在建置主機前拒絕，不建立 sender 或共用舊產品驗簽。</summary>
    /// <param name="key">注入的部署鍵名。</param>
    /// <param name="value">足以啟用不允許功能的合成值。</param>
    [Theory]
    [InlineData("ExceptionNotifications:SendLine", "true")]
    [InlineData("LineWebhook:Channels:0:ChannelId", "synthetic-channel")]
    [InlineData("LineWebhook:EnableLegacyCallback", "true")]
    public void Host_F2RejectsChannelsAndLineBeforeBuild(string key, string value)
    {
        var builder = Builder(Path.GetTempPath());
        try
        {
            builder.Configuration[key] = value;
            Assert.Throws<InvalidOperationException>(() => Program.BuildApplication(builder));
        }
        finally { ((IDisposable)builder.Configuration).Dispose(); }
    }

    /// <summary>正式組合根將全部manager consumer接到同一觀測殼；建立與解析描述子不開SDK連線，DI各owner確定釋放。</summary>
    [Fact]
    public async Task Host_DefaultManager_UsesSingleObservedOwner()
    {
        var builder = Builder(Path.GetTempPath());
        builder.Configuration["CrmConnection:Username"] = "synthetic-service";
        await using var app = Program.BuildApplication(builder);
        var manager = app.Services.GetRequiredService<IDataverseConnectionManager>();
        Assert.Equal("ObservedDataverseConnectionManager", manager.GetType().Name);
        Assert.Same(manager, app.Services.GetRequiredService<IDataverseConnectionManager>());
        Assert.Equal(0, app.Services.GetRequiredService<HostingMeters>().GetSnapshot().DataverseLeasesActive);
    }

    /// <summary>只建立與本案分離的 Deployment 快照；避免讀真實 User Secrets 或修改程序環境。</summary>
    private static WebApplicationBuilder Builder(string directory)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development, ContentRootPath = directory, ApplicationName = typeof(HostTests).Assembly.FullName });
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["CrmConnection:ServerUrl"] = "https://goodshepherd20.example/XRMServices/2011/Organization.svc",
            ["GoodShepherd2:AllowedDevelopmentOrganizations:0"] = "goodshepherd20",
            ["ExceptionNotifications:WriteExceptionLog"] = "true",
            ["ExceptionNotifications:SendLine"] = "false",
            ["ConnectionStrings:Platform"] = "Server=(localdb)\\MSSQLLocalDB;Database=GoodShepherd2Platform;Integrated Security=true;Encrypt=true",
            ["LineWebhook:Queue:Capacity"] = "8", ["LineWebhook:Queue:PartitionCount"] = "1"
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        return builder;
    }

    /// <summary>建立只含三個合成 GUID 的 WhoAmI 成功回應，不使用真實組織／使用者識別。</summary>
    private static WhoAmIResponse Identity() => new() { Results = new ParameterCollection { ["UserId"] = Guid.NewGuid(), ["BusinessUnitId"] = Guid.NewGuid(), ["OrganizationId"] = Guid.NewGuid() } };
}
