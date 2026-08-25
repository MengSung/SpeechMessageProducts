// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Models/SmallGroupData.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於資料模型或 ViewModel 層，註解重點在說明欄位語意、序列化/繫結用途與相容性限制。
// 主要型別：class SmallGroupData
// 主要成員：InsertMember、UpdateMember、PopulateObjectAndUpdateEntity、DeleteMember、LoginType、SmallGroupLeaderContactId、SmallGroupLeaderFullName、SundayPrayers、SundayPrayersString、DataStatus
// 引用命名空間：ChurchReport.Models.CrmTransmitModule、ChurchReport.WebServiceConnector、Newtonsoft.Json、System、System.Collections.Generic、System.Linq、System.Text、System.Threading.Tasks
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
        // 獨立建構的資料使用本地鎖；掛入 SmallGroupDataList 後由資料圖 owner
        // 接入該圖的共享鎖。不同 Session 不共享此物件或同步根。
        private readonly object _localSyncRoot = new();
        private object _syncRoot;

        public SmallGroupData()
        {
            ModifyFlag = false;
            DisplayFlag = true; // 預設為 true，確保資料網格會顯示
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

        /// <summary>
        /// 將此資料集合接入所屬 SmallGroupDataList 的共享同步根。
        /// 僅保護短暫記憶體更新，不得在該同步邊界執行 CRM、HTTP 或其他 I/O。
        /// </summary>
        /// <param name="syncRoot">目前資料圖的私有同步根。</param>
        internal void AttachSynchronizationRoot(object syncRoot)
        {
            _syncRoot = syncRoot ?? throw new ArgumentNullException(nameof(syncRoot));
        }

        private object SynchronizationRoot => _syncRoot ?? _localSyncRoot;

        public void InsertMember( string values)
        {
            lock (SynchronizationRoot)
            {
                var aNewMember = new Member();
                JsonConvert.PopulateObject(values, aNewMember);
                Members.Add(aNewMember);
            }
        }

        /// <summary>
        /// 以目前資料圖的同步根加入已建立的成員；此方法只修改記憶體集合。
        /// </summary>
        /// <param name="member">要加入的成員。</param>
        public void AddMember(Member member)
        {
            ArgumentNullException.ThrowIfNull(member);
            lock (SynchronizationRoot)
            {
                Members.Add(member);
            }
        }
        public void UpdateMember(string key, string values)
        {
            lock (SynchronizationRoot)
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
        }
        public void PopulateObjectAndUpdateEntity(string key, string values)
        {
            lock (SynchronizationRoot)
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
        }
        public Member DeleteMember(string key)
        {
            lock (SynchronizationRoot)
            {
                try
                {
                    if (Members != null)
                    {
                        if (Members.Count > 0)
                        {
                            //var aDeleteMember = Members.FirstOrDefault(o => o.PresentRecordId == key);
                            Member aDeleteMember = Members.FirstOrDefault(o => o.PresentRecordId == key);

                            Members.Remove(aDeleteMember);

                            return aDeleteMember;
                        }
                        else
                        {
                            return null;
                        }
                    }
                    else
                    {
                        return null;
                    }
                }
                catch (System.Exception e)
                {
                    string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                    return null;
                }
            }
        }
    }
}
