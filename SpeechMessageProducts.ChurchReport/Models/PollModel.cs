/*
 * 檔案整體說明
 *
 * 目的與定位：描述服事意願問卷中聚會接待、兒童、敬拜、社區、培育與代禱等選項及群組顯示旗標，供表單繫結；勾選只表示提交候選，不等於角色或權限已變更。
 *
 * 主要功能與流程：
 * 本檔宣告 PollModel。
 * 資料／選項欄位包含 PollModel、SundayTreat、DisplayGrowFlag、SaturdayChild、SundaydayChild、SundayNewFriend、DisplayPpt、
 * WorshipVocal、WorshipInstrument、Instrument、CommunityProfit、CommunityFlower、IncubateCampaign、DisplayDecipleFlag、
 * SundayPrayer、IncubateCampaignLeader，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 模型含巢狀集合或敏感欄位時，呼叫端須維持目前登入／操作的所有權；顯示文字不能取代正式資料庫ID。
 * 修改繫結欄位、預設值或複製路徑時，須同步核對序列化、表單、快照及上傳映射，避免局部更新污染其他讀者。
 * 可沿 SpeechMessageProducts.ChurchReport/Controllers/QrCodeController.cs、
 * SpeechMessageProducts.ChurchReport/Models/PollManager.cs、
 * SpeechMessageProducts.ChurchReport/Views/QrCode/PollQrCodeView.cshtml 追查目前工作樹中的合作／使用位置。
 */
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
        public bool SundaydayChild { get; set; }                //主日兒主同工
        public bool SundayNewFriend { get; set; }               //主日新人接待同工
        public bool DisplayPpt { get; set; }                    //主日控台同工，放印PPT
        public bool WorshipVocal { get; set; }                  //主日敬拜團(人聲)
        public bool WorshipInstrument { get; set; }             //主日敬拜團(樂器)
        public String Instrument { get; set; }                  //樂器名稱
        public bool CommunityProfit { get; set; }               //社區福音行動(益人學苑)
        public bool CommunityFlower { get; set; }               //社區福音行動(恩朵協會)
        public bool IncubateCampaign { get; set; }              //培育營會行政同工
        public bool DisplayDecipleFlag { get; set; }            //顯示門徒班旗標

        //C、小組長班結業者建議可參與服事項目《可複選》
        public bool SundayPrayer { get; set; }                  //主日禱告服事
        public bool IncubateCampaignLeader { get; set; }        //培育營會帶組同工
        public String Others { get; set; }                      //其他
        public bool DisplayLeaderFlag { get; set; }             //顯示小組長班旗標

        //意見回饋調查
        public String PollContent { get; set; }                 //意見回饋調查
    }
}
