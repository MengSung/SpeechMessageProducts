// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Models/InMemoryAppointmentsDataContext.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於資料模型或 ViewModel 層，註解重點在說明欄位語意、序列化/繫結用途與相容性限制。
// 主要型別：class InMemoryAppointmentsDataContext
// 主要成員：SaveChanges、Appointments
// 引用命名空間：Microsoft.AspNetCore.Http、Microsoft.Extensions.Caching.Memory、System、System.Collections.Generic、System.Linq、System.Threading.Tasks
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
