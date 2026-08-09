using FluentAssertions;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.Abstractions.Connectors;
using SpeechMessage.Dynamics.Abstractions.Execution;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.Connectors.Data8;
using System.Xml.Linq;

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
    /// 保護 Slice C add-many 只發出固定 AddListMembersListRequest，且只把已排序的 bounded member set
    /// 交給 SDK。故障注入是目前 connector dispatch 尚未接線；決定性斷言是 action request 的 list/member
    /// 欄位固定、回應只含 changed/read-back-confirmed 分類，沒有 generic Entity 或 caller-selected message。
    /// </summary>
    [Fact]
    public async Task Created_client_executes_a_fixed_add_list_members_action()
    {
        var listId = Guid.Parse("aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb");
        var memberIds = new[]
        {
            Guid.Parse("55555555-5555-5555-5555-555555555555"),
            Guid.Parse("11111111-1111-1111-1111-111111111111")
        };
        var queryStep = 0;
        var service = new FakeOrganizationService(
            OrganizationId,
            retrieveMultiple: query =>
            {
                queryStep++;
                AssertFixedMembershipQuery(query, listId, memberIds);
                return queryStep == 1
                    ? new EntityCollection()
                    : new EntityCollection(memberIds.Select(memberId => new Entity("listmember")
                    {
                        ["listid"] = listId,
                        ["entityid"] = memberId
                    }).ToList());
            },
            execute: request =>
            {
                var add = request.Should().BeOfType<AddListMembersListRequest>().Subject;
                add.ListId.Should().Be(listId);
                add.MemberIds.Should().Equal(memberIds);
                return new OrganizationResponse();
            });
        var factory = new OnPremiseData8ConnectorClientFactory(CreateConnectionSettings(), _ => service);
        var operation = new ConnectorOperation
        {
            OperationId = OperationIds.ListMembersAddMany,
            WorkloadSubjectId = "test",
            DeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(5),
            Parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["listId"] = listId,
                ["memberIds"] = memberIds
            }
        };

        ConnectorOperationResult result;
        await using (var client = await factory.CreateAsync(CreateProfile(), CancellationToken.None))
        {
            result = await client.ExecuteAsync(operation, CancellationToken.None);
        }

        result.Succeeded.Should().BeTrue();
        result.Data!.ResponseKind.Should().Be(OperationResponseKind.StaticListMembershipMutation);
        result.Data.StaticListMembershipMutation!.Disposition.Should().Be(P72ControlledMutationDisposition.Changed);
        result.Data.StaticListMembershipMutation.CorrelationCategory
            .Should().Be(P72ControlledMutationCorrelationCategory.ReadBackConfirmed);
        service.ExecuteCount.Should().Be(1);
        service.RetrieveMultipleCount.Should().Be(2);
        service.DisposeCount.Should().Be(1);
    }

    /// <summary>
    /// 保護 add-many 的 idempotency：baseline 已存在的成員不得再次送入 AddListMembersListRequest，只有
    /// 缺少的 GUID 可以 dispatch。故障注入是目前實作把完整 caller set 重送；決定性斷言是 action 只含
    /// missing member，且 post-read 仍確認完整 target set。測試不保存任何跨 request membership state。
    /// </summary>
    [Fact]
    public async Task Created_client_adds_only_members_missing_from_the_baseline()
    {
        var listId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var existingMemberId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var missingMemberId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var queryStep = 0;
        var service = new FakeOrganizationService(
            OrganizationId,
            retrieveMultiple: query =>
            {
                AssertFixedMembershipQuery(query, listId, [existingMemberId, missingMemberId]);
                queryStep++;
                return queryStep == 1
                    ? new EntityCollection([new Entity("listmember")
                    {
                        ["listid"] = listId,
                        ["entityid"] = existingMemberId
                    }])
                    : new EntityCollection(
                    [
                        new Entity("listmember")
                        {
                            ["listid"] = listId,
                            ["entityid"] = existingMemberId
                        },
                        new Entity("listmember")
                        {
                            ["listid"] = listId,
                            ["entityid"] = missingMemberId
                        }
                    ]);
            },
            execute: request =>
            {
                var add = request.Should().BeOfType<AddListMembersListRequest>().Subject;
                add.ListId.Should().Be(listId);
                add.MemberIds.Should().Equal(missingMemberId);
                return new OrganizationResponse();
            });
        var factory = new OnPremiseData8ConnectorClientFactory(CreateConnectionSettings(), _ => service);
        var operation = new ConnectorOperation
        {
            OperationId = OperationIds.ListMembersAddMany,
            WorkloadSubjectId = "test",
            DeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(5),
            Parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["listId"] = listId,
                ["memberIds"] = new[] { existingMemberId, missingMemberId }
            }
        };

        ConnectorOperationResult result;
        await using (var client = await factory.CreateAsync(CreateProfile(), CancellationToken.None))
        {
            result = await client.ExecuteAsync(operation, CancellationToken.None);
        }

        result.Data!.StaticListMembershipMutation!.Disposition.Should().Be(P72ControlledMutationDisposition.Changed);
        result.Data.StaticListMembershipMutation.CorrelationCategory
            .Should().Be(P72ControlledMutationCorrelationCategory.ReadBackConfirmed);
        service.ExecuteCount.Should().Be(1);
        service.RetrieveMultipleCount.Should().Be(2);
        service.DisposeCount.Should().Be(1);
    }

    /// <summary>
    /// 保護 Slice C remove-one 只能使用固定 RemoveMemberListRequest，不能退化為任意 Delete 或 Entity request。
    /// 故障注入是目前 connector dispatch 尚未接線；決定性斷言是 list/member identity 與封閉 response branch。
    /// </summary>
    [Fact]
    public async Task Created_client_executes_a_fixed_remove_list_member_action()
    {
        var listId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var memberId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var queryStep = 0;
        var service = new FakeOrganizationService(
            OrganizationId,
            retrieveMultiple: query =>
            {
                queryStep++;
                AssertFixedMembershipQuery(query, listId, [memberId]);
                return queryStep == 1
                    ? new EntityCollection([new Entity("listmember")
                    {
                        ["listid"] = listId,
                        ["entityid"] = memberId
                    }])
                    : new EntityCollection();
            },
            execute: request =>
            {
                var remove = request.Should().BeOfType<RemoveMemberListRequest>().Subject;
                remove.ListId.Should().Be(listId);
                remove.EntityId.Should().Be(memberId);
                return new OrganizationResponse();
            });
        var factory = new OnPremiseData8ConnectorClientFactory(CreateConnectionSettings(), _ => service);
        var operation = new ConnectorOperation
        {
            OperationId = OperationIds.ListMembersRemoveOne,
            WorkloadSubjectId = "test",
            DeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(5),
            Parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["listId"] = listId,
                ["memberId"] = memberId
            }
        };

        ConnectorOperationResult result;
        await using (var client = await factory.CreateAsync(CreateProfile(), CancellationToken.None))
        {
            result = await client.ExecuteAsync(operation, CancellationToken.None);
        }

        result.Succeeded.Should().BeTrue();
        result.Data!.ResponseKind.Should().Be(OperationResponseKind.StaticListMembershipMutation);
        result.Data.StaticListMembershipMutation!.Disposition.Should().Be(P72ControlledMutationDisposition.Changed);
        result.Data.StaticListMembershipMutation.CorrelationCategory
            .Should().Be(P72ControlledMutationCorrelationCategory.ReadBackConfirmed);
        service.ExecuteCount.Should().Be(1);
        service.RetrieveMultipleCount.Should().Be(2);
        service.DisposeCount.Should().Be(1);
    }

    /// <summary>
    /// 保護實際 Dataverse <c>listmember</c> read-back 的 lookup 投影相容性。故障注入使用真機常見的
    /// <see cref="EntityReference"/> 形狀，而不是既有 fake 預設的裸 GUID；決定性斷言是 Data8 connector
    /// 仍只送出一次固定 AddListMembers action，並能以 list/contact 邏輯名稱與非空 identity 讀回確認。
    /// 這能避免真機寫入已提交後，因測試替身未模擬 lookup 形狀而被誤判成不明狀態。service 與所有 SDK
    /// Entity 都只活在本方法及同步 fake callback，await using 結束時由 connector lease 確定釋放。
    /// </summary>
    [Fact]
    public async Task Created_client_reads_listmember_lookup_attributes_as_entity_references()
    {
        var listId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var memberId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var queryStep = 0;
        var service = new FakeOrganizationService(
            OrganizationId,
            retrieveMultiple: query =>
            {
                AssertFixedMembershipQuery(query, listId, [memberId]);
                if (queryStep++ == 0)
                {
                    return new EntityCollection();
                }

                return new EntityCollection(
                [
                    new Entity("listmember")
                    {
                        ["listid"] = new EntityReference("list", listId),
                        ["entityid"] = new EntityReference("contact", memberId)
                    }
                ]);
            },
            execute: request =>
            {
                var add = request.Should().BeOfType<AddListMembersListRequest>().Subject;
                add.ListId.Should().Be(listId);
                add.MemberIds.Should().Equal(memberId);
                return new OrganizationResponse();
            });
        var factory = new OnPremiseData8ConnectorClientFactory(CreateConnectionSettings(), _ => service);
        var operation = new ConnectorOperation
        {
            OperationId = OperationIds.ListMembersAddMany,
            WorkloadSubjectId = "test",
            DeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(5),
            Parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["listId"] = listId,
                ["memberIds"] = new[] { memberId }
            }
        };

        ConnectorOperationResult result;
        await using (var client = await factory.CreateAsync(CreateProfile(), CancellationToken.None))
        {
            result = await client.ExecuteAsync(operation, CancellationToken.None);
        }

        result.Succeeded.Should().BeTrue();
        result.Data!.StaticListMembershipMutation!.Disposition.Should().Be(P72ControlledMutationDisposition.Changed);
        result.Data.StaticListMembershipMutation.CorrelationCategory
            .Should().Be(P72ControlledMutationCorrelationCategory.ReadBackConfirmed);
        service.ExecuteCount.Should().Be(1);
        service.RetrieveMultipleCount.Should().Be(2);
        service.DisposeCount.Should().Be(1);
    }

    /// <summary>
    /// 驗證 add-many 在 baseline 已完整包含要求的成員時只回傳 no-change，絕不重送 CRM action。
    /// 這保護 caller idempotency 與 unknown-timeout 後的 reconciliation 邊界。
    /// </summary>
    [Fact]
    public async Task Created_client_returns_no_change_for_an_already_complete_add_members_set()
    {
        var listId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var memberId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var service = new FakeOrganizationService(
            OrganizationId,
            retrieveMultiple: query =>
            {
                AssertFixedMembershipQuery(query, listId, [memberId]);
                return new EntityCollection([new Entity("listmember")
                {
                    ["listid"] = listId,
                    ["entityid"] = memberId
                }]);
            },
            execute: _ => throw new InvalidOperationException("No-change must not dispatch an action."));
        var factory = new OnPremiseData8ConnectorClientFactory(CreateConnectionSettings(), _ => service);
        var operation = new ConnectorOperation
        {
            OperationId = OperationIds.ListMembersAddMany,
            WorkloadSubjectId = "test",
            DeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(5),
            Parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["listId"] = listId,
                ["memberIds"] = new[] { memberId }
            }
        };

        ConnectorOperationResult result;
        await using (var client = await factory.CreateAsync(CreateProfile(), CancellationToken.None))
        {
            result = await client.ExecuteAsync(operation, CancellationToken.None);
        }

        result.Data!.StaticListMembershipMutation!.Disposition.Should().Be(P72ControlledMutationDisposition.NoChange);
        result.Data.StaticListMembershipMutation.CorrelationCategory
            .Should().Be(P72ControlledMutationCorrelationCategory.NoDispatch);
        service.RetrieveMultipleCount.Should().Be(1);
        service.ExecuteCount.Should().Be(0);
        service.DisposeCount.Should().Be(1);
    }

    /// <summary>
    /// 驗證 remove-one 在 baseline 已不存在時只回傳 no-change，避免重送 RemoveMember action。
    /// </summary>
    [Fact]
    public async Task Created_client_returns_no_change_for_an_absent_remove_member()
    {
        var listId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var memberId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var service = new FakeOrganizationService(
            OrganizationId,
            retrieveMultiple: query =>
            {
                AssertFixedMembershipQuery(query, listId, [memberId]);
                return new EntityCollection();
            },
            execute: _ => throw new InvalidOperationException("No-change must not dispatch an action."));
        var factory = new OnPremiseData8ConnectorClientFactory(CreateConnectionSettings(), _ => service);
        var operation = new ConnectorOperation
        {
            OperationId = OperationIds.ListMembersRemoveOne,
            WorkloadSubjectId = "test",
            DeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(5),
            Parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["listId"] = listId,
                ["memberId"] = memberId
            }
        };

        ConnectorOperationResult result;
        await using (var client = await factory.CreateAsync(CreateProfile(), CancellationToken.None))
        {
            result = await client.ExecuteAsync(operation, CancellationToken.None);
        }

        result.Data!.StaticListMembershipMutation!.Disposition.Should().Be(P72ControlledMutationDisposition.NoChange);
        result.Data.StaticListMembershipMutation.CorrelationCategory
            .Should().Be(P72ControlledMutationCorrelationCategory.NoDispatch);
        service.RetrieveMultipleCount.Should().Be(1);
        service.ExecuteCount.Should().Be(0);
        service.DisposeCount.Should().Be(1);
    }

    /// <summary>
    /// 保護 static-list membership pre-read/read-back 對 501 筆 GUID 一律切成最多 500 筆的固定查詢。
    /// 故障注入是目前 template 仍把整組 GUID 放進單一 IN；決定性斷言是兩個 phase 各收到兩個 bounded
    /// QueryExpression，action 本身仍只執行一次且傳送完整、distinct、defensive-copied member set。
    /// 測試替身只在同步 callback 內產生短生命週期 EntityCollection，沒有 CRM session、背景工作或快取。
    /// </summary>
    [Fact]
    public async Task Created_client_reads_membership_in_bounded_500_id_chunks()
    {
        var listId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var memberIds = Enumerable.Range(1, 501)
            .Select(index => new Guid(index, 0, 0, new byte[8]))
            .ToArray();
        var queryCall = 0;
        var service = new FakeOrganizationService(
            OrganizationId,
            retrieveMultiple: query =>
            {
                var phaseCall = queryCall++;
                var chunkIndex = phaseCall % 2;
                var expectedChunk = memberIds.Skip(chunkIndex * 500).Take(500).ToArray();
                AssertFixedMembershipQuery(query, listId, expectedChunk);
                expectedChunk.Length.Should().BeLessThanOrEqualTo(500);
                return phaseCall < 2
                    ? new EntityCollection()
                    : new EntityCollection(expectedChunk.Select(memberId => new Entity("listmember")
                    {
                        ["listid"] = listId,
                        ["entityid"] = memberId
                    }).ToList());
            },
            execute: request =>
            {
                var add = request.Should().BeOfType<AddListMembersListRequest>().Subject;
                add.ListId.Should().Be(listId);
                add.MemberIds.Should().Equal(memberIds);
                return new OrganizationResponse();
            });
        var factory = new OnPremiseData8ConnectorClientFactory(CreateConnectionSettings(), _ => service);
        var operation = new ConnectorOperation
        {
            OperationId = OperationIds.ListMembersAddMany,
            WorkloadSubjectId = "test",
            DeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(5),
            Parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["listId"] = listId,
                ["memberIds"] = memberIds
            }
        };

        ConnectorOperationResult result;
        await using (var client = await factory.CreateAsync(CreateProfile(), CancellationToken.None))
        {
            result = await client.ExecuteAsync(operation, CancellationToken.None);
        }

        result.Data!.ResponseKind.Should().Be(OperationResponseKind.StaticListMembershipMutation);
        result.Data.StaticListMembershipMutation!.Disposition.Should().Be(P72ControlledMutationDisposition.Changed);
        result.Data.StaticListMembershipMutation.CorrelationCategory
            .Should().Be(P72ControlledMutationCorrelationCategory.ReadBackConfirmed);
        service.ExecuteCount.Should().Be(1);
        service.RetrieveMultipleCount.Should().Be(4);
        service.DisposeCount.Should().Be(1);
    }

    /// <summary>
    /// 保護 contact transfer composite 依固定順序完成 target membership、source removal、週報 present record、
    /// primary-list lookup，並在每個 component read-back 後才回傳成功。故障注入是目前 connector dispatch 尚未
    /// 接線；決定性斷言是不得接受 caller 的 entity/field map，且只執行兩個固定 membership action、一次
    /// present-record create、一次 contact lookup update。所有資料只在同步 fake callback 內存活，不跨 lease。
    /// </summary>
    [Fact]
    public async Task Created_client_executes_and_reconciles_the_fixed_contact_list_transfer_graph()
    {
        var contactId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var sourceListId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var targetListId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var weeklyReportId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var presentRecordId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var weekStartDate = new DateTimeOffset(2026, 8, 9, 0, 0, 0, TimeSpan.Zero);
        var sourceMembershipReads = 0;
        var targetMembershipReads = 0;
        var presentRecordReads = 0;
        var contactReads = 0;
        var service = new FakeOrganizationService(
            OrganizationId,
            retrieveMultiple: query =>
            {
                var expression = query.Should().BeOfType<QueryExpression>().Subject;
                if (expression.EntityName == "listmember")
                {
                    var listCondition = expression.Criteria.Conditions.Single(condition =>
                        condition.AttributeName == "listid" && condition.Operator == ConditionOperator.Equal);
                    var queriedListId = listCondition.Values.Should().ContainSingle().Which.Should().BeOfType<Guid>().Subject;
                    var memberCondition = expression.Criteria.Conditions.Single(condition =>
                        condition.AttributeName == "entityid" && condition.Operator == ConditionOperator.In);
                    memberCondition.Values.Should().ContainSingle().Which.Should().Be(contactId);
                    if (queriedListId == sourceListId)
                    {
                        return sourceMembershipReads++ == 0
                            ? new EntityCollection([new Entity("listmember")
                            {
                                ["listid"] = sourceListId,
                                ["entityid"] = contactId
                            }])
                            : new EntityCollection();
                    }

                    queriedListId.Should().Be(targetListId);
                    return targetMembershipReads++ == 0
                        ? new EntityCollection()
                        : new EntityCollection([new Entity("listmember")
                        {
                            ["listid"] = targetListId,
                            ["entityid"] = contactId
                        }]);
                }

                if (expression.EntityName == "new_group_present_weekly_report")
                {
                    expression.Criteria.Conditions.Should().Contain(condition =>
                        condition.AttributeName == "new_list_group_present_weekly_report" &&
                        condition.Operator == ConditionOperator.Equal &&
                        condition.Values.Contains(targetListId));
                    expression.Criteria.Conditions.Should().Contain(condition =>
                        condition.AttributeName == "new_sunday_date" &&
                        condition.Operator == ConditionOperator.Equal &&
                        condition.Values.Contains(weekStartDate.UtcDateTime));
                    return new EntityCollection([new Entity("new_group_present_weekly_report", weeklyReportId)]);
                }

                expression.EntityName.Should().Be("new_present_record");
                expression.Criteria.Conditions.Should().Contain(condition =>
                    condition.AttributeName == "new_group_present_weekly_report_prese" &&
                    condition.Operator == ConditionOperator.Equal &&
                    condition.Values.Contains(weeklyReportId));
                expression.Criteria.Conditions.Should().Contain(condition =>
                    condition.AttributeName == "new_contact_new_present_record" &&
                    condition.Operator == ConditionOperator.Equal &&
                    condition.Values.Contains(contactId));
                expression.Criteria.Conditions.Should().Contain(condition =>
                    condition.AttributeName == "new_sunday_date" &&
                    condition.Operator == ConditionOperator.Equal &&
                    condition.Values.Contains(weekStartDate.UtcDateTime));
                return presentRecordReads++ == 0
                    ? new EntityCollection()
                    : new EntityCollection([new Entity("new_present_record", presentRecordId)
                    {
                        ["new_group_present_weekly_report_prese"] = new EntityReference(
                            "new_group_present_weekly_report", weeklyReportId),
                        ["new_contact_new_present_record"] = new EntityReference("contact", contactId),
                        ["new_list_new_present_record"] = new EntityReference("list", targetListId),
                        ["new_sunday_date"] = weekStartDate.UtcDateTime
                    }]);
            },
            update: entity =>
            {
                entity.LogicalName.Should().Be("contact");
                entity.Id.Should().Be(contactId);
                entity.Attributes.Should().BeEquivalentTo(new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["new_cell_list_contact"] = new EntityReference("list", targetListId)
                });
            },
            retrieve: (entityName, id, columnSet) =>
            {
                entityName.Should().Be("contact");
                id.Should().Be(contactId);
                columnSet.Columns.Should().Equal("new_cell_list_contact");
                contactReads++;
                return new Entity("contact", contactId)
                {
                    ["new_cell_list_contact"] = new EntityReference(
                        "list",
                        contactReads == 1 ? sourceListId : targetListId)
                };
            },
            execute: request =>
            {
                switch (request)
                {
                    case AddListMembersListRequest add:
                        add.ListId.Should().Be(targetListId);
                        add.MemberIds.Should().Equal(contactId);
                        break;
                    case RemoveMemberListRequest remove:
                        remove.ListId.Should().Be(sourceListId);
                        remove.EntityId.Should().Be(contactId);
                        break;
                    default:
                        throw new InvalidOperationException("Unexpected transfer request.");
                }

                return new OrganizationResponse();
            },
            create: entity =>
            {
                entity.LogicalName.Should().Be("new_present_record");
                entity.Attributes.Should().BeEquivalentTo(new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["new_group_present_weekly_report_prese"] = new EntityReference(
                        "new_group_present_weekly_report", weeklyReportId),
                    ["new_contact_new_present_record"] = new EntityReference("contact", contactId),
                    ["new_list_new_present_record"] = new EntityReference("list", targetListId),
                    ["new_sunday_date"] = weekStartDate.UtcDateTime
                });
                return presentRecordId;
            });
        var factory = new OnPremiseData8ConnectorClientFactory(CreateConnectionSettings(), _ => service);
        var operation = new ConnectorOperation
        {
            OperationId = OperationIds.NewPersonContactTransferBetweenLists,
            WorkloadSubjectId = "test",
            DeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(5),
            Parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["contactId"] = contactId,
                ["sourceListId"] = sourceListId,
                ["targetListId"] = targetListId,
                ["weekStartDate"] = weekStartDate
            }
        };

        ConnectorOperationResult result;
        await using (var client = await factory.CreateAsync(CreateProfile(), CancellationToken.None))
        {
            result = await client.ExecuteAsync(operation, CancellationToken.None);
        }

        result.Data!.ResponseKind.Should().Be(OperationResponseKind.ContactListTransfer);
        result.Data.ContactListTransfer!.Disposition.Should().Be(P72ControlledMutationDisposition.Changed);
        result.Data.ContactListTransfer.CorrelationCategory
            .Should().Be(P72ControlledMutationCorrelationCategory.ReadBackConfirmed);
        service.ExecuteCount.Should().Be(2);
        service.CreateCount.Should().Be(1);
        service.UpdateCount.Should().Be(1);
        service.RetrieveCount.Should().Be(2);
        service.DisposeCount.Should().Be(1);
    }

    /// <summary>
    /// 保護 change-area-leader 只能由 server-owned relationship 解析區牧與區名，並一次更新三個目標欄位、
    /// 清除三個固定 deputy lookup。故障注入是 connector 尚未接線；決定性斷言是完整六欄 baseline/read-back、
    /// exact Entity update 與封閉 response branch，沒有 caller field-map、FetchXML 或跨 request SDK graph。
    /// </summary>
    [Fact]
    public async Task Created_client_updates_and_confirms_the_six_fixed_small_group_fields()
    {
        var listId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var raceLeaderId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var areaLeaderId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var retrieveStep = 0;
        var service = new FakeOrganizationService(
            OrganizationId,
            retrieveMultiple: query =>
            {
                var expression = query.Should().BeOfType<QueryExpression>().Subject;
                expression.EntityName.Should().Be("list");
                expression.ColumnSet.Columns.Should().Equal(
                    "new_contact_list_arealeader",
                    "new_area_name");
                expression.Criteria.Conditions.Should().Contain(condition =>
                    condition.AttributeName == "new_contact_race_leager_list" &&
                    condition.Operator == ConditionOperator.Equal);
                return new EntityCollection([new Entity("list", Guid.Parse("33333333-3333-3333-3333-333333333333"))
                {
                    ["new_contact_list_arealeader"] = new EntityReference("contact", areaLeaderId),
                    ["new_area_name"] = "測試牧區"
                }]);
            },
            update: entity =>
            {
                entity.LogicalName.Should().Be("list");
                entity.Id.Should().Be(listId);
                entity.Attributes.Should().BeEquivalentTo(new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["new_contact_list_arealeader"] = new EntityReference("contact", areaLeaderId),
                    ["new_area_name"] = "測試牧區",
                    ["new_contact_race_leager_list"] = new EntityReference("contact", raceLeaderId),
                    ["new_contact_list_co_arealeader"] = null,
                    ["new_contact_co_race_leager_list"] = null,
                    ["new_contact_list_vice_family_leader"] = null
                });
            },
            retrieve: (entityName, id, columnSet) =>
            {
                retrieveStep++;
                entityName.Should().Be("list");
                id.Should().Be(listId);
                columnSet.Columns.Should().Equal(SmallGroupFixedFieldNames);
                return retrieveStep == 1
                    ? new Entity("list", listId)
                    : new Entity("list", listId)
                    {
                        ["new_contact_list_arealeader"] = new EntityReference("contact", areaLeaderId),
                        ["new_area_name"] = "測試牧區",
                        ["new_contact_race_leager_list"] = new EntityReference("contact", raceLeaderId),
                        ["new_contact_list_co_arealeader"] = null,
                        ["new_contact_co_race_leager_list"] = null,
                        ["new_contact_list_vice_family_leader"] = null
                    };
            });
        var factory = new OnPremiseData8ConnectorClientFactory(CreateConnectionSettings(), _ => service);
        var operation = new ConnectorOperation
        {
            OperationId = OperationIds.ListManagementSmallGroupUpdateFields,
            WorkloadSubjectId = "test",
            DeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(5),
            Parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["listId"] = listId,
                ["mode"] = "change-area-leader",
                ["targetLeaderContactId"] = raceLeaderId
            }
        };

        ConnectorOperationResult result;
        await using (var client = await factory.CreateAsync(CreateProfile(), CancellationToken.None))
        {
            result = await client.ExecuteAsync(operation, CancellationToken.None);
        }

        result.Data!.ResponseKind.Should().Be(OperationResponseKind.SmallGroupFixedFieldsMutation);
        result.Data.SmallGroupFixedFieldsMutation!.Disposition.Should().Be(P72ControlledMutationDisposition.Changed);
        service.RetrieveMultipleCount.Should().Be(1);
        service.RetrieveCount.Should().Be(2);
        service.UpdateCount.Should().Be(1);
        service.ExecuteCount.Should().Be(0);
        service.DisposeCount.Should().Be(1);
    }

    /// <summary>
    /// 驗證 change-race-leader 只更新 race leader 欄位，並保留其餘五欄 baseline。
    /// </summary>
    [Fact]
    public async Task Created_client_updates_only_the_race_leader_for_small_group_race_mode()
    {
        var listId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var oldRaceLeaderId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var newRaceLeaderId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var retrieveStep = 0;
        var service = new FakeOrganizationService(
            OrganizationId,
            update: entity =>
            {
                entity.LogicalName.Should().Be("list");
                entity.Id.Should().Be(listId);
                entity.Attributes.Should().BeEquivalentTo(new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["new_contact_race_leager_list"] = new EntityReference("contact", newRaceLeaderId)
                });
            },
            retrieve: (entityName, id, columnSet) =>
            {
                entityName.Should().Be("list");
                id.Should().Be(listId);
                columnSet.Columns.Should().Equal(SmallGroupFixedFieldNames);
                retrieveStep++;
                return new Entity("list", listId)
                {
                    ["new_contact_list_arealeader"] = new EntityReference("contact", oldRaceLeaderId),
                    ["new_area_name"] = "area",
                    ["new_contact_race_leager_list"] = new EntityReference(
                        "contact",
                        retrieveStep == 1 ? oldRaceLeaderId : newRaceLeaderId),
                    ["new_contact_list_co_arealeader"] = new EntityReference("contact", oldRaceLeaderId),
                    ["new_contact_co_race_leager_list"] = new EntityReference("contact", oldRaceLeaderId),
                    ["new_contact_list_vice_family_leader"] = new EntityReference("contact", oldRaceLeaderId)
                };
            });
        var factory = new OnPremiseData8ConnectorClientFactory(CreateConnectionSettings(), _ => service);
        var operation = new ConnectorOperation
        {
            OperationId = OperationIds.ListManagementSmallGroupUpdateFields,
            WorkloadSubjectId = "test",
            DeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(5),
            Parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["listId"] = listId,
                ["mode"] = "change-race-leader",
                ["targetLeaderContactId"] = newRaceLeaderId
            }
        };

        ConnectorOperationResult result;
        await using (var client = await factory.CreateAsync(CreateProfile(), CancellationToken.None))
        {
            result = await client.ExecuteAsync(operation, CancellationToken.None);
        }

        result.Data!.SmallGroupFixedFieldsMutation!.Disposition.Should().Be(P72ControlledMutationDisposition.Changed);
        result.Data.SmallGroupFixedFieldsMutation.CorrelationCategory
            .Should().Be(P72ControlledMutationCorrelationCategory.ReadBackConfirmed);
        service.RetrieveCount.Should().Be(2);
        service.RetrieveMultipleCount.Should().Be(0);
        service.UpdateCount.Should().Be(1);
        service.ExecuteCount.Should().Be(0);
        service.DisposeCount.Should().Be(1);
    }

    /// <summary>
    /// 驗證 change-race-leader 在 projection 已符合時不會送出 update。
    /// </summary>
    [Fact]
    public async Task Created_client_returns_no_change_for_an_already_matching_small_group_race_leader()
    {
        var listId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var raceLeaderId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var service = new FakeOrganizationService(
            OrganizationId,
            update: _ => throw new InvalidOperationException("No-change must not update the list."),
            retrieve: (entityName, id, columnSet) =>
            {
                entityName.Should().Be("list");
                id.Should().Be(listId);
                columnSet.Columns.Should().Equal(SmallGroupFixedFieldNames);
                return new Entity("list", listId)
                {
                    ["new_contact_race_leager_list"] = new EntityReference("contact", raceLeaderId)
                };
            });
        var factory = new OnPremiseData8ConnectorClientFactory(CreateConnectionSettings(), _ => service);
        var operation = new ConnectorOperation
        {
            OperationId = OperationIds.ListManagementSmallGroupUpdateFields,
            WorkloadSubjectId = "test",
            DeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(5),
            Parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["listId"] = listId,
                ["mode"] = "change-race-leader",
                ["targetLeaderContactId"] = raceLeaderId
            }
        };

        ConnectorOperationResult result;
        await using (var client = await factory.CreateAsync(CreateProfile(), CancellationToken.None))
        {
            result = await client.ExecuteAsync(operation, CancellationToken.None);
        }

        result.Data!.SmallGroupFixedFieldsMutation!.Disposition.Should().Be(P72ControlledMutationDisposition.NoChange);
        result.Data.SmallGroupFixedFieldsMutation.CorrelationCategory
            .Should().Be(P72ControlledMutationCorrelationCategory.NoDispatch);
        service.RetrieveCount.Should().Be(1);
        service.UpdateCount.Should().Be(0);
        service.ExecuteCount.Should().Be(0);
        service.DisposeCount.Should().Be(1);
    }

    /// <summary>
    /// 保護 contact owner assignment 先驗證 active systemuser、讀取 baseline owner、執行一次 AssignRequest 並
    /// read-back ownerid。故障注入是 connector 尚未接線；決定性斷言是不接受 team/任意 entity，且結果不包含
    /// contact/owner identity、SDK response、credential、session 或 transport detail。
    /// </summary>
    [Fact]
    public async Task Created_client_assigns_contact_to_an_active_system_user_and_confirms_owner()
    {
        var contactId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var originalOwnerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var targetOwnerId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var contactReadStep = 0;
        var service = new FakeOrganizationService(
            OrganizationId,
            retrieve: (entityName, id, columnSet) =>
            {
                if (entityName == "systemuser")
                {
                    id.Should().Be(targetOwnerId);
                    columnSet.Columns.Should().Equal("isdisabled");
                    return new Entity("systemuser", targetOwnerId) { ["isdisabled"] = false };
                }

                entityName.Should().Be("contact");
                id.Should().Be(contactId);
                columnSet.Columns.Should().Equal("ownerid");
                contactReadStep++;
                var ownerId = contactReadStep == 1 ? originalOwnerId : targetOwnerId;
                return new Entity("contact", contactId)
                {
                    ["ownerid"] = new EntityReference("systemuser", ownerId)
                };
            },
            execute: request =>
            {
                var assign = request.Should().BeOfType<AssignRequest>().Subject;
                assign.Target.Should().Be(new EntityReference("contact", contactId));
                assign.Assignee.Should().Be(new EntityReference("systemuser", targetOwnerId));
                return new OrganizationResponse();
            });
        var factory = new OnPremiseData8ConnectorClientFactory(CreateConnectionSettings(), _ => service);
        var operation = new ConnectorOperation
        {
            OperationId = OperationIds.ContactAssignOwner,
            WorkloadSubjectId = "test",
            DeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(5),
            Parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["contactId"] = contactId,
                ["ownerSystemUserId"] = targetOwnerId
            }
        };

        ConnectorOperationResult result;
        await using (var client = await factory.CreateAsync(CreateProfile(), CancellationToken.None))
        {
            result = await client.ExecuteAsync(operation, CancellationToken.None);
        }

        result.Data!.ResponseKind.Should().Be(OperationResponseKind.ContactOwnerAssignment);
        result.Data.ContactOwnerAssignment!.Disposition.Should().Be(P72ControlledMutationDisposition.Changed);
        service.RetrieveCount.Should().Be(3);
        service.ExecuteCount.Should().Be(1);
        service.UpdateCount.Should().Be(0);
        service.DisposeCount.Should().Be(1);
    }

    /// <summary>
    /// 驗證 contact 已由目標 active systemuser 擁有時，Assign action 不會被重送。
    /// </summary>
    [Fact]
    public async Task Created_client_returns_no_change_when_contact_owner_already_matches()
    {
        var contactId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var ownerId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var service = new FakeOrganizationService(
            OrganizationId,
            retrieve: (entityName, id, columnSet) =>
            {
                if (entityName == "systemuser")
                {
                    id.Should().Be(ownerId);
                    columnSet.Columns.Should().Equal("isdisabled");
                    return new Entity("systemuser", ownerId) { ["isdisabled"] = false };
                }

                entityName.Should().Be("contact");
                id.Should().Be(contactId);
                columnSet.Columns.Should().Equal("ownerid");
                return new Entity("contact", contactId)
                {
                    ["ownerid"] = new EntityReference("systemuser", ownerId)
                };
            },
            execute: _ => throw new InvalidOperationException("No-change must not dispatch Assign."));
        var factory = new OnPremiseData8ConnectorClientFactory(CreateConnectionSettings(), _ => service);
        var operation = new ConnectorOperation
        {
            OperationId = OperationIds.ContactAssignOwner,
            WorkloadSubjectId = "test",
            DeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(5),
            Parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["contactId"] = contactId,
                ["ownerSystemUserId"] = ownerId
            }
        };

        ConnectorOperationResult result;
        await using (var client = await factory.CreateAsync(CreateProfile(), CancellationToken.None))
        {
            result = await client.ExecuteAsync(operation, CancellationToken.None);
        }

        result.Data!.ContactOwnerAssignment!.Disposition.Should().Be(P72ControlledMutationDisposition.NoChange);
        result.Data.ContactOwnerAssignment.CorrelationCategory
            .Should().Be(P72ControlledMutationCorrelationCategory.NoDispatch);
        service.RetrieveCount.Should().Be(2);
        service.ExecuteCount.Should().Be(0);
        service.UpdateCount.Should().Be(0);
        service.DisposeCount.Should().Be(1);
    }

    /// <summary>驗證 list-member pre/post read 只使用 connector-owned QueryExpression 與兩個固定 GUID filter。</summary>
    /// <summary>
    /// 驗證 transfer composite 在省略 source list、但指定 optional owner 時，仍會以單一 graph 完成 owner
    /// assignment，並在最後 read-back 所有元件；任何步驟都不會自動重送。
    /// </summary>
    [Fact]
    public async Task Created_client_executes_transfer_with_optional_owner_without_source_list()
    {
        var contactId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var targetListId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var weeklyReportId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var presentRecordId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var oldOwnerId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var targetOwnerId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var weekStartDate = new DateTimeOffset(2026, 8, 9, 0, 0, 0, TimeSpan.Zero);
        var targetMembershipReads = 0;
        var presentRecordReads = 0;
        var primaryListReads = 0;
        var ownerReads = 0;
        var service = new FakeOrganizationService(
            OrganizationId,
            retrieveMultiple: query =>
            {
                var expression = query.Should().BeOfType<QueryExpression>().Subject;
                switch (expression.EntityName)
                {
                    case "listmember":
                        expression.Criteria.Conditions.Should().Contain(condition =>
                            condition.AttributeName == "listid" && condition.Values.Contains(targetListId));
                        return targetMembershipReads++ == 0
                            ? new EntityCollection()
                            : new EntityCollection([new Entity("listmember")
                            {
                                ["listid"] = targetListId,
                                ["entityid"] = contactId
                            }]);
                    case "new_group_present_weekly_report":
                        return new EntityCollection([new Entity("new_group_present_weekly_report", weeklyReportId)]);
                    case "new_present_record":
                        presentRecordReads++;
                        return presentRecordReads == 1
                            ? new EntityCollection()
                            : new EntityCollection([new Entity("new_present_record", presentRecordId)
                            {
                                ["new_group_present_weekly_report_prese"] = new EntityReference(
                                    "new_group_present_weekly_report", weeklyReportId),
                                ["new_contact_new_present_record"] = new EntityReference("contact", contactId),
                                ["new_list_new_present_record"] = new EntityReference("list", targetListId),
                                ["new_sunday_date"] = weekStartDate.UtcDateTime
                            }]);
                    default:
                        throw new InvalidOperationException("Unexpected transfer query.");
                }
            },
            retrieve: (entityName, id, columnSet) =>
            {
                if (entityName == "systemuser")
                {
                    id.Should().Be(targetOwnerId);
                    columnSet.Columns.Should().Equal("isdisabled");
                    return new Entity("systemuser", targetOwnerId) { ["isdisabled"] = false };
                }

                entityName.Should().Be("contact");
                id.Should().Be(contactId);
                if (columnSet.Columns.SequenceEqual(["new_cell_list_contact"]))
                {
                    return primaryListReads++ == 0
                        ? new Entity("contact", contactId)
                        : new Entity("contact", contactId)
                        {
                            ["new_cell_list_contact"] = new EntityReference("list", targetListId)
                        };
                }

                columnSet.Columns.Should().Equal("ownerid");
                return new Entity("contact", contactId)
                {
                    ["ownerid"] = new EntityReference(
                        "systemuser",
                        ownerReads++ == 0 ? oldOwnerId : targetOwnerId)
                };
            },
            update: entity =>
            {
                entity.LogicalName.Should().Be("contact");
                entity.Id.Should().Be(contactId);
                entity.Attributes.Should().BeEquivalentTo(new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["new_cell_list_contact"] = new EntityReference("list", targetListId)
                });
            },
            execute: request =>
            {
                switch (request)
                {
                    case AddListMembersListRequest add:
                        add.ListId.Should().Be(targetListId);
                        add.MemberIds.Should().Equal(contactId);
                        break;
                    case AssignRequest assign:
                        assign.Target.Should().Be(new EntityReference("contact", contactId));
                        assign.Assignee.Should().Be(new EntityReference("systemuser", targetOwnerId));
                        break;
                    default:
                        throw new InvalidOperationException("Unexpected transfer request.");
                }

                return new OrganizationResponse();
            },
            create: entity =>
            {
                entity.LogicalName.Should().Be("new_present_record");
                return presentRecordId;
            });
        var factory = new OnPremiseData8ConnectorClientFactory(CreateConnectionSettings(), _ => service);
        var operation = new ConnectorOperation
        {
            OperationId = OperationIds.NewPersonContactTransferBetweenLists,
            WorkloadSubjectId = "test",
            DeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(5),
            Parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["contactId"] = contactId,
                ["targetListId"] = targetListId,
                ["weekStartDate"] = weekStartDate,
                ["ownerSystemUserId"] = targetOwnerId
            }
        };

        ConnectorOperationResult result;
        await using (var client = await factory.CreateAsync(CreateProfile(), CancellationToken.None))
        {
            result = await client.ExecuteAsync(operation, CancellationToken.None);
        }

        result.Data!.ContactListTransfer!.Disposition.Should().Be(P72ControlledMutationDisposition.Changed);
        result.Data.ContactListTransfer.CorrelationCategory
            .Should().Be(P72ControlledMutationCorrelationCategory.ReadBackConfirmed);
        service.ExecuteCount.Should().Be(2);
        service.CreateCount.Should().Be(1);
        service.UpdateCount.Should().Be(1);
        service.RetrieveMultipleCount.Should().Be(7);
        service.RetrieveCount.Should().Be(5);
        service.DisposeCount.Should().Be(1);
    }

    /// <summary>
    /// 驗證 transfer 在 source 與 target 同時已有成員的 partial state 會 fail closed，且在任何 mutation 前停止。
    /// </summary>
    [Fact]
    public async Task Created_client_rejects_a_partial_transfer_graph_before_dispatch()
    {
        var contactId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var sourceListId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var targetListId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var weeklyReportId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var weekStartDate = new DateTimeOffset(2026, 8, 9, 0, 0, 0, TimeSpan.Zero);
        var service = new FakeOrganizationService(
            OrganizationId,
            retrieveMultiple: query =>
            {
                var expression = query.Should().BeOfType<QueryExpression>().Subject;
                if (expression.EntityName == "listmember")
                {
                    var listId = expression.Criteria.Conditions
                        .Single(condition => condition.AttributeName == "listid")
                        .Values.Should().ContainSingle().Which.Should().BeOfType<Guid>().Subject;
                    new[] { sourceListId, targetListId }.Should().Contain(listId);
                    return new EntityCollection([new Entity("listmember")
                    {
                        ["listid"] = listId,
                        ["entityid"] = contactId
                    }]);
                }

                if (expression.EntityName == "new_group_present_weekly_report")
                {
                    return new EntityCollection([new Entity("new_group_present_weekly_report", weeklyReportId)]);
                }

                expression.EntityName.Should().Be("new_present_record");
                return new EntityCollection();
            },
            retrieve: (entityName, id, columnSet) =>
            {
                entityName.Should().Be("contact");
                id.Should().Be(contactId);
                columnSet.Columns.Should().Equal("new_cell_list_contact");
                return new Entity("contact", contactId)
                {
                    ["new_cell_list_contact"] = new EntityReference("list", sourceListId)
                };
            },
            execute: _ => throw new InvalidOperationException("Partial state must not dispatch."),
            update: _ => throw new InvalidOperationException("Partial state must not update."),
            create: _ => throw new InvalidOperationException("Partial state must not create."));
        var factory = new OnPremiseData8ConnectorClientFactory(CreateConnectionSettings(), _ => service);
        var operation = new ConnectorOperation
        {
            OperationId = OperationIds.NewPersonContactTransferBetweenLists,
            WorkloadSubjectId = "test",
            DeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(5),
            Parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["contactId"] = contactId,
                ["sourceListId"] = sourceListId,
                ["targetListId"] = targetListId,
                ["weekStartDate"] = weekStartDate
            }
        };

        var action = async () =>
        {
            await using var client = await factory.CreateAsync(CreateProfile(), CancellationToken.None);
            await client.ExecuteAsync(operation, CancellationToken.None);
        };

        await action.Should().ThrowAsync<InvalidOperationException>();
        service.ExecuteCount.Should().Be(0);
        service.CreateCount.Should().Be(0);
        service.UpdateCount.Should().Be(0);
        service.RetrieveMultipleCount.Should().Be(4);
        service.RetrieveCount.Should().Be(1);
        service.DisposeCount.Should().Be(1);
    }

    /// <summary>
    /// CE 8.2 對 Slice C 五個 Data8 write/action operation 一律 fail closed；測試確保不會因版本不支援而觸發 CRM。
    /// </summary>
    [Theory]
    [InlineData("add")]
    [InlineData("remove")]
    [InlineData("small-group")]
    [InlineData("owner")]
    [InlineData("transfer")]
    public async Task Created_client_fails_closed_for_slice_c_operations_on_ce82(string operationShape)
    {
        var contactId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var listId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var ownerId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var operation = operationShape switch
        {
            "add" => new ConnectorOperation
            {
                OperationId = OperationIds.ListMembersAddMany,
                WorkloadSubjectId = "test",
                DeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(5),
                Parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["listId"] = listId,
                    ["memberIds"] = new[] { contactId }
                }
            },
            "remove" => new ConnectorOperation
            {
                OperationId = OperationIds.ListMembersRemoveOne,
                WorkloadSubjectId = "test",
                DeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(5),
                Parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["listId"] = listId,
                    ["memberId"] = contactId
                }
            },
            "small-group" => new ConnectorOperation
            {
                OperationId = OperationIds.ListManagementSmallGroupUpdateFields,
                WorkloadSubjectId = "test",
                DeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(5),
                Parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["listId"] = listId,
                    ["mode"] = "change-race-leader",
                    ["targetLeaderContactId"] = contactId
                }
            },
            "owner" => new ConnectorOperation
            {
                OperationId = OperationIds.ContactAssignOwner,
                WorkloadSubjectId = "test",
                DeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(5),
                Parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["contactId"] = contactId,
                    ["ownerSystemUserId"] = ownerId
                }
            },
            "transfer" => new ConnectorOperation
            {
                OperationId = OperationIds.NewPersonContactTransferBetweenLists,
                WorkloadSubjectId = "test",
                DeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(5),
                Parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["contactId"] = contactId,
                    ["targetListId"] = listId,
                    ["weekStartDate"] = new DateTimeOffset(2026, 8, 9, 0, 0, 0, TimeSpan.Zero)
                }
            },
            _ => throw new ArgumentOutOfRangeException(nameof(operationShape))
        };
        var service = new FakeOrganizationService(
            OrganizationId,
            retrieveMultiple: _ => throw new InvalidOperationException("CE 8.2 must fail before CRM query."),
            retrieve: (_, _, _) => throw new InvalidOperationException("CE 8.2 must fail before CRM read."),
            execute: _ => throw new InvalidOperationException("CE 8.2 must fail before CRM action."),
            update: _ => throw new InvalidOperationException("CE 8.2 must fail before CRM update."),
            create: _ => throw new InvalidOperationException("CE 8.2 must fail before CRM create."));
        var factory = new OnPremiseData8ConnectorClientFactory(CreateConnectionSettings(), _ => service);

        var action = async () =>
        {
            await using var client = await factory.CreateAsync(
                CreateProfile() with { CeVersion = CeVersion.Ce82 },
                CancellationToken.None);
            await client.ExecuteAsync(operation, CancellationToken.None);
        };

        await action.Should().ThrowAsync<InvalidOperationException>();
        service.RetrieveMultipleCount.Should().Be(0);
        service.RetrieveCount.Should().Be(0);
        service.ExecuteCount.Should().Be(0);
        service.UpdateCount.Should().Be(0);
        service.CreateCount.Should().Be(0);
        service.DisposeCount.Should().Be(1);
    }

    private static void AssertFixedMembershipQuery(QueryBase query, Guid listId, IReadOnlyList<Guid> memberIds)
    {
        var expression = query.Should().BeOfType<QueryExpression>().Subject;
        expression.EntityName.Should().Be("listmember");
        expression.ColumnSet.Columns.Should().Equal("listid", "entityid");
        var listCondition = expression.Criteria.Conditions.Single(condition =>
            condition.AttributeName == "listid" && condition.Operator == ConditionOperator.Equal);
        listCondition.Values.Should().ContainSingle().Which.Should().Be(listId);
        var memberCondition = expression.Criteria.Conditions.Single(condition =>
            condition.AttributeName == "entityid" && condition.Operator == ConditionOperator.In);
        memberCondition.Values.Cast<Guid>().OrderBy(value => value)
            .Should().Equal(memberIds.OrderBy(value => value));
    }

    private static readonly string[] SmallGroupFixedFieldNames =
    [
        "new_contact_list_arealeader",
        "new_area_name",
        "new_contact_race_leager_list",
        "new_contact_list_co_arealeader",
        "new_contact_co_race_leager_list",
        "new_contact_list_vice_family_leader"
    ];

    /// <summary>
    /// 保護 P7.2 的 Data8 contact basic-info capability 只能建立一個固定 <c>contact</c> patch，且完成後只讀回
    /// <c>mobilephone</c> 與 <c>address2_line1</c>。故障注入是未實作的 write operation；決定性斷言是 service
    /// 恰好執行一次 Update 與一次 allowlisted Retrieve，回應僅公開 changed/read-back-confirmed，沒有 Entity、
    /// contact ID、欄位值、endpoint、credential、token、cookie 或原始 SDK response。測試替身不開啟 CRM、WCF、
    /// 網路、timer 或背景工作，並在 client dispose 後確認唯一 service owner 已釋放。
    /// </summary>
    [Fact]
    public async Task Created_client_executes_a_fixed_contact_basic_info_update_then_confirms_only_allowlisted_fields()
    {
        var contactId = Guid.Parse("aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb");
        var service = new FakeOrganizationService(
            OrganizationId,
            update: entity =>
            {
                entity.LogicalName.Should().Be("contact");
                entity.Id.Should().Be(contactId);
                entity.Attributes.Should().BeEquivalentTo(
                    new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["mobilephone"] = "0900-000-001",
                        ["address2_line1"] = "P7.2 fixture address"
                    });
            },
            retrieve: (entityName, id, columnSet) =>
            {
                entityName.Should().Be("contact");
                id.Should().Be(contactId);
                columnSet.AllColumns.Should().BeFalse();
                columnSet.Columns.Should().Equal("mobilephone", "address2_line1");
                return new Entity("contact", contactId)
                {
                    ["mobilephone"] = "0900-000-001",
                    ["address2_line1"] = "P7.2 fixture address"
                };
            });
        var factory = new OnPremiseData8ConnectorClientFactory(CreateConnectionSettings(), _ => service);
        var operation = new ConnectorOperation
        {
            OperationId = OperationIds.MemberInfoContactUpdateBasicInfo,
            WorkloadSubjectId = "test",
            DeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(5),
            Parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["contactId"] = contactId,
                ["phone"] = "0900-000-001",
                ["address"] = "P7.2 fixture address"
            }
        };

        ConnectorOperationResult result;
        await using (var client = await factory.CreateAsync(CreateProfile(), CancellationToken.None))
        {
            result = await client.ExecuteAsync(operation, CancellationToken.None);
        }

        result.Succeeded.Should().BeTrue();
        result.Data!.ResponseKind.Should().Be(OperationResponseKind.ContactBasicInfoUpdate);
        result.Data.ContactBasicInfoUpdate!.Disposition.Should().Be(ContactBasicInfoUpdateDisposition.Changed);
        result.Data.ContactBasicInfoUpdate.CorrelationCategory
            .Should().Be(ContactBasicInfoUpdateCorrelationCategory.ReadBackConfirmed);
        service.UpdateCount.Should().Be(1);
        service.RetrieveCount.Should().Be(1);
        service.RetrieveMultipleCount.Should().Be(0);
        service.ExecuteCount.Should().Be(0);
        service.DisposeCount.Should().Be(1);
    }

    /// <summary>
    /// 保護 B1 connector 只更新三個固定 LINE profile 欄位，且 clear 會寫入 null、preserve 不會寫入欄位。
    /// 故障注入是目前 client 尚未擁有 B1 template；決定性斷言是一次 Update 後用固定三欄 ColumnSet read-back，
    /// 回應不含 contact、URL、文字、Entity 或 SDK graph，並在 client scope 結束時唯一 service owner 正好釋放一次。
    /// 測試完全離線，不建立 LINE／CRM 網路、credential、session、timer、stream 或背景工作。
    /// </summary>
    [Fact]
    public async Task Created_client_updates_only_fixed_line_profile_fields_and_confirms_bounded_read_back()
    {
        var contactId = Guid.Parse("bbbbbbbb-1111-2222-3333-cccccccccccc");
        var service = new FakeOrganizationService(
            OrganizationId,
            update: entity =>
            {
                entity.LogicalName.Should().Be("contact");
                entity.Id.Should().Be(contactId);
                entity.Attributes.Should().BeEquivalentTo(new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["new_line_picture_url"] = "https://profile.line-scdn.net/p7.2-test",
                    ["new_line_status_message"] = null,
                    ["new_line_displayname"] = "測試顯示名稱"
                });
            },
            retrieve: (entityName, id, columnSet) =>
            {
                entityName.Should().Be("contact");
                id.Should().Be(contactId);
                columnSet.AllColumns.Should().BeFalse();
                columnSet.Columns.Should().Equal(
                    "new_line_picture_url",
                    "new_line_status_message",
                    "new_line_displayname");
                return new Entity("contact", contactId)
                {
                    ["new_line_picture_url"] = "https://profile.line-scdn.net/p7.2-test",
                    ["new_line_status_message"] = null,
                    ["new_line_displayname"] = "測試顯示名稱"
                };
            });
        var factory = new OnPremiseData8ConnectorClientFactory(CreateConnectionSettings(), _ => service);
        var operation = new ConnectorOperation
        {
            OperationId = OperationIds.MemberInfoContactUpdateLineProfile,
            WorkloadSubjectId = "test",
            DeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(5),
            EstimatedBytes = 512,
            Parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["contactId"] = contactId,
                ["pictureMode"] = "set",
                ["pictureUrl"] = "https://profile.line-scdn.net/p7.2-test",
                ["statusMode"] = "clear",
                ["displayNameMode"] = "set",
                ["displayName"] = "測試顯示名稱"
            }
        };

        ConnectorOperationResult result;
        await using (var client = await factory.CreateAsync(CreateProfile(), CancellationToken.None))
        {
            result = await client.ExecuteAsync(operation, CancellationToken.None);
        }

        result.Succeeded.Should().BeTrue();
        result.Data!.ResponseKind.Should().Be(OperationResponseKind.ContactLineProfileUpdate);
        result.Data.ContactLineProfileUpdate!.Disposition.Should().Be(ContactLineProfileUpdateDisposition.Changed);
        result.Data.ContactLineProfileUpdate.CorrelationCategory
            .Should().Be(ContactLineProfileUpdateCorrelationCategory.ReadBackConfirmed);
        service.UpdateCount.Should().Be(1);
        service.RetrieveCount.Should().Be(1);
        service.RetrieveMultipleCount.Should().Be(0);
        service.ExecuteCount.Should().Be(0);
        service.DisposeCount.Should().Be(1);
    }

    /// <summary>
    /// 保護 B1 只有三個 allowlisted 欄位全部讀回相符時才能回傳成功。故障注入令 status read-back 與剛完成的
    /// mutation 不同；決定性斷言是 connector 回報不確定寫入結果、沒有建立成功 envelope，且 client scope
    /// 結束時唯一 service owner 仍 Dispose 一次。這個離線替身不配置 CE 連線或保存欄位基線。
    /// </summary>
    [Fact]
    public async Task Created_client_rejects_a_line_profile_read_back_mismatch_and_disposes_service()
    {
        var contactId = Guid.Parse("bbbbbbbb-aaaa-2222-3333-cccccccccccc");
        var service = new FakeOrganizationService(
            OrganizationId,
            update: _ => { },
            retrieve: (_, _, _) => new Entity("contact", contactId)
            {
                ["new_line_picture_url"] = "https://profile.line-scdn.net/p7.2-test",
                ["new_line_status_message"] = "unexpected-status",
                ["new_line_displayname"] = "測試顯示名稱"
            });
        var factory = new OnPremiseData8ConnectorClientFactory(CreateConnectionSettings(), _ => service);
        var operation = new ConnectorOperation
        {
            OperationId = OperationIds.MemberInfoContactUpdateLineProfile,
            WorkloadSubjectId = "test",
            DeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(5),
            EstimatedBytes = 512,
            Parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["contactId"] = contactId,
                ["pictureMode"] = "set",
                ["pictureUrl"] = "https://profile.line-scdn.net/p7.2-test",
                ["statusMode"] = "clear",
                ["displayNameMode"] = "set",
                ["displayName"] = "測試顯示名稱"
            }
        };

        var action = async () =>
        {
            await using var client = await factory.CreateAsync(CreateProfile(), CancellationToken.None);
            await client.ExecuteAsync(operation, CancellationToken.None);
        };

        await action.Should().ThrowAsync<InvalidOperationException>();
        service.UpdateCount.Should().Be(1);
        service.RetrieveCount.Should().Be(1);
        service.DisposeCount.Should().Be(1);
    }

    /// <summary>
    /// 保護 B2 aggregate 由 connector 自行解析 contact.customertypecode metadata、固定的 active app-named
    /// 小組清單與 membership，再建立不可由 caller 改寫的 aggregate FetchXML。故障注入是目前尚未存在的 B2
    /// connector template；決定性斷言是 caller 只供應 search，metadata 找到唯一「結案」與 label match，
    /// grouped contact 以 not-in 排除，結果只投影 bounded raw value/count。所有 SDK graph 僅活在同步 fake callback，
    /// client scope 結束後 service 恰好 Dispose 一次，沒有 cache、session、timer、stream 或 background task。
    /// </summary>
    [Fact]
    public async Task Created_client_builds_server_owned_ungrouped_commitment_aggregate_and_projects_safe_counts()
    {
        var listId = Guid.Parse("cccccccc-1111-2222-3333-dddddddddddd");
        var groupedContactId = Guid.Parse("dddddddd-1111-2222-3333-eeeeeeeeeeee");
        var queryStep = 0;
        var service = new FakeOrganizationService(
            OrganizationId,
            retrieveMultiple: query =>
            {
                queryStep++;
                if (queryStep == 1)
                {
                    var lists = query.Should().BeOfType<QueryExpression>().Subject;
                    lists.EntityName.Should().Be("list");
                    lists.ColumnSet.Columns.Should().Equal("listid");
                    lists.Criteria.Conditions.Select(condition => new { condition.AttributeName, condition.Operator })
                        .Should().Equal(
                            new { AttributeName = "statecode", Operator = ConditionOperator.Equal },
                            new { AttributeName = "purpose", Operator = ConditionOperator.Equal },
                            new { AttributeName = "new_app_named", Operator = ConditionOperator.Equal });
                    lists.Criteria.Conditions[1].Values.Should().ContainSingle().Which.Should().Be("小組名單");
                    lists.Criteria.Conditions[2].Values.Should().ContainSingle().Which.Should().Be(true);
                    return new EntityCollection([new Entity("list", listId) { ["listid"] = listId }]);
                }

                if (queryStep == 2)
                {
                    var memberships = query.Should().BeOfType<QueryExpression>().Subject;
                    memberships.EntityName.Should().Be("listmember");
                    memberships.ColumnSet.Columns.Should().Equal("listid", "entityid");
                    memberships.Criteria.Conditions.Should().ContainSingle(condition =>
                        condition.AttributeName == "listid" && condition.Operator == ConditionOperator.In);
                    memberships.Criteria.Conditions[0].Values.Should().ContainSingle().Which.Should().Be(listId);
                    memberships.LinkEntities.Should().ContainSingle();
                    var contactLink = memberships.LinkEntities[0];
                    contactLink.LinkToEntityName.Should().Be("contact");
                    contactLink.LinkCriteria.Conditions.Should().ContainSingle(condition =>
                        condition.AttributeName == "statecode" && condition.Operator == ConditionOperator.Equal);
                    contactLink.LinkCriteria.Filters.Should().ContainSingle();
                    return new EntityCollection(
                    [
                        new Entity("listmember")
                        {
                            ["listid"] = listId,
                            ["entityid"] = groupedContactId
                        }
                    ]);
                }

                var aggregate = query.Should().BeOfType<FetchExpression>().Subject;
                var document = XDocument.Parse(aggregate.Query);
                document.Root!.Attribute("aggregate")!.Value.Should().Be("true");
                document.Descendants("entity").Should().ContainSingle(node =>
                    HasAttributeValue(node, "name", "contact"));
                document.Descendants("attribute").Should().Contain(node =>
                    HasAttributeValue(node, "name", "customertypecode") &&
                    HasAttributeValue(node, "alias", "commitmenttype") &&
                    HasAttributeValue(node, "groupby", "true"));
                document.Descendants("condition").Should().Contain(node =>
                    HasAttributeValue(node, "attribute", "customertypecode") &&
                    HasAttributeValue(node, "operator", "not-null"));
                document.Descendants("condition").Should().Contain(node =>
                    HasAttributeValue(node, "attribute", "customertypecode") &&
                    HasAttributeValue(node, "operator", "ne") &&
                    HasAttributeValue(node, "value", "100000011"));
                document.Descendants("condition").Should().Contain(node =>
                    HasAttributeValue(node, "attribute", "contactid") &&
                    HasAttributeValue(node, "operator", "not-in") &&
                    node.Elements("value").Single().Value == groupedContactId.ToString("D"));
                document.Descendants("condition").Should().Contain(node =>
                    HasAttributeValue(node, "attribute", "fullname") &&
                    HasAttributeValue(node, "operator", "like") &&
                    HasAttributeValue(node, "value", "%會友%"));
                document.Descendants("condition").Should().Contain(node =>
                    HasAttributeValue(node, "attribute", "customertypecode") &&
                    HasAttributeValue(node, "operator", "in") &&
                    node.Elements("value").Single().Value == "100000001");
                return new EntityCollection(
                [
                    new Entity("contact")
                    {
                        ["commitmenttype"] = new AliasedValue(
                            "contact",
                            "customertypecode",
                            new OptionSetValue(100000001)),
                        ["rowcount"] = new AliasedValue("contact", "contactid", 5)
                    }
                ]);
            },
            execute: request =>
            {
                var metadataRequest = request.Should().BeOfType<RetrieveAttributeRequest>().Subject;
                metadataRequest.EntityLogicalName.Should().Be("contact");
                metadataRequest.LogicalName.Should().Be("customertypecode");
                metadataRequest.RetrieveAsIfPublished.Should().BeTrue();
                return CreateCustomerTypeMetadataResponse();
            });
        var factory = new OnPremiseData8ConnectorClientFactory(CreateConnectionSettings(), _ => service);
        var operation = new ConnectorOperation
        {
            OperationId = OperationIds.MemberInfoContactCountUngroupedCommitment,
            WorkloadSubjectId = "test",
            DeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(5),
            EstimatedBytes = 320,
            Parameters = new Dictionary<string, object?>(StringComparer.Ordinal) { ["search"] = "會友" }
        };

        ConnectorOperationResult result;
        await using (var client = await factory.CreateAsync(CreateProfile(), CancellationToken.None))
        {
            result = await client.ExecuteAsync(operation, CancellationToken.None);
        }

        result.Succeeded.Should().BeTrue();
        result.Data!.ResponseKind.Should().Be(OperationResponseKind.UngroupedCommitmentCounts);
        result.Data.UngroupedCommitmentCounts.Should().ContainSingle()
            .Which.Should().Be(new UngroupedCommitmentCountRecord { Value = 100000001, Count = 5 });
        queryStep.Should().Be(3);
        service.ExecuteCount.Should().Be(1);
        service.RetrieveMultipleCount.Should().Be(3);
        service.UpdateCount.Should().Be(0);
        service.RetrieveCount.Should().Be(0);
        service.DisposeCount.Should().Be(1);
    }

    /// <summary>
    /// 保護 B2 不得把分頁未完成或 logical entity 錯配的 aggregate row 當成完整計數。故障注入分別令
    /// <see cref="EntityCollection.MoreRecords"/> 為 true，或令 alias row 宣告為 account；決定性斷言是
    /// connector 在建立成功 envelope 前失敗、沒有回傳 partial count，且唯一 Data8 service owner 仍釋放一次。
    /// 測試只使用同步記憶體替身，不建立 CE 連線、credential、session、timer、stream 或背景工作。
    /// </summary>
    /// <param name="aggregateHasMoreRecords">是否模擬尚有未讀 aggregate page。</param>
    /// <param name="aggregateEntityName">模擬 aggregate row 宣告的 logical entity name。</param>
    [Theory]
    [InlineData(true, "contact")]
    [InlineData(false, "account")]
    public async Task Created_client_rejects_incomplete_or_wrong_entity_ungrouped_aggregate(
        bool aggregateHasMoreRecords,
        string aggregateEntityName)
    {
        var queryStep = 0;
        var service = new FakeOrganizationService(
            OrganizationId,
            retrieveMultiple: query =>
            {
                queryStep++;
                if (queryStep == 1)
                {
                    query.Should().BeOfType<QueryExpression>().Which.EntityName.Should().Be("list");
                    return new EntityCollection();
                }

                query.Should().BeOfType<FetchExpression>();
                return new EntityCollection(
                [
                    new Entity(aggregateEntityName)
                    {
                        ["commitmenttype"] = new AliasedValue(
                            "contact",
                            "customertypecode",
                            new OptionSetValue(100000001)),
                        ["rowcount"] = new AliasedValue("contact", "contactid", 1)
                    }
                ])
                {
                    MoreRecords = aggregateHasMoreRecords,
                    PagingCookie = aggregateHasMoreRecords ? "opaque-test-cookie" : null
                };
            },
            execute: _ => CreateCustomerTypeMetadataResponse());
        var factory = new OnPremiseData8ConnectorClientFactory(CreateConnectionSettings(), _ => service);
        var operation = new ConnectorOperation
        {
            OperationId = OperationIds.MemberInfoContactCountUngroupedCommitment,
            WorkloadSubjectId = "test",
            DeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(5),
            EstimatedBytes = 256,
            Parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
        };

        var action = async () =>
        {
            await using var client = await factory.CreateAsync(CreateProfile(), CancellationToken.None);
            await client.ExecuteAsync(operation, CancellationToken.None);
        };

        await action.Should().ThrowAsync<InvalidOperationException>();
        queryStep.Should().Be(2);
        service.ExecuteCount.Should().Be(1);
        service.RetrieveMultipleCount.Should().Be(2);
        service.DisposeCount.Should().Be(1);
    }

    /// <summary>
    /// 保護 B2 必須從 metadata 找到唯一 normalized「結案」OptionSet value。故障注入分別移除結案 label，
    /// 或加入第二個不同 value 的結案 label；決定性斷言是任何 list、membership 或 aggregate query 開始前
    /// fail closed，且唯一 service owner 仍釋放一次，不把 metadata 或 profile state 放入 cache／session。
    /// </summary>
    /// <param name="addTwoClosedValues">是否注入兩個相異的結案 OptionSet value。</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Created_client_rejects_missing_or_ambiguous_closed_status_metadata(bool addTwoClosedValues)
    {
        var options = new List<OptionMetadata>
        {
            new(new Label("01. 會友", 1028), 100000001)
        };
        if (addTwoClosedValues)
        {
            options.Add(new OptionMetadata(new Label("10. 結案", 1028), 100000011));
            options.Add(new OptionMetadata(new Label("11. 結案", 1028), 100000012));
        }

        var service = new FakeOrganizationService(
            OrganizationId,
            retrieveMultiple: _ => throw new InvalidOperationException("Metadata failure must precede CRM queries."),
            execute: _ => CreateCustomerTypeMetadataResponse(options));
        var factory = new OnPremiseData8ConnectorClientFactory(CreateConnectionSettings(), _ => service);
        var operation = new ConnectorOperation
        {
            OperationId = OperationIds.MemberInfoContactCountUngroupedCommitment,
            WorkloadSubjectId = "test",
            DeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(5),
            EstimatedBytes = 256,
            Parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
        };

        var action = async () =>
        {
            await using var client = await factory.CreateAsync(CreateProfile(), CancellationToken.None);
            await client.ExecuteAsync(operation, CancellationToken.None);
        };

        await action.Should().ThrowAsync<InvalidOperationException>();
        service.ExecuteCount.Should().Be(1);
        service.RetrieveMultipleCount.Should().Be(0);
        service.DisposeCount.Should().Be(1);
    }

    /// <summary>
    /// 建立 B2 測試專用的 bounded picklist metadata response。內容只含三個固定 OptionSet label/value，
    /// 不包含 endpoint、Organization、credential 或 contact data；回應只在 fake Execute callback 期間使用。
    /// </summary>
    private static RetrieveAttributeResponse CreateCustomerTypeMetadataResponse()
        => CreateCustomerTypeMetadataResponse(
        [
            new OptionMetadata(new Label("01. 會友", 1028), 100000001),
            new OptionMetadata(new Label("02. 新朋友", 1028), 100000002),
            new OptionMetadata(new Label("10. 結案", 1028), 100000011)
        ]);

    /// <summary>
    /// 以 caller 提供的有限 OptionMetadata 集合建立離線 RetrieveAttributeResponse。輸入只由測試擁有並立即
    /// 複製到 response；helper 不保存 metadata、不建立 CRM client，也不跨測試共用 mutable OptionSet state。
    /// </summary>
    /// <param name="options">本測試案例擁有的有限 OptionSet options。</param>
    private static RetrieveAttributeResponse CreateCustomerTypeMetadataResponse(
        IReadOnlyCollection<OptionMetadata> options)
    {
        var metadata = new PicklistAttributeMetadata
        {
            LogicalName = "customertypecode",
            OptionSet = new OptionSetMetadata()
        };
        foreach (var option in options)
        {
            metadata.OptionSet.Options.Add(option);
        }

        var response = new RetrieveAttributeResponse();
        response.Results["AttributeMetadata"] = metadata;
        return response;
    }

    /// <summary>以 expression-tree 相容方式比較 XML attribute；只讀測試 DOM，不配置外部資源。</summary>
    private static bool HasAttributeValue(XElement element, string attributeName, string expectedValue)
        => string.Equals(element.Attribute(attributeName)?.Value, expectedValue, StringComparison.Ordinal);

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
    /// Create、RetrieveMultiple／Dispose 的精確次數。它不開啟 channel、handle、timer 或 thread；所有未預期
    /// CRM 呼叫立即失敗，避免測試誤把 generic service 當成未受控通道。
    /// </summary>
    private sealed class FakeOrganizationService : IOrganizationService, IDisposable
    {
        private readonly Guid _organizationId;
        private readonly Func<QueryBase, EntityCollection>? _retrieveMultiple;
        private readonly Action<Entity>? _update;
        private readonly Func<string, Guid, ColumnSet, Entity>? _retrieve;
        private readonly Func<OrganizationRequest, OrganizationResponse>? _execute;
        private readonly Func<Entity, Guid>? _create;
        private int _executeCount;
        private int _createCount;
        private int _retrieveMultipleCount;
        private int _updateCount;
        private int _retrieveCount;
        private int _disposeCount;

        /// <summary>
        /// 建立只提供必要 CRM 回應的離線 service。Query callback 為 null 時仍只允許 WhoAmI；提供時也只接受
        /// 此測試明確檢查的 QueryExpression，不會形成可任意執行 CRM command 的替身。
        /// </summary>
        /// <param name="organizationId">WhoAmI 唯一回傳的非秘密組織 GUID。</param>
        /// <param name="retrieveMultiple">測試所有的固定查詢 callback；不保存真實資料庫、連線或 session。</param>
        public FakeOrganizationService(
            Guid organizationId,
            Func<QueryBase, EntityCollection>? retrieveMultiple = null,
            Action<Entity>? update = null,
            Func<string, Guid, ColumnSet, Entity>? retrieve = null,
            Func<OrganizationRequest, OrganizationResponse>? execute = null,
            Func<Entity, Guid>? create = null)
        {
            _organizationId = organizationId;
            _retrieveMultiple = retrieveMultiple;
            _update = update;
            _retrieve = retrieve;
            _execute = execute;
            _create = create;
        }

        public int ExecuteCount => Volatile.Read(ref _executeCount);

        /// <summary>取得 transfer fixture 建立唯一 present record 的次數；未注入 create callback 時立即 fail closed。</summary>
        public int CreateCount => Volatile.Read(ref _createCount);

        /// <summary>取得固定 QueryExpression 實際呼叫次數，供驗證沒有背景補送或未界定 paging。</summary>
        public int RetrieveMultipleCount => Volatile.Read(ref _retrieveMultipleCount);

        /// <summary>
        /// 取得已被允許的固定 update 次數。此計數只在測試 process 內使用，不保存 Entity、聯絡人資料、
        /// credential 或連線狀態，讓測試能判定 no-change／write path 是否意外執行。
        /// </summary>
        public int UpdateCount => Volatile.Read(ref _updateCount);

        /// <summary>
        /// 取得已被允許的固定 read-back 次數。它只量測本替身的同步 callback，不建立 session、cache、timer
        /// 或背景資源，因此不會跨測試保留任何 mutable 狀態。
        /// </summary>
        public int RetrieveCount => Volatile.Read(ref _retrieveCount);

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public OrganizationResponse Execute(OrganizationRequest request)
        {
            Interlocked.Increment(ref _executeCount);
            if (_execute is not null)
            {
                return _execute(request)
                    ?? throw new InvalidOperationException("The fake execute callback returned null.");
            }

            request.Should().BeOfType<WhoAmIRequest>();
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

        /// <summary>
        /// 執行測試明確注入的固定 create callback。callback 不會被 service 保存任何資料列或 SDK graph；回傳的
        /// GUID 只供目前 transfer read-back 測試使用，避免 fake 意外變成可任意建立 CRM entity 的通道。
        /// </summary>
        public Guid Create(Entity entity)
        {
            ArgumentNullException.ThrowIfNull(entity);
            var callback = _create ?? throw new NotSupportedException();
            Interlocked.Increment(ref _createCount);
            return callback(entity);
        }

        /// <summary>
        /// 執行測試明確注入的固定 update callback。沒有 callback 時立即失敗，避免測試替身默默接受任意
        /// Entity／欄位 map；callback 不會被 service 保留，確保 Entity 僅活在目前呼叫 scope。
        /// </summary>
        public void Update(Entity entity)
        {
            ArgumentNullException.ThrowIfNull(entity);
            var callback = _update ?? throw new NotSupportedException();
            Interlocked.Increment(ref _updateCount);
            callback(entity);
        }

        public void Delete(string entityName, Guid id) => throw new NotSupportedException();

        /// <summary>
        /// 執行測試明確注入的 allowlisted read-back callback。沒有 callback、空 entity 名稱、空 GUID 或空
        /// ColumnSet 都立即失敗，防止 unit test 偽造未驗證的 CRM read；回傳 Entity 只會立刻由 connector
        /// projection 使用，不會存入 fake 的欄位、session 或快取。
        /// </summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
        {
            if (string.IsNullOrWhiteSpace(entityName) || id == Guid.Empty || columnSet is null)
            {
                throw new ArgumentException("The fixed read-back test request is invalid.");
            }

            var callback = _retrieve ?? throw new NotSupportedException();
            Interlocked.Increment(ref _retrieveCount);
            return callback(entityName, id, columnSet)
                ?? throw new InvalidOperationException("The fake read-back callback returned null.");
        }

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
