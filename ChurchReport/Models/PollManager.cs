using ChurchReport.WebServiceConnector;
using LineMessagingProcessor;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Xrm.Sdk;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ToolUtilityNameSpace;
// These namespaces are found in the Microsoft.Xrm.Sdk.dll assembly
// located in the SDK\bin folder of the SDK download.

namespace ChurchReport.Models
{
    public class PollManager : Controller
    {
        #region 資料區
        private ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass("DYNAMICS365");

        public PollModel m_PollModel { get; set; } = new PollModel();


        public Entity m_Contact;

        public String LoginType { get; set; } = "網頁登入";   //登入方式
        #endregion
        #region 電腦網頁登入
        public async Task<IActionResult> SavePoll(PollModel PollModel)
        {
            try
            {
                return Json(new { status = "1", message = "感謝回答問卷調查"});
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                //m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                //return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }
        #endregion
    }
}
