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
            m_PreviousDate = new DateTime(1900, 1, 1);
        }

        public String m_Account;
        public String m_Password;

        public string GroupId { get; set; } = "";
        public string RoomId { get; set; } = "";
        public string LineUserId { get; set; } = "";
        public string ViewType { get; set; } = "";

        public DateTime m_PreviousDate { get; set; } // 之前選擇的日期
        public DateTime m_SelectDate { get; set; } // 行事曆日期

        public List<Appointment> m_Appointments;

        public List<Appointment> SetupAppointmentList()
        {
            try
            {
                if (m_PreviousDate.Year == 1900)
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
                m_Appointments = new List<Appointment> {
                    new Appointment {
                        AppointmentId = 1,
                        Text = "503教室-白碧娥",
                        OwnerId = new int[] { 4 },
                        //StartDate = new DateTime(2015, 5, 25, 9, 30, 0),
                        StartDate = m_SelectDate.AddDays(-1).AddHours(2),
                        //EndDate = new DateTime(2015, 5, 25, 11, 30, 0)
                        EndDate =  m_SelectDate.AddDays(-1).AddHours(4),
                    },
                    new Appointment {
                        AppointmentId = 2,
                        Text = "親子教室-吳創然",
                        OwnerId = new int[] { 2 },
                        //StartDate = new DateTime(2015, 5, 25, 9, 30, 0),
                        StartDate = m_SelectDate.AddDays(-2).AddHours(2),
                        //EndDate = new DateTime(2015, 5, 25, 11, 30, 0)
                        EndDate =  m_SelectDate.AddDays(-2).AddHours(4),
                        AllDay = true
                    },
                    new Appointment {
                        AppointmentId = 3,
                        Text = "張全興出差至台北",
                        OwnerId = new int[] { 1 },
                        //StartDate = new DateTime(2015, 5, 25, 9, 30, 0),
                        StartDate = m_SelectDate.AddDays(-3).AddHours(2),
                        //EndDate = new DateTime(2015, 5, 25, 11, 30, 0)
                        EndDate =  m_SelectDate.AddDays(-3).AddHours(4),
                    },
                    new Appointment {
                        AppointmentId = 4,
                        Text = "郭寬宏牧師特會",
                        OwnerId = new int[] { 3 },
                        //StartDate = new DateTime(2015, 5, 25, 9, 30, 0),
                        StartDate = m_SelectDate.AddDays(-1).AddHours(4),
                        //EndDate = new DateTime(2015, 5, 25, 11, 30, 0)
                        EndDate =  m_SelectDate.AddDays(-1).AddHours(6),
                    },
                    new Appointment {
                        AppointmentId = 5,
                        Text = "502教室-林文也",
                        OwnerId = new int[] { 1 },
                        //StartDate = new DateTime(2015, 5, 25, 9, 30, 0),
                        StartDate = m_SelectDate.AddDays(-2).AddHours(4),
                        //EndDate = new DateTime(2015, 5, 25, 11, 30, 0)
                        EndDate =  m_SelectDate.AddDays(-2).AddHours(6),
                    },
                    new Appointment {
                        AppointmentId = 6,
                        Text = "同工行政會議",
                        OwnerId = new int[] { 4 },
                        //StartDate = new DateTime(2015, 5, 25, 9, 30, 0),
                        StartDate = m_SelectDate.AddDays(-3).AddHours(4),
                        //EndDate = new DateTime(2015, 5, 25, 11, 30, 0)
                        EndDate =  m_SelectDate.AddDays(-3).AddHours(6),
                    },
                    new Appointment {
                        AppointmentId = 7,
                        Text = "飛牛牧場一日遊",
                        OwnerId = new int[] { 2 },
                        //StartDate = new DateTime(2015, 5, 25, 9, 30, 0),
                        StartDate = m_SelectDate.AddDays(-1).AddHours(5),
                        //EndDate = new DateTime(2015, 5, 25, 11, 30, 0)
                        EndDate =  m_SelectDate.AddDays(-1).AddHours(6),
                    },
                    new Appointment {
                        AppointmentId = 8,
                        Text = "領袖小組長會議",
                        OwnerId = new int[] { 3, 4 },
                        //StartDate = new DateTime(2015, 5, 25, 9, 30, 0),
                        StartDate = m_SelectDate.AddDays(-2).AddHours(3),
                        //EndDate = new DateTime(2015, 5, 25, 11, 30, 0)
                        EndDate =  m_SelectDate.AddDays(-2).AddHours(5),
                    },
                    new Appointment {
                        AppointmentId = 9,
                        Text = "白碧娥休假",
                        OwnerId = new int[] { 2 },
                        //StartDate = new DateTime(2015, 5, 25, 9, 30, 0),
                        StartDate = m_SelectDate.AddDays(-4).AddHours(2),
                        //EndDate = new DateTime(2015, 5, 25, 11, 30, 0)
                        EndDate =  m_SelectDate.AddDays(-4).AddHours(5),
                    },
                    new Appointment {
                        AppointmentId = 10,
                        Text = "蕭菀伶事假",
                        OwnerId = new int[] { 3 },
                        //StartDate = new DateTime(2015, 5, 25, 9, 30, 0),
                        StartDate = m_SelectDate.AddDays(1).AddHours(2),
                        //EndDate = new DateTime(2015, 5, 25, 11, 30, 0)
                        EndDate =  m_SelectDate.AddDays(1).AddHours(4),
                        AllDay = true
                    },
                    new Appointment {
                        AppointmentId = 11,
                        Text = "503教室-蕭菀伶",
                        OwnerId = new int[] { 1, 3 },
                        StartDate = m_SelectDate.AddDays(2).AddHours(2),
                        //EndDate = new DateTime(2015, 5, 25, 11, 30, 0)
                        EndDate =  m_SelectDate.AddDays(2).AddHours(4),
                    },
                    new Appointment {
                        AppointmentId = 12,
                        Text = "受洗約談預備會議",
                        OwnerId = new int[] { 4 },
                        StartDate = m_SelectDate.AddDays(3).AddHours(2),
                        //EndDate = new DateTime(2015, 5, 25, 11, 30, 0)
                        EndDate =  m_SelectDate.AddDays(3).AddHours(4),
                    },
                    new Appointment {
                        AppointmentId = 13,
                        Text = "台中牧師聯禱會",
                        OwnerId = new int[] { 3 },
                        StartDate = m_SelectDate.AddDays(1).AddHours(4),
                        //EndDate = new DateTime(2015, 5, 25, 11, 30, 0)
                        EndDate =  m_SelectDate.AddDays(1).AddHours(6),
                    },
                    new Appointment {
                        AppointmentId = 14,
                        Text = "晨禱特會見證",
                        OwnerId = new int[] { 4 },
                        StartDate = m_SelectDate.AddDays(2).AddHours(4),
                        //EndDate = new DateTime(2015, 5, 25, 11, 30, 0)
                        EndDate =  m_SelectDate.AddDays(2).AddHours(6),
                    },
                    new Appointment {
                        AppointmentId = 15,
                        Text = "張全興事假",
                        OwnerId = new int[] { 1 },
                        StartDate = m_SelectDate.AddDays(3).AddHours(4),
                        //EndDate = new DateTime(2015, 5, 25, 11, 30, 0)
                        EndDate =  m_SelectDate.AddDays(3).AddHours(6),
                    },
                    new Appointment {
                        AppointmentId = 16,
                        Text = "郭寬宏牧師出國",
                        OwnerId = new int[] { 2 },
                        StartDate = m_SelectDate.AddDays(1).AddHours(5),
                        //EndDate = new DateTime(2015, 5, 25, 11, 30, 0)
                        EndDate =  m_SelectDate.AddDays(1).AddHours(6),
                    },
                    new Appointment {
                        AppointmentId = 17,
                        Text = "領袖同工聚餐",
                        OwnerId = new int[] { 1, 2, 3, 4 },
                        StartDate = m_SelectDate.AddDays(-3).AddHours(4),
                        //EndDate = new DateTime(2015, 5, 25, 11, 30, 0)
                        EndDate =  m_SelectDate.AddDays(-3).AddHours(6),
                        RecurrenceRule = "FREQ=DAILY;BYDAY=MO,TU,WE,TH,FR;UNTIL=2020530"
                    }
                };

            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
    }
}


