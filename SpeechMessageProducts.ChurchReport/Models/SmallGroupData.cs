/*
 * 檔案整體說明
 *
 * 目的與定位：組合目前小組的週報、會友與統計狀態，提供整合回報畫面及深度快照的資料基礎；可變成員必須保持操作所有權，背景上傳不得反向修改Session原物件。
 *
 * 主要功能與流程：
 * 本檔宣告 SmallGroupData。
 * - 將此資料集合接入所屬 SmallGroupDataList 的共享同步根。 僅保護短暫記憶體更新，不得在該同步邊界執行 CRM、HTTP 或其他 I/O。
 * - 在所屬資料圖的唯一同步根內解析 DevExtreme 新增 payload，並以 PresentRecordId 原子防重。
 * - 以目前資料圖的同步根加入已建立的成員；此方法只修改記憶體集合。
 * 主要操作入口包含 AttachSynchronizationRoot、InsertMember、AddMember、UpdateMember、PopulateObjectAndUpdateEntity、
 * DeleteMember。
 *
 * 使用邊界與維護：
 * 模型含巢狀集合或敏感欄位時，呼叫端須維持目前登入／操作的所有權；顯示文字不能取代正式資料庫ID。
 * 修改繫結欄位、預設值或複製路徑時，須同步核對序列化、表單、快照及上傳映射，避免局部更新污染其他讀者。
 * 可沿 ChurchReport.MemberInfo.Tests/Models/ListManagerIntegratePublicationTests.cs、
 * ChurchReport.MemberInfo.Tests/Models/SmallGroupDataListSnapshotIsolationTests.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/NewPersonController.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/PersonalController.cs、
 * SpeechMessageProducts.ChurchReport/Models/ListManager.cs 追查目前工作樹中的合作／使用位置。
 */
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

        /// <summary>
        /// 在所屬資料圖的唯一同步根內解析 DevExtreme 新增 payload，並以 PresentRecordId 原子防重。
        /// </summary>
        /// <param name="values">目前 request 提供的 JSON 欄位；身份仍須由 server／資料庫流程建立與驗證。</param>
        /// <exception cref="InvalidOperationException">ID 缺少或已存在時擲出；不會修改原集合。</exception>
        /// <remarks>
        /// 方法只做短暫記憶體操作，不得在鎖內加入 CRM、HTTP、檔案 I/O 或等待 Task。姓名及其他
        /// 顯示內容不參與比較，避免誤刪合法同名資料；例外也不包含姓名、credential 或 Session ID。
        /// </remarks>
        public void InsertMember(string values)
        {
            lock (SynchronizationRoot)
            {
                var aNewMember = new Member();
                JsonConvert.PopulateObject(values, aNewMember);

                // 此方法是單一 consumer collection 的寫入邊界。解析完成後立即用資料庫
                // PresentRecordId 比對既有列；不得因姓名相同就拒絕不同 ID，也不得以取第一筆
                // 或覆蓋字典值掩蓋同一 ID 的重送。鎖內沒有 I/O，例外發生時集合維持原狀。
                if (aNewMember == null || string.IsNullOrWhiteSpace(aNewMember.PresentRecordId))
                {
                    throw new InvalidOperationException("新增出席資料缺少有效 PresentRecordId，拒絕發布。");
                }

                var normalizedId = aNewMember.PresentRecordId.Trim();
                if (Members?.Any(member => member != null &&
                        string.Equals(member.PresentRecordId?.Trim(), normalizedId, StringComparison.OrdinalIgnoreCase)) == true)
                {
                    throw new InvalidOperationException(
                        $"新增出席資料偵測到重複 PresentRecordId '{normalizedId}'，拒絕重送。");
                }

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
