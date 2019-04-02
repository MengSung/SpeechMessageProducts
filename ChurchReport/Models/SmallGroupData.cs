using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ChurchReport.Models
{
    public class SmallGroupData
    {
        public SmallGroupData()
        { }
        public String LoginType { get; set; }
        public String SmallGroupLeaderContactId { get; set; }
        public String SmallGroupLeaderFullName { get; set; }
        public DateTime SundayPrayers { get; set; }
        public String SundayPrayersString { get; set; }

        public String DataStatus { get; set; }

        public String SundayPeriod { get; set; } // 提醒小組長回報的期間

        public List<Member> Members { get ; set ; }

        public void UpdateMember(string key, string values)
        {

            // 修改幸福小組週報
            Member aUpdatedMember = JsonConvert.DeserializeObject<Member>(values);

            //IEnumerable<Member> filteringQuery =
            //    from aMember in Members
            //    where PresentRecordId ==key
            //    select aMember;

            var aMember = Members.First(o => o.PresentRecordId == key);
            JsonConvert.PopulateObject(values, aMember);

            //IEnumerable<Member> aMember = Members.Where(c => c.PresentRecordId == key );

            //JsonConvert.PopulateObject( values, aMember );

            // 設定前端傳來週報有被修改過的旗標
            //aUpdatedHappyGroupWeeklyReport.ModifiedFlag = true;

            // 修改系統的幸福小組週報
            //m_DownloadHappyGroup.UpdateHappyGroupWeeklyReport(key, ref aUpdatedHappyGroupWeeklyReport);

            // 從前端傳來有更改過的週報去更新網頁端的幸福小組週報內容
        }

    }
}
