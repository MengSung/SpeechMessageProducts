using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using ChurchReport.Models;

#region Dynamics 365 Microsoft.Xrm.Sdk.dll
// These namespaces are found in the Microsoft.Xrm.Sdk.dll assembly
// located in the SDK\bin folder of the SDK download.
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Client;
using Microsoft.Xrm.Sdk.Discovery;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Messages;
using ToolUtilityNameSpace;
using System.Text.RegularExpressions;
#endregion

namespace ChurchReport.WebServiceConnector
{
    public class HappyGroupUtility
    {
        #region 資料區
        #region 參數資料
        private ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass("DYNAMICS365");
        //private ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass("CRM2011");

        private static Regex DigitsOnly = new Regex(@"[^\d]");

        private Dictionary<String, String> m_FeedBackReport = new Dictionary<string, string>();

        bool m_SetIdentityFlag = false;
        #endregion

        #region 常數參數

        //private const String CRM_TYPE = "CRM2011";
        private const String CRM_TYPE = "DYNAMICS365";

        //private const int MONTH_PERIOD = 2;      //幾個月內出席超過這次數就會改變委身類型=>小組組員
        private const int WEEK_PERIOD = 8;      //過去幾　WEEK_PERIOD　周內出席超過這次數就會改變委身類型=>小組組員
        private const int MINIMUM_THRESHOLD = 4;      //2個月內出席超過這次數就會改變委身類型=>小組組員

        #region 除錯用參數
        private const int TOTAL_LEVEL = 1;//改變這個值，就會改追蹤的階層，值越小越不會追蹤，若是 TOTAL_LEVEL = 3 ，則大於 3 的 LEVEL，例如 : LEVEL_4、LEVEL_5 就不會被追蹤
        //private const int TOTAL_LEVEL = 5;//改變這個值，就會改追蹤的階層，值越大越會追蹤，若是 TOTAL_LEVEL = 3 ，則大於 3 的 LEVEL，例如 : LEVEL_4、LEVEL_5 就不會被追蹤
        private const int LEVEL_1 = 1; // 比較容易被看到的，可能是比較大範圍的部分
        private const int LEVEL_2 = 2;
        private const int LEVEL_3 = 3;
        private const int LEVEL_4 = 4;
        private const int LEVEL_5 = 5; // 比較不會被看到的，可能是比較細節的部分
                                       // 如果 TRACE_LEVEL >= TRACE_LEVEL_GROUND 就會進行追蹤
                                       // 如果 TRACE_LEVEL < TRACE_LEVEL_GROUND 就不會進行追蹤
                                       //int TRACE_LEVEL = 5;
                                       //int TRACE_LEVEL_GROUND = 3;
        #endregion

        #endregion
        #endregion

        #region 副程式呼叫
        private Entity FindLoginUser(ref Entity aContactEntity, String Account, String Password)
        {
            // 找登入使用者及其ID
            if (Account != "LineIdLogin")
            {
                return this.m_ToolUtilityClass.RetrieveContactEntityByAccountNumber(Account, Password);
            }
            else
            {
                // 用 LINE 登入
                return this.m_ToolUtilityClass.RetrieveContactEntityByLineUserId(Password);
            }
        }
        private void TransferIdentity(Entity aContact, int Counter, int NewComeMaxiNumber, int UnGroupMaxiNumber)
        {
            #region // 版本轉換
            //switch (Identity)
            //{
            //    case 100000000:
            //        return "8. 新朋友";
            //    case 100000001:
            //        return "5. 神學生";
            //    case 100000002:
            //        return "4. 小組長";
            //    case 100000003:
            //        return "3. 全職同工";
            //    case 100000004:
            //        return "7. 未入組";
            //    case 100000005:
            //        return "1. 牧師";
            //    case 100000006:
            //        return "2, 師母";
            //    case 100000007:
            //        return "9. 外教會";
            //    case 100000008:
            //        return "10. 未入組結案";
            //    case 1:
            //        return "6. 小組組員";
            //    default:
            //        return ".";
            //}
            #endregion

            // 確認是否是新人或是未入組
            int aIdentityNumber = this.m_ToolUtilityClass.GetOptionSetAttribute(aContact, "customertypecode");

            // 因為新朋友、未入組會變更委身類型，旗標防止設定太多次，false表示尚未設定
            if (aIdentityNumber == 100000000)
            {
                //m_SetIdentityFlag = false; // 因為新朋友、未入組會變更委身類型，旗標防止設定太多次，false表示尚未設定
                // 新朋友
                if (Counter >= NewComeMaxiNumber && m_SetIdentityFlag == false)
                {
                    // 只要設定一次就好
                    m_SetIdentityFlag = true;

                    // 新朋友變為未入組
                    this.m_ToolUtilityClass.SetOptionSetAttribute(ref aContact, "customertypecode", 100000004);
                    if (CRM_TYPE == "DYNAMICS365")
                    {
                        this.m_ToolUtilityClass.UpdateEntityDynamics365(ref this.m_ToolUtilityClass.m_OrganizationService, ref aContact);
                    }
                    else
                    {
                        this.m_ToolUtilityClass.UpdateEntityCrm2011(ref this.m_ToolUtilityClass.m_Crm2011OrganizationService, ref aContact);
                    }
                }
                else { }
            }
            else if (aIdentityNumber == 100000004)
            {
                //未入組
                if (Counter >= UnGroupMaxiNumber && m_SetIdentityFlag == false)
                {
                    // 只要設定一次就好
                    m_SetIdentityFlag = true;

                    // 未入組變為未入組結案(超過或是等於)
                    this.m_ToolUtilityClass.SetOptionSetAttribute(ref aContact, "customertypecode", 100000008);

                    if (CRM_TYPE == "DYNAMICS365")
                    {
                        this.m_ToolUtilityClass.UpdateEntityDynamics365(ref this.m_ToolUtilityClass.m_OrganizationService, ref aContact);
                    }
                    else
                    {
                        this.m_ToolUtilityClass.UpdateEntityCrm2011(ref this.m_ToolUtilityClass.m_Crm2011OrganizationService, ref aContact);
                    }

                }
                else { }
            }
            else
            {

            }

        }
        #endregion

    }
}
