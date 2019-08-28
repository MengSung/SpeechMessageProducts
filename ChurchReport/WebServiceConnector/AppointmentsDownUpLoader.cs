using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using ChurchReport.Models.CrmTransmitModule;
using ChurchReport.Models;

#region Dynamics 365 Microsoft.Xrm.Sdk.dll
// These namespaces are found in the Microsoft.Xrm.Sdk.dll assembly
// located in the SDK\bin folder of the SDK download.
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Client;
using Microsoft.Xrm.Sdk.Discovery;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Messages;
using ToolUtilityNameSpace;
using System.Text.RegularExpressions;
using System.Collections;
#endregion

namespace ChurchReport.WebServiceConnector
{
    public class AppointmentsDownUpLoader
    {
        #region 資料區
        #region 參數資料
        private ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass("DYNAMICS365");
        //ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass("CRM2011");

        private static Regex DigitsOnly = new Regex(@"[^\d]");

        private Dictionary<String, String> m_FeedBackReport = new Dictionary<string, string>();

        bool m_SetIdentityFlag = false;
        #endregion
        #region 常數參數

        //private const String CRM_TYPE = "CRM2011";
        private const String CRM_TYPE = "DYNAMICS365";

        //private const int MONTH_PERIOD = 2;      //幾個月內出席超過這次數就會改變委身類型=>小組組員
        private const int WEEK_PERIOD = 8;      //過去幾　WEEK_PERIOD　周內出席超過這次數就會改變委身類型=>小組組員
        private const int MINIMUM_THRESHOLD = 4;      //2個月內出席超過這次數就會改變委身類型=>小組組員

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
        #endregion
        #region 下載資料時所需要的參數

        Entity m_ContactEntity; //登入者在系統裡的實體
        Guid m_ContactId; //登入者在系統裡的ID

        Guid m_OwnerId; // 預約人的負責人 Id
        #endregion
        #region 主程式區
        /// <summary>
        ///     載入使用者的本月約會
        /// </summary>
        /// <param name="Account"></param>
        /// <param name="Password"></param>
        /// <param name="aSelectDate"></param>
        /// <returns></returns>
        public List<Appointment> GetAppointmentList(String Account, String Password, DateTime aSelectDate)
        {
            // 取得登入者
            FindLoginUser(Account, Password);

            if (m_ContactEntity == null)
            {
                // 沒找到就直接離開
                return new List<Appointment>();
            }
            else
            {
                return RetrieveAppointmentList(aSelectDate);
            }

            return new List<Appointment>() ;
        }
        #region 使用者登入
        private void FindLoginUser(String Account, String Password)
        {
            // 找登入使用者及其ID
            if (Account != "LineIdLogin")
            {
                this.m_ContactEntity = this.m_ToolUtilityClass.RetrieveContactEntityByAccountNumber(Account, Password);
            }
            else
            {
                // 用 LINE 登入
                this.m_ContactEntity = this.m_ToolUtilityClass.RetrieveContactEntityByLineUserId(Password);
            }

            this.m_ContactId = m_ContactEntity.Id;
        }
        #endregion

