// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Models/SmallGroupDataList.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於資料模型或 ViewModel 層，註解重點在說明欄位語意、序列化/繫結用途與相容性限制。
// 主要型別：class SmallGroupDataList
// 主要成員：SetupContactIdString、SetSmallGroupDateOfWeeklyReport、TransferToMemberInfomationPackage、MappingMembers、AddNewPersonToMember
// 引用命名空間：System、System.Collections.Generic、System.Linq、System.Threading.Tasks、ToolUtilityNameSpace、Microsoft.Xrm.Sdk、Microsoft.Xrm.Sdk.Query、Microsoft.Xrm.Sdk.Client
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ToolUtilityNameSpace;


// These namespaces are found in the Microsoft.Xrm.Sdk.dll assembly
// located in the SDK\bin folder of the SDK download.
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Client;
using Microsoft.Xrm.Sdk.Discovery;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Messages;
using ChurchReport.WebServiceConnector;
using ChurchReport.Models.CrmTransmitModule;
using ChurchReport.ViewModels;

namespace ChurchReport.Models
{
    public class SmallGroupDataList
    {
        // 所有三組 Members 集合的唯一同步 owner。鎖只保護快照建立時的短暫讀取，
        // 不在臨界區執行 CRM、網路或其他 I/O，避免阻塞前景請求及延長資源生命週期。
        private readonly object _syncRoot = new();

        /// <summary>
        /// 供同一物件圖的協作者在替換集合參考時使用的同步根；不應跨物件圖保存或公開給請求外部。
        /// </summary>
        internal object SyncRoot => _syncRoot;

        public SmallGroupDataList()
        {
            _smallGroupData.AttachSynchronizationRoot(_syncRoot);
            _newPersonFollowUpData.AttachSynchronizationRoot(_syncRoot);
            _happyGroup.AttachSynchronizationRoot(_syncRoot);
            _allMemeberData.AttachSynchronizationRoot(_syncRoot);
        }

        public String m_FullName = "";
        //public String m_Account  = "";
        //public String m_Password = "";
        //public DateTime m_SelectDate = new DateTime(2000, 1, 1);// 初始值 2000 表示還沒選
        public DateTime m_SelectDate = DateTime.Now;// 初始值 2000 表示還沒選
        public DateTime m_SundayDate;
        private bool m_FirstLoginFlag;

        private SmallGroupData _smallGroupData = new SmallGroupData();
        private SmallGroupData _newPersonFollowUpData = new SmallGroupData();
        private SmallGroupData _happyGroup = new SmallGroupData();
        private SmallGroupData _allMemeberData = new SmallGroupData();

        /// <summary>小組長點名資料；替換時立即接入目前資料圖的共享同步根。</summary>
        public SmallGroupData m_SmallGroupData
        {
            get => _smallGroupData;
            set => ExecuteSynchronized(() => _smallGroupData = AttachToThisGraph(value));
        }

        /// <summary>新人跟進資料；與其他集合共享同一份資料圖同步根。</summary>
        public SmallGroupData m_NewPersonFollowUpData
        {
            get => _newPersonFollowUpData;
            set => ExecuteSynchronized(() => _newPersonFollowUpData = AttachToThisGraph(value));
        }

        /// <summary>幸福小組資料；替換時不保留舊同步根或跨 request 可變參考。</summary>
        public SmallGroupData m_HappyGroup
        {
            get => _happyGroup;
            set => ExecuteSynchronized(() => _happyGroup = AttachToThisGraph(value));
        }

        /// <summary>全部成員資料；參與背景上傳，所有前景 CRUD 必須與快照共鎖。</summary>
        public SmallGroupData m_AllMemeberData
        {
            get => _allMemeberData;
            set => ExecuteSynchronized(() => _allMemeberData = AttachToThisGraph(value));
        }

        /// <summary>
        /// 將新建或替換的 group 接入目前資料圖的同步根；此 setter 只執行記憶體參考綁定。
        /// </summary>
        private SmallGroupData AttachToThisGraph(SmallGroupData data)
        {
            var attached = data ?? new SmallGroupData();
            attached.AttachSynchronizationRoot(_syncRoot);
            return attached;
        }

        public MemberInfomationPackage m_MemberInfomationPackage;

        /// <summary>
        /// 在目前資料圖唯一同步根內完成跨集合的小型記憶體異動。
        ///
        /// 委派不得進行 CRM、HTTP、DI、檔案、網路或等待背景 Task；這可讓快照只看到完整
        /// 舊圖或完整新圖，又不把不同使用者的 Session 圖放進同一把全域鎖。
        /// </summary>
        /// <param name="mutation">只包含目前資料圖記憶體變更的委派。</param>
        internal void ExecuteSynchronized(Action mutation)
        {
            ArgumentNullException.ThrowIfNull(mutation);
            lock (_syncRoot)
            {
                mutation();
            }
        }

