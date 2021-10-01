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
    public class QPayFeeProcessor : Controller, IDisposable
    {
        private LineMessagingClient m_LineMessagingClient { get; }

        private PushUtility m_PushUtility { get; }

        private ReplyUtility m_ReplyUtility { get; }

        private QPayProcessor m_QPayProcessor { get; }

        ToolUtilityClass m_ToolUtilityClass;

        // 客製化
        // 安平靈糧堂 2.0
        private const String SPEECHMESSAGE_CHANNEL_ACCESS_TOKEN = @"MwTnnrBtGgUaj+ZfbiKx7dxYxIuJKBmX9PLwKcRQU+VG4u0Gvyv2VeIjmNOr3pVGfH4JizB2wNbT0K0c4pT/XXCoBpK3lMQGaRAfS0FMoy05WDFQJgTL7etz9BHrzzWL6j0aFfutv6F4sMvcAdkTPgdB04t89/1O/w1cDnyilFU=";

        // 胡夢嵩回傳　EXCEPTION　專用的ＩＤ
        private const String MENGSUNG_LINE_ID = @"U7638e4ed509708a3573ba6d69970583d";

        public QPayFeeProcessor()
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

        ~QPayFeeProcessor()
        {
            // Do not re-create Dispose clean-up code here.
            // Calling Dispose(false) is optimal in terms of
            // readability and maintainability.
            Dispose(false);
        }
        #endregion

        //[HttpGet]
        //[Route("QPayReturnUrl")]
        //public async Task<IActionResult> QPayReturnUrl(int? id = 0)
        //{
        //    return new OkObjectResult("付款結果可能成功");
        //}

        public ActionResult QPayFeeProcessorReturnUrl(string ShopNo, String PayToken, QryOrderPay aQryOrderPay)
        {
            try
            {
                //m_PushUtility.SendMessage(MENGSUNG_LINE_ID, "QPayReturnUrl_001");

                Entity aFeeEntity = this.m_ToolUtilityClass.RetrieveEntity("new_fee", new Guid(aQryOrderPay.TSResultContent.Param1));
                if (aFeeEntity == null)
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

                // 取得付款人
                Entity aContact = this.m_ToolUtilityClass.RetrieveEntity("contact", this.m_ToolUtilityClass.GetEntityLookupAttribute(aFeeEntity, "new_contact_new_fee"));
                // 取得付款人姓名
                String aFullName = this.m_ToolUtilityClass.GetEntityStringAttribute(aContact, "fullname");
                // 取得付款人 Line Id
                String UserLineId = this.m_ToolUtilityClass.GetEntityStringAttribute(aContact, "new_lineid");

                // 收費單描述說明
                String Description =
                                     "姓名     : " + aFullName + Environment.NewLine +
                                     "訂單編號 : " + aQryOrderPay.TSResultContent.OrderNo + Environment.NewLine +
                                     "日期     : " + DateTime.Now.ToLocalTime().ToString() + Environment.NewLine +
                                     "實收金額 : " + ((int)Convert.ToUInt32(aQryOrderPay.TSResultContent.Amount) / 100).ToString() + Environment.NewLine +
                                     "付款方式 : " + "信用卡" + Environment.NewLine +
                                     "程式呼叫 : " + aQryOrderPay.Description + Environment.NewLine +
                                     "交易結果 : " + aQryOrderPay.TSResultContent.Description + Environment.NewLine +
                                     //"這是 ChurcchReport Webhook!!" + Environment.NewLine +
                                     "--------------------" + Environment.NewLine;

                if (aQryOrderPay.Status == "S" && aQryOrderPay.TSResultContent.Status == "S")
                {
                    if (this.m_ToolUtilityClass.GetEntityStringAttribute(aFeeEntity, "new_payment_records").Contains(aQryOrderPay.TSResultContent.OrderNo) != true && this.m_ToolUtilityClass.GetOptionSetAttribute(ref aFeeEntity, "new_pay_status") == 100000000)
                    {
                        #region 信用卡會回傳2次，一次是RETURN_URL、一次是BACKEND_URL，為免收費單紀錄信用卡兩次，所以如果這裡已經有信用卡訂單編號，就不再處理了
                        // 收費單付款日期
                        this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aFeeEntity, "new_pay_date", DateTime.Now.ToLocalTime());
                        // 收費單總共實收金額
                        Money aTotalPaid = new Money(Convert.ToUInt32(this.m_ToolUtilityClass.GetEntityMoneyAttribute(ref aFeeEntity, "new_fee_really_paid").Value + new Money((int)Convert.ToUInt32(aQryOrderPay.TSResultContent.Amount) / 100).Value));
                        this.m_ToolUtilityClass.SetEntityMoneyAttribute(ref aFeeEntity, "new_fee_really_paid", aTotalPaid);
                        // 收費單實現阿拉伯數字到大寫中文的轉換，金額轉為大寫金額
                        this.m_ToolUtilityClass.SetEntityStringAttribute(ref aFeeEntity, "new_big_chinese_number", MoneyToChinese((Convert.ToUInt32(aQryOrderPay.TSResultContent.Amount) / 100).ToString()));
                        // 收費單付款方式
                        this.m_ToolUtilityClass.SetOptionSetAttribute(ref aFeeEntity, "new_pay_way", 100000001); // 100000001 = 信用卡
                        // 收費單付款狀態
                        this.m_ToolUtilityClass.SetOptionSetAttribute(ref aFeeEntity, "new_pay_status", 100000001); // 100000001 = 信用卡已繳費
                        // 收費單說明
                        String aOriginalDescription = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aFeeEntity, "new_description");
                        this.m_ToolUtilityClass.SetEntityStringAttribute(ref aFeeEntity, "new_description", aOriginalDescription + "信用卡付款結果成功!" + Environment.NewLine + Description);

                        // 付款紀錄
                        String aPaymentRecords =
                                this.m_ToolUtilityClass.GetEntityStringAttribute(aFeeEntity, "new_payment_records") +
                                DateTime.Now.ToString() +
                                ": ReturnUrl => 信用卡訂單編號= " + aQryOrderPay.TSResultContent.OrderNo +
                                "，金額:" + ((int)Convert.ToUInt32(aQryOrderPay.TSResultContent.Amount) / 100).ToString() +
                                "，PayToken = " + PayToken +
                                Environment.NewLine;

                        this.m_ToolUtilityClass.SetEntityStringAttribute(ref aFeeEntity, "new_payment_records", aPaymentRecords);

                        if (aQryOrderPay.TSResultContent.OrderNo.StartsWith("C"))
                        {
                            // 已付款信用卡訂單編號
                            this.m_ToolUtilityClass.SetEntityStringAttribute(ref aFeeEntity, "new_q_paid_card_order_no", aQryOrderPay.TSResultContent.OrderNo);
                        }

                        // 更新收費單
                        this.m_ToolUtilityClass.UpdateEntity(ref aFeeEntity);

                        #region// 取得上課紀錄單，更新報名狀態
                        Guid aStorLessonsId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aFeeEntity, "new_stor_lessons_new_fee");
                        if (aStorLessonsId != Guid.Empty)
                        {
                            Entity aStorLessons = this.m_ToolUtilityClass.RetrieveEntity("new_stor_lessons", aStorLessonsId);

                            #region 報名狀態
                            // 有審核的教會=>報名成功:安平靈糧堂
                            this.m_ToolUtilityClass.SetOptionSetAttribute(ref aStorLessons, "new_enroll_status", 100000008);
                            #endregion

                            this.m_ToolUtilityClass.UpdateEntity(ref aStorLessons);
                        }
                        #endregion

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

                        // LINE 通知付款人
                        this.m_PushUtility.SendMessage(UserLineId, "信用卡付款結果成功!" + Environment.NewLine + Description);

                        //return Json(new Dictionary<string, string>() { { "Status", "S" } });
                        return new OkObjectResult("已經完成了信用卡付款!" + Environment.NewLine + Description + "收到" + aFullName + "的費用!");

                        #endregion
                    }
                    else
                    {
                        //return Json(new Dictionary<string, string>() { { "Status", "S" } });
                        return new OkObjectResult("信用卡付款結果成功!" + Environment.NewLine + Description + "收到" + aFullName + "的費用!");
                    }
                }
                else
                {
                    //return Json(new Dictionary<string, string>() { { "Status", "S" } });
                    // 收費單說明
                    String aOriginalDescription = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aFeeEntity, "new_description");
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aFeeEntity, "new_description", aOriginalDescription + "信用卡付款結果失敗!" + Environment.NewLine + Description);

                    // 更新收費單
                    this.m_ToolUtilityClass.UpdateEntity(ref aFeeEntity);

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

        #endregion
    }
}
