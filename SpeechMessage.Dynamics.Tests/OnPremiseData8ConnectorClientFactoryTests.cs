using FluentAssertions;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.Abstractions.Connectors;
using SpeechMessage.Dynamics.Abstractions.Execution;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.Connectors.Data8;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證 Data8 OnPremise client factory 只在已解析的 Data8 Profile 與固定 credential reference 相符時建立
/// 短生命週期 Connector client。所有替身均完全離線；測試保護 WCF service 的唯一 Dispose ownership、WhoAmI
/// 安全 GUID 投影與取消 fail-closed，不會建立真實 credential、network session、timer 或背景工作。
/// </summary>
public sealed class OnPremiseData8ConnectorClientFactoryTests
{
    private static readonly Guid OrganizationId = Guid.Parse("bfb92ead-3705-f011-8143-00155d006608");

    /// <summary>
    /// 保護 credential reference 不相符時 factory 在建立任何 WCF service 前 fail closed。故障注入為已解析但
    /// 指向另一個 reference 的 Data8 Profile；主要斷言是固定例外與 service factory 零呼叫，避免把另一組
    /// 組織的 credential 或 session 意外帶入目前 Pool。
    /// </summary>
    [Fact]
    public async Task Create_async_rejects_a_mismatched_credential_reference_before_creating_a_service()
    {
        var created = 0;
        var factory = new OnPremiseData8ConnectorClientFactory(
            CreateConnectionSettings(),
            _ =>
            {
                Interlocked.Increment(ref created);
                return new FakeOrganizationService(OrganizationId);
            });
        var profile = CreateProfile() with { CredentialReference = "another-reference" };

        var action = async () => await factory.CreateAsync(profile, CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>();
        Volatile.Read(ref created).Should().Be(0);
    }

    /// <summary>
    /// 保護 factory 建立的 SDK-free client 只執行 allowlisted WhoAmI，並把三個 GUID 投影為純字串結果。
    /// 故障模型是 service 沒有被 lease owner Dispose；主要斷言是 operation 結果不含 SDK 物件且 client
    /// Dispose 後 fake service 恰好釋放一次，因此 pool drain 不會遺留 WCF channel 或跨 request session。
    /// </summary>
    [Fact]
    public async Task Created_client_executes_whoami_and_disposes_the_owned_service_exactly_once()
    {
        var service = new FakeOrganizationService(OrganizationId);
        var factory = new OnPremiseData8ConnectorClientFactory(CreateConnectionSettings(), _ => service);
        var client = await factory.CreateAsync(CreateProfile(), CancellationToken.None);
        var operation = new ConnectorOperation
        {
            OperationId = "runtime.health.whoami",
            WorkloadSubjectId = "test",
            DeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(5)
        };

        var result = await client.ExecuteAsync(operation, CancellationToken.None);
        await client.DisposeAsync();
        await client.DisposeAsync();

        result.Succeeded.Should().BeTrue();
        result.Values["organizationId"].Should().Be(OrganizationId.ToString("D"));
        service.ExecuteCount.Should().Be(1);
        service.DisposeCount.Should().Be(1);
    }

    /// <summary>
    /// 保護真正的 Data8 client 對 Package01 fee read 只能建立固定 QueryExpression，並在同一個 connector scope
    /// 將 CRM Entity 投影為安全 fee branch。故障注入由離線 service 擷取 query，確保 legacy contactName 不會
    /// 進入條件、也不會接受 caller FetchXML/endpoint；決定性斷言是固定 entity/filter/order/page 上限、封閉 DTO
    /// 與 client Dispose 後 service 恰好釋放一次。測試不建立 D365/WCF/ADFS 連線或真實憑證。
    /// </summary>
    [Fact]
    public async Task Created_client_executes_a_server_owned_package01_fee_read_and_projects_only_safe_records()
    {
        var contactId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var feeId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var service = new FakeOrganizationService(
            OrganizationId,
            query =>
            {
                var feeQuery = query.Should().BeOfType<QueryExpression>().Subject;
                feeQuery.EntityName.Should().Be("new_fee");
                feeQuery.ColumnSet.Columns.Should().BeEquivalentTo(
                    [
                        "new_feeid",
                        "new_name",
                        "createdon",
                        "new_pay_date",
                        "new_fee_really_paid",
                        "new_pay_way",
                        "new_category",
                        "new_others",
                        "new_paid_period"
                    ],
                    options => options.WithStrictOrdering());
                feeQuery.Criteria.Conditions.Should().HaveCount(2);
                feeQuery.Criteria.Conditions[0].AttributeName.Should().Be("new_contact_new_fee");
                feeQuery.Criteria.Conditions[0].Operator.Should().Be(ConditionOperator.Equal);
                feeQuery.Criteria.Conditions[0].Values.Should().ContainSingle().Which.Should().Be(contactId);
                feeQuery.Criteria.Conditions[1].AttributeName.Should().Be("new_category");
                feeQuery.Criteria.Conditions[1].Operator.Should().Be(ConditionOperator.NotNull);
                feeQuery.Orders.Should().HaveCount(2);
                feeQuery.Orders[0].AttributeName.Should().Be("new_name");
                feeQuery.Orders[0].OrderType.Should().Be(OrderType.Ascending);
                feeQuery.Orders[1].AttributeName.Should().Be("new_feeid");
                feeQuery.Orders[1].OrderType.Should().Be(OrderType.Ascending);
                feeQuery.PageInfo.PageNumber.Should().Be(1);
                feeQuery.PageInfo.Count.Should().Be(128);
                feeQuery.PageInfo.PagingCookie.Should().BeNull();

                var page = new EntityCollection();
                var row = new Entity("new_fee", feeId)
                {
                    ["new_feeid"] = feeId,
                    ["new_name"] = "測試奉獻",
                    ["createdon"] = new DateTime(2026, 8, 7, 1, 2, 3, DateTimeKind.Utc),
                    ["new_pay_date"] = new DateTime(2026, 8, 7, 0, 0, 0, DateTimeKind.Utc),
                    ["new_fee_really_paid"] = new Money(123.45m),
                    ["new_pay_way"] = new OptionSetValue(100000002),
                    ["new_category"] = new OptionSetValue(100000003),
                    ["new_others"] = "備註",
                    ["new_paid_period"] = "2026-08"
                };
                row.FormattedValues["new_pay_way"] = "ATM 轉帳";
                row.FormattedValues["new_category"] = "十一奉獻";
                page.Entities.Add(row);
                return page;
            });
        var factory = new OnPremiseData8ConnectorClientFactory(CreateConnectionSettings(), _ => service);
        var client = await factory.CreateAsync(CreateProfile(), CancellationToken.None);
        var operation = new ConnectorOperation
        {
            OperationId = OperationIds.FeeDedicationRetrieveByContact,
            WorkloadSubjectId = "test",
            DeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(5),
            Parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["contactId"] = contactId
            }
        };

        var result = await client.ExecuteAsync(operation, CancellationToken.None);
        await client.DisposeAsync();

        result.Succeeded.Should().BeTrue();
        result.Data!.ResponseKind.Should().Be(OperationResponseKind.Package01FeeRecords);
        result.Data.FeeRecords.Should().ContainSingle().Which.Should().BeEquivalentTo(new Package01FeeRecord
        {
            FeeId = feeId,
            CreatedOn = new DateTimeOffset(2026, 8, 7, 1, 2, 3, TimeSpan.Zero),
            PayDate = new DateTimeOffset(2026, 8, 7, 0, 0, 0, TimeSpan.Zero),
            Amount = 123.45m,
            PayWayOption = 100000002,
            PayWayLabel = "ATM 轉帳",
            CategoryLabel = "十一奉獻",
            Others = "備註",
            PaidPeriod = "2026-08",
            Name = "測試奉獻"
        });
        service.RetrieveMultipleCount.Should().Be(1);
        service.DisposeCount.Should().Be(1);
    }

