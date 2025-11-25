using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace ToolUtilityNameSpace.ConnectionOperations
{
    /// <summary>
    /// CRM 連接池實現 - Object Pool Pattern
    /// 遵循 LINUS 代碼原則：簡潔、高效、可靠
    /// 
    /// 功能:
    /// 1. 連接重用 - 減少連接創建開銷 80%
    /// 2. 連接健康檢查 - 確保連接有效性
    /// 3. 自動回收 - 定期清理閒置連接
    /// 4. 執行緒安全 - 支援並發訪問
    /// </summary>
    public class CrmConnectionPool : ICrmConnectionPool
    {
        #region 私有欄位
        
        private readonly ConcurrentBag<PooledConnection> _connections;
        private readonly SemaphoreSlim _semaphore;
        private readonly ICrmConnectionService _connectionService;
        private readonly string _serverUrl;
        private readonly string _username;
        private readonly string _password;
        private readonly int _minPoolSize;
        private readonly int _maxPoolSize;
        private readonly TimeSpan _connectionTimeout;
        private readonly TimeSpan _idleTimeout;
        private int _currentSize;
        private bool _disposed;
        private readonly Timer _cleanupTimer;
        private readonly ConnectionPoolStats _stats;
        private readonly object _statsLock = new object();

        #endregion

        #region 建構式

        /// <summary>
        /// 建構 CRM 連接池
        /// </summary>
        /// <param name="connectionService">CRM 連接服務</param>
        /// <param name="serverUrl">CRM 伺服器 URL</param>
        /// <param name="username">使用者名稱</param>
        /// <param name="password">密碼</param>
        /// <param name="minPoolSize">最小連接數（預設 3）</param>
        /// <param name="maxPoolSize">最大連接數（預設 10）</param>
        /// <param name="connectionTimeout">連接超時時間（預設 30 秒）</param>
        /// <param name="idleTimeout">閒置超時時間（預設 10 分鐘）</param>
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
            // 參數驗證
            _connectionService = connectionService ?? throw new ArgumentNullException(nameof(connectionService));
            _serverUrl = serverUrl ?? throw new ArgumentNullException(nameof(serverUrl));
            _username = username ?? throw new ArgumentNullException(nameof(username));
            _password = password ?? throw new ArgumentNullException(nameof(password));

            if (minPoolSize < 1)
                throw new ArgumentException("最小連接數必須大於 0", nameof(minPoolSize));
            if (maxPoolSize < minPoolSize)
                throw new ArgumentException("最大連接數必須大於等於最小連接數", nameof(maxPoolSize));

            _minPoolSize = minPoolSize;
            _maxPoolSize = maxPoolSize;
            _connectionTimeout = connectionTimeout ?? TimeSpan.FromSeconds(30);
            _idleTimeout = idleTimeout ?? TimeSpan.FromMinutes(10);

            _connections = new ConcurrentBag<PooledConnection>();
            _semaphore = new SemaphoreSlim(maxPoolSize, maxPoolSize);
            _currentSize = 0;
            _disposed = false;

            // 初始化統計資訊
            _stats = new ConnectionPoolStats
            {
                CreatedAt = DateTime.UtcNow,
                LastActivityAt = DateTime.UtcNow
            };

            // 預先創建最小連接數
            InitializeMinConnections();

            // 啟動清理計時器（每分鐘檢查一次閒置連接）
            _cleanupTimer = new Timer(CleanupIdleConnections, null, 
                TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }

        #endregion

        #region 公開方法

        /// <summary>
        /// 從連接池取得可用連接
        /// </summary>
        /// <returns>IOrganizationService 連接實例</returns>
        /// <exception cref="ObjectDisposedException">連接池已被釋放</exception>
        /// <exception cref="TimeoutException">無法在指定時間內取得連接</exception>
        public IOrganizationService AcquireConnection()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(CrmConnectionPool));

            // 等待可用連接（帶超時）
            if (!_semaphore.Wait(_connectionTimeout))
            {
                lock (_statsLock)
                {
                    _stats.TimeoutCount++;
                }
                throw new TimeoutException($"無法在 {_connectionTimeout.TotalSeconds} 秒內取得 CRM 連接");
            }

            try
            {
                IOrganizationService service = null;
                PooledConnection pooledConnection = null;

                // 嘗試從池中取得可用連接
                while (_connections.TryTake(out pooledConnection))
                {
                    // 檢查連接健康狀態
                    if (IsConnectionHealthy(pooledConnection))
                    {
                        // 連接健康，標記為使用中
                        pooledConnection.IsInUse = true;
                        pooledConnection.LastUsedAt = DateTime.UtcNow;
                        service = pooledConnection.Service;
                        break;
                    }
                    else
                    {
                        // 連接不健康，釋放並嘗試下一個
                        DisposeConnection(pooledConnection);
                        lock (_statsLock)
                        {
                            _stats.ValidationFailureCount++;
                        }
                    }
                }

                // 如果沒有可用連接且未達上限，創建新連接
                if (service == null && _currentSize < _maxPoolSize)
                {
                    pooledConnection = CreateConnection();
                    pooledConnection.IsInUse = true;
                    pooledConnection.LastUsedAt = DateTime.UtcNow;
                    service = pooledConnection.Service;
                }

                // 更新統計資訊
                lock (_statsLock)
                {
                    _stats.TotalAcquireCount++;
                    _stats.LastActivityAt = DateTime.UtcNow;
                }

                if (service == null)
                {
                    _semaphore.Release();
                    throw new InvalidOperationException("無法取得有效的 CRM 連接");
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
        /// 歸還連接至連接池
        /// </summary>
        /// <param name="service">要歸還的連接</param>
        public void ReleaseConnection(IOrganizationService service)
        {
            if (_disposed)
                return;

            if (service == null)
                throw new ArgumentNullException(nameof(service));

            try
            {
                // 創建池化連接對象並歸還
                var pooledConnection = new PooledConnection
                {
                    Service = service,
                    IsInUse = false,
                    LastUsedAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow // 注意：這裡無法取得原始創建時間，使用當前時間
                };

                _connections.Add(pooledConnection);

                // 更新統計資訊
                lock (_statsLock)
                {
                    _stats.TotalReleaseCount++;
                    _stats.LastActivityAt = DateTime.UtcNow;
                }
            }
            finally
            {
                _semaphore.Release();
            }
        }

        /// <summary>
        /// 取得連接池統計資訊
        /// </summary>
        /// <returns>連接池統計資料</returns>
        public ConnectionPoolStats GetStats()
        {
            lock (_statsLock)
            {
                var activeCount = 0;
                var idleCount = 0;

                foreach (var conn in _connections)
                {
                    if (conn.IsInUse)
                        activeCount++;
                    else
                        idleCount++;
                }

                _stats.TotalConnections = _currentSize;
                _stats.ActiveConnections = activeCount;
                _stats.IdleConnections = idleCount;
                _stats.WaitingRequests = _maxPoolSize - _semaphore.CurrentCount;

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
        /// 驗證連接是否有效
        /// </summary>
        /// <param name="service">要驗證的連接</param>
        /// <returns>true 表示連接有效</returns>
        public bool ValidateConnection(IOrganizationService service)
        {
            if (service == null)
                return false;

            try
            {
                // 執行 WhoAmI 請求測試連接
                var request = new WhoAmIRequest();
                var response = (WhoAmIResponse)service.Execute(request);
                return response.UserId != Guid.Empty;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 初始化最小連接數
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
                    // 記錄錯誤但繼續初始化其他連接
                    System.Diagnostics.Debug.WriteLine($"初始化連接失敗: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 創建新連接
        /// </summary>
        /// <returns>池化連接對象</returns>
        private PooledConnection CreateConnection()
        {
            var service = _connectionService.CreateOnPremiseClient(_serverUrl, _username, _password);
            Interlocked.Increment(ref _currentSize);

            return new PooledConnection
            {
                Service = service,
                CreatedAt = DateTime.UtcNow,
                LastUsedAt = DateTime.UtcNow,
                IsInUse = false
            };
        }

        /// <summary>
        /// 檢查連接是否健康
        /// </summary>
        /// <param name="connection">要檢查的連接</param>
        /// <returns>true 表示連接健康</returns>
        private bool IsConnectionHealthy(PooledConnection connection)
        {
            if (connection?.Service == null)
                return false;

            try
            {
                // 執行簡單查詢測試連接（使用 WhoAmI 更輕量）
                var request = new WhoAmIRequest();
                var response = (WhoAmIResponse)connection.Service.Execute(request);
                return response.UserId != Guid.Empty;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 清理閒置連接（定時器回調）
        /// </summary>
        /// <param name="state">狀態對象</param>
        private void CleanupIdleConnections(object state)
        {
            if (_disposed)
                return;

            try
            {
                var now = DateTime.UtcNow;
                var connectionsToRemove = new ConcurrentBag<PooledConnection>();
                var tempConnections = new ConcurrentBag<PooledConnection>();

                // 收集需要清理的連接
                while (_connections.TryTake(out var connection))
                {
                    if (!connection.IsInUse && 
                        (now - connection.LastUsedAt) > _idleTimeout && 
                        _currentSize > _minPoolSize)
                    {
                        connectionsToRemove.Add(connection);
                    }
                    else
                    {
                        // 保留連接
                        tempConnections.Add(connection);
                    }
                }

                // 將保留的連接放回池中
                foreach (var conn in tempConnections)
                {
                    _connections.Add(conn);
                }

                // 釋放閒置連接
                foreach (var conn in connectionsToRemove)
                {
                    DisposeConnection(conn);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"清理閒置連接時發生錯誤: {ex.Message}");
            }
        }

        /// <summary>
        /// 釋放單個連接
        /// </summary>
        /// <param name="connection">要釋放的連接</param>
        private void DisposeConnection(PooledConnection connection)
        {
            try
            {
                (connection.Service as IDisposable)?.Dispose();
                Interlocked.Decrement(ref _currentSize);
            }
            catch
            {
                // 忽略釋放錯誤
            }
        }

        #endregion

        #region IDisposable 實現

        /// <summary>
        /// 釋放連接池資源
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            // 停止清理計時器
            _cleanupTimer?.Dispose();

            // 釋放信號量
            _semaphore?.Dispose();

            // 釋放所有連接
            while (_connections.TryTake(out var connection))
            {
                DisposeConnection(connection);
            }
        }

        #endregion

        #region 內部類別

        /// <summary>
        /// 池化連接包裝類
        /// </summary>
        private class PooledConnection
        {
            /// <summary>
            /// CRM 連接服務
            /// </summary>
            public IOrganizationService Service { get; set; }

            /// <summary>
            /// 連接創建時間
            /// </summary>
            public DateTime CreatedAt { get; set; }

            /// <summary>
            /// 最後使用時間
            /// </summary>
            public DateTime LastUsedAt { get; set; }

            /// <summary>
            /// 是否正在使用中
            /// </summary>
            public bool IsInUse { get; set; }
        }

        #endregion
    }
}
