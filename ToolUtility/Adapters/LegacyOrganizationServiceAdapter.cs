using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Client;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Net;
using System.ServiceModel.Description;
using System.Threading;
using System.Threading.Tasks;
using ToolUtilityNameSpace.Interfaces;

namespace ToolUtilityNameSpace.Adapters
{
    /// <summary>
    /// Legacy OrganizationServiceProxy Adapter（.NET Framework 4.6.2 適用）
    /// 設計模式：Adapter Pattern
    /// 用途：將舊的 OrganizationServiceProxy (WCF) 包裝成 ICrmClient
    /// 注意：此 Adapter 僅作為過渡期使用，最終將移除
    /// </summary>
    /// <remarks>
    /// 此 Adapter 符合 Linus 原則：
    /// 1. 簡潔實作 - 保持最小變更，直接轉發到 OrganizationServiceProxy
    /// 2. 可回滾 - 不影響現有行為，可快速切換回舊實作
    /// 3. 漸進式遷移 - 短期保留，讓上層程式碼先改用 ICrmClient
    /// 
    /// TODO (Phase 2):
    /// - 逐步將所有呼叫遷移到 DataverseServiceClientAdapter
    /// - 移除此 Adapter 與 OrganizationServiceProxy 相依性
    /// 
    /// ?? 警告：
    /// - OrganizationServiceProxy 在 .NET Core / .NET 10 不支援
    /// - 此 Adapter 僅能在 .NET Framework 環境使用
    /// </remarks>
    public class LegacyOrganizationServiceAdapter : ICrmClient
    {
        private readonly IOrganizationService _organizationService;
        private readonly OrganizationServiceProxy _organizationServiceProxy;
        private bool _disposed = false;

        #region 建構式

        /// <summary>
        /// 使用 OrganizationServiceProxy 建立 Adapter
        /// </summary>
        public LegacyOrganizationServiceAdapter(OrganizationServiceProxy organizationServiceProxy)
        {
            _organizationServiceProxy = organizationServiceProxy ?? throw new ArgumentNullException(nameof(organizationServiceProxy));
            _organizationService = organizationServiceProxy;
        }

        /// <summary>
        /// 使用 IOrganizationService 建立 Adapter（相容 CRM 2011）
        /// </summary>
        public LegacyOrganizationServiceAdapter(IOrganizationService organizationService)
        {
            _organizationService = organizationService ?? throw new ArgumentNullException(nameof(organizationService));
            _organizationServiceProxy = organizationService as OrganizationServiceProxy;
        }

        /// <summary>
        /// 使用連線參數建立 Adapter（向後相容）
        /// </summary>
        /// <param name="serverUrl">伺服器 URL（例如：http://crm:5555/org）</param>
        /// <param name="domain">網域</param>
        /// <param name="username">使用者名稱</param>
        /// <param name="password">密碼</param>
        public LegacyOrganizationServiceAdapter(string serverUrl, string domain, string username, string password)
        {
            if (string.IsNullOrWhiteSpace(serverUrl))
                throw new ArgumentException("Server URL cannot be null or empty", nameof(serverUrl));

            var uri = new Uri(serverUrl);
            var credentials = new ClientCredentials
            {
                Windows = { ClientCredential = new NetworkCredential(username, password, domain) }
            };

            var serviceManagement = ServiceConfigurationFactory.CreateConfiguration<IOrganizationService>(uri);
            _organizationServiceProxy = new OrganizationServiceProxy(serviceManagement, credentials);
            _organizationService = _organizationServiceProxy;
        }

        #endregion

        #region ICrmClient 同步實作

        public Guid Create(Entity entity)
        {
            if (entity == null)
                throw new ArgumentNullException(nameof(entity));

            return _organizationService.Create(entity);
        }

        public void Update(Entity entity)
        {
            if (entity == null)
                throw new ArgumentNullException(nameof(entity));

            _organizationService.Update(entity);
        }

        public void Delete(string entityName, Guid id)
        {
            if (string.IsNullOrWhiteSpace(entityName))
                throw new ArgumentException("Entity name cannot be null or empty", nameof(entityName));
            if (id == Guid.Empty)
                throw new ArgumentException("Entity ID cannot be empty", nameof(id));

            _organizationService.Delete(entityName, id);
        }

        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
        {
            if (string.IsNullOrWhiteSpace(entityName))
                throw new ArgumentException("Entity name cannot be null or empty", nameof(entityName));
            if (id == Guid.Empty)
                throw new ArgumentException("Entity ID cannot be empty", nameof(id));
            if (columnSet == null)
                throw new ArgumentNullException(nameof(columnSet));

            return _organizationService.Retrieve(entityName, id, columnSet);
        }

        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            if (query == null)
                throw new ArgumentNullException(nameof(query));

            return _organizationService.RetrieveMultiple(query);
        }

        public OrganizationResponse Execute(OrganizationRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            return _organizationService.Execute(request);
        }

        #endregion

        #region ICrmClient 非同步實作 (同步方法包裝)

        // ?? 注意：OrganizationServiceProxy 不支援真正的非同步
        // 這裡使用 Task.Run 包裝同步方法（不是真正的非同步 I/O）
        // 在遷移到 DataverseServiceClientAdapter 後會改用真正的非同步

        public Task<Guid> CreateAsync(Entity entity, CancellationToken cancellationToken = default)
        {
            return Task.Run(() => Create(entity), cancellationToken);
        }

        public Task UpdateAsync(Entity entity, CancellationToken cancellationToken = default)
        {
            return Task.Run(() => Update(entity), cancellationToken);
        }

        public Task DeleteAsync(string entityName, Guid id, CancellationToken cancellationToken = default)
        {
            return Task.Run(() => Delete(entityName, id), cancellationToken);
        }

        public Task<Entity> RetrieveAsync(string entityName, Guid id, ColumnSet columnSet, CancellationToken cancellationToken = default)
        {
            return Task.Run(() => Retrieve(entityName, id, columnSet), cancellationToken);
        }

        public Task<EntityCollection> RetrieveMultipleAsync(QueryBase query, CancellationToken cancellationToken = default)
        {
            return Task.Run(() => RetrieveMultiple(query), cancellationToken);
        }

        public Task<OrganizationResponse> ExecuteAsync(OrganizationRequest request, CancellationToken cancellationToken = default)
        {
            return Task.Run(() => Execute(request), cancellationToken);
        }

        #endregion

        #region 連線狀態

        public bool IsReady
        {
            get
            {
                try
                {
                    // 簡單測試：嘗試執行 WhoAmI
                    if (_organizationService == null) return false;
                    
                    // OrganizationServiceProxy 沒有 IsReady 屬性，假設已連線
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        public string OrganizationName
        {
            get
            {
                // OrganizationServiceProxy 沒有直接取得組織名稱的方法
                // 可透過 ServiceConfiguration 取得，但較複雜
                return _organizationServiceProxy?.ServiceConfiguration?.CurrentServiceEndpoint?.Address?.Uri?.Host ?? "Unknown";
            }
        }

        #endregion

        #region IDisposable 實作

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                // 釋放 OrganizationServiceProxy
                _organizationServiceProxy?.Dispose();
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
