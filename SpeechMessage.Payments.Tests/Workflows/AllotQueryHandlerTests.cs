/*
 * 檔案整體說明
 *
 * 目的與定位：以離線合成 Dataverse、gateway 與教會時鐘替身驗證 P4-03 的待辦處理器契約，
 * 不連接永豐、D365、LINE 或任何測試／正式主機，也不讀取 User Secrets。
 *
 * 主要功能與流程：每個案例建立一筆含固定教會、收費單及合法冪等鍵的 OutboundWorkItem，
 * 由 DispatchProxy 僅模擬 FeeAllotRecords 與 ChurchPaymentProfile 實際呼叫的同步 SDK 方法；
 * 假 gateway 記錄單次查詢輸入並回傳合成批次或錯誤。測試鎖定來源與 payload 一致性、
 * 費用／會友／教會隔離、無資料不更新、失敗重試分類、教會當地日期及重複執行的穩定結果。
 *
 * 使用邊界與維護：隔離、資源與失敗驗證：替身與 Entity 都由單一案例持有，沒有靜態可變狀態、連線或背景工作；
 * Handler 只借用 SDK、gateway 與 clock，測試結束即釋放所有參照。非法 payload 必須在第一個
 * SDK 呼叫前拒絕；跨教會拒絕必須發生於永豐查詢前；可重試錯誤只轉為 HTTP 503 類例外，
 * 終止錯誤只含固定錯誤碼。所有值皆為合成資料，離線通過不代表沙箱或正式環境驗收。
 */
using System.Net;
using System.Reflection;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Hosting.Outbound;
using SpeechMessage.Localization;
using SpeechMessage.Payments.Abstractions;
using SpeechMessage.Payments.Models;
using SpeechMessage.Payments.Workflows.Allot;
using SpeechMessageProducts.GoodShepherd.Core.Fees;
using DomainMoney = SpeechMessage.Localization.Money;
using Xunit;

namespace SpeechMessage.Payments.Tests.Workflows;

/// <summary>
/// 保護 AllotQuery handler 的待辦權威資料、教會隔離、錯誤分類及資料回寫編排。
/// SDK、付款供應商與時鐘均為案例區域替身，故障注入及呼叫次數提供決定性斷言。
/// </summary>
public sealed class AllotQueryHandlerTests
{
    private static readonly Guid FeeId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ChurchId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid OtherChurchId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid ContactId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    /// <summary>無已驗簽批次時工作正常完成，且不對收費單執行更新。</summary>
    [Fact]
    public async Task Handler_NoData_CompletesWithoutUpdate()
    {
        var fixture = CreateFixture(Success());

        await fixture.Handler().ExecuteAsync(CancellationToken.None);

        Assert.Equal(1, fixture.Gateway.Calls);
        Assert.Equal(0, fixture.Sdk.UpdateCalls);
    }

