using Line.Pay;
using Line.Pay.Models;
using System;
using System.Threading.Tasks;
using Microsoft.Xrm.Sdk;

using ChurchReport.Models;

using ToolUtilityNameSpace;
using ToolUtilityNameSpace.Factory;
using Microsoft.Extensions.Configuration;
using System.IO;

namespace ChurchReport.WebServiceConnector
{
    public class LinePayProcessor
    {
        LinePayClient m_LinePayClient;

        // 透過 Factory 取得 ToolUtilityClass 單一實例
        ToolUtilityClass m_ToolUtilityClass = ToolUtilityFactory.GetInstance("DYNAMICS365-9.0");

        // 客製化
        private const String CONFIRM_URL = "https://nankanchurch.speechmessage.com.tw:335/api/callback/Confirm";

        public LinePayProcessor()
        {
            IConfiguration configuration;

            var builder = new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory()).AddJsonFile("appsettings.json");

            configuration = builder.Build();

            m_LinePayClient = new LinePayClient(configuration["LinePay:ChannelId"], configuration["LinePay:ChannelSecret"], bool.Parse(configuration["LinePay:IsSandbox"]));

        }
        public async Task<string> NotifyLinePay(String LineId, DedicationModel DedicationModel)
        {
            try
            {
                #region 通知住綁定的輸入格式

                var reserve = new Reserve()
                {
                    //ProductName = "全人醫治",
                    //ProductName = "幸福茶會",
                    //ProductName = "十一奉獻",
                    ProductName = DedicationModel.Category,

                    //ProductImageUrl = "https://upload.cc/i1/2019/01/09/j2fmYa.jpg",
                    //ProductImageUrl = "https://web.opendrive.com/api/v1/download/file.json/MF8xODExMDI4NDRf?inline=1",
                    ProductImageUrl = "https://upload.cc/i1/2019/01/09/f69ikp.jpg",
                    Amount = DedicationModel.Amount,
                    Currency = Currency.TWD,
                    OrderId = Guid.NewGuid().ToString(),
                    ConfirmUrl = CONFIRM_URL,
                    CancelUrl = CONFIRM_URL,
                    Capture = true,
                    //ConfirmUrlType = ConfirmUrlType.SERVER,
                    ConfirmUrlType = ConfirmUrlType.CLIENT,
                    LanguageCode = LanguageCode.zh_Hant,
                    PayType = PayType.NORMAL
                };

                //var response = m_LinePayClient.ReserveAsync(reserve);
                //Task<ReserveResponse> response = await m_LinePayClient.ReserveAsync(reserve);
                var response = await m_LinePayClient.ReserveAsync(reserve);

                CreateFee(LineId, response, DedicationModel);
                //Redirect(response.Info.PaymentUrl.Web);

                return response.Info.PaymentUrl.Web;

                //RedirectToPage(response.Info.PaymentUrl.App);

                //BindingAction.Add(new UriTemplateAction("不想繳費", response.Info.PaymentUrl.App));

                //await m_PushUtility.SendMessage(DisplayLineId, "付款網址 = " + response.Info.PaymentUrl.Web);

                #endregion

            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //Monitor.Exit(this);
                throw e;
            }
        }

        public void CreateFee( String LineId,ReserveResponse aReserveResponse, DedicationModel DedicationModel)
        {
            try
            {
                #region 通知住綁定的輸入格式

                Entity aFeeToCreated = new Entity("new_fee");

                SetFeeParameter( LineId, aFeeToCreated, aReserveResponse, DedicationModel);

                this.m_ToolUtilityClass.CreateEntity(aFeeToCreated);
                #endregion

            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //Monitor.Exit(this);
                throw e;
            }
        }
        public void SetFeeParameter(String LineId, Entity aFeeToCreated, ReserveResponse aReserveResponse, DedicationModel DedicationModel)
        {
            try
            {
                #region 通知住綁定的輸入格式
                // 連絡人姓名
                Entity aContact = this.m_ToolUtilityClass.RetrieveContactEntityByLineUserId(LineId);

                // 取得課程名稱
                //String LessonDisplayName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aLessonEntity, "new_name");

                // 取得報名者的全名
                String FullName = "";
                FullName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContact, "fullname");

                // 收費單名稱
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aFeeToCreated, "new_name",  FullName + "奉獻");

                // 收費單姓名關聯 LOOKUP
                this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aFeeToCreated, "new_contact_new_fee", "contact", aContact.Id);

                // 收費單課程關聯LOOKUP
                //this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aFeeToCreated, "new_disciple_lessons_new_fee", "new_disciple_lessons", aLessonEntity.Id);

                // 收費單應收金額
                this.m_ToolUtilityClass.SetEntityMoneyAttribute(ref aFeeToCreated, "new_fee_shoud_pay", new Money(DedicationModel.Amount));

                // 收費單實收金額
                this.m_ToolUtilityClass.SetEntityMoneyAttribute(ref aFeeToCreated, "new_fee_really_paid", new Money(0));

                // 收費單實現阿拉伯數字到大寫中文的轉換，金額轉為大寫金額
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aFeeToCreated, "new_big_chinese_number", MoneyToChinese(DedicationModel.Amount.ToString()));

                // 收費單付款方式
                this.m_ToolUtilityClass.SetOptionSetAttribute(ref aFeeToCreated, "new_pay_way", 100000005);

                // 收費單上課紀錄關聯
                //this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aFeeToCreated, "new_stor_lessons_new_fee", "new_stor_lessons", NewStorLessonId);

                // 收費單收費日期
                this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aFeeToCreated, "new_pay_date", DateTime.Now.ToLocalTime());

                // Line Pay
                //this.m_ToolUtilityClass.SetEntityIntAttribute(ref aFeeToCreated, "new_transaction_id", (int)aReserveResponse.Info.TransactionId);

                // 交易識別碼
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aFeeToCreated, "new_transaction_id_string", aReserveResponse.Info.TransactionId.ToString());
                //付款憑證
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aFeeToCreated, "new_payment_access_token", aReserveResponse.Info.PaymentAccessToken);
                // 付款網頁
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aFeeToCreated, "new_payment_url_web", aReserveResponse.Info.PaymentUrl.Web);
                // 付款應用
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aFeeToCreated, "new_payment_url_app", aReserveResponse.Info.PaymentUrl.App);

                #endregion

            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

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
        #endregion
    }
}
