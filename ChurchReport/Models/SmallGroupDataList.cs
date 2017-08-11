using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ToolUtilityNameSpace;


// These namespaces are found in the Microsoft.Xrm.Sdk.dll assembly
// located in the SDK\bin folder of the SDK download.
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Client;
using Microsoft.Xrm.Sdk.Discovery;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Messages;

namespace ChurchReport.Models
{
    static public class SmallGroupDataList
    {
        static ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass("DYNAMICS365");

        static public SmallGroupData m_SmallGroupData = new SmallGroupData();

        public static void SetupContactIdString(String ContactIdString)
        {
            m_SmallGroupData.SmallGroupLeaderContactId = ContactIdString;
        }
        public static void SetupSmallGroupDate(String ContactIdString)
        {
            Guid aContactGuid = new Guid(ContactIdString);

            String FullName = m_ToolUtilityClass.RetrieveEntity("contact", aContactGuid).Attributes["fullname"].ToString();

            m_SmallGroupData.SmallGroupLeaderFullName = FullName;
            m_SmallGroupData.SundayPrayers = DateTime.Parse("2017/07/30");

            SmallGroupDataList.m_SmallGroupData.members = new List<Member>()
            {
                #region 加入新成員
            new Member {
                          Id =1,
                          FullName = "林國仁",
                          Status = "族系族長",
                          SmallGroupName = "國仁哥小組",
                          SectionName = "國仁哥族系",
                          PrayItem = "請為黎巴嫩行程代禱",
                          Sunday = true,
                          SmallGroup = true,
                          StateID1 = 2,
                          Number1 = 4,
                          StateID2 = 1,
                          Number2 = 2,
                          //Picture = "../../images/employees/01.png" //D:\暫存區\ASP.NET CORE 練習區\DevExtremeAspNetCoreApp1\DevExtremeAspNetCoreApp1\wwwroot\images\employees
                          Picture = "../../images/employees/01.png" //D:\暫存區\ASP.NET CORE 練習區\DevExtremeAspNetCoreApp1\DevExtremeAspNetCoreApp1\wwwroot\images\employees
                          //Picture = "https://tpehoc.speechmessage.com.tw/image/download.aspx?attribute=entityimage&entity=contact&id=66cd8034-953f-e711-80d9-00155d00640b" //D:\暫存區\ASP.NET CORE 練習區\DevExtremeAspNetCoreApp1\DevExtremeAspNetCoreApp1\wwwroot\images\employees
                        },
                        new Member
                        {
                            Id = 2,
                            FullName = "林萬全",
                            Status = "組員",
                            SmallGroupName = "國仁哥小組",
                            SectionName = "國仁哥族系",
                            PrayItem = "工作順利、敬拜到達神寶座前",
                            Sunday = true,
                            SmallGroup = false,
                            StateID1 = 5,
                            Number1 = 2,
                            StateID2 =3,
                            Number2 =1,
                            Picture = "../../images/employees/02.png"
                        },
                        new Member
                        {
                            Id = 3,
                            FullName = "陳永初",
                            Status = "組員",
                            SmallGroupName = "國仁哥小組",
                            SectionName = "國仁哥族系",
                            PrayItem = "有智慧處理組織問題",
                            Sunday = true,
                            SmallGroup = true,
                            StateID1 = 4,
                            Number1 = 2,
                            StateID2 = 1,
                            Number2 = 1,
                            Picture = "../../images/employees/03.png"
                        },
                        new Member
                        {
                            Id = 4,
                            FullName = "喬仁睿",
                            Status = "組員",
                            SmallGroupName = "國仁哥小組",
                            SectionName = "國仁哥族系",
                            PrayItem = "工作順利禱告",
                            Sunday = false,
                            SmallGroup = true,
                            StateID1 = 5,
                            Number1 = 1,
                            StateID2 = 2,
                            Number2 = 2,
                            Picture = "../../images/employees/04.png"
                        },
                        new Member
                        {
                            Id = 5,
                            FullName = "胡夢嵩",
                            Status = "組員",
                            SmallGroupName = "夢嵩小組",
                            SectionName = "國仁哥族系",
                            PrayItem = "開發系統順利",
                            Sunday = true,
                            SmallGroup = true,
                            StateID1 = 1,
                            Number1 = 3,
                            StateID2 = 5,
                            Number2 = 1,
                            Picture = "../../images/employees/05.png"
                        },
                        new Member
                        {
                            Id = 6,
                            FullName = "陳銘浚",
                            Status = "組員",
                            SmallGroupName = "夢嵩小組",
                            SectionName = "國仁哥族系",
                            PrayItem = "超自然醫治耳鳴",
                            Sunday = true,
                            SmallGroup = true,
                            StateID1 = 2,
                            Number1 = 3,
                            StateID2 = 4,
                            Number2 = 3,
                            Picture = "../../images/employees/06.png"
                        },
                        new Member
                        {
                            Id = 7,
                            FullName = "溫浩雋",
                            Status = "組員",
                            SmallGroupName = "夢嵩小組",
                            SectionName = "國仁哥族系",
                            PrayItem = "請位失眠禱告",
                            Sunday = false,
                            SmallGroup = true,
                            StateID1 = 4,
                            Number1 =1,
                            StateID2 = 3,
                            Number2 = 2,
                            Picture = "../../images/employees/07.png"
                        },
                        new Member
                        {
                            Id = 8,
                            FullName = "熊國平",
                            Status = "組員",
                            SmallGroupName = "夢嵩小組",
                            SectionName = "國仁哥族系",
                            PrayItem = "請代禱癌細胞消失無蹤",
                            Sunday = true,
                            SmallGroup = true,
                            StateID1 = 2,
                            Number1 = 1,
                            StateID2 = 5,
                            Number2 = 3,
                            Picture = "../../images/employees/08.png"
                        },
                        new Member
                        {
                            Id = 9,
                            FullName = "莊順昌",
                            Status = "組員",
                            SmallGroupName = "夢嵩小組",
                            SectionName = "國仁哥族系",
                            PrayItem = "與妻子關係和諧禱告",
                            Sunday = true,
                            SmallGroup = false,
                            StateID1 = 3,
                            Number1 = 5,
                            StateID2 =2,
                            Number2 = 6,
                            Picture = "../../images/employees/09.png"
                        },
                        new Member
                        {
                            Id = 10,
                            FullName = "張剛爵",
                            Status = "組員",
                            SmallGroupName = "夢嵩小組",
                            SectionName = "國仁哥族系",
                            PrayItem = "ISO過關",
                            Sunday = false,
                            SmallGroup = false,
                            StateID1 = 1,
                            Number1 =3,
                            StateID2 = 5,
                            Number2 = 1,
                            Picture = "../../images/employees/05.png"
                        },
                        #endregion
            };

        }
        public static void SetupSmallGroupDate(String ContactIdString, String SundayDate)
        {
            Guid aContactGuid = new Guid(ContactIdString);

            String FullName = m_ToolUtilityClass.RetrieveEntity("contact", aContactGuid).Attributes["fullname"].ToString();

            m_SmallGroupData.SmallGroupLeaderFullName = FullName;
            m_SmallGroupData.SundayPrayers = DateTime.Parse(SundayDate);

            SmallGroupDataList.m_SmallGroupData.members = new List<Member>()
            {
                #region 加入新成員
            new Member {
                          Id =1,
                          FullName = "胡逸凡",
                          Status = "族系族長",
                          SmallGroupName = "國仁哥小組",
                          SectionName = "國仁哥族系",
                          PrayItem = "請為黎巴嫩行程代禱，努力禱告",
                          Sunday = true,
                          SmallGroup = true,
                          StateID1 = 2,
                          Number1 = 4,
                          StateID2 = 1,
                          Number2 = 2,
                          //Picture = "../../images/employees/01.png" //D:\暫存區\ASP.NET CORE 練習區\DevExtremeAspNetCoreApp1\DevExtremeAspNetCoreApp1\wwwroot\images\employees
                          Picture = "../../images/employees/01.png" //D:\暫存區\ASP.NET CORE 練習區\DevExtremeAspNetCoreApp1\DevExtremeAspNetCoreApp1\wwwroot\images\employees
                          //Picture = "https://tpehoc.speechmessage.com.tw/image/download.aspx?attribute=entityimage&entity=contact&id=66cd8034-953f-e711-80d9-00155d00640b" //D:\暫存區\ASP.NET CORE 練習區\DevExtremeAspNetCoreApp1\DevExtremeAspNetCoreApp1\wwwroot\images\employees
                        },
                        new Member
                        {
                            Id = 2,
                            FullName = "胡逸祥",
                            Status = "組員",
                            SmallGroupName = "國仁哥小組",
                            SectionName = "國仁哥族系",
                            PrayItem = "工作順利、敬拜到達神寶座前，非常順利!",
                            Sunday = true,
                            SmallGroup = true,
                            StateID1 = 5,
                            Number1 = 2,
                            StateID2 =3,
                            Number2 =1,
                            Picture = "../../images/employees/02.png"
                        },
                        new Member
                        {
                            Id = 3,
                            FullName = "陳永初",
                            Status = "組員",
                            SmallGroupName = "國仁哥小組",
                            SectionName = "國仁哥族系",
                            PrayItem = "有智慧處理組織問題",
                            Sunday = false,
                            SmallGroup = false,
                            StateID1 = 4,
                            Number1 = 2,
                            StateID2 = 1,
                            Number2 = 1,
                            Picture = "../../images/employees/03.png"
                        },
                        new Member
                        {
                            Id = 4,
                            FullName = "喬仁睿",
                            Status = "組員",
                            SmallGroupName = "國仁哥小組",
                            SectionName = "國仁哥族系",
                            PrayItem = "工作順利禱告",
                            Sunday = false,
                            SmallGroup = true,
                            StateID1 = 5,
                            Number1 = 1,
                            StateID2 = 2,
                            Number2 = 2,
                            Picture = "../../images/employees/04.png"
                        },
                        new Member
                        {
                            Id = 5,
                            FullName = "胡夢嵩",
                            Status = "組員",
                            SmallGroupName = "夢嵩小組",
                            SectionName = "國仁哥族系",
                            PrayItem = "開發系統順利",
                            Sunday = true,
                            SmallGroup = true,
                            StateID1 = 1,
                            Number1 = 3,
                            StateID2 = 5,
                            Number2 = 1,
                            Picture = "../../images/employees/05.png"
                        },
                        new Member
                        {
                            Id = 6,
                            FullName = "陳銘浚",
                            Status = "組員",
                            SmallGroupName = "夢嵩小組",
                            SectionName = "國仁哥族系",
                            PrayItem = "超自然醫治耳鳴",
                            Sunday = true,
                            SmallGroup = true,
                            StateID1 = 2,
                            Number1 = 3,
                            StateID2 = 4,
                            Number2 = 3,
                            Picture = "../../images/employees/06.png"
                        },
                        new Member
                        {
                            Id = 7,
                            FullName = "溫浩雋",
                            Status = "組員",
                            SmallGroupName = "夢嵩小組",
                            SectionName = "國仁哥族系",
                            PrayItem = "請位失眠禱告",
                            Sunday = false,
                            SmallGroup = true,
                            StateID1 = 4,
                            Number1 =1,
                            StateID2 = 3,
                            Number2 = 2,
                            Picture = "../../images/employees/07.png"
                        },
                        new Member
                        {
                            Id = 8,
                            FullName = "熊國平",
                            Status = "組員",
                            SmallGroupName = "夢嵩小組",
                            SectionName = "國仁哥族系",
                            PrayItem = "請代禱癌細胞消失無蹤",
                            Sunday = true,
                            SmallGroup = true,
                            StateID1 = 2,
                            Number1 = 1,
                            StateID2 = 5,
                            Number2 = 3,
                            Picture = "../../images/employees/08.png"
                        },
                        new Member
                        {
                            Id = 9,
                            FullName = "莊順昌",
                            Status = "組員",
                            SmallGroupName = "夢嵩小組",
                            SectionName = "國仁哥族系",
                            PrayItem = "與妻子關係和諧禱告",
                            Sunday = true,
                            SmallGroup = false,
                            StateID1 = 3,
                            Number1 = 5,
                            StateID2 =2,
                            Number2 = 6,
                            Picture = "../../images/employees/09.png"
                        },
                        new Member
                        {
                            Id = 10,
                            FullName = "張剛爵",
                            Status = "組員",
                            SmallGroupName = "夢嵩小組",
                            SectionName = "國仁哥族系",
                            PrayItem = "ISO過關",
                            Sunday = false,
                            SmallGroup = false,
                            StateID1 = 1,
                            Number1 =3,
                            StateID2 = 5,
                            Number2 = 1,
                            Picture = "../../images/employees/05.png"
                        },
                        #endregion
            };

        }
    }
}
