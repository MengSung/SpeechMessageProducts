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


