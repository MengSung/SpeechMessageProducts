using System;
using ToolUtilityNameSpace.Core;
using ToolUtilityNameSpace.Utilities;

namespace ToolUtilityNameSpace
{
    /// <summary>
    /// ToolUtilityClass - 工具方法 (Partial Class 10/10)
    /// 包含：字串處理等工具方法
    /// </summary>
    public partial class ToolUtilityClass
    {
        #region 字串處理方法
        static public void DeleteLastComma(ref String StringToProcess)
        {
            try
            {
                Core.ToolUtilityFacade.DeleteLastComma(ref StringToProcess);
            }
            catch (Exception e)
            {
                throw e;
            }
        }

        static public void DeleteLastChar(ref String StringToProcess)
        {
            try
            {
                StringUtility.DeleteLastChar(ref StringToProcess);
            }
            catch (Exception e)
            {
                throw e;
            }
        }

        static public String DeletePresentRate(String StringToProcess)
        {
            try
            {
                return StringUtility.DeletePresentRate(StringToProcess);
            }
            catch (Exception e)
            {
                throw e;
            }
        }

        public String TrimPresentRate(String StringToProcess)
        {
            try
            {
                return StringUtility.TrimPresentRate(StringToProcess);
            }
            catch (Exception e)
            {
                throw e;
            }
        }

        public String FilterDigit(String aFilteredString)
        {
            try
            {
                return _facade.FilterDigit(aFilteredString);
            }
            catch (Exception e)
            {
                throw e;
            }
        }
        #endregion
    }
}
