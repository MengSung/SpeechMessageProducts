using Line.Pay;
using Line.Pay.Models;
using System;
using System.Threading.Tasks;
using Microsoft.Xrm.Sdk;

using ChurchReport.Models;

using ToolUtilityNameSpace;
using Microsoft.Extensions.Configuration;
using System.IO;

using QPay.Domain;
using System;
using System.Threading.Tasks;
using Line.Messaging;
using Line.Pay;
using Line.Pay.Models;
using Microsoft.Xrm.Sdk;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ChurchReport.Tools;
using UserProfile = Line.Messaging.UserProfile;

namespace ChurchReport.WebServiceConnector
{
    public class QPayProcessor
    {
        #region 資料區
        private static ConfigurationBuilder m_ConfigurationBuilder = (ConfigurationBuilder)new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory()).AddJsonFile("appsettings.json");
        private static IConfiguration m_Configuration = m_ConfigurationBuilder.Build();

        // 商店編號
        // SANDBOX 測試用
        //string m_ShopNo = "NA0149_001";
        // 永豐金流正式環境
        //string m_ShopNo = "DA4195_001";
        private string m_ShopNo = m_Configuration["Sinopac:ShopNo"];

        #region 公司內部開發
        // 使用 ChurchReport 當作 WebHook
        //private const String RETURN_URL = "https://nankanchurchback.speechmessage.com.tw:480/api/QPayCard/QPayReturnUrl";
        //private const String BACKEND_URL = "http://QPbackendback.speechmessage.com.tw/api/QPayAtm/QPayBackendUrl";// 公司內部開發
        #endregion
        #region 雲端機房
        private const String RETURN_URL = "https://nankanchurch.speechmessage.com.tw:335/api/QPayCard/QPayReturnUrl";
        private const String BACKEND_URL = "http://QPaybackend.speechmessage.com.tw/api/QPayAtm/QPayBackendUrl"; // 雲端機房
        #endregion

        // 客製化
        // 南崁基督長老教會
        private const String CHANNEL_ACCESS_TOKEN = @"m7bC4vm/2pA8VEBbHZ1YHdr0iz4fmOMWqT1jEZg+62DFvGEEfY7JEJ7up5gNdpJ3DSZHFmr+YZpEu02B15B4ZMx7s03ZeLqZi1lSmpxsA04Zi6cOJlQemlXjlUMlh+HOKb3BfOhOPY+hYtMbH2tUXQdB04t89/1O/w1cDnyilFU=";

        //private LinePayClient m_LinePayClient { get; }

        private LineMessagingClient m_LineMessagingClient { get; }
        ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass("DYNAMICS365");

        //private LineNotifyUtility m_LineNotifyUtility = new LineNotifyUtility();

        private PushUtility m_PushUtility { get; }
        private ReplyUtility m_ReplyUtility { get; }

        //private String m_LocalCardOrderNo = "";
        //private String m_LocalAtmOrderNo = "";

        //private DateTime m_AtmExpireDate;

        // 登入的連絡人
        public Entity m_LoginContact;

        // 客製化
        private const String QPAY_ORGANIZATION = "nankanchurch";

