using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ToolUtilityNameSpace;
using ToolUtilityNameSpace.DependencyInjection;

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
    /// <summary>
    /// 繳費與點名清單管理類別
    /// 負責處理課程繳費、點名相關的業務邏輯
    /// 使用 Dependency Injection 模式注入 ToolUtilityClass
    /// </summary>
    public class FeeList
    {
        #region 成員資料

        public List<Lesson> LessonList { get; set; }
        public List<Fee> FeeDataList { get; set; }

        public String FeeType { get; set; }
        public String Result { get; set; }

        public String m_FullName = "";
        public String m_Account = "";
        public String m_Password = "";

        public String SmallGroupLeaderContactId { get; set; }

        private readonly IToolUtilityProvider _toolUtilityProvider;
        
        /// <summary>
        /// 工具類單例 (透過 Provider 取得)
        /// </summary>
        private ToolUtilityClass ToolUtility => _toolUtilityProvider?.GetToolUtility();

        FeeDownUpLoader m_FeeDownUpLoader = new FeeDownUpLoader();

        public ClassName m_ClassName = new ClassName();

        #endregion

        #region 建構函式
        
        /// <summary>
        /// 無參數建構函式 (向後相容，但不建議使用)
        /// 此建構函式為了相容舊程式碼而保留，新程式碼應使用帶參數的建構函式
        /// </summary>
        [Obsolete("請使用帶有 IToolUtilityProvider 參數的建構函式")]
        public FeeList()
        {
            // 向後相容：如果沒有提供 Provider，則不使用 ToolUtility
            _toolUtilityProvider = null;
        }

        /// <summary>
        /// 依賴注入建構函式 (建議使用)
        /// </summary>
        /// <param name="toolUtilityProvider">ToolUtility 提供者實例 (透過 DI 注入)</param>
        public FeeList(IToolUtilityProvider toolUtilityProvider)
        {
            _toolUtilityProvider = toolUtilityProvider ?? throw new ArgumentNullException(nameof(toolUtilityProvider));
        }

        #endregion

        #region 初始化繳費與點名
        public void SetupLoginUserInfo(String FullName, String Account, String Password)
        {
            m_FullName = FullName;
            m_Account = Account;
            m_Password = Password;
        }

        public void SetupLessonList(String Account, String Password)
        {

            m_Account = Account;
            m_Password = Password;

            String LocalResult = "";

            LessonList = m_FeeDownUpLoader.GetLessonList(Account, Password, ref LocalResult, ref m_ClassName);

            if (LessonList.Count > 0)
            {
                FeeType = "有繳費點名";
            }
            else
            {
                FeeType = "無繳費點名";
            }
            Result = LocalResult;
        }
        public void SetupPresentFeeList(String DiscipleLessonsId)
        {
            String LocalResult = "";

            FeeDataList = m_FeeDownUpLoader.GetPresentFeeList(DiscipleLessonsId, ref LocalResult, ref m_ClassName);

            Result = LocalResult;
        }
        public void SetupFeeDataList(String Account, String Password)
        {

            m_Account = Account;
            m_Password = Password;

            String LocalResult = "";

            FeeDataList = m_FeeDownUpLoader.GetFeeList(Account, Password, ref LocalResult, ref m_ClassName);

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
        public void SetupFeeDataList()
        {
            String LocalResult = "";


            FeeDataList = m_FeeDownUpLoader.GetFeeList(m_Account, m_Password, ref LocalResult, ref m_ClassName);
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

            if (KeyList.Count > 0)
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

                if (Key == "PayDate" && ValueList[0] == null)
                {
                    // 繳費日期
                    String PayDateValue = "{\"PayDate\":\"" + DateTime.MinValue.ToUniversalTime().ToString("u") + "\"}";
                    JsonConvert.PopulateObject(PayDateValue, aFee, settings);
                }
                //else if(Key == "PayDate" && ValueList[1] != null)
                //{
                //    //DateTime aPayDate = new DateTime(ValueList[1])
                //    //String PayDateValue = "{\"PayDate\":\"" + new Datetime.ToUniversalTime().ToString("u") + "\"}";
                //    //JsonConvert.PopulateObject(PayDateValue, aFee, settings);
                //}


                if (Key == "Amount" && ValueList[0] == null)
                {
                    // 實收金額
                    String AmountValue = "{\"Amount\":\"" + "0" + "\"}";
                    JsonConvert.PopulateObject(AmountValue, aFee, settings);
                }
                if (Key == "RefundDate" && ValueList[0] == null)
                {
                    // 退費日期
                    String RefundDateValue = "{\"RefundDate\":\"" + DateTime.MinValue.ToUniversalTime().ToString("u") + "\"}";
                    JsonConvert.PopulateObject(RefundDateValue, aFee, settings);
                }

                if (Key == "RefundAmount" && ValueList[0] == null)
                {
                    // 退費金額
                    String RefundAmountValue = "{\"RefundAmount\":\"" + "0" + "\"}";
                    JsonConvert.PopulateObject(RefundAmountValue, aFee, settings);
                }
                //else if(Key == "RefundAmount" && ValueList[0] != null)
                //{
                //    aFee.Amount = aFee.Amount - aFee.RefundAmount;
                //}
            }

        }

    }
}

