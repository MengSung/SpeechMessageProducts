/*
 * 檔案整體說明
 *
 * 目的與定位：驗證 GoodShepherd.Core 奉獻類別清單的穩定資料庫身分、排序與讀取筆數上限，
 * 對應 P8-01a／P8-01b 共用領域契約；本測試只使用記憶體資料及合成 IOrganizationService。
 * 主要流程：建立同名不同 ID、重複 ID 與不同排序候選，驗證清單只有在整批身分檢查成功後
 * 才可發布；另以合成 SDK 回傳 201 筆資料，確認讀取器拒絕超過 200 筆而不發布部分結果。
 * 使用邊界與維護：教會 ID 由測試呼叫端提供且作為查詢分區，SDK 替身不連組織、不讀秘密；
 * 替身沒有外部資源，所有列與候選只在單一測試存活，不存在跨測試快取或背景工作。
 * 故障與決定性斷言：重複主鍵必須 fail closed、同名異 ID 必須都保留、排序依 SortOrder
 * 再依 Code、超量必須拋出明確資料錯誤；測試失敗不代表任何實機資料或服務狀態。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using SpeechMessageProducts.GoodShepherd.Core.Offerings;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd.Core.Tests.Offerings;

/// <summary>保護奉獻類別以資料庫 ID 為唯一身分的發布契約，所有 SDK 回應皆由本測試擁有的合成替身提供。</summary>
public sealed class OfferingCategoryTests
{
    private static readonly Guid ChurchId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly string[] ExpectedOrder = { "alpha", "beta", "zeta" };

    /// <summary>兩個資料庫 ID 即使顯示名稱相同仍代表不同正式資料，清單不得按名稱合併。</summary>
    [Fact]
    public void List_SameNameDifferentIds_BothKept()
    {
        var list = new OfferingCategoryList(new[]
        {
            Category("10000000-0000-0000-0000-000000000001", "legacy-1", "十一奉獻", 1),
            Category("10000000-0000-0000-0000-000000000002", "legacy-2", "十一奉獻", 2)
        });

        Assert.Equal(2, list.Categories.Count);
        Assert.Equal(2, list.Categories.Select(item => item.Id).Distinct().Count());
        Assert.All(list.Categories, item => Assert.Equal("十一奉獻", item.Name));
    }

    /// <summary>同一集合出現相同資料庫 ID 時整批拒絕，避免 UI 收到重複穩定 key 或靜默覆蓋列。</summary>
    [Fact]
    public void List_DuplicateId_PublicationRejected()
    {
        var duplicateId = "10000000-0000-0000-0000-000000000001";

        Assert.Throws<InvalidOperationException>(() => new OfferingCategoryList(new[]
        {
            Category(duplicateId, "legacy-1", "奉獻甲", 1),
            Category(duplicateId, "legacy-2", "奉獻乙", 2)
        }));
    }

    /// <summary>清單排序先使用明確排序值再以代碼作穩定次序，不依賴來源列或字典的偶然排列。</summary>
    [Fact]
    public void List_OrderedBySortOrderThenCode()
    {
        var list = new OfferingCategoryList(new[]
        {
            Category("10000000-0000-0000-0000-000000000003", "zeta", "丙", 1),
            Category("10000000-0000-0000-0000-000000000002", "beta", "乙", 1),
            Category("10000000-0000-0000-0000-000000000001", "alpha", "甲", 0)
        });

        Assert.Equal(ExpectedOrder, list.Categories.Select(item => item.Code));
    }

    /// <summary>清單在發布前複製來源集合，呼叫端後續清空可變候選不得改變已發布快照。</summary>
    [Fact]
    public void List_SourceMutation_DoesNotChangePublishedSnapshot()
    {
        var source = new List<OfferingCategory>
        {
            Category("10000000-0000-0000-0000-000000000001", "legacy-1", "奉獻", 1)
        };
        var snapshot = new OfferingCategoryList(source);

        source.Clear();

        Assert.Single(snapshot.Categories);
    }

    /// <summary>顯示文字依 lookup、舊選項格式化標籤、通用字典文字回退，未知值不會冒充特定奉獻類別。</summary>
    [Fact]
    public void Text_LookupThenLegacyLabelThenGenericFallback()
    {
        Assert.Equal("新類別", OfferingCategoryText.Resolve("新類別", "舊標籤", "奉獻"));
        Assert.Equal("舊標籤", OfferingCategoryText.Resolve(null, "舊標籤", "奉獻"));
        Assert.Equal("奉獻", OfferingCategoryText.Resolve(null, null, "奉獻"));
    }

    /// <summary>合成 SDK 對單一教會回傳 201 列時，讀取器拒絕整批且不回傳前 200 列的部分快照。</summary>
    [Fact]
    public void Reader_MoreThan200_Rejected()
    {
        var service = new OfferingCategoryService(Enumerable.Range(1, 201).Select(index =>
            new Entity("new_offeringcategory", GuidFrom(index))
            {
                ["new_accountid"] = new EntityReference("account", ChurchId),
                ["new_code"] = "category-" + index,
                ["new_name"] = "類別 " + index,
                ["new_sortorder"] = index,
                ["statecode"] = new OptionSetValue(0)
            }).ToArray());

        Assert.Throws<InvalidOperationException>(() => OfferingCategoryReader.Read(service, ChurchId));
        Assert.Equal(1, service.ReadCount);
    }

    /// <summary>讀取剛好 200 列可完整發布，查詢明確限制單一 account 並只取 201 列以判定超量。</summary>
    [Fact]
    public void Reader_AtLimit_ReturnsCompleteChurchScopedSnapshot()
    {
        var service = new OfferingCategoryService(Enumerable.Range(1, 200).Select(index =>
            new Entity("new_offeringcategory", GuidFrom(index))
            {
                ["new_accountid"] = new EntityReference("account", ChurchId),
                ["new_code"] = "category-" + index,
                ["new_name"] = "類別 " + index,
                ["new_sortorder"] = index,
                ["statecode"] = new OptionSetValue(0)
            }).ToArray());

        var list = OfferingCategoryReader.Read(service, ChurchId);

        Assert.Equal(200, list.Categories.Count);
        Assert.Equal(201, service.LastQuery!.TopCount);
        Assert.Contains(service.LastQuery.Criteria.Conditions, condition =>
            condition.AttributeName == "new_accountid" && condition.Values.Contains(ChurchId));
    }

    private static OfferingCategory Category(string id, string code, string name, int sortOrder) =>
        new(Guid.Parse(id), ChurchId, code, name, sortOrder, null, null, true);

    private static Guid GuidFrom(int value)
    {
        var bytes = new byte[16];
        BitConverter.GetBytes(value).CopyTo(bytes, 12);
        return new Guid(bytes);
    }

    private sealed class OfferingCategoryService(Entity[] rows) : IOrganizationService
    {
        internal int ReadCount { get; private set; }
        internal Microsoft.Xrm.Sdk.Query.QueryExpression? LastQuery { get; private set; }
        public Guid Create(Entity entity) => throw new NotSupportedException();
        public void Update(Entity entity) => throw new NotSupportedException();
        public void Delete(string entityName, Guid id) => throw new NotSupportedException();
        public OrganizationResponse Execute(OrganizationRequest request) => throw new NotSupportedException();
        public Entity Retrieve(string entityName, Guid id, Microsoft.Xrm.Sdk.Query.ColumnSet columnSet) => throw new NotSupportedException();
        public EntityCollection RetrieveMultiple(Microsoft.Xrm.Sdk.Query.QueryBase query)
        {
            ReadCount++;
            LastQuery = Assert.IsType<Microsoft.Xrm.Sdk.Query.QueryExpression>(query);
            return new EntityCollection(rows);
        }
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) =>
            throw new NotSupportedException();
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) =>
            throw new NotSupportedException();
    }
}
