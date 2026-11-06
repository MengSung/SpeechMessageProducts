/*
 * 檔案整體說明
 *
 * 目的與定位：ToolUtilityClass - 工具方法 (Partial Class 10/10) 包含：字串處理等工具方法
 *
 * 主要功能與流程：
 * 本檔宣告 ToolUtilityClass。
 * 主要操作入口包含 TrimPresentRate、FilterDigit。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ChurchReport.MemberInfo.Tests/Images/ImageUploadCharacterizationTests.cs、
 * ChurchReport.MemberInfo.Tests/Integration/ContactOwnerRouteTests.cs、
 * ChurchReport.MemberInfo.Tests/LineSharedWorkflow/LineUtilityClassWorkflowTests.cs、
 * ChurchReport.MemberInfo.Tests/Models/InMemoryDataContextSmallGroupCacheIsolationTests.cs、
 * ChurchReport.MemberInfo.Tests/Payments/ChurchReportPaymentContextBuilderTests.cs 追查目前工作樹中的合作／使用位置。
 */
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
