// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport.MemberInfo.Tests/Payments/DonationPaymentProcessorKeyInNotificationTests.cs
// 所屬區塊：ChurchReport 會員、付款與 LINE 共用流程的測試專案，用來固定產品層行為與回歸案例。
// 檔案責任：此檔案屬於測試範圍，註解重點在說明測試意圖、固定的回歸條件，以及避免未來重構時誤改既有契約。
// 主要型別：class DonationPaymentProcessorKeyInNotificationTests、class AtmNotificationProbeProcessor、class ThrowingDonationPaymentCreateGatewayAdapter
// 主要成員：BuildDedicationNotificationLineRetryKey_returns_provider_safe_uuid、BuildAtmPaymentLineRetryKey_returns_provider_safe_uuid、BuildDedicationNotificationLineRetryKey_rejects_empty_fee_id、TrySendAtmPaymentInstructionsAsync_uses_backup_line_id_when_primary_line_id_fails、InvokeBuildDedicationNotificationLineRetryKey、InvokeBuildAtmPaymentLineRetryKey、InvokeTrySendAtmPaymentInstructionsAsync、SendAtmPaymentInstructionsAsync、CreateCardPaymentAsync、CreateLegacyOrderAsync
// 引用命名空間：System.Reflection、System.Runtime.CompilerServices、ChurchReport.Models、ChurchReport.Payments、ChurchReport.WebServiceConnector、FluentAssertions、SpeechMessage.Payments.Models、Xunit
// 閱讀路徑：閱讀此檔案時應先看測試名稱、Arrange/Act/Assert 結構與 mock/fake 設定，因為它們描述了被保護的產品規則與外部契約。
// 維護重點：測試註解應協助理解案例保護的規則，不應把斷言改成只配合目前實作的描述。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using ChurchReport.Models;
using ChurchReport.Payments;
using ChurchReport.WebServiceConnector;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using SpeechMessage.Payments.Models;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Payments;

/// <summary>
/// 保護「後台手動輸入奉獻」完成後的 LINE 通知規則。
///
/// 這批測試刻意只測不需要 CRM 連線的純邏輯：
/// - 手動輸入奉獻保存收費單後，會用 retry key 呼叫必達通知路徑。
/// - retry key 最後會進入 LINE HTTP header，所以不能混入中文奉獻類別、付款方式或其他非 ASCII 文字。
/// - ChurchReport 的奉獻語意留在產品層；共用 LINE 專案只接收已整理好的 user id、文字與 retry key。
/// </summary>
public sealed class DonationPaymentProcessorKeyInNotificationTests
{
    [Fact]
    public void BuildDedicationNotificationLineRetryKey_returns_provider_safe_uuid()
    {
        var feeId = Guid.Parse("8f992f5a-0a27-4f08-9d0b-420ce5f6b4c1");
        var model = new DonationPaymentFormModel
        {
            Amount = 8,
            Category = "十一奉獻",
            PayWay = "ATM轉帳/匯款"
        };

        var retryKey = InvokeBuildDedicationNotificationLineRetryKey(feeId, model);

        Guid.TryParseExact(retryKey, "D", out _).Should().BeTrue(
            "LINE X-Line-Retry-Key 應保持標準 UUID 字串，避免自訂冒號長字串被 provider 或 proxy 拒收");
        retryKey.All(c => c <= 0x7f).Should().BeTrue(
            "LINE retry key 會進入 HTTP header，應避免中文或其他非 ASCII 字元");
        retryKey.Should().NotContain(":");
        retryKey.Should().NotContain(feeId.ToString("N"));
        retryKey.Should().NotBe($"churchreport:keyin-dedication:{feeId:N}:{model.Amount}");
        retryKey.Should().NotContain(model.Category);
        retryKey.Should().NotContain(model.PayWay);
    }

    [Fact]
    public void BuildAtmPaymentLineRetryKey_returns_provider_safe_uuid()
    {
        var feeId = Guid.Parse("8f992f5a-0a27-4f08-9d0b-420ce5f6b4c1");

        var retryKey = InvokeBuildAtmPaymentLineRetryKey(
            feeId,
            providerOrderNo: "TSATM123456",
            atmPayNo: "85405640000357");

        Guid.TryParseExact(retryKey, "D", out _).Should().BeTrue(
            "ATM 付款資訊 LINE 通知也會送 X-Line-Retry-Key，必須使用 provider-safe UUID");
        retryKey.All(c => c <= 0x7f).Should().BeTrue();
        retryKey.Should().NotContain(":");
        retryKey.Should().NotContain(feeId.ToString("N"));
        retryKey.Should().NotContain("TSATM123456");
        retryKey.Should().NotContain("85405640000357");
    }

