using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;

namespace ChurchReport.Models
{
    public static class ContextDictionary
    {
        public static Dictionary<String, InMemoryDataContextSmallGroup> StaticContextDictionary = new Dictionary<String, InMemoryDataContextSmallGroup>();

        public static InMemoryDataContextSmallGroup GetInMemoryDataContextSmallGroup(IHttpContextAccessor httpContextAccessor, IMemoryCache memoryCache)
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
                    InMemoryDataContextSmallGroup aInMemoryDataContextSmallGroup = new InMemoryDataContextSmallGroup(httpContextAccessor, memoryCache);

                    aInMemoryDataContextSmallGroup.HappyGroupDataManager.m_ActiveHappyGroupWeeklyReportList.SpiritLeaderList = "";
                    //aInMemoryDataContextSmallGroup.HappyGroupDataManager.m_ActiveHappyGroupWeeklyReportList.HappyGroupName = "";

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
