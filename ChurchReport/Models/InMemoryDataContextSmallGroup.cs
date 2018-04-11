using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Collections.Generic;
using ToolUtilityNameSpace;

namespace ChurchReport.Models
{
    public class InMemoryDataContextSmallGroup
    {
        IHttpContextAccessor _contextAccessor;
        IMemoryCache _memoryCache;

        private ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass("DYNAMICS365");

        public SmallGroupDataList m_SmallGroupDataList = new SmallGroupDataList();
        public WeeklyReportData m_WeeklyReportData = new WeeklyReportData();

        public InMemoryDataContextSmallGroup(IHttpContextAccessor contextAccessor, IMemoryCache memoryCache)
        {
            _contextAccessor = contextAccessor;
            _memoryCache = memoryCache;
        }

        #region 小組長處理區


        public void SetupSmallGroupData(String FullName, String Account, String Password, DateTime aSelectDate, bool DisplayDateFlag)
        {
            try
            {
                String ContactIdString = m_ToolUtilityClass.RetrieveContactByAccountNumber(Account, Password);

                m_SmallGroupDataList.SetupContactIdString(ContactIdString);

                m_SmallGroupDataList.SetupSmallGroupData(FullName, Account, Password, DateTime.Now, true);

            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }

        }

        public void SetCacheData()
        {
            var session = _contextAccessor.HttpContext.Session;
            var key = session.Id + "_SmallGroupDataList";

            if (_memoryCache.Get(key) == null)
            {
                //m_SmallGroupDataList.m_FullName = "嘟嘟妞妞";

                _memoryCache.Set<SmallGroupDataList>( key, m_SmallGroupDataList, new MemoryCacheEntryOptions
                {
                    SlidingExpiration = TimeSpan.FromMinutes(10)
                });
                session.SetInt32("dirty", 1);
            }
        }

        public SmallGroupDataList GetCacheData()
        {
            var session = _contextAccessor.HttpContext.Session;
            var key = session.Id + "_SmallGroupDataList";

            SmallGroupDataList aSmallGroupDataList = new SmallGroupDataList();
            if (!_memoryCache.TryGetValue(key, out aSmallGroupDataList))
            {
                //Time = "Cache is expired or not available";
            }

            return aSmallGroupDataList;
        }

        public SmallGroupDataList SmallGroupDataList
        {
            get
            {
                var session = _contextAccessor.HttpContext.Session;
                var key = session.Id + "_SmallGroupDataList";

                if (_memoryCache.Get(key) == null)
                {
                    _memoryCache.Set<SmallGroupDataList>(key, m_SmallGroupDataList, new MemoryCacheEntryOptions
                    {
                        SlidingExpiration = TimeSpan.FromMinutes(10)
                    });
                    session.SetInt32("dirty", 1);
                    //_memoryCache.Set<ICollection<DiscipleLessons>>(key, m_ClassSheetManager.m_ReportDiscipleLessonsList, options: new MemoryCacheEntryOptions
                    //{
                    //    SlidingExpiration = TimeSpan.FromMinutes(10),
                    //});
                    //session.SetInt32("dirty", 1);
                }

                return _memoryCache.Get< SmallGroupDataList > (key);
            }
        }
        #endregion


        #region 週報處理區

        public void SetupWeeklyReport(String Account, String Password, DateTime SundayDate)
        {
            m_WeeklyReportData.SetupWeeklyReport(Account, Password, SundayDate);

        }

        public WeeklyReportData WeeklyReportData
        {
            get
            {
                var session = _contextAccessor.HttpContext.Session;
                var key = session.Id + "_WeeklyReportData";

                if (_memoryCache.Get(key) == null)
                {
                    _memoryCache.Set<WeeklyReportData>(key, m_WeeklyReportData, new MemoryCacheEntryOptions
                    {
                        SlidingExpiration = TimeSpan.FromMinutes(10)
                    });
                    session.SetInt32("dirty", 1);
                }

                return _memoryCache.Get<WeeklyReportData>(key);
            }
        }

        #endregion
        public void SaveChanges()
        {
            //foreach (var employee in DiscipleLessons.Where(a => a.DiscipleLessonsId == 0))
            //{
            //    employee.ID = DiscipleLessons.Max(a => a.ID) + 1;
            //}
        }
    }
}
