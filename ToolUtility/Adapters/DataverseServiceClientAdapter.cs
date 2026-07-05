// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/Adapters/DataverseServiceClientAdapter.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class DataverseServiceClientAdapter
// 主要成員：Create、Update、Delete、Retrieve、RetrieveMultiple、Execute、CreateAsync、UpdateAsync、DeleteAsync、RetrieveAsync
// 引用命名空間：Microsoft.Xrm.Sdk、Microsoft.Xrm.Sdk.Query、Microsoft.PowerPlatform.Dataverse.Client、System、System.Threading、System.Threading.Tasks、ToolUtilityNameSpace.Interfaces
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
// 此檔案需要在 .NET Core 3.1+ 或 .NET 5+ 環境才能編譯
// 在 .NET Framework 4.6.2 環境下會被條件編譯排除
#if NETCOREAPP || NET5_0_OR_GREATER

using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.PowerPlatform.Dataverse.Client;
using System;
using System.Threading;
using System.Threading.Tasks;
using ToolUtilityNameSpace.Interfaces;

namespace ToolUtilityNameSpace.Adapters
{
    /// <summary>
    /// Dataverse ServiceClient Adapter（.NET Core / .NET 10 適用）
    /// 設計模式：Adapter Pattern
    /// 用途：將新的 Microsoft.PowerPlatform.Dataverse.Client.ServiceClient 包裝成 ICrmClient
    /// </summary>
    /// <remarks>
    /// ?? 此 Adapter 僅在 .NET Core 3.1+ 或 .NET 5+ 環境下編譯
    ///
    /// 此 Adapter 符合 Linus 原則：
    /// 1. 簡潔實作 - 直接轉發呼叫，不加入額外邏輯
    /// 2. 可測試性 - 透過 ICrmClient 介面方便 mock
    /// 3. 向後相容 - 同時支援同步與非同步呼叫
    ///
    /// TODO (Phase 2):
    /// - 加入 Retry 策略（使用 Polly）
    /// - 加入 Telemetry / Logging（使用 Decorator）
    /// - 加入連線池管理
    /// </remarks>
    public class DataverseServiceClientAdapter : ICrmClient
    {
        private readonly ServiceClient _serviceClient;
        private bool _disposed = false;

        #region 建構式

        /// <summary>
        /// 使用連線字串建立 Adapter
        /// </summary>
        /// <param name="connectionString">Dataverse 連線字串（支援 OAuth）</param>
        /// <example>
        /// AuthType=OAuth;Username=user@org.onmicrosoft.com;Password=***;Url=https://org.crm.dynamics.com;AppId=***;RedirectUri=app://***;LoginPrompt=Auto
        /// </example>
        public DataverseServiceClientAdapter(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("Connection string cannot be null or empty", nameof(connectionString));

            _serviceClient = new ServiceClient(connectionString);

            if (!_serviceClient.IsReady)
            {
                throw new InvalidOperationException(
                    $"Failed to connect to Dataverse: {_serviceClient.LastError}");
            }
        }

        /// <summary>
        /// 使用現有 ServiceClient 建立 Adapter（用於測試或進階情境）
        /// </summary>
        public DataverseServiceClientAdapter(ServiceClient serviceClient)
        {
            _serviceClient = serviceClient ?? throw new ArgumentNullException(nameof(serviceClient));
        }

        #endregion

        #region ICrmClient 同步實作

        public Guid Create(Entity entity)
        {
            if (entity == null)
                throw new ArgumentNullException(nameof(entity));

            return _serviceClient.Create(entity);
        }

        public void Update(Entity entity)
        {
            if (entity == null)
                throw new ArgumentNullException(nameof(entity));

            _serviceClient.Update(entity);
        }

        public void Delete(string entityName, Guid id)
        {
            if (string.IsNullOrWhiteSpace(entityName))
                throw new ArgumentException("Entity name cannot be null or empty", nameof(entityName));
            if (id == Guid.Empty)
                throw new ArgumentException("Entity ID cannot be empty", nameof(id));

            _serviceClient.Delete(entityName, id);
        }

        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
        {
            if (string.IsNullOrWhiteSpace(entityName))
                throw new ArgumentException("Entity name cannot be null or empty", nameof(entityName));
            if (id == Guid.Empty)
                throw new ArgumentException("Entity ID cannot be empty", nameof(id));
            if (columnSet == null)
                throw new ArgumentNullException(nameof(columnSet));

            return _serviceClient.Retrieve(entityName, id, columnSet);
        }

        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            if (query == null)
                throw new ArgumentNullException(nameof(query));

            return _serviceClient.RetrieveMultiple(query);
        }

        public OrganizationResponse Execute(OrganizationRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            return _serviceClient.Execute(request);
        }

        #endregion

        #region ICrmClient 非同步實作

        public async Task<Guid> CreateAsync(Entity entity, CancellationToken cancellationToken = default)
        {
            if (entity == null)
                throw new ArgumentNullException(nameof(entity));

            // ServiceClient 的 CreateAsync 支援 CancellationToken
            return await _serviceClient.CreateAsync(entity, cancellationToken).ConfigureAwait(false);
        }

        public async Task UpdateAsync(Entity entity, CancellationToken cancellationToken = default)
        {
            if (entity == null)
                throw new ArgumentNullException(nameof(entity));

            await _serviceClient.UpdateAsync(entity, cancellationToken).ConfigureAwait(false);
        }

        public async Task DeleteAsync(string entityName, Guid id, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(entityName))
                throw new ArgumentException("Entity name cannot be null or empty", nameof(entityName));
            if (id == Guid.Empty)
                throw new ArgumentException("Entity ID cannot be empty", nameof(id));

            await _serviceClient.DeleteAsync(entityName, id, cancellationToken).ConfigureAwait(false);
        }

        public async Task<Entity> RetrieveAsync(string entityName, Guid id, ColumnSet columnSet, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(entityName))
                throw new ArgumentException("Entity name cannot be null or empty", nameof(entityName));
            if (id == Guid.Empty)
                throw new ArgumentException("Entity ID cannot be empty", nameof(id));
            if (columnSet == null)
                throw new ArgumentNullException(nameof(columnSet));

            return await _serviceClient.RetrieveAsync(entityName, id, columnSet, cancellationToken).ConfigureAwait(false);
        }

        public async Task<EntityCollection> RetrieveMultipleAsync(QueryBase query, CancellationToken cancellationToken = default)
        {
            if (query == null)
                throw new ArgumentNullException(nameof(query));

            return await _serviceClient.RetrieveMultipleAsync(query, cancellationToken).ConfigureAwait(false);
        }

        public async Task<OrganizationResponse> ExecuteAsync(OrganizationRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            return await _serviceClient.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
        }

        #endregion

        #region 連線狀態

        public bool IsReady => _serviceClient?.IsReady ?? false;

        public string OrganizationName => _serviceClient?.ConnectedOrgFriendlyName ?? string.Empty;

        #endregion

        #region IDisposable 實作

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                // 釋放 ServiceClient（會關閉連線）
                _serviceClient?.Dispose();
            }

            _disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}

#endif // NETCOREAPP || NET5_0_OR_GREATER
