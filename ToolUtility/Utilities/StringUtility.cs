/*
 * 檔案整體說明
 *
 * 目的與定位：字串工具類 - 提供常用的字串處理功能
 *
 * 主要功能與流程：
 * 本檔宣告 StringUtility。
 * 主要操作入口包含 DeleteLastComma、FilterDigit、DeleteLastChar、DeletePresentRate、TrimPresentRate。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ToolUtility.Tests/Utilities/StringUtilityTests.cs、ToolUtility/Core/ToolUtilityFacade.cs、
 * ToolUtility/ToolUtilityPartials/ToolUtilityClass.Utility.cs 追查目前工作樹中的合作／使用位置。
 */
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