    /// <summary>網路／供應商暫時不可用轉成 worker 可辨識的 HTTP 503 重試例外，不回顯供應商訊息。</summary>
    [Theory]
    [InlineData(PaymentErrorKind.NetworkFailure)]
    [InlineData(PaymentErrorKind.ProviderUnavailable)]
    public async Task Handler_Http5xxOrNetwork_IsRetryable(PaymentErrorKind kind)
    {
        var fixture = CreateFixture(Failure(kind));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => fixture.Handler().ExecuteAsync(CancellationToken.None));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
        Assert.Equal(0, fixture.Sdk.UpdateCalls);
    }

    /// <summary>供應商明確拒絕屬終止失敗，例外只含固定分類碼且不更新 D365。</summary>
    [Fact]
    public async Task Handler_ProviderRejected_NotRetried()
    {
        var fixture = CreateFixture(Failure(PaymentErrorKind.ProviderRejected));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Handler().ExecuteAsync(CancellationToken.None));

        Assert.Equal("ProviderRejected", exception.Message);
        Assert.Equal(0, fixture.Sdk.UpdateCalls);
    }

    /// <summary>收費單會友屬於另一教會時，在讀完關聯後立即拒絕且不得呼叫永豐或寫入。</summary>
    [Fact]
    public async Task Handler_FeeOfOtherChurch_Rejected()
    {
        var fixture = CreateFixture(Success(), memberChurchId: OtherChurchId);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Handler().ExecuteAsync(CancellationToken.None));

        Assert.Equal("Fee is outside the authorized church.", exception.Message);
        Assert.Equal(0, fixture.Gateway.Calls);
        Assert.Equal(0, fixture.Sdk.UpdateCalls);
    }

    /// <summary>相同伺服器待辦重跑會重新讀取並產生相同的完整回寫值；撥款日期須以教會午夜轉成 UTC，且不沿用前次操作的 mutable SDK Entity。</summary>
    [Fact]
    public async Task Handler_SameTaskTwice_SameResult()
    {
        var fixture = CreateFixture(Success(MatchingBatch()));
        var handler = fixture.Handler();

        await handler.ExecuteAsync(CancellationToken.None);
        await handler.ExecuteAsync(CancellationToken.None);

        Assert.Equal(2, fixture.Gateway.Calls);
        Assert.Equal(2, fixture.Sdk.UpdateCalls);
        Assert.All(fixture.Sdk.Updates, update =>
        {
            Assert.Equal(new Microsoft.Xrm.Sdk.Money(123.00m), Assert.IsType<Microsoft.Xrm.Sdk.Money>(
                update.Target["new_allot_amount"]));
            Assert.Equal(new Microsoft.Xrm.Sdk.Money(0.45m), Assert.IsType<Microsoft.Xrm.Sdk.Money>(
                update.Target["new_allot_fee"]));
            Assert.Equal(new DateTime(2026, 11, 9, 16, 0, 0, DateTimeKind.Utc), Assert.IsType<DateTime>(
                update.Target["new_allot_date"]));
            Assert.Equal("SYN-ALLOT-001", Assert.IsType<string>(update.Target["new_allot_no"]));
        });
    }

    /// <summary>HTTP 200 但不可解析的供應商內容不得視為無資料，必須以 503 類失敗供 worker 重試。</summary>
    [Fact]
    public async Task Handler_UnreadableResponse_IsRetryable()
    {
        var fixture = CreateFixture(Failure(PaymentErrorKind.SerializationFailure));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => fixture.Handler().ExecuteAsync(CancellationToken.None));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
        Assert.Equal(0, fixture.Sdk.UpdateCalls);
    }

    /// <summary>payload 僅允許一個合法 Guid feeId；缺欄、多欄、重複欄或非法 JSON 均須在任何 SDK I/O 前拒絕。</summary>
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"feeId\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\",\"churchId\":\"bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb\"}")]
    [InlineData("{\"feeId\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\",\"feeId\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\"}")]
    [InlineData("{not-json")]
    public async Task Handler_InvalidPayload_Rejected(string payload)
    {
        var fixture = CreateFixture(Success());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Handler(payload).ExecuteAsync(CancellationToken.None));

        Assert.Equal("InvalidAllotQueryPayload", exception.Message);
        Assert.Equal(0, fixture.Sdk.RetrieveCalls);
        Assert.Equal(0, fixture.Gateway.Calls);
    }

    /// <summary>冪等鍵缺少有效來源收費單 ID 時，在任何 CRM 或永豐呼叫前拒絕該待辦。</summary>
    [Fact]
    public async Task Handler_MissingSourceId_Rejected()
    {
        var fixture = CreateFixture(Success());
        var item = CreateItem(idempotencyKey: $"{ChurchId:N}-{(int)OutboundWorkType.AllotQuery}-{Guid.Empty:N}");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Handler(item).ExecuteAsync(CancellationToken.None));

        Assert.Equal("InvalidAllotQueryPayload", exception.Message);
        Assert.Equal(0, fixture.Sdk.RetrieveCalls);
        Assert.Equal(0, fixture.Gateway.Calls);
    }

    /// <summary>payload feeId 不等於伺服器待辦來源 ID 時拒絕，不能藉 payload 改換被查詢或更新的收費單。</summary>
    [Fact]
    public async Task Handler_PayloadDoesNotMatchSourceId_Rejected()
    {
        var fixture = CreateFixture(Success());
        var otherFeeId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        var item = CreateItem(
            payload: $"{{\"feeId\":\"{otherFeeId:D}\"}}",
            idempotencyKey: $"{ChurchId:N}-{(int)OutboundWorkType.AllotQuery}-{FeeId:N}");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Handler(item).ExecuteAsync(CancellationToken.None));

        Assert.Equal("InvalidAllotQueryPayload", exception.Message);
        Assert.Equal(0, fixture.Sdk.RetrieveCalls);
        Assert.Equal(0, fixture.Gateway.Calls);
    }

    /// <summary>跨 UTC 午夜時以已核對教會時區取得的本地今日建立日期窗，不能使用伺服器 UTC 日期。</summary>
    [Fact]
    public async Task Handler_UsesChurchToday_AcrossUtcMidnight()
    {
        var churchToday = new DateTime(2026, 11, 13, 0, 0, 0, DateTimeKind.Unspecified);
        var fixture = CreateFixture(
            Success(),
            clock: new FixedChurchClock(
                new DateTimeOffset(2026, 11, 12, 23, 30, 0, TimeSpan.Zero),
                churchToday));

        await fixture.Handler().ExecuteAsync(CancellationToken.None);

        Assert.Equal("Asia/Taipei", fixture.Clock.LastTimeZoneId);
        Assert.NotNull(fixture.Gateway.LastRequest);
        Assert.Equal(new DateOnly(2026, 10, 28), fixture.Gateway.LastRequest!.FromDate);
        Assert.Equal(new DateOnly(2026, 11, 13), fixture.Gateway.LastRequest.ToDate);
    }

    private static Fixture CreateFixture(
        PaymentAllotQueryResult result,
        Guid? memberChurchId = null,
        FixedChurchClock? clock = null)
    {
        var sdk = new SyntheticOrganizationService(FeeId, ChurchId, ContactId, memberChurchId ?? ChurchId);
        var gateway = new SyntheticAllotGateway(result);
        var effectiveClock = clock ?? new FixedChurchClock(
            new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero),
            new DateTime(2026, 11, 13, 0, 0, 0, DateTimeKind.Unspecified));
        return new Fixture(sdk, gateway, effectiveClock);
    }

    private static PaymentAllotQueryResult Success(params PaymentAllotBatch[] batches) =>
        new() { Error = PaymentError.None, Batches = batches };

    private static PaymentAllotQueryResult Failure(PaymentErrorKind kind) =>
        new()
        {
            Error = new PaymentError { Kind = kind, Code = kind.ToString(), Message = "SYNTHETIC-PRIVATE-MARKER" }
        };

    private static PaymentAllotBatch MatchingBatch() =>
        new()
        {
            AllotNo = "SYN-ALLOT-001",
            AllotDate = new DateOnly(2026, 11, 10),
            Items = new[]
            {
                new PaymentAllotItem
                {
                    OrderNo = "SYN-ATM-001",
                    Amount = new DomainMoney(123.45m, "TWD"),
                    Fee = new DomainMoney(0.45m, "TWD")
                }
            }
        };

    private static OutboundWorkItem CreateItem(
        string? payload = null,
        string? idempotencyKey = null) =>
        new(
            Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
            "1",
            ChurchId,
            null,
            OutboundWorkType.AllotQuery,
            payload ?? $"{{\"feeId\":\"{FeeId:D}\"}}",
            idempotencyKey ?? $"{ChurchId:N}-{(int)OutboundWorkType.AllotQuery}-{FeeId:N}",
            OutboundWorkStatus.Processing,
            0,
            null,
            new DateTime(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc));

    private sealed record Fixture(
        SyntheticOrganizationService Sdk,
        SyntheticAllotGateway Gateway,
        FixedChurchClock Clock)
    {
        internal AllotQueryHandler Handler(string? payload = null) =>
            Handler(CreateItem(payload));

        internal AllotQueryHandler Handler(OutboundWorkItem item) =>
            new(item, Sdk.Service, Gateway, Clock);
    }

    /// <summary>案例區域 handler 只回傳合成結果並保存本次請求供欄位斷言，不保存設定或跨案例狀態。</summary>
    private sealed class SyntheticAllotGateway(PaymentAllotQueryResult result) : IPaymentAllotGateway
    {
        internal int Calls { get; private set; }
        internal PaymentAllotQueryRequest? LastRequest { get; private set; }

        public Task<PaymentAllotQueryResult> QueryAllotAsync(
            PaymentAllotQueryRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            LastRequest = request;
            return Task.FromResult(result);
        }
    }

    /// <summary>明確固定 UTC 與 church-local 今日；不依賴測試機時區或系統時鐘。</summary>
    private sealed class FixedChurchClock(DateTimeOffset utcNow, DateTime churchToday) : IChurchClock
    {
        internal string? LastTimeZoneId { get; private set; }
        public DateTimeOffset UtcNow => utcNow;

        public DateTime ToChurchTime(DateTime utc, string ianaTimeZoneId) =>
            throw new NotSupportedException("This test only exercises TodayInChurch.");

        public DateTime TodayInChurch(string ianaTimeZoneId)
        {
            LastTimeZoneId = ianaTimeZoneId;
            return churchToday;
        }

        public DateTime ToUtc(LocalDateTimeOfChurch local) =>
            throw new NotSupportedException("This test does not convert church-local input to UTC.");
    }

    /// <summary>DispatchProxy 僅實作 FeeAllotRecords 與 ChurchPaymentProfile 使用的 SDK 讀取及更新入口。</summary>
    private sealed class SyntheticOrganizationService
    {
        private readonly Guid _feeId;
        private readonly Guid _churchId;
        private readonly Guid _contactId;
        private readonly Guid _memberChurchId;
        private UpdateRequest? _lastUpdate;
        private bool _awaitingReadBack;

        internal SyntheticOrganizationService(Guid feeId, Guid churchId, Guid contactId, Guid memberChurchId)
        {
            _feeId = feeId;
            _churchId = churchId;
            _contactId = contactId;
            _memberChurchId = memberChurchId;
            Service = DispatchProxy.Create<IOrganizationService, OrganizationServiceProxy>();
            ((OrganizationServiceProxy)(object)Service).InvokeMethod = Invoke;
        }

        internal IOrganizationService Service { get; }
        internal int RetrieveCalls { get; private set; }
        internal int UpdateCalls { get; private set; }
        internal List<UpdateRequest> Updates { get; } = new();

        private object? Invoke(MethodInfo method, object?[]? arguments)
        {
            if (method.Name == nameof(IOrganizationService.Retrieve))
            {
                RetrieveCalls++;
                var entityName = Assert.IsType<string>(arguments![0]);
                var id = Assert.IsType<Guid>(arguments[1]);
                return entityName switch
                {
                    "new_fee" when id == _feeId && _awaitingReadBack => CreateUpdatedFee(),
                    "new_fee" when id == _feeId => CreateFee(),
                    "contact" when id == _contactId => CreateContact(),
                    "account" when id == _churchId => CreateChurch(),
                    _ => throw new InvalidOperationException("Unexpected synthetic retrieve.")
                };
            }

            if (method.Name == nameof(IOrganizationService.Execute))
            {
                var update = Assert.IsType<UpdateRequest>(arguments![0]);
                UpdateCalls++;
                _lastUpdate = update;
                _awaitingReadBack = true;
                Updates.Add(update);
                return new UpdateResponse();
            }

            throw new NotSupportedException($"Unexpected SDK operation: {method.Name}");
        }

        private Entity CreateFee()
        {
            var entity = new Entity("new_fee", _feeId) { RowVersion = "1" };
            entity["new_pay_way"] = new OptionSetValue(100000002);
            entity["new_contact_new_fee"] = new EntityReference("contact", _contactId);
            entity["new_q_pay_order_atm_no"] = "SYN-ATM-001";
            entity["new_q_paid_order_atm_no"] = string.Empty;
            return entity;
        }

        private Entity CreateUpdatedFee()
        {
            var entity = new Entity("new_fee", _feeId);
            entity["new_allot_date"] = _lastUpdate!.Target.GetAttributeValue<DateTime>("new_allot_date");
            _awaitingReadBack = false;
            return entity;
        }

        private Entity CreateContact()
        {
            var entity = new Entity("contact", _contactId);
            entity["parentcustomerid"] = new EntityReference("account", _memberChurchId);
            return entity;
        }

        private Entity CreateChurch()
        {
            var entity = new Entity("account", _churchId);
            entity["new_timezone"] = "Asia/Taipei";
            entity["new_sinopacprofile"] = "SyntheticTestOnly";
            return entity;
        }
    }

    /// <summary>同步 SDK 代理的每個呼叫都交給目前測試 owner；不連線且不保留案例間狀態。</summary>
    public class OrganizationServiceProxy : DispatchProxy
    {
        internal Func<MethodInfo, object?[]?, object?> InvokeMethod { get; set; } =
            (_, _) => throw new InvalidOperationException("Synthetic SDK proxy was not initialized.");

        /// <summary>只分派到建構時注入的案例函式，其餘 SDK API 一律 fail closed。</summary>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            InvokeMethod(targetMethod ?? throw new ArgumentNullException(nameof(targetMethod)), args);
    }
}
