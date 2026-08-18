// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/Utilities/StringUtility.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class StringUtility
// 主要成員：DeleteLastComma、FilterDigit、DeleteLastChar、DeletePresentRate、TrimPresentRate
// 引用命名空間：System、System.Linq、System.Text.RegularExpressions
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace ToolUtilityNameSpace.Utilities
{
    /// <summary>
    /// 字串工具類 - 提供常用的字串處理功能
    /// </summary>
    public static class StringUtility
    {
        /// <summary>
        /// 刪除字串最後一個逗號（支援中文全形逗號和英文逗號）
        /// </summary>
        public static void DeleteLastComma(ref string stringToProcess)
        {
            if (stringToProcess == null) return;
            if (stringToProcess.Length == 0) return;

            // 尋找最後一個逗號（中文或英文）
            int lastIndexChinese = stringToProcess.LastIndexOf('，');
            int lastIndexEnglish = stringToProcess.LastIndexOf(',');
            int lastIndex = Math.Max(lastIndexChinese, lastIndexEnglish);

            // 使用 >= 0：當整個字串就是一個逗號（索引 0）時，結果應為空字串。
            // 原本的 > 0 會讓 "，" 保持不變，屬 off-by-one。
            // 影響範圍僅限「最後一個逗號位於索引 0」的字串，其餘行為不變。
            if (lastIndex >= 0)
            {
                stringToProcess = stringToProcess.Substring(0, lastIndex);
            }
        }

        /// <summary>
        /// 過濾出字串中的所有數字
        /// </summary>
        public static string FilterDigit(string filteredString)
        {
            if (string.IsNullOrEmpty(filteredString)) return string.Empty;

            // 使用 Regex 移除所有非數字字元
            Regex digitsOnly = new Regex(@"[^\d]");
            return digitsOnly.Replace(filteredString, "");
        }

        /// <summary>
        /// 刪除字串的最後一個字元
        /// </summary>
        public static void DeleteLastChar(ref string stringToProcess)
        {
            if (stringToProcess == null) return;
            if (stringToProcess.Length == 0) return;

            int length = stringToProcess.Length;
            if (length > 0)
            {
                stringToProcess = stringToProcess.Substring(0, length - 1);
            }
        }

        /// <summary>
        /// 刪除字串中的主日出席率資訊（靜態方法）
        /// </summary>
        public static string DeletePresentRate(string stringToProcess)
        {
            if (string.IsNullOrEmpty(stringToProcess)) return stringToProcess;

            try
            {
                string spotKeyString = "-主日出席率:";
                int startPosition = stringToProcess.IndexOf(spotKeyString);

                if (startPosition > 0)
                {
                    return stringToProcess.Substring(0, startPosition);
                }
                else
                {
                    return stringToProcess;
                }
            }
            catch (Exception)
            {
                return stringToProcess;
            }
        }

        /// <summary>
        /// 修剪字串中的主日出席率資訊（實例方法版本）
        /// </summary>
        public static string TrimPresentRate(string stringToProcess)
        {
            // 與 DeletePresentRate 功能相同
            return DeletePresentRate(stringToProcess);
        }
    }
}
