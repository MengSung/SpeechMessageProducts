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
using ChurchReport.WebServiceConnector;
using ChurchReport.Models.CrmTransmitModule;
using ChurchReport.ViewModels;

namespace ChurchReport.Models
{
    public class FeeList
    {
        #region 成員資料

        public List<Fee> FeeDataList { get; set; }

        public String m_FullName = "";
        public String m_Account  = "";
        public String m_Password = "";
        //public DateTime m_SelectDate = new DateTime(2000, 1, 1);// 初始值 2000 表示還沒選
        public DateTime m_SelectDate = DateTime.Now;// 初始值 2000 表示還沒選
        public DateTime m_SundayDate;
        private bool m_FirstLoginFlag;

        private ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass("DYNAMICS365");

        FeeDownUpLoader m_FeeDownUpLoader = new FeeDownUpLoader();

        #endregion

        #region 初始化繳費與點名
        public void SetupFeeDataList(String Account, String Password)
        {
            //m_ActiveHappyGroupWeeklyReportList = m_DownloadHappyGroup.GetHappyGroupWeeklyReportList(Account, Password);
            FeeDataList = m_FeeDownUpLoader.GetHappyGroupList(Account, Password);

            if (m_ActiveHappyGroupListClass != null && m_ActiveHappyGroupListClass.HappyGroupWeeklyReportListClass != null && m_ActiveHappyGroupListClass.HappyGroupWeeklyReportListClass.Count > 0)
            {
                HappyType = "有幸福小組名單";
            }
            else
            {
                HappyType = "沒幸福小組名單";
            }
            //m_ActiveHappyGroupListClass.HappyGroupWeeklyReportListClass = new List<HappyGroupWeeklyReportListClass>();
            //m_ActiveHappyGroupListClass.HappyGroupWeeklyReportListClass.Add(m_ActiveHappyGroupWeeklyReportList);
        }
        #endregion


    }
}

