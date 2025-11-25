using Microsoft.Xrm.Sdk;
using System;
using ToolUtilityNameSpace.ConnectionOperations;

namespace ToolUtilityNameSpace.Factory
{
    /// <summary>
    /// Factory 模式實現：負責創建和管理 ToolUtilityClass 單一實例
    /// 遵循 SOLID 原則中的單一職責原則 (Single Responsibility Principle)
    /// </summary>
    public sealed class ToolUtilityFactory
    {
        private static readonly object _lock = new object();
        private static ToolUtilityClass _instance;
        private static volatile bool _isInitialized = false;

        // 私有建構函數防止外部實例化
        private ToolUtilityFactory()
        {
        }

        /// <summary>
        /// 取得 ToolUtilityClass 的單一實例 (Thread-Safe Double-Check Locking)
        /// </summary>
        /// <returns>ToolUtilityClass 實例</returns>
        public static ToolUtilityClass GetInstance()
        {
            if (!_isInitialized)
            {
                lock (_lock)
                {
                    if (!_isInitialized)
                    {
                        _instance = new ToolUtilityClass();
                        _isInitialized = true;
                    }
                }
            }
            return _instance;
        }

        /// <summary>
        /// 取得 ToolUtilityClass 的單一實例，使用指定的 DiscoveryServiceType
        /// </summary>
        /// <param name="discoveryServiceType">服務類型</param>
        /// <returns>ToolUtilityClass 實例</returns>
        public static ToolUtilityClass GetInstance(string discoveryServiceType)
        {
            if (!_isInitialized)
            {
                lock (_lock)
                {
                    if (!_isInitialized)
                    {
                        _instance = new ToolUtilityClass(discoveryServiceType);
                        _isInitialized = true;
                    }
                }
            }
            return _instance;
        }

        /// <summary>
        /// 重置實例 (僅供測試使用，生產環境不應調用)
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
