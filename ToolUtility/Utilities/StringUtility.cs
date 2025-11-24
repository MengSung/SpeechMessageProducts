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
            
            if (lastIndex > 0)
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
