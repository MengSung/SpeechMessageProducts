// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/WebServiceConnector/Converters/IdentityConverter.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class IdentityConverter
// 主要成員：IndexToIdentity、IndexToSpiritualIdentity、IndexToIdentityFallback、IndexToClearIdentity、IndexToBaptizedSituation、IsNewComerOrUnGrouped、IsClosedCase
// 引用命名空間：System、Microsoft.Extensions.Caching.Memory、Microsoft.Xrm.Sdk
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Xrm.Sdk;

namespace ChurchReport.WebServiceConnector.Converters
{
    /// <summary>
    /// 委身類型相關的數值與文字轉換器
    /// 遵循 Linus 代碼原則：單一職責、小型函數
    /// </summary>
    public class IdentityConverter
    {
        private readonly IOrganizationService _organizationService;
        private readonly IMemoryCache _cache;

        /// <summary>
        /// 初始化委身類型轉換器
        /// </summary>
        /// <param name="organizationService">CRM 服務（用於動態查詢）</param>
        /// <param name="cache">快取服務</param>
        public IdentityConverter(IOrganizationService organizationService, IMemoryCache cache = null)
        {
            _organizationService = organizationService;
            _cache = cache ?? new MemoryCache(new MemoryCacheOptions());
        }

        #region 委身類型轉換 (動態查詢)

        /// <summary>
        /// 將委身類型數值轉換為顯示文字（使用動態查詢）
        /// </summary>
        public string IndexToIdentity(int identity)
        {
            try
            {
                var optionSetService = new ChurchReport.Services.OptionSetMetadataService(
                    _organizationService,
                    null,
                    _cache
                );

                string displayText = optionSetService.GetOptionSetText("contact", "customertypecode", identity);
                System.Diagnostics.Debug.WriteLine($"[IdentityConverter.IndexToIdentity] 輸入值: {identity}, 回傳文字: {displayText}");

                return displayText;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[IdentityConverter.IndexToIdentity] 動態查詢失敗: {ex.Message}");
                return IndexToIdentityFallback(identity);
            }
        }

        /// <summary>
        /// 將受洗狀態數值轉換為顯示文字（使用動態查詢）
        /// </summary>
        public string IndexToSpiritualIdentity(int spiritualIdentity)
        {
            try
            {
                var optionSetService = new ChurchReport.Services.OptionSetMetadataService(
                    _organizationService,
                    null,
                    _cache
                );

                string displayText = optionSetService.GetOptionSetText("contact", "new_spiriitual_identity", spiritualIdentity);
                System.Diagnostics.Debug.WriteLine($"[IdentityConverter.IndexToSpiritualIdentity] 輸入值: {spiritualIdentity}, 回傳文字: {displayText}");

                return displayText;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[IdentityConverter.IndexToSpiritualIdentity] 動態查詢失敗: {ex.Message}");
                return "-未知-";
            }
        }

        #endregion

        #region 備用的硬編碼對應表

        /// <summary>
        /// 委身類型備用對應表（當動態查詢失敗時使用）
        /// </summary>
        private static string IndexToIdentityFallback(int identity)
        {
            return identity switch
            {
                100000000 => "08. 新朋友",
                100000001 => "10. 未入組結案",
                100000002 => "02. 小組長",
                100000003 => "01. 族系組長",
                100000004 => "07. 未入組",
                100000005 => "03. 見習小組長",
                100000006 => "04. 小家長",
                100000007 => "09. 外教會",
                1 => "05. 小組組員",
                2 => "06. 未成年",
                _ => "未知類型"
            };
        }

        /// <summary>
        /// 將委身類型數值轉換為簡化的易懂文字
        /// </summary>
        public static string IndexToClearIdentity(int identity)
        {
            return identity switch
            {
                100000000 => "新朋友",
                100000004 => "未入組",
                100000007 => "未入組", // 外教會歸類為未入組
                1 => "小組組員",
                _ => "小組組員"
            };
        }

        /// <summary>
        /// 洗禮狀態轉換（長老教會專用）
        /// </summary>
        public static string IndexToBaptizedSituation(int baptizedSituation)
        {
            return baptizedSituation switch
            {
                100000000 => "堅信禮(籍在)",
                100000001 => "成人禮(籍在)",
                100000002 => "成人禮(籍在)",
                100000003 => "小兒禮(籍不在)",
                100000004 => "未受洗(籍不在)",
                _ => "未受洗(籍不在)"
            };
        }

        #endregion

        #region 委身類型驗證

        /// <summary>
        /// 驗證是否為新人或未入組
        /// </summary>
        public static bool IsNewComerOrUnGrouped(int identityNumber)
        {
            // 100000000 = 新朋友
            // 100000004 = 未入組
            return identityNumber == 100000000 || identityNumber == 100000004;
        }

        /// <summary>
        /// 驗證是否為結案狀態
        /// </summary>
        public static bool IsClosedCase(string identityText)
        {
            return identityText.Contains("結案");
        }

        #endregion
    }
}
