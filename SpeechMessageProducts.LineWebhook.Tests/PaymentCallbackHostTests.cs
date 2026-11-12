/*
 * 檔案整體說明
 *
 * 目的與定位：以真實 ASP.NET Core／Kestrel 路由測試 P4-05 QPay ATM 與信用卡回呼；所有商店代號、PayToken、付款狀態與訂單均為合成資料，不連永豐、D365、正式 LINE 或正式商店。
 * 主要功能與流程：建立隔離的 WebApplication、注入假的 IPaymentGateway 與可選產品 lookup/updater，送出兩種 HTTP 格式，檢查狀態碼、呼叫順序、三道驗證、重複付款及 64 KiB 本文界線。
 * 使用邊界與維護：每個測試持有自己的 Kestrel Host、HttpClient、DI scope 與有限測試狀態；fixture 唯一擁有該案例的 OS 暫存目錄，關閉 Host 後必定嘗試刪除，建置失敗也會清理。測試 runner 的 TRX／log evidence 另留在本機 task evidence 目錄。假 Gateway 的成功結果不代表銀行驗簽實機成功；本文、ShopNo 與 PayToken 不寫入日誌或回報。
 */
using System.Net;
using System.Runtime.ExceptionServices;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SpeechMessage.Hosting;
using SpeechMessage.Localization;
using SpeechMessage.Payments.Abstractions;
using SpeechMessage.Payments.AspNetCore.Callbacks;
using SpeechMessage.Payments.AspNetCore.DependencyInjection;
using SpeechMessage.Payments.Configuration;
using SpeechMessage.Payments.Models;
using SpeechMessage.Payments.Workflows;
using SpeechMessage.Payments.Workflows.Callbacks;
using ToolUtilityNameSpace.Diagnostics;
using Xunit;

namespace SpeechMessageProducts.LineWebhook.Tests;

/// <summary>
/// 保護 04A §5 的 callback fail-closed、訂單隔離、付款一致性與有界輸入契約。
/// 以本機 Kestrel 和合成依賴故障注入；每個測試斷言 HTTP 結果及 updater／lookup 呼叫，
/// 並由案例 owner 關閉 Host、client 及服務容器，不把離線結果當成永豐實機驗證。
/// </summary>
public sealed class PaymentCallbackHostTests
{
    private const string ShopNo = "SYNTHETIC-SHOP";
    private const string PayToken = "synthetic-pay-token";
    private const string ProfileName = "Sandbox";
    private const string OrderNo = "synthetic-order-1";

    /// <summary>ShopNo 不唯一或不相符、callback 格式無效及查詢簽章失敗時不得更新訂單。</summary>
    [Fact]
    public async Task Callback_ShopNoMismatch_Rejected()
    {
        var gateway = new PaymentGatewayDouble();
        var lookup = new OrderLookupDouble(CreateOrder());
        var updater = new RecordUpdaterDouble(lookup);
        await using var host = new PaymentCallbackTestHost(gateway, lookup, updater);
        await host.StartAsync();

        using (var malformed = await host.PostAtmAsync("{"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        }

        using (var duplicateJsonKey = await host.PostAtmAsync(
            $"{{\"ShopNo\":\"{ShopNo}\",\"shopno\":\"{ShopNo}\",\"PayToken\":\"{PayToken}\"}}"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, duplicateJsonKey.StatusCode);
        }

        using (var duplicateFormKey = await host.PostRawCardAsync(
            $"ShopNo={ShopNo}&shopno={ShopNo}&PayToken={PayToken}"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, duplicateFormKey.StatusCode);
        }

        using (var mismatchedShop = await host.PostAtmAsync(
            "{\"ShopNo\":\"OTHER-SYNTHETIC-SHOP\",\"PayToken\":\"synthetic-pay-token\"}"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, mismatchedShop.StatusCode);
        }

        Assert.Equal(0, gateway.ParseCalls);
        Assert.Equal(0, gateway.QueryCalls);
        Assert.Equal(0, lookup.FindCalls);
        Assert.Equal(0, updater.Calls);

        gateway.StatusResult = CreateStatus(error: new PaymentError
        {
            Kind = PaymentErrorKind.SignatureInvalid,
            Message = $"Synthetic provider signature failure {PayToken}"
        });
        using var invalidProviderSignature = await host.PostAtmAsync(CreateAtmBody());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, invalidProviderSignature.StatusCode);
        Assert.Equal(1, gateway.QueryCalls);
        Assert.Equal(0, lookup.FindCalls);
        Assert.Equal(0, updater.Calls);
        var responseText = await invalidProviderSignature.Content.ReadAsStringAsync();
        Assert.DoesNotContain(PayToken, responseText, StringComparison.Ordinal);
        Assert.DoesNotContain(PayToken, host.ExceptionLog, StringComparison.Ordinal);

        gateway.QueryException = new InvalidOperationException($"Synthetic provider failure {PayToken}");
        using var providerFailure = await host.PostAtmAsync(CreateAtmBody());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, providerFailure.StatusCode);
        Assert.DoesNotContain(PayToken, await providerFailure.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.DoesNotContain(PayToken, host.ExceptionLog, StringComparison.Ordinal);
    }