    [Fact]
    public void BuildDedicationNotificationLineRetryKey_rejects_empty_fee_id()
    {
        var model = new DonationPaymentFormModel
        {
            Amount = 8
        };

        var action = () => InvokeBuildDedicationNotificationLineRetryKey(Guid.Empty, model);

        action.Should().Throw<TargetInvocationException>()
            .Which.InnerException.Should().BeOfType<ArgumentException>()
            .Which.ParamName.Should().Be("feeId");
    }

    [Fact]
    public async Task TrySendAtmPaymentInstructionsAsync_uses_backup_line_id_when_primary_line_id_fails()
    {
        // ATM 虛擬帳號是奉獻者完成付款的必要資訊，不是可有可無的行銷通知。
        // CRM 歷史資料可能同時存在 new_lineid 與 new_lineid_backup：
        // - new_lineid 可能是舊的、已解除好友或已失效的 LINE user id。
        // - new_lineid_backup 則是 LINE 綁定流程搬移後留下的備援 user id。
        //
        // 因此當第一個 LINE ID 被 provider 拒收時，流程應嘗試下一個候選 ID；
        // 否則使用者雖然看得到網頁 ATM 資訊，卻不會收到原本應該推播的 LINE 付款資訊。
        var processor = (AtmNotificationProbeProcessor)RuntimeHelpers.GetUninitializedObject(
            typeof(AtmNotificationProbeProcessor));
        processor.LineIdToReject = "UstalePrimary";

        var warning = await InvokeTrySendAtmPaymentInstructionsAsync(
            processor,
            new[] { "UstalePrimary", "UbackupValid" },
            "ATM payment instructions",
            "d2da3967-e0fc-4f01-9efa-414d221e1e11",
            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));

