using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Dynamics.WorkerHost;
using SpeechMessage.Dynamics.WorkerProtocol;
using Xunit;

namespace SpeechMessage.Dynamics.Crm82Worker.Tests;

/// <summary>
/// 固定 CE 8.2 Package01 fee QueryExpression、分頁上限與 SDK-free row projection。
/// 每個測試只建立單一同步 fake client；查詢不會平行執行，也不會把 mutable QueryExpression、
/// Entity、Money、OptionSetValue 或 FormattedValue 保存到 Worker 邊界之外。
/// </summary>
public sealed class Package01FeeQueryOperationTests
{
    internal const string ContactNameSentinel = "ignored-contact-name";

    private static readonly Guid ContactId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset StartDate =
        new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset EndDate =
        new DateTimeOffset(2026, 1, 31, 23, 59, 59, TimeSpan.Zero);

    /// <summary>
    /// 證明 entity、ColumnSet、filter、status、UTC date、雙重排序與初始 paging 完全固定；
    /// optional contactName 雖存在於相容 request，仍不會進入 query value 或改變 routing。
    /// </summary>
    [Fact]
    public void Builds_the_exact_server_owned_query_and_discards_contact_name()
    {
        var request = CreateRequest(includeContactName: true);
        Assert.True(request.Parameters.TryGetValue("contactName", out var contactName));
        Assert.Equal(ContactNameSentinel, contactName!.Scalar);

        var client = new FakeCrm82SdkClient();
        client.RetrieveMultipleHandler = query =>
        {
            Assert.Equal("new_fee", query.EntityName);
            Assert.Equal(
                new[]
                {
                    "new_feeid",
                    "new_name",
                    "createdon",
                    "new_pay_date",
                    "new_fee_really_paid",
                    "new_pay_way",
                    "new_category",
                    "new_others",
                    "new_paid_period"
                },
                query.ColumnSet.Columns.ToArray());
            Assert.Equal(LogicalOperator.And, query.Criteria.FilterOperator);
            Assert.Empty(query.Criteria.Filters);
            Assert.Equal(5, query.Criteria.Conditions.Count);
            AssertCondition(
                query.Criteria.Conditions[0],
                "new_contact_new_fee",
                ConditionOperator.Equal,
                ContactId);
            AssertCondition(
                query.Criteria.Conditions[1],
                "new_category",
                ConditionOperator.NotNull);
            AssertCondition(
                query.Criteria.Conditions[2],
                "new_pay_status",
                ConditionOperator.In,
                100000001,
                100000002,
                100000003,
                100000004,
                100000006);
            AssertCondition(
                query.Criteria.Conditions[3],
                "new_pay_date",
                ConditionOperator.OnOrAfter,
                StartDate.UtcDateTime);
            AssertCondition(
                query.Criteria.Conditions[4],
                "new_pay_date",
                ConditionOperator.OnOrBefore,
                EndDate.UtcDateTime);
            Assert.Equal(DateTimeKind.Utc, Assert.IsType<DateTime>(
                query.Criteria.Conditions[3].Values.Single()).Kind);
            Assert.Equal(DateTimeKind.Utc, Assert.IsType<DateTime>(
                query.Criteria.Conditions[4].Values.Single()).Kind);
            Assert.Equal(2, query.Orders.Count);
            AssertOrder(query.Orders[0], "new_name", OrderType.Ascending);
            AssertOrder(query.Orders[1], "new_feeid", OrderType.Ascending);
            Assert.Equal(1, query.PageInfo.PageNumber);
            Assert.Equal(Package01FeeWorkerContract.MaximumRowsPerPage, query.PageInfo.Count);
            Assert.Null(query.PageInfo.PagingCookie);
            Assert.False(query.PageInfo.ReturnTotalRecordCount);
            Assert.DoesNotContain(
                ContactNameSentinel,
                query.Criteria.Conditions.SelectMany(condition => condition.Values)
                    .OfType<string>());
            Assert.Empty(query.LinkEntities);
            return CreatePage(Array.Empty<Entity>());
        };

        var result = Package01FeeQueryOperation.Execute(client, request);

        Assert.Equal(WorkerValueKind.Array, result.Kind);
        Assert.Single(result.Items!);
        Assert.Empty(result.Items![0].Items!);
        Assert.Equal(1, client.RetrieveMultipleCallCount);
    }

