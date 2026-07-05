// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/Factories/CrmClientFactory.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class CrmClientFactory
// 主要成員：Create、CreateDataverseClient、CreateDataverse、CreateLegacyClient、CreateLegacy
// 引用命名空間：Microsoft.Extensions.Configuration、System、ToolUtilityNameSpace.Adapters、ToolUtilityNameSpace.Interfaces
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
