/*
 * 檔案整體說明
 *
 * 目的與定位：以合成 Dataverse SDK 回應驗證 P8-02 角色清單所需的嚴格 OptionSet metadata 讀取契約。
 *
 * 主要功能與流程：
 * 測試只透過本檔擁有的 IOrganizationService 替身提供 PicklistAttributeMetadata，驗證服務只選
 * LanguageCode 1028、缺少該語系時拒絕發布、保留相同 label 但不同 option value 的列，以及快取
 * 按伺服器組織範圍分區。測試資料均為合成選項，不包含任何會友資料、組織連線或憑證。
 *
 * 使用邊界與維護：
 * 替身僅回應 RetrieveAttributeRequest；其他 SDK 操作一律拒絕。MemoryCache 由單一測試建立並在
 * 測試結束 Dispose；服務不擁有或延長測試替身生命週期。不得將本測試解讀為 D365 實機驗收。
 *
 * 失敗處理與驗證：
 * 每個案例以輸出選項值／標籤、明確例外及 SDK 呼叫次數作決定性斷言；若日後加入語系回退、
 * Dictionary 標籤折疊或未分區快取，對應案例即失敗。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using ChurchReport.Services;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Personal;

/// <summary>保護角色 metadata 的精確語系、選項身分保留及組織快取隔離契約。</summary>
public sealed class OptionSetMetadataStrictRoleOptionsTests
{
    /// <summary>LanguageCode 1028 是唯一可用來源；其他語系及 UserLocalizedLabel 不得替代它。</summary>
    [Fact]
    public void GetOptionSetOptions_UsesExactLanguageCode1028()
    {
        var service = new RecordingOrganizationService(Metadata(
            Option(100, (2052, "簡體標籤"), (1028, "小組組員"), (1033, "English"))));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var metadataService = new OptionSetMetadataService(service, cache: cache);

        var options = metadataService.GetOptionSetOptions("contact", "customertypecode", "goodshepherdback");

        options.Should().ContainSingle();
        options[0].Key.Should().Be(100);
        options[0].Value.Should().Be("小組組員");
    }

    /// <summary>缺少 1028 的任一 option 是故障注入；整份快照必須拒絕而不能部分成功或回退語系。</summary>
    [Fact]
    public void GetOptionSetOptions_MissingLanguage1028_FailsClosed()
    {
        var service = new RecordingOrganizationService(Metadata(
            Option(100, (1028, "小組組員")),
            Option(101, (2052, "只有簡體"))));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var metadataService = new OptionSetMetadataService(service, cache: cache);

        var action = () => metadataService.GetOptionSetOptions(
            "contact", "customertypecode", "goodshepherdback");

        action.Should().Throw<InvalidOperationException>();
        service.ExecuteCalls.Should().Be(1);
    }

    /// <summary>注入相同繁中標籤的不同 option value，確認服務保留每一個 D365 metadata 身分。</summary>
    [Fact]
    public void GetOptionSetOptions_PreservesDistinctOptionsWithDuplicateLabels()
    {
        var service = new RecordingOrganizationService(Metadata(
            Option(100, (1028, "小組組員")),
            Option(200, (1028, "小組組員"))));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var metadataService = new OptionSetMetadataService(service, cache: cache);

        var options = metadataService.GetOptionSetOptions("contact", "customertypecode", "goodshepherdback");

        options.Select(option => (option.Key, option.Value))
            .Should().Equal((100, "小組組員"), (200, "小組組員"));
    }

    /// <summary>以兩個伺服器組織範圍及差異 metadata 注入快取碰撞，確認快照不跨組織重用。</summary>
    [Fact]
    public void GetOptionSetOptions_CacheIsPartitionedByOrganizationScope()
    {
        var service = new RecordingOrganizationService(
            Metadata(Option(100, (1028, "甲組織標籤"))),
            Metadata(Option(200, (1028, "乙組織標籤"))));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var metadataService = new OptionSetMetadataService(service, cache: cache);

        var first = metadataService.GetOptionSetOptions("contact", "customertypecode", "org-a");
        var second = metadataService.GetOptionSetOptions("contact", "customertypecode", "org-b");
        var firstAgain = metadataService.GetOptionSetOptions("contact", "customertypecode", "org-a");

        first[0].Key.Should().Be(100);
        second[0].Key.Should().Be(200);
        firstAgain[0].Value.Should().Be("甲組織標籤");
        service.ExecuteCalls.Should().Be(2);
    }

    private static PicklistAttributeMetadata Metadata(params OptionMetadata[] options) =>
        new()
        {
            LogicalName = "customertypecode",
            OptionSet = new OptionSetMetadata(new OptionMetadataCollection(options))
        };

    private static OptionMetadata Option(int value, params (int LanguageCode, string Label)[] labels)
    {
        if (labels.Length == 0)
            throw new ArgumentException("Synthetic option labels are required.", nameof(labels));

        var label = new Label();
        foreach (var localizedLabel in labels)
            label.LocalizedLabels.Add(new LocalizedLabel(localizedLabel.Label, localizedLabel.LanguageCode));
        label.UserLocalizedLabel = new LocalizedLabel(labels[0].Label, labels[0].LanguageCode);
        return new OptionMetadata
        {
            Value = value,
            Label = label
        };
    }

    private sealed class RecordingOrganizationService : IOrganizationService
    {
        private readonly Queue<AttributeMetadata> responses;

        internal RecordingOrganizationService(params AttributeMetadata[] responses)
        {
            this.responses = new Queue<AttributeMetadata>(responses);
        }

        internal int ExecuteCalls { get; private set; }

        public OrganizationResponse Execute(OrganizationRequest request)
        {
            request.Should().BeOfType<RetrieveAttributeRequest>();
            ExecuteCalls++;
            if (responses.Count == 0)
                throw new InvalidOperationException("No synthetic metadata response remains.");
            var response = new RetrieveAttributeResponse();
            response.Results["AttributeMetadata"] = responses.Dequeue();
            return response;
        }

        public Guid Create(Entity entity) => throw new NotSupportedException();
        public void Update(Entity entity) => throw new NotSupportedException();
        public void Delete(string entityName, Guid id) => throw new NotSupportedException();
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => throw new NotSupportedException();
        public EntityCollection RetrieveMultiple(QueryBase query) => throw new NotSupportedException();
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) =>
            throw new NotSupportedException();
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) =>
            throw new NotSupportedException();
    }
}
