/*
 * 檔案整體說明
 *
 * 目的與定位：CRM Client Factory 設計模式：Factory Method Pattern 用途：根據配置決定建立哪種 ICrmClient 實作
 *
 * 主要功能與流程：
 * 本檔宣告 CrmClientFactory。
 * - 建立 Dataverse (Modern) Client
 * - 建立 Dataverse Client（直接提供連線字串）
 * - 建立 Legacy (On-Premise) Client
 * 主要操作入口包含 Create、CreateDataverseClient、CreateDataverse、CreateLegacyClient、CreateLegacy。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 */
using Microsoft.Extensions.Configuration;
using System;
using ToolUtilityNameSpace.Adapters;
using ToolUtilityNameSpace.Interfaces;

namespace ToolUtilityNameSpace.Factories
{
    /// <summary>
    /// CRM Client Factory
    /// 設計模式：Factory Method Pattern
    /// 用途：根據配置決定建立哪種 ICrmClient 實作
    /// </summary>
    /// <remarks>
    /// 此 Factory 符合 Linus 原則：
    /// 1. 簡潔實作 - 只負責建立物件，不包含業務邏輯
    /// 2. 可配置 - 透過 IConfiguration 注入，方便測試與切換
    /// 3. 單一職責 - 只做物件建立
    ///
    /// 使用範例：
    /// <code>
    /// // 在 Startup.cs / Program.cs 註冊
    /// services.AddScoped&lt;ICrmClient&gt;(sp =>
    /// {
    ///     var config = sp.GetRequiredService&lt;IConfiguration&gt;();
    ///     return CrmClientFactory.Create(config);
    /// });
    /// </code>
    /// </remarks>
    public static class CrmClientFactory
    {
        /// <summary>
        /// 根據配置建立 ICrmClient
        /// </summary>
        /// <param name="configuration">應用程式配置</param>
        /// <returns>ICrmClient 實作</returns>
        /// <example>
        /// appsettings.json:
        /// {
        ///   "CrmConnection": {
        ///     "Type": "Dataverse",  // "Dataverse" 或 "Legacy"
        ///     "ConnectionString": "AuthType=OAuth;..."
        ///   }
        /// }
        /// </example>
        public static ICrmClient Create(IConfiguration configuration)
        {
            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));

            var crmType = configuration["CrmConnection:Type"] ?? "Legacy";

            switch (crmType.ToUpperInvariant())
            {
#if NETCOREAPP || NET5_0_OR_GREATER
                case "DATAVERSE":
                case "DYNAMICS365":
                    return CreateDataverseClient(configuration);
#endif

                case "LEGACY":
                case "ONPREMISE":
                case "CRM2011":
                    return CreateLegacyClient(configuration);

                default:
#if NETCOREAPP || NET5_0_OR_GREATER
                    throw new InvalidOperationException(
                        $"Unknown CRM connection type: {crmType}. Valid types: Dataverse, Legacy");
#else
                    throw new InvalidOperationException(
                        $"Unknown CRM connection type: {crmType}. Valid types: Legacy (Dataverse not supported in .NET Framework)");
#endif
            }
        }

#if NETCOREAPP || NET5_0_OR_GREATER
        /// <summary>
        /// 建立 Dataverse (Modern) Client
        /// </summary>
        private static ICrmClient CreateDataverseClient(IConfiguration configuration)
        {
            var connectionString = configuration["CrmConnection:ConnectionString"];

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "CrmConnection:ConnectionString is required for Dataverse client");
            }

            return new DataverseServiceClientAdapter(connectionString);
        }

        /// <summary>
        /// 建立 Dataverse Client（直接提供連線字串）
        /// </summary>
        public static ICrmClient CreateDataverse(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("Connection string cannot be null or empty", nameof(connectionString));

            return new DataverseServiceClientAdapter(connectionString);
        }
#endif

        /// <summary>
        /// 建立 Legacy (On-Premise) Client
        /// </summary>
        private static ICrmClient CreateLegacyClient(IConfiguration configuration)
        {
            // 方式 1: 使用連線字串（若有提供）
            var connectionString = configuration["CrmConnection:ConnectionString"];
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                // TODO: 解析連線字串並建立 LegacyAdapter
                // 目前 LegacyAdapter 不支援連線字串，需要個別參數
            }

            // 方式 2: 使用個別參數（向後相容）
            var serverUrl = configuration["CrmConnection:ServerUrl"];
            var domain = configuration["CrmConnection:Domain"];
            var username = configuration["CrmConnection:Username"];
            var password = configuration["CrmConnection:Password"];

            if (string.IsNullOrWhiteSpace(serverUrl))
            {
                throw new InvalidOperationException(
                    "CrmConnection:ServerUrl is required for Legacy client");
            }

            return new LegacyOrganizationServiceAdapter(serverUrl, domain, username, password);
        }

        /// <summary>
        /// 建立 Legacy Client（直接提供參數）
        /// </summary>
        public static ICrmClient CreateLegacy(string serverUrl, string domain, string username, string password)
        {
            if (string.IsNullOrWhiteSpace(serverUrl))
                throw new ArgumentException("Server URL cannot be null or empty", nameof(serverUrl));

            return new LegacyOrganizationServiceAdapter(serverUrl, domain, username, password);
        }
    }
}
