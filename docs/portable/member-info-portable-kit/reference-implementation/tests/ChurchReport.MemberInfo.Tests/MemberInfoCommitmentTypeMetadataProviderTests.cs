using ChurchReport.Services.MemberInfo;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using Xunit;

namespace ChurchReport.MemberInfo.Tests;

public class MemberInfoCommitmentTypeMetadataProviderTests
{
    [Fact]
    public void GetOptions_PreservesConfiguredOrderInsteadOfNumericValueOrder()
    {
        var service = new RecordingOrganizationService(Metadata(
            (100000006, "牧師師母"),
            (1, "小組組員"),
            (100000000, "新朋友")));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var provider = new MemberInfoCommitmentTypeMetadataProvider(service, cache);

        var options = provider.GetOptions();

        options.Select(option => option.Value)
            .Should().Equal(100000006, 1, 100000000);
        options.Select(option => option.Order)
            .Should().Equal(0, 1, 2);
        options[0].Label.Should().Be("牧師師母");
    }

    [Fact]
    public void GetOptions_UsesSharedCacheAfterFirstMetadataRequest()
    {
        var service = new RecordingOrganizationService(Metadata(
            (100000006, "牧師師母"),
            (1, "小組組員")));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var provider = new MemberInfoCommitmentTypeMetadataProvider(service, cache);

        provider.GetOptions();
        provider.GetOptions();

        service.ExecuteCalls.Should().Be(1);
    }

    [Fact]
    public void GetOptions_SkipsOptionWithoutValue()
    {
        var metadata = Metadata((100000006, "牧師師母"));
        metadata.OptionSet.Options.Add(new OptionMetadata(
            new Label("沒有值", 1028),
            null));
        using var cache = new MemoryCache(new MemoryCacheOptions());

        var options = new MemberInfoCommitmentTypeMetadataProvider(
                new RecordingOrganizationService(metadata),
                cache)
            .GetOptions();

        options.Should().ContainSingle();
        options[0].Value.Should().Be(100000006);
    }

    [Fact]
    public void GetOptions_MetadataFailureReturnsEmptyWithoutThrowing()
    {
        var service = new RecordingOrganizationService(
            Metadata((100000006, "牧師師母")),
            throwOnExecute: true);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var provider = new MemberInfoCommitmentTypeMetadataProvider(service, cache);

        var act = () => provider.GetOptions();

        act.Should().NotThrow();
        act().Should().BeEmpty();
        provider.GetOptions().Should().BeEmpty();
        service.ExecuteCalls.Should().Be(1, "短暫失敗也應快取，避免每個表格重複打 metadata API");
    }

    private static PicklistAttributeMetadata Metadata(
        params (int Value, string Label)[] options)
    {
        var collection = new OptionMetadataCollection();
        foreach (var option in options)
        {
            collection.Add(new OptionMetadata(
                new Label(option.Label, 1028),
                option.Value));
        }

        return new PicklistAttributeMetadata
        {
            LogicalName = "customertypecode",
            OptionSet = new OptionSetMetadata(collection)
        };
    }

    private sealed class RecordingOrganizationService : IOrganizationService
    {
        private readonly AttributeMetadata metadata;
        private readonly bool throwOnExecute;

        public RecordingOrganizationService(
            AttributeMetadata metadata,
            bool throwOnExecute = false)
        {
            this.metadata = metadata;
            this.throwOnExecute = throwOnExecute;
        }

        public int ExecuteCalls { get; private set; }

        public OrganizationResponse Execute(OrganizationRequest request)
        {
            ExecuteCalls++;
            request.Should().BeOfType<RetrieveAttributeRequest>();
            if (throwOnExecute)
            {
                throw new InvalidOperationException("metadata unavailable");
            }

            var response = new RetrieveAttributeResponse();
            response.Results["AttributeMetadata"] = metadata;
            return response;
        }

        public Guid Create(Entity entity) => throw new NotSupportedException();
        public void Update(Entity entity) => throw new NotSupportedException();
        public void Delete(string entityName, Guid id) => throw new NotSupportedException();
        public Entity Retrieve(
            string entityName,
            Guid id,
            ColumnSet columnSet) => throw new NotSupportedException();
        public EntityCollection RetrieveMultiple(
            QueryBase query) => throw new NotSupportedException();
        public void Associate(
            string entityName,
            Guid entityId,
            Relationship relationship,
            EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
        public void Disassociate(
            string entityName,
            Guid entityId,
            Relationship relationship,
            EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
    }
}
