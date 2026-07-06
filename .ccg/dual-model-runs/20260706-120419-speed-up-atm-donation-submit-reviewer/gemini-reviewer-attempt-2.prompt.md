ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\reviewer.md
<TASK>
# CCG reviewer Task: speed-up-atm-donation-submit

## Repository
D:\網頁APP雲端線上版本\DevExpressDevExtreme-21.2.7版本\音訊產品版本\ChurchReport

## Request
# Review Task: speed-up-atm-donation-submit

User asked to speed up ATM/匯款 donation submission because Processing spinner waits too long.

Implemented change:
- Keep ATM virtual account creation and CRM fee update synchronous.
- Add AtmLineNotificationDisplayTimeout = TimeSpan.FromSeconds(2).
- In TrySendAtmPaymentInstructionsAsync, each LINE send attempt now waits at most 2 seconds before returning a visible LINE failure message: LINE API 逾時未回應，請保存本頁付款資訊。
- Original immediate success/failure behavior and fallback LINE ID behavior remain covered by tests.
- Added regression test proving a simulated 3-second LINE send returns before the full LINE delay.

Validation already run:
- Focused timeout test: passed 1/1.
- Full DonationPaymentProcessorKeyInNotificationTests: passed 7/7.
- Full test suite: passed 234/234.
- Isolated build: succeeded with 0 warnings / 0 errors.
- git diff --check on target files: clean.

Review focus:
- Correctness of bounded wait behavior.
- Whether any Critical issue exists around background completion / unobserved task / scoped service lifetime.
- Whether user-visible LINE result and ATM payment information behavior remain consistent.
- Whether tests sufficiently cover regression.

## Diff