        /// <summary>
        /// 原子更新小組點名與全部成員中的同一筆資料，避免背景快照在兩次 JSON 原地更新之間
        /// 取得語意不一致的資料圖。
        /// </summary>
        public void UpdateSmallGroupAndAllMember(string key, string values)
        {
            ExecuteSynchronized(() =>
            {
                m_SmallGroupData.UpdateMember(key, values);
                m_AllMemeberData.UpdateMember(key, values);
            });
        }

        /// <summary>
        /// 原子更新新人跟進與全部成員中的同一筆資料；鎖內只更新 JSON 繫結結果，不執行 CRM。
        /// </summary>
        public void UpdateNewPersonAndAllMember(string key, string values)
        {
            ExecuteSynchronized(() =>
            {
                m_NewPersonFollowUpData.UpdateMember(key, values);
                m_AllMemeberData.UpdateMember(key, values);
            });
        }

        /// <summary>
        /// 從四組前景集合移除同一筆成員，並回傳全部成員集合原本持有的資料供鎖外 CRM 刪除使用。
        /// </summary>
        public Member DeleteMemberFromAllGroups(string key)
        {
            Member deletedMember = null;
            ExecuteSynchronized(() =>
            {
                deletedMember = m_AllMemeberData.DeleteMember(key);
                m_SmallGroupData.DeleteMember(key);
                m_NewPersonFollowUpData.DeleteMember(key);
                m_HappyGroup.DeleteMember(key);
            });
            return deletedMember;
        }

        /// <summary>
        /// 將下載或初始化後已完成建構的成員加入全部成員集合。
        ///
        /// CRM 查詢、聯絡人轉換與任何網路 I/O 必須在呼叫此方法前完成；本方法只在目前 Session
        /// 資料圖的短暫同步區間加入完整 <see cref="Member"/>，因此 SaveIntegrate 快照不會取得
        /// 正在變更的 <see cref="List{T}"/> 容器，也不會把同步根分享給其他使用者的資料圖。
        /// </summary>
        /// <param name="member">已完整建構且僅屬於目前資料圖的成員。</param>
        public void AddMemberToAllMemberData(Member member)
        {
            ArgumentNullException.ThrowIfNull(member);
            ExecuteSynchronized(() => m_AllMemeberData.AddMember(member));
        }

        /// <summary>
        /// 由全部成員集合重建小組與新人跟進集合。
        ///
        /// 這是下載資料完成後的純記憶體分類步驟；整個讀取、分類與兩個集合參考替換共用一個
        /// 同步邊界，使背景快照只會得到舊分類或完整新分類。不得將 CRM 查詢、HTTP 或 Task
        /// 排程放進此方法。
        /// </summary>
        public void RebuildSmallGroupAndNewPersonDataFromAllMembers()
        {
            ExecuteSynchronized(() =>
            {
                var allMembers = m_AllMemeberData.Members ?? new List<Member>();
                var smallGroupMembers = new List<Member>(allMembers.Count);
                var newPersonMembers = new List<Member>(allMembers.Count);

                foreach (var member in allMembers)
                {
                    var status = member?.Status ?? string.Empty;
                    if (status.Contains("新朋友") || status.Contains("未入組"))
                    {
                        newPersonMembers.Add(member);
                    }
                    else if (!status.Contains("外教會") && !status.Contains("結案"))
                    {
                        smallGroupMembers.Add(member);
                    }
                }

                _smallGroupData = AttachToThisGraph(new SmallGroupData { Members = smallGroupMembers });
                _newPersonFollowUpData = AttachToThisGraph(new SmallGroupData { Members = newPersonMembers });
            });
        }

        /// <summary>
        /// 由全部成員集合重建幸福小組集合。
        ///
        /// 來源列舉與新集合發布在同一把資料圖鎖內完成，保證快照不會在舊集合已移除、新集合尚未
        /// 建立的中間狀態讀取。此方法只複製清單容器；成員深拷貝仍由背景快照建立時負責。
        /// </summary>
        public void RebuildHappyGroupDataFromAllMembers()
        {
            ExecuteSynchronized(() =>
            {
                var allMembers = m_AllMemeberData.Members ?? new List<Member>();
                _happyGroup = AttachToThisGraph(new SmallGroupData { Members = new List<Member>(allMembers) });
            });
        }

