/*
 * 檔案整體說明
 *
 * 目的與定位：以假SDK確認目前會友計數只送出一次aggregate查詢並採伺服器計數，不下載整份名單計算。
 *
 * 主要功能與流程：
 * 本檔宣告 MemberInfoCurrentContactCounterTests、RecordingOrganizationService。
 * 測試案例入口包含 Count_UsesOneAggregateFetchAndReturnsTheServerCount、RetrieveMultiple、Create、Update、Delete、Retrieve、
 * Execute、Associate；其餘入口見下方成員契約。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 * 可沿 ChurchReport.MemberInfo.Tests/MemberInfoCommitmentTypeMetadataProviderTests.cs 追查目前工作樹中的合作／使用位置。
 */
using ChurchReport.Services.MemberInfo;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Xunit;

namespace ChurchReport.MemberInfo.Tests;

public class MemberInfoCurrentContactCounterTests
{
    [Fact]
    public void Count_UsesOneAggregateFetchAndReturnsTheServerCount()
    {
        var result = new EntityCollection();
        var aggregateRow = new Entity("contact");
        aggregateRow["currentcontactcount"] = new AliasedValue("contact", "contactid", 321);
        result.Entities.Add(aggregateRow);
        var service = new RecordingOrganizationService(result);

        var count = MemberInfoCurrentContactCounter.Count(service, 7);

        count.Should().Be(321);
        service.RetrieveMultipleCalls.Should().Be(1);
        var fetch = service.LastQuery.Should().BeOfType<FetchExpression>().Subject.Query;
        fetch.Should().Contain("aggregate=\"true\"");
        fetch.Should().Contain("aggregate=\"countcolumn\"");
        fetch.Should().Contain("attribute=\"statecode\" operator=\"eq\" value=\"0\"");
        fetch.Should().Contain("attribute=\"customertypecode\" operator=\"ne\" value=\"7\"");
    }

    private sealed class RecordingOrganizationService : IOrganizationService
    {
        private readonly EntityCollection result;

        public RecordingOrganizationService(EntityCollection result)
        {
            this.result = result;
        }

        public QueryBase? LastQuery { get; private set; }
        public int RetrieveMultipleCalls { get; private set; }

        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            LastQuery = query;
            RetrieveMultipleCalls++;
            return result;
        }

        public Guid Create(Entity entity) => throw new NotSupportedException();
        public void Update(Entity entity) => throw new NotSupportedException();
        public void Delete(string entityName, Guid id) => throw new NotSupportedException();
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => throw new NotSupportedException();
        public OrganizationResponse Execute(OrganizationRequest request) => throw new NotSupportedException();
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
    }
}
