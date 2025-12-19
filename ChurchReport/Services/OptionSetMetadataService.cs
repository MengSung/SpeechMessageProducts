using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions; // ? 新增
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ChurchReport.Services
{
    /// <summary>
    /// OptionSet Metadata 服務
    /// 負責從 Dynamics 365 動態取得 OptionSet 的完整清單並提供快取機制
    /// </summary>
    public class OptionSetMetadataService
    {
        private readonly IOrganizationService _organizationService;
        private readonly ILogger<OptionSetMetadataService> _logger;
        private readonly IMemoryCache _cache;
        private const int CACHE_DURATION_HOURS = 24; // 快取 24 小時

        public OptionSetMetadataService(
            IOrganizationService organizationService,
            ILogger<OptionSetMetadataService> logger = null,
            IMemoryCache cache = null)
        {
            _organizationService = organizationService ?? throw new ArgumentNullException(nameof(organizationService));
            
            // ? 允許 logger 為 null，使用 NullLogger 作為預設值
            _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<OptionSetMetadataService>.Instance;
            
            // ? 允許 cache 為 null，使用 MemoryCache 作為預設值
            _cache = cache ?? new MemoryCache(new MemoryCacheOptions());
        }

        /// <summary>
        /// 取得指定實體的 OptionSet 對應表（顯示文字 → 值）
        /// 包含快取機制，避免頻繁查詢 Metadata
        /// </summary>
        /// <param name="entityName">實體名稱（例如：new_fee）</param>
        /// <param name="attributeName">屬性名稱（例如：new_category）</param>
        /// <returns>Dictionary&lt;string, int&gt; - 顯示文字對應到 OptionSet 值</returns>
        public Dictionary<string, int> GetOptionSetMapping(string entityName, string attributeName)
        {
            try
            {
                // 產生快取鍵
                string cacheKey = $"OptionSet_{entityName}_{attributeName}";

                // 嘗試從快取取得
                if (_cache.TryGetValue(cacheKey, out Dictionary<string, int> cachedMapping))
                {
                    _logger.LogDebug($"[OptionSetMetadataService] 從快取取得 {entityName}.{attributeName}");
                    return cachedMapping;
                }

                // 從 Dynamics 365 查詢 Metadata
                _logger.LogInformation($"[OptionSetMetadataService] 查詢 Metadata: {entityName}.{attributeName}");

                var retrieveAttributeRequest = new RetrieveAttributeRequest
                {
                    EntityLogicalName = entityName,
                    LogicalName = attributeName,
                    RetrieveAsIfPublished = true
                };

                var retrieveAttributeResponse = (RetrieveAttributeResponse)_organizationService.Execute(retrieveAttributeRequest);
                var attributeMetadata = retrieveAttributeResponse.AttributeMetadata;

                if (attributeMetadata is PicklistAttributeMetadata picklistMetadata)
                {
                    var mapping = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                    foreach (var option in picklistMetadata.OptionSet.Options)
                    {
                        // 取得顯示文字（優先使用繁體中文）
                        string displayText = GetLocalizedLabel(option.Label, "zh-TW") ?? 
                                           GetLocalizedLabel(option.Label, "zh-CN") ?? 
                                           option.Label.UserLocalizedLabel?.Label ?? 
                                           $"Unknown_{option.Value}";

                        if (option.Value.HasValue)
                        {
                            mapping[displayText] = option.Value.Value;
                            _logger.LogDebug($"  - {displayText} → {option.Value.Value}");
                        }
                    }

                    // 存入快取（24 小時過期）
                    var cacheOptions = new MemoryCacheEntryOptions()
                        .SetAbsoluteExpiration(TimeSpan.FromHours(CACHE_DURATION_HOURS));

                    _cache.Set(cacheKey, mapping, cacheOptions);

                    _logger.LogInformation($"[OptionSetMetadataService] 成功取得 {mapping.Count} 個選項，已快取");
                    return mapping;
                }
                else
                {
                    _logger.LogWarning($"[OptionSetMetadataService] {entityName}.{attributeName} 不是 PicklistAttribute");
                    return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[OptionSetMetadataService] 取得 OptionSet 對應表失敗: {entityName}.{attributeName}");
                return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// 根據顯示文字取得 OptionSet 值
        /// </summary>
        /// <param name="entityName">實體名稱</param>
        /// <param name="attributeName">屬性名稱</param>
        /// <param name="displayText">顯示文字</param>
        /// <param name="defaultValue">找不到時的預設值（選填）</param>
        /// <returns>OptionSet 值</returns>
        public int GetOptionSetValue(string entityName, string attributeName, string displayText, int? defaultValue = null)
        {
            try
            {
                var mapping = GetOptionSetMapping(entityName, attributeName);

                if (mapping.TryGetValue(displayText?.Trim() ?? string.Empty, out int value))
                {
                    return value;
                }

                // Fuzzy matching: strip numeric prefixes/symbols (e.g., "01.", "01 ", "01-") then compare
                string Normalize(string text)
                {
                    if (string.IsNullOrWhiteSpace(text)) return string.Empty;
                    return Regex.Replace(text.Trim(), "^\\d+\\s*[\\.、．-]?\\s*", string.Empty);
                }

                var normalizedInput = Normalize(displayText);
                var fuzzy = mapping.FirstOrDefault(kvp => string.Equals(Normalize(kvp.Key), normalizedInput, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(fuzzy.Key))
                {
                    _logger.LogInformation($"[OptionSetMetadataService] Fuzzy matched '{displayText}' -> '{fuzzy.Key}'");
                    return fuzzy.Value;
                }

                _logger.LogWarning($"[OptionSetMetadataService] 找不到對應值: {entityName}.{attributeName} = '{displayText}'");

                if (defaultValue.HasValue)
                {
                    _logger.LogInformation($"[OptionSetMetadataService] 使用預設值: {defaultValue.Value}");
                    return defaultValue.Value;
                }

                throw new KeyNotFoundException($"找不到對應的 OptionSet 值: {displayText}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[OptionSetMetadataService] GetOptionSetValue 失敗: {displayText}");
                throw;
            }
        }

        /// <summary>
        /// 根據 OptionSet 值取得顯示文字（反向查詢）
        /// </summary>
        /// <param name="entityName">實體名稱</param>
        /// <param name="attributeName">屬性名稱</param>
        /// <param name="optionSetValue">OptionSet 值</param>
        /// <returns>顯示文字</returns>
        public string GetOptionSetText(string entityName, string attributeName, int optionSetValue)
        {
            try
            {
                var mapping = GetOptionSetMapping(entityName, attributeName);
                var reversedMapping = mapping.ToDictionary(kvp => kvp.Value, kvp => kvp.Key);

                if (reversedMapping.TryGetValue(optionSetValue, out string displayText))
                {
                    return displayText;
                }

                _logger.LogWarning($"[OptionSetMetadataService] 找不到對應文字: {entityName}.{attributeName} = {optionSetValue}");
                return $"Unknown_{optionSetValue}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[OptionSetMetadataService] GetOptionSetText 失敗: {optionSetValue}");
                return $"Error_{optionSetValue}";
            }
        }

        /// <summary>
        /// 清除指定 OptionSet 的快取
        /// </summary>
        public void ClearCache(string entityName, string attributeName)
        {
            string cacheKey = $"OptionSet_{entityName}_{attributeName}";
            _cache.Remove(cacheKey);
            _logger.LogInformation($"[OptionSetMetadataService] 已清除快取: {cacheKey}");
        }

        /// <summary>
        /// 取得本地化標籤（支援多語言）
        /// </summary>
        private string GetLocalizedLabel(Label label, string languageCode)
        {
            if (label == null || label.LocalizedLabels == null)
                return null;

            var localizedLabel = label.LocalizedLabels
                .FirstOrDefault(l => l.LanguageCode == GetLanguageCodeId(languageCode));

            return localizedLabel?.Label;
        }

        /// <summary>
        /// 將語言代碼轉換為 LCID
        /// </summary>
        private int GetLanguageCodeId(string languageCode)
        {
            switch (languageCode.ToLower())
            {
                case "zh-tw": return 1028; // 繁體中文
                case "zh-cn": return 2052; // 簡體中文
                case "en-us": return 1033; // 英文
                default: return 1028; // 預設繁體中文
            }
        }
    }
}