        /// <summary>
        /// 在短暫同步區間內建立完整的唯讀退路快照。
        ///
        /// 快照擁有三組全新的 List 與每一個 Member 的全新實例；背景工作後續清理
        /// 只能改寫這份副本。鎖離開後才會交給背景流程執行，故不會把 CRM 或其他
        /// I/O 帶入臨界區，也不會讓跨請求流程長期持有原始 Session 物件圖。此鎖只界定
        /// SaveIntegrate 的快照建立邊界；既有其他寫入路徑尚未全面採用它，不能把本方法
        /// 視為整個 legacy 物件圖的全域併發控制機制。
        /// </summary>
        /// <returns>與來源資料隔離的 SmallGroupDataList；來源集合為 null 時仍建立空 List。</returns>
        public SmallGroupDataList CreateIsolatedSnapshot()
        {
            lock (_syncRoot)
            {
                return new SmallGroupDataList
                {
                    m_FullName = m_FullName,
                    m_SelectDate = m_SelectDate,
                    m_SundayDate = m_SundayDate,
                    m_FirstLoginFlag = m_FirstLoginFlag,
                    m_SmallGroupData = CloneSmallGroupData(m_SmallGroupData),
                    m_NewPersonFollowUpData = CloneSmallGroupData(m_NewPersonFollowUpData),
                    // SaveIntegrate 需要全部成員作為 CRM 上傳輸入，因此必須完整深拷貝；幸福小組
                    // 集合則維持新物件預設的空資料，避免把未參與上傳／清理流程的會員延長至背景生命週期。
                    m_AllMemeberData = CloneSmallGroupData(m_AllMemeberData)
                };
            }
        }

        /// <summary>
        /// 複製單一小組資料及其成員容器，確保 null 集合也會正規化為快照自有的空 List。
        /// </summary>
        /// <param name="source">要從同步區間內讀取的來源小組資料。</param>
        /// <returns>不含來源 Members 或 Member 參考的新資料物件。</returns>
        private static SmallGroupData CloneSmallGroupData(SmallGroupData source)
        {
            if (source == null)
            {
                return new SmallGroupData { Members = new List<Member>() };
            }

            return new SmallGroupData
            {
                LoginType = source.LoginType,
                SmallGroupLeaderContactId = source.SmallGroupLeaderContactId,
                SmallGroupLeaderFullName = source.SmallGroupLeaderFullName,
                SundayPrayers = source.SundayPrayers,
                SundayPrayersString = source.SundayPrayersString,
                DataStatus = source.DataStatus,
                ModifyFlag = source.ModifyFlag,
                SundayPeriod = source.SundayPeriod,
                DisplayFlag = source.DisplayFlag,
                Members = source.Members == null
                    ? new List<Member>()
                    : source.Members.Select(member => member == null ? null : new Member(member)).ToList()
            };
        }

        public void SetupContactIdString(String ContactIdString)
        {
            m_SmallGroupData.SmallGroupLeaderContactId = ContactIdString;
        }
        public void SetSmallGroupDateOfWeeklyReport(String FullName, DateTime SmallGroupDate)
        {
            foreach (GroupWeeklyReportGuid aGroupWeeklyReportGuid in m_MemberInfomationPackage.GroupWeeklyReportGuidList)
            {
                if (aGroupWeeklyReportGuid.SmallGroupLeaderName != null && aGroupWeeklyReportGuid.SmallGroupLeaderName.Contains(FullName))
                {
                    // 找到登入者的小組，因為登入者有可能是小家長，所以要確定登入者的小組聚會日期
                    aGroupWeeklyReportGuid.SmallGroupDate = SmallGroupDate;

                    return;
                }
            }
        }
        public void TransferToMemberInfomationPackage(SmallGroupData aGroupDataList)
        {
            int MemberCounter = 0;
            foreach (Member aMember in aGroupDataList.Members)
            {
                MappingMembers(aMember);

                MemberCounter++;
            }
        }
        public void MappingMembers(Member aMember)
        {
            foreach (MemberInfomation aMemberInfomation in m_MemberInfomationPackage.ListMemberInfomation)
            {
                if (aMember.Group == aMemberInfomation.Group && aMember.FullName == aMemberInfomation.Name)
                {
                    aMemberInfomation.Phone = aMember.Phone;
                    aMemberInfomation.HomePhone = aMember.HomePhone;
                    aMemberInfomation.Address = aMember.Address;
                    //aMemberInfomation.BirthDate = aMember.BirthDate;
                    aMemberInfomation.Industry = aMember.Industry;
                    aMemberInfomation.EquipmentStatus = aMember.EquipmentStatus;    // 裝備狀態
                    aMemberInfomation.SpiritualIdentity = aMember.SpiritualIdentity;// 受洗狀態
                    aMemberInfomation.BaptizedSituation = aMember.BaptizedSituation;// 洗禮狀態(長老教會專用)

                    aMemberInfomation.SundayPresent = aMember.Sunday;
                    aMemberInfomation.SmallGroupPresent = aMember.SmallGroup;
                    aMemberInfomation.Note = aMember.PrayItem;
                    aMemberInfomation.FollowUpOption = aMember.FollowUpOption;
                    aMemberInfomation.FollowUp = aMember.FollowUp;
                    aMemberInfomation.FollowUpResult = aMember.FollowUpResult;
                    aMemberInfomation.FollowUpNextStep = aMember.FollowUpNextStep;
                    aMemberInfomation.FollowUpNote = aMember.FollowUpNote;

                    #region 靈修、晨、晚禱
                    aMemberInfomation.SpiritualWork = aMember.SpiritualWork; // 讀經次數
                    aMemberInfomation.MorningPray = aMember.MorningPray;     // 晨禱(家庭祭壇)
                    aMemberInfomation.GeneralCare = aMember.GeneralCare;     // 晚禱(禱告會次數)
                    #endregion

                    break;
                }
            }
        }

