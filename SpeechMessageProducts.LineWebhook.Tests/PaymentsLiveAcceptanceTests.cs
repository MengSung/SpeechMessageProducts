/*
 * 檔案整體說明
 *
 * 目的與定位：承接 P4-06 唯一核准的永豐沙箱與 goodshepherdback 實機驗收；案例使用正式 Payments gateway、Outbound registration、Worker 與 D365 儲存層，不啟動 LineWebhook HTTP 主機。
 * 主要功能與流程：每案先從 LineWebhook User Secrets 讀取固定教會 ID、Development CRM 與 Sandbox profile，確認 CRM HTTPS 主機、沙箱 API 主機、必要設定鍵及 account.new_timezone，再建立本次專屬 Contact、Fee、Outbound Task。測試完成後按正式資料 ID 刪除待辦、收費單與會友，並只在 account 設定仍等於本次測試值時恢復原 profile。
 * 使用邊界與維護：User Secrets 是設定來源，不是教會授權；教會 ID 必須是前置檢查取得的正式 ID，profile 由 account 欄位提供。每個 xUnit 案例擁有獨立 Host、DI scope、Dataverse 服務與測試資料；Worker 停止並排空後才清理資料、釋放 scope、Host 與設定根，不共用跨案例狀態。
 * 失敗與驗證：任何環境不符都在建立資料前 fail closed；測試只使用新永豐沙箱端點，不送真實付款。即時查詢案例最多等待 120 秒；一旦看到 Failed 或 Pending 且 attempts 大於零，立即停止 Worker、記錄安全的 new_lasterror 與 gateway 階段診斷並失敗，避免等待完整 timeout。診斷只接受固定階段／錯誤代碼、數字 HTTP 狀態與合規 APIM 數字碼，不讀取例外本文、request、response body 或秘密。寫回案例以合成 gateway 比對舊公式並明標不是永豐實回，收費日與撥款日共用同一個教會曆日，且逐欄精確比對金額、手續費、日期及撥款編號。合成寫回前唯讀查 TWD 幣別精度及兩個 Money 欄位精度，並在本機測試輸出記錄讀回金額與精度；純值回歸測試保護合成金額選擇不受幣別／欄位精度混淆。冪等案例核對正式待辦 ID、attempts、modifiedon 與單次 gateway 呼叫。每案最後以精確識別查詢確認殘留為零。
 */
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Hosting.Outbound;
using SpeechMessage.Localization;
using SpeechMessage.Payments.Abstractions;
using SpeechMessage.Payments.DependencyInjection;
using SpeechMessage.Payments.Gateway;
using SpeechMessage.Payments.Models;
using SpeechMessage.Payments.AspNetCore.Outbound;
using SpeechMessageProducts.GoodShepherd.Core.Outbound;
using ToolUtilityNameSpace.DependencyInjection;
using ToolUtilityNameSpace.Outbound;
using Xunit;
using Xunit.Abstractions;
using DataverseMoney = Microsoft.Xrm.Sdk.Money;
using LocalizationMoney = SpeechMessage.Localization.Money;

namespace SpeechMessageProducts.LineWebhook.Tests;