    /// <summary>
    /// 證明第二頁沿用固定 Count 與排序，只變更 PageNumber 並攜帶前一頁 cookie；
    /// page 呼叫保持同步順序，沒有 parallel paging 或共享 query cache。
    /// </summary>
    [Fact]
    public void Carries_the_cookie_and_page_number_across_two_pages_without_changing_order()
    {
        var client = new FakeCrm82SdkClient();
        var pageNumbers = new List<int>();
        var cookies = new List<string?>();
        var orderSignatures = new List<string>();
        client.RetrieveMultipleHandler = query =>
        {
            pageNumbers.Add(query.PageInfo.PageNumber);
            cookies.Add(query.PageInfo.PagingCookie);
            orderSignatures.Add(string.Join(
                "|",
                query.Orders.Select(order => order.AttributeName + ":" + order.OrderType)));

            return client.RetrieveMultipleCallCount == 1
                ? CreatePage(
                    new[] { CreateMinimalEntity(Guid.NewGuid()) },
                    moreRecords: true,
                    pagingCookie: "cookie-for-page-2")
                : CreatePage(new[] { CreateMinimalEntity(Guid.NewGuid()) });
        };

        var result = Package01FeeQueryOperation.Execute(client, CreateRequest());

        Assert.Equal(new[] { 1, 2 }, pageNumbers);
        Assert.Equal(new string?[] { null, "cookie-for-page-2" }, cookies);
        Assert.All(
            orderSignatures,
            signature => Assert.Equal(
                "new_name:Ascending|new_feeid:Ascending",
                signature));
        Assert.Equal(2, result.Items!.Count);
        Assert.All(result.Items, page => Assert.Single(page.Items!));
    }

    /// <summary>
    /// 證明第四頁仍宣告 MoreRecords 時立即以容量錯誤拒絕，且不發出第五次 SDK 呼叫；
    /// 已投影的前四頁只存在方法區域，例外路徑不會回傳 partial success。
    /// </summary>
    [Fact]
    public void Rejects_more_records_after_the_fourth_page_without_a_fifth_call()
    {
        var client = new FakeCrm82SdkClient
        {
            RetrieveMultipleHandler = _ => CreatePage(
                Array.Empty<Entity>(),
                moreRecords: true,
                pagingCookie: "next-cookie")
        };

        Assert.Throws<OfficialWorkerResultLimitExceededException>(
            () => Package01FeeQueryOperation.Execute(client, CreateRequest()));

        Assert.Equal(Package01FeeWorkerContract.MaximumPageCount, client.RetrieveMultipleCallCount);
    }

    /// <summary>
    /// 證明 MoreRecords 沒有非空 paging cookie 時 fail closed；Worker 不猜測 cookie、
    /// 不重送第一頁，也不以 page number-only 分頁造成重複或遺漏資料。
    /// </summary>
    /// <param name="pagingCookie">CRM page 回傳的缺失或空白 cookie。</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_more_records_without_a_next_paging_cookie(string? pagingCookie)
    {
        var client = new FakeCrm82SdkClient
        {
            RetrieveMultipleHandler = _ => CreatePage(
                Array.Empty<Entity>(),
                moreRecords: true,
                pagingCookie: pagingCookie)
        };

        Assert.Throws<InvalidOperationException>(
            () => Package01FeeQueryOperation.Execute(client, CreateRequest()));
        Assert.Equal(1, client.RetrieveMultipleCallCount);
    }

    /// <summary>
    /// 證明第一頁 canonical bytes 已超過 64 KiB 時，operation 會立即透過 WorkerHost 的
    /// result validator 映射成固定容量例外；即使 CRM 宣告 MoreRecords，也不得再取得第二頁，
    /// 以免把已知超限的 page 繼續累積成更大的 worker memory retention。
    /// </summary>
    [Fact]
    public void Rejects_an_oversized_page_before_requesting_the_next_page()
    {
        var largeName = new string('x', 16 * 1024);
        var entities = CreateEntities(5).ToArray();
        foreach (var entity in entities)
        {
            entity["new_name"] = largeName;
        }

        var client = new FakeCrm82SdkClient
        {
            RetrieveMultipleHandler = _ => CreatePage(
                entities,
                moreRecords: true,
                pagingCookie: "next-cookie")
        };

        var exception = Assert.Throws<OfficialWorkerResultLimitExceededException>(
            () => Package01FeeQueryOperation.Execute(client, CreateRequest()));

        Assert.Equal(
            OfficialWorkerResultLimitExceededException.FixedMessage,
            exception.Message);
        Assert.Equal(1, client.RetrieveMultipleCallCount);
    }

    /// <summary>
    /// 證明單頁 401 列整體失敗，不會截成 400 列，也不會保留 Entity 或 partial WorkerValue。
    /// </summary>
    [Fact]
    public void Rejects_more_than_four_hundred_rows_in_one_page()
    {
        var client = new FakeCrm82SdkClient
        {
            RetrieveMultipleHandler = _ => CreatePage(CreateEntities(401))
        };

        Assert.Throws<OfficialWorkerResultLimitExceededException>(
            () => Package01FeeQueryOperation.Execute(client, CreateRequest()));
        Assert.Equal(1, client.RetrieveMultipleCallCount);
    }

