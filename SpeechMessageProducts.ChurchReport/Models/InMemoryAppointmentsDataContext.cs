/*
 * 檔案整體說明
 *
 * 目的與定位：提供行事曆資料來源的記憶體容器與SaveChanges介面，將既有約會管理器取得的清單交給排程端點；此Context含可變集合，建立及重用必須遵守目前登入範圍。
 *
 * 主要功能與流程：
 * 本檔宣告 InMemoryAppointmentsDataContext。
 * 主要操作入口包含 SaveChanges。
 *
 * 使用邊界與維護：
 * 模型含巢狀集合或敏感欄位時，呼叫端須維持目前登入／操作的所有權；顯示文字不能取代正式資料庫ID。
 * 修改繫結欄位、預設值或複製路徑時，須同步核對序列化、表單、快照及上傳映射，避免局部更新污染其他讀者。
 * 可沿 SpeechMessageProducts.ChurchReport/Controllers/ApiControllers/SchedulerDataController.cs 追查目前工作樹中的合作／使用位置。
 */
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ChurchReport.Models
{
    public class InMemoryAppointmentsDataContext
    {
        IHttpContextAccessor _contextAccessor;
        IMemoryCache _memoryCache;

        public InMemoryAppointmentsDataContext(IHttpContextAccessor contextAccessor, IMemoryCache memoryCache)
        {
            _contextAccessor = contextAccessor;
            _memoryCache = memoryCache;
        }

        public ICollection<Appointment> Appointments
        {
            get
            {
                AppointmentsListManager aAppointmentsList = new AppointmentsListManager();

                var session = _contextAccessor.HttpContext.Session;
                var key = session.Id + "_Appointments";


                if (_memoryCache.Get(key) == null)
                {
                    _memoryCache.Set<ICollection<Appointment>>(key, aAppointmentsList.m_Appointments, new MemoryCacheEntryOptions
                    {
                        SlidingExpiration = TimeSpan.FromMinutes(10)
                    });
                    session.SetInt32("dirty", 1);
                }

                return _memoryCache.Get<ICollection<Appointment>>(key);
            }
        }

        public void SaveChanges( String Key)
        {
            foreach (var appointment in Appointments.Where(a => a.AppointmentId == Key))
            {
                appointment.AppointmentId = Appointments.Max(a => a.AppointmentId) + 1;
            }
        }
    }
}