        public void AddNewPersonToMember(PersonFormViewModel aPersonFormViewModel)
        {
            ArgumentNullException.ThrowIfNull(aPersonFormViewModel);
            ExecuteSynchronized(() =>
            {
                String aGroupName = aPersonFormViewModel.Position;
                if (aGroupName != null)
                {
                    if (aGroupName.Contains("幸福"))
                    {
                        Member aMember = new Member
                        {
                            PresentRecordId = aPersonFormViewModel.PresentRecordId,
                            Id = m_HappyGroup.Members.Count,
                            Group = aGroupName,
                            FullName = aPersonFormViewModel.LastName,
                            Phone = aPersonFormViewModel.Phone,
                            HomePhone = aPersonFormViewModel.HomePhone,
                            Industry = aPersonFormViewModel.Industry,
                            EquipmentStatus = aPersonFormViewModel.EquipmentStatus,
                            SpiritualIdentity = aPersonFormViewModel.SpiritualIdentity,
                            BaptizedSituation = aPersonFormViewModel.BaptizedSituation,
                            Address = aPersonFormViewModel.Address,
                            Status = "幸福BEST",
                            SmallGroupName = aGroupName,
                            SectionName = aGroupName,
                            PrayItem = aPersonFormViewModel.Notes,
                            Sunday = false,
                            SmallGroup = false,
                            StateID1 = 2,
                            Number1 = 4,
                            StateID2 = 1,
                            Number2 = 2,
                            Picture = "../../images/employees/01.png"
                        };

                        m_HappyGroup.DisplayFlag = true;
                        m_HappyGroup.Members.Add(aMember);
                        m_AllMemeberData.Members.Add(aMember);
                    }
                    else
                    {
                        Member aMember = new Member
                        {
                            PresentRecordId = aPersonFormViewModel.PresentRecordId,
                            Id = m_SmallGroupData.Members.Count,
                            Group = aGroupName,
                            FullName = aPersonFormViewModel.LastName,
                            Phone = aPersonFormViewModel.Phone,
                            HomePhone = aPersonFormViewModel.HomePhone,
                            Industry = aPersonFormViewModel.Industry,
                            EquipmentStatus = aPersonFormViewModel.EquipmentStatus,
                            SpiritualIdentity = aPersonFormViewModel.SpiritualIdentity,
                            BaptizedSituation = aPersonFormViewModel.BaptizedSituation,
                            Address = aPersonFormViewModel.Address,
                            Status = aPersonFormViewModel.CustomerTypeCode,
                            SmallGroupName = aGroupName,
                            SectionName = aGroupName,
                            PrayItem = aPersonFormViewModel.Notes,
                            Sunday = false,
                            SmallGroup = false,
                            StateID1 = 2,
                            Number1 = 4,
                            StateID2 = 1,
                            Number2 = 2,
                            Picture = "../../images/employees/01.png"
                        };

                        if (aPersonFormViewModel.CustomerTypeCode == "小組組員")
                        {
                            m_SmallGroupData.DisplayFlag = true;
                            m_SmallGroupData.Members.Add(aMember);
                        }
                        else
                        {
                            m_NewPersonFollowUpData.DisplayFlag = true;
                            m_NewPersonFollowUpData.Members.Add(aMember);
                        }

                        m_AllMemeberData.Members.Add(aMember);
                    }
                }
            });
        }
    }
}
