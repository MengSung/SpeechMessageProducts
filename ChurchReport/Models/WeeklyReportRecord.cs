using Microsoft.Xrm.Sdk;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ToolUtilityNameSpace;

namespace ChurchReport.Models
{
    public class WeeklyReportRecord
    {
        // 新增新人時，選擇進入哪一個小組的清單 + 小家長或一人帶多個小組時，提供選擇點選進入觀看的Grid

        #region 資料區
        #region 常數參數

        //private const String CRM_TYPE = "CRM2011";
        private const String CRM_TYPE = "DYNAMICS365";

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
        private ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass("DYNAMICS365");
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
        public string ReportContent { get; set; } // 小組日誌
        #endregion
        #endregion


    }
}
