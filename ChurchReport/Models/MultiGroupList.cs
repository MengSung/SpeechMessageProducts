using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ChurchReport.Models
{
    public class MultiGroupList
    {
        // 新增新人時，選擇進入哪一個小組的清單 + 區長或一人帶多個小組時，提供選擇點選進入觀看的Grid
        public List<WeeklyReportRecord> m_WeeklyReportRecordListData { get; set; }
    }
}
