// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Models/PollModel.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於資料模型或 ViewModel 層，註解重點在說明欄位語意、序列化/繫結用途與相容性限制。
// 主要型別：class PollModel
// 主要成員：SundayTreat、DisplayGrowFlag、SaturdayChild、SundaydayChild、SundayNewFriend、DisplayPpt、WorshipVocal、WorshipInstrument、Instrument、CommunityProfit
// 引用命名空間：System、System.Collections.Generic、System.Linq、System.Threading.Tasks
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
