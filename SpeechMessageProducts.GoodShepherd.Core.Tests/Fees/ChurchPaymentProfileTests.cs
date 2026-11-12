/*
 * 檔案整體說明
 *
 * 目的與定位：保護 GoodShepherd.Core 依教會 account 正式 ID 讀取永豐設定檔名稱的隔離契約。
 * 本測試以合成 IOrganizationService 驗證讀取只限定 account.new_sinopacprofile、A/B 教會資料
 * 不會在並行呼叫間串用，以及空值和非法 profile 名稱必須 fail closed。
 *
 * 主要情境：每個案例只建立一個本地 SDK 替身和獨立教會 ID；對 A、B 先作明確讀取，
 * 再執行 100 次交錯並行讀取，並注入不存在的教會 ID、空白名稱及超長／含非法字元的名稱。
 * SDK 替身只允許讀 account 的單一欄位，其他實體、欄位範圍及所有寫入操作都立即失敗。
 *
 * 資源擁有權：測試不建立 Host、不連 D365、不讀秘密，也沒有背景工作、連線或全域可變狀態；
 * 每個 Task 都由本測試 await，替身和請求紀錄在案例結束即釋放。離線結果不代表實機資料或授權。
 */
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessageProducts.GoodShepherd.Core.Fees;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd.Core.Tests.Fees;

/// <summary>以合成 SDK 驗證教會 profile 欄位讀取、並行隔離及 fail-closed 名稱檢查。</summary>
public sealed class ChurchPaymentProfileTests
{
    private static readonly string[] ProfileColumns = { "new_sinopacprofile" };

    /// <summary>
    /// 以 server-owned account ID 取得各自設定檔；100 次 A/B 交錯並行讀取不能串值，
    /// 缺失教會及空白／非法名稱均失敗，且 SDK 查詢只能選取指定 profile 欄位。
    /// </summary>
    [Fact]
    public async Task Profile_TenantA_CannotUseTenantBProfile()
    {
        var churchA = Guid.NewGuid();
        var churchB = Guid.NewGuid();
        var profiles = new Dictionary<Guid, string?>
        {
            [churchA] = "A_Sandbox-01.profile",
            [churchB] = "B_Sandbox-02.profile"
        };
        var invalidNames = new[] { "", " ", "bad/name", new string('A', 65), "教會設定" };
        var invalidChurchIds = invalidNames.Select(_ => Guid.NewGuid()).ToArray();
        for (var index = 0; index < invalidNames.Length; index++)
        {
            profiles[invalidChurchIds[index]] = invalidNames[index];
        }

        var sdk = new ProfileSdk(profiles);
        Assert.Equal("A_Sandbox-01.profile", ChurchPaymentProfile.Read(sdk, churchA, CancellationToken.None));
        Assert.Equal("B_Sandbox-02.profile", ChurchPaymentProfile.Read(sdk, churchB, CancellationToken.None));

        var reads = Enumerable.Range(0, 100)
            .Select(index => Task.Run(() =>
            {
                var id = index % 2 == 0 ? churchA : churchB;
                return (Index: index, Profile: ChurchPaymentProfile.Read(sdk, id, CancellationToken.None));
            }))
            .ToArray();
        var observed = await Task.WhenAll(reads);
        foreach (var read in observed)
        {
            Assert.Equal(read.Index % 2 == 0 ? "A_Sandbox-01.profile" : "B_Sandbox-02.profile", read.Profile);
        }

        Assert.Throws<InvalidOperationException>(() =>
            ChurchPaymentProfile.Read(sdk, Guid.NewGuid(), CancellationToken.None));
        for (var index = 0; index < invalidNames.Length; index++)
        {
            var exception = Assert.Throws<InvalidOperationException>(() =>
                ChurchPaymentProfile.Read(sdk, invalidChurchIds[index], CancellationToken.None));
            if (!string.IsNullOrWhiteSpace(invalidNames[index]))
            {
                Assert.Equal(-1, exception.Message.IndexOf(invalidNames[index], StringComparison.Ordinal));
            }
        }

        var readCountBeforeEmptyId = sdk.Reads.Count;
        Assert.Throws<InvalidOperationException>(() =>
            ChurchPaymentProfile.Read(sdk, Guid.Empty, CancellationToken.None));
        Assert.Equal(readCountBeforeEmptyId, sdk.Reads.Count);
        Assert.Equal(51, sdk.Reads.Count(read => read.Id == churchA));
        Assert.Equal(51, sdk.Reads.Count(read => read.Id == churchB));

        Assert.All(sdk.Reads, read =>
        {
            Assert.NotEqual(Guid.Empty, read.Id);
            Assert.Equal("account", read.EntityName);
            Assert.Equal(ProfileColumns, read.Columns);
        });
    }

    /// <summary>
    /// 僅支援本測試中固定的 account profile 欄位讀取；依 ID 查找合成值，
    /// 並以執行緒安全集合保存不可變查詢摘要，供並行隔離斷言使用。
    /// </summary>
    private sealed class ProfileSdk : IOrganizationService
    {
        private readonly Dictionary<Guid, string?> profiles;

        /// <summary>複製單案例輸入，避免呼叫端在並行測試期間改動 SDK 替身的資料來源。</summary>
        public ProfileSdk(IReadOnlyDictionary<Guid, string?> profiles)
        {
            this.profiles = profiles.ToDictionary(pair => pair.Key, pair => pair.Value);
        }

        /// <summary>只記錄查詢實體、正式 ID 及欄位名稱，不保存返回物件或設定值。</summary>
        public ConcurrentBag<(Guid Id, string EntityName, string[] Columns)> Reads { get; } = new();

        /// <summary>只接受 account.new_sinopacprofile 的讀取，並依正式 ID 回傳本案例合成資料。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
        {
            if (entityName != "account" || columnSet.Columns.Count != 1
                || columnSet.Columns[0] != "new_sinopacprofile")
            {
                throw new InvalidOperationException("Unexpected retrieve scope.");
            }

            Reads.Add((id, entityName, columnSet.Columns.ToArray()));
            if (!profiles.TryGetValue(id, out var profileName))
            {
                throw new InvalidOperationException("Synthetic account is missing.");
            }

            var account = new Entity("account", id);
            if (profileName is not null)
            {
                account["new_sinopacprofile"] = profileName;
            }
            return account;
        }

        /// <summary>測試契約不允許建立資料。</summary>
        public Guid Create(Entity entity) => throw new InvalidOperationException("Unexpected create.");

        /// <summary>測試契約不允許更新資料。</summary>
        public void Update(Entity entity) => throw new InvalidOperationException("Unexpected update.");

        /// <summary>測試契約不允許刪除資料。</summary>
        public void Delete(string entityName, Guid id) => throw new InvalidOperationException("Unexpected delete.");

        /// <summary>測試契約不允許建立關聯。</summary>
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) =>
            throw new InvalidOperationException("Unexpected associate.");

        /// <summary>測試契約不允許解除關聯。</summary>
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) =>
            throw new InvalidOperationException("Unexpected disassociate.");

        /// <summary>測試契約不允許查詢多筆資料。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query) => throw new InvalidOperationException("Unexpected query.");

        /// <summary>測試契約不允許執行額外 SDK 訊息。</summary>
        public OrganizationResponse Execute(OrganizationRequest request) => throw new InvalidOperationException("Unexpected request.");
    }
}
