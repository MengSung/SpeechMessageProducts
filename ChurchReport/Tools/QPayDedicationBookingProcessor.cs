using ChurchReport.WebServiceConnector;
using Line.Messaging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Xrm.Sdk;
using QPay.Domain;
using System;
using System.Collections.Generic;
using ToolUtilityNameSpace;


namespace ChurchReport.Tools
{
    public class QPayDedicationBookingProcessor : Controller, IDisposable
    {
        #region 資料區
        private LineMessagingClient m_LineMessagingClient { get; }

        private PushUtility m_PushUtility { get; }

        private ReplyUtility m_ReplyUtility { get; }

        private QPayProcessor m_QPayProcessor { get; }

        ToolUtilityClass m_ToolUtilityClass;

        // 客製化
        // 東湖禮拜堂 2.0
        private const String SPEECHMESSAGE_CHANNEL_ACCESS_TOKEN = @"z9/Jcbkvfm24ZogW0nHlaqONQjFYB6A10y2EPp07kWmw6vXSQSrTYrno3OuzCfM+ewEFWlahpjSOYa4HzyYxhZuAFbvoTQVPI/gjkE2PYMx5BESvuwJLJRZ86u3my9lD7zzvDNdZwStZzJh+IHmPFwdB04t89/1O/w1cDnyilFU=";

        // 胡夢嵩回傳　EXCEPTION　專用的ＩＤ
        private const String MENGSUNG_LINE_ID = @"U7638e4ed509708a3573ba6d69970583d";
        #endregion
        #region 初始化
        public QPayDedicationBookingProcessor()
        {
            this.m_LineMessagingClient = new LineMessagingClient(SPEECHMESSAGE_CHANNEL_ACCESS_TOKEN);

            //// 客製化
            m_PushUtility = new PushUtility(m_LineMessagingClient);
            m_ReplyUtility = new ReplyUtility(m_LineMessagingClient);

            m_QPayProcessor = new QPayProcessor(m_LineMessagingClient, m_PushUtility, m_ReplyUtility);

            m_ToolUtilityClass = new ToolUtilityClass("DYNAMICS365");

        }

        #region 釋放記憶體
        private bool _disposed = false;

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                m_ToolUtilityClass.Dispose();
            }

            _disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~QPayDedicationBookingProcessor()
        {
            // Do not re-create Dispose clean-up code here.
            // Calling Dispose(false) is optimal in terms of
            // readability and maintainability.
            Dispose(false);
        }
        #endregion

        #endregion
        #region 主程式
        public ActionResult QPayDedicationBookingProcessorReturnUrl(string ShopNo, String PayToken, QryOrderPay aQryOrderPay)
        {
            try
            {
                #region 處理認獻單
                // 取得認獻
                Entity aDedicationBookingEntity = this.m_ToolUtilityClass.RetrieveEntity("new_dedication_booking", new Guid(aQryOrderPay.TSResultContent.Param1));

                #region 沒找到認獻的例外處理
                if (aDedicationBookingEntity == null)
                {
                    if (aQryOrderPay.Status == "S" && aQryOrderPay.TSResultContent.Status == "S")
                    {
                        //return Json(new Dictionary<string, string>() { { "Status", "S" } });
                        return new OkObjectResult("信用卡付款結果成功!" + Environment.NewLine + aQryOrderPay.Description);
                    }
                    else
                    {
                        //return Json(new Dictionary<string, string>() { { "Status", "S" } });
                        return new OkObjectResult("信用卡付款結果失敗!" + Environment.NewLine + aQryOrderPay.Description);
                    }
                }
                else { }
                #endregion
                #region 是否已經有關連到此認獻的第001期收費單
                String aDedicationBookingName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aDedicationBookingEntity, "new_name");
                //取得認獻單目前的期數
                String aPaidPeriod = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aDedicationBookingEntity, "new_paid_period");

