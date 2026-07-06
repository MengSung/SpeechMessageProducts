// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Models/AppointmentsListManager.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於資料模型或 ViewModel 層，註解重點在說明欄位語意、序列化/繫結用途與相容性限制。
// 主要型別：class AppointmentsListManager
// 主要成員：SetupAppointmentList、SetupAppointment、CreateAppointment、UpdateAppointment、DeleteAppointment、GroupId、RoomId、LineUserId、ViewType、ScheduleType
// 引用命名空間：ChurchReport.WebServiceConnector、Microsoft.Xrm.Sdk、System、System.Collections.Generic、System.Linq、System.Threading.Tasks
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using ChurchReport.WebServiceConnector;
using Microsoft.Xrm.Sdk;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ChurchReport.Models
{
    public class AppointmentsListManager
    {
        public AppointmentsListManager()
        {
        }

        public String m_Account ="";
        public String m_Password = "";

        public Entity m_LoginContact = new Entity();

        public string GroupId { get; set; } = "";
        public string RoomId { get; set; } = "";
        public string LineUserId { get; set; } = "";
        public string ViewType { get; set; } = "";
        public string ScheduleType { get; set; } = "場地及資源預約"; // 差勤簽核 OR 場地及資源預約

        public DateTime m_PreviousDate { get; set; } = DateTime.Now; // 之前選擇的日期
        public DateTime m_SelectDate { get; set; } = DateTime.Now; // 行事曆日期

        public String UserType = "行政同工";

        // 要顯示的約會清單
        public List<Appointment> m_Appointments = new List<Appointment>();

        AppointmentsDownUpLoader m_AppointmentsDownUpLoader = new AppointmentsDownUpLoader();

        public List<Appointment> SetupAppointmentList()
        {
            try
            {
                if (m_Account != "" || LineUserId != "")
                {
                    if (m_PreviousDate == null || m_PreviousDate.Year == 1)
                    {
                        // 這是第一次載入約會，所以預設是本日
                        m_PreviousDate = m_SelectDate = DateTime.Now;

                        SetupAppointment();
                    }
                    else
                    {
                        if (m_PreviousDate != m_SelectDate)
                        {
                            // 有改變選擇的日期
                            m_PreviousDate = m_SelectDate;

                            SetupAppointment();
                        }
                        else
                        {
                            // 沒變選擇的日期
                            if (m_SelectDate.Date == DateTime.Now.Date)
                            {
                                SetupAppointment();
                            }
                            else
                            {
                                SetupAppointment();
                            }
                        }
                    }
                }

                return m_Appointments;
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public void SetupAppointment()
        {
            try
            {
                // 載入使用者的本月約會
                // UserType 會在此取得行政同工
                // ScheduleType 是下參數決定是取得和種類的約會，差勤簽核 OR 場地及資源預約
                m_Appointments = this.m_AppointmentsDownUpLoader.GetAppointmentList(this.m_Account, this.m_Password, this.m_SelectDate, ref UserType , ScheduleType );
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }

        public void CreateAppointment(ref Appointment aAppointment)
        {
            try
            {
                // 新增約會
                this.m_AppointmentsDownUpLoader.CreateAppointment(ref aAppointment);

                //this.m_Appointments.Add(aAppointment);

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
                // 修改約會
                this.m_AppointmentsDownUpLoader.UpdateAppointment(aAppointment);
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
                this.m_AppointmentsDownUpLoader.DeleteAppointment(aAppointment);
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }


    }
}