/// <summary>
/// 驗證 P4-06 三個核准的沙箱實機案例。測試會修改 goodshepherdback 測試資料，
/// 並以 User Secrets 驅動 Development CRM 與永豐 Sandbox；一般測試由 runsettings 排除本類別。
/// </summary>
public sealed class PaymentsLiveAcceptanceTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>保存 xUnit 輸出 owner；只寫 provider 驗簽後狀態碼與清理計數，不輸出組態或憑證。</summary>
    /// <param name="output">由測試執行器擁有且僅在本案例期間使用的輸出介面。</param>
    public PaymentsLiveAcceptanceTests(ITestOutputHelper output) =>
        _output = output ?? throw new ArgumentNullException(nameof(output));

    /// <summary>
    /// 將待辦錯誤欄位限制為 provider 固定代碼或階段／HTTP／APIM 格式；
    /// D365 中可能存在舊資料或外部寫入內容，不直接把任意字串送入測試輸出。
    /// </summary>
    private static string SafeDiagnosticErrorCode(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "None";

        if (value is "NetworkFailure" or "ProviderUnavailable" or "SignatureInvalid"
            or "SerializationFailure" or "ConfigurationInvalid" or "RequestInvalid"
            or "ProviderRejected" or "Timeout" or "Http429" or "Http5xx" or "Unhandled")
            return value;

        var parts = value.Split(':');
        if (parts.Length is < 2 or > 3 || parts[0] is not ("Nonce" or "Order"))
            return "Other";

        if (parts.Length == 2 && parts[1] is "NonceMissing" or "Timeout" or "Transport" or "Unreadable")
            return value;

        if (!int.TryParse(parts[1], System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var statusCode)
            || statusCode is < 100 or > 599)
            return "Other";

        if (parts.Length == 2)
            return value;

        return parts[2].Length is > 0 and <= 4
            && parts[2].All(character => character is >= '0' and <= '9')
                ? value
                : "Other";
    }

    /// <summary>
    /// 以未完成的合成 gateway 工作注入明確等待狀態，確認診斷可區別呼叫已進入但尚未返回；
    /// finally 會完成受控工作，確保離線測試不留下未完成 Task 或背景資源。
    /// </summary>
    [Fact]
    [Trait("Category", "PaymentsOffline")]
    public async Task RecordingGateway_TracksInFlightUntilVerifiedResult()
    {
        var completion = new TaskCompletionSource<PaymentAllotQueryResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var gateway = new RecordingAllotGateway(new DeferredAllotGateway(completion.Task));
        var operation = gateway.QueryAllotAsync(new PaymentAllotQueryRequest());

        try
        {
            Assert.Equal(1, gateway.CallsStarted);
            Assert.Equal(0, gateway.CallsCompleted);
            Assert.Equal("InFlight", gateway.LastOutcome);
        }
        finally
        {
            completion.TrySetResult(new PaymentAllotQueryResult
            {
                ProviderStatus = "S",
                Batches = Array.Empty<PaymentAllotBatch>()
            });
        }

        var result = await operation;
        Assert.Equal("S", result.ProviderStatus);
        Assert.Equal(1, gateway.CallsCompleted);
        Assert.Equal("Success", gateway.LastOutcome);
    }

    /// <summary>
    /// 先依幣別精度選擇合成金額；離線回歸測試會確認欄位固定精度也必須納入決策。
    /// </summary>
    private static (decimal Amount, decimal Fee) SelectSyntheticAmounts(
        int currencyPrecision,
        AllotMoneyPrecision moneyPrecision)
    {
        var amountSupportsFractions = SupportsFractionalDigits(currencyPrecision, moneyPrecision.AllotAmount);
        var feeSupportsFractions = SupportsFractionalDigits(currencyPrecision, moneyPrecision.AllotFee);
        return amountSupportsFractions && feeSupportsFractions
            ? (1234.56m, 23.45m)
            : (1234m, 23m);
    }

    /// <summary>只在欄位metadata明確允許至少兩位小數時選小數合成值；未知來源採整數保守值。</summary>
    private static bool SupportsFractionalDigits(int currencyPrecision, MoneyFieldPrecision field) =>
        field.PrecisionSource switch
        {
            0 => field.Precision is >= 2,
            2 => currencyPrecision >= 2,
            _ => false
        };

    /// <summary>
    /// 經真實永豐 Sandbox gateway 執行一次撥款查詢；唯一測試訂單不可能預先存在，
    /// 因此要求 Worker 於 120 秒內完成、回應已驗簽且四個撥款欄位保持原值。
    /// </summary>
    [Fact]
    [Trait("Category", "PaymentsLive")]
    public async Task Live_AllotQuery_SandboxRoundTrip()
    {
        await using var fixture = await LiveFixture.CreateAsync(_output);
        fixture.CreateTestRows();
        var before = fixture.ReadFeeSnapshot();
        var taskId = fixture.EnqueueAllotQuery();

        await fixture.RunWorkerUntilCompletedAsync(taskId, TimeSpan.FromSeconds(120));

        Assert.Equal(before, fixture.ReadFeeSnapshot());
        var status = fixture.VerifiedProviderStatus;
        Assert.False(string.IsNullOrWhiteSpace(status));
        _output.WriteLine("Verified Sinopac sandbox provider status: {0}", status);
        await fixture.CleanupAndVerifyZeroResidueAsync();
    }

    /// <summary>
    /// 離線保護合成回應金額必須同時符合 currency 與 Money 欄位精度；
    /// 固定 PrecisionSource=0、Precision=0 的合成metadata可重現本次欄位捨入案例，不連 D365。
    /// </summary>
    [Theory]
    [Trait("Category", "PaymentsOffline")]
    [InlineData(2, 0, 0, 0, 0, 1234, 23)]
    [InlineData(2, 2, 0, 2, 0, 1234.56, 23.45)]
    [InlineData(2, 0, 2, 0, 2, 1234.56, 23.45)]
    [InlineData(0, 2, 2, 2, 2, 1234, 23)]
    [InlineData(2, 2, 1, 2, 1, 1234, 23)]
    public void SyntheticAmounts_RespectD365FieldPrecision(
        int currencyPrecision,
        int allotAmountPrecision,
        int allotAmountPrecisionSource,
        int allotFeePrecision,
        int allotFeePrecisionSource,
        decimal expectedAmount,
        decimal expectedFee)
    {
        var actual = SelectSyntheticAmounts(
            currencyPrecision,
            new AllotMoneyPrecision(
                new MoneyFieldPrecision(allotAmountPrecision, allotAmountPrecisionSource),
                new MoneyFieldPrecision(allotFeePrecision, allotFeePrecisionSource)));

        Assert.Equal(expectedAmount, actual.Amount);
        Assert.Equal(expectedFee, actual.Fee);
    }

    /// <summary>
    /// 以合成撥款批次經相同真實 D365 Worker 管線回寫，核對 Amount-Fee 與當日日期；
    /// 此案例的永豐回應由替身合成，不能當成供應商實機回應。
    /// </summary>
    [Fact]
    [Trait("Category", "PaymentsLive")]
    public async Task Live_AllotWriteBack_SyntheticBatch_MatchesLegacy()
    {
        DateOnly? syntheticAllotDate = null;
        var syntheticAllotNo = "P4-06-SYNTHETIC-" + Guid.NewGuid().ToString("N");
        var syntheticAllotAmount = 0m;
        var syntheticAllotFee = 0m;
        SyntheticAllotGateway? gateway = null;
        await using var fixture = await LiveFixture.CreateAsync(
            _output,
            orderNo =>
            {
                gateway = new SyntheticAllotGateway(
                    () => new PaymentAllotQueryResult
                    {
                        ProviderStatus = "SYNTHETIC",
                        Batches = new[]
                        {
                            new PaymentAllotBatch
                            {
                                AllotNo = syntheticAllotNo,
                                AllotDate = syntheticAllotDate
                                    ?? throw new InvalidOperationException("The synthetic church date was not initialized."),
                                Items = new[]
                                {
                                    new PaymentAllotItem
                                    {
                                        OrderNo = orderNo,
                                        Amount = new LocalizationMoney(syntheticAllotAmount, "TWD"),
                                        Fee = new LocalizationMoney(syntheticAllotFee, "TWD")
                                    }
                                }
                            }
                        }
                    });
                return gateway;
            });
        var currencyPrecision = fixture.ReadTwdCurrencyPrecision();
        var moneyPrecision = fixture.ReadAllotMoneyPrecision();
        var allotDate = fixture.TodayInChurch;
        syntheticAllotDate = allotDate;
        var syntheticAmounts = SelectSyntheticAmounts(currencyPrecision, moneyPrecision);
        syntheticAllotAmount = syntheticAmounts.Amount;
        syntheticAllotFee = syntheticAmounts.Fee;
        var expectedAllotAmount = syntheticAllotAmount - syntheticAllotFee;
        _output.WriteLine(
            "Synthetic provider values: amount={0}; fee={1}; expected new_allot_amount={2}; expected new_allot_fee={3}; transactioncurrency.currencyprecision={4}",
            syntheticAllotAmount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            syntheticAllotFee.ToString(System.Globalization.CultureInfo.InvariantCulture),
            expectedAllotAmount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            syntheticAllotFee.ToString(System.Globalization.CultureInfo.InvariantCulture),
            currencyPrecision.ToString(System.Globalization.CultureInfo.InvariantCulture));
        fixture.CreateTestRows(allotDate);
        var taskId = fixture.EnqueueAllotQuery();

        try
        {
            await fixture.RunWorkerUntilCompletedAsync(taskId, TimeSpan.FromSeconds(60));

            var updated = fixture.ReadFeeSnapshot();
            _output.WriteLine(
                "Synthetic D365 read-back: new_allot_amount={0}; new_allot_fee={1}; transactioncurrency.currencyprecision={2}",
                updated.AllotAmount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null",
                updated.AllotFee?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null",
                currencyPrecision.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal((decimal?)expectedAllotAmount, updated.AllotAmount);
            Assert.Equal((decimal?)syntheticAllotFee, updated.AllotFee);
            Assert.NotNull(updated.AllotDate);
            Assert.Equal(allotDate, DateOnly.FromDateTime(updated.AllotDate.Value));
            Assert.Equal(syntheticAllotNo, updated.AllotNo);
        }
        catch (Exception exception)
        {
            fixture.WriteFailureDiagnostics(
                "synthetic",
                taskId,
                gateway!.CallCount,
                allotDate,
                syntheticAllotNo,
                expectedAllotAmount,
                syntheticAllotFee,
                currencyPrecision);
            _output.WriteLine("Synthetic case failure type: {0}", exception.GetType().Name);
            throw;
        }

        _output.WriteLine("Allot response source: synthetic gateway; this is not a Sinopac response.");
        await fixture.CleanupAndVerifyZeroResidueAsync();
    }

    /// <summary>
    /// 對同一 fee 連續入列兩次，確認資料庫替代鍵回傳同一正式 task ID，
    /// 第二次入列不變更 attempts 或 fee 的 modifiedon，且真正 Worker 只呼叫 gateway 一次。
    /// </summary>
    [Fact]
    [Trait("Category", "PaymentsLive")]
    public async Task Live_SameFeeEnqueuedTwice_ExecutesOnce()
    {
        var gateway = new CountingEmptyAllotGateway();
        await using var fixture = await LiveFixture.CreateAsync(_output, _ => gateway);
        fixture.CreateTestRows();
        var firstTaskId = fixture.EnqueueAllotQuery();
        var beforeDuplicate = fixture.ReadTaskSnapshot(firstTaskId);
        var feeModifiedOnBeforeDuplicate = fixture.ReadFeeModifiedOn();
        var secondTaskId = fixture.EnqueueAllotQuery();
        var afterDuplicate = fixture.ReadTaskSnapshot(firstTaskId);
        var feeModifiedOnAfterDuplicate = fixture.ReadFeeModifiedOn();

        Assert.Equal(firstTaskId, secondTaskId);
        Assert.Equal(beforeDuplicate.Attempts, afterDuplicate.Attempts);
        Assert.Equal(feeModifiedOnBeforeDuplicate, feeModifiedOnAfterDuplicate);

        await fixture.RunWorkerUntilCompletedAsync(firstTaskId, TimeSpan.FromSeconds(60));

        Assert.Equal(1, gateway.CallCount);
        await fixture.CleanupAndVerifyZeroResidueAsync();
    }

    /// <summary>
    /// 單次測試的唯一資料／連線 owner。初始化先驗證所有外部邊界，再唯讀取得教會狀態；
    /// 清理先停止 Worker，後依本次正式 ID 刪除資料並條件式還原 profile。
    /// </summary>
    private sealed class LiveFixture : IAsyncDisposable
    {
        private const string SandboxApiHost = "apisbx.sinopac.com";
        private const int AllotQueryPaymentWay = 100000001;
        private const int AllotQueryTaskStatusCompleted = 100000002;
        private readonly ITestOutputHelper _output;
        private readonly string _orderNo = "C-P4-06-" + Guid.NewGuid().ToString("N");
        private readonly string _contactName = "P4-06-TEST-" + Guid.NewGuid().ToString("N");
        private readonly string _feeName = "P4-06-TEST-" + Guid.NewGuid().ToString("N");
        private readonly List<Guid> _taskIds = new();
        private readonly ConcurrentQueue<string> _workerFailures = new();
        private IConfigurationRoot? _configuration;
        private IHost? _host;
        private IServiceScope? _crmScope;
        private IOrganizationService? _sdk;
        private Guid _churchId;
        private Guid _contactId;
        private Guid _feeId;
        private string _profileName = string.Empty;
        private string _idempotencyKey = string.Empty;
        private string? _originalProfile;
        private bool _originalProfileAttributePresent;
        private bool _profileMutationAttempted;
        private bool _workerStarted;
        private bool _workerStopped;
        private bool _cleanupFinalized;
        private bool _cleanupCompleted;
        private int _workerFailureCount;
        private RecordingAllotGateway? _recordingGateway;
        private int? _twdCurrencyPrecision;
        private string _timeZoneId = "Asia/Taipei";
        private string _timeZoneState = "未讀取";

        /// <summary>只由工廠建立；整個案例使用獨立的教會、資料 ID 與資源 owner。</summary>
        private LiveFixture(ITestOutputHelper output) => _output = output;

        /// <summary>供即時案例取得驗簽成功的供應商狀態碼；合成案例不使用此值。</summary>
        internal string? VerifiedProviderStatus => _recordingGateway?.VerifiedProviderStatus;

        /// <summary>從已唯讀驗證的教會時區取得純日曆日期，不使用開發機時區。</summary>
        internal DateOnly TodayInChurch =>
            DateOnly.FromDateTime(
                _host!.Services.GetRequiredService<IChurchClock>().TodayInChurch(_timeZoneId));

        /// <summary>建立案例設定、檢查沙箱邊界、連線唯讀讀取 account，最後才建立測試資料。</summary>
        internal static async Task<LiveFixture> CreateAsync(
            ITestOutputHelper output,
            Func<string, IPaymentAllotGateway>? gatewayFactory = null)
        {
            var fixture = new LiveFixture(output);
            try
            {
                fixture.LoadAndValidateConfiguration();
                fixture.BuildHost(gatewayFactory);
                fixture.OpenCrmScope();
                fixture.ReadAndValidateChurchBeforeWrites();
                return fixture;
            }
            catch
            {
                await fixture.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// 建立本次唯一 contact 與 fee，確認每次 SDK Create 均回傳非空正式 ID；
        /// 可指定教會純日期讓 fee 與合成撥款批次共用曆日；最後才切換 account profile。
        /// </summary>
        /// <param name="churchPayDate">
        /// 可選教會曆日；未提供時使用已驗證教會時區的今日。寫入前會將該日午夜轉為 UTC。
        /// </param>
        internal void CreateTestRows(DateOnly? churchPayDate = null)
        {
            var contact = new Entity("contact")
            {
                ["lastname"] = _contactName,
                ["description"] = "P4-06 test-only member; delete by returned record ID.",
                ["parentcustomerid"] = new EntityReference("account", _churchId)
            };
            _contactId = _sdk!.Create(contact);
            if (_contactId == Guid.Empty)
                throw new InvalidOperationException("The test contact did not receive a Dataverse identity.");

            var churchClock = _host!.Services.GetRequiredService<IChurchClock>();
            var churchPayDay = churchPayDate
                ?? DateOnly.FromDateTime(churchClock.TodayInChurch(_timeZoneId));
            var churchPayMidnight = DateTime.SpecifyKind(
                churchPayDay.ToDateTime(TimeOnly.MinValue),
                DateTimeKind.Unspecified);
            var fee = new Entity("new_fee")
            {
                ["new_name"] = _feeName,
                ["new_contact_new_fee"] = new EntityReference("contact", _contactId),
                ["new_pay_way"] = new OptionSetValue(AllotQueryPaymentWay),
                ["new_pay_date"] = churchClock.ToUtc(
                    new LocalDateTimeOfChurch(churchPayMidnight, _timeZoneId)),
                ["new_q_pay_card_order_no"] = _orderNo,
                ["new_q_paid_card_order_no"] = string.Empty,
                ["new_q_pay_order_atm_no"] = string.Empty,
                ["new_q_paid_order_atm_no"] = string.Empty
            };
            _feeId = _sdk.Create(fee);
            if (_feeId == Guid.Empty)
                throw new InvalidOperationException("The test fee did not receive a Dataverse identity.");

            SetChurchProfileForThisTest();
            _idempotencyKey = string.Concat(
                _churchId.ToString("N"),
                "-",
                ((int)OutboundTaskType.AllotQuery).ToString(System.Globalization.CultureInfo.InvariantCulture),
                "-",
                _feeId.ToString("N"));
        }

        /// <summary>
        /// 以真正的 OutboundTaskWriter 及 D365 alternate key 入列；只保存資料庫回傳的 task ID，
        /// 清理時不以來源 fee ID、名稱或猜測值替代。
        /// </summary>
        internal Guid EnqueueAllotQuery()
        {
            EnsureTestRowsExist();
            using var store = new D365OutboundEnqueueStore(_sdk!);
            var writer = new OutboundTaskWriter(store);
            var taskId = writer.Enqueue(
                OutboundTaskType.AllotQuery,
                _churchId,
                _contactId,
                _feeId,
                new Dictionary<string, object> { ["feeId"] = _feeId },
                CancellationToken.None);
            if (taskId == Guid.Empty)
                throw new InvalidOperationException("The test task did not receive a Dataverse identity.");
            if (!_taskIds.Contains(taskId))
                _taskIds.Add(taskId);
            return taskId;
        }

        /// <summary>
        /// 最多等待指定時間，直到真實 D365 store 與 Worker 將本次 task 標記 Completed。
        /// 若 Worker 已記錄失敗或完成一次可重試嘗試後回到 Pending，立即停止它並輸出安全診斷，
        /// 不再等待後續退避重試；測試 owner 隨即透過 finally 清理本次資料與設定。
        /// </summary>
        internal async Task RunWorkerUntilCompletedAsync(Guid taskId, TimeSpan timeout)
        {
            if (taskId == Guid.Empty || timeout <= TimeSpan.Zero || timeout > TimeSpan.FromSeconds(120))
                throw new ArgumentOutOfRangeException(nameof(timeout));

            _workerStarted = true;
            await _host!.StartAsync().ConfigureAwait(false);
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed < timeout)
            {
                var snapshot = ReadTaskSnapshot(taskId);
                if (snapshot.Status == AllotQueryTaskStatusCompleted)
                {
                    await StopWorkerAsync().ConfigureAwait(false);
                    return;
                }

                if (snapshot.Status == (int)OutboundWorkStatus.Failed
                    || snapshot.Status == (int)OutboundWorkStatus.Pending && snapshot.Attempts > 0)
                {
                    await StopWorkerAsync().ConfigureAwait(false);
                    WriteSandboxFailureDiagnostics(taskId, ReadTaskSnapshot(taskId));
                    throw new InvalidOperationException(
                        "The P4-06 outbound task did not complete: " + snapshot.ErrorCode);
                }

                await Task.Delay(TimeSpan.FromSeconds(1), TimeProvider.System).ConfigureAwait(false);
            }

            await StopWorkerAsync().ConfigureAwait(false);
            WriteSandboxTimeoutDiagnostics(taskId);
            throw new TimeoutException(
                "The P4-06 outbound task did not complete within "
                + timeout.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + " seconds.");
        }

        /// <summary>只輸出已清理的 new_lasterror 與固定 gateway 分類；不讀取 provider 原始訊息。</summary>
        private void WriteSandboxFailureDiagnostics(Guid taskId, TaskSnapshot task)
        {
            var gateway = _recordingGateway;
            _output.WriteLine(
                "Sandbox attempt diagnostic: taskStatus={0}; attempts={1}; new_lasterror={2}; gatewayStarted={3}; gatewayCompleted={4}; gatewayOutcome={5}; verifiedProviderStatus={6}; workerFailures={7}",
                SafeTaskStatus(task.Status),
                task.Attempts,
                task.ErrorCode,
                gateway?.CallsStarted ?? 0,
                gateway?.CallsCompleted ?? 0,
                gateway?.LastOutcome ?? "NotResolved",
                SafeProviderStatus(gateway?.VerifiedProviderStatus),
                string.Join(",", _workerFailures.ToArray()));
        }

        /// <summary>對本案例唯一 Worker 設定有界停止期限；只在成功停止後發布 drained 狀態。</summary>
        private async Task StopWorkerAsync()
        {
            if (_host is null || !_workerStarted || _workerStopped)
                return;

            using var stopDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await _host.StopAsync(stopDeadline.Token).ConfigureAwait(false);
            _workerStopped = true;
        }

        /// <summary>
        /// 120 秒上限屆滿時只讀取本次 task 的固定狀態與白名單 new_lasterror，並附上 gateway 進出計數；
        /// 不輸出 task ID、payload、provider 本文、例外本文、設定值或憑證。
        /// </summary>
        internal void WriteSandboxTimeoutDiagnostics(Guid taskId)
        {
            try
            {
                var task = ReadTaskSnapshot(taskId);
                var gateway = _recordingGateway;
                _output.WriteLine(
                    "Sandbox timeout diagnostic: taskStatus={0}; attempts={1}; new_lasterror={2}; gatewayStarted={3}; gatewayCompleted={4}; gatewayOutcome={5}; verifiedProviderStatus={6}; workerFailures={7}",
                    SafeTaskStatus(task.Status),
                    task.Attempts,
                    task.ErrorCode,
                    gateway?.CallsStarted ?? 0,
                    gateway?.CallsCompleted ?? 0,
                    gateway?.LastOutcome ?? "NotResolved",
                    SafeProviderStatus(gateway?.VerifiedProviderStatus),
                    string.Join(",", _workerFailures.ToArray()));
            }
            catch (Exception diagnosticException)
            {
                _output.WriteLine(
                    "Sandbox timeout diagnostic unavailable: {0}",
                    diagnosticException.GetType().Name);
            }
        }

        /// <summary>只輸出程式定義的待辦狀態名稱，未知 Dataverse 選項值以 Other 隱去。</summary>
        private static string SafeTaskStatus(int status) => status switch
        {
            (int)OutboundWorkStatus.Pending => "Pending",
            (int)OutboundWorkStatus.Processing => "Processing",
            (int)OutboundWorkStatus.Completed => "Completed",
            (int)OutboundWorkStatus.Failed => "Failed",
            _ => "Other"
        };

        /// <summary>限制狀態碼只接受短 ASCII 識別字；其他外部字串不寫入測試輸出。</summary>
        private static string SafeProviderStatus(string? status)
        {
            if (string.IsNullOrWhiteSpace(status))
                return "not-received";
            if (status.Length > 16 || status.Any(character =>
                    !char.IsAsciiLetterOrDigit(character) && character is not ('_' or '-')))
            {
                return "unrecognized";
            }

            return status;
        }

        /// <summary>
        /// 唯讀查詢本組織唯一 TWD 幣別的小數位數；查詢最多取兩列並在寫入測試資料前完成，
        /// 結果只保留於本 fixture，供合成金額選擇與操作紀錄使用。
        /// </summary>
        internal int ReadTwdCurrencyPrecision()
        {
            if (_twdCurrencyPrecision.HasValue)
                return _twdCurrencyPrecision.Value;

            var query = new QueryExpression("transactioncurrency")
            {
                ColumnSet = new ColumnSet("currencyprecision"),
                TopCount = 2
            };
            query.Criteria.AddCondition("isocurrencycode", ConditionOperator.Equal, "TWD");
            var currencies = _sdk!.RetrieveMultiple(query).Entities;
            if (currencies.Count != 1
                || !currencies[0].Attributes.TryGetValue("currencyprecision", out var rawPrecision)
                || rawPrecision is not int precision
                || precision < 0
                || precision > 4)
            {
                throw new InvalidOperationException("The TWD currency precision could not be read uniquely.");
            }

            _twdCurrencyPrecision = precision;
            return precision;
        }

        /// <summary>
        /// 只向已驗證的 goodshepherdback 讀取兩個已發布 Money 欄位的精度中繼資料；
        /// 不讀取或輸出業務資料、組態、憑證或個人資訊。
        /// </summary>
        internal AllotMoneyPrecision ReadAllotMoneyPrecision()
        {
            var allotAmount = ReadMoneyAttributePrecision("new_allot_amount");
            var allotFee = ReadMoneyAttributePrecision("new_allot_fee");
            WriteMoneyAttributePrecision("new_allot_amount", allotAmount);
            WriteMoneyAttributePrecision("new_allot_fee", allotFee);
            return new AllotMoneyPrecision(allotAmount, allotFee);
        }

        /// <summary>讀取單一固定 new_fee Money 欄位的已發布精度與精度來源。</summary>
        private MoneyFieldPrecision ReadMoneyAttributePrecision(string attributeName)
        {
            var response = (RetrieveAttributeResponse)_sdk!.Execute(new RetrieveAttributeRequest
            {
                EntityLogicalName = "new_fee",
                LogicalName = attributeName,
                RetrieveAsIfPublished = false
            });
            var metadata = response.AttributeMetadata as Microsoft.Xrm.Sdk.Metadata.MoneyAttributeMetadata
                ?? throw new InvalidOperationException("The D365 allot attribute is not published Money metadata.");
            if (!string.Equals(metadata.LogicalName, attributeName, StringComparison.Ordinal))
                throw new InvalidOperationException("The D365 Money attribute identity did not match the requested field.");
            return new MoneyFieldPrecision(metadata.Precision, metadata.PrecisionSource);
        }

        /// <summary>只記錄固定欄位名稱與已發布 precision metadata，不輸出 D365 業務值。</summary>
        private void WriteMoneyAttributePrecision(string attributeName, MoneyFieldPrecision precision)
        {
            _output.WriteLine(
                "Read-only D365 Money metadata: attribute={0}; precision={1}; precisionSource={2}",
                attributeName,
                precision.Precision?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null",
                precision.PrecisionSource?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null");
        }

        /// <summary>讀取 fee 的四個撥款欄位，轉成不保留 SDK Entity 的比較純值。</summary>
        internal FeeSnapshot ReadFeeSnapshot()
        {
            EnsureTestRowsExist();
            var fee = _sdk!.Retrieve(
                "new_fee",
                _feeId,
                new ColumnSet("new_allot_amount", "new_allot_fee", "new_allot_date", "new_allot_no"));
            var rawAllotDate = fee.GetAttributeValue<DateTime?>("new_allot_date");
            DateTime? churchAllotDate = rawAllotDate.HasValue
                ? ToChurchCalendarDate(rawAllotDate.Value)
                : null;
            return new FeeSnapshot(
                fee.GetAttributeValue<DataverseMoney>("new_allot_amount")?.Value,
                fee.GetAttributeValue<DataverseMoney>("new_allot_fee")?.Value,
                churchAllotDate,
                fee.GetAttributeValue<string>("new_allot_no"));
        }

        /// <summary>讀取指定 task 的狀態與 attempts，供重複入列前後的冪等性比對。</summary>
        internal TaskSnapshot ReadTaskSnapshot(Guid taskId)
        {
            if (taskId == Guid.Empty)
                throw new ArgumentException("A persisted task identity is required.", nameof(taskId));
            var task = _sdk!.Retrieve(
                "new_outboundtask",
                taskId,
                new ColumnSet("new_taskstatus", "new_attempts", "new_lasterror"));
            var errorCode = task.GetAttributeValue<string>("new_lasterror");
            var safeErrorCode = SafeDiagnosticErrorCode(errorCode);
            return new TaskSnapshot(
                task.GetAttributeValue<OptionSetValue>("new_taskstatus")?.Value ?? -1,
                task.GetAttributeValue<int>("new_attempts"),
                safeErrorCode);
        }

        /// <summary>讀取本次正式 fee 的伺服器修改時間，證明第二次入列未改動收費單。</summary>
        internal DateTime? ReadFeeModifiedOn()
        {
            EnsureTestRowsExist();
            var fee = _sdk!.Retrieve("new_fee", _feeId, new ColumnSet("modifiedon"));
            return fee.GetAttributeValue<DateTime?>("modifiedon");
        }

        /// <summary>
        /// 先停止 Worker，再依正式 ID 清理所有列，恢復原 profile，最後以精確 marker／key 查詢
        /// 確認三類測試資料殘留為零；清理故障不會導致廣域刪除或跳過後續清理。
        /// </summary>
        internal async Task CleanupAndVerifyZeroResidueAsync()
        {
            try
            {
                await CleanupAsync(finalAttempt: false).ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                _output.WriteLine("The first exact cleanup attempt failed; retrying the owned records once.");
                await CleanupAsync(finalAttempt: true).ConfigureAwait(false);
            }

            Assert.True(_cleanupCompleted, "P4-06 cleanup did not return all owned resources and test data to baseline.");
        }

        /// <summary>重複 Dispose 冪等；若測試在明確收尾前失敗，仍嘗試完成相同精確 ID 清理。</summary>
        public async ValueTask DisposeAsync() => await CleanupAsync(finalAttempt: true).ConfigureAwait(false);

        /// <summary>
        /// 只由已驗證設定建構本案例組態；錯誤只說明缺少的鍵名或固定拒絕原因，不含設定值。
        /// </summary>
        private void LoadAndValidateConfiguration()
        {
            _configuration = new ConfigurationBuilder()
                .AddUserSecrets(typeof(global::SpeechMessageProducts.LineWebhook.Program).Assembly,
                    optional: false,
                    reloadOnChange: false)
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ASPNETCORE_ENVIRONMENT"] = "Development",
                    ["DOTNET_ENVIRONMENT"] = "Development",
                    ["Payment:Outbound:Enabled"] = "true"
                })
                .Build();

            var crmUrl = RequireValue("CrmConnection:ServerUrl");
            _ = RequireValue("CrmConnection:Username");
            _ = RequireValue("CrmConnection:Password");
            if (!Uri.TryCreate(crmUrl, UriKind.Absolute, out var crmUri)
                || !string.Equals(crmUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || crmUri.Host.IndexOf("goodshepherdback", StringComparison.OrdinalIgnoreCase) < 0)
                throw new InvalidOperationException("The CRM endpoint is not the approved goodshepherdback HTTPS host.");

            var churchIdText = RequireValue("PaymentsLive:ChurchId");
            if (!Guid.TryParse(churchIdText, out _churchId) || _churchId == Guid.Empty)
                throw new InvalidOperationException("PaymentsLive:ChurchId must be a non-empty persisted identity.");

            _profileName = RequireValue("PaymentsLive:ProfileName");
            if (_profileName.Length > 64
                || _profileName.Any(character =>
                    !(character is >= 'A' and <= 'Z'
                        or >= 'a' and <= 'z'
                        or >= '0' and <= '9'
                        or '_' or '-' or '.')))
                throw new InvalidOperationException("PaymentsLive:ProfileName is outside the supported profile-name grammar.");

            var prefix = "Payment:Profiles:" + _profileName + ":";
            if (!string.Equals(_configuration[prefix + "Provider"], "Sinopac", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(_configuration[prefix + "Environment"], "Sandbox", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The selected payment profile is not the Sinopac Sandbox profile.");

            foreach (var credential in new[] { "ShopNo", "A1", "A2", "B1", "B2", "XKeyId" })
                _ = RequireValue(prefix + "Credentials:" + credential);

            var apiBaseUrl = RequireValue(prefix + "Endpoints:ApiBaseUrl");
            if (!Uri.TryCreate(apiBaseUrl, UriKind.Absolute, out var apiUri)
                || !string.Equals(apiUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || !apiUri.IsDefaultPort
                || !string.Equals(apiUri.Host, SandboxApiHost, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(
                    apiUri.AbsolutePath.TrimEnd('/'),
                    "/funBIZ-Sbx/QPay.WebAPI/api",
                    StringComparison.Ordinal))
                throw new InvalidOperationException("The payment endpoint is not the approved Sinopac Sandbox HTTPS host.");
        }

        /// <summary>
        /// 組裝真實 ToolUtility／Payments／Outbound DI 管線；指定替身時只取代撥款 gateway，
        /// Worker、D365 store、handler factory 與 scope 管理仍使用產品實作。
        /// </summary>
        private void BuildHost(Func<string, IPaymentAllotGateway>? gatewayFactory)
        {
            var configuration = _configuration!;
            var selectedGateway = gatewayFactory?.Invoke(_orderNo);
            _host = new HostBuilder()
                .ConfigureServices((_, services) =>
                {
                    services.AddSingleton<IConfiguration>(configuration);
                    services.AddLogging();
                    services.AddSingleton<IChurchClock, ChurchClock>();
                    services.AddSingleton<Action<Exception, string, CancellationToken>>(
                        _ => RecordWorkerFailure);
                    services.AddToolUtility(configuration, "GoodShepherd");
                    services.AddSpeechMessagePayments(configuration.GetSection("Payment"));
                    if (selectedGateway is not null)
                    {
                        services.RemoveAll<IPaymentAllotGateway>();
                        services.AddSingleton(selectedGateway);
                    }
                    else
                    {
                        var routedGatewayRegistration = services.LastOrDefault(
                            descriptor => descriptor.ServiceType == typeof(IPaymentAllotGateway));
                        if (routedGatewayRegistration?.ImplementationFactory is null)
                            throw new InvalidOperationException("The payment allot gateway registration is unavailable.");

                        services.RemoveAll<IPaymentAllotGateway>();
                        services.AddSingleton<IPaymentAllotGateway>(provider =>
                        {
                            _recordingGateway = new RecordingAllotGateway(
                                (IPaymentAllotGateway)routedGatewayRegistration.ImplementationFactory(provider));
                            return _recordingGateway;
                        });
                    }
                    services.AddSpeechMessagePaymentOutbound(configuration);
                })
                .Build();
        }

        /// <summary>
        /// 只在失敗時輸出有界且固定分類的診斷；絕不讀取例外本文、SDK Entity 或設定值。
        /// 合成案例額外比較 D365 讀回金額、手續費、編號與日期，便於確認失敗邊界。
        /// </summary>
        internal void WriteFailureDiagnostics(
            string responseSource,
            Guid taskId,
            int gatewayCallCount,
            DateOnly expectedDate,
            string expectedAllotNo,
            decimal expectedAllotAmount,
            decimal expectedAllotFee,
            int currencyPrecision)
        {
            try
            {
                var task = ReadTaskSnapshot(taskId);
                var fee = ReadFeeSnapshot();
                var actualDate = fee.AllotDate.HasValue
                    ? DateOnly.FromDateTime(fee.AllotDate.Value).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
                    : "missing";
                var amountMatches = fee.AllotAmount == expectedAllotAmount;
                var feeMatches = fee.AllotFee == expectedAllotFee;
                var allotNoMatches = string.Equals(fee.AllotNo, expectedAllotNo, StringComparison.Ordinal);
                var dateMatches = fee.AllotDate.HasValue
                    && DateOnly.FromDateTime(fee.AllotDate.Value) == expectedDate;
                _output.WriteLine(
                    "P4-06 safe diagnostics: source={0}; taskStatus={1}; attempts={2}; errorClass={3}; gatewayCalls={4}; workerFailures={5}; expected new_allot_amount={6}; actual new_allot_amount={7}; expected new_allot_fee={8}; actual new_allot_fee={9}; transactioncurrency.currencyprecision={10}; amountMatch={11}; feeMatch={12}; allotNoMatch={13}; dateMatch={14}; expectedDate={15}; actualDate={16}",
                    responseSource,
                    task.Status,
                    task.Attempts,
                    task.ErrorCode,
                    gatewayCallCount,
                    string.Join(",", _workerFailures.ToArray()),
                    expectedAllotAmount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    fee.AllotAmount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null",
                    expectedAllotFee.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    fee.AllotFee?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null",
                    currencyPrecision.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    amountMatches,
                    feeMatches,
                    allotNoMatches,
                    dateMatches,
                    expectedDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                    actualDate);
            }
            catch (Exception diagnosticException)
            {
                _output.WriteLine(
                    "P4-06 safe diagnostic collection failed: {0}",
                    diagnosticException.GetType().Name);
            }
        }

        /// <summary>
        /// 將 worker 最終失敗縮減為固定操作代號與 CLR 例外型別；保存量最多十六筆，
        /// 不呼叫 formatter、不讀取 Exception.Message、Data 或 StackTrace。
        /// </summary>
        private void RecordWorkerFailure(
            Exception exception,
            string operation,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _workerFailureCount) > 16)
                return;

            var safeOperation = operation switch
            {
                "OutboundTaskWorker.ReadCandidates" => operation,
                "OutboundTaskWorker.CandidateValidation" => operation,
                "OutboundTaskWorker.Claim" => operation,
                "OutboundTaskWorker.TerminalFailure" => operation,
                "OutboundTaskWorker.Settle" => operation,
                "OutboundTaskWorker.SettleCompleted" => operation,
                _ => "OutboundTaskWorker.Other"
            };
            _workerFailures.Enqueue(safeOperation + ":" + exception.GetType().Name);
        }

        /// <summary>由此 fixture 持有一個測試專屬 scope，Worker 使用獨立 scope，不共用 SDK 生命週期。</summary>
        private void OpenCrmScope()
        {
            _crmScope = _host!.Services.CreateScope();
            _sdk = _crmScope.ServiceProvider.GetRequiredService<IOrganizationService>();
        }

        /// <summary>
        /// 依 Dataverse UserLocal 語意將 SDK 回傳的 UTC 瞬間換成教會日期；
        /// Unspecified 只補 UTC Kind，Local Kind 拒絕以開發機時區推測。
        /// </summary>
        private DateTime ToChurchCalendarDate(DateTime value)
        {
            var utc = value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
                _ => throw new InvalidOperationException("The D365 fee date has an invalid time kind.")
            };
            return _host!.Services.GetRequiredService<IChurchClock>()
                .ToChurchTime(utc, _timeZoneId)
                .Date;
        }

        /// <summary>唯讀驗證 ChurchId 確實存在且 account timezone/profile 欄位型別可安全處理。</summary>
        private void ReadAndValidateChurchBeforeWrites()
        {
            var church = _sdk!.Retrieve(
                "account",
                _churchId,
                new ColumnSet("new_sinopacprofile", "new_timezone"));
            if (church.LogicalName != "account" || church.Id != _churchId)
                throw new InvalidOperationException("The configured church identity did not resolve to the requested account.");

            _originalProfileAttributePresent = church.Attributes.TryGetValue("new_sinopacprofile", out var profileValue);
            if (profileValue is not null && profileValue is not string)
                throw new InvalidOperationException("The church payment profile has an invalid data type.");
            _originalProfile = profileValue as string;

            if (!church.Attributes.TryGetValue("new_timezone", out var timeZoneValue) || timeZoneValue is null)
            {
                _timeZoneId = "Asia/Taipei";
                _timeZoneState = "空白，依既定 Asia/Taipei fallback";
            }
            else if (timeZoneValue is string timeZone)
            {
                if (string.IsNullOrWhiteSpace(timeZone))
                {
                    _timeZoneId = "Asia/Taipei";
                    _timeZoneState = "空白，依既定 Asia/Taipei fallback";
                }
                else
                {
                    _timeZoneId = timeZone;
                    _timeZoneState = "已有值";
                }
            }
            else
            {
                throw new InvalidOperationException("The church timezone has an invalid data type.");
            }

            _ = _host!.Services.GetRequiredService<IChurchClock>().TodayInChurch(_timeZoneId);
        }

        /// <summary>
        /// 先重讀同一 account；只有尚為本次 profile 時才以讀回 row version 還原，
        /// 避免覆蓋其他 writer 在測試期間所作的變更。
        /// </summary>
        private void SetChurchProfileForThisTest()
        {
            var current = _sdk!.Retrieve("account", _churchId, new ColumnSet("new_sinopacprofile"));
            var currentProfile = current.GetAttributeValue<string>("new_sinopacprofile");
            if (string.Equals(currentProfile, _profileName, StringComparison.Ordinal))
                return;

            if (string.IsNullOrWhiteSpace(current.RowVersion))
                throw new InvalidOperationException("The church profile row version is unavailable.");

            var update = new Entity("account", _churchId)
            {
                RowVersion = current.RowVersion,
                ["new_sinopacprofile"] = _profileName
            };
            _profileMutationAttempted = true;
            _sdk.Execute(new UpdateRequest
            {
                Target = update,
                ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches
            });
        }

        /// <summary>以每次建立回傳的正式 ID 建立並關聯 fee；所有名稱均有本次操作專屬隨機標記。</summary>
        private void EnsureTestRowsExist()
        {
            if (_churchId == Guid.Empty || _contactId == Guid.Empty || _feeId == Guid.Empty)
                throw new InvalidOperationException("The P4-06 test rows have not been created.");
        }

        /// <summary>檢查 task identity 前綴，然後由實際 writer 使用固定 payload 建立／取回待辦。</summary>
        private string RequireValue(string key)
        {
            var value = _configuration?[key];
            return string.IsNullOrWhiteSpace(value)
                ? throw new InvalidOperationException("Required User Secrets key is missing or empty: " + key + ".")
                : value;
        }

        /// <summary>停止唯一 worker 並確保資源清理每個階段都會執行；只保留固定操作名與例外型別。</summary>
        private async Task CleanupAsync(bool finalAttempt)
        {
            if (_cleanupFinalized)
                return;

            var failures = new List<string>();

            if (_host is not null && _workerStarted && !_workerStopped)
            {
                await TryCleanupAsync(
                    async () =>
                    {
                        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                        await _host.StopAsync(deadline.Token).ConfigureAwait(false);
                        _workerStopped = true;
                    },
                    "stop-worker",
                    failures).ConfigureAwait(false);
            }

            var workerDrained = !_workerStarted || _workerStopped;
            if (workerDrained)
            {
                if (_profileMutationAttempted)
                {
                    TryCleanup(
                        RestoreChurchProfileIfStillOwned,
                        "restore-church-profile",
                        failures);
                }

                foreach (var taskId in _taskIds.Distinct().ToArray())
                {
                    TryCleanup(
                        () => DeleteOwnedRowIfPresent("new_outboundtask", "new_outboundtaskid", taskId),
                        "delete-owned-task",
                        failures);
                }

                if (_feeId != Guid.Empty)
                {
                    TryCleanup(
                        () => DeleteOwnedRowIfPresent("new_fee", "new_feeid", _feeId),
                        "delete-owned-fee",
                        failures);
                }

                if (_contactId != Guid.Empty)
                {
                    TryCleanup(
                        () => DeleteOwnedRowIfPresent("contact", "contactid", _contactId),
                        "delete-owned-contact",
                        failures);
                }

                if (_sdk is not null)
                {
                    TryCleanup(VerifyNoOwnedRowsRemain, "verify-zero-residue", failures);
                }
            }
            else if (failures.Count == 0)
            {
                failures.Add("defer-crm-cleanup-worker-not-stopped");
            }

            if (failures.Count == 0 || finalAttempt)
            {
                DisposeOwnedResources(failures);
                _cleanupFinalized = finalAttempt || failures.Count == 0;
            }
            else if (workerDrained)
            {
                DisposeStoppedHost(failures);
            }

            _cleanupCompleted = failures.Count == 0;
            if (failures.Count != 0)
            {
                _output.WriteLine("Cleanup failures: {0}", string.Join(", ", failures));
                throw new InvalidOperationException("P4-06 cleanup did not return all owned resources to baseline.");
            }

            _output.WriteLine("Cleanup verified: zero test contacts, fees and outbound tasks; timezone state: {0}.", _timeZoneState);
        }

        /// <summary>
        /// 依資料列正式主鍵先確認是否仍存在，再刪除本 fixture 擁有的單一列；
        /// 此讀前檢查讓前一次刪除已在伺服器完成但回應中斷時，後續重試保持冪等。
        /// </summary>
        private void DeleteOwnedRowIfPresent(string entityName, string primaryIdAttribute, Guid id)
        {
            if (id == Guid.Empty)
                return;

            var query = new QueryExpression(entityName)
            {
                ColumnSet = new ColumnSet(false),
                TopCount = 1
            };
            query.Criteria.AddCondition(primaryIdAttribute, ConditionOperator.Equal, id);
            if (_sdk!.RetrieveMultiple(query).Entities.Count != 0)
                _sdk.Delete(entityName, id);
        }

        /// <summary>停止成功後釋放 Host；失敗時保留參考，讓最終清理仍可重試 Dispose。</summary>
        private void DisposeStoppedHost(List<string> failures)
        {
            if (_host is null)
                return;

            var countBeforeDispose = failures.Count;
            TryCleanup(_host.Dispose, "dispose-host", failures);
            if (failures.Count == countBeforeDispose)
                _host = null;
        }

        /// <summary>依 Host、CRM scope、設定根順序釋放本 fixture 唯一擁有的本機資源。</summary>
        private void DisposeOwnedResources(List<string> failures)
        {
            DisposeStoppedHost(failures);

            if (_crmScope is not null)
            {
                var countBeforeDispose = failures.Count;
                TryCleanup(_crmScope.Dispose, "dispose-crm-scope", failures);
                if (failures.Count == countBeforeDispose)
                {
                    _crmScope = null;
                    _sdk = null;
                }
            }

            if (_configuration is IDisposable disposableConfiguration)
            {
                var countBeforeDispose = failures.Count;
                TryCleanup(disposableConfiguration.Dispose, "dispose-configuration", failures);
                if (failures.Count == countBeforeDispose)
                    _configuration = null;
            }
        }

        /// <summary>僅當 profile 欄位仍指向本次測試設定時才還原，並使用讀回列版本避免丟失並行更新。</summary>
        private void RestoreChurchProfileIfStillOwned()
        {
            var current = _sdk!.Retrieve("account", _churchId, new ColumnSet("new_sinopacprofile"));
            var currentProfile = current.GetAttributeValue<string>("new_sinopacprofile");
            if (!string.Equals(currentProfile, _profileName, StringComparison.Ordinal))
            {
                if (string.Equals(currentProfile, _originalProfile, StringComparison.Ordinal)
                    && current.Attributes.Contains("new_sinopacprofile") == _originalProfileAttributePresent)
                    return;
                throw new InvalidOperationException("The church profile changed outside this test.");
            }

            if (string.IsNullOrWhiteSpace(current.RowVersion))
                throw new InvalidOperationException("The church profile row version is unavailable during cleanup.");

            var update = new Entity("account", _churchId) { RowVersion = current.RowVersion };
            update["new_sinopacprofile"] = _originalProfileAttributePresent ? _originalProfile : null;
            _sdk.Execute(new UpdateRequest
            {
                Target = update,
                ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches
            });
            _profileMutationAttempted = false;
        }

        /// <summary>以唯一測試名稱與穩定替代鍵唯讀盤點；查詢不包含廣域刪除或其他教會資料。</summary>
        private void VerifyNoOwnedRowsRemain()
        {
            var contacts = CountByExactValue("contact", "lastname", _contactName);
            var fees = CountByExactValue("new_fee", "new_name", _feeName);
            var tasks = _idempotencyKey.Length == 0
                ? 0
                : CountByExactValue("new_outboundtask", "new_idempotencykey", _idempotencyKey);
            if (contacts != 0 || fees != 0 || tasks != 0)
                throw new InvalidOperationException("P4-06 exact-marker residue count was non-zero.");
        }

        /// <summary>只讀取一個固定識別欄位的最多兩筆結果，避免不受界定的全表掃描。</summary>
        private int CountByExactValue(string entityName, string attributeName, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return 0;
            var query = new QueryExpression(entityName)
            {
                ColumnSet = new ColumnSet(false),
                TopCount = 2
            };
            query.Criteria.AddCondition(attributeName, ConditionOperator.Equal, value);
            return _sdk!.RetrieveMultiple(query).Entities.Count;
        }

        /// <summary>執行一個同步清理步驟並記錄固定操作名與例外型別，不輸出原始訊息或資料值。</summary>
        private static void TryCleanup(Action action, string operation, List<string> failures)
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failures.Add(operation + ":" + exception.GetType().Name);
            }
        }

        /// <summary>執行一個非同步清理步驟並在失敗時繼續後續資源釋放。</summary>
        private static async Task TryCleanupAsync(
            Func<Task> action,
            string operation,
            List<string> failures)
        {
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failures.Add(operation + ":" + exception.GetType().Name);
            }
        }

        /// <summary>唯讀 fee 比較值，不保留 SDK Entity 或任何其他教會資料。</summary>
        internal sealed record FeeSnapshot(decimal? AllotAmount, decimal? AllotFee, DateTime? AllotDate, string? AllotNo);

        /// <summary>冪等性比較所需的 task 狀態與 attempts 純值。</summary>
        internal sealed record TaskSnapshot(int Status, int Attempts, string ErrorCode);
    }

    /// <summary>已發布單一 Money 欄位的純精度快照，不保留 SDK metadata 物件或外部服務參照。</summary>
    /// <param name="Precision">欄位宣告的小數位數；null 代表服務未提供精度。</param>
    /// <param name="PrecisionSource">欄位宣告的精度來源代碼；null 時合成資料採保守整數值。</param>
    internal sealed record MoneyFieldPrecision(int? Precision, int? PrecisionSource);

    /// <summary>本次合成寫回所需的兩個 Money 欄位精度純值。</summary>
    /// <param name="AllotAmount">new_allot_amount 的已發布精度metadata。</param>
    /// <param name="AllotFee">new_allot_fee 的已發布精度metadata。</param>
    internal sealed record AllotMoneyPrecision(
        MoneyFieldPrecision AllotAmount,
        MoneyFieldPrecision AllotFee);

    /// <summary>讓真實 gateway 的驗簽後狀態碼只保留在本案例記錄中，不攔截或改寫任何請求。</summary>
    private sealed class RecordingAllotGateway(IPaymentAllotGateway inner) : IPaymentAllotGateway
    {
        private string? _verifiedProviderStatus;
        private string? _lastOutcome;
        private int _callsStarted;
        private int _callsCompleted;

        /// <summary>取得最後一筆已完成 provider 呼叫的狀態碼；錯誤回應的狀態仍由原 gateway 分類。</summary>
        internal string? VerifiedProviderStatus => Volatile.Read(ref _verifiedProviderStatus);

        /// <summary>以原子讀取回報進入 provider gateway 的呼叫數，不暴露請求欄位。</summary>
        internal int CallsStarted => Volatile.Read(ref _callsStarted);

        /// <summary>以原子讀取回報已返回或已失敗的 gateway 呼叫數。</summary>
        internal int CallsCompleted => Volatile.Read(ref _callsCompleted);

        /// <summary>只保留 InFlight、Success、固定 provider 錯誤分類或 CLR 例外型別。</summary>
        internal string LastOutcome => Volatile.Read(ref _lastOutcome) ?? "NotStarted";

        /// <summary>原樣委派查詢，僅記錄固定進出階段、標準化錯誤分類與已驗簽狀態碼。</summary>
        public async Task<PaymentAllotQueryResult> QueryAllotAsync(
            PaymentAllotQueryRequest request,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _callsStarted);
            Volatile.Write(ref _lastOutcome, "InFlight");
            try
            {
                var result = await inner.QueryAllotAsync(request, cancellationToken).ConfigureAwait(false);
                Volatile.Write(ref _verifiedProviderStatus, result.ProviderStatus);
                Volatile.Write(
                    ref _lastOutcome,
                    result.Error is { HasError: true } error
                        ? SafeDiagnosticErrorCode(error.Code)
                        : "Success");
                return result;
            }
            catch (Exception exception)
            {
                Volatile.Write(ref _lastOutcome, exception.GetType().Name);
                throw;
            }
            finally
            {
                Interlocked.Increment(ref _callsCompleted);
            }
        }
    }

    /// <summary>
    /// 離線診斷測試專用 gateway；回傳由測試 owner 控制完成的 Task，不建立網路或保留資料列。
    /// </summary>
    private sealed class DeferredAllotGateway(Task<PaymentAllotQueryResult> result) : IPaymentAllotGateway
    {
        private readonly Task<PaymentAllotQueryResult> _result = result
            ?? throw new ArgumentNullException(nameof(result));

        /// <summary>傳回受控合成結果；若呼叫端已取消則在轉交前拒絕執行。</summary>
        public Task<PaymentAllotQueryResult> QueryAllotAsync(
            PaymentAllotQueryRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _result;
        }
    }

    /// <summary>供 P4-06 案例使用的 operation-local 撥款回應替身，不保存 CRM 或 profile 狀態。</summary>
    private sealed class SyntheticAllotGateway(Func<PaymentAllotQueryResult> resultFactory) : IPaymentAllotGateway
    {
        private int _callCount;

        /// <summary>以原子讀取提供此合成案例的呼叫數，不保存任何教會或設定狀態。</summary>
        internal int CallCount => Volatile.Read(ref _callCount);

        /// <summary>每次呼叫以合成值建立 provider-neutral 結果；不建立 HTTP client 或連接永豐。</summary>
        public Task<PaymentAllotQueryResult> QueryAllotAsync(
            PaymentAllotQueryRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _callCount);
            return Task.FromResult(resultFactory());
        }
    }

    /// <summary>計數型合成 gateway；用於證明相同正式待辦重送後只執行一次處理器。</summary>
    private sealed class CountingEmptyAllotGateway : IPaymentAllotGateway
    {
        private int _callCount;

        /// <summary>以原子讀取提供 worker 完成後的查詢次數。</summary>
        internal int CallCount => Volatile.Read(ref _callCount);

        /// <summary>每次被真正處理器呼叫時遞增一次，並回傳無資料的合成成功結果。</summary>
        public Task<PaymentAllotQueryResult> QueryAllotAsync(
            PaymentAllotQueryRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _callCount);
            return Task.FromResult(new PaymentAllotQueryResult
            {
                ProviderStatus = "SYNTHETIC",
                Batches = Array.Empty<PaymentAllotBatch>()
            });
        }
    }
}
