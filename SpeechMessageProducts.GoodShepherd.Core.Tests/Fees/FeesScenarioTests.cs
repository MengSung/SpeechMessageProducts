/*
 * 檔案整體說明
 * 目的與定位：P5-11 收費通知的雙目標 reading 規則驗證，讀取與真外殼相同的
 * scenarios／expected，不手造 legacy.json，不執行退役 DLL 或真網路。
 * 主要流程：六案依舊分派執行真 Core 與共用通知入口，完整比較通知摘要、數量、
 * 正式 retry ID 與錯誤型別；對正常及時區邊界另外逐字比對人工審閱的通知全文。
 * 資源與隔離：每案 using 獨占 SDK 與 sink，故障必須保留原實例、無真寫入；
 * owner 在同步 Task 排空後清除來源、回呼與摘要。無秘密、Session、Timer 或
 * 背景資源；這是離線規則證據，不代表舊程式實跑或實機影子差異零。
 */
using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Fees;
using SpeechMessageProducts.GoodShepherd.Core.Messaging;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd.Core.Tests.Fees;

/// <summary>兩個目標消費同一份人工 reading 期望，避免範本代號自比或目標差異被隱藏。</summary>
public sealed class FeesScenarioTests
{
    /// <summary>六個情境均完整比較推播欄位與原故障；文字不能由實際輸出重生。</summary>
    [Fact]
    public async Task Fees_Scenarios_MatchExpected()
    {
        var scenarios = (JArray)FeesGoldenFixture.Read("scenarios")["cases"]!;
        var expected = FeesGoldenFixture.Read("expected");
        Assert.Equal("reading", (string?)expected["basis"]);
        Assert.InRange(scenarios.Count, 3, 6);
        Assert.Equal(scenarios.Count, ((JArray)expected["cases"]!).Count);
        for (var index = 0; index < scenarios.Count; index++)
        {
            var scenario = scenarios[index];
            var contract = expected["cases"]![index]!;
            Assert.Equal((string?)scenario["name"], (string?)contract["name"]);
            using var sdk = new FeesGoldenFixture(scenario);
            var failure = await Record.ExceptionAsync(async () =>
            {
                if (sdk.Message == "ExecuteWorkflow")
                    await FeeNotificationService.RecordAsync(sdk, FeesGoldenFixture.Id("record"),
                        FeesGoldenFixture.Id("organization"), sdk, CancellationToken.None);
            });
            Assert.Equal((string?)contract["exception"], failure?.GetType().Name);
            if (sdk.ReadFailure is not null) Assert.Same(sdk.ReadFailure, failure);
            Assert.True(JToken.DeepEquals(contract["lineCalls"], sdk.Calls), "Fee reading intent mismatch.");
            Assert.Empty((JArray)contract["writes"]!);
            Assert.Equal(0, sdk.WriteAttempts);
            if (contract["outputs"]!["Text"]!.Type != JTokenType.Null)
            {
                var fields = scenario["seed"]![0]!["fields"]!;
                var catalog = new GoodShepherdTextCatalog(new NoOverrides());
                var context = new TextContext(FeesGoldenFixture.Id("church"), "zh-TW", "zh-TW",
                    "synthetic-fees", "GoodShepherd");
                var text = FeeNotificationRule.Compose(catalog, context, "合成會友",
                    DateTime.Parse((string)fields["new_pay_date"]!, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
                    (string)scenario["church"]!["timeZone"]!, (int)fields["new_category"]!["option"]!,
                    (int)fields["new_pay_way"]!["option"]!, (string)fields["new_others"]!,
                    (decimal)fields["new_fee_really_paid"]!["money"]!);
                Assert.Equal((string?)contract["outputs"]!["Text"], text);
            }
        }
    }

    /// <summary>只回傳當次文字 scope 的空覆寫，測試無需快取或外部設定。</summary>
    private sealed class NoOverrides : IChurchTextOverrideSnapshotProvider
    {
        /// <summary>不保存 context，也不將其他案例覆寫帶入。</summary>
        public IChurchTextOverrideSnapshot GetSnapshot(TextContext context) =>
            new ChurchTextOverrideSnapshot(context, Array.Empty<ChurchTextOverride>());
    }
}
