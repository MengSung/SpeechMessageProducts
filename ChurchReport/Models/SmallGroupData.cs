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

        ListSmallGroupWeeklyReport ParentListSmallGroupWeeklyReport { get; set; }

        public String LoginType { get; set; }
        public String SmallGroupLeaderContactId { get; set; }
        public String SmallGroupLeaderFullName { get; set; }
        public DateTime SundayPrayers { get; set; }
        public String SundayPrayersString { get; set; }

        public String DataStatus { get; set; }

        public bool ModifyFlag { get; set; }

        public String SundayPeriod { get; set; } // 提醒小組長回報的期間

        public List<Member> Members { get ; set ; }

        //public String m_WeeklyReportData { get; set; }
        //public String m_WeeklyReportAnalysis { get; set; }
        //public bool m_DisplayFlag { get; set; }

        public String WeeklyReportData { get; set; }
        public String WeeklyReportAnalysis { get; set; }
        public bool DisplayFlag { get; set; }

        public void InsertMember( string values)
        { 
            var aNewMember = new Member();
            JsonConvert.PopulateObject(values, aNewMember);

            Members.Add(aNewMember);
        }
        //private readonly object m_QueryManyToOneLocker = new object();
        public void UpdateMember(string key, string values)
        {

            // 修改幸福小組週報
            //Member aUpdatedMember = JsonConvert.DeserializeObject<Member>(values);

            //IEnumerable<Member> filteringQuery =
            //    from aMember in Members
            //    where PresentRecordId ==key
            //    select aMember;

            //var aUpdatedMember = Members.First(o => o.PresentRecordId == key);
            //lock (m_QueryManyToOneLocker)
            //{


            //var firstObj = result.FirstOrDefault(m => m.Name == "Ada No");
            //if (firstObj != null)
            //{
            //    myobject.Ada = firstObj.Value;
            //}

            //if (result != null && result.Any(m => m.Name == "Ada No"))
            //{
            //    myobject.Ada = result.FirstOrDefault(m => m.Name == "Ada No").Value;
            //}
            //Member aUpdatedMember = Members.SingleOrDefault(o => o.PresentRecordId == key);
            Member aUpdatedMember = Members.DefaultIfEmpty(null).FirstOrDefault(o => o.PresentRecordId == key);
            //var aUpdatedMember = Members.FirstOrDefault(o => o.PresentRecordId == key);

            //if(aUpdatedMember == null)
            //{
            //    return;
            //}

            //Member aUpdatedMember;
            //if (Members != null && Members.Any(m => m.PresentRecordId == key))
            //{
            //    //aUpdatedMember = Members.FirstOrDefault(m => m.PresentRecordId == key);
            //}
            //else
            //{
            //    return;
            //}


            //Member aUpdatedMember = Members.Where( o=>o.PresentRecordId == key).FirstOrDefault();

            if (ParentListSmallGroupWeeklyReport != null)
                {
                    ParentListSmallGroupWeeklyReport.ModifyFlag = true;
                }
                this.ModifyFlag = true;
                aUpdatedMember.ModifyFlag = true;


            string Format = "";
            if (values.Contains("台北標準時間"))
            {
                Format = "ddd MMM dd yyyy HH:mm:ss GMT+0800 (台北標準時間)"; // DataGrid如果沒有設PAGE，則正確的日期格式
            }
            else if (values.Contains("CST"))
            {
                Format = "ddd MMM dd yyyy HH:mm:ss GMT+0800 (CST)"; // DataGrid如果沒有設PAGE，則正確的日期格式
            }
            else
            {
                //yyyy-MM-dd HH:mm:ss
                //Format = "ddd MMM dd yyyy HH:mm:ss GMT+0800"; // DataGrid如果沒有設PAGE，則正確的日期格式
                Format = "yyyy-MM-ddTHH:mm:ssZ"; // DataGrid如果沒有設PAGE，則正確的日期格式
            }
            var settings = new JsonSerializerSettings
            {
                // 轉換成當地時間
                //DateTimeZoneHandling = DateTimeZoneHandling.Local,
                DateTimeZoneHandling = DateTimeZoneHandling.Utc,
                //DateTimeZoneHandling = DateTimeZoneHandling.RoundtripKind,
                DateFormatString = Format,
                NullValueHandling = NullValueHandling.Ignore,
                MissingMemberHandling = MissingMemberHandling.Ignore
            };

            //var settings = new JsonSerializerSettings
            //{
            //    // 轉換成當地時間
            //    //DateTimeZoneHandling = DateTimeZoneHandling.Local,
            //    DateTimeZoneHandling = DateTimeZoneHandling.Utc,

            //    NullValueHandling = NullValueHandling.Ignore,
            //    MissingMemberHandling = MissingMemberHandling.Ignore
            //};



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
                else if(Key == "BirthDate" && ValueList[0] != null)
                {
                    //DateTime aBirthDate = DateTime.Parse(ValueList[0]);
                    //String BirthDateValue = "{\"BirthDate\":\"" + DateTime.Parse(ValueList[0]).ToUniversalTime().ToString("u") + "\"}";
                    //String BirthDateValue = "{\"BirthDate\":\"" + (DateTime.Parse(ValueList[0])).AddMonths(-1).ToLocalTime().ToString("u") + "\"}";
                    //String BirthDateValue = "{\"BirthDate\":\"" + (DateTime.Parse(ValueList[0])).AddMonths(-5).ToUniversalTime().ToString("u") + "\"}";
                    String BirthDateValue = "{\"BirthDate\":\"" + (DateTime.Parse(ValueList[0])).ToUniversalTime().ToString("u") + "\"}";
                    //String BirthDateValue = "{\"BirthDate\":\"" + DateTime.Parse(ValueList[0]).ToLocalTime().ToString() + "\"}";
                    JsonConvert.PopulateObject(BirthDateValue, aUpdatedMember, settings);
                }
                else
                {
                    JsonConvert.PopulateObject(values, aUpdatedMember, settings);
                }
            }




            JsonConvert.PopulateObject(values, aUpdatedMember, settings);
            //JsonConvert.PopulateObject(values, aUpdatedMember);


            //}
            //IEnumerable<Member> aMember = Members.Where(c => c.PresentRecordId == key );

            //JsonConvert.PopulateObject( values, aMember );

            // 設定前端傳來週報有被修改過的旗標
            //aUpdatedHappyGroupWeeklyReport.ModifiedFlag = true;

            // 修改系統的幸福小組週報
            //m_DownloadHappyGroup.UpdateHappyGroupWeeklyReport(key, ref aUpdatedHappyGroupWeeklyReport);

            // 從前端傳來有更改過的週報去更新網頁端的幸福小組週報內容
        }


        public void PopulateObjectAndUpdateEntity(string key, string values)
        {
            Member aUpdatedMember = Members.First(o => o.PresentRecordId == key);

            if (ParentListSmallGroupWeeklyReport != null)
            {
                ParentListSmallGroupWeeklyReport.ModifyFlag = true;
            }
            this.ModifyFlag = true;
            aUpdatedMember.ModifyFlag = true;


            var settings = new JsonSerializerSettings
            {
                // 轉換成當地時間
                //DateTimeZoneHandling = DateTimeZoneHandling.Local,
                DateTimeZoneHandling = DateTimeZoneHandling.Utc,

                NullValueHandling = NullValueHandling.Ignore,
                MissingMemberHandling = MissingMemberHandling.Ignore
            };

            //DiscipleLessons aBestRecord = JsonConvert.DeserializeObject<DiscipleLessons>(ProcessNullValue(Values), settings);

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