        #endregion
        #region 初始化
        public QPayProcessor()
        {
            this.m_LineMessagingClient = new LineMessagingClient(CHANNEL_ACCESS_TOKEN);

            // 客製化
            m_PushUtility = new PushUtility(m_LineMessagingClient);
            m_ReplyUtility = new ReplyUtility(m_LineMessagingClient);
        }
        public QPayProcessor(LineMessagingClient aLineMessagingClient, PushUtility aPushUtility, ReplyUtility aReplyUtility)
        {
            m_LineMessagingClient = aLineMessagingClient;
            //m_LinePayClient = LinePayClient;

            m_PushUtility = aPushUtility;
            m_ReplyUtility = aReplyUtility;
        }
        #endregion
        #region 建立收費單
        public async Task<string> CreateFeeAsync(Entity LineLoginContact, QpayModel QpayModel)
        {
            try
            {
                #region 非同步建立收費單

                // 產品名稱加入姓名
                QpayModel.FullName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref LineLoginContact, "fullname");

                if (QpayModel.PayWay == "信用卡" || QpayModel.PayWay == "銀聯卡")
                {
                    Guid aCreatedFeeId = CreateFee(LineLoginContact, QpayModel, false);
                    Entity aFeeToUpdate = this.m_ToolUtilityClass.RetrieveEntity("new_fee", aCreatedFeeId);

                    CreOrder CreatedCardOrder;
                    if (QpayModel.PayWay == "信用卡")
                    {
                        // 信用卡
                        CreatedCardOrder = await CreOrderCard(QpayModel.Amount, QpayModel.Category + "-" + QpayModel.FullName, DateTime.Now.ToString("yyyyMMddhhmmssfff"), aCreatedFeeId.ToString(), "C", "ONE", "", 0, "M", 1, "收費單", QpayModel.SelectedCreditCard);
                    }
                    else
                    {
                        // 銀聯卡
                        CreatedCardOrder = await CreOrderCard(QpayModel.Amount, QpayModel.Category + "-" + QpayModel.FullName, DateTime.Now.ToString("yyyyMMddhhmmssfff"), aCreatedFeeId.ToString(), "C", "CUP", "", 0, "M", 1, "收費單", QpayModel.SelectedCreditCard);
                    }
                    if (CreatedCardOrder.CardParam != null && CreatedCardOrder.CardParam.CardPayURL != null)
                    {
                        // 用剛剛建立的收費單，填寫訂單編號
                        UpdateFee(ref aFeeToUpdate, CreatedCardOrder.OrderNo, "", "");

                        return CreatedCardOrder.CardParam.CardPayURL;
                    }
                    else
                    {
                        return "信用卡繳費失敗!" + CreatedCardOrder.Description;
                    }
                }
                else if (QpayModel.PayWay == "信用卡定期定額(每個月)")
                {
                    //ONE 一次付清
                    //STAGING 分期付款
                    //BONUS 紅利折抵
                    //CUP 銀聯卡一次付清
                    //REGULAR 定期定額扣款

                    // 建立認獻單
                    Guid aCreatedDedicationBookingId = CreateDedicationBooking(LineLoginContact, QpayModel);
                    Entity aDedicationBookingToUpdate = this.m_ToolUtilityClass.RetrieveEntity("new_dedication_booking", aCreatedDedicationBookingId);

                    CreOrder CreatedCardOrder = await CreOrderCard(QpayModel.Amount, QpayModel.Category + "-" + QpayModel.FullName, DateTime.Now.ToString("yyyyMMddhhmmssfff"), aCreatedDedicationBookingId.ToString(), "C", "REGULAR", "", TransferToDeductTotalNum(QpayModel.DeductTotalNumber), "M", 1, "認獻單", QpayModel.SelectedCreditCard);

                    if (CreatedCardOrder.CardParam != null && CreatedCardOrder.CardParam.CardPayURL != null)
                    {
                        if (CreatedCardOrder.Status == "S")
                        {
                            // 用剛剛建立的認獻單，填寫訂單編號， 更新收費單或是認獻單(因為欄位名稱一致)
                            UpdateFee(ref aDedicationBookingToUpdate, CreatedCardOrder.OrderNo, "", "");

                            return CreatedCardOrder.CardParam.CardPayURL;
                        }
                        else
                        {
                            // 信用卡繳費失敗!
                            // 用剛剛建立的認獻單，填寫訂單編號， 更新收費單或是認獻單(因為欄位名稱一致)
                            UpdateFee(ref aDedicationBookingToUpdate, CreatedCardOrder.Description, "", "");

                            return "信用卡繳費失敗!" + CreatedCardOrder.Description;
                        }
                    }
                    else
                    {
                        // 認獻單狀態 = 啟動失敗
                        this.m_ToolUtilityClass.SetOptionSetAttribute(ref aDedicationBookingToUpdate, "new_dedication_booking_status", 100000003);

                        // 認獻單備註 = 寫入失敗的原因
                        this.m_ToolUtilityClass.SetEntityStringAttribute(ref aDedicationBookingToUpdate, "new_explain", "建立永豐信用卡訂單時就失敗了");

                        // 更新認獻單
                        this.m_ToolUtilityClass.UpdateEntity(aDedicationBookingToUpdate);

                        return "信用卡定期定額建立失敗!" + CreatedCardOrder.Description;
                    }
                }
                else if (QpayModel.PayWay == "行動支付")
                {
                    //ONE 一次付清
                    //STAGING 分期付款
                    //BONUS 紅利折抵
                    //CUP 銀聯卡一次付清
                    //REGULAR 定期定額扣款

                    Guid aCreatedFeeId = CreateFee(LineLoginContact, QpayModel, false);
                    Entity aFeeToUpdate = this.m_ToolUtilityClass.RetrieveEntity("new_fee", aCreatedFeeId);

                    CreOrder CreatedCardOrder = await CreOrderCard(QpayModel.Amount, QpayModel.Category + "-" + QpayModel.FullName, DateTime.Now.ToString("yyyyMMddhhmmssfff"), aCreatedFeeId.ToString(), "M", "ONE", "", 0, "M", 1, "收費單", QpayModel.SelectedCreditCard);

                    if (CreatedCardOrder.MobileParam != null && CreatedCardOrder.MobileParam.MobilePayURL != null)
                    {
                        // 用剛剛建立的收費單，填寫訂單編號
                        UpdateFee(ref aFeeToUpdate, CreatedCardOrder.OrderNo, "", "");

                        return CreatedCardOrder.MobileParam.MobilePayURL;
                    }
                    else
                    {
                        UpdateFee(ref aFeeToUpdate, CreatedCardOrder.Description, "", "");

                        return "行動支付付款失敗!" + CreatedCardOrder.Description;
                    }

                }
                else if (QpayModel.PayWay == "LinePay")
                {
                    //ONE 一次付清
                    //STAGING 分期付款
                    //BONUS 紅利折抵
                    //CUP 銀聯卡一次付清
                    //REGULAR 定期定額扣款

                    Guid aCreatedFeeId = CreateFee(LineLoginContact, QpayModel, false);
                    Entity aFeeToUpdate = this.m_ToolUtilityClass.RetrieveEntity("new_fee", aCreatedFeeId);

                    CreOrder CreatedCardOrder = await CreOrderCard(QpayModel.Amount, QpayModel.Category + "-" + QpayModel.FullName, DateTime.Now.ToString("yyyyMMddhhmmssfff"), aCreatedFeeId.ToString(), "L", "ONE", "", 0, "M", 1, "收費單", QpayModel.SelectedCreditCard);

                    if (CreatedCardOrder.MobileParam != null && CreatedCardOrder.MobileParam.MobilePayURL != null)
                    {
                        // 用剛剛建立的收費單，填寫訂單編號
                        UpdateFee(ref aFeeToUpdate, CreatedCardOrder.OrderNo, "", "");

                        return CreatedCardOrder.MobileParam.MobilePayURL;
                    }
                    else
                    {
                        UpdateFee(ref aFeeToUpdate, CreatedCardOrder.Description, "", "");

                        return "LinePay付款失敗!" + CreatedCardOrder.Description;
                    }
                }
                else if (QpayModel.PayWay == "ATM轉帳/匯款")
                {
                    Guid aCreatedFeeId = CreateFee(LineLoginContact, QpayModel, false);
                    Entity aFeeToUpdate = this.m_ToolUtilityClass.RetrieveEntity("new_fee", aCreatedFeeId);

                    return await ProcessAtm(aCreatedFeeId, aFeeToUpdate, QpayModel, "", LineLoginContact);
                }
                else
                {
                    return "信用卡繳費失敗!";
                }

                #endregion
            }
            catch (System.Exception e)
            { 
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //Monitor.Exit(this);
                throw e;
            }
        }
        public void SetFeeParameter(String LineId, Entity aFeeToCreated, QpayModel QpayModel, bool KeyinMode)
        {
            try
            {
                #region 建立收費單所需要的參數
                // 連絡人姓名
                Entity aContact = this.m_ToolUtilityClass.RetrieveContactEntityByLineUserId(LineId);

                // 取得報名者的全名
                String FullName = "";
                FullName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContact, "fullname");

                // 收費單名稱
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aFeeToCreated, "new_name", FullName + "奉獻");

                // 收費單姓名關聯 LOOKUP
                this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aFeeToCreated, "new_contact_new_fee", "contact", aContact.Id);

                // 收費單應收金額
                this.m_ToolUtilityClass.SetEntityMoneyAttribute(ref aFeeToCreated, "new_fee_shoud_pay", new Money(QpayModel.Amount));

                // 收費單實收金額
                this.m_ToolUtilityClass.SetEntityMoneyAttribute(ref aFeeToCreated, "new_fee_really_paid", new Money(0));

                // 收費單實現阿拉伯數字到大寫中文的轉換，金額轉為大寫金額
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aFeeToCreated, "new_big_chinese_number", MoneyToChinese(QpayModel.Amount.ToString()));

                // 收費單付款方式，預設是ATM轉帳
                this.m_ToolUtilityClass.SetOptionSetAttribute(ref aFeeToCreated, "new_pay_way", 100000002);

                // 帳戶後六碼
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aFeeToCreated, "new_last_six_digit", QpayModel.SerialNumber);

                // 收費單收費日期
                this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aFeeToCreated, "new_pay_date", QpayModel.DedicationDate.ToLocalTime());

                // 收入項目
                SetFeePayCategory(QpayModel.Category, ref aFeeToCreated);

                // 收入類別
                SetIncomeCategory(QpayModel.Category, ref aFeeToCreated);

                //會計科目
                //SetAccountingCode(QpayModel.Category, ref aFeeToCreated);

                // 收費單奉獻其他類別
                if (QpayModel.Category == "其他")
                {
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aFeeToCreated, "new_others", QpayModel.Others);
                }

                #endregion
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //Monitor.Exit(this);
                throw e;
            }
        }
        public void SetFeePayCategory(String Value, ref Entity aFeeEntity)
        {

            switch (Value)
            {
                case "月定獻金":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_category", 100000000);
                    break;
                case "感恩獻金":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_category", 100000002);
                    break;
                case "節期獻金":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_category", 100000001);
                    break;
                case "對內獻金":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_category", 100000003);
                    break;
                case "對外獻金":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_category", 100000004);
                    break;
                case "建築獻金":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_category", 100000006);
                    break;
                case "建築奉獻":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_category", 100000006);
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aFeeEntity, "new_others", "建築奉獻");
                    break;
                case "建堂認獻":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_category", 100000006);
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aFeeEntity, "new_others", "建堂認獻");
                    break;
                case "慈善獻金":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_category", 100000005);
                    break;
                case "獎學獻金":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_category", 100000009);
                    break;
                case "聖餐獻金":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_category", 100000017);
                    break;
                case "宣教獻金(聖餐)":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_category", 100000007);
                    break;
                case "禮拜獻金":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_category", 100000010);
                    break;
                case "利息收入":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_category", 100000011);
                    break;
                case "其他收入":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_category", 100000012);
                    break;
                case "借入款":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_category", 100000013);
                    break;
                case "補助款":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_category", 100000014);
                    break;
                case "專帳其他收入(利息)":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_category", 100000015);
                    break;
                case "特別獻金":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_category", 100000008);
                    break;
                default:
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_category", 100000000);
                    break;
            }
        }
        public void SetIncomeCategory(String Value, ref Entity aFeeEntity)
        {
            if (Value == "月定獻金" || Value == "禮拜獻金" || Value == "聖餐獻金" || Value == "節期獻金" || Value == "感恩獻金" || Value == "特別獻金" || Value == "利息收入" || Value == "對內獻金" || Value == "其他收入")
            { 
                this.m_ToolUtilityClass.SetEntityStringAttribute(aFeeEntity, "new_income_category", "經常費收入");
            }
            else
            {
                this.m_ToolUtilityClass.SetEntityStringAttribute(aFeeEntity, "new_income_category", "專帳收入");
            }
        }
        public void SetAccountingCode(String Value, ref Entity aFeeEntity)
        {

            switch (Value)
            {
                case "月定獻金":
                    this.m_ToolUtilityClass.SetEntityStringAttribute(aFeeEntity, "new_accounting_code", "4111100");
                    break;
                case "建堂奉獻":
                    this.m_ToolUtilityClass.SetEntityStringAttribute(aFeeEntity, "new_accounting_code", "4113100");
                    break;
                case "感恩獻金":
                    this.m_ToolUtilityClass.SetEntityStringAttribute(aFeeEntity, "new_accounting_code", "4112100");
                    break;
                case "其他奉獻":
                    this.m_ToolUtilityClass.SetEntityStringAttribute(aFeeEntity, "new_accounting_code", "4119100");
                    break;
                case "指定奉獻":
                    this.m_ToolUtilityClass.SetEntityStringAttribute(aFeeEntity, "new_accounting_code", "4115100");
                    break;
                case "宣教奉獻":
                    this.m_ToolUtilityClass.SetEntityStringAttribute(aFeeEntity, "new_accounting_code", "4114100");
                    break;
                case "慈惠奉獻":
                    this.m_ToolUtilityClass.SetEntityStringAttribute(aFeeEntity, "new_accounting_code", "4116100");
                    break;
                case "特別獻金":
                    this.m_ToolUtilityClass.SetEntityStringAttribute(aFeeEntity, "new_accounting_code", "4117100");
                    break;
                default:
                    this.m_ToolUtilityClass.SetEntityStringAttribute(aFeeEntity, "new_accounting_code", "4111100");
                    break;
            }
        }
        public void SetPayCategory(String Value, String AttributeName, ref Entity aFeeEntity)
        {

            switch (Value)
            {
                case "月定獻金":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, AttributeName, 100000000);
                    break;
                case "感恩獻金":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, AttributeName, 100000002);
                    break;
                case "節期獻金":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, AttributeName, 100000001);
                    break;
                case "對內獻金":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, AttributeName, 100000003);
                    break;
                case "對外獻金":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, AttributeName, 100000004);
                    break;
                case "建築獻金":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, AttributeName, 100000006);
                    break;
                case "建築奉獻":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, AttributeName, 100000006);
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aFeeEntity, "new_others", "建築奉獻");
                    break;
                case "建堂認獻":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, AttributeName, 100000006);
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aFeeEntity, "new_others", "建堂認獻");
                    break;
                case "慈善獻金":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, AttributeName, 100000005);
                    break;
                case "獎學獻金":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, AttributeName, 100000009);
                    break;
                case "聖餐獻金":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, AttributeName, 100000017);
                    break;
                case "宣教獻金(聖餐)":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, AttributeName, 100000007);
                    break;
                case "禮拜獻金":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, AttributeName, 100000010);
                    break;
                case "利息收入":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, AttributeName, 100000011);
                    break;
                case "其他收入":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, AttributeName, 100000012);
                    break;
                case "借入款":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, AttributeName, 100000013);
                    break;
                case "補助款":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, AttributeName, 100000014);
                    break;
                case "專帳其他收入(利息)":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, AttributeName, 100000015);
                    break;
                case "特別獻金":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, AttributeName, 100000008);
                    break;
                default:
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, AttributeName, 100000000);
                    break;
            }
        }
        public void UpdateFee(ref Entity aFeeToUpdate, String CardOrderNo, String AtmOrderNo, String AtmPayNo)
        {
            try
            {
                #region 更新收費單或是認獻單(因為欄位名稱一致)
                SetFeeParameter(aFeeToUpdate, CardOrderNo, AtmOrderNo, AtmPayNo);

                this.m_ToolUtilityClass.UpdateEntity(aFeeToUpdate);
                #endregion
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //Monitor.Exit(this);
                throw e;
            }
        }
        public void SetFeeParameter(Entity aFeeToCreated, String CardOrderNo, String AtmOrderNo, String AtmPayNo)
        {
            try
            {
                #region 設定更新收費單所需的參數

                // 永豐金流 QPay
                if (CardOrderNo != "")
                {
                    // 收費單付款方式
                    //this.m_ToolUtilityClass.SetOptionSetAttribute(ref aFeeToCreated, "new_pay_way", 100000001); // 100000001 = 信用卡

                    // 信用卡訂單編號
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aFeeToCreated, "new_q_pay_card_order_no", CardOrderNo);
                }
                if (AtmOrderNo != "")
                {
                    // 收費單付款方式
                    //this.m_ToolUtilityClass.SetOptionSetAttribute(ref aFeeToCreated, "new_pay_way", 100000002); // 100000002 = ATM轉帳/匯款

                    // 虛擬帳號訂單編號
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aFeeToCreated, "new_q_pay_order_atm_no", AtmOrderNo);
                    // 轉帳/匯款編號
                    String aAtmPayNumber = this.m_ToolUtilityClass.GetEntityStringAttribute(aFeeToCreated, "new_atm_pay_number") + DateTime.Now.ToString() + " = " + AtmOrderNo + " : " + AtmPayNo + Environment.NewLine;
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aFeeToCreated, "new_atm_pay_number", aAtmPayNumber);

                    // ATM轉帳匯款虛擬帳號
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aFeeToCreated, "new_atm_pay_no", AtmPayNo);

                    // ATM轉帳匯款到期日
                    this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aFeeToCreated, "new_atm_expire_date", DateTime.Now.AddDays(10).ToLocalTime());
                }

                #endregion
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //Monitor.Exit(this);
                throw e;
            }
        }
        public async Task<string> ProcessAtm(Guid aCreatedFeeId, Entity aFeeToUpdate, QpayModel QpayModel, String LineId, Entity LineLoginContact)
        {
            try
            {
                #region 建立收費單
                QpayModel.FullName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref LineLoginContact, "fullname");

                CreOrder CreatedAtmOrder = await CreateOrderATM(QpayModel.Amount, QpayModel.Category + "-" + QpayModel.FullName, DateTime.Now.ToString("yyyyMMddhhmmssfff"), aCreatedFeeId.ToString());

                // 用剛剛建立的收費單，填寫訂單編號
                UpdateFee(ref aFeeToUpdate, "", CreatedAtmOrder.OrderNo, CreatedAtmOrder.ATMParam.AtmPayNo);

                String AtmInfoToLine =
                        "姓名 : " + this.m_ToolUtilityClass.GetEntityStringAttribute(ref LineLoginContact, "fullname") + Environment.NewLine +
                        "名稱 : " + QpayModel.Category + Environment.NewLine +
                        "金額 : " + QpayModel.Amount + "元" + Environment.NewLine +
                        "付款到期日: " + DateTime.Now.AddDays(10).ToLocalTime().ToShortDateString() + Environment.NewLine +
                        "*** 請依照訊息付款 ***" + Environment.NewLine +
                        "銀行代碼 : 807 永豐商業銀行" + Environment.NewLine +
                        "分行代號 : 021 台北分行" + Environment.NewLine +
                        "帳號     : " + CreatedAtmOrder.ATMParam.AtmPayNo + Environment.NewLine +
                        "戶名     : 其他應付款-代收-網路收款";

                LineId = LineId != "" ? LineId : this.m_ToolUtilityClass.GetEntityStringAttribute(ref LineLoginContact, "new_lineid");
                await m_PushUtility.SendMessage(LineId, AtmInfoToLine);

                String AtmInfo =
                        "姓名 : " + this.m_ToolUtilityClass.GetEntityStringAttribute(ref LineLoginContact, "fullname") + "<br/>" +
                        "名稱 : " + QpayModel.Category + "<br/>" +
                        "金額 : " + QpayModel.Amount + "元" + "<br/>" +
                        "付款到期日: " + DateTime.Now.AddDays(10).ToLocalTime().ToShortDateString() + "<br/>" +
                        "*** 請依照訊息付款 ***" + "<br/>" +
                        "銀行代碼 : 807 永豐商業銀行" + "<br/>" +
                        "分行代號 : 021 台北分行" + "<br/>" +
                        "帳號     : " + CreatedAtmOrder.ATMParam.AtmPayNo + "<br/>" +
                        "戶名     : 其他應付款-代收-網路收款";
                return AtmInfo;
                #endregion

            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //Monitor.Exit(this);
                throw e;
            }
        }
        public async Task<string> SaveKeyInDedication(QpayModel QpayModel)
        {
            try
            {
                #region 非同步建立收費單
                Entity aContact = GetContact(QpayModel);
                if (aContact == null)
                {
                    return "錯誤:找不到會友!";
                }
                else
                {
                    Guid aCreatedFeeId = CreateFee(aContact, QpayModel, true);

                    // 奉獻感謝與通知
                    //SendGratitudeLineMessage(aContact, QpayModel);

                    //String Result = "感謝" + this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContact, "fullname") + "您的奉獻";
                    String Result = "上傳成功<br/>" +
                                    "--------------------" + "<br/>" +
                                    "日期    : " + QpayModel.DedicationDate.ToShortDateString() + "<br/>" +
                                    "姓名    : " + QpayModel.FullName + "<br/>" +
                                    "奉獻編號: " + this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContact, "pager") + "<br/>" +
                                    "身分證字號: " + this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContact, "new_personal_id") + "<br/>" +
                                    "電話    : " + QpayModel.Mobile + "<br/>" +
                                    "類別    : " + QpayModel.Category + "<br/>" +
                                    "奉獻地點: " + QpayModel.DedicateLocation + "<br/>" +
                                    "付款方式: " + QpayModel.PayWay + "<br/>" +
                                    "金額    : " + QpayModel.Amount + "<br/>" +
                                    "備註    : " + QpayModel.Explain + "<br/>"
                                    ;
                    return Result;
                }


                #endregion
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //Monitor.Exit(this);
                throw e;
            }
        }
        public Guid CreateFee(Entity aContact, QpayModel QpayModel, bool KeyinMode)
        {
            try
            {
                #region 建立收費單

                Entity aFeeToCreated = new Entity("new_fee");

                SetFeeParameter(aContact, aFeeToCreated, QpayModel, KeyinMode);

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
        public void SetFeeParameter(Entity aContact, Entity aFeeToCreated, QpayModel QpayModel, bool KeyinMode )
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

                // 收費單應收金額
                this.m_ToolUtilityClass.SetEntityMoneyAttribute(ref aFeeToCreated, "new_fee_shoud_pay", new Money(QpayModel.Amount));

                if (QpayModel.PayWay == "現金")
                {
                    // 收費單實收金額，如果付款方式是"現金"，就預設是足額實收，因為程式應該是跑行政人員收奉獻，所以就都表示已付款
                    this.m_ToolUtilityClass.SetEntityMoneyAttribute(ref aFeeToCreated, "new_fee_really_paid", new Money(QpayModel.Amount));

                    // 收費單付款狀態，預設是現金已繳費
                    SetPayStatus("現金已繳費", ref aFeeToCreated);
                }
                else if (QpayModel.PayWay == "銀行轉帳")
                {
                    // 收費單實收金額，如果付款方式是"現金"，就預設是足額實收，因為程式應該是跑行政人員收奉獻，所以就都表示已付款
                    this.m_ToolUtilityClass.SetEntityMoneyAttribute(ref aFeeToCreated, "new_fee_really_paid", new Money(QpayModel.Amount));

                    // 收費單付款狀態，銀行轉帳已繳費
                    SetPayStatus("銀行轉帳已繳費", ref aFeeToCreated);
                }
                else if (QpayModel.PayWay == "信用卡")
                {
                    if (KeyinMode == true) // 是否是會計輸入的
                    {
                        // 收費單實收金額，如果付款方式是"現金"，就預設是足額實收，因為程式應該是跑行政人員收奉獻，所以就都表示已付款
                        this.m_ToolUtilityClass.SetEntityMoneyAttribute(ref aFeeToCreated, "new_fee_really_paid", new Money(QpayModel.Amount));

                        // 收費單付款狀態，信用卡已繳費，這是會計輸入的，所以是"信用卡已繳費"
                        SetPayStatus("信用卡已繳費", ref aFeeToCreated);
                    }
                    else
                    {
                        // 收費單實收金額
                        this.m_ToolUtilityClass.SetEntityMoneyAttribute(ref aFeeToCreated, "new_fee_really_paid", new Money(0));

                        // 收費單付款狀態，信用卡已繳費，這是奉獻網頁建立的的，所以是"新建立"
                        SetPayStatus("新建立", ref aFeeToCreated);
                    }
                }
                else
                {
                    // 收費單實收金額
                    this.m_ToolUtilityClass.SetEntityMoneyAttribute(ref aFeeToCreated, "new_fee_really_paid", new Money(0));

                    // 收費單付款狀態，預設是新建立
                    SetPayStatus("新建立", ref aFeeToCreated);
                }
                // 收費單實收金額，因為程式應該是跑行政人員收奉獻，所以就都表示已付款
                //this.m_ToolUtilityClass.SetEntityMoneyAttribute(ref aFeeToCreated, "new_fee_really_paid", new Money(QpayModel.Amount));

                // 收費單實現阿拉伯數字到大寫中文的轉換，金額轉為大寫金額
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aFeeToCreated, "new_big_chinese_number", MoneyToChinese(QpayModel.Amount.ToString()));

                // 收費單付款方式，預設是現金
                SetPayMethod(QpayModel.PayWay, ref aFeeToCreated);

                // 帳戶後六碼
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aFeeToCreated, "new_last_six_digit", QpayModel.LastSixDigit);

                // 收費單收費日期
                this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aFeeToCreated, "new_pay_date", QpayModel.DedicationDate.ToLocalTime());

                // 奉獻類別
                SetFeePayCategory(QpayModel.Category, ref aFeeToCreated);

                // 收入類別
                SetIncomeCategory(QpayModel.Category, ref aFeeToCreated);

                //會計科目
                //SetAccountingCode(QpayModel.Category, ref aFeeToCreated);

                // 設定輸入奉獻人員
                if (m_LoginContact != null)
                {
                    this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aFeeToCreated, "new_keyin_contact_new_fee", "contact", this.m_LoginContact.Id);
                }

                // 收費單奉獻其他類別
                if (QpayModel.Others != "" && QpayModel.Others != null)
                {
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aFeeToCreated, "new_others", QpayModel.Others);
                }

                // 奉獻地點
                if (QpayModel.DedicateLocation != null)
                {
                    // 奉獻地點值不為NULL，所以應該是行政人員輸入而來的 parentcustomerid

                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aFeeToCreated, "new_dedicate_location", QpayModel.DedicateLocation);
                }
                else
                {
                    // 奉獻地點值為NULL，所以應該是信用卡或ATM、匯款而來的
                    // 奉獻地點就要依據連絡人所屬教會設定
                    // 取得連絡人所屬教會
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aFeeToCreated, "new_dedicate_location", this.m_ToolUtilityClass.GetEntityLookupDisplayName(ref aContact, "parentcustomerid"));
                }

                // 奉獻備註
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aFeeToCreated, "new_explain", QpayModel.Explain);

                #endregion
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //Monitor.Exit(this);
                throw e;
            }
        }
        public Guid CreateDedicationBooking(Entity aContact, QpayModel QpayModel)
        {
            try
            {
                #region 建立認獻單

                Entity aDedicationBookingToCreated = new Entity("new_dedication_booking");

                SetDedicationBookingParameter(aContact, aDedicationBookingToCreated, QpayModel);

                // 新增認獻單
                Guid aDedicationBookingId = this.m_ToolUtilityClass.CreateEntity(aDedicationBookingToCreated);
                Entity aRetrievedDedicationBooking = this.m_ToolUtilityClass.RetrieveEntity("new_dedication_booking", aDedicationBookingId);

                //指派負責人
                if (aRetrievedDedicationBooking != null && aContact != null)
                {
                    try
                    {
                        this.m_ToolUtilityClass.AssignOwner("new_dedication_booking", aRetrievedDedicationBooking, this.m_ToolUtilityClass.GetOwnerId(aContact));
                    }
                    catch (System.Exception e)
                    {
                        String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                    }
                }

                return aDedicationBookingId;
                #endregion
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //Monitor.Exit(this);
                throw e;
            }
        }
        public void SetDedicationBookingParameter(Entity aContact, Entity aDedicationBookingToCreated, QpayModel QpayModel)
        {
            try
            {
                #region 建立認獻單所需要的參數
                // 取得報名者的全名
                String FullName = "";
                FullName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContact, "fullname");

                // 認獻單名稱
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aDedicationBookingToCreated, "new_name", FullName + "奉獻");

                // 認獻單姓名關聯 LOOKUP
                this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aDedicationBookingToCreated, "new_contact_new_dedication_booking", "contact", aContact.Id);

                // 認獻單狀態 = 尚未啟動
                this.m_ToolUtilityClass.SetOptionSetAttribute(ref aDedicationBookingToCreated, "new_dedication_booking_status", 100000000);

                // 認獻單每期金額
                this.m_ToolUtilityClass.SetEntityMoneyAttribute(ref aDedicationBookingToCreated, "new_amount_per_stage", new Money(QpayModel.Amount));

                // 認獻單總期數
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aDedicationBookingToCreated, "new_total_stages", QpayModel.DeductTotalNumber);

                // 認獻單應收金額
                this.m_ToolUtilityClass.SetEntityMoneyAttribute(ref aDedicationBookingToCreated, "new_dedication_amount", new Money(QpayModel.Amount * TransferToDeductTotalNum(QpayModel.DeductTotalNumber)));

                // 認獻單開始日期
                this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aDedicationBookingToCreated, "new_dedication_start_date", DateTime.Now);

                // 認獻單結束日期
                this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aDedicationBookingToCreated, "new_dedication_end_date", DateTime.Now.AddMonths(TransferToDeductTotalNum(QpayModel.DeductTotalNumber)));

                // 奉獻類別
                SetPayCategory(QpayModel.Category, "new_dedication_category", ref aDedicationBookingToCreated);

                // 奉獻備註
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aDedicationBookingToCreated, "new_explain", QpayModel.Explain);

                #endregion
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //Monitor.Exit(this);
                throw e;
            }
        }
        public Entity GetContact(QpayModel QpayModel)
        {
            try
            {
                #region 非同步建立收費單
                if (QpayModel.DedicationNumber != "" && QpayModel.DedicationNumber != null)
                {
                    // 連絡人有奉獻編號
                    EntityCollection aContactEntityCollection = this.m_ToolUtilityClass.RetrieveEntityCollectionByField("contact", "pager", QpayModel.DedicationNumber);

                    foreach (Entity aContact in aContactEntityCollection.Entities)
                    {
                        // 有相同的奉獻編號
                        if (QpayModel.FullName == this.m_ToolUtilityClass.GetEntityStringAttribute(aContact, "fullname"))
                        {
                            // 透過姓名篩選出來
                            return aContact;
                        }
                    }

                    return null;
                }
                else if (QpayModel.FullName != "" && QpayModel.Mobile != "" && QpayModel.Mobile != null)
                {
                    // 連絡人沒有奉獻編號，但是有姓名及行動電話
                    Entity aRetrievedContact = this.m_ToolUtilityClass.RetrieveContactEntityByFullNameAndMobileNumber(QpayModel.FullName, QpayModel.Mobile);

                    if (aRetrievedContact != null)
                    {
                        return aRetrievedContact;
                    }
                    else
                    {
                        return this.m_ToolUtilityClass.RetrieveEntityByField("contact", "telephone2", QpayModel.Mobile);
                    }
                }
                else if (QpayModel.FullName != "")
                {
                    // 連絡人沒有奉獻編號及行動電話，但是有姓名
                    EntityCollection aContactEntitycollection = this.m_ToolUtilityClass.RetrieveContactEntityByFullNameCollection(QpayModel.FullName);

                    if (aContactEntitycollection.Entities.Count == 1)
                    {
                        // 不能有同名同姓
                        return aContactEntitycollection.Entities[0];
                    }
                    else
                    {
                        return null;
                    }
                }
                else { return null; }
                #endregion
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //Monitor.Exit(this);
                throw e;
            }
        }
        public void SetPayMethod(String Value, ref Entity aFeeEntity)
        {
            // 收費單付款狀態
            switch (Value)
            {
                case "未知":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_pay_way", 100000004);
                    break;
                case "現金":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_pay_way", 100000000);
                    break;
                case "信用卡":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_pay_way", 100000001);
                    break;
                case "ATM轉帳/匯款":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_pay_way", 100000002);
                    break;
                case "銀行轉帳":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_pay_way", 100000006);
                    break;
                case "超商付款":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_pay_way", 100000004);
                    break;
                case "行動支付":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_pay_way", 100000007);
                    break;
                case "銀聯卡":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_pay_way", 100000008);
                    break;
                case "LinePay":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_pay_way", 100000005);
                    break;
                default:
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_pay_way", 100000004);
                    break;

            }
        }
        public void SetPayStatus(String Value, ref Entity aFeeEntity)
        {

            switch (Value)
            {
                case "新建立":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_pay_status", 100000000);
                    break;
                case "信用卡已繳費":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_pay_status", 100000001);
                    break;
                case "ATM轉帳/匯款已繳費":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_pay_status", 100000002);
                    break;
                case "現金已繳費":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_pay_status", 100000003);
                    break;
                case "銀行轉帳已繳費":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_pay_status", 100000004);
                    break;
                default:
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_pay_status", 100000000);
                    break;

            }
        }
        public void SendGratitudeLineMessage(Entity aContact, QpayModel QpayModel)
        {
            try
            {
                #region 非同步建立收費單
                String LineId = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContact, "new_lineid");

                if (LineId != "")
                {
                    String GratitudeMessage =
                        "敬收 " + m_ToolUtilityClass.GetEntityStringAttribute(ref aContact, "fullname") + " 奉獻" + Environment.NewLine +
                        "日期 : " + QpayModel.DedicationDate.ToShortDateString() + Environment.NewLine +
                        "類別 : " + QpayModel.Category + "  " + QpayModel.Others + Environment.NewLine +
                        "付款方式: " + QpayModel.PayWay + Environment.NewLine +
                        "金額 : " + QpayModel.Amount;

                    m_PushUtility.SendMessage(LineId, GratitudeMessage);
                }

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
        #region 永豐金流工具區
        public async Task<CreOrder> CreOrderCard(int Amount, String ProductName, String OrderDate, String FeeId, String PayType, String PayTypeSub, String Staging, int DeductTotalNum, String PeriodType, int DeductFreq, String CCToken = null)
        {
            //設定參數
            CreOrderReq creOrderReq = new CreOrderReq()
            {
                ShopNo = m_ShopNo,
                OrderNo = PayType + OrderDate,
                Amount = Amount * 100,
                CurrencyID = "TWD",
                PrdtName = ProductName,
                ReturnURL = RETURN_URL,
                BackendURL = BACKEND_URL,
                PayType = PayType, //支付方式
                Param1 = FeeId,
                Param2 = QPAY_ORGANIZATION,
                CardParam = new CreOrderCardParamReq()
                {
                    AutoBilling = "Y",
                    PayTypeSub = PayTypeSub,
                    Staging = Staging,
                    DeductTotalNum = DeductTotalNum,
                    PeriodType = PeriodType,
                    DeductFreq = DeductFreq,
                    CCToken = CCToken
                }
            };

            CreOrder retObj = QPayToolkit.OrderCreate(creOrderReq);

            var Result = QPayCommon.SerializeToJson(retObj);

            return retObj;

        }
        public async Task<CreOrder> CreOrderCard(int Amount, String ProductName, String OrderDate, String FeeId, String PayType, String PayTypeSub, String Staging, int DeductTotalNum, String PeriodType, int DeductFreq, String CreditCategory, String CCToken = null)
        {
            //設定參數
            CreOrderReq creOrderReq = new CreOrderReq()
            {
                ShopNo = m_ShopNo,
                OrderNo = PayType + OrderDate,
                Amount = Amount * 100,
                CurrencyID = "TWD",
                PrdtName = ProductName,
                ReturnURL = RETURN_URL,
                BackendURL = BACKEND_URL,
                PayType = PayType, //支付方式
                Param1 = FeeId,
                Param2 = QPAY_ORGANIZATION,
                Param3 = CreditCategory,
                CardParam = new CreOrderCardParamReq()
                {
                    AutoBilling = "Y",
                    PayTypeSub = PayTypeSub,
                    Staging = Staging,
                    DeductTotalNum = DeductTotalNum,
                    PeriodType = PeriodType,
                    DeductFreq = DeductFreq,
                    CCToken = CCToken
                }
            };

            CreOrder retObj = QPayToolkit.OrderCreate(creOrderReq);

            var Result = QPayCommon.SerializeToJson(retObj);

            return retObj;

        }
        public async Task<CreOrder> CreateOrderATM(int Amount, String ProductName, String OrderDate, String FeeId)
        {
            //設定參數
            //設定參數
            CreOrderReq creOrderReq = new CreOrderReq()
            {
                ShopNo = m_ShopNo,
                OrderNo = "A" + OrderDate,
                Amount = Amount * 100,
                CurrencyID = "TWD",
                PrdtName = ProductName,
                ReturnURL = RETURN_URL,
                BackendURL = BACKEND_URL,
                PayType = "A",
                Param1 = FeeId,
                Param2 = QPAY_ORGANIZATION,
                Param3 = "收費單",
                ATMParam = new CreOrderATMParamReq()
                {
                    ExpireDate = DateTime.Now.AddDays(10).ToLocalTime().ToString("yyyyMMdd")
                }
            };

            return QPayToolkit.OrderCreate(creOrderReq);

        }
        public async Task<QryOrder> OrderQuery(String aOrderNo)
        {
            QryOrderReq orderQueryReq = new QryOrderReq()
            {
                ShopNo = m_ShopNo,
                OrderNo = aOrderNo
            };

            QryOrder retObj = QPayToolkit.OrderQuery(orderQueryReq);

            return retObj;
        }
        public QryOrderPay OrderPayQuery(String aPayToken)
        {
            QryOrderPayReq orderPayQueryReq = new QryOrderPayReq()
            {
                ShopNo = m_ShopNo,
                PayToken = aPayToken
            };

            QryOrderPay retObj = QPayToolkit.OrderPayQuery(orderPayQueryReq);

            return retObj;
        }
        public QryOrderPay OrderPayQuery(String aShopNo, String aPayToken)
        {
            QryOrderPayReq orderPayQueryReq = new QryOrderPayReq()
            {
                ShopNo = aShopNo,
                PayToken = aPayToken
            };

            QryOrderPay retObj = QPayToolkit.OrderPayQuery(orderPayQueryReq, ConvertShopNoToHashCodeAndSite(aShopNo));

            return retObj;
        }
        public async Task<QryBill> BillQuery(String aPayDate)
        {
            QryBillReq billQueryReq = new QryBillReq()
            {
                ShopNo = m_ShopNo,
                BillDate = aPayDate
            };

            QryBill retObj = QPayToolkit.BillQuery(billQueryReq);

            //ltResponse.Text = QPayCommon.SerializeToJson(retObj);
            return retObj;
        }
        public async Task<QryAllot> AllotQuery(String aAllotDateS, String aAllotDateE, String aPayType)
        {
            QryAllotReq allotQueryReq = new QryAllotReq()
            {
                ShopNo = m_ShopNo,
                AllotDateS = aAllotDateS,
                AllotDateE = aAllotDateE,
                PayType = aPayType
            };

            QryAllot retObj = QPayToolkit.AllotQuery(allotQueryReq);

            //ltResponse.Text = QPayCommon.SerializeToJson(retObj);
            return retObj;
        }
        public async Task<QryOrderUnCaptured> OrderUnCapturedQuery()
        {
            QryOrderUnCapturedReq orderUnCapturedReq = new QryOrderUnCapturedReq()
            {
                ShopNo = m_ShopNo
            };

            QryOrderUnCaptured retObj = QPayToolkit.OrderUnCapturedQuery(orderUnCapturedReq);

            //ltResponse.Text = QPayCommon.SerializeToJson(retObj);
            return retObj;
        }
        public async Task<OrderMaintain> OrderMaintain(String aOrderNo, String aCommand)
        {
            OrderMaintainReq orderMaintainReq = new OrderMaintainReq()
            {
                ShopNo = m_ShopNo,
                OrderNo = aOrderNo,
                Command = aCommand
            };

            OrderMaintain retObj = QPayToolkit.OrderMaintain(orderMaintainReq);

            //ltResponse.Text = QPayCommon.SerializeToJson(retObj);
            return retObj;
        }
        public String GetLastCCToken(Entity aContact)
        {
            #region// 取得連絡人信用卡資訊

            String VisaInfo = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContact, "new_visa_info");

            if (VisaInfo != "")
            {
                String[] VisaInfoSplit = VisaInfo.Split('|');

                if (VisaInfoSplit.Length > 0)
                {
                    String[] VisaCCTokenSplit = VisaInfoSplit[VisaInfoSplit.Length - 1].Split('，');

                    if (VisaCCTokenSplit.Length > 0)
                    {
                        return VisaCCTokenSplit[0];
                    }
                    else
                    {
                        return null;
                    }
                }
                else
                {
                    return null;
                }

            }
            else
            {
                return null;
            }
            #endregion
        }
        #endregion
        #region 永豐金流工具區
        private string ConvertShopNoToHashCodeAndSite(String aShopNo)
        {
            String A21 = "";
            String A22 = "";
            String B21 = "";
            String B22 = "";

            //客製化
            switch (aShopNo)
            {
                case "DA1626_001":
                    // 永和禮拜堂"板橋民族分行"
                    return "D1695F439A69448F,7E460E920A184845,DEA83EFB714943F3,DC237C5C69914F0C";
                case "DA1626_003":
                    // 永和禮拜堂"永和分行"
                    return "2C5D55945FCF4767,76052054D7054EA6,13F282F8A0F5475D,D782B4F1893A4334";
                case "DA2424_001":
                    // iM行動教會
                    return "9825732578154B95,C89A75CD59D0430F,DAB73CB2A41E47FF,B09695CE58FA4774";
                case "DA2659_001":
                    // 台北得勝靈糧堂
                    return "C8DAEA50FFB64CF4,F141E5BBE21B4D47,A922E0C106D14C35,CA22A88D1032412F";
                case "NA0149_001":
                    // 音訊教會 SandBox
                    return "5E854757C751413F,D743D0EB06904837,08169D5445644513,8E52B5A180EE4399";
                case "DA2890_001":
                    // 忠孝路長老教會
                    return "BDC962CCC8AB4AE2,946D46DBDDDE43E0,6038DFB03B4342AE,B1F64046CB2E44FC";
                case "DA3033_001":
                    // 東湖禮拜堂
                    return "4B1657DE6F3547A3,3AB478872D0A49C7,0748F400DD834C07,6506CD86B0174396";
                case "DA3190_001":
                    // 楊梅靈糧堂
                    String A11 = "1E582BECE43F421A";
                    String A12 = "8F6ACB29B8EF4C67";
                    String B11 = "8C06D1D49C544C51";
                    String B12 = "041D9136AA9647F2";
                    return A11 + "," + A12 + "," + B11 + "," + B12;
                case "DA3189_001":
                    // 以利亞之家
                    A21 = "A88FB80292D6420D";
                    A22 = "3844DD3B214D487C";
                    B21 = "27BC1983D2914C11";
                    B22 = "32D5A23910734C93";
                    return A21 + "," + A22 + "," + B21 + "," + B22;
                case "DA3412_001":
                    // 南崁長老教會
                    A21 = "2B27264C1D794727";
                    A22 = "7C91CB903482427D";
                    B21 = "7360D573A5A34184";
                    B22 = "3C85541425624385";
                    return A21 + "," + A22 + "," + B21 + "," + B22;
                case "DA3806_001":
                    // 好消息協會
                    A21 = "81F5DAFEAFD343EC";
                    A22 = "80BA10061E59467B";
                    B21 = "B5F2CBA592004D2D";
                    B22 = "D6D805E2CF514E12";
                    return A21 + "," + A22 + "," + B21 + "," + B22;
                case "DA3855_002":
                    // 法國號靈糧堂
                    A21 = "08B9715C313F4ABB";
                    A22 = "E8AC362AB9174D3C";
                    B21 = "81D71D28D7E04414";
                    B22 = "927ADFBE9F854C81";
                    return A21 + "," + A22 + "," + B21 + "," + B22;
                case "DA4001_001":
                    // 社團法人台灣基督教天母豐盛協會
                    A21 = "B2FC3849C9F6487C";
                    A22 = "6ADDD7D7CCFC48BA";
                    B21 = "2F83CE17C6044E3D";
                    B22 = "48737E77D6864915";
                    return A21 + "," + A22 + "," + B21 + "," + B22;
                case "DA4195_001":
                    // 南崁基督長老教會
                    A21 = "B83DCBFA2D994F19";
                    A22 = "6ED32787DA504871";
                    B21 = "13E56D7A39AB4768";
                    B22 = "163EC08BC1624854";
                    return A21 + "," + A22 + "," + B21 + "," + B22;
                default:
                    return "5E854757C751413F,D743D0EB06904837,08169D5445644513,8E52B5A180EE4399";
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