        #endregion
        #region 下載資料
        public List<Appointment> RetrieveAppointmentList(DateTime aSelectDate)
        {
            try
            {
                int DaysInSelectDate = System.DateTime.DaysInMonth(aSelectDate.Year, aSelectDate.Month);
                DateTime StartDate = new DateTime(aSelectDate.Year, aSelectDate.Month, 1);
                DateTime EndDate = new DateTime(aSelectDate.Year, aSelectDate.Month, DaysInSelectDate);
                EntityCollection AppointmentEntityCollection = this.m_ToolUtilityClass.RetrieveAppointmentsByFetchXml(StartDate, EndDate);

                List<Appointment>  aAppointmentList = SetupAppointmentsListEntityCollection(AppointmentEntityCollection);

                SetupAppointmentListByLesson( aAppointmentList, StartDate, EndDate);

                return aAppointmentList;
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }

        #region 依據約會建立的行事曆
        public List<Appointment> SetupAppointmentsListEntityCollection(EntityCollection AppointmentEntityCollection)
        {
            try
            {
                List<Appointment> aAppointmentsList = new List<Appointment>();

                ArrayList aFromOrToIdList = new ArrayList(); //出席者+列席者的 ID 陣列
                ArrayList aFromOrToTypeList = new ArrayList();//出席者+列席者的 型態(聯絡人、組織、使用者)陣列
                foreach (Entity aAppointmentEntity in AppointmentEntityCollection.Entities)
                {
                    #region 處理一個一個約會

                    SetupAppointmentList( aAppointmentsList, aAppointmentEntity, aFromOrToIdList, aFromOrToTypeList);

                    #endregion
                }

                return aAppointmentsList;
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public void SetupAppointmentList(List<Appointment> aAppointmentsList, Entity aAppointmentEntity, ArrayList aFromOrToIdList, ArrayList aFromOrToTypeList)
        {
            try
            {
                #region 處理一個一個約會
                int CategoryId = this.m_ToolUtilityClass.GetOptionSetAttribute(ref aAppointmentEntity, "new_meeting_kind");
                String AppointmentType = this.ConvertCategoryIdToAppointmentType(CategoryId);

                aFromOrToIdList.Clear();
                aFromOrToTypeList.Clear();

                //設定出席者的 ID 陣列
                this.m_ToolUtilityClass.GetActivityPartyIdList(aAppointmentEntity, "requiredattendees", aFromOrToIdList, aFromOrToTypeList);
                //設定列席者的 ID 陣列
                this.m_ToolUtilityClass.GetActivityPartyIdList(aAppointmentEntity, "optionalattendees", aFromOrToIdList, aFromOrToTypeList);

                if (AppointmentType != "教會行事曆")
                {
                    foreach (Guid ContactId in aFromOrToIdList)
                    {
                        if (m_ContactEntity.Id.ToString() == ContactId.ToString())
                        {
                            Appointment aAppointment = SetupAppointment(aAppointmentEntity, AppointmentType, CategoryId);
                            aAppointmentsList.Add(aAppointment);

                            break;
                        }
                    }
                }
                else {
                    // 如果是"教會行事曆"則一律要顯示
                    Appointment aAppointment = SetupAppointment(aAppointmentEntity, AppointmentType, CategoryId);
                    aAppointmentsList.Add(aAppointment);
                }
                #endregion

                return;
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public Appointment SetupAppointment( Entity aAppointmentEntity, String AppointmentType, int CategoryId)
        {
            try
            {
                #region 建立一個約會
                return new Appointment
                {
                    AppointmentId = aAppointmentEntity.Id.ToString(),
                    Text = this.m_ToolUtilityClass.GetEntityStringAttribute(aAppointmentEntity, "subject"),
                    AppointmentType = AppointmentType,
                    CategoryId = new int[] { CategoryId },
                    OwnerId = new int[] { CategoryId },
                    StartDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(aAppointmentEntity, "scheduledstart").ToLocalTime(),
                    EndDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(aAppointmentEntity, "scheduledend").ToLocalTime(),
                };

                #endregion
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        #endregion
        #region 依據課程建立的行事曆
        public void SetupAppointmentListByLesson(List<Appointment> aAppointmentsList, DateTime StartDate, DateTime EndDate)
        {
            try
            {
                #region 取得課程

                String ContactFullName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref this.m_ContactEntity, "fullname");

                // 本月開始上課的課程
                EntityCollection LessonEntityCollectionByMonth = this.m_ToolUtilityClass.RetrieveLessonsByMonth(StartDate, EndDate);

                // 本月我已報名的課程
                EntityCollection EnrolledLessonEntityCollection = this.m_ToolUtilityClass.RetrieveEnrolledLessonsByFetchXml(StartDate, EndDate, ContactFullName, m_ContactEntity.Id.ToString());

                foreach (Entity aLessonEntity in LessonEntityCollectionByMonth.Entities)
                {
                    bool SearchFlag = false;
                    foreach (Entity aEnrolledLessonEntity in EnrolledLessonEntityCollection.Entities)
                    {
                        if(aLessonEntity.Id == aEnrolledLessonEntity.Id)
                        {
                            // 已報名的課程
                            SetdLessonAppointment(aAppointmentsList, aLessonEntity, 8);
                            SearchFlag = true;
                            break;
                        }
                    }

                    // 沒報名的課程
                    if (SearchFlag == false)
                    {
                        SetdLessonAppointment(aAppointmentsList, aLessonEntity, 3);
                    }
                }
                #endregion

                return;
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }


        public void SetdLessonAppointment(List<Appointment> aAppointmentsList, Entity LessonEntity, int CategoryId)
        {
            try
            {
                #region 取得課程

                aAppointmentsList.Add(
                    new Appointment
                    {
                        AppointmentId = LessonEntity.Id.ToString(),
                        Text = this.m_ToolUtilityClass.GetEntityStringAttribute(LessonEntity, "new_name"),
                        AppointmentType = "課程",
                        CategoryId = new int[] { CategoryId },
                        OwnerId = new int[] { CategoryId },
                        StartDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(LessonEntity, "new_class_start_date").ToLocalTime(),
                        EndDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(LessonEntity, "new_class_end_date").ToLocalTime(),
                    }
                );
                #endregion

                return;
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }

        #endregion
        #endregion
        #region 新增、修改、刪除約會
        public void CreateAppointment(Appointment aAppointment)
        {
            try
            {
                Entity aAppointmentEntity = new Entity("appointment");

                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aAppointmentEntity, "subject", aAppointment.Text);
                this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aAppointmentEntity, "scheduledstart", aAppointment.StartDate.ToLocalTime());
                this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aAppointmentEntity, "scheduledend", aAppointment.EndDate.ToLocalTime());


                aAppointmentEntity["requiredattendees"] = BuildReciever(m_ContactEntity);

                //this.m_ToolUtilityClass.SetOptionSetAttribute(ref aAppointmentEntity, "statecode", 3);


                m_OwnerId = this.m_ToolUtilityClass.GetOwnerId(m_ContactEntity);

                Entity oParty = new Entity("activityparty");
                oParty["partyid"] = new EntityReference("systemuser", m_OwnerId);

                //Place the party record into a collection
                EntityCollection oCollection = new EntityCollection();
                oCollection.Entities.Add(oParty);

                //Set the organizer field to the collection:
                aAppointmentEntity["organizer"] = new EntityCollection();
                aAppointmentEntity["organizer"] = oCollection;


                // 新增約會
                Guid CreatedAppointmentEntityId = this.m_ToolUtilityClass.CreateEntity(aAppointmentEntity);

                #region 指派約會的負責人
                // 小組長的負責人 Id
                //m_OwnerId = this.m_ToolUtilityClass.GetOwnerId(m_ContactEntity);

                this.m_ToolUtilityClass.AssignOwner("appointment", this.m_ToolUtilityClass.RetrieveEntity("appointment", CreatedAppointmentEntityId), this.m_OwnerId);
                #endregion



                this.m_ToolUtilityClass.SetAppointmentStatusToScheduled(CreatedAppointmentEntityId);


            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }

        public EntityCollection BuildReciever(Entity aRecieverEntity)
        {
            try
            {

                #region// 傳送模板訊息
                Entity aParty = new Entity("activityparty");
                aParty["partyid"] = new EntityReference("contact", aRecieverEntity.Id);


                // Create a new EntityCollection and add the 2 parties
                EntityCollection to = new EntityCollection();
                to.Entities.Add(aParty);

                return to;
                #endregion

            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        #endregion

        #region 類別對應工具
        private String ConvertCategoryIdToAppointmentType(int CategoryId)
        {
            switch (CategoryId)
            {
                case 1:
                    return "教會行事曆";
                case 2:
                    return "服事";
                case 3:
                    return "課程";
                case 4:
                    return "場地";
                case 5:
                    return "會議";
                case 6:
                    return "人資出差勤";
                case 7:
                    return "其他";
                default:
                    return "其他";
            }
        }

        private int ConvertAppointmentTypeToCategoryId(String AppointmentType)
        {
            switch (AppointmentType)
            {
                case "教會行事曆":
                    return 1;
                case "服事":
                    return 2;
                case "課程":
                    return 3;
                case "場地":
                    return 4;
                case "會議":
                    return 5;
                case "人資出差勤":
                    return 6;
                case "其他":
                    return 7;
                default:
                    return 7;
            }
        }

        #endregion
    }
}
