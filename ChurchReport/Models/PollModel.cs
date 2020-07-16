using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ChurchReport.Models
{
    public class PollModel
    {
        public PollModel()
        { }


        //A、	成⾧班結業者建議可參與之服事項目
        public bool SundayTreat { get; set; }                   //主日招待同工
        public bool DisplayGrowFlag { get; set; }               //顯示成⾧班旗標

        //B、門徒班結業者建議可參與之服事項目《可複選》
        public bool SaturdayChild { get; set; }                 //週六兒主服事同工
        public bool SundaydayChild { get; set; }                //主日兒主幼顧同工
        public bool SundayNewFriend { get; set; }               //主日新人接待同工
        public bool DisplayPpt { get; set; }                    //主日控台同工，放印PPT
        public bool WorshipVocal { get; set; }                  //主日敬拜團(人聲)
        public bool WorshipInstrument { get; set; }             //主日敬拜團(樂器)
        public String Instrument { get; set; }                  //樂器名稱
        public bool CommunityProfit { get; set; }               //社區福音行動(益人學苑)
        public bool CommunityFlower { get; set; }               //社區福音行動(恩朵協會)
        public bool IncubateCampaign { get; set; }              //培育營會行政同工
        public bool DisplayDecipleFlag { get; set; }            //顯示門徒班旗標

        //C、領袖班結業者建議可參與服事項目《可複選》
        public bool SundayPrayer { get; set; }                  //主日禱告服事
        public bool IncubateCampaignLeader { get; set; }        //培育營會帶組同工
        public String Others { get; set; }                      //其他
        public bool DisplayLeaderFlag { get; set; }             //顯示領袖班旗標

        //意見回饋調查
        public String PollContent { get; set; }                 //意見回饋調查
    }
}
