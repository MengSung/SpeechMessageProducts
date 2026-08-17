// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/ConnectionOperations/PooledOrganizationService.cs
// 檔案責任：提供 ASP.NET Core Scoped 的 IOrganizationService 包裝器，將每個 request
// 的 Dataverse 租借與釋放責任交給 DI scope，避免呼叫端遺漏成對 ReleaseConnection。
// 資源生命週期：建構時取得一條池化連線；Dispose 時正常歸還，傳輸狀態不確定時改為銷毀。
// 隔離保證：此型別是 Scoped，沒有 static 或跨 scope 的可變服務參考；故障連線不會被下一個
// request、使用者或 profile 重用。
// 編碼要求：本檔案需維持 UTF-8 without BOM、CRLF 與結尾 CRLF。
// ============================================================================
using System;
using System.ServiceModel;
using System.Threading;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace ToolUtilityNameSpace.ConnectionOperations
{
    /// <summary>
    /// 將一條 <see cref="IOrganizationService"/> 租約綁定到 ASP.NET Core request scope 的包裝器。
    /// 建構式是唯一的取得點，<see cref="Dispose"/> 是唯一的歸還點；因此容器在 scope 結束時
    /// 能確定釋放資源。每個執行個體只持有自己的服務參考，不使用共享可變狀態，避免跨 request、
    /// 跨使用者或跨 profile 洩漏。
    /// </summary>
    public sealed class PooledOrganizationService : IOrganizationService, IDisposable
    {
        private readonly CrmConnectionPool _pool;
        private readonly IOrganizationService _service;
        private int _disposed;
        private int _faulted;

        /// <summary>
        /// 取得本 request 專屬的池化 Dataverse 連線。
        /// 服務最長只存活至此 scoped 物件被 DI 容器釋放；若建構失敗，池不會留下未追蹤的租約。
        /// </summary>
        /// <param name="pool">全應用程式唯一的連線池；它負責建立、追蹤及最終銷毀實際通道。</param>
        /// <exception cref="ArgumentNullException">當連線池未提供時擲回，避免無法保證歸還路徑。</exception>
        public PooledOrganizationService(CrmConnectionPool pool)
        {
            _pool = pool ?? throw new ArgumentNullException(nameof(pool));
            _service = _pool.AcquireConnection();
        }

        /// <summary>
        /// 直接轉送 Dataverse 關聯操作；若底層傳輸狀態不確定，會標記租約故障以阻止連線回池。
        /// </summary>
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
        {
            Invoke(service => service.Associate(entityName, entityId, relationship, relatedEntities));
        }

        /// <summary>
        /// 直接轉送 Dataverse 建立操作；回傳底層 SDK 建立的資料列識別碼。
        /// </summary>
        public Guid Create(Entity entity)
        {
            return Invoke(service => service.Create(entity));
        }

        /// <summary>
        /// 直接轉送 Dataverse 刪除操作；傳輸逾時、取消或 WCF 通訊失敗後不允許連線重用。
        /// </summary>
        public void Delete(string entityName, Guid id)
        {
            Invoke(service => service.Delete(entityName, id));
        }

        /// <summary>
        /// 直接轉送 Dataverse 解除關聯操作，並沿用相同的故障隔離規則。
        /// </summary>
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
        {
            Invoke(service => service.Disassociate(entityName, entityId, relationship, relatedEntities));
        }

        /// <summary>
        /// 直接轉送 Dataverse 組織請求，保留 SDK 回應與例外語意。
        /// </summary>
        public OrganizationResponse Execute(OrganizationRequest request)
        {
            return Invoke(service => service.Execute(request));
        }

        /// <summary>
        /// 直接轉送 Dataverse 單筆讀取操作。
        /// </summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
        {
            return Invoke(service => service.Retrieve(entityName, id, columnSet));
        }

        /// <summary>
        /// 直接轉送 Dataverse 集合查詢操作。
        /// </summary>
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            return Invoke(service => service.RetrieveMultiple(query));
        }

        /// <summary>
        /// 直接轉送 Dataverse 更新操作。
        /// </summary>
        public void Update(Entity entity)
        {
            Invoke(service => service.Update(entity));
        }

        /// <summary>
        /// 結束此 scope 的連線租約。正常路徑歸還至原池；任何已標記的傳輸故障路徑皆由池銷毀
        /// 連線及其底層資源而不回池。重複釋放為冪等，確保 DI 與呼叫端清理競爭不會重複釋放名額。
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            if (Volatile.Read(ref _faulted) != 0)
            {
                _pool.MarkConnectionFaulted(_service);
            }

            _pool.ReleaseConnection(_service);
        }

        private T Invoke<T>(Func<IOrganizationService, T> operation)
        {
            ThrowIfDisposed();

            try
            {
                return operation(_service);
            }
            catch (Exception exception) when (IsUncertainTransportFailure(exception))
            {
                Interlocked.Exchange(ref _faulted, 1);
                throw;
            }
        }

        private void Invoke(Action<IOrganizationService> operation)
        {
            ThrowIfDisposed();

            try
            {
                operation(_service);
            }
            catch (Exception exception) when (IsUncertainTransportFailure(exception))
            {
                Interlocked.Exchange(ref _faulted, 1);
                throw;
            }
        }

        private static bool IsUncertainTransportFailure(Exception exception)
        {
            return exception is TimeoutException ||
                exception is OperationCanceledException ||
                exception is CommunicationException;
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(PooledOrganizationService));
            }
        }
    }
}
