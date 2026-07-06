// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Models/WeeklyReportRecord.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於資料模型或 ViewModel 層，註解重點在說明欄位語意、序列化/繫結用途與相容性限制。
// 主要型別：class WeeklyReportRecord
// 主要成員：ListEntityId、WeeklyReportEntityId、Name、TotalNumber、SundayNumber、SmallGroupNumber、SundayRate、SmallGroupRate、ReportStatus、ReportContent
// 引用命名空間：Microsoft.Xrm.Sdk、System、System.Collections.Generic、System.Linq、System.Threading.Tasks、ToolUtilityNameSpace、ToolUtilityNameSpace.Factory
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using Microsoft.Xrm.Sdk;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ToolUtilityNameSpace;
using ToolUtilityNameSpace.Factory;

namespace ChurchReport.Models
{
    public class WeeklyReportRecord
    {
        // 新增新人時，選擇進入哪一個小組的清單 + 小家長或一人帶多個小組時，提供選擇點選進入觀看的Grid

        #region 資料區
        #region 常數參數

        //private const String CRM_TYPE = "CRM2011";
        private const String CRM_TYPE = "DYNAMICS365-9.0";

        private const bool TRANSFER_IDENTITY_FLAG = true;

        #region 除錯用參數
        private const int TOTAL_LEVEL = 1;//改變這個值，就會改追蹤的階層，值越小越不會追蹤，若是 TOTAL_LEVEL = 3 ，則大於 3 的 LEVEL，例如 : LEVEL_4、LEVEL_5 就不會被追蹤
        //private const int TOTAL_LEVEL = 5;//改變這個值，就會改追蹤的階層，值越大越會追蹤，若是 TOTAL_LEVEL = 3 ，則大於 3 的 LEVEL，例如 : LEVEL_4、LEVEL_5 就不會被追蹤
        private const int LEVEL_1 = 1; // 比較容易被看到的，可能是比較大範圍的部分
        private const int LEVEL_2 = 2;
        private const int LEVEL_3 = 3;
        private const int LEVEL_4 = 4;
        private const int LEVEL_5 = 5; // 比較不會被看到的，可能是比較細節的部分
        // 如果 TRACE_LEVEL >= TRACE_LEVEL_GROUND 就會進行追蹤
        // 如果 TRACE_LEVEL < TRACE_LEVEL_GROUND 就不會進行追蹤
        //int TRACE_LEVEL = 5;
        //int TRACE_LEVEL_GROUND = 3;
        #endregion

        #endregion
        #region 參數資料
        // 透過 Factory 取得 ToolUtilityClass 單一實例
        private ToolUtilityClass m_ToolUtilityClass = ToolUtilityFactory.GetInstance("DYNAMICS365-9.0");
        #endregion
        #region 類別資料
        public String ListEntityId { get; set; } // 小組 ID
        public String WeeklyReportEntityId { get; set; } // 週報 ID
        public string Name { get; set; } // 小組名稱
        public string TotalNumber { get; set; } // 小組人數
        public string SundayNumber { get; set; } // 主日人數
        public string SmallGroupNumber { get; set; } // 小組人數
        public string SundayRate { get; set; } // 主日出席率
        public string SmallGroupRate { get; set; } // 小組出席率
        public string ReportStatus { get; set; } // 週報狀態
        public string ReportContent { get; set; } // 小組日誌
        #endregion
        #endregion


    }
}