    /// <summary>
    /// 保護其餘五個 P7.1 read operation 都具有自己的 server-owned query route，且不會因為它們都屬於 Package01
    /// 就落入 generic CRM 通道。故障注入是以一個離線 service 擷取每種 QueryExpression；決定性斷言是每個
    /// operation 的 entity、必要 filter、order/link shape 與安全 response branch 正確。這些 tests 不發出 CE
    /// 呼叫，所有 Entity/Query 只存在於單一同步 callback，client 仍由 await using 確定釋放。
    /// </summary>
    /// <param name="operationId">必須由 Data8 實作的既有 Package01 capability ID。</param>
    /// <param name="responseKind">registry 指定且產品可接受的唯一安全 response discriminator。</param>
    [Theory]
    [InlineData(OperationIds.FeeDedicationRetrieveByContactDateRange, OperationResponseKind.Package01FeeRecords)]
    [InlineData(OperationIds.FeesRetrieveByDedicationPeriod, OperationResponseKind.Package01FeeRecords)]
    [InlineData(OperationIds.FeesEditorLoadByDiscipleLesson, OperationResponseKind.Package01StorLessonRecords)]
    [InlineData(OperationIds.LessonsStorRetrieveByContact, OperationResponseKind.Package01StorLessonRecords)]
    [InlineData(OperationIds.LessonsStorRetrieveByDiscipleLesson, OperationResponseKind.Package01StorLessonRecords)]
    public async Task Created_client_executes_each_remaining_package01_read_with_its_fixed_query_contract(
        string operationId,
        OperationResponseKind responseKind)
    {
        var service = new FakeOrganizationService(
            OrganizationId,
            query =>
            {
                AssertPackage01QueryContract(operationId, query);
                return responseKind == OperationResponseKind.Package01FeeRecords
                    ? CreateFeePage()
                    : CreateStorLessonPage();
            });
        var factory = new OnPremiseData8ConnectorClientFactory(CreateConnectionSettings(), _ => service);
        ConnectorOperationResult result;
        await using (var client = await factory.CreateAsync(CreateProfile(), CancellationToken.None))
        {
            result = await client.ExecuteAsync(CreatePackage01Operation(operationId), CancellationToken.None);
        }

        result.Succeeded.Should().BeTrue();
        result.Data!.OperationId.Should().Be(operationId);
        result.Data.ResponseKind.Should().Be(responseKind);
        service.RetrieveMultipleCount.Should().Be(1);
        service.DisposeCount.Should().Be(1);
    }