    /// <summary>
    /// 證明累計第 1,601 列在第四頁出現時以 total limit fail closed；
    /// 前 1,600 列不可被當成成功結果傳回，且不會要求第五頁。
    /// </summary>
    [Fact]
    public void Rejects_more_than_one_thousand_six_hundred_total_rows()
    {
        var client = new FakeCrm82SdkClient();
        client.RetrieveMultipleHandler = _ =>
        {
            var currentCall = client.RetrieveMultipleCallCount;
            return CreatePage(
                CreateEntities(currentCall == 4 ? 401 : 400),
                moreRecords: currentCall < 4,
                pagingCookie: currentCall < 4 ? "cookie-" + currentCall : null);
        };

        Assert.Throws<OfficialWorkerResultLimitExceededException>(
            () => Package01FeeQueryOperation.Execute(client, CreateRequest()));
        Assert.Equal(4, client.RetrieveMultipleCallCount);
    }

    /// <summary>
    /// 證明 Entity.Id、DateTime、Money、OptionSetValue、FormattedValues 與三個 string 欄位
    /// 依固定十欄順序投影；缺失值使用 Null，缺失 amount 則使用 0m，明確 Money(0) 仍保持 0m。
    /// </summary>
    [Fact]
    public void Projects_money_option_set_formatted_values_nulls_and_zero_amount()
    {
        var feeId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var createdOn = new DateTime(2026, 2, 3, 4, 5, 6, DateTimeKind.Utc);
        var payDate = new DateTime(2026, 2, 4, 7, 8, 9, DateTimeKind.Utc);
        var populated = new Entity("new_fee", feeId)
        {
            ["createdon"] = createdOn,
            ["new_pay_date"] = payDate,
            ["new_fee_really_paid"] = new Money(123.45m),
            ["new_pay_way"] = new OptionSetValue(100000002),
            ["new_category"] = new OptionSetValue(100000003),
            ["new_others"] = "備註",
            ["new_paid_period"] = "2026-02",
            ["new_name"] = "奉獻收費單"
        };
        populated.FormattedValues["new_pay_way"] = "ATM轉帳";
        populated.FormattedValues["new_category"] = "十一奉獻";
        var missing = new Entity("new_fee", Guid.Empty);
        var explicitZero = new Entity("new_fee", Guid.NewGuid())
        {
            ["new_fee_really_paid"] = new Money(0m)
        };
        var client = new FakeCrm82SdkClient
        {
            RetrieveMultipleHandler = _ => CreatePage(
                new[] { populated, missing, explicitZero })
        };

        var result = Package01FeeQueryOperation.Execute(client, CreateRequest());
        var rows = Assert.Single(result.Items!).Items!;
        Assert.Equal(3, rows.Count);

        var populatedCells = rows[0].Items!;
        AssertScalar(populatedCells[0], WorkerValueKind.Guid, feeId.ToString("N"));
        AssertScalar(
            populatedCells[1],
            WorkerValueKind.UtcDateTime,
            createdOn.Ticks.ToString(CultureInfo.InvariantCulture));
        AssertScalar(
            populatedCells[2],
            WorkerValueKind.UtcDateTime,
            payDate.Ticks.ToString(CultureInfo.InvariantCulture));
        AssertScalar(populatedCells[3], WorkerValueKind.Decimal, "123.45");
        AssertScalar(populatedCells[4], WorkerValueKind.Int64, "100000002");
        AssertScalar(populatedCells[5], WorkerValueKind.String, "ATM轉帳");
        AssertScalar(populatedCells[6], WorkerValueKind.String, "十一奉獻");
        AssertScalar(populatedCells[7], WorkerValueKind.String, "備註");
        AssertScalar(populatedCells[8], WorkerValueKind.String, "2026-02");
        AssertScalar(populatedCells[9], WorkerValueKind.String, "奉獻收費單");

        var missingCells = rows[1].Items!;
        Assert.Equal(WorkerValueKind.Null, missingCells[0].Kind);
        Assert.Equal(WorkerValueKind.Null, missingCells[1].Kind);
        Assert.Equal(WorkerValueKind.Null, missingCells[2].Kind);
        AssertScalar(missingCells[3], WorkerValueKind.Decimal, "0");
        Assert.Equal(WorkerValueKind.Null, missingCells[4].Kind);
        Assert.All(missingCells.Skip(5), cell => Assert.Equal(WorkerValueKind.Null, cell.Kind));

        AssertScalar(rows[2].Items![3], WorkerValueKind.Decimal, "0");
    }

