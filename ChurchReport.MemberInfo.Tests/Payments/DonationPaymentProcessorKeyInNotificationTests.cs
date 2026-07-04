using System.Reflection;
using System.Runtime.CompilerServices;
using ChurchReport.Models;
using ChurchReport.Payments;
using ChurchReport.WebServiceConnector;
using FluentAssertions;
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

        warning.Should().BeEmpty("備援 LINE ID 成功送出後，頁面不應再顯示 LINE 未送出的警告");
        processor.AttemptedLineIds.Should().Equal("UstalePrimary", "UbackupValid");
        processor.LastDeliveredMessage.Should().Be("ATM payment instructions");
        processor.LastDeliveredRetryKey.Should().Be("d2da3967-e0fc-4f01-9efa-414d221e1e11");
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

        public List<string> AttemptedLineIds => _attemptedLineIds ??= new List<string>();

        public string? LastDeliveredMessage { get; private set; }

        public string? LastDeliveredRetryKey { get; private set; }

        protected override Task SendAtmPaymentInstructionsAsync(string lineId, string lineMessage, string retryKey)
        {
            AttemptedLineIds.Add(lineId);

            if (lineId == LineIdToReject)
            {
                throw new InvalidOperationException("Simulated LINE provider rejection for stale primary LINE id.");
            }

            LastDeliveredMessage = lineMessage;
            LastDeliveredRetryKey = retryKey;
            return Task.CompletedTask;
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
