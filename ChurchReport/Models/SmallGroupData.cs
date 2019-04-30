using ChurchReport.Models.CrmTransmitModule;
using ChurchReport.WebServiceConnector;
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
        {
            ModifyFlag = false;
        }
        public String LoginType { get; set; }
        public String SmallGroupLeaderContactId { get; set; }
        public String SmallGroupLeaderFullName { get; set; }
        public DateTime SundayPrayers { get; set; }
        public String SundayPrayersString { get; set; }
        public String DataStatus { get; set; }
        public bool ModifyFlag { get; set; }
        public String SundayPeriod { get; set; } // 提醒小組長回報的期間
        public List<Member> Members { get ; set ; } // 
        public bool DisplayFlag { get; set; }
        public void InsertMember( string values)
        { 
            var aNewMember = new Member();
            JsonConvert.PopulateObject(values, aNewMember);

            Members.Add(aNewMember);
        }
        public void UpdateMember(string key, string values)
        {

            // 修改資料
            ModifyFlag = true; // 先修改旗標表示有被更新到

            // 找到該會友的紀錄
            Member aUpdatedMember = Members.DefaultIfEmpty(null).FirstOrDefault(o => o.PresentRecordId == key);

            // 該會友的修改旗標設定唯有被修改過
            aUpdatedMember.ModifyFlag = true;

            var settings = new JsonSerializerSettings
            {
                // 轉換成當地時間
                DateTimeZoneHandling = DateTimeZoneHandling.Local,
                //DateTimeZoneHandling = DateTimeZoneHandling.Utc,

                NullValueHandling = NullValueHandling.Ignore,
                MissingMemberHandling = MissingMemberHandling.Ignore
            };

            // 會友資料被修改
            JsonConvert.PopulateObject(values, aUpdatedMember, settings);
        }
        public void PopulateObjectAndUpdateEntity(string key, string values)
        {
            // 修改資料
            ModifyFlag = true; // 先修改旗標表示有被更新到

            // 找到該會友的紀錄
            Member aUpdatedMember = Members.First(o => o.PresentRecordId == key);

            aUpdatedMember.ModifyFlag = true;

            var settings = new JsonSerializerSettings
            {
                // 轉換成當地時間
                //DateTimeZoneHandling = DateTimeZoneHandling.Local,
                DateTimeZoneHandling = DateTimeZoneHandling.Utc,

                NullValueHandling = NullValueHandling.Ignore,
                MissingMemberHandling = MissingMemberHandling.Ignore
            };

            JsonConvert.PopulateObject(values, aUpdatedMember, settings);

            Dictionary<string, string> aDictionary = JsonConvert.DeserializeObject<Dictionary<string, string>>(values);

            List<string> KeyList = new List<string>(aDictionary.Keys);
            List<string> ValueList = new List<string>(aDictionary.Values);

            if (KeyList.Count > 0)
            {
                String Key = KeyList[0];

                if (Key == "BirthDate" && ValueList[0] == null)
                {
                    String BirthDateValue = "{\"BirthDate\":\"" + DateTime.MinValue.ToUniversalTime().ToString("u") + "\"}";
                    JsonConvert.PopulateObject(BirthDateValue, aUpdatedMember, settings);
                }
            }

        }
        public void DeleteMember(string key)
        {
            var aDeleteMember = Members.First(o => o.PresentRecordId == key);

            Members.Remove(aDeleteMember);
        }

    }
}