    /// <summary>
    /// 保護每個 Package01 Data8 page 都必須套用 registry 的 64 KiB `MaximumPageBytes`，不能只依賴四頁合計的
    /// 256 KiB 上限。故障注入是在離線 CRM page 放入單筆超過單頁預算、但仍低於累積預算的顯示字串；決定性斷言
    /// 是 fee 與 stor-lesson 兩種 response branch 都在回傳任何 DTO 前 fail closed，並由 lease owner 照常釋放
    /// fake service，不建立真實 CE/WCF session 或保留 CRM Entity。
    /// </summary>
    /// <param name="operationId">用於覆蓋 fee 與 stor-lesson 投影路徑的固定 allowlisted operation。</param>
    [Theory]
    [InlineData(OperationIds.FeeDedicationRetrieveByContactDateRange)]
    [InlineData(OperationIds.LessonsStorRetrieveByContact)]
    public async Task Created_client_rejects_a_page_that_exceeds_the_registry_page_byte_budget(
        string operationId)
    {
        var service = new FakeOrganizationService(
            OrganizationId,
            _ => CreatePageExceedingSinglePageByteBudget(operationId));
        var factory = new OnPremiseData8ConnectorClientFactory(CreateConnectionSettings(), _ => service);

        await using (var client = await factory.CreateAsync(CreateProfile(), CancellationToken.None))
        {
            var action = async () => await client.ExecuteAsync(CreatePackage01Operation(operationId), CancellationToken.None);

            await action.Should().ThrowAsync<InvalidOperationException>();
        }

        service.RetrieveMultipleCount.Should().Be(1);
        service.DisposeCount.Should().Be(1);
    }