    /// <summary>同一 PayToken 先處理再重送時，合成 lookup 標記已結清，付款後 updater 只執行一次。</summary>
    [Fact]
    public async Task Callback_Duplicate_ProcessedOnce()
    {
        var gateway = new PaymentGatewayDouble();
        var lookup = new OrderLookupDouble(CreateOrder());
        var updater = new RecordUpdaterDouble(lookup);
        await using var host = new PaymentCallbackTestHost(gateway, lookup, updater);
        await host.StartAsync();

        using (var first = await host.PostAtmAsync(CreateAtmBody()))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.Equal("{\"Status\":\"S\"}", await first.Content.ReadAsStringAsync());
        }

        using (var duplicate = await host.PostCardAsync(ShopNo, PayToken))
        {
            Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
            Assert.Equal("OK", await duplicate.Content.ReadAsStringAsync());
        }

        Assert.Equal(2, gateway.ParseCalls);
        Assert.Equal(2, gateway.QueryCalls);
        Assert.Equal(2, lookup.FindCalls);
        Assert.Equal(1, updater.Calls);
        Assert.Equal(ProfileName, gateway.LastQueryRequest?.ProfileName);
        Assert.Equal(PaymentProviderKind.Sinopac, gateway.LastQueryRequest?.ProviderHint);
        Assert.Equal(PayToken, gateway.LastQueryRequest?.ProviderOrderRef);
        Assert.Equal(ShopNo, gateway.LastCallbackRequest?.Form["ShopNo"]);
        Assert.Equal(PayToken, gateway.LastCallbackRequest?.Form["PayToken"]);
        Assert.Empty(gateway.LastCallbackRequest?.Query ?? new Dictionary<string, string>());
        Assert.Empty(gateway.LastCallbackRequest?.RawBody ?? string.Empty);
    }

    /// <summary>Pending、缺額、零額、幣別不同或訂單號不屬於本訂單時都拒絕且不更新。</summary>
    [Fact]
    public async Task Callback_AmountOrCurrencyMismatch_Rejected()
    {
        var gateway = new PaymentGatewayDouble();
        var lookup = new OrderLookupDouble(CreateOrder());
        var updater = new RecordUpdaterDouble(lookup);
        await using var host = new PaymentCallbackTestHost(gateway, lookup, updater);
        await host.StartAsync();

        gateway.StatusResult = CreateStatus(status: PaymentStatus.Pending);
        using (var pending = await host.PostAtmAsync(CreateAtmBody()))
        {
            Assert.Equal(HttpStatusCode.Conflict, pending.StatusCode);
        }
        Assert.Equal(0, lookup.FindCalls);
        Assert.Equal(0, updater.Calls);

        gateway.StatusResult = CreateStatus(amount: new Money(0m, "TWD"));
        using (var zeroAmount = await host.PostAtmAsync(CreateAtmBody()))
        {
            Assert.Equal(HttpStatusCode.Conflict, zeroAmount.StatusCode);
        }

        gateway.StatusResult = CreateStatus(amount: new Money(10m, "USD"));
        using (var currencyMismatch = await host.PostAtmAsync(CreateAtmBody()))
        {
            Assert.Equal(HttpStatusCode.Conflict, currencyMismatch.StatusCode);
        }

        gateway.StatusResult = CreateStatus();
        lookup.CurrentOrder = CreateOrder(orderNumbers: ["another-synthetic-order"]);
        using (var orderMismatch = await host.PostAtmAsync(CreateAtmBody()))
        {
            Assert.Equal(HttpStatusCode.Conflict, orderMismatch.StatusCode);
        }

        lookup.CurrentOrder = CreateOrder() with { ProfileName = "OtherSandbox" };
        using (var profileMismatch = await host.PostAtmAsync(CreateAtmBody()))
        {
            Assert.Equal(HttpStatusCode.Conflict, profileMismatch.StatusCode);
        }

        Assert.Equal(4, lookup.FindCalls);
        Assert.Equal(0, updater.Calls);
        Assert.DoesNotContain(PayToken, host.ExceptionLog, StringComparison.Ordinal);
    }

    /// <summary>產品掛勾缺席或缺少 updater 時不得回報付款成功；查無訂單則依契約以 200 結束且不更新。</summary>
    [Fact]
    public async Task Callback_NoProductHandler_Returns503()
    {
        var gatewayWithoutLookup = new PaymentGatewayDouble();
        await using (var noLookupHost = new PaymentCallbackTestHost(gatewayWithoutLookup))
        {
            await noLookupHost.StartAsync();
            using var noLookup = await noLookupHost.PostAtmAsync(CreateAtmBody());
            Assert.Equal(HttpStatusCode.ServiceUnavailable, noLookup.StatusCode);
            Assert.Equal(1, gatewayWithoutLookup.QueryCalls);
        }

        var gatewayWithoutUpdater = new PaymentGatewayDouble();
        var lookupWithoutUpdater = new OrderLookupDouble(CreateOrder());
        await using (var noUpdaterHost = new PaymentCallbackTestHost(gatewayWithoutUpdater, lookupWithoutUpdater))
        {
            await noUpdaterHost.StartAsync();
            using var noUpdater = await noUpdaterHost.PostAtmAsync(CreateAtmBody());
            Assert.Equal(HttpStatusCode.ServiceUnavailable, noUpdater.StatusCode);
            Assert.False(lookupWithoutUpdater.CurrentOrder!.AlreadySettled);
        }

        var gatewayWithoutOrder = new PaymentGatewayDouble();
        var missingOrderLookup = new OrderLookupDouble(null);
        var missingOrderUpdater = new RecordUpdaterDouble(missingOrderLookup);
        await using var missingOrderHost = new PaymentCallbackTestHost(
            gatewayWithoutOrder, missingOrderLookup, missingOrderUpdater);
        await missingOrderHost.StartAsync();
        using var missingOrder = await missingOrderHost.PostAtmAsync(CreateAtmBody());
        Assert.Equal(HttpStatusCode.OK, missingOrder.StatusCode);
        Assert.Equal(0, missingOrderUpdater.Calls);
    }

    /// <summary>停用開關預設 false；ATM 與信用卡兩個路由都不得存在。</summary>
    [Fact]
    public async Task Callbacks_Disabled_NotMapped()
    {
        var gateway = new PaymentGatewayDouble();
        await using var host = new PaymentCallbackTestHost(gateway, enabled: false);
        await host.StartAsync();

        using var atm = await host.PostAtmAsync(CreateAtmBody());
        using var card = await host.PostCardAsync(ShopNo, PayToken);

        Assert.Equal(HttpStatusCode.NotFound, atm.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, card.StatusCode);
        Assert.Equal(0, gateway.ParseCalls);
        Assert.Equal(0, gateway.QueryCalls);
    }

    /// <summary>已知長度與 chunked 內文超過 64 KiB 都在解析或付款查詢前回 413。</summary>
    [Fact]
    public async Task Callback_BodyTooLarge_413()
    {
        var gateway = new PaymentGatewayDouble();
        var lookup = new OrderLookupDouble(CreateOrder());
        var updater = new RecordUpdaterDouble(lookup);
        await using var host = new PaymentCallbackTestHost(gateway, lookup, updater);
        await host.StartAsync();
        var oversizedBody = Encoding.UTF8.GetBytes(new string('x', 65_537));

        using (var knownLength = await host.PostRawAtmAsync(new ByteArrayContent(oversizedBody)))
        {
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, knownLength.StatusCode);
        }

        using (var chunked = await host.PostRawAtmAsync(new UnknownLengthContent(oversizedBody)))
        {
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, chunked.StatusCode);
        }

        Assert.Equal(0, gateway.ParseCalls);
        Assert.Equal(0, gateway.QueryCalls);
        Assert.Equal(0, lookup.FindCalls);
        Assert.Equal(0, updater.Calls);
    }

    private static string CreateAtmBody() =>
        $"{{\"ShopNo\":\"{ShopNo}\",\"PayToken\":\"{PayToken}\"}}";

    private static PaymentCallbackOrder CreateOrder(
        bool alreadySettled = false,
        string currency = "TWD",
        IReadOnlyList<string>? orderNumbers = null) =>
        new()
        {
            ProfileName = ProfileName,
            Currency = currency,
            OrderNumbers = orderNumbers ?? [OrderNo],
            AlreadySettled = alreadySettled,
            Items = new Dictionary<string, object?>()
        };

    private static PaymentStatusResult CreateStatus(
        PaymentStatus status = PaymentStatus.Succeeded,
        Money? amount = null,
        PaymentError? error = null) =>
        new()
        {
            Status = status,
            ProductOrderId = OrderNo,
            ProviderOrderRef = PayToken,
            ProviderTransactionId = "synthetic-transaction",
            PaidAmount = amount ?? new Money(10m, "TWD"),
            Error = error ?? PaymentError.None
        };

    private sealed class PaymentCallbackTestHost : IAsyncDisposable
    {
        private readonly string evidenceDirectory;
        private HttpClient? client;

        public WebApplication App { get; }

        public string ExceptionLog
        {
            get
            {
                var path = Path.Combine(evidenceDirectory, "Logs", "Exception.log");
                return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
            }
        }

        public PaymentCallbackTestHost(
            PaymentGatewayDouble gateway,
            OrderLookupDouble? lookup = null,
            RecordUpdaterDouble? updater = null,
            bool enabled = true)
        {
            evidenceDirectory = Path.Combine(
                Path.GetTempPath(),
                $"SpeechMessageProducts.P4-05-{Guid.NewGuid():N}");

            WebApplicationBuilder? builder = null;
            WebApplication? app = null;
            try
            {
                builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions
                {
                    EnvironmentName = "Testing",
                    ContentRootPath = Path.GetTempPath(),
                    ApplicationName = typeof(Program).Assembly.GetName().Name
                });
                builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
                builder.Logging.ClearProviders();
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ExceptionNotifications:WriteExceptionLog"] = "true",
                    ["ExceptionNotifications:SendLine"] = "false",
                    ["Payment:Callbacks:Enabled"] = enabled.ToString(),
                    ["Payment:Profiles:Sandbox:Provider"] = nameof(PaymentProviderKind.Sinopac),
                    ["Payment:Profiles:Sandbox:Credentials:ShopNo"] = ShopNo
                });
                builder.Services.AddRouting();
                builder.Services.AddSpeechMessageHosting(
                    Path.Combine(evidenceDirectory, "Logs"),
                    new ExceptionOutputOptions(writeExceptionLog: true, sendLine: false));
                builder.Services.Configure<PaymentOptions>(builder.Configuration.GetSection("Payment"));
                builder.Services.AddSpeechMessagePaymentAspNetCore();
                builder.Services.AddSingleton<IPaymentGateway>(gateway);
                if (lookup is not null)
                {
                    builder.Services.AddSingleton<IPaymentCallbackOrderLookup>(lookup);
                }

                if (updater is not null)
                {
                    builder.Services.AddSingleton<IPaymentRecordUpdater>(updater);
                }

                Directory.CreateDirectory(evidenceDirectory);
                app = builder.Build();
                app.MapSpeechMessagePaymentCallbacks();
                App = app;
            }
            catch (Exception startupException)
            {
                var cleanupFailures = new List<Exception>();
                try
                {
                    if (builder is not null && app is null)
                    {
                        ((IDisposable)builder.Configuration).Dispose();
                    }
                    else if (app is not null)
                    {
                        app.DisposeAsync().AsTask().GetAwaiter().GetResult();
                    }
                }
                catch (Exception cleanupException)
                {
                    cleanupFailures.Add(cleanupException);
                }

                try
                {
                    if (Directory.Exists(evidenceDirectory))
                    {
                        Directory.Delete(evidenceDirectory, recursive: true);
                    }
                }
                catch (Exception cleanupException)
                {
                    cleanupFailures.Add(cleanupException);
                }

                if (cleanupFailures.Count > 0)
                {
                    cleanupFailures.Insert(0, startupException);
                    throw new AggregateException(cleanupFailures);
                }

                ExceptionDispatchInfo.Capture(startupException).Throw();
                throw;
            }
        }

        public async Task StartAsync()
        {
            await App.StartAsync();
            client = new HttpClient(new SocketsHttpHandler
            {
                UseCookies = false,
                AllowAutoRedirect = false
            })
            {
                BaseAddress = new Uri(App.Urls.Single()),
                Timeout = TimeSpan.FromSeconds(10)
            };
        }

        public async Task<HttpResponseMessage> PostAtmAsync(string body)
        {
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            return await (client ?? throw new InvalidOperationException("Host not started."))
                .PostAsync("/api/QPayAtm/QPayBackendUrl", content);
        }

        public async Task<HttpResponseMessage> PostCardAsync(string shopNo, string payToken)
        {
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["ShopNo"] = shopNo,
                ["PayToken"] = payToken
            });
            return await (client ?? throw new InvalidOperationException("Host not started."))
                .PostAsync("/api/QPayCard/QPayReturnUrl", content);
        }

        public async Task<HttpResponseMessage> PostRawCardAsync(string body)
        {
            using var content = new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded");
            return await (client ?? throw new InvalidOperationException("Host not started."))
                .PostAsync("/api/QPayCard/QPayReturnUrl", content);
        }

        public async Task<HttpResponseMessage> PostRawAtmAsync(HttpContent content)
        {
            using (content)
            using (var request = new HttpRequestMessage(HttpMethod.Post, "/api/QPayAtm/QPayBackendUrl")
            {
                Content = content
            })
            {
                return await (client ?? throw new InvalidOperationException("Host not started."))
                    .SendAsync(request);
            }
        }

        public async ValueTask DisposeAsync()
        {
            var cleanupFailures = new List<Exception>();
            try
            {
                await App.StopAsync();
            }
            catch (Exception exception)
            {
                cleanupFailures.Add(exception);
            }

            try
            {
                client?.Dispose();
            }
            catch (Exception exception)
            {
                cleanupFailures.Add(exception);
            }

            try
            {
                await App.DisposeAsync();
            }
            catch (Exception exception)
            {
                cleanupFailures.Add(exception);
            }

            try
            {
                Directory.Delete(evidenceDirectory, recursive: true);
            }
            catch (Exception exception)
            {
                cleanupFailures.Add(exception);
            }

            if (cleanupFailures.Count == 1)
            {
                ExceptionDispatchInfo.Capture(cleanupFailures[0]).Throw();
            }
            else if (cleanupFailures.Count > 1)
            {
                throw new AggregateException(cleanupFailures);
            }
        }
    }

    private sealed class PaymentGatewayDouble : IPaymentGateway
    {
        public int ParseCalls { get; private set; }
        public int QueryCalls { get; private set; }
        public PaymentCallbackRequest? LastCallbackRequest { get; private set; }
        public PaymentQueryRequest? LastQueryRequest { get; private set; }
        public PaymentStatusResult StatusResult { get; set; } = CreateStatus();
        public Exception? QueryException { get; set; }

        public Task<PaymentCreateResult> CreatePaymentAsync(
            PaymentCreateRequest request,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("CreatePaymentAsync is outside the callback test path.");

        public Task<PaymentStatusResult> QueryPaymentAsync(
            PaymentQueryRequest request,
            CancellationToken cancellationToken = default)
        {
            QueryCalls++;
            LastQueryRequest = request;
            if (QueryException is not null)
            {
                return Task.FromException<PaymentStatusResult>(QueryException);
            }

            return Task.FromResult(StatusResult);
        }

        public Task<PaymentCallbackResult> ParseCallbackAsync(
            PaymentCallbackRequest request,
            CancellationToken cancellationToken = default)
        {
            ParseCalls++;
            LastCallbackRequest = request;
            return Task.FromResult(new PaymentCallbackResult
            {
                Status = PaymentStatus.Pending,
                ProviderTransactionId = PayToken,
                Error = PaymentError.None
            });
        }
    }

    private sealed class OrderLookupDouble(PaymentCallbackOrder? initialOrder) : IPaymentCallbackOrderLookup
    {
        public int FindCalls { get; private set; }
        public PaymentCallbackOrder? CurrentOrder { get; set; } = initialOrder;

        public Task<PaymentCallbackOrder?> FindAsync(
            PaymentStatusResult status,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FindCalls++;
            return Task.FromResult(CurrentOrder);
        }

        public void MarkSettled()
        {
            if (CurrentOrder is not null)
            {
                CurrentOrder = CurrentOrder with { AlreadySettled = true };
            }
        }
    }

    private sealed class RecordUpdaterDouble(OrderLookupDouble lookup) : IPaymentRecordUpdater
    {
        public int Calls { get; private set; }

        public Task UpdateAsync(PaymentPostPaymentContext context, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            lookup.MarkSettled();
            return Task.CompletedTask;
        }
    }

    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) =>
            stream.WriteAsync(bytes).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
