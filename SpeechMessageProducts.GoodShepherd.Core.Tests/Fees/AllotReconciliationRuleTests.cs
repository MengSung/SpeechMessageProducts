/*
 * 檔案整體說明
 *
 * 目的與定位：保護 GoodShepherd Core P4-03 的撥款純規則及收費單資料存取契約。
 * 測試只使用 P4-01 核准的合成 allot-legacy-cases.json、Money 純值及本檔建立的
 * IOrganizationService 替身；不連線 D365、永豐或其他外部服務，也不讀取任何秘密。
 *
 * 主要功能與流程：固定案例逐筆驗證付款類型、日期窗及四個回寫值；兩項經
 * 04A §4A 核准的差異以明確 case id 覆寫測試期望，原 golden JSON 保持不變。
 * 專用案例證明空白訂單號永不比對；合成 SDK 記錄 fee、contact、account 的精確
 * ID 與欄位投影，驗證 contact.parentcustomerid 必須屬於授權 church；更新測試
 * 斷言 IfRowVersionMatches、原始 row version、Money 欄位及撥款日期同日午夜。
 *
 * 使用邊界與維護：每個測試自行建立不可跨案例共用的合成列、查詢紀錄
 * 與替身；生產方法接收的 SDK 由呼叫端擁有，本測試不建立連線、快取、工作或計時器。
 * SDK 替身只允許指定的 fee/member/church 讀取或單次條件更新，意外 I/O 立即失敗；
 * 讀取到錯誤教會時必須在回傳資料前拒絕。所有 JSON 文件以 using 結束生命週期。
 * 這些離線斷言僅驗證程式契約，不代表真實 Dataverse 權限、metadata 或網路結果。
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessageProducts.GoodShepherd.Core.Fees;
using Xunit;
using DomainMoney = SpeechMessage.Localization.Money;

namespace SpeechMessageProducts.GoodShepherd.Core.Tests.Fees;

/// <summary>
/// 以合成 golden 與 SDK 替身驗證撥款規則、教會歸屬、列版本條件更新及純日期寫回。
/// </summary>
public sealed class AllotReconciliationRuleTests
{
    /// <summary>
    /// 逐筆套用 P4-01 固定案例，核對查詢類型、日期窗及四個撥款欄位；
    /// 對空白日期／空白訂單套用 04A §4A 差異；信用卡查詢類型則按 04A 明定的 C 前綴規則
    /// 計算，因 P4-01 合成 golden 的數筆 SYN-CARD 輸入不是 C 前綴但 queryType 寫成 C。
    /// golden 保持原貌，避免把測試期待反向灌回特性資料。
    /// </summary>
    [Fact]
    public void Reconcile_LegacyCases_SameFourFields()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FindLegacyCasesPath()));
        foreach (var testCase in document.RootElement.GetProperty("cases").EnumerateArray())
        {
            var id = testCase.GetProperty("id").GetString()!;
            var input = testCase.GetProperty("input");
            var expected = testCase.GetProperty("expected");
            var orderNumbers = input.GetProperty("orderNumbers");
            var fee = new FeeAllotSnapshot(
                Guid.NewGuid(),
                input.GetProperty("paymentWay").GetInt32(),
                ParseOptionalDate(input.GetProperty("feeDate")),
                orderNumbers.GetProperty("cardOrderNo").GetString() ?? string.Empty,
                orderNumbers.GetProperty("paidCardOrderNo").GetString() ?? string.Empty,
                orderNumbers.GetProperty("atmOrderNo").GetString() ?? string.Empty,
                orderNumbers.GetProperty("paidAtmOrderNo").GetString() ?? string.Empty);
            var churchToday = ParseDate(input.GetProperty("today"));
            var payType = AllotReconciliationRule.ResolvePayType(
                fee.PayWay,
                fee.CardOrderNo,
                fee.PaidCardOrderNo);
            var expectedQueryType = expected.GetProperty("queryType").ValueKind == JsonValueKind.Null
                ? null
                : expected.GetProperty("queryType").GetString();
            if (fee.PayWay == 100000001)
            {
                expectedQueryType = HasCreditPrefix(fee.CardOrderNo)
                    || HasCreditPrefix(fee.PaidCardOrderNo)
                    ? "C"
                    : "M";
            }

            Assert.Equal(
                expectedQueryType,
                payType);

            if (payType is null)
            {
                Assert.Equal(JsonValueKind.Null, expected.GetProperty("dateRange").ValueKind);
                Assert.Equal(JsonValueKind.Null, expected.GetProperty("writes").ValueKind);
                continue;
            }

            var window = AllotReconciliationRule.ResolveWindow(fee.PayDate, churchToday);
            var expectedRange = expected.GetProperty("dateRange");
            var expectedStart = expectedRange.GetProperty("start").GetString()!;
            var expectedEnd = expectedRange.GetProperty("end").GetString()!;

            // 已核准差異：空白收費日的查詢起日固定為 churchToday - 16 天；
            // P4-01 的 legacy placeholder 日期不是正式行為，原 JSON 不作修改。
            if (id == "blank-fee-date-legacy-range")
            {
                expectedStart = churchToday.Date.AddDays(-16)
                    .ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
            }

            Assert.Equal(expectedStart, window.FromDate.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal(expectedEnd, window.ToDate.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture));

            var batches = ReadBatches(input.GetProperty("response"));
            var update = AllotReconciliationRule.Match(fee, payType, batches);
            var expectedWrites = expected.GetProperty("writes");

            // 已核准差異：舊案例空白訂單號的空字串相等結果需覆寫為不更新；
            // 這只調整本測試的期望，不竄改或重新產生 P4-01 golden。
            if (id == "blank-order-legacy-equality-match")
            {
                Assert.Null(update);
                continue;
            }

            if (expectedWrites.ValueKind == JsonValueKind.Null)
            {
                Assert.Null(update);
                continue;
            }

            Assert.NotNull(update);
            Assert.Equal(expectedWrites.GetProperty("new_allot_amount").GetDecimal(), update!.AllotAmount.Amount);
            Assert.Equal("TWD", update.AllotAmount.Currency);
            Assert.Equal(expectedWrites.GetProperty("new_allot_fee").GetDecimal(), update.AllotFee.Amount);
            Assert.Equal("TWD", update.AllotFee.Currency);
            Assert.Equal(ParseDate(expectedWrites.GetProperty("new_allot_date")), update.AllotDate.Date);
            Assert.Equal(expectedWrites.GetProperty("new_allot_no").GetString(), update.AllotNo);
        }
    }

    /// <summary>信用卡依第一個以 C 開頭的舊訂單號選 C，否則選 M；ATM 與行動支付依固定代碼解析。</summary>
    [Fact]
    public void ResolvePayType_ByPayWayAndPrefix()
    {
        Assert.Equal("C", AllotReconciliationRule.ResolvePayType(100000001, "CARD-1", string.Empty));
        Assert.Equal("C", AllotReconciliationRule.ResolvePayType(100000001, "x", "CARD-2"));
        Assert.Equal("M", AllotReconciliationRule.ResolvePayType(100000001, string.Empty, string.Empty));
        Assert.Equal("A", AllotReconciliationRule.ResolvePayType(100000002, "CARD-1", string.Empty));
        Assert.Equal("M", AllotReconciliationRule.ResolvePayType(100000007, string.Empty, string.Empty));
        Assert.Null(AllotReconciliationRule.ResolvePayType(100000099, "CARD-1", string.Empty));
    }

    /// <summary>有收費日沿用前一天至後十五天並以教會今日截尾，缺日則從今日前十六天起查。</summary>
    [Fact]
    public void ResolveWindow_ByPayDateAndToday()
    {
        var today = new DateTime(2026, 3, 20);
        var dated = AllotReconciliationRule.ResolveWindow(new DateTime(2026, 3, 10, 18, 30, 0), today);
        Assert.Equal(new DateTime(2026, 3, 9), dated.FromDate);
        Assert.Equal(today, dated.ToDate);

        var futureEnd = AllotReconciliationRule.ResolveWindow(new DateTime(2026, 3, 1), today);
        Assert.Equal(new DateTime(2026, 2, 28), futureEnd.FromDate);
        Assert.Equal(new DateTime(2026, 3, 16), futureEnd.ToDate);

        var missingDate = AllotReconciliationRule.ResolveWindow(null, today);
        Assert.Equal(new DateTime(2026, 3, 4), missingDate.FromDate);
        Assert.Equal(today, missingDate.ToDate);
    }

    /// <summary>即使收費單和撥款明細的訂單號都是空白，也不得將空字串相等當成付款證據。</summary>
    [Fact]
    public void Match_BlankOrderNumbers_NeverMatch()
    {
        var fee = new FeeAllotSnapshot(
            Guid.NewGuid(), 100000001, new DateTime(2026, 3, 10),
            string.Empty, " ", string.Empty, string.Empty);
        var batches = new[]
        {
            new AllotBatch(
                "SYN-ALLOT-BLANK",
                new DateTime(2026, 3, 18),
                new[]
                {
                    new AllotDetail(string.Empty, new DomainMoney(123.45m, "TWD"), new DomainMoney(3.45m, "TWD")),
                    new AllotDetail(" ", new DomainMoney(123.45m, "TWD"), new DomainMoney(3.45m, "TWD"))
                })
        };

        Assert.Null(AllotReconciliationRule.Match(fee, "M", batches));
    }

    /// <summary>
    /// 以每案合成 fee、member 與 church 回應讀取資料；member 指向其他 account 時，
    /// Read 必須 fail closed，且不可把錯誤教會資料作為 FeeAllotRecord 發布。
    /// </summary>
    [Fact]
    public void Read_FeeMemberMustBelongToAuthorizedChurch()
    {
        var feeId = Guid.NewGuid();
        var churchId = Guid.NewGuid();
        var otherChurchId = Guid.NewGuid();
        var contactId = Guid.NewGuid();
        var sdk = new FeeRecordsSdk(feeId, contactId, otherChurchId, churchId);

        Assert.Throws<InvalidOperationException>(() =>
            FeeAllotRecords.Read(sdk, feeId, churchId, CancellationToken.None));
        Assert.Equal(new[] { ("new_fee", feeId), ("contact", contactId) }, sdk.Reads.Select(read => (read.EntityName, read.Id)));
        Assert.Equal(0, sdk.ExecuteCount);
    }

    /// <summary>
    /// 驗證更新只送四個核定欄位，列版本條件為 IfRowVersionMatches，
    /// 並將撥款日維持同一曆日午夜，不依本機或 UTC 時區換算。
    /// </summary>
    [Fact]
    public void Apply_UsesRowVersionAndCalendarMidnight()
    {
        var feeId = Guid.NewGuid();
        var churchId = Guid.NewGuid();
        var contactId = Guid.NewGuid();
        var sdk = new FeeRecordsSdk(feeId, contactId, churchId, churchId);
        var record = FeeAllotRecords.Read(sdk, feeId, churchId, CancellationToken.None);
        var update = new FeeAllotUpdate(
            new DomainMoney(1230m, "TWD"),
            new DomainMoney(4.56m, "TWD"),
            new DateTime(2026, 3, 18, 23, 45, 0, DateTimeKind.Utc),
            "SYN-ALLOT-001");

        FeeAllotRecords.Apply(sdk, record, update, CancellationToken.None);

        var request = Assert.IsType<UpdateRequest>(sdk.UpdateRequest);
        Assert.Equal(ConcurrencyBehavior.IfRowVersionMatches, request.ConcurrencyBehavior);
        Assert.Equal(feeId, request.Target.Id);
        Assert.Equal("synthetic-row-version-17", request.Target.RowVersion);
        Assert.Equal(1, sdk.ExecuteCount);
        Assert.Equal(4, request.Target.Attributes.Count);
        Assert.Equal(1230m, request.Target.GetAttributeValue<Microsoft.Xrm.Sdk.Money>("new_allot_amount").Value);
        Assert.Equal(4.56m, request.Target.GetAttributeValue<Microsoft.Xrm.Sdk.Money>("new_allot_fee").Value);
        Assert.Equal("SYN-ALLOT-001", request.Target.GetAttributeValue<string>("new_allot_no"));
        var writtenDate = request.Target.GetAttributeValue<DateTime>("new_allot_date");
        Assert.Equal(new DateTime(2026, 3, 18), writtenDate);
        Assert.Equal(DateTimeKind.Unspecified, writtenDate.Kind);
    }

    /// <summary>
    /// P4-03 的正式命名案例：撥款日期含 UTC Kind 與非午夜時間時，
    /// 寫入仍採 provider 所給的曆日並正規化為 Dataverse 日期欄位的午夜。
    /// </summary>
    [Fact]
    public void Update_AllotDate_IsSameCalendarDay()
    {
        var feeId = Guid.NewGuid();
        var churchId = Guid.NewGuid();
        var contactId = Guid.NewGuid();
        var sdk = new FeeRecordsSdk(feeId, contactId, churchId, churchId);
        var record = FeeAllotRecords.Read(sdk, feeId, churchId, CancellationToken.None);
        var providerDate = new DateTime(2026, 12, 31, 23, 45, 0, DateTimeKind.Utc);
        var update = new FeeAllotUpdate(
            new DomainMoney(1m, "TWD"),
            new DomainMoney(0m, "TWD"),
            providerDate,
            "SYN-ALLOT-DATE");

        FeeAllotRecords.Apply(sdk, record, update, CancellationToken.None);

        var request = Assert.IsType<UpdateRequest>(sdk.UpdateRequest);
        var writtenDate = request.Target.GetAttributeValue<DateTime>("new_allot_date");
        Assert.Equal(2026, writtenDate.Year);
        Assert.Equal(12, writtenDate.Month);
        Assert.Equal(31, writtenDate.Day);
        Assert.Equal(TimeSpan.Zero, writtenDate.TimeOfDay);
        Assert.Equal(DateTimeKind.Unspecified, writtenDate.Kind);
        Assert.Equal(1, sdk.ExecuteCount);
    }

    /// <summary>從合成 fixture 建立新的批次與明細純值；不共享 JSON 節點或可變集合。</summary>
    private static List<AllotBatch> ReadBatches(JsonElement response)
    {
        var allot = response.GetProperty("allot");
        var batches = new List<AllotBatch>();
        foreach (var batch in allot.EnumerateArray())
        {
            var details = new List<AllotDetail>();
            foreach (var detail in batch.GetProperty("details").EnumerateArray())
            {
                details.Add(new AllotDetail(
                    detail.GetProperty("orderNo").GetString() ?? string.Empty,
                    new DomainMoney(detail.GetProperty("amount").GetDecimal() / 100m, "TWD"),
                    new DomainMoney(detail.GetProperty("fee").GetDecimal() / 100m, "TWD")));
            }

            batches.Add(new AllotBatch(
                batch.GetProperty("allotNo").GetString()!,
                ParseDate(batch.GetProperty("allotDate")),
                details));
        }

        return batches;
    }

    /// <summary>將 ISO 日期或空白 fixture 欄位解析成不帶時區轉換的日期部分。</summary>
    private static DateTime? ParseOptionalDate(JsonElement value) =>
        string.IsNullOrEmpty(value.GetString()) ? null : ParseDate(value);

    /// <summary>解析固定 yyyy-MM-dd 測試日期；Kind 為 Unspecified，表示不是 UTC 瞬間。</summary>
    private static DateTime ParseDate(JsonElement value) =>
        DateTime.ParseExact(value.GetString()!, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>使用各目標可用且 ordinal 的方式判定 C 前綴，不依賴文化或平台字串比較。</summary>
    private static bool HasCreditPrefix(string value)
    {
#if NET10_0_OR_GREATER
        return value.StartsWith('C');
#else
        return value.Length > 0 && value[0] == 'C';
#endif
    }

    /// <summary>沿輸出目錄祖先尋找唯讀 golden；找不到時以固定路徑錯誤失敗而不猜其他 fixture。</summary>
    private static string FindLegacyCasesPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "SpeechMessage.Payments.Tests",
                "Golden",
                "Sinopac",
                "allot-legacy-cases.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("The approved synthetic allot fixture was not found.");
    }

    /// <summary>
    /// 僅允許本測試指定的收費單、會友及教會讀取與條件更新；其餘 SDK 呼叫均代表測試違約。
    /// </summary>
    private sealed class FeeRecordsSdk : IOrganizationService
    {
        private readonly Guid? feeId;
        private readonly Guid? contactId;
        private readonly Guid? memberChurchId;
        private readonly Guid? requestedChurchId;

        /// <summary>建立純 Apply 案例的替身，或建立三段讀取流程所需的合成身份。</summary>
        public FeeRecordsSdk(
            Guid? feeId = null,
            Guid? contactId = null,
            Guid? memberChurchId = null,
            Guid? requestedChurchId = null)
        {
            this.feeId = feeId;
            this.contactId = contactId;
            this.memberChurchId = memberChurchId;
            this.requestedChurchId = requestedChurchId;
        }

        /// <summary>保存每次讀取的實體與 ID，不保存回應欄位或個人資料。</summary>
        public List<(string EntityName, Guid Id)> Reads { get; } = new();

        /// <summary>Apply 時接收的單一合成條件更新；沒有更新時維持 null。</summary>
        public UpdateRequest? UpdateRequest { get; private set; }

        /// <summary>記錄 SDK Execute 次數，供跨教會失敗案例證明零寫入。</summary>
        public int ExecuteCount { get; private set; }

        /// <summary>依正式 ID 回傳本案例合成資料，且嚴格驗證各階段欄位投影。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
        {
            Reads.Add((entityName, id));
            if (entityName == "new_fee" && id == feeId)
            {
                if (UpdateRequest is not null)
                {
                    AssertColumns(columnSet, "new_allot_date");
                    return new Entity("new_fee", id)
                    {
                        ["new_allot_date"] = UpdateRequest.Target.GetAttributeValue<DateTime>("new_allot_date")
                    };
                }

                AssertColumns(columnSet,
                    "new_pay_way", "new_pay_date", "new_contact_new_fee",
                    "new_q_pay_card_order_no", "new_q_paid_card_order_no",
                    "new_q_pay_order_atm_no", "new_q_paid_order_atm_no", "versionnumber");
                var fee = new Entity("new_fee", id) { RowVersion = "synthetic-row-version-17" };
                fee["new_pay_way"] = new OptionSetValue(100000001);
                fee["new_pay_date"] = new DateTime(2026, 3, 10);
                fee["new_contact_new_fee"] = new EntityReference("contact", contactId!.Value);
                fee["new_q_pay_card_order_no"] = "SYN-CARD-001";
                fee["new_q_paid_card_order_no"] = string.Empty;
                fee["new_q_pay_order_atm_no"] = string.Empty;
                fee["new_q_paid_order_atm_no"] = string.Empty;
                return fee;
            }

            if (entityName == "contact" && id == contactId)
            {
                AssertColumns(columnSet, "parentcustomerid");
                return new Entity("contact", id)
                {
                    ["parentcustomerid"] = new EntityReference("account", memberChurchId!.Value)
                };
            }

            if (entityName == "account" && id == requestedChurchId)
            {
                AssertColumns(columnSet, "new_timezone");
                return new Entity("account", id) { ["new_timezone"] = "Asia/Taipei" };
            }

            throw new InvalidOperationException("Unexpected synthetic retrieve identity.");
        }

        /// <summary>Apply 唯一允許的 SDK 寫入，完整保留正式 UpdateRequest 供斷言。</summary>
        public OrganizationResponse Execute(OrganizationRequest request)
        {
            UpdateRequest = Assert.IsType<UpdateRequest>(request);
            ExecuteCount++;
            return new UpdateResponse();
        }

        /// <summary>建立不屬本測試契約。</summary>
        public Guid Create(Entity entity) => throw new InvalidOperationException("Unexpected create.");

        /// <summary>直接更新不屬本測試契約；必須以 Execute 傳送列版本條件。</summary>
        public void Update(Entity entity) => throw new InvalidOperationException("Unexpected update.");

        /// <summary>刪除不屬本測試契約。</summary>
        public void Delete(string entityName, Guid id) => throw new InvalidOperationException("Unexpected delete.");

        /// <summary>建立關聯不屬本測試契約。</summary>
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) =>
            throw new InvalidOperationException("Unexpected associate.");

        /// <summary>解除關聯不屬本測試契約。</summary>
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) =>
            throw new InvalidOperationException("Unexpected disassociate.");

        /// <summary>多筆查詢不屬本測試契約；費用讀取必須依正式 ID Retrieve。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query) => throw new InvalidOperationException("Unexpected query.");

        /// <summary>欄位投影必須精確符合資料流程，不容許意外擴大讀取範圍。</summary>
        private static void AssertColumns(ColumnSet columnSet, params string[] expected)
        {
            Assert.Equal(expected, columnSet.Columns.ToArray());
            Assert.False(columnSet.AllColumns);
        }
    }
}