                if (this.m_ToolUtilityClass.RetrieveFeeByFetchXml(aDedicationBookingName, aDedicationBookingEntity.Id.ToString(), "001").Entities.Count > 0 || aPaidPeriod == "001")
                {
                    // 認獻單目前期數已經是 001
                    // 或是:已經有001期的收費單了，就不再往下繼續執行了
                    return new OkObjectResult("已經有收費單了!" + Environment.NewLine + aQryOrderPay.Description);
                }
                #endregion
                #endregion
                #region 處理付款人
                // 取得付款人
                Entity aContact = this.m_ToolUtilityClass.RetrieveEntity("contact", this.m_ToolUtilityClass.GetEntityLookupAttribute(aDedicationBookingEntity, "new_contact_new_dedication_booking"));
                // 取得付款人姓名
                String aFullName = this.m_ToolUtilityClass.GetEntityStringAttribute(aContact, "fullname");
                // 取得付款人 Line Id
                String UserLineId = this.m_ToolUtilityClass.GetEntityStringAttribute(aContact, "new_lineid");
                #region// 設定連絡人信用卡資訊
                if (aQryOrderPay.TSResultContent.CCToken != "")
                {
                    String VisaInfo = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContact, "new_visa_info");

                    if (IsCreditCardInList(aContact, aQryOrderPay) != true)
                    {
                        VisaInfo =
                                aQryOrderPay.TSResultContent.CCToken + "，" +
                                aQryOrderPay.TSResultContent.LeftCCNo + "，" +
                                aQryOrderPay.TSResultContent.RightCCNo + "，" +
                                //aQryOrderPay.TSResultContent.AuthCode + "，" +
                                aQryOrderPay.TSResultContent.CCExpDate +
                                "|" + VisaInfo;

                        this.m_ToolUtilityClass.SetEntityStringAttribute(ref aContact, "new_visa_info", VisaInfo);

                        this.m_ToolUtilityClass.UpdateEntity(ref aContact);
                    }
                }
                #endregion
                #endregion
                #region 收費單描述說明
                String Description =
                                     "姓名     : " + aFullName + Environment.NewLine +
                                     "訂單編號 : " + aQryOrderPay.TSResultContent.OrderNo + Environment.NewLine +
                                     "日期     : " + DateTime.Now.ToLocalTime().ToString() + Environment.NewLine +
                                     "實收金額 : " + ((int)Convert.ToUInt32(aQryOrderPay.TSResultContent.Amount) / 100).ToString() + Environment.NewLine +
                                     "付款方式 : " + "信用卡定期定額" + Environment.NewLine +
                                     "總期數   : " + this.m_ToolUtilityClass.GetEntityStringAttribute(ref aDedicationBookingEntity, "new_total_stages") + Environment.NewLine +
                                     "目前期數 : " + ProcessStageNumber(aQryOrderPay.TSResultContent.OrderNo) + Environment.NewLine +
                                     "程式呼叫 : " + aQryOrderPay.Description + Environment.NewLine +
                                     "交易結果 : " + aQryOrderPay.TSResultContent.Description + Environment.NewLine +
                                     //"這是 ChurcchReport Webhook!!" + Environment.NewLine +
                                     "--------------------" + Environment.NewLine;

                #endregion
                // 建立收費單
                CreateFee(aContact, aDedicationBookingEntity, aQryOrderPay, Description);

                #region 處理認獻單定期定額的第幾期字串
                String StageNumber = ProcessStageNumber(aQryOrderPay.TSResultContent.OrderNo);
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aDedicationBookingEntity, "new_paid_period", StageNumber);

                if (TransferToDeductTotalNum(this.m_ToolUtilityClass.GetEntityStringAttribute(ref aDedicationBookingEntity, "new_total_stages")) > Convert.ToUInt32(StageNumber.Replace("0", "")))
                {
                    // 總期數大於目前期數
                    // 認獻單狀態 = 進行中
                    this.m_ToolUtilityClass.SetOptionSetAttribute(ref aDedicationBookingEntity, "new_dedication_booking_status", 100000001);
                }
                else
                {
                    // 總期數小於或等於目前期數
                    // 認獻單狀態 = 已結束
                    this.m_ToolUtilityClass.SetOptionSetAttribute(ref aDedicationBookingEntity, "new_dedication_booking_status", 100000002);
                }
                #endregion

                if (aQryOrderPay.Status == "S" && aQryOrderPay.TSResultContent.Status == "S")
                {
                    // 認獻單備註 = 寫入成功的原因
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aDedicationBookingEntity, "new_explain", "信用卡定期定額付款結果成功!" + Environment.NewLine + this.m_ToolUtilityClass.GetEntityStringAttribute(ref aDedicationBookingEntity, "new_explain") + Environment.NewLine + "--------------------------------------" + Environment.NewLine + Description);

                    // 更新認獻單
                    this.m_ToolUtilityClass.UpdateEntity(ref aDedicationBookingEntity);

                    //if (this.m_ToolUtilityClass.GetEntityStringAttribute(aDedicationBookingEntity, "new_payment_records").Contains(aQryOrderPay.TSResultContent.OrderNo) != true && this.m_ToolUtilityClass.GetOptionSetAttribute(ref aFeeEntity, "new_pay_status") == 100000000)
                    if (aQryOrderPay.TSResultContent.OrderNo != "")
                    {
                        #region 信用卡會回傳2次，一次是RETURN_URL、一次是BACKEND_URL，為免收費單紀錄信用卡兩次，所以如果這裡已經有信用卡訂單編號，就不再處理了

                        // LINE 通知付款人
                        this.m_PushUtility.SendMessage(UserLineId, "信用卡付款結果成功!" + Environment.NewLine + Description);

                        //return Json(new Dictionary<string, string>() { { "Status", "S" } });
                        return new OkObjectResult("已經完成了信用卡付款!" + Environment.NewLine + Description + "收到" + aFullName + "的費用!");

                        #endregion
                    }
                    else
                    {
                        return new OkObjectResult("信用卡付款結果成功!" + Environment.NewLine + Description + "收到" + aFullName + "的費用!");
                    }
                }
                else
                {
                    // 認獻單狀態 = 啟動失敗
                    this.m_ToolUtilityClass.SetOptionSetAttribute(ref aDedicationBookingEntity, "new_dedication_booking_status", 100000003);

                    // 認獻單備註 = 寫入失敗的原因
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aDedicationBookingEntity, "new_explain", "信用卡定期定額付款結果失敗!" + Environment.NewLine + this.m_ToolUtilityClass.GetEntityStringAttribute(ref aDedicationBookingEntity, "new_explain") + Environment.NewLine + "--------------------------------------" + Environment.NewLine + Description);

                    // 更新認獻單
                    this.m_ToolUtilityClass.UpdateEntity(aDedicationBookingEntity);

                    // LINE 通知付款人
                    this.m_PushUtility.SendMessage(UserLineId, "信用卡付款結果失敗!" + Environment.NewLine + Description);

                    return new OkObjectResult("信用卡付款結果失敗!" + Environment.NewLine + Description);
                }
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                m_PushUtility.SendMessage(MENGSUNG_LINE_ID, ErrorString);
                //Monitor.Exit(this);
                throw e;
            }
        }
        #endregion
        #region 建立收費單
        public Guid CreateFee(Entity aContact, Entity aDedicationBookingEntity, QryOrderPay aQryOrderPay, String Description)
        {
            try
            {
                #region 建立收費單

                Entity aFeeToCreated = new Entity("new_fee");

                SetFeeParameter(aContact, aFeeToCreated, aDedicationBookingEntity, aQryOrderPay, Description);

                // 新增收費單
                Guid aFeeId = this.m_ToolUtilityClass.CreateEntity(aFeeToCreated);
                Entity aRetrievedFee = this.m_ToolUtilityClass.RetrieveEntity("new_fee", aFeeId);

                //指派負責人
                if (aRetrievedFee != null && aContact != null)
                {
                    try
                    {
                        this.m_ToolUtilityClass.AssignOwner("new_fee", aRetrievedFee, this.m_ToolUtilityClass.GetOwnerId(aContact));
                    }
                    catch (System.Exception e)
                    {
                        String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                    }
                }

                return aFeeId;
                #endregion
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //Monitor.Exit(this);
                throw e;
            }
        }
        public void SetFeeParameter(Entity aContact, Entity aFeeToCreated, Entity aDedicationBookingEntity, QryOrderPay aQryOrderPay, String Description)
        {
            try
            {
                #region 建立收費單所需要的參數
                // 取得報名者的全名
                String FullName = "";
                FullName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContact, "fullname");

                // 收費單名稱
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aFeeToCreated, "new_name", FullName + "奉獻");

                // 收費單姓名關聯 LOOKUP
                this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aFeeToCreated, "new_contact_new_fee", "contact", aContact.Id);

                // 收費單姓認獻名關聯 LOOKUP
                this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aFeeToCreated, "new_dedication_booking_new_fee", "new_dedication_booking", aDedicationBookingEntity.Id);

                // 收費單定期定額期數
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aFeeToCreated, "new_paid_period", ProcessStageNumber(aQryOrderPay.TSResultContent.OrderNo));

                // 收費單應收金額
                this.m_ToolUtilityClass.SetEntityMoneyAttribute(ref aFeeToCreated, "new_fee_shoud_pay", this.m_ToolUtilityClass.GetEntityMoneyAttribute(aDedicationBookingEntity, "new_amount_per_stage"));

                // 收費單實現阿拉伯數字到大寫中文的轉換，金額轉為大寫金額
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aFeeToCreated, "new_big_chinese_number", MoneyToChinese(this.m_ToolUtilityClass.GetEntityMoneyAttribute(aDedicationBookingEntity, "new_amount_per_stage").Value.ToString()));

                // 收費單付款方式，預設是信用卡
                this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeToCreated, "new_pay_way", 100000001);

                // 收費單收費日期
                this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aFeeToCreated, "new_pay_date", DateTime.Now.ToLocalTime());

                // 奉獻類別
                this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeToCreated, "new_category", this.m_ToolUtilityClass.GetOptionSetAttribute(ref aDedicationBookingEntity, "new_dedication_category"));

                // 付款紀錄
                String aPaymentRecords =
                        this.m_ToolUtilityClass.GetEntityStringAttribute(aFeeToCreated, "new_payment_records") +
                        DateTime.Now.ToString() +
                        ": ReturnUrl => 信用卡訂單編號= " + aQryOrderPay.TSResultContent.OrderNo +
                        "，金額:" + ((int)Convert.ToUInt32(aQryOrderPay.TSResultContent.Amount) / 100).ToString() +
                        Environment.NewLine;

                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aFeeToCreated, "new_payment_records", aPaymentRecords);

                if (aQryOrderPay.TSResultContent.OrderNo.StartsWith("C"))
                {
                    // 已付款信用卡訂單編號
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aFeeToCreated, "new_q_paid_card_order_no", aQryOrderPay.TSResultContent.OrderNo);
                }

                if (aQryOrderPay.Status == "S" && aQryOrderPay.TSResultContent.Status == "S")
                {
                    // 信用卡付款結果成功
                    // 收費單付款狀態，是信用卡已繳費
                    this.m_ToolUtilityClass.SetOptionSetAttribute(ref aFeeToCreated, "new_pay_status", 100000001); // 100000001 = 信用卡已繳費
                    // 收費單實收金額
                    this.m_ToolUtilityClass.SetEntityMoneyAttribute(ref aFeeToCreated, "new_fee_really_paid", new Money((int)Convert.ToUInt32(aQryOrderPay.TSResultContent.Amount) / 100));
                    // 收費單說明
                    String aOriginalDescription = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aFeeToCreated, "new_description");
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aFeeToCreated, "new_description", aOriginalDescription + "信用卡付款結果成功!" + Environment.NewLine + Description);
                }
                else
                {
                    // 信用卡付款結果失敗
                    // 收費單付款狀態，是新建立
                    this.m_ToolUtilityClass.SetOptionSetAttribute(ref aFeeToCreated, "new_pay_status", 100000000); // 100000000 = 信用卡新建立
                    // 收費單實收金額為0
                    this.m_ToolUtilityClass.SetEntityMoneyAttribute(ref aFeeToCreated, "new_fee_really_paid", new Money(0));
                    // 收費單說明
                    String aOriginalDescription = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aFeeToCreated, "new_description");
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aFeeToCreated, "new_description", aOriginalDescription + "信用卡付款結果失敗!" + Environment.NewLine + Description);
                }


                // 收費單奉獻其他類別
                //if (QpayModel.Others != "" && QpayModel.Others != null)
                //{
                //    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aFeeToCreated, "new_others", QpayModel.Others);
                //}

                // 奉獻地點
                //if (QpayModel.DedicateLocation != null)
                //{
                //    // 奉獻地點值不為NULL，所以應該是行政人員輸入而來的 parentcustomerid

                //    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aFeeToCreated, "new_dedicate_location", QpayModel.DedicateLocation);
                //}
                //else
                //{
                //    // 奉獻地點值為NULL，所以應該是信用卡或ATM、匯款而來的
                //    // 奉獻地點就要依據連絡人所屬教會設定
                //    // 取得連絡人所屬教會
                //    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aFeeToCreated, "new_dedicate_location", this.m_ToolUtilityClass.GetEntityLookupDisplayName(ref aContact, "parentcustomerid"));
                //}

                // 奉獻備註
                //this.m_ToolUtilityClass.SetEntityStringAttribute(ref aFeeToCreated, "new_explain", QpayModel.Explain);

                #endregion
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //Monitor.Exit(this);
                throw e;
            }
        }
        #endregion
        #region 工具區
        /// <summary>
        /// 實現阿拉伯數字到大寫中文的轉換，金額轉為大寫金額
        /// </summary>
        /// <param name="LowerMoney"></param>
        /// <returns></returns>
        public string MoneyToChinese(string LowerMoney)

        {

            string functionReturnValue = null;

            bool IsNegative = false; // 是否是負數

            if (LowerMoney.Trim().Substring(0, 1) == "-")

            {

                // 是負數則先轉為正數

                LowerMoney = LowerMoney.Trim().Remove(0, 1);

                IsNegative = true;

            }

            string strLower = null;

            string strUpart = null;

            string strUpper = null;

            int iTemp = 0;

            // 保留兩位小數 123.489→123.49　　123.4→123.4

            LowerMoney = Math.Round(double.Parse(LowerMoney), 2).ToString();

            if (LowerMoney.IndexOf(".") > 0)

            {

                if (LowerMoney.IndexOf(".") == LowerMoney.Length - 2)

                {

                    LowerMoney = LowerMoney + "0";

                }

            }

            else

            {

                LowerMoney = LowerMoney + ".00";

            }

            strLower = LowerMoney;

            iTemp = 1;

            strUpper = "";

            while (iTemp <= strLower.Length)

            {

                switch (strLower.Substring(strLower.Length - iTemp, 1))

                {

                    case ".":

                        strUpart = "圓";

                        break;

                    case "0":

                        strUpart = "零";

                        break;

                    case "1":

                        strUpart = "壹";

                        break;

                    case "2":

                        strUpart = "貳";

                        break;

                    case "3":

                        strUpart = "叄";

                        break;

                    case "4":

                        strUpart = "肆";

                        break;

                    case "5":

                        strUpart = "伍";

                        break;

                    case "6":

                        strUpart = "陸";

                        break;

                    case "7":

                        strUpart = "柒";

                        break;

                    case "8":

                        strUpart = "捌";

                        break;

                    case "9":

                        strUpart = "玖";

                        break;

                }

                switch (iTemp)

                {

                    case 1:

                        strUpart = strUpart + "分";

                        break;

                    case 2:

                        strUpart = strUpart + "角";

                        break;

                    case 3:

                        strUpart = strUpart + "";

                        break;

                    case 4:

                        strUpart = strUpart + "";

                        break;

                    case 5:

                        strUpart = strUpart + "拾";

                        break;

                    case 6:

                        strUpart = strUpart + "佰";

                        break;

                    case 7:

                        strUpart = strUpart + "仟";

                        break;

                    case 8:

                        strUpart = strUpart + "萬";

                        break;

                    case 9:

                        strUpart = strUpart + "拾";

                        break;

                    case 10:

                        strUpart = strUpart + "佰";

                        break;

                    case 11:

                        strUpart = strUpart + "仟";

                        break;

                    case 12:

                        strUpart = strUpart + "億";

                        break;

                    case 13:

                        strUpart = strUpart + "拾";

                        break;

                    case 14:

                        strUpart = strUpart + "佰";

                        break;

                    case 15:

                        strUpart = strUpart + "仟";

                        break;

                    case 16:

                        strUpart = strUpart + "萬";

                        break;

                    default:

                        strUpart = strUpart + "";

                        break;

                }

                strUpper = strUpart + strUpper;

                iTemp = iTemp + 1;

            }

            strUpper = strUpper.Replace("零拾", "零");

            strUpper = strUpper.Replace("零佰", "零");

            strUpper = strUpper.Replace("零仟", "零");

            strUpper = strUpper.Replace("零零零", "零");

            strUpper = strUpper.Replace("零零", "零");

            strUpper = strUpper.Replace("零角零分", "整");

            strUpper = strUpper.Replace("零分", "整");

            strUpper = strUpper.Replace("零角", "零");

            strUpper = strUpper.Replace("零億零萬零圓", "億圓");

            strUpper = strUpper.Replace("億零萬零圓", "億圓");

            strUpper = strUpper.Replace("零億零萬", "億");

            strUpper = strUpper.Replace("零萬零圓", "萬圓");

            strUpper = strUpper.Replace("零億", "億");

            strUpper = strUpper.Replace("零萬", "萬");

            strUpper = strUpper.Replace("零圓", "圓");

            strUpper = strUpper.Replace("零零", "零");

            // 對壹圓以下的金額的處理

            if (strUpper.Substring(0, 1) == "圓")

            {

                strUpper = strUpper.Substring(1, strUpper.Length - 1);

            }

            if (strUpper.Substring(0, 1) == "零")

            {

                strUpper = strUpper.Substring(1, strUpper.Length - 1);

            }

            if (strUpper.Substring(0, 1) == "角")

            {

                strUpper = strUpper.Substring(1, strUpper.Length - 1);

            }

            if (strUpper.Substring(0, 1) == "分")

            {

                strUpper = strUpper.Substring(1, strUpper.Length - 1);

            }

            if (strUpper.Substring(0, 1) == "整")

            {

                strUpper = "零圓整";

            }

            functionReturnValue = strUpper;

            if (IsNegative == true)

            {

                return "負" + functionReturnValue;

            }

            else

            {

                return functionReturnValue;

            }

        }

        public bool IsCreditCardInList(Entity aContact, QryOrderPay aQryOrderPay)
        {
            #region// 取得連絡人信用卡資訊

            String VisaInfo = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContact, "new_visa_info");

            if (VisaInfo != "")
            {
                // 有儲存的信用卡
                String[] VisaInfoSplit = VisaInfo.Split('|');

                if (VisaInfoSplit.Length > 0)
                {
                    // 檢驗一個一個的信用卡是否重覆
                    foreach (String CreditCard in VisaInfoSplit)
                    {
                        String[] VisaCCTokenSplit = CreditCard.Split('，');

                        if (VisaCCTokenSplit.Length == 4)
                        {
                            if
                            (
                                VisaCCTokenSplit[1] == aQryOrderPay.TSResultContent.LeftCCNo &&
                                VisaCCTokenSplit[2] == aQryOrderPay.TSResultContent.RightCCNo &&
                                VisaCCTokenSplit[3] == aQryOrderPay.TSResultContent.CCExpDate
                            )
                            {
                                // 有一樣的信用卡
                                return true;
                            }
                        }
                    }
                }
            }
            else
            {
                // 還沒有儲存的信用卡
                return false;
            }

            // 每個儲存的信用卡與目前要儲存的信用卡都不一樣
            return false;

            #endregion
        }

        public string ProcessStageNumber(string OrderNo)
        {
            //處理定期定額的第幾期字串
            if (OrderNo.Contains("_") == true)
            {
                //第二期以後開始有底線
                string[] OrderNoArray = OrderNo.Split('_');

                if (OrderNoArray.Length >= 2)
                {
                    return OrderNoArray[1];
                }
                else
                {
                    return "000";
                }
            }
            else
            {
                //沒有底線，所以是第一期
                return "001";
            }

        }
        private int TransferToDeductTotalNum(string DeductTotalNumber)
        {
            switch (DeductTotalNumber)
            {
                case "3個月":
                    return 3;
                case "6個月":
                    return 6;
                case "12個月":
                    return 12;
                case "18個月":
                    return 18;
                case "24個月":
                    return 24;
                default:
                    return 0;
            }
        }


        #endregion
    }
}