    /// <summary>
    /// 建立只在本測試記憶體內存在的 factory 設定。字串內容不是真實 endpoint 或 credential；production factory
    /// 不記錄這些值，且設定 owner 是 host composition root，不會把它傳入 OperationExecutionRequest 或 Pool key。
    /// </summary>
    private static Data8OnPremiseConnectionSettings CreateConnectionSettings()
        => new(
            "churchreport.crmconnection",
            "https://example.invalid/XRMServices/2011/Organization.svc",
            "TEST\\service",
            "not-a-real-password");

    /// <summary>
    /// 建立與 production resolver 輸出同形狀的 immutable Data8 Profile；它只含 credential reference，
    /// 不含密碼、URL 或可變 client，因此測試不會把 secret/session 狀態放入共享資料結構。
    /// </summary>
    private static ResolvedProfile CreateProfile()
        => new(
            "sunnyvalechback",
            "sunnyvalechback",
            OrganizationId,
            CeVersion.Ce91,
            ConnectorKind.Data8,
            "churchreport.crmconnection",
            new ResolvedPoolPolicy(0, 1, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1), false),
            new ResolvedOperationPolicy(TimeSpan.FromSeconds(5), 0, TimeSpan.FromMilliseconds(1)),
            GenerationId: 1);

    /// <summary>
    /// 建立五個剩餘 P7.1 read operation 的 executor-normalized 測試 operation。這裡只使用 Guid、UTC
    /// DateTimeOffset 與 bounded string scalar，模擬 Data8ProfileOperationExecutor 已完成的輸入防線；測試不
    /// 能提供 FetchXML、endpoint、credential、connector 或 profile override。
    /// </summary>
    /// <param name="operationId">待測的已登錄 Package01 operation。</param>
    /// <returns>僅含該 operation 固定 schema 必需值的 connector operation。</returns>
    private static ConnectorOperation CreatePackage01Operation(string operationId)
    {
        var contactId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var dedicationBookingId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var discipleLessonId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var parameters = operationId switch
        {
            OperationIds.FeeDedicationRetrieveByContactDateRange => new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["contactId"] = contactId,
                ["startDate"] = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
                ["endDate"] = new DateTimeOffset(2026, 8, 31, 23, 59, 59, TimeSpan.Zero)
            },
            OperationIds.FeesRetrieveByDedicationPeriod => new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["dedicationBookingId"] = dedicationBookingId,
                ["paidPeriod"] = "2026-08"
            },
            OperationIds.FeesEditorLoadByDiscipleLesson or OperationIds.LessonsStorRetrieveByDiscipleLesson =>
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["discipleLessonId"] = discipleLessonId
                },
            OperationIds.LessonsStorRetrieveByContact => new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["contactId"] = contactId
            },
            _ => throw new ArgumentOutOfRangeException(nameof(operationId), operationId, "Unsupported Package01 test operation.")
        };
        return new ConnectorOperation
        {
            OperationId = operationId,
            WorkloadSubjectId = "test",
            DeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(5),
            Parameters = parameters
        };
    }

    /// <summary>
    /// 逐項驗證實際傳給 IOrganizationService 的 fixed QueryExpression。此 helper 是測試的唯一 query decoder，
    /// 避免多個 test 各自寬鬆讀取 raw SDK object 而產生不同安全定義；每個分支都同時確認 128-row page cap。
    /// </summary>
    private static void AssertPackage01QueryContract(string operationId, QueryBase query)
    {
        var expression = query.Should().BeOfType<QueryExpression>().Subject;
        expression.PageInfo.Count.Should().Be(128);
        expression.PageInfo.PageNumber.Should().Be(1);
        expression.PageInfo.PagingCookie.Should().BeNull();
        switch (operationId)
        {
            case OperationIds.FeeDedicationRetrieveByContactDateRange:
                expression.EntityName.Should().Be("new_fee");
                expression.Criteria.Conditions.Should().HaveCount(5);
                expression.Criteria.Conditions[0].AttributeName.Should().Be("new_contact_new_fee");
                expression.Criteria.Conditions[1].AttributeName.Should().Be("new_category");
                expression.Criteria.Conditions[2].AttributeName.Should().Be("new_pay_status");
                expression.Criteria.Conditions[2].Operator.Should().Be(ConditionOperator.In);
                expression.Criteria.Conditions[3].AttributeName.Should().Be("new_pay_date");
                expression.Criteria.Conditions[3].Operator.Should().Be(ConditionOperator.OnOrAfter);
                expression.Criteria.Conditions[4].AttributeName.Should().Be("new_pay_date");
                expression.Criteria.Conditions[4].Operator.Should().Be(ConditionOperator.OnOrBefore);
                expression.Orders.Select(order => order.AttributeName).Should().ContainInOrder("new_name", "new_feeid");
                return;
            case OperationIds.FeesRetrieveByDedicationPeriod:
                expression.EntityName.Should().Be("new_fee");
                expression.Criteria.Conditions.Should().HaveCount(3);
                expression.Criteria.Conditions.Select(condition => condition.AttributeName)
                    .Should().ContainInOrder("new_dedication_booking_new_fee", "new_paid_period", "statecode");
                expression.Orders.Select(order => order.AttributeName).Should().ContainInOrder("createdon", "new_feeid");
                return;
            case OperationIds.FeesEditorLoadByDiscipleLesson:
            case OperationIds.LessonsStorRetrieveByDiscipleLesson:
                expression.EntityName.Should().Be("new_stor_lessons");
                expression.Criteria.Conditions.Should().HaveCount(4);
                expression.Criteria.Conditions.Select(condition => condition.AttributeName)
                    .Should().ContainInOrder(
                        "new_enroll_status",
                        "new_new_disciple_lessons_new_stor_les",
                        "statuscode",
                        "statecode");
                expression.LinkEntities.Should().ContainSingle(link => link.EntityAlias == "contact");
                return;
            case OperationIds.LessonsStorRetrieveByContact:
                expression.EntityName.Should().Be("new_stor_lessons");
                expression.Criteria.Conditions.Should().HaveCount(2);
                expression.Criteria.Conditions.Select(condition => condition.AttributeName)
                    .Should().ContainInOrder("new_contact_new_stor_lessons", "statecode");
                expression.LinkEntities.Select(link => link.EntityAlias).Should().ContainInOrder("contact", "lesson");
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(operationId), operationId, "Unsupported Package01 query contract.");
        }
    }

    /// <summary>
    /// 建立一頁合法最小 fee EntityCollection。每個 SDK object 只存活在 fake service callback 到 client projection
    /// 的同步範圍；沒有 CRM channel、cookie、timer 或 static data，故用於驗證 query contract 不會污染其他測試。
    /// </summary>
    private static EntityCollection CreateFeePage()
    {
        var feeId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        var page = new EntityCollection();
        page.Entities.Add(new Entity("new_fee", feeId) { ["new_feeid"] = feeId });
        return page;
    }

    /// <summary>
    /// 建立一頁合法最小 stor-lesson EntityCollection。lookup/日期/顯示欄位刻意缺失，驗證 shared contract 的
    /// nullable DTO 語意；primary id 保持有效，避免測試把不合法資料誤當成 query 成功。
    /// </summary>
    private static EntityCollection CreateStorLessonPage()
    {
        var storLessonId = Guid.Parse("88888888-8888-8888-8888-888888888888");
        var page = new EntityCollection();
        page.Entities.Add(new Entity("new_stor_lessons", storLessonId)
        {
            ["new_stor_lessonsid"] = storLessonId
        });
        return page;
    }

    /// <summary>
    /// 建立超過單頁但未超過總回應預算的離線資料列，精準重現「只檢查 cumulative bytes」時會漏放行的情況。
    /// 長字串只活在這個 test callback 與 connector 投影的同步範圍；它不會寫入 log、cache、fixture 或 CRM。
    /// </summary>
    /// <param name="operationId">決定要建立 fee 或 stor-lesson 的 schema 正確測試 Entity。</param>
    /// <returns>只有一筆資料、但投影後超過 registry 64 KiB 單頁預算的 EntityCollection。</returns>
    private static EntityCollection CreatePageExceedingSinglePageByteBudget(string operationId)
    {
        var oversizedText = new string('x', 64 * 1024);
        var page = new EntityCollection();
        if (operationId == OperationIds.FeeDedicationRetrieveByContactDateRange)
        {
            var feeId = Guid.Parse("99999999-9999-9999-9999-999999999999");
            page.Entities.Add(new Entity("new_fee", feeId)
            {
                ["new_feeid"] = feeId,
                ["new_others"] = oversizedText
            });
            return page;
        }

        if (operationId == OperationIds.LessonsStorRetrieveByContact)
        {
            var storLessonId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
            page.Entities.Add(new Entity("new_stor_lessons", storLessonId)
            {
                ["new_stor_lessonsid"] = storLessonId,
                ["contact.fullname"] = new AliasedValue("contact", "fullname", oversizedText)
            });
            return page;
        }

        throw new ArgumentOutOfRangeException(nameof(operationId), operationId, "Unsupported Package01 page-budget test operation.");
    }

    /// <summary>
    /// 離線 IOrganizationService 替身只接受 WhoAmI 與指定測試注入的固定 QueryExpression，並記錄 Execute、
    /// RetrieveMultiple／Dispose 的精確次數。它不開啟 channel、handle、timer 或 thread；所有未預期 CRM 呼叫
    /// 立即失敗，避免測試誤把 generic service 當成未受控通道。
    /// </summary>
    private sealed class FakeOrganizationService : IOrganizationService, IDisposable
    {
        private readonly Guid _organizationId;
        private readonly Func<QueryBase, EntityCollection>? _retrieveMultiple;
        private int _executeCount;
        private int _retrieveMultipleCount;
        private int _disposeCount;

        /// <summary>
        /// 建立只提供必要 CRM 回應的離線 service。Query callback 為 null 時仍只允許 WhoAmI；提供時也只接受
        /// 此測試明確檢查的 QueryExpression，不會形成可任意執行 CRM command 的替身。
        /// </summary>
        /// <param name="organizationId">WhoAmI 唯一回傳的非秘密組織 GUID。</param>
        /// <param name="retrieveMultiple">測試所有的固定查詢 callback；不保存真實資料庫、連線或 session。</param>
        public FakeOrganizationService(
            Guid organizationId,
            Func<QueryBase, EntityCollection>? retrieveMultiple = null)
        {
            _organizationId = organizationId;
            _retrieveMultiple = retrieveMultiple;
        }

        public int ExecuteCount => Volatile.Read(ref _executeCount);

        /// <summary>取得固定 QueryExpression 實際呼叫次數，供驗證沒有背景補送或未界定 paging。</summary>
        public int RetrieveMultipleCount => Volatile.Read(ref _retrieveMultipleCount);

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public OrganizationResponse Execute(OrganizationRequest request)
        {
            request.Should().BeOfType<WhoAmIRequest>();
            Interlocked.Increment(ref _executeCount);
            return new WhoAmIResponse
            {
                Results = new ParameterCollection
                {
                    ["UserId"] = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    ["BusinessUnitId"] = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    ["OrganizationId"] = _organizationId
                }
            };
        }

        public Guid Create(Entity entity) => throw new NotSupportedException();

        public void Update(Entity entity) => throw new NotSupportedException();

        public void Delete(string entityName, Guid id) => throw new NotSupportedException();

        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => throw new NotSupportedException();

        /// <summary>
        /// 執行唯一由本測試注入並立即驗證的固定 QueryExpression。callback 不存在、query 為 null 或 callback
        /// 回傳 null 都 fail closed；此替身不會快取 query、EntityCollection 或資料列，因此測試結束後沒有
        /// CRM Entity graph 能跨測試 retained。
        /// </summary>
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            ArgumentNullException.ThrowIfNull(query);
            var callback = _retrieveMultiple ?? throw new NotSupportedException();
            Interlocked.Increment(ref _retrieveMultipleCount);
            return callback(query) ?? throw new InvalidOperationException("The fake query callback returned null.");
        }

        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
            => throw new NotSupportedException();

        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
            => throw new NotSupportedException();

        public void Dispose() => Interlocked.Increment(ref _disposeCount);
    }
}
