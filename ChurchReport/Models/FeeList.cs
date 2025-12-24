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

        /// <summary>
        /// 修改歷程 ViewModel (暫存修改，不立即提交到資料庫)
        /// </summary>
        public FeeChangeHistoryViewModel ChangeHistory { get; set; }

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
            ChangeHistory = new FeeChangeHistoryViewModel();
        }

        /// <summary>
        /// 依賴注入建構函式 (建議使用)
        /// </summary>
        /// <param name="toolUtilityProvider">ToolUtility 提供者實例 (透過 DI 注入)</param>
        public FeeList(IToolUtilityProvider toolUtilityProvider)
        {
            _toolUtilityProvider = toolUtilityProvider ?? throw new ArgumentNullException(nameof(toolUtilityProvider));
            ChangeHistory = new FeeChangeHistoryViewModel();
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

        /// <summary>
        /// 填充物件並記錄修改歷程 (不立即更新資料庫)
        /// 修改會被暫存在 ChangeHistory 中，等待批次上傳
        /// </summary>
        /// <param name="Values">JSON 格式的修改值</param>
        /// <param name="aFee">要修改的 Fee 物件</param>
        public void PopulateObjectAndUpdateEntity(string Values, Fee aFee)
        {
            var settings = new JsonSerializerSettings
            {
                // 轉換成當地時間
                DateTimeZoneHandling = DateTimeZoneHandling.Local,

                NullValueHandling = NullValueHandling.Ignore,
                MissingMemberHandling = MissingMemberHandling.Ignore
            };

            // 先更新記憶體中的 Fee 物件
            JsonConvert.PopulateObject(Values, aFee, settings);

            Dictionary<string, string> aDictionary = JsonConvert.DeserializeObject<Dictionary<string, string>>(Values);

            List<string> KeyList = new List<string>(aDictionary.Keys);
            List<string> ValueList = new List<string>(aDictionary.Values);

            if (KeyList.Count > 0)
            {
                String Key = KeyList[0];
                String Value = ValueList[0];

                // ? 記錄修改到 ChangeHistory，而不直接更新資料庫
                ChangeHistory.RecordChange(aFee.StorLessonsId, Key, Value);

                // 處理特殊欄位的聯動邏輯 (僅更新記憶體物件)
                if (Key == "Amount" && !string.IsNullOrEmpty(Value) && Value != "0")
                {
                    // 當填入金額時，自動設定繳費日期與繳費方式
                    String PayDateValue = "{\"PayDate\":\"" + DateTime.Now.ToUniversalTime().ToString("u") + "\"}";
                    JsonConvert.PopulateObject(PayDateValue, aFee, settings);

                    String PayWayValue = "{\"PayWay\":\"現金\"}";
                    JsonConvert.PopulateObject(PayWayValue, aFee, settings);

                    // 同時記錄這些聯動修改
                    ChangeHistory.RecordChange(aFee.StorLessonsId, "PayDate", DateTime.Now.ToUniversalTime().ToString("u"));
                    ChangeHistory.RecordChange(aFee.StorLessonsId, "PayWay", "現金");
                }

                if (Key == "PayDate" && Value == null)
                {
                    // 清空繳費日期
                    String PayDateValue = "{\"PayDate\":\"" + DateTime.MinValue.ToUniversalTime().ToString("u") + "\"}";
                    JsonConvert.PopulateObject(PayDateValue, aFee, settings);
                }

                if (Key == "Amount" && Value == null)
                {
                    // 清空實收金額
                    String AmountValue = "{\"Amount\":\"" + "0" + "\"}";
                    JsonConvert.PopulateObject(AmountValue, aFee, settings);
                }

                if (Key == "RefundDate" && Value == null)
                {
                    // 清空退費日期
                    String RefundDateValue = "{\"RefundDate\":\"" + DateTime.MinValue.ToUniversalTime().ToString("u") + "\"}";
                    JsonConvert.PopulateObject(RefundDateValue, aFee, settings);
                }

                if (Key == "RefundAmount" && Value == null)
                {
                    // 清空退費金額
                    String RefundAmountValue = "{\"RefundAmount\":\"" + "0" + "\"}";
                    JsonConvert.PopulateObject(RefundAmountValue, aFee, settings);
                }
            }
        }

        /// <summary>
        /// 批次提交所有待處理的修改到資料庫
        /// 由 save-button 觸發
        /// </summary>
        /// <returns>成功提交的記錄數量</returns>
        public int CommitPendingChanges()
        {
            int successCount = 0;
            int totalChanges = ChangeHistory.GetPendingCount();

            System.Diagnostics.Debug.WriteLine($"[CommitPendingChanges] 開始批次上傳 - 待處理記錄數: {totalChanges}");

            foreach (var changeRecord in ChangeHistory.PendingChanges.Values)
            {
                try
                {
                    // 對每個欄位執行資料庫更新
                    foreach (var field in changeRecord.ModifiedFields)
                    {
                        string fieldName = field.Key;
                        string fieldValue = field.Value;

                        bool createFlag = false;

                        System.Diagnostics.Debug.WriteLine(
                            $"[CommitPendingChanges] 更新 StorLessonsId={changeRecord.StorLessonsId}, " +
                            $"Field={fieldName}, Value={fieldValue}");

                        // 呼叫原本的更新方法
                        m_FeeDownUpLoader.UpdateFeeDataList(
                            fieldName, 
                            fieldValue, 
                            changeRecord.StorLessonsId, 
                            ref createFlag);
                    }

                    successCount++;
                    System.Diagnostics.Debug.WriteLine(
                        $"[CommitPendingChanges] 成功更新 StorLessonsId={changeRecord.StorLessonsId}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[CommitPendingChanges] 更新失敗 - StorLessonsId={changeRecord.StorLessonsId}, " +
                        $"錯誤: {ex.Message}");
                    // 繼續處理其他記錄
                }
            }

            System.Diagnostics.Debug.WriteLine(
                $"[CommitPendingChanges] 批次上傳完成 - 成功: {successCount}/{totalChanges}");

            // 清除所有待處理的修改
            ChangeHistory.ClearAll();

            return successCount;
        }

    }
}

