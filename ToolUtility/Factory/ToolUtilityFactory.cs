using Microsoft.Extensions.Configuration;
using Microsoft.Xrm.Sdk;
using System;
using ToolUtilityNameSpace.ConnectionOperations;

namespace ToolUtilityNameSpace.Factory
{
    /// <summary>
    /// Factory 負責建立、管理及防護管理 ToolUtilityClass 的實例
    /// 遵守 SOLID 的單一職責原則 (Single Responsibility Principle)
    /// </summary>
    public sealed class ToolUtilityFactory
    {
        private static readonly object _lock = new object();
        private static ToolUtilityClass _instance;
        private static volatile bool _isInitialized = false;
        private static IConfiguration _configuration;

        // 私有建構函式防止外部建立實例
        private ToolUtilityFactory()
        {
        }

        /// <summary>
        /// 設定 IConfiguration 實例
        /// </summary>
        /// <param name="configuration">配置物件</param>
        public static void SetConfiguration(IConfiguration configuration)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        /// <summary>
        /// 獲得 ToolUtilityClass 的單一實例 (Thread-Safe Double-Check Locking)
        /// </summary>
        /// <returns>ToolUtilityClass 實例</returns>
        public static ToolUtilityClass GetInstance()
        {
            if (_configuration == null)
            {
                throw new InvalidOperationException("配置尚未設定。請先調用 SetConfiguration() 方法。");
            }

            if (!_isInitialized)
            {
                lock (_lock)
                {
                    if (!_isInitialized)
                    {
                        _instance = new ToolUtilityClass(_configuration);
                        _isInitialized = true;
                    }
                }
            }
            return _instance;
        }

        /// <summary>
        /// 獲得 ToolUtilityClass 的單一實例，使用指定的 DiscoveryServiceType
        /// </summary>
        /// <param name="discoveryServiceType">服務類型</param>
        /// <returns>ToolUtilityClass 實例</returns>
        public static ToolUtilityClass GetInstance(string discoveryServiceType)
        {
            if (_configuration == null)
            {
                throw new InvalidOperationException("配置尚未設定。請先調用 SetConfiguration() 方法。");
            }

            if (!_isInitialized)
            {
                lock (_lock)
                {
                    if (!_isInitialized)
                    {
                        _instance = new ToolUtilityClass(discoveryServiceType, _configuration);
                        _isInitialized = true;
                    }
                }
            }
            return _instance;
        }

        /// <summary>
        /// 重設實例 (僅供測試使用，生產環境不建議呼叫)
        /// </summary>
        internal static void ResetInstance()
        {
            lock (_lock)
            {
                if (_instance != null)
                {
                    _instance.Dispose();
                    _instance = null;
                }
                _isInitialized = false;
            }
        }

        /// <summary>
        /// 檢查是否已經初始化
        /// </summary>
        public static bool IsInitialized => _isInitialized;
    }
}
