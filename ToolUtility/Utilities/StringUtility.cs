using System;
using System.Linq;

namespace ToolUtilityNameSpace.Utilities
{
    public static class StringUtility
    {
        public static void DeleteLastComma(ref string stringToProcess)
        {
            if (stringToProcess == null) return;
            if (stringToProcess.Length == 0) return;

            // Consider both full-width '¡A' and ASCII ','
            int lastIndex = Math.Max(stringToProcess.LastIndexOf('¡A'), stringToProcess.LastIndexOf(','));
            if (lastIndex == stringToProcess.Length - 1)
            {
                stringToProcess = stringToProcess.Substring(0, lastIndex);
            }
        }

        public static string FilterDigit(string filteredString)
        {
            if (string.IsNullOrEmpty(filteredString)) return string.Empty;
            return new string(filteredString.Where(char.IsDigit).ToArray());
        }

        public static void DeleteLastChar(ref string stringToProcess)
        {
            if (stringToProcess == null) return;
            if (stringToProcess.Length == 0) return;
            if (stringToProcess.Length == 1)
            {
                stringToProcess = string.Empty;
                return;
            }
            stringToProcess = stringToProcess.Substring(0, stringToProcess.Length - 1);
        }
    }
}