        warning.Should().Contain("LINE 發送結果：成功發送", "備援 LINE ID 成功送出後，頁面應明確顯示 LINE 已成功發送");
        processor.AttemptedLineIds.Should().Equal("UstalePrimary", "UbackupValid");
        processor.LastDeliveredMessage.Should().Be("ATM payment instructions");
        processor.LastDeliveredRetryKey.Should().Be("d2da3967-e0fc-4f01-9efa-414d221e1e11");
    }

    [Fact]
    public async Task TrySendAtmPaymentInstructionsAsync_reports_failure_reason_when_all_line_ids_fail()
    {
        // 所有候選 LINE ID 都被 provider 拒收時，頁面必須顯示最後一次失敗原因；
        // 這能避免使用者只看到 ATM 帳號，卻不知道 LINE 推播其實沒有送達。
        var processor = (AtmNotificationProbeProcessor)RuntimeHelpers.GetUninitializedObject(
            typeof(AtmNotificationProbeProcessor));
        processor.LineIdToReject = "UstalePrimary";

        var warning = await InvokeTrySendAtmPaymentInstructionsAsync(
            processor,
            new[] { "UstalePrimary" },
            "ATM payment instructions",
            "d2da3967-e0fc-4f01-9efa-414d221e1e11",
            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));

        warning.Should().Contain("LINE 發送結果：發送失敗");
        warning.Should().Contain("失敗原因");
        warning.Should().Contain("Simulated LINE provider rejection");
    }

    [Fact]
    public async Task TrySendAtmPaymentInstructionsAsync_reports_unbound_line_id()
    {
        // 沒有任何 LINE ID 時不應嘗試送出，也不應靜默成功；
        // 畫面要提示奉獻者尚未綁定 LINE，並提醒保留網頁上的付款資訊。
        var processor = (AtmNotificationProbeProcessor)RuntimeHelpers.GetUninitializedObject(
            typeof(AtmNotificationProbeProcessor));

        var warning = await InvokeTrySendAtmPaymentInstructionsAsync(
            processor,
            Array.Empty<string>(),
            "ATM payment instructions",
            "d2da3967-e0fc-4f01-9efa-414d221e1e11",
            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));

        warning.Should().Contain("LINE 發送結果：發送失敗");
        warning.Should().Contain("奉獻者尚未綁定 LINE");
    }

    [Fact]
    public async Task TrySendAtmPaymentInstructionsAsync_returns_timeout_result_when_line_api_is_slow()
    {
        // ATM 虛擬帳號已經建立後，LINE API 慢或卡住時不能讓奉獻者長時間停在 Processing。
        // 此測試鎖住使用者體驗：付款資訊要先回畫面，LINE 狀態顯示逾時失敗即可。
        var processor = (AtmNotificationProbeProcessor)RuntimeHelpers.GetUninitializedObject(
            typeof(AtmNotificationProbeProcessor));
        processor.LineIdToDelay = "UslowLineApi";
        processor.SimulatedDelay = TimeSpan.FromSeconds(2);

        var stopwatch = Stopwatch.StartNew();
        var warning = await InvokeTrySendAtmPaymentInstructionsAsync(
            processor,
            new[] { "UslowLineApi" },
            "ATM payment instructions",
            "d2da3967-e0fc-4f01-9efa-414d221e1e11",
            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));
        stopwatch.Stop();

        warning.Should().Contain("LINE 發送結果：發送失敗");
        warning.Should().Contain("LINE API 逾時未回應");
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1),
            "LINE API 慢於顯示上限時，ATM 付款資訊應先回到畫面，不應等待完整 LINE 呼叫時間");
        processor.AttemptedLineIds.Should().Equal("UslowLineApi");
    }

    [Fact]
    public async Task SendDedicationNotificationAsync_returns_timeout_result_when_line_api_is_slow()
    {
        var processor = (DedicationNotificationProbeProcessor)RuntimeHelpers.GetUninitializedObject(
            typeof(DedicationNotificationProbeProcessor));
        processor.LineIdToDelay = "UslowLineApi";
        processor.SimulatedDelay = TimeSpan.FromSeconds(2);
        var contact = new Entity("contact")
        {
            Id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")
        };
        contact["new_lineid"] = "UslowLineApi";
        contact["fullname"] = "測試奉獻者";
        var formModel = new DonationPaymentFormModel
        {
            Amount = 500,
            Category = "十一奉獻",
            PayWay = "現金"
        };

        var stopwatch = Stopwatch.StartNew();
        var result = await InvokeSendDedicationNotificationAsync(
            processor,
            contact,
            formModel,
            Guid.Parse("8f992f5a-0a27-4f08-9d0b-420ce5f6b4c1"));
        stopwatch.Stop();

        result.Should().Contain("LINE 發送結果：發送失敗");
        result.Should().Contain("LINE API 逾時未回應");
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1),
            "手動輸入奉獻的畫面等待上限應與 ATM 一樣，不應等待完整 LINE 呼叫時間");
        processor.AttemptedLineIds.Should().Equal("UslowLineApi");
    }

    private static string InvokeBuildDedicationNotificationLineRetryKey(Guid feeId, DonationPaymentFormModel model)
    {
        var method = typeof(DonationPaymentProcessor).GetMethod(
            "BuildDedicationNotificationLineRetryKey",
            BindingFlags.Static | BindingFlags.NonPublic);

        method.Should().NotBeNull("手動輸入奉獻通知需要穩定 retry key，避免 LINE 重試時產生重複通知");

        return (string)method!.Invoke(null, new object[] { feeId, model })!;
    }

    private static string InvokeBuildAtmPaymentLineRetryKey(Guid feeId, string providerOrderNo, string atmPayNo)
    {
        var method = typeof(DonationPaymentProcessor).GetMethod(
            "BuildAtmPaymentLineRetryKey",
            BindingFlags.Static | BindingFlags.NonPublic);

        method.Should().NotBeNull("ATM 付款資訊通知需要穩定且 provider-safe 的 LINE retry key");

        return (string)method!.Invoke(null, new object[] { feeId, providerOrderNo, atmPayNo })!;
    }

    private static async Task<string> InvokeTrySendAtmPaymentInstructionsAsync(
        DonationPaymentProcessor processor,
        IReadOnlyList<string> lineIds,
        string lineMessage,
        string retryKey,
        Guid contactId)
    {
        var method = typeof(DonationPaymentProcessor).GetMethod(
            "TrySendAtmPaymentInstructionsAsync",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(IReadOnlyList<string>), typeof(string), typeof(string), typeof(Guid) },
            modifiers: null);

        method.Should().NotBeNull("ATM 必達付款資訊通知需要逐一嘗試主要與備援 LINE ID");

        var task = method!.Invoke(
            processor,
            new object[] { lineIds, lineMessage, retryKey, contactId }) as Task<string>;

        task.Should().NotBeNull();
        return await task!;
    }

    private static async Task<string> InvokeSendDedicationNotificationAsync(
        DonationPaymentProcessor processor,
        Entity contact,
        DonationPaymentFormModel model,
        Guid feeId)
    {
        var method = typeof(DonationPaymentProcessor).GetMethod(
            "SendDedicationNotificationAsync",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(Entity), typeof(DonationPaymentFormModel), typeof(Guid) },
            modifiers: null);

        method.Should().NotBeNull("手動輸入奉獻 LINE 通知需要有可驗證的畫面等待上限");

        var task = method!.Invoke(
            processor,
            new object[] { contact, model, feeId }) as Task<string>;

        task.Should().NotBeNull();
        return await task!;
    }

    private sealed class AtmNotificationProbeProcessor : DonationPaymentProcessor
    {
        private List<string>? _attemptedLineIds;

        // RuntimeHelpers.GetUninitializedObject 建立測試物件時不會執行這個建構子；
        // 但 C# 編譯器仍要求衍生類別明確指定可呼叫的 base constructor。
        // 這個 adapter 只為了讓測試替身能通過編譯，ATM 通知 fallback 測試不會碰到建單流程。
        private AtmNotificationProbeProcessor()
            : base(new ThrowingDonationPaymentCreateGatewayAdapter())
        {
        }

        public string? LineIdToReject { get; set; }

        public string? LineIdToDelay { get; set; }

        public TimeSpan SimulatedDelay { get; set; } = TimeSpan.FromSeconds(3);

        public List<string> AttemptedLineIds => _attemptedLineIds ??= new List<string>();

        public string? LastDeliveredMessage { get; private set; }

        public string? LastDeliveredRetryKey { get; private set; }

        protected override async Task SendAtmPaymentInstructionsAsync(string lineId, string lineMessage, string retryKey)
        {
            AttemptedLineIds.Add(lineId);

            if (lineId == LineIdToDelay)
            {
                await Task.Delay(SimulatedDelay);
            }

            if (lineId == LineIdToReject)
            {
                throw new InvalidOperationException("Simulated LINE provider rejection for stale primary LINE id.");
            }

            LastDeliveredMessage = lineMessage;
            LastDeliveredRetryKey = retryKey;
        }
    }

    private sealed class DedicationNotificationProbeProcessor : DonationPaymentProcessor
    {
        private List<string>? _attemptedLineIds;

        private DedicationNotificationProbeProcessor()
            : base(new ThrowingDonationPaymentCreateGatewayAdapter())
        {
        }

        public string? LineIdToDelay { get; set; }

        public TimeSpan SimulatedDelay { get; set; } = TimeSpan.FromSeconds(3);

        public List<string> AttemptedLineIds => _attemptedLineIds ??= new List<string>();

        protected override string ResolveDedicationNotificationLineId(Entity contact)
        {
            return LineIdToDelay ?? string.Empty;
        }

        protected override async Task SendDedicationNotificationLineAsync(string lineUserId, string message, string retryKey)
        {
            AttemptedLineIds.Add(lineUserId);

            if (lineUserId == LineIdToDelay)
            {
                await Task.Delay(SimulatedDelay);
            }
        }
    }

    private sealed class ThrowingDonationPaymentCreateGatewayAdapter : IDonationPaymentCreateGatewayAdapter
    {
        public Task<PaymentCreateResult> CreateCardPaymentAsync(
            DonationPaymentCreateInput input,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("ATM notification fallback test must not create a payment order.");
        }

        public Task<CreOrder> CreateLegacyOrderAsync(
            DonationPaymentCreateInput input,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("ATM notification fallback test must not create a payment order.");
        }
    }

}
