// ============================================================================
// 檔案：SpeechMessage.Dynamics.Tests/Package01FeeReadClientTests.cs
// 目的：驗證 Package 1 fee/lesson client 的 DTO 解析與 executor 參數組裝。
//
// 保母教學：
// - 不連真實 CRM / Gateway。
// - 用 fake executor 回傳 OData JSON 形狀。
// ============================================================================

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.ProductClient.FeeReads;

namespace SpeechMessage.Dynamics.Tests;

public sealed class Package01FeeReadClientTests
{
    [Fact]
    public async Task Date_range_query_sends_typed_parameters_only()
    {
        OperationExecutionRequest? seen = null;
        var executor = new FakeExecutor(request =>
        {
            seen = request;
            return OperationExecutionResult.Success(JsonDocument.Parse("""
            {
              "value": [
                {
                  "new_feeid": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                  "createdon": "2026-01-02T00:00:00Z",
                  "new_pay_date": "2026-01-03T00:00:00Z",
                  "new_fee_really_paid": 1200,
                  "new_pay_way": 100000001,
                  "new_pay_way@OData.Community.Display.V1.FormattedValue": "信用卡",
                  "new_category@OData.Community.Display.V1.FormattedValue": "十一奉獻",
                  "new_others": "note",
                  "new_paid_period": "1"
                }
              ]
            }
            """).RootElement);
        });

        var client = new Package01FeeReadClient(executor, NullLogger<Package01FeeReadClient>.Instance);
        var contactId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var rows = await client.RetrieveDedicationFeesByContactDateRangeAsync(
            "jesus-dev",
            "church-report-service",
            contactId,
            new DateTime(2026, 1, 1),
            new DateTime(2026, 1, 31),
            "王小明");

        seen.Should().NotBeNull();
        seen!.CapabilityOperationId.Should().Be(OperationIds.FeeDedicationRetrieveByContactDateRange);
        seen.Parameters.Keys.Should().BeEquivalentTo(new[] { "contactId", "startDate", "endDate", "contactName" });
        seen.Parameters.ContainsKey("rawFetchXml").Should().BeFalse();

        rows.Should().HaveCount(1);
        rows[0].Amount.Should().Be(1200);
        rows[0].PayWayLabel.Should().Be("信用卡");
        rows[0].CategoryLabel.Should().Be("十一奉獻");
        rows[0].PaidPeriod.Should().Be("1");
    }

    [Fact]
    public void Parse_supports_wrapped_operation_payload_and_money_object()
    {
        var json = JsonDocument.Parse("""
        {
          "operationId": "fee.dedication.retrieve.by.contact",
          "data": {
            "value": [
              {
                "new_fee_really_paid": { "Value": 500 },
                "new_pay_way": 100000000
              }
            ]
          }
        }
        """).RootElement;

        var rows = Package01FeeReadClient.ParseFeeRecords(json);
        rows.Should().HaveCount(1);
        rows[0].Amount.Should().Be(500);
        rows[0].PayWayOption.Should().Be(100000000);
    }

    [Fact]
    public async Task Stor_lessons_by_contact_sends_typed_parameters_and_parses_lookup_values()
    {
        OperationExecutionRequest? seen = null;
        var executor = new FakeExecutor(request =>
        {
            seen = request;
            return OperationExecutionResult.Success(JsonDocument.Parse("""
            {
              "value": [
                {
                  "new_stor_lessonsid": "cccccccc-cccc-cccc-cccc-cccccccccccc",
                  "_new_contact_new_stor_lessons_value": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
                  "_new_new_disciple_lessons_new_stor_les_value": "dddddddd-dddd-dddd-dddd-dddddddddddd",
                  "new_new_disciple_lessons_new_stor_les@OData.Community.Display.V1.FormattedValue": "幸福001",
                  "createdon": "2026-02-01T00:00:00Z",
                  "new_current_complete": true,
                  "contact.mobilephone": "0912345678"
                }
              ]
            }
            """).RootElement);
        });

        var client = new Package01FeeReadClient(executor, NullLogger<Package01FeeReadClient>.Instance);
        var rows = await client.RetrieveStorLessonsByContactAsync(
            "jesus-prod",
            "church-report-service",
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            "王小明");

        seen!.CapabilityOperationId.Should().Be(OperationIds.LessonsStorRetrieveByContact);
        seen.Parameters.Keys.Should().BeEquivalentTo(new[] { "contactId", "contactName" });
        rows.Should().HaveCount(1);
        rows[0].StorLessonId.Should().Be(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"));
        rows[0].DiscipleLessonId.Should().Be(Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"));
        rows[0].DiscipleLessonName.Should().Be("幸福001");
        rows[0].CurrentComplete.Should().BeTrue();
        rows[0].ContactMobile.Should().Be("0912345678");
    }

    [Fact]
    public async Task Fees_by_dedication_period_requires_paid_period_parameter()
    {
        OperationExecutionRequest? seen = null;
        var executor = new FakeExecutor(request =>
        {
            seen = request;
            return OperationExecutionResult.Success(JsonDocument.Parse("""{ "value": [] }""").RootElement);
        });

        var client = new Package01FeeReadClient(executor, NullLogger<Package01FeeReadClient>.Instance);
        var rows = await client.RetrieveFeesByDedicationPeriodAsync(
            "jesus-prod",
            "church-report-service",
            Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
            "2026-01",
            "booking-name");

        seen!.CapabilityOperationId.Should().Be(OperationIds.FeesRetrieveByDedicationPeriod);
        seen.Parameters.Keys.Should().BeEquivalentTo(new[]
        {
            "dedicationBookingId", "paidPeriod", "dedicationBookingName"
        });
        rows.Should().BeEmpty();
    }


    [Fact]
    public async Task Stor_lessons_by_disciple_lesson_sends_typed_parameters()
    {
        OperationExecutionRequest? seen = null;
        var executor = new FakeExecutor(request =>
        {
            seen = request;
            return OperationExecutionResult.Success(JsonDocument.Parse("""
            {
              "value": [
                {
                  "new_stor_lessonsid": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                  "_new_contact_new_stor_lessons_value": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
                  "new_current_complete": false
                }
              ]
            }
            """).RootElement);
        });

        var client = new Package01FeeReadClient(executor, NullLogger<Package01FeeReadClient>.Instance);
        var rows = await client.RetrieveStorLessonsByDiscipleLessonAsync(
            "jesus-prod",
            "church-report-service",
            Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            "幸福001");

        seen!.CapabilityOperationId.Should().Be(OperationIds.LessonsStorRetrieveByDiscipleLesson);
        seen.Parameters.Keys.Should().BeEquivalentTo(new[] { "discipleLessonId", "lessonName" });
        rows.Should().HaveCount(1);
        rows[0].ContactId.Should().Be(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));
    }
    private sealed class FakeExecutor : IDynamicsOperationExecutor
    {
        private readonly Func<OperationExecutionRequest, OperationExecutionResult> _handler;

        public FakeExecutor(Func<OperationExecutionRequest, OperationExecutionResult> handler)
        {
            _handler = handler;
        }

        public Task<OperationExecutionResult> ExecuteAsync(
            OperationExecutionRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_handler(request));
    }
}