```diff
diff --git a/ChurchReport.MemberInfo.Tests/Payments/DonationPaymentProcessorKeyInNotificationTests.cs b/ChurchReport.MemberInfo.Tests/Payments/DonationPaymentProcessorKeyInNotificationTests.cs index 91d4de14..5fb5a7dc 100644 --- a/ChurchReport.MemberInfo.Tests/Payments/DonationPaymentProcessorKeyInNotificationTests.cs +++ b/ChurchReport.MemberInfo.Tests/Payments/DonationPaymentProcessorKeyInNotificationTests.cs @@ -11,6 +11,7 @@  // 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。  // 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。  // ============================================================================ +using System.Diagnostics;  using System.Reflection;  using System.Runtime.CompilerServices;  using ChurchReport.Models; @@ -157,6 +158,32 @@ public sealed class DonationPaymentProcessorKeyInNotificationTests          warning.Should().Contain("奉獻者尚未綁定 LINE");      }   +    [Fact] +    public async Task TrySendAtmPaymentInstructionsAsync_returns_timeout_result_when_line_api_is_slow() +    { +        // ATM 虛擬帳號已經建立後，LINE API 慢或卡住時不能讓奉獻者長時間停在 Processing。 +        // 此測試鎖住使用者體驗：付款資訊要先回畫面，LINE 狀態顯示逾時失敗即可。 +        var processor = (AtmNotificationProbeProcessor)RuntimeHelpers.GetUninitializedObject( +            typeof(AtmNotificationProbeProcessor)); +        processor.LineIdToDelay = "UslowLineApi"; +        processor.SimulatedDelay = TimeSpan.FromSeconds(3); + +        var stopwatch = Stopwatch.StartNew(); +        var warning = await InvokeTrySendAtmPaymentInstructionsAsync( +            processor, +            new[] { "UslowLineApi" }, +            "ATM payment instructions", +            "d2da3967-e0fc-4f01-9efa-414d221e1e11", +            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")); +        stopwatch.Stop(); + +        warning.Should().Contain("LINE 發送結果：發送失敗"); +        warning.Should().Contain("LINE API 逾時未回應"); +        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3), +            "LINE API 慢於顯示上限時，ATM 付款資訊應先回到畫面，不應等待完整 LINE 呼叫時間"); +        processor.AttemptedLineIds.Should().Equal("UslowLineApi"); +    } +      private static string InvokeBuildDedicationNotificationLineRetryKey(Guid feeId, DonationPaymentFormModel model)      {          var method = typeof(DonationPaymentProcessor).GetMethod( @@ -217,16 +244,25 @@ public sealed class DonationPaymentProcessorKeyInNotificationTests            public string? LineIdToReject { get; set; }   +        public string? LineIdToDelay { get; set; } + +        public TimeSpan SimulatedDelay { get; set; } = TimeSpan.FromSeconds(3); +          public List<string> AttemptedLineIds => _attemptedLineIds ??= new List<string>();            public string? LastDeliveredMessage { get; private set; }            public string? LastDeliveredRetryKey { get; private set; }   -        protected override Task SendAtmPaymentInstructionsAsync(string lineId, string lineMessage, string retryKey) +        protected override async Task SendAtmPaymentInstructionsAsync(string lineId, string lineMessage, string retryKey)          {              AttemptedLineIds.Add(lineId);   +            if (lineId == LineIdToDelay) +            { +                await Task.Delay(SimulatedDelay); +            } +              if (lineId == LineIdToReject)              {                  throw new InvalidOperationException("Simulated LINE provider rejection for stale primary LINE id."); @@ -234,7 +270,6 @@ public sealed class DonationPaymentProcessorKeyInNotificationTests                LastDeliveredMessage = lineMessage;              LastDeliveredRetryKey = retryKey; -            return Task.CompletedTask;          }      }   diff --git a/ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.PaymentProcessing.cs b/ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.PaymentProcessing.cs index 28455b66..298043f6 100644 --- a/ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.PaymentProcessing.cs +++ b/ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.PaymentProcessing.cs @@ -35,6 +35,8 @@ namespace ChurchReport.WebServiceConnector      /// </summary>      public partial class DonationPaymentProcessor      { +        private static readonly TimeSpan AtmLineNotificationDisplayTimeout = TimeSpan.FromSeconds(2); +          #region ===== 信用卡付款 =====            /// <summary> @@ -349,7 +351,31 @@ namespace ChurchReport.WebServiceConnector                    try                  { -                    await SendAtmPaymentInstructionsAsync(lineId, lineMessage, retryKey); +                    var sendTask = SendAtmPaymentInstructionsAsync(lineId, lineMessage, retryKey); +                    var timeoutTask = Task.Delay(AtmLineNotificationDisplayTimeout); +                    var completedTask = await Task.WhenAny(sendTask, timeoutTask); +                    if (completedTask == timeoutTask) +                    { +                        System.Diagnostics.Trace.WriteLine( +                            $"[DonationPaymentProcessor] ATM LINE notification timed out before display response. ContactId={contactId}, AttemptIndex={index + 1}, TimeoutMs={AtmLineNotificationDisplayTimeout.TotalMilliseconds}"); +                        _ = sendTask.ContinueWith( +                            task => +                            { +                                if (task.IsFaulted) +                                { +                                    System.Diagnostics.Trace.WriteLine( +                                        $"[DonationPaymentProcessor] ATM LINE notification background completion failed. ContactId={contactId}, AttemptIndex={index + 1}, Error={task.Exception}"); +                                } +                            }, +                            TaskContinuationOptions.ExecuteSynchronously); + +                        return BuildLineNotificationDisplayResult( +                            "發送失敗", +                            "LINE API 逾時未回應，請保存本頁付款資訊。", +                            false); +                    } + +                    await sendTask;                        if (index > 0)                      {
```


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.
</TASK>
OUTPUT:
- Use Traditional Chinese when possible.
- Return concrete findings and decisions.
- For reviewer role, classify findings as Critical / Warning / Info.
- If the task cannot be completed, explain the exact blocker.