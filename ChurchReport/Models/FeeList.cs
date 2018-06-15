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
using Newtonsoft.Json;

namespace ChurchReport.Models
{
    public class FeeList
    {
        #region 成員資料

        public List<Fee> FeeDataList { get; set; }

        public String FeeType { get; set; }
        public String Result { get; set; }

        public String m_FullName = "";
        public String m_Account  = "";
        public String m_Password = "";

        public String SmallGroupLeaderContactId { get; set; }

        private ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass("DYNAMICS365");

        FeeDownUpLoader m_FeeDownUpLoader = new FeeDownUpLoader();

        #endregion

        #region 初始化繳費與點名
        public void SetupLoginUserInfo( String FullName , String Account, String Password)
        {
             m_FullName = FullName;
             m_Account = Account;
             m_Password = Password;
        }

        public void SetupFeeDataList(String Account, String Password)
        {
            m_Account = Account;
            m_Password = Password;

            String LocalResult = "";

            FeeDataList = m_FeeDownUpLoader.GetFeeList(Account, Password, ref LocalResult);
            if(FeeDataList.Count > 0 )
            {
                FeeType = "有繳費點名";
            }
            else
            {
                FeeType = "無繳費點名";
            }
            Result = LocalResult;
        }
        public void SetupFeeDataList()
        {
            String LocalResult = "";

            FeeDataList = m_FeeDownUpLoader.GetFeeList( m_Account, m_Password, ref LocalResult);
            if (FeeDataList.Count > 0)
            {
                FeeType = "有繳費點名";
            }
            else
            {
                FeeType = "無繳費點名";
            }

            Result = LocalResult;

        }

        #endregion

        public void PopulateObjectAndUpdateEntity(string Values, Fee aFee)
        {
            var settings = new JsonSerializerSettings
            {
                // 轉換成當地時間
                DateTimeZoneHandling = DateTimeZoneHandling.Local,

                NullValueHandling = NullValueHandling.Ignore,
                MissingMemberHandling = MissingMemberHandling.Ignore
            };

            //DiscipleLessons aBestRecord = JsonConvert.DeserializeObject<DiscipleLessons>(ProcessNullValue(Values), settings);

            JsonConvert.PopulateObject(Values, aFee, settings);

            Dictionary<string, string> aDictionary = JsonConvert.DeserializeObject<Dictionary<string, string>>(Values);

            List<string> KeyList = new List<string>(aDictionary.Keys);
            List<string> ValueList = new List<string>(aDictionary.Values);

            if ( KeyList.Count > 0 )
            {
                String Key = KeyList[0];

                bool CreateFlag = false; 
                m_FeeDownUpLoader.UpdateFeeDataList(KeyList[0], ValueList[0], aFee.StorLessonsId, ref CreateFlag);

                //String DATE = DateTime.Now.ToLocalTime().ToLongTimeString();

                if (Key == "Amount" && CreateFlag == true)
                {
                    String PayDateValue = "{\"PayDate\":\"" + DateTime.Now.ToUniversalTime().ToString("u") + "\"}";
                    JsonConvert.PopulateObject(PayDateValue, aFee, settings);

                    String PayWayValue = "{\"PayWay\":\"現金\"}";
                    JsonConvert.PopulateObject(PayWayValue, aFee, settings);

                }
            }

        }

    }
}

