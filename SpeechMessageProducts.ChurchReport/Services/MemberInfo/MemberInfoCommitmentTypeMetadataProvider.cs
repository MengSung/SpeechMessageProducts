using Microsoft.Extensions.Caching.Memory;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ChurchReport.Services.MemberInfo
{
    /// <summary>
    /// Dynamics 客製化畫面中的單一委身類型選項。
    /// Value 只是 CRM 儲存識別碼；Order 才是 OptionSet.Options 集合中的顯示順位。
    /// </summary>
    public sealed record MemberInfoCommitmentTypeOption(
        int Value,
        string Label,
        int Order);

    /// <summary>
    /// 讀取 contact.customertypecode 的 metadata 客製化順序。
    /// 本服務刻意保留 CRM 回傳的集合先後，不依數值或中文文字重新排序，讓不同教會／版本
    /// 可直接沿用各自的系統設定。快取只存 schema metadata，不含任何會友個資。
    /// </summary>
    public sealed class MemberInfoCommitmentTypeMetadataProvider
    {
        private const string CacheKey =
            "member-info:metadata:contact:customertypecode:configured-order";
        private static readonly TimeSpan SuccessCacheDuration = TimeSpan.FromMinutes(30);
        private static readonly TimeSpan FailureCacheDuration = TimeSpan.FromMinutes(1);

        private readonly IOrganizationService organizationService;
        private readonly IMemoryCache cache;

        public MemberInfoCommitmentTypeMetadataProvider(
            IOrganizationService organizationService,
            IMemoryCache cache)
        {
            this.organizationService = organizationService
                ?? throw new ArgumentNullException(nameof(organizationService));
            this.cache = cache ?? throw new ArgumentNullException(nameof(cache));
        }

        /// <summary>
        /// 取得依 Dynamics 客製化順序排列的選項快照；Order 從零開始且只由集合位置產生。
        /// metadata 暫時失敗時回傳短期快取的空集合，呼叫端即可採「未知值」穩定排序，
        /// 不會誤用 raw 整數或硬編碼清單冒充正確順序。
        /// </summary>
        public IReadOnlyList<MemberInfoCommitmentTypeOption> GetOptions()
        {
            if (cache.TryGetValue(
                    CacheKey,
                    out IReadOnlyList<MemberInfoCommitmentTypeOption> cached) &&
                cached != null)
            {
                return cached;
            }

            try
            {
                var response = (RetrieveAttributeResponse)organizationService.Execute(
                    new RetrieveAttributeRequest
                    {
                        EntityLogicalName = "contact",
                        LogicalName = "customertypecode",
                        RetrieveAsIfPublished = true
                    });
                var metadata = response.AttributeMetadata as PicklistAttributeMetadata;
                var options = metadata?.OptionSet?.Options ?? new OptionMetadataCollection();

                // Select 的索引就是系統客製化順位；此處禁止加入 OrderBy(value/label)。
                IReadOnlyList<MemberInfoCommitmentTypeOption> result = options
                    .Where(option => option.Value.HasValue)
                    .Select((option, order) => new MemberInfoCommitmentTypeOption(
                        option.Value.Value,
                        ResolveLabel(option),
                        order))
                    .ToArray();
                cache.Set(CacheKey, result, SuccessCacheDuration);
                return result;
            }
            catch
            {
                IReadOnlyList<MemberInfoCommitmentTypeOption> empty =
                    Array.Empty<MemberInfoCommitmentTypeOption>();
                cache.Set(CacheKey, empty, FailureCacheDuration);
                return empty;
            }
        }

        /// <summary>
        /// 優先使用繁體中文，其次簡體中文及目前使用者語系；完全無標籤時保留可診斷文字。
        /// </summary>
        private static string ResolveLabel(OptionMetadata option)
        {
            return option.Label?.LocalizedLabels?
                       .FirstOrDefault(label => label.LanguageCode == 1028)?.Label
                   ?? option.Label?.LocalizedLabels?
                       .FirstOrDefault(label => label.LanguageCode == 2052)?.Label
                   ?? option.Label?.UserLocalizedLabel?.Label
                   ?? $"Unknown_{option.Value}";
        }
    }
}
