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

                return SetupAppointmentsListEntityCollection( AppointmentEntityCollection );
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public List<Appointment> SetupAppointmentsListEntityCollection (EntityCollection AppointmentEntityCollection)
        {
            try
            {

                List<Appointment> aAppointmentsList = new List<Appointment>();

                ArrayList aFromOrToIdList = new ArrayList();
                ArrayList aFromOrToTypeList = new ArrayList();
                foreach (Entity aAppointmentEntity in AppointmentEntityCollection.Entities)
                {
                    aFromOrToIdList.Clear();
                    aFromOrToTypeList.Clear();

                    this.m_ToolUtilityClass.GetActivityPartyIdList(aAppointmentEntity, "requiredattendees", aFromOrToIdList, aFromOrToTypeList);
                    this.m_ToolUtilityClass.GetActivityPartyIdList(aAppointmentEntity, "optionalattendees", aFromOrToIdList, aFromOrToTypeList);
                    
                    foreach (Guid ContactId in aFromOrToIdList)
                    {
                        if ( m_ContactEntity.Id.ToString() == ContactId.ToString() )
                        {
                            aAppointmentsList.Add
                            (
                                new Appointment
                                {
                                    AppointmentId = aAppointmentEntity.Id.ToString(),
                                    Text = this.m_ToolUtilityClass.GetEntityStringAttribute(aAppointmentEntity, "subject"),
                                    OwnerId = new int[] { 1 },
                                    StartDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(aAppointmentEntity, "scheduledstart").ToLocalTime(),
                                    EndDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(aAppointmentEntity, "scheduledend").ToLocalTime(),
                                }
                            );

                            break;
                        }
                    }
                }

                return aAppointmentsList;
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }

        #endregion
        #region 上傳資料
        #endregion

    }
}
