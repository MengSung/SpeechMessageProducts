using ChurchReport.Tools;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Collections.Generic;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ChurchReport.Models
{
    public static class ContextDictionary 
    {
        public static Dictionary<String, InMemoryDataContextSmallGroup> StaticContextDictionary = new Dictionary<String, InMemoryDataContextSmallGroup>();

        public static InMemoryDataContextSmallGroup GetInMemoryDataContextSmallGroup(IHttpContextAccessor httpContextAccessor, IMemoryCache memoryCache, IPayment aPaymentService)
        {
            try
            {
                var session = httpContextAccessor.HttpContext.Session;
                var Key = session.Id ;

                if (StaticContextDictionary.ContainsKey(Key))
                {
                    // 關鍵( Key ) 已經在字典裡了
                    return StaticContextDictionary[Key];
                }
                else
                {
                    // 關鍵( Key )還沒有在字典裡
                    InMemoryDataContextSmallGroup aInMemoryDataContextSmallGroup = new InMemoryDataContextSmallGroup(httpContextAccessor, memoryCache, aPaymentService);

                    // 一個人會開多個幸福小組
                    //foreach (HappyGroupWeeklyReportListClass aHappyGroupWeeklyReportListClass in aInMemoryDataContextSmallGroup.HappyGroupDataManager.m_ActiveHappyGroupListClass.HappyGroupWeeklyReportListClass)
                    //{
                    //    // aHappyGroupWeeklyReportListClass = 幸福小組名單
                    //    aHappyGroupWeeklyReportListClass.SpiritLeaderList = "";
                    //    //aInMemoryDataContextSmallGroup.HappyGroupDataManager.m_ActiveHappyGroupWeeklyReportList.SpiritLeaderList = "";
                    //}

                    //for (int i = 0 ; i < aInMemoryDataContextSmallGroup.HappyGroupDataManager.m_ActiveHappyGroupListClass.HappyGroupWeeklyReportListClass.Count; i ++ )
                    //{
                    //    aInMemoryDataContextSmallGroup.HappyGroupDataManager.m_ActiveHappyGroupListClass.HappyGroupWeeklyReportListClass[i].SpiritLeaderList = "";
                    //}

                    StaticContextDictionary.Add( Key, aInMemoryDataContextSmallGroup );

                    return StaticContextDictionary[Key]; 
                }
            }
            catch (System.Exception e)
            {
                throw e;
            }
        }

    }
}
