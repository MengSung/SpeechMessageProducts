using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ChurchReport.Models
{
    static class SmallGroupData
    {
        public static bool UpdateFlag = false; // 剛開始時先設定為尚未更新資料

        private static List<Member> members = new List<Member>()
        {
            new Member {
                Id =1,
                FullName = "林國仁",
                SmallGroupName = "國仁哥小組",
                SectionName = "國仁哥族系",
                PrayItem = "請為黎巴嫩行程代禱",
                Sunday = true,
                SmallGroup = true,
                StateID = 2,
                Number = 4,
                //Picture = "../../images/employees/01.png" //D:\暫存區\ASP.NET CORE 練習區\DevExtremeAspNetCoreApp1\DevExtremeAspNetCoreApp1\wwwroot\images\employees
                Picture = "../../images/employees/01.png" //D:\暫存區\ASP.NET CORE 練習區\DevExtremeAspNetCoreApp1\DevExtremeAspNetCoreApp1\wwwroot\images\employees
                //Picture = "https://tpehoc.speechmessage.com.tw/image/download.aspx?attribute=entityimage&entity=contact&id=66cd8034-953f-e711-80d9-00155d00640b" //D:\暫存區\ASP.NET CORE 練習區\DevExtremeAspNetCoreApp1\DevExtremeAspNetCoreApp1\wwwroot\images\employees
            },
            new Member {
                Id =2,
                FullName = "林萬全",
                SmallGroupName = "國仁哥小組",
                SectionName = "國仁哥族系",
                PrayItem = "工作順利、敬拜到達神寶座前",
                Sunday = true,
                SmallGroup = false,
                StateID = 1,
                Number = 3,
                Picture = "../../images/employees/02.png"
            },
            new Member {
                Id =3,
                FullName = "陳永初",
                SmallGroupName = "國仁哥小組",
                SectionName = "國仁哥族系",
                PrayItem = "有智慧處理組織問題",
                Sunday = true,
                SmallGroup = true,
                StateID = 3,
                Number = 2,
                Picture = "../../images/employees/03.png"
            },
            new Member {
                Id =4,
                FullName = "喬仁睿",
                SmallGroupName = "國仁哥小組",
                SectionName = "國仁哥族系",
                PrayItem = "工作順利禱告",
                Sunday = false,
                SmallGroup = true,
                StateID = 4,
                Number = 3,
                Picture = "../../images/employees/04.png"
            },
            new Member {
                Id =5,
                FullName = "胡夢嵩",
                SmallGroupName = "夢嵩小組",
                SectionName = "國仁哥族系",
                PrayItem = "開發系統順利",
                Sunday = true,
                SmallGroup = true,
                StateID = 5,
                Number = 4,
                Picture = "../../images/employees/05.png"
            },
            new Member {
                Id =6,
                FullName = "陳銘浚",
                SmallGroupName = "夢嵩小組",
                SectionName = "國仁哥族系",
                PrayItem = "超自然醫治耳鳴",
                Sunday = true,
                SmallGroup = true,
                StateID = 1,
                Number = 1,
                Picture = "../../images/employees/06.png"
            },
            new Member {
                Id =7,
                FullName = "溫浩雋",
                SmallGroupName = "夢嵩小組",
                SectionName = "國仁哥族系",
                PrayItem = "請位失眠禱告",
                Sunday = false,
                SmallGroup = true,
                StateID = 3,
                Number = 2,
                Picture = "../../images/employees/07.png"
            },
            new Member {
                Id =8,
                FullName = "熊國平",
                SmallGroupName = "夢嵩小組",
                SectionName = "國仁哥族系",
                PrayItem = "請代禱癌細胞消失無蹤",
                Sunday = true,
                SmallGroup = true,
                StateID = 4,
                Number = 3,
                Picture = "../../images/employees/08.png"
            },
            new Member {
                Id =9,
                FullName = "莊順昌",
                SmallGroupName = "夢嵩小組",
                SectionName = "國仁哥族系",
                PrayItem = "與妻子關係和諧禱告",
                Sunday = true,
                SmallGroup = false,
                StateID = 5,
                Number = 2,
                Picture = "../../images/employees/09.png"
            },
            new Member {
                Id =10,
                FullName = "張剛爵",
                SmallGroupName = "夢嵩小組",
                SectionName = "國仁哥族系",
                PrayItem = "ISO過關",
                Sunday = false,
                SmallGroup = false,
                StateID = 2,
                Number = 1,
                Picture = "../../images/employees/05.png"
            },
        };
        public static List<Member> Members { get => members; set => members = value; }

        public static void LoadSmallGroupData(DateTime Sunday)
        {
            SmallGroupData.Members.Clear();

            SmallGroupData.Members.Add(
                new Member
                {
                    Id = 1,
                    FullName = "胡逸凡",
                    SmallGroupName = "國仁哥小組",
                    SectionName = "國仁哥族系",
                    PrayItem = "請為綠島行程代禱",
                    Sunday = true,
                    SmallGroup = true,
                    StateID = 2,
                    Number = 4,
                    //Picture = "../../images/employees/01.png" //D:\暫存區\ASP.NET CORE 練習區\DevExtremeAspNetCoreApp1\DevExtremeAspNetCoreApp1\wwwroot\images\employees
                    Picture = "../../images/employees/01.png" //D:\暫存區\ASP.NET CORE 練習區\DevExtremeAspNetCoreApp1\DevExtremeAspNetCoreApp1\wwwroot\images\employees
                    //Picture = "https://tpehoc.speechmessage.com.tw/image/download.aspx?attribute=entityimage&entity=contact&id=66cd8034-953f-e711-80d9-00155d00640b" //D:\暫存區\ASP.NET CORE 練習區\DevExtremeAspNetCoreApp1\DevExtremeAspNetCoreApp1\wwwroot\images\employees
                }
            );

        }
    }
}
