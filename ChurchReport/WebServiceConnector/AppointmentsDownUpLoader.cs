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
using System.Net;
using System.IO;
using System.Text;
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

        private Dictionary<String, double> m_SigningReport = new Dictionary<string, double>();

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
        public List<Appointment> GetAppointmentList(String Account, String Password, DateTime aSelectDate, ref String UserType, String ScheduleType)
        {
            // 取得登入者
            FindLoginUser(Account, Password);

            SetUserType(ref UserType);

            if (m_ContactEntity == null)
            {
                // 沒找到就直接離開
                return new List<Appointment>();
            }
            else
            {
                if (UserType != "行政同工" && ScheduleType == "差勤簽核")
                {
                    // 使用者不是行政同工，但卻想要查看差勤簽核，回傳空約會
                    return new List<Appointment>();
                }
                else
                {
                    return RetrieveAppointmentList(aSelectDate, ScheduleType);
                }
            }

            return new List<Appointment>();
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

                // 1. 依據約會建立的行事曆
                int DaysInSelectDate = System.DateTime.DaysInMonth(aSelectDate.Year, aSelectDate.Month);
                DateTime StartDate = new DateTime(aSelectDate.Year, aSelectDate.Month, 1);
                DateTime EndDate = new DateTime(aSelectDate.Year, aSelectDate.Month, DaysInSelectDate);

                // 取得約會集合
                EntityCollection AppointmentEntityCollection = this.m_ToolUtilityClass.RetrieveAppointmentsByFetchXml(StartDate, EndDate);

                // 建立約會
                List<Appointment> aAppointmentList = SetupAppointmentsListEntityCollection(AppointmentEntityCollection);

                // 2. 依據課程建立的行事曆
                SetupAppointmentListByLesson(aAppointmentList, StartDate, EndDate);

                return aAppointmentList;
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public List<Appointment> RetrieveAppointmentList(DateTime aSelectDate, String ScheduleType)
        {
            try
            {
                // 1. 依據約會建立的行事曆
                int DaysInSelectDate = System.DateTime.DaysInMonth(aSelectDate.Year, aSelectDate.Month);
                DateTime StartDate = new DateTime(aSelectDate.Year, aSelectDate.Month, 1);
                DateTime EndDate = new DateTime(aSelectDate.Year, aSelectDate.Month, DaysInSelectDate);

                // 取得約會集合
                EntityCollection AppointmentEntityCollection;
                if (ScheduleType == "差勤簽核")
                {
                    //行程類別:人資出差勤 = 6
                    AppointmentEntityCollection = this.m_ToolUtilityClass.RetrieveAppointmentsByFetchXmlAndScheduleType(StartDate, EndDate, "6");
                }
                else
                {
                    //行程類別:場地 = 4
                    AppointmentEntityCollection = this.m_ToolUtilityClass.RetrieveAppointmentsByFetchXmlAndScheduleType(StartDate, EndDate, "4");
                }

                // 建立約會
                List<Appointment> aAppointmentList = SetupAppointmentsListEntityCollection(AppointmentEntityCollection);

                // 2. 依據課程建立的行事曆
                //SetupAppointmentListByLesson(aAppointmentList, StartDate, EndDate);

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

                    SetupAppointmentList(aAppointmentsList, aAppointmentEntity, aFromOrToIdList, aFromOrToTypeList);

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

                int LeaveId = this.m_ToolUtilityClass.GetOptionSetAttribute(ref aAppointmentEntity, "new_leave_kind");
                String LeaveType = this.ConvertLeaveIdToAppointmentType(LeaveId);

                int LocationId = this.m_ToolUtilityClass.GetOptionSetAttribute(ref aAppointmentEntity, "new_location_kind");
                String LocationType = this.ConvertLocationIdToAppointmentType(LocationId);

                // 清空出席者及列席者清單
                aFromOrToIdList.Clear();
                // 清空出席者及列席者類型清單
                aFromOrToTypeList.Clear();

                //設定出席者的 ID 陣列
                this.m_ToolUtilityClass.GetActivityPartyIdList(aAppointmentEntity, "requiredattendees", aFromOrToIdList, aFromOrToTypeList);
                //設定列席者的 ID 陣列
                this.m_ToolUtilityClass.GetActivityPartyIdList(aAppointmentEntity, "optionalattendees", aFromOrToIdList, aFromOrToTypeList);

                Guid ListEntityId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aAppointmentEntity, "new_list_appointment");

                #region 如果不是教會行事曆，登入者有在此約會的出席者及列席者的約會才要顯示
                //if (AppointmentType != "教會行事曆")
                //{
                //    // 如果不是"教會行事曆"則要看看連絡人是否在出席者、列席者清單中
                //    bool SearchFlag = false;
                //    foreach (Guid ContactId in aFromOrToIdList)
                //    {
                //        if (m_ContactEntity.Id.ToString() == ContactId.ToString())
                //        {
                //            // 登入者有在此約會的出席者及列席者
                //            Appointment aAppointment = SetupAppointment(aAppointmentEntity, AppointmentType, CategoryId, LeaveId, LocationId);
                //            aAppointmentsList.Add(aAppointment);

                //            SearchFlag = true;
                //            break;
                //        }
                //    }

                //    if (ListEntityId != Guid.Empty && SearchFlag == false)
                //    {
                //        SetupAppointmentFromList(aAppointmentsList, aAppointmentEntity, ListEntityId, AppointmentType, CategoryId, LeaveId, LocationId);
                //    }
                //}
                //else
                //{
                //    // 如果是"教會行事曆"則一律要顯示
                //    Appointment aAppointment = SetupAppointment(aAppointmentEntity, AppointmentType, CategoryId, LeaveId, LocationId);
                //    aAppointmentsList.Add(aAppointment);
                //}
                #endregion

                // 迦南基督長老教會版本是只有差勤簽核及場地預約，所以就"全部"都要顯示
                Appointment aAppointment = SetupAppointment(aAppointmentEntity, AppointmentType, CategoryId, LeaveId, LocationId);
                aAppointmentsList.Add(aAppointment);

                #endregion

                return;
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public void SetupAppointmentFromList(List<Appointment> aAppointmentsList, Entity aAppointmentEntity, Guid ListEntityId, String AppointmentType, int CategoryId, int LeaveId, int LocationId)
        {
            try
            {
                #region 建立一個約會

                ArrayList MemberEntityIdList = this.m_ToolUtilityClass.GetAllMemberDataFromList(ListEntityId);

                foreach (Guid MemberId in MemberEntityIdList)
                {
                    if (m_ContactEntity.Id == MemberId)
                    {
                        // 登入者有在此約會的出席者及列席者
                        Appointment aAppointment = SetupAppointment(aAppointmentEntity, AppointmentType, CategoryId, LeaveId, LocationId);
                        aAppointmentsList.Add(aAppointment);

                        break;
                    }
                }
                #endregion
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }

        public Appointment SetupAppointment(Entity aAppointmentEntity, String AppointmentType, int CategoryId, int LeaveId, int LocationId)
        {
            try
            {
                #region 建立一個約會
                return new Appointment
                {
                    AppointmentId = aAppointmentEntity.Id.ToString(),
                    Text = this.m_ToolUtilityClass.GetEntityStringAttribute(aAppointmentEntity, "subject"),
                    AppointmentType = AppointmentType,
                    CategoryId = CategoryId,
                    LeaveId = LeaveId,
                    LocationId = LocationId,
                    OwnerId = new int[] { CategoryId },
                    StartDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(aAppointmentEntity, "scheduledstart").ToLocalTime(),
                    EndDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(aAppointmentEntity, "scheduledend").ToLocalTime(),
                    Description = this.m_ToolUtilityClass.GetEntityStringAttribute(aAppointmentEntity, "description"),
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
                        if (aLessonEntity.Id == aEnrolledLessonEntity.Id)
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
                        CategoryId = CategoryId,
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
        public void CreateAppointment(ref Appointment aAppointment)
        {
            try
            {
                #region 新增約會
                Entity aAppointmentEntity = new Entity("appointment");

                #region //設定"主題"
                if (aAppointment.Text == "")
                {
                    aAppointment.Text = SetAppointmentSubject(ref aAppointment, GetAppointmentSigningType(ref aAppointment));
                }
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aAppointmentEntity, "subject", aAppointment.Text);
                #endregion
                #region //設定"開始結束時間"
                this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aAppointmentEntity, "scheduledstart", aAppointment.StartDate);

                if (aAppointment.AllDay == true)
                {
                    aAppointment.EndDate = aAppointment.EndDate.AddDays(1);
                }
                else
                { }

                this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aAppointmentEntity, "scheduledend", aAppointment.EndDate);
                #endregion
                #region //設定"時數"及"日數"
                int Hours = 0;
                float Days = 0.0F;
                String HolidayDescription = "";
                //CalculateHoursAndDays(ref aAppointment, ref Hours, ref Days);
                if (aAppointment.AllDay != true)
                {
                    CalculateHoursAndDaysOfLocalDate(aAppointment.StartDate, aAppointment.EndDate, ref Hours, ref Days, ref HolidayDescription);
                }
                else
                {
                    CalculateHoursAndDaysOfAllDayEvent(aAppointment.StartDate, aAppointment.EndDate, ref Hours, ref Days, ref HolidayDescription);
                }
                if (HolidayDescription != "")
                {
                    //HolidayDescription = Environment.NewLine + "----------------------------------------------" + Environment.NewLine + "假日說明:" + Environment.NewLine + HolidayDescription;
                    HolidayDescription = Environment.NewLine + "----------------------------" + Environment.NewLine + "假日說明:" + Environment.NewLine + HolidayDescription;
                }

                this.m_ToolUtilityClass.SetEntityIntAttribute(ref aAppointmentEntity, "new_hours", Hours);
                this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aAppointmentEntity, "new_days", Days);
                //this.m_ToolUtilityClass.SetEntityFloatAttribute(ref aAppointmentEntity, "new_days", Days);
                #endregion
                #region //設定"簽核內容"
                String Content = GetAppointmentContent(ref aAppointment, GetAppointmentSigningType(ref aAppointment), Hours.ToString(), Days.ToString());
                //if( aAppointment.Description != null && aAppointment.Description != "")
                //{
                //    aAppointment.Description += Environment.NewLine + "----------------------------------------------" + Environment.NewLine;
                //}
                //String Description = aAppointment.Description + Content + HolidayDescription;

                //if ( GetAppointmentSigningType(ref aAppointment) == "請假簽核" )
                //{
                //    Content += GetAllAppointmentAsync(aAppointmentEntity);
                //}

                String Description = Content + HolidayDescription;
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aAppointmentEntity, "new_signing_content", Description);
                #endregion
                #region //設定"描述"
                //String Description = aAppointment.Description + Content + HolidayDescription;
                //Description = Content + HolidayDescription;
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aAppointmentEntity, "description", Description);
                #endregion
                #region //設定"行程類別"
                if (aAppointment.CategoryId != null && aAppointment.CategoryId > 0)
                {
                    if (aAppointment.CategoryId > 0)
                    {
                        this.m_ToolUtilityClass.SetOptionSetAttribute(ref aAppointmentEntity, "new_meeting_kind", (int)aAppointment.CategoryId);
                    }
                    else
                    {
                        // 新增約會沒填類別，預設就是"其他"
                        String SigningType = GetAppointmentSigningType(ref aAppointment);
                        if (SigningType == "請假簽核")
                        {
                            this.m_ToolUtilityClass.SetOptionSetAttribute(ref aAppointmentEntity, "new_meeting_kind", ConvertAppointmentTypeToCategoryId("人資出差勤"));
                        }
                        else if (SigningType == "場地或資源簽核")
                        {
                            this.m_ToolUtilityClass.SetOptionSetAttribute(ref aAppointmentEntity, "new_meeting_kind", ConvertAppointmentTypeToCategoryId("場地"));
                        }
                        else
                        {
                            this.m_ToolUtilityClass.SetOptionSetAttribute(ref aAppointmentEntity, "new_meeting_kind", ConvertAppointmentTypeToCategoryId("其他"));
                        }
                    }
                }
                else
                {
                    // 新增約會沒填類別，預設就是"其他"
                    String SigningType = GetAppointmentSigningType(ref aAppointment);
                    if (SigningType == "請假簽核")
                    {
                        this.m_ToolUtilityClass.SetOptionSetAttribute(ref aAppointmentEntity, "new_meeting_kind", ConvertAppointmentTypeToCategoryId("人資出差勤"));
                    }
                    else if (SigningType == "場地或資源簽核")
                    {
                        this.m_ToolUtilityClass.SetOptionSetAttribute(ref aAppointmentEntity, "new_meeting_kind", ConvertAppointmentTypeToCategoryId("場地"));
                    }
                    else
                    {
                        this.m_ToolUtilityClass.SetOptionSetAttribute(ref aAppointmentEntity, "new_meeting_kind", ConvertAppointmentTypeToCategoryId("其他"));
                    }
                }
                #endregion
                #region //設定"人資休假"
                if (aAppointment.LeaveId != null && aAppointment.LeaveId > 0)
                {
                    if (aAppointment.LeaveId > 0)
                    {
                        this.m_ToolUtilityClass.SetOptionSetAttribute(ref aAppointmentEntity, "new_leave_kind", (int)aAppointment.LeaveId);
                    }
                    else
                    {
                        // 新增約會沒填人資，預設就是"未填"
                        this.m_ToolUtilityClass.SetOptionSetAttribute(ref aAppointmentEntity, "new_leave_kind", 1);
                    }
                }
                else
                {
                    // 新增約會沒填人資，預設就是"未填"
                    this.m_ToolUtilityClass.SetOptionSetAttribute(ref aAppointmentEntity, "new_leave_kind", 1);
                }
                #endregion
                #region //設定"場地預約"
                if (aAppointment.LocationId != null && aAppointment.LocationId > 0)
                {
                    if (aAppointment.LocationId > 0)
                    {
                        this.m_ToolUtilityClass.SetOptionSetAttribute(ref aAppointmentEntity, "new_location_kind", (int)aAppointment.LocationId);
                    }
                    else
                    {
                        // 新增約會沒填場地，預設就是"未填"
                        this.m_ToolUtilityClass.SetOptionSetAttribute(ref aAppointmentEntity, "new_location_kind", 100000000);
                    }
                }
                else
                {
                    // 新增約會沒填場地，預設就是"未填"
                    this.m_ToolUtilityClass.SetOptionSetAttribute(ref aAppointmentEntity, "new_location_kind", 100000000);
                }
                #endregion
                #region//設定出席者
                aAppointmentEntity["requiredattendees"] = BuildReciever(m_ContactEntity);
                #endregion
                #region //設定召集人
                m_OwnerId = this.m_ToolUtilityClass.GetOwnerId(m_ContactEntity);

                Entity oParty = new Entity("activityparty");
                oParty["partyid"] = new EntityReference("systemuser", m_OwnerId);

                //Place the party record into a collection
                EntityCollection oCollection = new EntityCollection();
                oCollection.Entities.Add(oParty);

                //Set the organizer field to the collection:
                aAppointmentEntity["organizer"] = new EntityCollection();
                aAppointmentEntity["organizer"] = oCollection;
                #endregion
                #region //設定申請人
                if (m_ContactEntity.Id != Guid.Empty)
                {
                    this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aAppointmentEntity, "new_applier_appointment", "contact", m_ContactEntity.Id);
                }
                #endregion
                #region //設定代理人
                Guid aGuid = this.m_ToolUtilityClass.GetEntityLookupAttribute(m_ContactEntity, "new_replace_contact");
                if (aGuid != Guid.Empty)
                {
                    this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aAppointmentEntity, "new_replace_contact_appointment", "contact", aGuid);
                }
                else
                {
                    //沒有設代理人，但是仍要查看第二順位代理人
                    aGuid = this.m_ToolUtilityClass.GetEntityLookupAttribute(m_ContactEntity, "new_second_replace_contact");
                    if (aGuid != Guid.Empty)
                    {
                        this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aAppointmentEntity, "new_replace_contact_appointment", "contact", aGuid);
                    }
                }
                #endregion
                #region //設定主管
                aGuid = this.m_ToolUtilityClass.GetEntityLookupAttribute(m_ContactEntity, "new_manager_contact");
                if (aGuid != Guid.Empty)
                {
                    this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aAppointmentEntity, "new_manager_contact_appointment", "contact", aGuid);
                }
                #endregion
                #region //設定簽核者，如果是新增的約會簽核，預設就是代理人
                //aGuid = this.m_ToolUtilityClass.GetEntityLookupAttribute(m_ContactEntity, "new_replace_contact");
                //if (aGuid != Guid.Empty)
                //{
                //    this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aAppointmentEntity, "new_signing_contact", "contact", aGuid);
                //}
                #endregion
                #region//設定全天事件
                this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aAppointmentEntity, "isalldayevent", aAppointment.AllDay);
                #endregion
                #region//新增約會
                Guid CreatedAppointmentEntityId = this.m_ToolUtilityClass.CreateEntity(aAppointmentEntity);
                aAppointment.AppointmentId = CreatedAppointmentEntityId.ToString();
                #endregion
                #region //指派約會的負責人
                //m_OwnerId = this.m_ToolUtilityClass.GetOwnerId(m_ContactEntity);

                try
                {
                    this.m_ToolUtilityClass.AssignOwner("appointment", this.m_ToolUtilityClass.RetrieveEntity("appointment", CreatedAppointmentEntityId), this.m_OwnerId);
                }
                catch (System.Exception e)
                {
                    String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                }
                #endregion

                this.m_ToolUtilityClass.SetAppointmentStatusToScheduled(CreatedAppointmentEntityId);

                #endregion
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public void UpdateAppointment(Appointment aAppointment)
        {
            try
            {
                Entity aAppointmentEntity = this.m_ToolUtilityClass.RetrieveEntity("appointment", new Guid(aAppointment.AppointmentId));

                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aAppointmentEntity, "subject", aAppointment.Text);
                this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aAppointmentEntity, "scheduledstart", aAppointment.StartDate.ToLocalTime());
                this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aAppointmentEntity, "scheduledend", aAppointment.EndDate.ToLocalTime());
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aAppointmentEntity, "description", aAppointment.Description);

                //設定"行事曆類別"
                if (aAppointment.CategoryId != null)
                {
                    if (aAppointment.CategoryId > 0)
                    {
                        this.m_ToolUtilityClass.SetOptionSetAttribute(ref aAppointmentEntity, "new_meeting_kind", (int)aAppointment.CategoryId);
                    }
                }
                //設定"人資休假"
                if (aAppointment.LeaveId != null)
                {
                    if (aAppointment.LeaveId > 0)
                    {
                        this.m_ToolUtilityClass.SetOptionSetAttribute(ref aAppointmentEntity, "new_leave_kind", (int)aAppointment.LeaveId);
                    }
                }
                //設定"場地"
                if (aAppointment.LocationId > 0 != null)
                {
                    if (aAppointment.LocationId > 0)
                    {
                        this.m_ToolUtilityClass.SetOptionSetAttribute(ref aAppointmentEntity, "new_location_kind", (int)aAppointment.LocationId);
                    }
                }

                // 設定出席者
                aAppointmentEntity["requiredattendees"] = BuildReciever(m_ContactEntity);

                // 設定全天事件
                this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aAppointmentEntity, "isalldayevent", aAppointment.AllDay);

                // 修改約會
                this.m_ToolUtilityClass.UpdateEntity(aAppointmentEntity);

            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public void DeleteAppointment(Appointment aAppointment)
        {
            try
            {
                // 刪除約會
                this.m_ToolUtilityClass.DeleteEntity("appointment", new Guid(aAppointment.AppointmentId));
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
        #region 選項轉換
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
                case 8:
                    return "已報名課程";
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
                case "已報名課程":
                    return 8;
                default:
                    return 7;
            }
        }

        #endregion
        #region 人資對應工具
        private String ConvertLeaveIdToAppointmentType(int CategoryId)
        {
            switch (CategoryId)
            {
                case 1:
                    return "未填";
                case 2:
                    return "一例一休";
                case 3:
                    return "特休";
                case 4:
                    return "病假";
                case 5:
                    return "事假";
                case 6:
                    return "公假";
                case 7:
                    return "婚假";
                case 8:
                    return "喪假";
                default:
                    return "未填";
            }
        }

        private int ConvertAppointmentTypeToLeaveId(String AppointmentType)
        {
            switch (AppointmentType)
            {
                case "未填":
                    return 1;
                case "一例一休":
                    return 2;
                case "特休":
                    return 3;
                case "病假":
                    return 4;
                case "事假":
                    return 5;
                case "公假":
                    return 6;
                case "婚假":
                    return 7;
                case "喪假":
                    return 8;
                default:
                    return 1;
            }
        }

        #endregion
        #region 場地對應工具
        private String ConvertLocationIdToAppointmentType(int CategoryId)
        {
            switch (CategoryId)
            {
                case 100000000:
                    return "未填";
                case 100000001:
                    return "2F大堂";
                case 100000002:
                    return "2F交誼廳";
                case 100000003:
                    return "2F拉法";
                case 100000004:
                    return "2F以勒";
                case 100000005:
                    return "2F尼西";
                case 100000006:
                    return "2F沙龍";
                case 100000007:
                    return "B1大堂";
                case 100000008:
                    return "B1副堂";
                case 100000009:
                    return "B1交誼廳";
                case 100000010:
                    return "B1敬拜團室";
                case 100000011:
                    return "B1新小組組員";
                case 100000012:
                    return "B1講員VIP";
                default:
                    return "未填";
            }
        }

        private int ConvertAppointmentTypeToLocationId(String AppointmentType)
        {
            switch (AppointmentType)
            {
                case "未填":
                    return 100000000;
                case "2F大堂":
                    return 100000001;
                case "2F交誼廳":
                    return 100000002;
                case "2F拉法":
                    return 100000003;
                case "2F以勒":
                    return 100000004;
                case "2F尼西":
                    return 100000005;
                case "2F沙龍":
                    return 100000006;
                case "B1大堂":
                    return 100000007;
                case "B1副堂":
                    return 100000008;
                case "B1交誼廳":
                    return 100000009;
                case "B1敬拜團室":
                    return 100000010;
                case "B1新小組組員":
                    return 100000011;
                case "B1講員VIP":
                    return 100000012;
                default:
                    return 1;
            }
        }

        #endregion
        #region 簽核狀態
        public String ConvertIndexToSigningStatus(int OptionValue)
        {
            try
            {
                switch (OptionValue)
                {
                    #region Switch
                    case 100000000:
                        {
                            return "初始值";
                        }
                    case 100000001:
                        {
                            return "同意";
                        }
                    case 100000002:
                        {
                            return "退回";
                        }
                    default:
                        {
                            return "初始值";
                        }
                        #endregion
                }
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public int ConvertSigningStatusToIndex(String OptionValue)
        {
            try
            {
                switch (OptionValue)
                {
                    #region Switch
                    case "初始值":
                        {
                            return 100000000;
                        }
                    case "同意":
                        {
                            return 100000001;
                        }
                    case "退回":
                        {
                            return 100000002;
                        }
                    default:
                        {
                            return 100000000;
                        }
                        #endregion
                }
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }

        #endregion
        #region 簽核事件
        public String ConvertIndexToSigningEvent(int OptionValue)
        {
            try
            {
                switch (OptionValue)
                {
                    #region Switch
                    case 100000000:
                        {
                            return "無事件";
                        }
                    case 100000001:
                        {
                            return "簽核";
                        }
                    case 100000002:
                        {
                            return "時間";
                        }
                    case 100000003:
                        {
                            return "場地";
                        }
                    case 100000004:
                        {
                            return "請假類別";
                        }
                    default:
                        {
                            return "初始值";
                        }
                        #endregion
                }
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public int ConvertSigningEventToIndex(String OptionValue)
        {
            try
            {
                switch (OptionValue)
                {
                    #region Switch
                    case "無事件":
                        {
                            return 100000000;
                        }
                    case "簽核":
                        {
                            return 100000001;
                        }
                    case "時間":
                        {
                            return 100000002;
                        }
                    case "場地":
                        {
                            return 100000003;
                        }
                    case "請假類別":
                        {
                            return 100000004;
                        }
                    default:
                        {
                            return 100000000;
                        }
                        #endregion
                }
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }

        #endregion
        #region 人資簽核狀態
        public String ConvertIndexToLeaveSigningStatus(int OptionValue)
        {
            try
            {
                switch (OptionValue)
                {
                    #region Switch
                    case 100000000:
                        {
                            return "初始狀態";
                        }
                    case 100000001:
                        {
                            return "等待代理人簽核中";
                        }
                    case 100000002:
                        {
                            return "代理人未簽核";
                        }
                    case 100000003:
                        {
                            return "代理人未批准";
                        }
                    case 100000004:
                        {
                            return "等待主管簽核中";
                        }
                    case 100000005:
                        {
                            return "主管未簽核";
                        }
                    case 100000006:
                        {
                            return "主管未批准";
                        }
                    case 100000007:
                        {
                            return "主管已簽核";
                        }
                    case 100000008:
                        {
                            return "病假結案";
                        }
                    default:
                        {
                            return "初始值";
                        }
                        #endregion
                }
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public int ConvertLeaveSigningStatusToIndex(String OptionValue)
        {
            try
            {
                switch (OptionValue)
                {
                    #region Switch
                    case "初始狀態":
                        {
                            return 100000000;
                        }
                    case "簽核等待代理人簽核中":
                        {
                            return 100000001;
                        }
                    case "代理人未簽核":
                        {
                            return 100000002;
                        }
                    case "代理人未批准":
                        {
                            return 100000003;
                        }
                    case "等待主管簽核中":
                        {
                            return 100000004;
                        }
                    case "主管未簽核":
                        {
                            return 100000005;
                        }
                    case "主管未批准":
                        {
                            return 100000006;
                        }
                    case "主管已簽核":
                        {
                            return 100000007;
                        }
                    case "病假結案":
                        {
                            return 100000008;
                        }
                    default:
                        {
                            return 100000000;
                        }
                        #endregion
                }
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }

        #endregion
        #endregion
        #region 工具區
        public String SetAppointmentSubject(ref Appointment aAppointment, String SigningType)
        {
            try
            {
                #region 如果約會沒有設定主題，在這裡幫忙設定
                // 設定申請人
                String Subject = SigningType + "-" + this.m_ToolUtilityClass.GetEntityStringAttribute(ref m_ContactEntity, "fullname") + "-";
                if (SigningType == "請假簽核")
                {
                    String Leave = this.ConvertLeaveIdToAppointmentType((int)aAppointment.LeaveId);
                    Subject += "請假假由: " + Leave + "，";
                    Subject += "開始時間: " + aAppointment.StartDate.ToLocalTime() + "，";
                    Subject += "結束時間: " + aAppointment.EndDate.ToLocalTime() + "。";
                }
                else if (SigningType == "場地或資源簽核")
                {
                    String Location = this.ConvertLocationIdToAppointmentType((int)aAppointment.LocationId);
                    Subject += "場地或資源: " + Location + "，";
                    Subject += "開始時間: " + aAppointment.StartDate.ToLocalTime() + "，";
                    Subject += "結束時間: " + aAppointment.EndDate.ToLocalTime() + "。";
                }
                else
                {
                    Subject += "開始時間: " + aAppointment.StartDate.ToLocalTime() + "，";
                    Subject += "結束時間: " + aAppointment.EndDate.ToLocalTime() + "。";
                }

                return Subject;
                #endregion
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //Monitor.Exit(this);
                throw e;
            }
        }
        public String GetAppointmentSigningType(ref Appointment aAppointment)
        {
            try
            {
                #region 如果約會沒有設定主題，在這裡幫忙設定
                if (aAppointment.LeaveId != null && aAppointment.LeaveId > 0)
                {
                    return "請假簽核";
                }
                else if (aAppointment.LocationId != null && aAppointment.LocationId > 0)
                {
                    return "場地或資源簽核";
                }
                else
                {
                    return "一般行事曆";
                }
                #endregion
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //Monitor.Exit(this);
                throw e;
            }
        }
        public String GetAppointmentContent(ref Appointment aAppointment, String SigningType, String Hours, String Days)
        {
            try
            {
                //設定"簽核內容"
                #region 建立出差勤及場地預約的本文訊息
                String Content = "";
                String LineId = "";

                if (SigningType == "請假簽核")
                {
                    Content = "申請人: " + this.m_ToolUtilityClass.GetEntityStringAttribute(ref m_ContactEntity, "fullname") + Environment.NewLine;

                    #region //設定代理人
                    String aReplaceContact = this.m_ToolUtilityClass.GetEntityLookupDisplayName(ref m_ContactEntity, "new_replace_contact");
                    if (aReplaceContact != "")
                    {
                        Content += "代理人: " + aReplaceContact + Environment.NewLine;
                    }
                    else
                    {
                        //沒有設代理人，但是仍要查看第二順位代理人
                        aReplaceContact = this.m_ToolUtilityClass.GetEntityLookupDisplayName(ref m_ContactEntity, "new_second_replace_contact");
                        if (aReplaceContact != "")
                        {
                            Content += "代理人: " + aReplaceContact + Environment.NewLine;
                        }
                    }
                    #endregion

                    String Leave = this.ConvertLeaveIdToAppointmentType((int)aAppointment.LeaveId);
                    Content += "請假假由: " + Leave + Environment.NewLine;
                    if (aAppointment.AllDay != true)
                    {
                        Content += "開始時間: " + aAppointment.StartDate.ToLocalTime() + Environment.NewLine;
                        Content += "結束時間: " + aAppointment.EndDate.ToLocalTime() + Environment.NewLine;
                    }
                    else
                    {
                        Content += "開始時間: " + aAppointment.StartDate.ToLocalTime().ToShortDateString() + Environment.NewLine;
                        Content += "結束時間: " + aAppointment.EndDate.AddDays(-1).ToLocalTime().ToShortDateString() + Environment.NewLine;
                    }
                    //Content += "期間(小時數為單位): " + Hours + " 小時" + Environment.NewLine;
                    //Content += "期間(日數為單位): " + Days + " 日" + Environment.NewLine;
                    Content += "總計期間: " + Days + " 日" + Environment.NewLine;
                    if (aAppointment.Description != null && aAppointment.Description != "")
                    {
                        Content += "說明: " + aAppointment.Description + Environment.NewLine;
                    }

                    //Content = Environment.NewLine + "----------------------------------------------" + Environment.NewLine + Content;
                    return Content;
                }
                else if (SigningType == "場地或資源簽核")
                {
                    Content = "申請人: " + this.m_ToolUtilityClass.GetEntityStringAttribute(ref m_ContactEntity, "fullname") + Environment.NewLine;
                    String Location = this.ConvertLocationIdToAppointmentType((int)aAppointment.LocationId);
                    Content += "場地或資源: " + Location + Environment.NewLine;
                    //Content += "開始時間: " + aAppointment.StartDate.ToLocalTime() + Environment.NewLine;
                    //Content += "結束時間: " + aAppointment.EndDate.ToLocalTime() + Environment.NewLine;
                    if (aAppointment.AllDay != true)
                    {
                        Content += "開始時間: " + aAppointment.StartDate.ToLocalTime() + Environment.NewLine;
                        Content += "結束時間: " + aAppointment.EndDate.ToLocalTime() + Environment.NewLine;
                    }
                    else
                    {
                        Content += "開始時間: " + aAppointment.StartDate.ToLocalTime().ToShortDateString() + Environment.NewLine;
                        Content += "結束時間: " + aAppointment.EndDate.AddDays(-1).ToLocalTime().ToShortDateString() + Environment.NewLine;
                    }
                    //Content += "期間(小時數為單位): " + Hours + " 小時" + Environment.NewLine;
                    //Content += "期間(日數為單位): " + Days + " 日" + Environment.NewLine;
                    //Content += "總計期間: " + Days + " 日" + Environment.NewLine;
                    TimeSpan aTimeSpan = new TimeSpan(aAppointment.EndDate.Ticks - aAppointment.StartDate.Ticks);
                    Content += "總計期間: " + aTimeSpan.Hours + " 小時" + Environment.NewLine;
                    if (aAppointment.Description != null && aAppointment.Description != "")
                    {
                        Content += "說明: " + aAppointment.Description + Environment.NewLine;
                    }

                    //Content = Environment.NewLine + "----------------------------------------------" + Environment.NewLine + Content;
                    return Content;
                }
                else
                {
                    return Content;
                }

                #endregion
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //Monitor.Exit(this);
                throw e;
            }
        }
        public void CalculateHoursAndDays(ref Appointment aAppointment, ref int Hours, ref float Days)
        {
            try
            {
                #region 建立出差勤及場地預約的本文訊息
                TimeSpan TimeSpan = new TimeSpan(aAppointment.EndDate.Ticks - aAppointment.StartDate.Ticks);

                Days = TimeSpan.Days;


                #endregion
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //Monitor.Exit(this);
                throw e;
            }
        }
        public void CalculateHoursAndDaysOfLocalDate(ref Appointment aAppointment, ref int Hours, ref float Days)
        {
            try
            {
                #region 建立出差勤及場地預約的本文訊息
                int MorningHour = 0;
                int AfternoonHour = 0;
                if (aAppointment.StartDate.Hour <= 12)
                {
                    if (aAppointment.EndDate.Hour >= 17)
                    {
                        MorningHour = 12 - aAppointment.StartDate.Hour;
                        AfternoonHour = 4;
                    }
                    else if (aAppointment.EndDate.Hour <= 12)
                    {
                        MorningHour = aAppointment.EndDate.Hour - aAppointment.EndDate.Hour;
                    }
                    else if (aAppointment.EndDate.Hour >= 12 && aAppointment.EndDate.Hour >= 17)
                    {
                        AfternoonHour = 17 - aAppointment.EndDate.Hour;
                    }
                }
                else if (aAppointment.StartDate.Hour >= 13)
                {
                    if (aAppointment.EndDate.Hour >= 17)
                    {
                        AfternoonHour = 17 - aAppointment.StartDate.Hour;
                    }
                    else
                    {
                        AfternoonHour = aAppointment.EndDate.Hour - aAppointment.EndDate.Hour;
                    }
                }

                #endregion
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //Monitor.Exit(this);
                throw e;
            }
        }
        public void CalculateHoursAndDaysOfLocalDate(DateTime StartDate, DateTime EndDate, ref int Hours, ref float Days, ref String Description)
        {
            try
            {
                DateTime CalculateStartDate = StartDate;
                DateTime CalculateEndDate = EndDate;

                DateTime TimeSpanStartDate = new DateTime(CalculateStartDate.Year, CalculateStartDate.Month, CalculateStartDate.Day, 0, 0, 0);
                DateTime TimeSpanEndDate = new DateTime(CalculateEndDate.Year, CalculateEndDate.Month, CalculateEndDate.Day, 0, 0, 0);
                TimeSpan TimeSpan = new TimeSpan(TimeSpanEndDate.Ticks - TimeSpanStartDate.Ticks);
                int TimeSpanDays = TimeSpan.Days - 1;
                if (SetCalculateStartEndDate(StartDate, EndDate, ref CalculateStartDate, ref CalculateEndDate) == true)
                {
                    Hours = GetHour(CalculateStartDate, CalculateEndDate);
                }
                else
                {
                    int FirstDateHour = GetHour(CalculateStartDate, new DateTime(CalculateStartDate.Year, CalculateStartDate.Month, CalculateStartDate.Day, 17, 0, 0));
                    int LastDateHour = GetHour(new DateTime(CalculateEndDate.Year, CalculateEndDate.Month, CalculateEndDate.Day, 8, 0, 0), CalculateEndDate);

                    //int LeaveDays = TimeSpanDays - GetHolidayNumber(TimeSpanStartDate.AddDays(1), TimeSpanEndDate.AddDays(-1));
                    int LeaveDays = TimeSpanDays - GetHolidayNumber(TimeSpanStartDate.AddDays(1), TimeSpanEndDate, ref Description);

                    Hours = FirstDateHour + LeaveDays * 8 + LastDateHour;
                }

                Days = (float)Hours / 8.0F;
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //Monitor.Exit(this);
                throw e;
            }
        }
        public void CalculateHoursAndDaysOfAllDayEvent(DateTime StartDate, DateTime EndDate, ref int Hours, ref float Days, ref String Description)
        {
            try
            {
                DateTime TimeSpanStartDate = new DateTime(StartDate.Year, StartDate.Month, StartDate.Day, 0, 0, 0);
                DateTime TimeSpanEndDate = new DateTime(EndDate.Year, EndDate.Month, EndDate.Day, 0, 0, 0);
                TimeSpan TimeSpan = new TimeSpan(TimeSpanEndDate.Ticks - TimeSpanStartDate.Ticks);

                int LeaveDays = TimeSpan.Days - GetHolidayNumber(StartDate, EndDate, ref Description);
                if (LeaveDays == 1)
                {
                    Hours = 8;
                }
                else
                {
                    Hours = LeaveDays * 8;
                }

                Days = (float)Hours / 8.0F;
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //Monitor.Exit(this);
                throw e;
            }
        }
        private bool SetCalculateStartEndDate(DateTime StartDate, DateTime EndDate, ref DateTime CalculateStartDate, ref DateTime CalculateEndDate)
        {
            try
            {
                if (StartDate.Date == EndDate.Date)
                {
                    //CalculateStartDate = StartDate;
                    //CalculateEndDate = EndDate;
                    return true;
                }
                else
                {
                    //CalculateStartDate = EndDate;
                    //CalculateEndDate = StartDate;
                    return false;
                }
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //Monitor.Exit(this);
                throw e;
            }
        }
        private int GetHour(DateTime StartDate, DateTime EndDate)
        {
            try
            {
                #region 建立出差勤及場地預約的本文訊息
                if (StartDate.Hour < 8 && StartDate.Hour <= 12 && EndDate.Hour >= 8 && EndDate.Hour <= 12)
                {
                    return EndDate.Hour - 8;
                }
                else if (StartDate.Hour >= 8 && StartDate.Hour <= 12 && EndDate.Hour >= 8 && EndDate.Hour <= 12)
                {
                    return EndDate.Hour - StartDate.Hour;
                }
                else if (StartDate.Hour >= 8 && StartDate.Hour <= 12 && EndDate.Hour >= 13 && EndDate.Hour <= 17)
                {
                    return (12 - StartDate.Hour) + (EndDate.Hour - 13);
                }
                else if (StartDate.Hour >= 12 && StartDate.Hour <= 17 && EndDate.Hour >= 13 && EndDate.Hour <= 17)
                {
                    if (StartDate.Hour < 13)
                    {
                        return EndDate.Hour - 13;
                    }
                    else
                    {
                        return EndDate.Hour - StartDate.Hour;
                    }
                }
                else if (StartDate.Hour >= 12 && StartDate.Hour <= 17 && EndDate.Hour >= 13 && EndDate.Hour > 17)
                {
                    if (StartDate.Hour < 13)
                    {
                        return 17 - 13;
                    }
                    else
                    {
                        return 17 - StartDate.Hour;
                    }
                }
                else
                {
                    return 0;
                }

                #endregion
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //Monitor.Exit(this);
                throw e;
            }
        }
        private int GetHolidayNumber(DateTime StartDate, DateTime EndDate, ref String Description)
        {
            try
            {
                #region 請假期間有幾個星期一

                DateTime TimeSpanStartDate = new DateTime(StartDate.Year, StartDate.Month, StartDate.Day, 0, 0, 0);
                DateTime TimeSpanEndDate = new DateTime(EndDate.Year, EndDate.Month, EndDate.Day, 0, 0, 0);
                TimeSpan TimeSpan = new TimeSpan(TimeSpanEndDate.Ticks - TimeSpanStartDate.Ticks);

                int HolidayNumber = 0;
                DateTime AuditDate = TimeSpanStartDate;
                Record[] aNationHolidayArray = GetNationHoliday();

                for (int i = 0; i < TimeSpan.Days; i++)
                {
                    AuditDate = StartDate.AddDays(i);

                    String NationHolidayDescription = "";
                    if (AuditDate.DayOfWeek == DayOfWeek.Monday)
                    {
                        Description += Environment.NewLine + AuditDate.Date.ToShortDateString() + Environment.NewLine + "星期一放假日" + Environment.NewLine;
                        HolidayNumber++;
                    }
                    else if (IsAHolidayRecord(AuditDate, aNationHolidayArray, ref NationHolidayDescription) == true)
                    {
                        Description += NationHolidayDescription;
                        HolidayNumber++;
                    }
                }

                return HolidayNumber;
                #endregion
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //Monitor.Exit(this);
                throw e;
            }
        }
        private bool IsAHolidayRecord(DateTime AuditDate, Record[] aNationHolidayArray, ref String Description)
        {
            try
            {
                #region

                foreach (Record aRecord in aNationHolidayArray)
                {
                    DateTime aRecordDateTime = DateTime.Parse(aRecord.date);

                    if (AuditDate.Date == aRecordDateTime.Date)
                    {
                        if (aRecord.isHoliday == "是")
                        {
                            if (aRecord.holidayCategory == "放假之紀念日及節日" || aRecord.holidayCategory == "調整放假日")
                            {
                                if (aRecord.name != "" && aRecord.description != "")
                                {
                                    Description = Environment.NewLine + AuditDate.Date.ToShortDateString() + Environment.NewLine + aRecord.name + Environment.NewLine + aRecord.holidayCategory + Environment.NewLine + aRecord.description + Environment.NewLine;
                                }
                                else if (aRecord.name == "" && aRecord.description == "")
                                {
                                    Description = Environment.NewLine + AuditDate.Date.ToShortDateString() + Environment.NewLine + aRecord.holidayCategory + Environment.NewLine;
                                }
                                else
                                {
                                    Description = Environment.NewLine + AuditDate.Date.ToShortDateString() + Environment.NewLine + aRecord.name + Environment.NewLine + aRecord.holidayCategory + Environment.NewLine + aRecord.description + Environment.NewLine;
                                }

                                return true;
                            }
                        }
                    }
                }

                return false;
                #endregion
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //Monitor.Exit(this);
                throw e;
            }
        }
        private Record[] GetNationHoliday()
        {
            try
            {
                #region
                //var url = "http://data.ntpc.gov.tw/api/v1/rest/datastore/382000000A-000077-002";

                //api/datasets/308DCD75-6434-45BC-A95F-584DA4FED251/json
                //var url = "https://data.ntpc.gov.tw/datasets/308DCD75-6434-45BC-A95F-584DA4FED251";
                //var url = "https://data.ntpc.gov.tw/api/datasets/308DCD75-6434-45BC-A95F-584DA4FED251/json/preview";
                //var url = "https://data.ntpc.gov.tw/api/datasets/308DCD75-6434-45BC-A95F-584DA4FED251/json";
                var url = "https://data.ntpc.gov.tw/api/datasets/308DCD75-6434-45BC-A95F-584DA4FED251/json?page=4&size=200";
                var request = WebRequest.Create(url);
                // 透過 Chrome 開發者工具可以取得 Method, ContentType
                request.Method = "GET";
                request.ContentType = "application/json;charset=UTF-8";
                //取得 request 的 response stream
                var response = request.GetResponse() as HttpWebResponse;
                var responseStream = response.GetResponseStream();
                var reader = new StreamReader(responseStream, Encoding.GetEncoding("utf-8"));
                var srcString = reader.ReadToEnd();

                Record[] RecordArray = Newtonsoft.Json.JsonConvert.DeserializeObject<Record[]>(srcString);

                //HolidayOpenData jsonData = Newtonsoft.Json.JsonConvert.DeserializeObject<HolidayOpenData>(srcString);

                return RecordArray;
                #endregion
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //Monitor.Exit(this);
                throw e;
            }
        }
        private void SetUserType(ref String UserType)
        {
            // 找登入使用者及其ID
            if (this.m_ToolUtilityClass.GetEntityLookupAttribute(ref this.m_ContactEntity, "new_replace_contact") != Guid.Empty)
            {
                // 代理人有填
                if (this.m_ToolUtilityClass.GetEntityLookupAttribute(ref this.m_ContactEntity, "new_manager_contact") != Guid.Empty)
                {
                    // 有填主管
                    UserType = "行政同工";
                }
                else
                {
                    // 沒填主管
                    UserType = "非行政同工";
                }
            }
            else
            {
                if (this.m_ToolUtilityClass.GetEntityLookupAttribute(ref this.m_ContactEntity, "new_second_replace_contact") != Guid.Empty)
                {
                    // 代理人沒填但是第二順位代理人都有填
                    if (this.m_ToolUtilityClass.GetEntityLookupAttribute(ref this.m_ContactEntity, "new_manager_contact") != Guid.Empty)
                    {
                        // 有填主管
                        UserType = "行政同工";
                    }
                    else
                    {
                        // 沒填主管
                        UserType = "非行政同工";
                    }

                }
                else
                {
                    // 代理人及第二順位代理人都沒填
                    UserType = "非行政同工";
                }
            }
        }
        #endregion
        #region 顯示出差勤休假統計
        public String GetAllAppointmentAsync(Entity aAppointmentEntity)
        {
            try
            {
                //this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, "GetAllAppointmentAsync-001");
                Guid aApplierId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aAppointmentEntity, "new_applier_appointment");
                #region //設定申請人
                //this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, "GetAllAppointmentAsync-002");
                Entity aApplier = this.m_ContactEntity;
                //this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, "GetAllAppointmentAsync-003");

                String ContactFullName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aApplier, "fullname");
                //this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, "GetAllAppointmentAsync-004");

                #endregion
                #region//取得休假集合
                //this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, "GetAllAppointmentAsync-005");
                EntityCollection aAppointmentEntityCollection = this.m_ToolUtilityClass.RetrieveAppointmentsByFetchXml(ContactFullName, aApplier.Id.ToString());
                //this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, "GetAllAppointmentAsync-006");
                #endregion
                if (aAppointmentEntityCollection.Entities.Count > 0)
                {
                    //this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, "GetAllAppointmentAsync-007");
                    //this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, Environment.NewLine + "------------------------ " + Environment.NewLine + ContactFullName + " 今年休假統計:" + Environment.NewLine + GetAllAppointments(aAppointmentEntityCollection));
                    String aSpecialLeaveDays = "依據年資應得特休日數 = " + this.m_ToolUtilityClass.GetEntityIntAttribute(ref aApplier, "new_special_leave_days").ToString() + "日" + Environment.NewLine + "---------------------";
                    return Environment.NewLine + "------------------------ " + Environment.NewLine + ContactFullName + " 今年休假統計:" + Environment.NewLine + aSpecialLeaveDays + Environment.NewLine + GetAllAppointments(aAppointmentEntityCollection);
                }
                else
                {
                    //this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, "GetAllAppointmentAsync-008");
                    return Environment.NewLine + "------------------------ " + Environment.NewLine + ContactFullName + "您好，您沒有任何休假紀錄。";
                }
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw e;
            }
        }
        public String GetAllAppointments(EntityCollection aAppointmentEntityCollection)
        {
            try
            {
                foreach (Entity aAppointment in aAppointmentEntityCollection.Entities)
                {
                    String AppointmentKey = ConvertLeaveIdToAppointmentType(this.m_ToolUtilityClass.GetOptionSetAttribute(aAppointment, "new_leave_kind"));
                    AppointmentKey += ":" + ConvertIndexToLeaveSigningStatus(this.m_ToolUtilityClass.GetOptionSetAttribute(aAppointment, "new_leave_signing_status"));

                    double Days = this.m_ToolUtilityClass.GetEntityDoubleAttribute(aAppointment, "new_days");

                    AddToDictionary(ref this.m_SigningReport, AppointmentKey, Days);
                }

                return GetSigningReport();
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public String GetSigningReport()
        {
            try
            {
                String SigningReport = "";
                foreach (var aSigningReport in m_SigningReport)
                {
                    SigningReport += aSigningReport.Key + "=" + aSigningReport.Value.ToString() + "日" + Environment.NewLine;
                }
                return SigningReport;
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        private bool AddToDictionary(ref Dictionary<String, double> aDictionary, String Method, double Content)
        {
            try
            {
                if (aDictionary.ContainsKey(Method))
                {
                    // 關鍵( Key ) 已經在字典裡了
                    aDictionary[Method] += Content;
                    return true;
                }
                else
                {
                    // 關鍵( Key )還沒有在字典裡
                    aDictionary.Add(Method, Content);
                    return false;
                }
            }
            catch (FormatException)
            {
                return false;
            }
            catch (System.Exception e)
            {
                throw e;
            }
        }

        #endregion

    }
}
