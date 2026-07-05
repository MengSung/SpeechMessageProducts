// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/Factory/ToolUtilityFactory.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class ToolUtilityFactory
// 主要成員：SetConfiguration、GetInstance、ResetInstance
// 引用命名空間：Microsoft.Extensions.Configuration、Microsoft.Xrm.Sdk、System、ToolUtilityNameSpace.ConnectionOperations
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
