// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/ConnectionOperations/CrmConnectionPool.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class CrmConnectionPool、class PooledConnection
// 主要成員：AcquireConnection、ReleaseConnection、GetStats、ValidateConnection、InitializeMinConnections、CreateConnection、IsConnectionHealthy、CleanupIdleConnections、DisposeConnection、TryReserveConnectionSlot
// 引用命名空間：Microsoft.Crm.Sdk.Messages、Microsoft.Xrm.Sdk、System、System.Collections.Concurrent、System.Collections.Generic、System.Threading
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace ToolUtilityNameSpace.ConnectionOperations
{
    /// <summary>
    /// CRM 連線池實作，採用 Object Pool Pattern。
    /// 設計目標是讓高併發情境下的 CRM 連線具備可重用、可控成長與可觀測特性，
    /// 並降低每次借還連線時的固定成本。
    /// </summary>
    public class CrmConnectionPool : ICrmConnectionPool
    {
        private readonly ConcurrentBag<PooledConnection> _connections;
        private readonly ConcurrentDictionary<IOrganizationService, PooledConnection> _connectionLookup;
        private readonly SemaphoreSlim _semaphore;
        private readonly ICrmConnectionService _connectionService;
        private readonly string _serverUrl;
        private readonly string _username;
        private readonly string _password;
        private readonly int _minPoolSize;
        private readonly int _maxPoolSize;
        private readonly TimeSpan _connectionTimeout;
        private readonly TimeSpan _idleTimeout;
        private readonly TimeSpan _healthCheckInterval;
        private int _currentSize;
        private int _waitingRequests;
        private bool _disposed;
        private readonly Timer _cleanupTimer;
        private readonly ConnectionPoolStats _stats;
        private readonly object _statsLock = new object();

        /// <summary>
        /// 建立 CRM 連線池。
        /// </summary>
        public CrmConnectionPool(
            ICrmConnectionService connectionService,
            string serverUrl,
            string username,
            string password,
            int minPoolSize = 3,
            int maxPoolSize = 20,
            TimeSpan? connectionTimeout = null,
            TimeSpan? idleTimeout = null)
        {
            _connectionService = connectionService ?? throw new ArgumentNullException(nameof(connectionService));
            _serverUrl = serverUrl ?? throw new ArgumentNullException(nameof(serverUrl));
            _username = username ?? throw new ArgumentNullException(nameof(username));
            _password = password ?? throw new ArgumentNullException(nameof(password));

            if (minPoolSize < 1)
                throw new ArgumentException("Minimum pool size must be greater than 0.", nameof(minPoolSize));
            if (maxPoolSize < minPoolSize)
                throw new ArgumentException("Maximum pool size must be greater than or equal to minimum pool size.", nameof(maxPoolSize));

            _minPoolSize = minPoolSize;
            _maxPoolSize = maxPoolSize;
            _connectionTimeout = connectionTimeout ?? TimeSpan.FromSeconds(30);
            _idleTimeout = idleTimeout ?? TimeSpan.FromMinutes(10);
            _healthCheckInterval = TimeSpan.FromSeconds(30);

            _connections = new ConcurrentBag<PooledConnection>();
            _connectionLookup = new ConcurrentDictionary<IOrganizationService, PooledConnection>();
            _semaphore = new SemaphoreSlim(maxPoolSize, maxPoolSize);
            _currentSize = 0;

            _stats = new ConnectionPoolStats
            {
                CreatedAt = DateTime.UtcNow,
                LastActivityAt = DateTime.UtcNow
            };

            InitializeMinConnections();
            _cleanupTimer = new Timer(CleanupIdleConnections, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }

        /// <summary>
        /// 從連線池取得可用連線。
        /// 優先重用閒置連線，必要時才建立新連線。
        /// 健康檢查採節流策略，避免每次借出都執行昂貴驗證。
        /// </summary>
        public IOrganizationService AcquireConnection()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(CrmConnectionPool));

            Interlocked.Increment(ref _waitingRequests);
            try
            {
                if (!_semaphore.Wait(_connectionTimeout))
                {
                    lock (_statsLock)
                    {
                        _stats.TimeoutCount++;
                    }

                    throw new TimeoutException($"Unable to acquire a CRM connection within {_connectionTimeout.TotalSeconds} seconds.");
                }
            }
            finally
            {
                Interlocked.Decrement(ref _waitingRequests);
            }

            try
            {
                IOrganizationService service = null;
                PooledConnection pooledConnection = null;
                var now = DateTime.UtcNow;

                while (_connections.TryTake(out pooledConnection))
                {
                    if (IsConnectionHealthy(pooledConnection, now))
                    {
                        pooledConnection.IsInUse = true;
                        pooledConnection.LastUsedAt = now;
                        service = pooledConnection.Service;
                        break;
                    }

                    DisposeConnection(pooledConnection);
                    lock (_statsLock)
                    {
                        _stats.ValidationFailureCount++;
                    }
                }

                if (service == null)
                {
                    pooledConnection = CreateConnection();
                    pooledConnection.IsInUse = true;
                    pooledConnection.LastUsedAt = now;
                    service = pooledConnection.Service;
                }

                lock (_statsLock)
                {
                    _stats.TotalAcquireCount++;
                    _stats.LastActivityAt = now;
                }

                if (service == null)
                {
                    _semaphore.Release();
                    throw new InvalidOperationException("Unable to acquire a valid CRM connection.");
                }

                return service;
            }
            catch
            {
                _semaphore.Release();
                throw;
            }
        }

        /// <summary>
        /// 將連線歸還至連線池。
        /// 若此連線原本不是由連線池建立，仍會暫時納入追蹤，
        /// 以維持借還流程的一致性。
        /// </summary>
        public void ReleaseConnection(IOrganizationService service)
        {
            if (_disposed)
                return;

            if (service == null)
                throw new ArgumentNullException(nameof(service));

            try
            {
                var now = DateTime.UtcNow;
                if (!_connectionLookup.TryGetValue(service, out var pooledConnection))
                {
                    pooledConnection = new PooledConnection
                    {
                        Service = service,
                        CreatedAt = now,
                        LastUsedAt = now,
                        LastValidatedAt = now,
                        PoolOwned = false
                    };

                    _connectionLookup.TryAdd(service, pooledConnection);
                }

                pooledConnection.IsInUse = false;
                pooledConnection.LastUsedAt = now;
                _connections.Add(pooledConnection);

                lock (_statsLock)
                {
                    _stats.TotalReleaseCount++;
                    _stats.LastActivityAt = now;
                }
            }
            finally
            {
                _semaphore.Release();
            }
        }

        /// <summary>
        /// 取得目前連線池統計資料。
        /// </summary>
        public ConnectionPoolStats GetStats()
        {
            lock (_statsLock)
            {
                var idleCount = _connections.Count;
                var activeCount = Math.Max(0, _maxPoolSize - _semaphore.CurrentCount);
                var totalConnections = Math.Max(Volatile.Read(ref _currentSize), idleCount + activeCount);

                _stats.TotalConnections = totalConnections;
                _stats.ActiveConnections = activeCount;
                _stats.IdleConnections = idleCount;
                _stats.WaitingRequests = Volatile.Read(ref _waitingRequests);

                return new ConnectionPoolStats
                {
                    TotalConnections = _stats.TotalConnections,
                    ActiveConnections = _stats.ActiveConnections,
                    IdleConnections = _stats.IdleConnections,
                    WaitingRequests = _stats.WaitingRequests,
                    CreatedAt = _stats.CreatedAt,
                    LastActivityAt = _stats.LastActivityAt,
                    TotalAcquireCount = _stats.TotalAcquireCount,
                    TotalReleaseCount = _stats.TotalReleaseCount,
                    TimeoutCount = _stats.TimeoutCount,
                    ValidationFailureCount = _stats.ValidationFailureCount
                };
            }
        }

        /// <summary>
        /// 立即驗證指定連線是否有效。
        /// </summary>
        public bool ValidateConnection(IOrganizationService service)
        {
            if (service == null)
                return false;

            try
            {
                var request = new WhoAmIRequest();
                var response = (WhoAmIResponse)service.Execute(request);
                return response.UserId != Guid.Empty;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 預先建立最小數量的連線，降低第一波流量的冷啟動延遲。
        /// </summary>
        private void InitializeMinConnections()
        {
            for (int i = 0; i < _minPoolSize; i++)
            {
                try
                {
                    var connection = CreateConnection();
                    _connections.Add(connection);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to initialize pooled CRM connection: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 建立新的池化連線並登錄到查找表。
        /// </summary>
        private PooledConnection CreateConnection()
        {
            if (!TryReserveConnectionSlot())
            {
                throw new InvalidOperationException("CRM connection pool reached the configured maximum size.");
            }

            try
            {
                var now = DateTime.UtcNow;
                var service = _connectionService.CreateOnPremiseClient(_serverUrl, _username, _password);
                var connection = new PooledConnection
                {
                    Service = service,
                    CreatedAt = now,
                    LastUsedAt = now,
                    LastValidatedAt = now,
                    IsInUse = false,
                    PoolOwned = true
                };

                _connectionLookup[service] = connection;
                return connection;
            }
            catch
            {
                Interlocked.Decrement(ref _currentSize);
                throw;
            }
        }

        /// <summary>
        /// 檢查連線健康狀態。
        /// 若距離上次驗證仍在節流區間內，直接視為可用，
        /// 以避免在高頻借用路徑上重複執行 WhoAmI。
        /// </summary>
        private bool IsConnectionHealthy(PooledConnection connection, DateTime now)
        {
            if (connection?.Service == null)
                return false;

            if ((now - connection.LastValidatedAt) < _healthCheckInterval)
            {
                return true;
            }

            try
            {
                var request = new WhoAmIRequest();
                var response = (WhoAmIResponse)connection.Service.Execute(request);
                var isHealthy = response.UserId != Guid.Empty;
                if (isHealthy)
                {
                    connection.LastValidatedAt = now;
                }

                return isHealthy;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 清理長時間閒置的池內連線。
        /// 僅會清理由池自行建立，且超過最小池大小的閒置連線。
        /// </summary>
        private void CleanupIdleConnections(object state)
        {
            if (_disposed)
                return;

            try
            {
                var now = DateTime.UtcNow;
                var connectionsToRemove = new List<PooledConnection>();
                var tempConnections = new List<PooledConnection>();

                while (_connections.TryTake(out var connection))
                {
                    if (!connection.IsInUse &&
                        connection.PoolOwned &&
                        (now - connection.LastUsedAt) > _idleTimeout &&
                        Volatile.Read(ref _currentSize) > _minPoolSize)
                    {
                        connectionsToRemove.Add(connection);
                    }
                    else
                    {
                        tempConnections.Add(connection);
                    }
                }

                foreach (var connection in tempConnections)
                {
                    _connections.Add(connection);
                }

                foreach (var connection in connectionsToRemove)
                {
                    DisposeConnection(connection);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to clean idle CRM connections: {ex.Message}");
            }
        }

        /// <summary>
        /// 釋放單一池化連線，並同步更新查找表與大小計數。
        /// </summary>
        private void DisposeConnection(PooledConnection connection)
        {
            try
            {
                if (connection?.Service != null)
                {
                    _connectionLookup.TryRemove(connection.Service, out _);
                }

                (connection?.Service as IDisposable)?.Dispose();
                if (connection != null && connection.PoolOwned)
                {
                    Interlocked.Decrement(ref _currentSize);
                }
            }
            catch
            {
                // 釋放失敗不再往外拋，避免清理流程中斷。
            }
        }

        /// <summary>
        /// 嘗試預留一個新的連線名額，避免多執行緒同時超額建立連線。
        /// </summary>
        private bool TryReserveConnectionSlot()
        {
            while (true)
            {
                var currentSize = Volatile.Read(ref _currentSize);
                if (currentSize >= _maxPoolSize)
                {
                    return false;
                }

                if (Interlocked.CompareExchange(ref _currentSize, currentSize + 1, currentSize) == currentSize)
                {
                    return true;
                }
            }
        }

        /// <summary>
        /// 釋放整個連線池。
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _cleanupTimer?.Dispose();
            _semaphore?.Dispose();

            while (_connections.TryTake(out var connection))
            {
                DisposeConnection(connection);
            }
        }

        /// <summary>
        /// 連線池內部使用的池化連線包裝物件。
        /// </summary>
        private class PooledConnection
        {
            /// <summary>
            /// 實際的 CRM 服務連線。
            /// </summary>
            public IOrganizationService Service { get; set; }

            /// <summary>
            /// 連線建立時間。
            /// </summary>
            public DateTime CreatedAt { get; set; }

            /// <summary>
            /// 最後一次借出或歸還時間。
            /// </summary>
            public DateTime LastUsedAt { get; set; }

            /// <summary>
            /// 最後一次健康檢查通過時間。
            /// </summary>
            public DateTime LastValidatedAt { get; set; }

            /// <summary>
            /// 是否正被外部使用中。
            /// </summary>
            public bool IsInUse { get; set; }

            /// <summary>
            /// 是否由連線池自行建立。
            /// </summary>
            public bool PoolOwned { get; set; }
        }
    }
}
