// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/ToolUtilityPartials/ToolUtilityClass.Utility.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class ToolUtilityClass
// 主要成員：TrimPresentRate、FilterDigit
// 引用命名空間：System、ToolUtilityNameSpace.Core、ToolUtilityNameSpace.Utilities
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