    /// <summary>
    /// 證明每個已知 SDK attribute 只接受其契約型別；string、DateTime、Money、OptionSetValue
    /// 任一錯置都使整個 operation 失敗，不會略過壞列或回傳先前已投影的資料。
    /// </summary>
    [Fact]
    public void Rejects_wrong_sdk_attribute_types_without_partial_success()
    {
        var mutations = new Action<Entity>[]
        {
            entity => entity["new_feeid"] = "not-a-guid",
            entity => entity["createdon"] = "not-a-date",
            entity => entity["new_pay_date"] = new Money(1m),
            entity => entity["new_fee_really_paid"] = 12.34m,
            entity => entity["new_pay_way"] = 100000002,
            entity => entity["new_category"] = "not-an-option",
            entity => entity["new_others"] = 1,
            entity => entity["new_paid_period"] = DateTime.UtcNow,
            entity => entity["new_name"] = new OptionSetValue(1)
        };

        foreach (var mutate in mutations)
        {
            var entity = CreateMinimalEntity(Guid.NewGuid());
            mutate(entity);
            var client = new FakeCrm82SdkClient
            {
                RetrieveMultipleHandler = _ => CreatePage(new[] { entity })
            };

            Assert.Throws<InvalidOperationException>(
                () => Package01FeeQueryOperation.Execute(client, CreateRequest()));
        }
    }

    /// <summary>建立固定、可由 adapter 再驗證的 Package01 request。</summary>
    /// <param name="includeContactName">是否加入只能被丟棄的 legacy compatibility 欄位。</param>
    /// <returns>不含端點、credential、Session 或 caller routing authority 的 request。</returns>
    internal static WorkerRequestV1 CreateRequest(bool includeContactName = false)
    {
        var parameters = new Dictionary<string, WorkerValue>(StringComparer.Ordinal)
        {
            ["contactId"] = WorkerValue.FromGuid(ContactId),
            ["startDate"] = WorkerValue.FromUtcDateTime(StartDate),
            ["endDate"] = WorkerValue.FromUtcDateTime(EndDate)
        };
        if (includeContactName)
        {
            parameters["contactName"] = WorkerValue.FromString(ContactNameSentinel);
        }

        return new WorkerRequestV1(
            WorkerProtocolVersion.Current,
            "0123456789abcdef0123456789abcdef",
            Guid.NewGuid(),
            "profile-generation-0001",
            Package01FeeWorkerContract.OperationDefinitionRevision,
            Package01FeeWorkerContract.CapabilityOperationId,
            EndDate.AddMinutes(1).UtcDateTime.Ticks,
            parameters);
    }

    /// <summary>建立只含 Entity logical name 與 ID 的合法最小 fee row。</summary>
    private static Entity CreateMinimalEntity(Guid id) => new Entity("new_fee", id);

    /// <summary>建立指定數量、各自具有唯一 ID 的合法最小 Entity 集合。</summary>
    private static IReadOnlyList<Entity> CreateEntities(int count)
    {
        var entities = new List<Entity>(count);
        for (var index = 0; index < count; index++)
        {
            var bytes = new byte[16];
            BitConverter.GetBytes(index + 1).CopyTo(bytes, 0);
            entities.Add(CreateMinimalEntity(new Guid(bytes)));
        }

        return entities;
    }

    /// <summary>建立一個可精確控制 MoreRecords 與 PagingCookie 的 SDK page。</summary>
    private static EntityCollection CreatePage(
        IEnumerable<Entity> entities,
        bool moreRecords = false,
        string? pagingCookie = null)
    {
        var page = new EntityCollection
        {
            MoreRecords = moreRecords,
            PagingCookie = pagingCookie
        };
        foreach (var entity in entities)
        {
            page.Entities.Add(entity);
        }

        return page;
    }

    /// <summary>比較一個固定 filter condition 的欄位、operator 與值順序。</summary>
    private static void AssertCondition(
        ConditionExpression condition,
        string attributeName,
        ConditionOperator conditionOperator,
        params object[] values)
    {
        Assert.Equal(attributeName, condition.AttributeName);
        Assert.Equal(conditionOperator, condition.Operator);
        Assert.Equal(values, condition.Values.ToArray());
    }

    /// <summary>比較 deterministic order 的欄位與方向。</summary>
    private static void AssertOrder(
        OrderExpression order,
        string attributeName,
        OrderType orderType)
    {
        Assert.Equal(attributeName, order.AttributeName);
        Assert.Equal(orderType, order.OrderType);
    }

    /// <summary>比較 SDK-free scalar kind 與 canonical invariant value。</summary>
    private static void AssertScalar(
        WorkerValue value,
        WorkerValueKind kind,
        string scalar)
    {
        Assert.Equal(kind, value.Kind);
        Assert.Equal(scalar, value.Scalar);
        Assert.Null(value.Items);
        Assert.Null(value.Members);
    }
}
