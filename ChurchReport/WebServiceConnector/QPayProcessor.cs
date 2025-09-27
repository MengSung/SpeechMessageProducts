using ChurchReport.Models;
using ChurchReport.Tools;
using Line.Messaging;
using Line.Pay;
using Line.Pay;
using Line.Pay.Models;
using Line.Pay.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk;
using QPay.Domain;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Dynamic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using ToolUtilityNameSpace;
using UserProfile = Line.Messaging.UserProfile;

namespace ChurchReport.WebServiceConnector
{
    public class QPayProcessor
    {
        #region 資料區
        #region 設定與配置
        private static ConfigurationBuilder m_ConfigurationBuilder = (ConfigurationBuilder)new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory()).AddJsonFile("appsettings.json");
        private static IConfiguration m_Configuration = m_ConfigurationBuilder.Build();
        #endregion

        #region 商店設定
        // 商店編號
        // SANDBOX 測試用
        //string m_ShopNo = "NA0149_001";
        private string m_ShopNo = m_Configuration["Sandbox:ShopNo"];

        // 永豐金流正式環境
        //string m_ShopNo = "DA4195_001";
        //private string m_ShopNo = m_Configuration["Sinopac:ShopNo"];
        #endregion

        #region 環境設定
        #region 公司內部開發
        // 使用 ChurchReport 當作 WebHook
        //private const String RETURN_URL = "https://nankanchurchback.speechmessage.com.tw:480/api/QPayCard/QPayReturnUrl";
        //private const String BACKEND_URL = "http://QPbackendback.speechmessage.com.tw/api/QPayAtm/QPayBackendUrl";// 公司內部開發
        #endregion
        #region 雲端機房
        //private const String RETURN_URL = "https://nankanchurchback.speechmessage.com.tw:335/api/QPayCard/QPayReturnUrl";
        //private const String BACKEND_URL = "http://QPaybackend.speechmessage.com.tw/api/QPayAtm/QPayBackendUrl"; // 雲端機房
        #endregion

        private readonly String RETURN_URL = m_Configuration["RETURN_URL"];
        private readonly String BACKEND_URL = m_Configuration["BACKEND_URL"];// 公司內部開發
        #endregion

        #region LINE Bot 設定
        // 聖谷行道會
        private const String CHANNEL_ACCESS_TOKEN = @"OMjL23DpFRDgphgN7JdzA7uCpv1wb4hXtsGh4FzxP8tHzeMyYOr/ry3BBqaRNJpVUhR6wPHLN4Wa4QiG5i3P5T/Y07swP5OjfCz9DKwTYC7T4mPb8x54pwtcqK1lIdgNm6skdZnu99fBsupEcbZLBAdB04t89/1O/w1cDnyilFU=";

        //private LinePayClient m_LinePayClient { get; }

        private LineMessagingClient m_LineMessagingClient { get; }
        private PushUtility m_PushUtility { get; }
        private ReplyUtility m_ReplyUtility { get; }

        //private LineNotifyUtility m_LineNotifyUtility = new LineNotifyUtility();
        #endregion

        #region 工具與服務
        ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass("DYNAMICS365");
        IPayment m_PaymentService;
        #endregion

        #region 業務資料
        //private String m_LocalCardOrderNo = "";
        //private String m_LocalAtmOrderNo = "";
        //private DateTime m_AtmExpireDate;

        // 登入的連絡人
        public Entity m_LoginContact;
        #endregion

        #region 客製化設定
        // 客製化
        private readonly String QPAY_ORGANIZATION = m_Configuration["QPAY_ORGANIZATION"];
        #endregion
        #endregion
        #region 初始化
        public QPayProcessor(IPayment aPaymentService)
        {
            this.m_LineMessagingClient = new LineMessagingClient(CHANNEL_ACCESS_TOKEN);

            // 客製化
            m_PushUtility = new PushUtility(m_LineMessagingClient);
            m_ReplyUtility = new ReplyUtility(m_LineMessagingClient);

            m_PaymentService = aPaymentService;
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
                        CreatedCardOrder = await CreOrderCard(QpayModel.Amount, QpayModel.Category + "-" + QpayModel.FullName, DateTime.Now.ToString("yyyyMMddhhmmssfff"), aCreatedFeeId.ToString(), "C", "ONE", "", 0, "M", 1, "收費單", LineLoginContact, QpayModel.SelectedCreditCard);
                    }
                    else
                    {
                        // 銀聯卡
                        CreatedCardOrder = await CreOrderCard(QpayModel.Amount, QpayModel.Category + "-" + QpayModel.FullName, DateTime.Now.ToString("yyyyMMddhhmmssfff"), aCreatedFeeId.ToString(), "C", "CUP", "", 0, "M", 1, "收費單", LineLoginContact, QpayModel.SelectedCreditCard);
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

                    CreOrder CreatedCardOrder = await CreOrderCard(QpayModel.Amount, QpayModel.Category + "-" + QpayModel.FullName, DateTime.Now.ToString("yyyyMMddhhmmssfff"), aCreatedDedicationBookingId.ToString(), "C", "REGULAR", "", TransferToDeductTotalNum(QpayModel.DeductTotalNumber), "M", 1, "認獻單", LineLoginContact, QpayModel.SelectedCreditCard);

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

                    CreOrder CreatedCardOrder = await CreOrderCard(QpayModel.Amount, QpayModel.Category + "-" + QpayModel.FullName, DateTime.Now.ToString("yyyyMMddhhmmssfff"), aCreatedFeeId.ToString(), "M", "ONE", "", 0, "M", 1, "收費單", LineLoginContact, QpayModel.SelectedCreditCard);

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

                    CreOrder CreatedCardOrder = await CreOrderCard(QpayModel.Amount, QpayModel.Category + "-" + QpayModel.FullName, DateTime.Now.ToString("yyyyMMddhhmmssfff"), aCreatedFeeId.ToString(), "L", "ONE", "", 0, "M", 1, "收費單", LineLoginContact, QpayModel.SelectedCreditCard);

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

                // 收費單奉獻其他類別
                if (QpayModel.Category == "其他")
                {
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aFeeToCreated, "new_others", QpayModel.Others);
                }

                // 收入類別
                SetIncomeCategory(QpayModel.Category, ref aFeeToCreated);

                //會計科目
                //SetAccountingCode(QpayModel.Category, ref aFeeToCreated);


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
                case "對外獻金-本宗":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_category", 100000004);
                    break;
                case "對外獻金-非本宗":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_category", 100000022);
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
                case "宣教基金":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_category", 100000007);
                    break;
                case "週年獻金":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_category", 100000023);
                    break;
                case "松年大學":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_category", 100000018);
                    break;
                case "友愛基金":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_category", 100000019);
                    break;
                case "生日助學金":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_category", 100000020);
                    break;
                case "青年宣教基金":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_category", 100000021);
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
                case "補助金":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_category", 100000014);
                    break;
                case "專帳其他收入(利息)":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_category", 100000015);
                    break;
                case "特別獻金":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_category", 100000008);
                    break;
                case "償還借款準備金":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_category", 100000016);
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
                if (Value != "特別獻金")
                {
                    // 不是特別獻金，則是經常費收入
                    this.m_ToolUtilityClass.SetEntityStringAttribute(aFeeEntity, "new_income_category", "經常費收入");
                }
                else
                {
                    // 處理特別獻金
                    if (this.m_ToolUtilityClass.GetEntityStringAttribute(aFeeEntity, "new_others") != "")
                     {
                        // 奉獻其他類別有資料則是專帳收入
                        this.m_ToolUtilityClass.SetEntityStringAttribute(aFeeEntity, "new_income_category", "專帳收入");
                    }
                    else
                    {
                        // 奉獻其他類別有資料則是經常費收入
                        this.m_ToolUtilityClass.SetEntityStringAttribute(aFeeEntity, "new_income_category", "經常費收入");
                    }
                }
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
                case "對外獻金-本宗":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, AttributeName, 100000004);
                    break;
                case "對外獻金-非本宗":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, AttributeName, 100000022);
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
                case "宣教基金":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, AttributeName, 100000007);
                    break;
                case "松年大學":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, AttributeName, 100000018);
                    break;
                case "友愛基金":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, AttributeName, 100000019);
                    break;
                case "生日助學金":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, AttributeName, 100000020);
                    break;
                case "青年宣教基金":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, AttributeName, 100000021);
                    break;
                case "週年獻金":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_category", 100000023);
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
                case "補助金":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, AttributeName, 100000014);
                    break;
                case "專帳其他收入(利息)":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, AttributeName, 100000015);
                    break;
                case "特別獻金":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, AttributeName, 100000008);
                    break;
                case "償還借款準備金":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, AttributeName, 100000016);
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

                // 收費單奉獻其他類別
                if (QpayModel.Others != "" && QpayModel.Others != null)
                {
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aFeeToCreated, "new_others", QpayModel.Others);
                }

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

                // 週報專用備註
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aFeeToCreated, "new_weekly_note", QpayModel.WeeklyNote);
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
                else if ( QpayModel.FullName != "" && QpayModel.FullName != null )
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
        public async Task<CreOrder> CreOrderCard(int Amount, String ProductName, String OrderDate, String FeeId, String PayType, String PayTypeSub, String Staging, int DeductTotalNum, String PeriodType, int DeductFreq, String CreditCategory, Entity LineLoginContact, String CCToken = null)
        {
            if (m_Configuration["PAY_PROVIDER"] == "永豐金流")
            {
                // 永豐金流
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

                CreOrder retObj = m_PaymentService.OrderCreate(creOrderReq);
                return retObj;
            }
            else if (m_Configuration["PAY_PROVIDER"] == "高鉅金流")
            {
                //高鉅金流
                CreOrder aRetObj = m_PaymentService.CreateOrder(GetRawData(Amount, ProductName, OrderDate, FeeId, PayType, PayTypeSub, LineLoginContact), GetService());

                //高鉅金流
                var Result = QPayCommon.SerializeToJson(aRetObj);

                return aRetObj;
            }
            else if (m_Configuration["PAY_PROVIDER"] == "台新金流")
            {
                //高鉅金流
                CreOrder aRetObj = m_PaymentService.CreateOrder(GetRawData(Amount, ProductName, OrderDate, FeeId, PayType, PayTypeSub, LineLoginContact), GetService());

                //高鉅金流
                var Result = QPayCommon.SerializeToJson(aRetObj);

                return aRetObj;
            }
            else
            {
                // 高鉅金流
                CreOrder aRetObj = m_PaymentService.CreateOrder(GetRawData(Amount, ProductName, OrderDate, FeeId, PayType, PayTypeSub, LineLoginContact), GetService());

                // 高鉅金流
                var Result = QPayCommon.SerializeToJson(aRetObj);

                return aRetObj;
            }

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

            return m_PaymentService.OrderCreate(creOrderReq);

        }
        public async Task<QryOrder> OrderQuery(String aOrderNo)
        {
            QryOrderReq orderQueryReq = new QryOrderReq()
            {
                ShopNo = m_ShopNo,
                OrderNo = aOrderNo
            };

            QryOrder retObj = m_PaymentService.OrderQuery(orderQueryReq);

            return retObj;
        }
        public QryOrderPay OrderPayQuery(String aPayToken)
        {
            QryOrderPayReq orderPayQueryReq = new QryOrderPayReq()
            {
                ShopNo = m_ShopNo,
                PayToken = aPayToken
            };

            QryOrderPay retObj = m_PaymentService.OrderPayQuery(orderPayQueryReq);

            return retObj;
        }
        public QryOrderPay OrderPayQuery(String aShopNo, String aPayToken)
        {
            QryOrderPayReq orderPayQueryReq = new QryOrderPayReq()
            {
                ShopNo = aShopNo,
                PayToken = aPayToken
            };

            QryOrderPay retObj = m_PaymentService.OrderPayQuery(orderPayQueryReq, ConvertShopNoToHashCodeAndSite(aShopNo));

            return retObj;
        }
        public async Task<QryBill> BillQuery(String aPayDate)
        {
            QryBillReq billQueryReq = new QryBillReq()
            {
                ShopNo = m_ShopNo,
                BillDate = aPayDate
            };

            QryBill retObj = m_PaymentService.BillQuery(billQueryReq);

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

            QryAllot retObj = m_PaymentService.AllotQuery(allotQueryReq);

            //ltResponse.Text = QPayCommon.SerializeToJson(retObj);
            return retObj;
        }
        public async Task<QryOrderUnCaptured> OrderUnCapturedQuery()
        {
            QryOrderUnCapturedReq orderUnCapturedReq = new QryOrderUnCapturedReq()
            {
                ShopNo = m_ShopNo
            };

            QryOrderUnCaptured retObj = m_PaymentService.OrderUnCapturedQuery(orderUnCapturedReq);

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

            OrderMaintain retObj = m_PaymentService.OrderMaintain(orderMaintainReq);

            //ltResponse.Text = QPayCommon.SerializeToJson(retObj);
            return retObj;
        }
        public String GetLastCCToken(Entity aContact)
        {
            #region 取得連絡人信用卡資訊

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
        #region 高鉅金流工具區
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
                case "DA4272_001":
                    // 聖谷行道會
                    A21 = "00DC1BDACCB645C6";
                    A22 = "185B6F59F737462E";
                    B21 = "6F9C2936E8524F76";
                    B22 = "8BB48C2260304E29";
                    return A21 + "," + A22 + "," + B21 + "," + B22;
                default:
                    return "5E854757C751413F,D743D0EB06904837,08169D5445644513,8E52B5A180EE4399";
            }
        }
        #endregion
        #region 高鉅金流工具區
        /// <summary>
        /// 取得高鉅金流付款所需的原始資料
        /// </summary>
        /// <param name="Amount">付款金額</param>
        /// <param name="ProductName">商品名稱</param>
        /// <param name="OrderDate">訂單日期</param>
        /// <param name="FeeId">收費單ID</param>
        /// <param name="PayType">付款類型 (C:信用卡, A:ATM, M:行動支付, L:LinePay)</param>
        /// <param name="PayTypeSub">付款子類型 (ONE:一次付清, REGULAR:定期定額, CUP:銀聯卡)</param>
        /// <returns>包含付款資訊的動態物件</returns>
        private dynamic GetRawData(int Amount, String ProductName, String OrderDate, String FeeId, String PayType, String PayTypeSub, Entity LineLoginContact)
        {
            // 建立商品項目清單 - 使用 ProductItem 強型別
            ArrayList items = CreateProductItems(FeeId, ProductName, Amount);

            //// 建立產品項目陣列，包含多種奉獻類型
            //// 每個ProductItem包含：id(產品編號)、name(奉獻類型名稱)、cost(單價)、amount(數量)
            //ProductItem[] products = 
            //{
            //    // 月定獻金：產品編號001，單價1000元，數量1
            //    new ProductItem { id = "001", name = "月定獻金", cost = 1000, amount = 1 },
            //    // 感恩獻金：產品編號002，單價500元，數量2
            //    new ProductItem { id = "002", name = "感恩獻金", cost = 500, amount = 2 }
            //};

            //// 呼叫CreateMultipleProductItems方法，將產品陣列轉換為ArrayList格式
            //// 此方法會計算每個產品的總價(cost * amount)並建立完整的商品清單
            //ArrayList items = CreateMultipleProductItems(products);

            // 建立付款原始資料物件
            dynamic rawData = new ExpandoObject();

            // 設定原始資料屬性
            SetRawDataProperties(rawData, Amount, FeeId, items, LineLoginContact);

            return rawData;
        }


        /// <summary>
        /// 建立產品項目清單 - 使用 ProductItem 強型別
        /// </summary>
        /// <param name="FeeId">收費單ID</param>
        /// <param name="ProductName">商品名稱</param>
        /// <param name="Amount">商品單價與總價</param>
        /// <param name="imageUrl">商品圖片連結(可選，主要用於LINE Pay)</param>
        /// <returns>包含產品項目的ArrayList</returns>
        private ArrayList CreateProductItems(String FeeId, String ProductName, int Amount, String imageUrl = null)
        {
            // 建立商品項目清單
            ArrayList items = new ArrayList();

            // 建立產品項目 - 使用強型別 ProductItem
            ProductItem productItem = new ProductItem
            {
                id = FeeId,           // 商品ID使用收費單ID
                name = ProductName,   // 商品名稱
                cost = Amount,        // 商品單價
                amount = 1,           // 商品數量固定為1
                total = Amount,       // 商品總價 (單價 * 數量)
                image_url = imageUrl  // 商品圖片連結(可選)
            };

            // 將商品項目加入清單
            items.Add(productItem);

            return items;
        }

        /// <summary>
        /// 建立多項產品項目清單
        /// </summary>
        /// <param name="products">產品項目陣列</param>
        /// <returns>包含多個產品項目的ArrayList</returns>
        private ArrayList CreateMultipleProductItems(ProductItem[] products)
        {
            ArrayList items = new ArrayList();

            foreach (ProductItem product in products)
            {
                // 確保總價計算正確
                product.total = product.cost * product.amount;
                items.Add(product);
            }

            return items;
        }
        /// <summary>
        /// 設定付款原始資料的屬性
        /// </summary>
        /// <param name="rawData">付款原始資料物件</param>
        /// <param name="Amount">付款金額</param>
        /// <param name="FeeId">收費單ID</param>
        /// <param name="items">商品項目清單</param>
        //private void SetRawDataProperties(dynamic rawData, int Amount, String FeeId, ArrayList items)
        //{
        //    // 設定商店代號 - 從設定檔取得MyPay商店ID
        //    rawData.store_uid = m_Configuration["MyPay:Store_Id"];
        //    // 設定商品清單
        //    rawData.items = items;
        //    // 設定總金額
        //    rawData.cost = Amount;
        //    // 設定使用者ID (目前固定為"胡夢嵩")
        //    rawData.user_id = "胡夢嵩";
        //    // 設定訂單編號 (使用收費單ID)
        //    rawData.order_id = FeeId;
        //    // 設定消費者IP位址 - 從設定檔取得，用於驗證
        //    rawData.ip = m_Configuration["MyPay:IP"];  // 此為消費者IP，會做為驗證用
        //    // 設定付款流程編號 (0表示一般付款流程)
        //    rawData.pfn = "0";
        //}

        private void SetRawDataProperties(dynamic rawData, int Amount, String FeeId, ArrayList items, Entity LineLoginContact)
        {
            #region 組織單位
            rawData.echo_0 = m_Configuration["QPAY_ORGANIZATION"];
            #endregion

            #region 基本商店資訊
            // 設定商店代號 - 從設定檔取得MyPay商店ID
            rawData.store_uid = m_Configuration["MyPay:Store_Id"];
            #endregion

            #region 消費者資訊
            // 設定使用者ID 
            rawData.user_id = LineLoginContact.Id;

            // 消費者姓名，電子錢包交易必要欄位
            // 使用 m_ToolUtilityClass 從實際登入的連絡人取得姓名
            rawData.user_name = this.m_ToolUtilityClass.GetEntityStringAttribute(ref LineLoginContact, "fullname")
                               ?? m_Configuration["MyPay:UserName"]
                               ?? "預設使用者";

            // 消費者真實姓名，電子錢包交易必要欄位
            // 優先使用連絡人的真實姓名，如無則使用設定檔
            rawData.user_real_name = this.m_ToolUtilityClass.GetEntityStringAttribute(ref LineLoginContact, "fullname")
                                   ?? m_Configuration["MyPay:UserRealName"]
                                   ?? "胡夢嵵";

            // 消費者郵遞區號 - 從連絡人地址取得
            rawData.user_zipcode = this.m_ToolUtilityClass.GetEntityStringAttribute(ref LineLoginContact, "address1_postalcode")
                                 ?? m_Configuration["MyPay:UserZipcode"]
                                 ?? "";

            // 消費者帳單地址 - 從連絡人地址取得
            String address1_line1 = this.m_ToolUtilityClass.GetEntityStringAttribute(ref LineLoginContact, "address1_line1") ?? "";
            String address1_line2 = this.m_ToolUtilityClass.GetEntityStringAttribute(ref LineLoginContact, "address1_line2") ?? "";
            String address1_line3 = this.m_ToolUtilityClass.GetEntityStringAttribute(ref LineLoginContact, "address1_line3") ?? "";
            String fullAddress = (address1_line1 + address1_line2 + address1_line3).Trim();
            rawData.user_address = !String.IsNullOrEmpty(fullAddress) ? fullAddress
                                 : m_Configuration["MyPay:UserAddress"] ?? "";

            // 證號類型 - 從設定檔取得
            rawData.user_sn_type = m_Configuration["MyPay:UserSnType"] ?? "";

            // 付款人身分證/統一證號/護照號碼 - 從連絡人取得
            rawData.user_sn = this.m_ToolUtilityClass.GetEntityStringAttribute(ref LineLoginContact, "new_personal_id")
                            ?? m_Configuration["MyPay:UserSn"]
                            ?? "";

            // 消費者家用電話 - 從連絡人取得
            rawData.user_phone = this.m_ToolUtilityClass.GetEntityStringAttribute(ref LineLoginContact, "telephone1")
                                ?? m_Configuration["MyPay:UserPhone"]
                                ?? "";

            // 消費者行動電話國碼，電子錢包交易必要欄位
            rawData.user_cellphone_code = m_Configuration["MyPay:UserCellphoneCode"] ?? "886";

            // 消費者行動電話，電子錢包交易必要欄位 - 從連絡人取得
            rawData.user_cellphone = this.m_ToolUtilityClass.GetEntityStringAttribute(ref LineLoginContact, "mobilephone")
                                    ?? this.m_ToolUtilityClass.GetEntityStringAttribute(ref LineLoginContact, "telephone2")
                                    ?? m_Configuration["MyPay:UserCellphone"]
                                    ?? "";

            // 消費者 E-Mail，電子錢包交易必要欄位 - 從連絡人取得
            rawData.user_email = this.m_ToolUtilityClass.GetEntityStringAttribute(ref LineLoginContact, "emailaddress1")
                                ?? m_Configuration["MyPay:UserEmail"]
                                ?? "";

            // 消費者生日 - 從連絡人取得
            try
            {
                DateTime birthDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(ref LineLoginContact, "birthdate");
                rawData.user_birthday = birthDate != DateTime.MinValue ? birthDate.ToString("yyyy-MM-dd")
                                       : m_Configuration["MyPay:UserBirthday"] ?? "";
            }
            catch
            {
                rawData.user_birthday = m_Configuration["MyPay:UserBirthday"] ?? "";
            }
            #endregion

            #region 訂單資訊
            // 設定總金額
            rawData.cost = Amount;
            // 預設交易幣別(預設為TWD新台幣)
            rawData.currency = m_Configuration["MyPay:Currency"] ?? "TWD";
            // 啟用dcc(自動換匯)
            rawData.enable_dcc = Convert.ToInt32(m_Configuration["MyPay:EnableDcc"] ?? "0");
            // 設定訂單編號 (使用收費單ID)
            rawData.order_id = FeeId;
            // 設定消費者IP位址 - 從設定檔取得，用於驗證
            rawData.ip = m_Configuration["MyPay:IP"];  // 此為消費者IP，會做為驗證用

            // 訂單內物品數
            rawData.item = items.Count.ToString();

            // 設定商品清單
            rawData.items = items;
            #endregion

            #region 付款設定
            // 設定付款流程編號 (0表示一般付款流程)
            rawData.pfn = "0";
            // 消費者操作介面類型 pc/app
            rawData.interface_type = m_Configuration["MyPay:InterfaceType"] ?? "app";
            // 折價金額 (預設0)
            rawData.discount = m_Configuration["MyPay:Discount"] ?? "0";
            // 交易成功導頁網址 - 高鉅金流會在此網址顯示成功頁面給用戶
            rawData.success_returl = m_Configuration["MyPay:SuccessReturl"] ?? "";
            // 交易失敗導頁網址 - 高鉅金流會在此網址顯示失敗頁面給用戶
            rawData.failure_returl = m_Configuration["MyPay:FailureReturl"] ?? "";
            // 高鉅金流後端回調網址 - 用於接收交易完成回傳資訊
            //rawData.notify_url = "https://sunnyvalechback.speechmessage.com.tw:8888/api/MyPay/return";
            rawData.notify_url = m_Configuration["MyPay:ReturnUrl"] ?? "";
            // 虛擬帳號與超商代碼使用之有效天數
            rawData.limit_pay_days = Convert.ToInt32(m_Configuration["MyPay:LimitPayDays"] ?? "7");
            // 運費
            rawData.shipping_fee = m_Configuration["MyPay:ShippingFee"] ?? "0";
            #endregion

            #region 發票設定 (可選)
            // 發票設定 (可選)
            if (m_Configuration.GetSection("MyPay:Invoice").Exists())
            {
                rawData.issue_invoice_state = m_Configuration["MyPay:Invoice:IssueInvoiceState"]; // 開立發票
                rawData.invoice_input_type = m_Configuration["MyPay:Invoice:InvoiceInputType"]; // 電子發票開立類型
                rawData.invoice_tax_id = m_Configuration["MyPay:Invoice:InvoiceTaxId"]; // 統一編號
                rawData.invoice_love_code = m_Configuration["MyPay:Invoice:InvoiceLoveCode"]; // 愛心碼
                rawData.invoice_b2b_title = m_Configuration["MyPay:Invoice:InvoiceB2bTitle"]; // 發票抬頭
                rawData.invoice_b2b_id = m_Configuration["MyPay:Invoice:InvoiceB2bId"]; // 統一編號
                rawData.invoice_b2b_address = m_Configuration["MyPay:Invoice:InvoiceB2bAddress"]; // 發票地址
            }
            #endregion
        }
        /// <summary>
        /// 取得高鉅金流服務請求設定
        /// </summary>
        /// <returns>包含服務名稱和指令的ServiceRequest物件</returns>
        private ServiceRequest GetService()
        {
            // 建立服務請求物件
            ServiceRequest rawData = new ServiceRequest();
            // 設定服務名稱 - 從設定檔取得
            rawData.service_name = m_Configuration["MyPay:ServiceName"];
            // 設定API指令 - 從設定檔取得
            rawData.cmd = m_Configuration["MyPay:CMD"];

            return rawData;
        }
        #endregion
        #region 高鉅金流 PayPage 回傳處理
        /// <summary>
        /// 驗證高鉅金流回傳的 Hash 簽名
        /// </summary>
        /// <param name="returnModel">回傳資料</param>
        /// <returns>驗證結果</returns>
        public bool VerifyMyPayHash(MyPayReturnModel returnModel)
        {
            try
            {
                string key = m_Configuration["MyPay:Key"];
                string iv = m_Configuration["MyPay:IV"];

                if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(iv))
                {
                    String ErrorString = $"ERROR: MyPay Key 或 IV 設定為空 - {DateTime.Now}";
                    return false;
                }

                // 根據高鉅金流文檔的簽名計算規則
                // 簽名組合：KEY + transaction_id + order_id + state + IV
                string rawData = $"{key}{returnModel.transaction_id}{returnModel.order_id}{returnModel.state}{iv}";

                // 使用 SHA256 計算 Hash
                using (SHA256 sha256 = SHA256.Create())
                {
                    byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(rawData));
                    StringBuilder hashBuilder = new StringBuilder();

                    foreach (byte b in bytes)
                    {
                        hashBuilder.Append(b.ToString("x2"));
                    }

                    string calculatedHash = hashBuilder.ToString().ToUpper();
                    return calculatedHash.Equals(returnModel.hash, StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (Exception ex)
            {
                String ErrorString = $"ERROR: VerifyMyPayHash - {DateTime.Now} - {ex}";
                return false;
            }
        }

        /// <summary>
        /// 處理高鉅金流回傳資訊並更新 Dynamics 365
        /// </summary>
        /// <param name="returnModel">回傳資料</param>
        /// <returns>處理結果</returns>
        public async Task<bool> ProcessMyPayReturn(MyPayReturnModel returnModel)
        {
            try
            {
                // 嘗試解析 order_id 成 Guid
                if (!Guid.TryParse(returnModel.order_id, out Guid entityId))
                {
                    String ErrorString = $"ERROR: 無法解析 order_id 為 Guid: {returnModel.order_id}";
                    return false;
                }

                // 先查詢收費單
                Entity entity = this.m_ToolUtilityClass.RetrieveEntity("new_fee", entityId);
                string entityType = "new_fee";

                // 如果找不到收費單，嘗試查詢認獻單
                if (entity == null)
                {
                    entity = this.m_ToolUtilityClass.RetrieveEntity("new_dedication_booking", entityId);
                    entityType = "new_dedication_booking";

                    if (entity == null)
                    {
                        String ErrorString = $"ERROR: 找不到對應的收費單或認獻單: {returnModel.order_id}";
                        return false;
                    }
                }

                // 檢查是否已處理過此交易 (冪等性處理)
                string existingTransactionId = this.m_ToolUtilityClass.GetEntityStringAttribute(entity, "new_mypay_transaction_id");
                if (!string.IsNullOrEmpty(existingTransactionId) && existingTransactionId == returnModel.transaction_id)
                {
                    // 已處理過此交易，直接回傳成功
                    return true;
                }

                // 記錄交易ID (用於避免重複處理)
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref entity, "new_mypay_transaction_id", returnModel.transaction_id);

                // 根據交易結果進行不同處理
                if (returnModel.retmsg == "付款完成") // 交易成功
                {
                    await ProcessSuccessfulMyPayReturn(entity, entityType, returnModel);
                }
                else // 交易失敗
                {
                    await ProcessFailedMyPayReturn(entity, entityType, returnModel);
                }

                // 更新實體到 Dynamics 365
                this.m_ToolUtilityClass.UpdateEntity(entity);

                // 發送LINE通知 (可選)
                await SendMyPaymentNotification(entity, entityType, returnModel);

                return true;
            }
            catch (Exception ex)
            {
                String ErrorString = $"ERROR: ProcessMyPayReturn - {DateTime.Now} - {ex}";
                return false;
            }
        }

        /// <summary>
        /// 處理高鉅金流付款成功的情況 - 完整實現所有 MyPayReturnModel 參數
        /// </summary>
        /// <param name="entity">要更新的實體</param>
        /// <param name="entityType">實體類型</param>
        /// <param name="returnModel">回傳資料</param>
        private async Task ProcessSuccessfulMyPayReturn(Entity entity, string entityType, MyPayReturnModel returnModel)
        {
            try
            {
                // 處理完整的 MyPayReturnModel 參數
                var processingResult = returnModel.ProcessAllReturnFields();

                if (entityType == "new_fee")
                {
                    // 處理收費單付款成功
                    await ProcessSuccessfulFeePayment(entity, returnModel, processingResult);
                }
                else if (entityType == "new_dedication_booking")
                {
                    // 處理認獻單付款成功
                    await ProcessSuccessfulDedicationBookingPayment(entity, returnModel, processingResult);
                }

                // 更新共同的交易資訊
                UpdateCommonTransactionInfo(entity, returnModel, processingResult);

                // 記錄詳細處理結果
                RecordDetailedProcessingResult(entity, returnModel, processingResult);

            }
            catch (Exception ex)
            {
                string errorMessage = $"ProcessSuccessfulMyPayReturn 錯誤: {ex.Message}";
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref entity, "new_explain", 
                    $"{this.m_ToolUtilityClass.GetEntityStringAttribute(entity, "new_explain")}\n{errorMessage}");
                
                String ErrorString = $"ERROR: {GetType().FullName} - {DateTime.Now} - {errorMessage}";
                throw new Exception(errorMessage, ex);
            }
        }

        /// <summary>
        /// 處理收費單付款成功的具體邏輯
        /// </summary>
        private async Task ProcessSuccessfulFeePayment(Entity entity, MyPayReturnModel returnModel, MyPayProcessingResult processingResult)
        {
            // 1. 更新付款狀態
            if (!string.IsNullOrEmpty(returnModel.pay_type))
            {
                // 根據付款方式設定狀態
                switch (returnModel.pay_type.ToLower())
                {
                    case "credit":
                    case "creditcard":
                        SetPayStatus("信用卡已繳費", ref entity);
                        break;
                    case "atm":
                    case "virtual_account":
                        SetPayStatus("ATM轉帳/匯款已繳費", ref entity);
                        break;
                    case "cvs":
                    case "convenience_store":
                        SetPayStatus("超商已繳費", ref entity);
                        break;
                    default:
                        SetPayStatus("信用卡已繳費", ref entity); // 預設
                        break;
                }
            }
            else
            {
                SetPayStatus("信用卡已繳費", ref entity);
            }

            // 2. 更新實收金額
            if (processingResult.TransactionInfo.ParsedCost.HasValue)
            {
                this.m_ToolUtilityClass.SetEntityMoneyAttribute(ref entity, "new_fee_really_paid", 
                    new Money(processingResult.TransactionInfo.ParsedCost.Value));
            }
            else if (processingResult.TransactionInfo.ParsedActualCost.HasValue)
            {
                this.m_ToolUtilityClass.SetEntityMoneyAttribute(ref entity, "new_fee_really_paid", 
                    new Money(processingResult.TransactionInfo.ParsedActualCost.Value));
            }
            else
            {
                // 如果回傳金額都沒有，使用應收金額
                Money shouldPay = this.m_ToolUtilityClass.GetEntityMoneyAttribute(entity, "new_fee_shoud_pay");
                if (shouldPay != null && shouldPay.Value > 0)
                {
                    this.m_ToolUtilityClass.SetEntityMoneyAttribute(ref entity, "new_fee_really_paid", shouldPay);
                }
            }

            // 3. 更新付款日期
            if (processingResult.TransactionInfo.ParsedFinishTime.HasValue)
            {
                this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref entity, "new_pay_date", 
                    processingResult.TransactionInfo.ParsedFinishTime.Value);
            }
            else
            {
                this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref entity, "new_pay_date", DateTime.Now.ToLocalTime());
            }

            // 4. 處理信用卡相關資訊
            if (!string.IsNullOrEmpty(processingResult.CreditCardInfo.CardNo))
            {
                string cardInfo = $"卡號後四碼: ****{processingResult.CreditCardInfo.CardNo}";
                if (!string.IsNullOrEmpty(processingResult.CreditCardInfo.CardType))
                {
                    cardInfo += $"\n卡別: {processingResult.CreditCardInfo.CardType}";
                }
                if (!string.IsNullOrEmpty(processingResult.CreditCardInfo.IssuingBank))
                {
                    cardInfo += $"\n發卡行: {processingResult.CreditCardInfo.IssuingBank}";
                }
                if (!string.IsNullOrEmpty(processingResult.CreditCardInfo.AuthCode))
                {
                    cardInfo += $"\n授權碼: {processingResult.CreditCardInfo.AuthCode}";
                }
                
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref entity, "new_mypay_card_info", cardInfo);
            }

            // 5. 處理分期資訊
            if (!string.IsNullOrEmpty(processingResult.CreditCardInfo.InstallmentInfo))
            {
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref entity, "new_installment_info", 
                    processingResult.CreditCardInfo.InstallmentInfo);
            }

            // 6. 處理紅利資訊
            if (!string.IsNullOrEmpty(processingResult.CreditCardInfo.RedeemInfo))
            {
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref entity, "new_redeem_info", 
                    processingResult.CreditCardInfo.RedeemInfo);
            }

            // 7. 處理虛擬帳號資訊
            if (!string.IsNullOrEmpty(processingResult.VirtualAccountInfo.BankId))
            {
                string atmInfo = $"銀行代碼: {processingResult.VirtualAccountInfo.BankId}";
                if (processingResult.VirtualAccountInfo.ParsedExpiredDate.HasValue)
                {
                    atmInfo += $"\n到期日: {processingResult.VirtualAccountInfo.ParsedExpiredDate.Value:yyyy-MM-dd}";
                }
                if (!string.IsNullOrEmpty(processingResult.VirtualAccountInfo.ResultContent))
                {
                    atmInfo += $"\n帳號資訊: {processingResult.VirtualAccountInfo.ResultContent}";
                }
                
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref entity, "new_virtual_account_info", atmInfo);
            }
        }

        /// <summary>
        /// 處理認獻單付款成功的具體邏輯
        /// </summary>
        private async Task ProcessSuccessfulDedicationBookingPayment(Entity entity, MyPayReturnModel returnModel, MyPayProcessingResult processingResult)
        {
            // 1. 認獻單狀態設為已啟動
            this.m_ToolUtilityClass.SetOptionSetAttribute(ref entity, "new_dedication_booking_status", 100000001); // 已啟動

            // 2. 處理定期定額相關資訊
            if (!string.IsNullOrEmpty(processingResult.RecurringInfo.GroupId))
            {
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref entity, "new_recurring_group_id", 
                    processingResult.RecurringInfo.GroupId);
            }

            if (!string.IsNullOrEmpty(processingResult.RecurringInfo.PaymentName))
            {
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref entity, "new_recurring_payment_name", 
                    processingResult.RecurringInfo.PaymentName);
            }

            if (!string.IsNullOrEmpty(processingResult.RecurringInfo.NumberOfInstallments))
            {
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref entity, "new_recurring_periods", 
                    processingResult.RecurringInfo.NumberOfInstallments);
            }

            // 3. 更新認獻單開始日期
            if (processingResult.TransactionInfo.ParsedFinishTime.HasValue)
            {
                this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref entity, "new_dedication_start_date", 
                    processingResult.TransactionInfo.ParsedFinishTime.Value);
            }
        }

        /// <summary>
        /// 更新共同的交易資訊
        /// </summary>
        private void UpdateCommonTransactionInfo(Entity entity, MyPayReturnModel returnModel, MyPayProcessingResult processingResult)
        {
            // 1. 記錄核心交易資訊
            string coreTransactionInfo = $"高鉅金流核心交易資訊:\n";
            if (!string.IsNullOrEmpty(processingResult.CoreFields.Uid))
            {
                coreTransactionInfo += $"Payment Hub 流水號: {processingResult.CoreFields.Uid}\n";
            }
            if (!string.IsNullOrEmpty(processingResult.CoreFields.Prc))
            {
                coreTransactionInfo += $"主要回傳碼: {processingResult.CoreFields.Prc}\n";
            }
            if (!string.IsNullOrEmpty(returnModel.transaction_id))
            {
                coreTransactionInfo += $"平台交易號: {returnModel.transaction_id}\n";
            }
            
            this.m_ToolUtilityClass.SetEntityStringAttribute(ref entity, "new_mypay_core_info", coreTransactionInfo);

            // 2. 記錄交易服務資訊
            if (processingResult.ServiceInfo.SupplierName != null || processingResult.ServiceInfo.SupplierCode != null)
            {
                string serviceInfo = "金融服務商資訊:\n";
                if (!string.IsNullOrEmpty(processingResult.ServiceInfo.SupplierName))
                {
                    serviceInfo += $"服務商名稱: {processingResult.ServiceInfo.SupplierName}\n";
                }
                if (!string.IsNullOrEmpty(processingResult.ServiceInfo.SupplierCode))
                {
                    serviceInfo += $"服務商代碼: {processingResult.ServiceInfo.SupplierCode}\n";
                }
                if (processingResult.ServiceInfo.TransactionMode.HasValue)
                {
                    serviceInfo += $"交易服務類型: {processingResult.ServiceInfo.TransactionMode.Value}\n";
                }
                if (processingResult.ServiceInfo.IsAgentCharge.HasValue)
                {
                    serviceInfo += $"經銷商代收費: {(processingResult.ServiceInfo.IsAgentCharge.Value == 1 ? "是" : "否")}\n";
                }
                
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref entity, "new_mypay_service_info", serviceInfo);
            }

            // 3. 記錄消費者資訊
            if (!string.IsNullOrEmpty(processingResult.ConsumerInfo.UserId) || 
                !string.IsNullOrEmpty(processingResult.ConsumerInfo.UserName))
            {
                string consumerInfo = "消費者資訊:\n";
                if (!string.IsNullOrEmpty(processingResult.ConsumerInfo.UserId))
                {
                    consumerInfo += $"用戶ID: {processingResult.ConsumerInfo.UserId}\n";
                }
                if (!string.IsNullOrEmpty(processingResult.ConsumerInfo.UserName))
                {
                    consumerInfo += $"用戶姓名: {processingResult.ConsumerInfo.UserName}\n";
                }
                if (!string.IsNullOrEmpty(processingResult.ConsumerInfo.UserRealName))
                {
                    consumerInfo += $"真實姓名: {processingResult.ConsumerInfo.UserRealName}\n";
                }
                if (!string.IsNullOrEmpty(processingResult.ConsumerInfo.UserPhone))
                {
                    consumerInfo += $"聯絡電話: {processingResult.ConsumerInfo.UserPhone}\n";
                }
                if (!string.IsNullOrEmpty(processingResult.ConsumerInfo.UserEmail))
                {
                    consumerInfo += $"電子郵件: {processingResult.ConsumerInfo.UserEmail}\n";
                }
                
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref entity, "new_mypay_consumer_info", consumerInfo);
            }

            // 4. 處理自訂參數
            if (processingResult.CustomParameters.Echo0 != null || 
                processingResult.CustomParameters.Echo1 != null ||
                processingResult.CustomParameters.Echo2 != null ||
                processingResult.CustomParameters.Echo3 != null ||
                processingResult.CustomParameters.Echo4 != null)
            {
                string customParams = "自訂回傳參數:\n";
                if (!string.IsNullOrEmpty(processingResult.CustomParameters.Echo0))
                {
                    customParams += $"Echo0 (組織): {processingResult.CustomParameters.Echo0}\n";
                }
                if (!string.IsNullOrEmpty(processingResult.CustomParameters.Echo1))
                {
                    customParams += $"Echo1: {processingResult.CustomParameters.Echo1}\n";
                }
                if (!string.IsNullOrEmpty(processingResult.CustomParameters.Echo2))
                {
                    customParams += $"Echo2: {processingResult.CustomParameters.Echo2}\n";
                }
                if (!string.IsNullOrEmpty(processingResult.CustomParameters.Echo3))
                {
                    customParams += $"Echo3: {processingResult.CustomParameters.Echo3}\n";
                }
                if (!string.IsNullOrEmpty(processingResult.CustomParameters.Echo4))
                {
                    customParams += $"Echo4: {processingResult.CustomParameters.Echo4}\n";
                }
                
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref entity, "new_mypay_custom_params", customParams);
            }

            // 5. 處理發票資訊
            if (!string.IsNullOrEmpty(returnModel.invoice_number))
            {
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref entity, "new_invoice_number", returnModel.invoice_number);
            }

            // 6. 記錄交易完整詳情
            string fullTransactionDetails = $"高鉅金流完整交易詳情 [{DateTime.Now:yyyy-MM-dd HH:mm:ss}]:\n";
            fullTransactionDetails += $"訂單ID: {returnModel.order_id}\n";
            fullTransactionDetails += $"商店代號: {returnModel.store_uid}\n";
            fullTransactionDetails += $"交易狀態: {(returnModel.state == "1" ? "成功" : "失敗")}\n";
            
            if (processingResult.TransactionInfo.ParsedCost.HasValue)
            {
                fullTransactionDetails += $"交易金額: {processingResult.TransactionInfo.ParsedCost.Value:C}\n";
            }
            
            if (!string.IsNullOrEmpty(processingResult.TransactionInfo.Currency))
            {
                fullTransactionDetails += $"交易幣別: {processingResult.TransactionInfo.Currency}\n";
            }
            
            if (processingResult.TransactionInfo.ParsedFinishTime.HasValue)
            {
                fullTransactionDetails += $"完成時間: {processingResult.TransactionInfo.ParsedFinishTime.Value:yyyy-MM-dd HH:mm:ss}\n";
            }
            
            if (!string.IsNullOrEmpty(processingResult.TransactionInfo.PaymentMethod))
            {
                fullTransactionDetails += $"付費方法: {processingResult.TransactionInfo.PaymentMethod}\n";
            }
            
            if (processingResult.TransactionInfo.TransactionType.HasValue)
            {
                fullTransactionDetails += $"交易類型: {processingResult.TransactionInfo.TransactionType.Value}\n";
            }
            
            fullTransactionDetails += $"回傳訊息: {returnModel.msg}\n";
            
            this.m_ToolUtilityClass.SetEntityStringAttribute(ref entity, "new_mypay_transaction_details", fullTransactionDetails);
        }

        /// <summary>
        /// 記錄詳細的處理結果
        /// </summary>
        private void RecordDetailedProcessingResult(Entity entity, MyPayReturnModel returnModel, MyPayProcessingResult processingResult)
        {
            // 更新備註，記錄處理摘要
            string currentNote = this.m_ToolUtilityClass.GetEntityStringAttribute(entity, "new_explain") ?? "";
            string newNote = $"{currentNote}\n{processingResult.Summary}";

            // 如果處理有錯誤，也記錄下來
            if (!processingResult.IsSuccess && !string.IsNullOrEmpty(processingResult.ErrorMessage))
            {
                newNote += $"\n處理錯誤: {processingResult.ErrorMessage}\n";
            }

            this.m_ToolUtilityClass.SetEntityStringAttribute(ref entity, "new_explain", newNote);

            // 記錄原始回傳的 JSON 資料 (用於除錯和稽核)
            try
            {
                var allFields = returnModel.GetAllFieldsDictionary();
                var jsonData = Newtonsoft.Json.JsonConvert.SerializeObject(allFields, Newtonsoft.Json.Formatting.Indented);
                //this.m_ToolUtilityClass.SetEntityStringAttribute(ref entity, "new_mypay_raw_data", jsonData);
            }
            catch (Exception ex)
            {
                // JSON 序列化失敗也不影響主流程
                string jsonError = $"JSON 序列化失敗: {ex.Message}";
                //this.m_ToolUtilityClass.SetEntityStringAttribute(ref entity, "new_mypay_raw_data", jsonError);
            }

            // 驗證所有必要欄位
            var validationResult = returnModel.ValidateAllFields();
            if (!validationResult.IsValid)
            {
                string validationErrors = "欄位驗證錯誤:\n" + string.Join("\n", validationResult.Errors);
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref entity, "new_mypay_validation_errors", validationErrors);
            }
        }

        /// <summary>
        /// 處理高鉅金流付款失敗的情況
        /// </summary>
        /// <param name="entity">要更新的實體</param>
        /// <param name="entityType">實體類型</param>
        /// <param name="returnModel">回傳資料</param>
        private async Task ProcessFailedMyPayReturn(Entity entity, string entityType, MyPayReturnModel returnModel)
        {
            try
            {
                // 處理失敗的 MyPayReturnModel 參數
                var processingResult = returnModel.ProcessAllReturnFields();

                // 更新備註，記錄失敗原因
                string currentNote = this.m_ToolUtilityClass.GetEntityStringAttribute(entity, "new_explain") ?? "";
                string newNote = $"{currentNote}\n[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 高鉅金流付款失敗\n" +
                               $"交易號: {returnModel.transaction_id}\n" +
                               $"失敗原因: {returnModel.msg}\n";

                // 如果有詳細的錯誤信息，也一併記錄
                if (!string.IsNullOrEmpty(returnModel.prc))
                {
                    newNote += $"主要回傳碼: {returnModel.prc}\n";
                }

                if (processingResult.TransactionInfo?.ReturnMessage != null)
                {
                    newNote += $"詳細訊息: {processingResult.TransactionInfo.ReturnMessage}\n";
                }

                this.m_ToolUtilityClass.SetEntityStringAttribute(ref entity, "new_explain", newNote);

                // 如果是認獻單，設為啟動失敗
                if (entityType == "new_dedication_booking")
                {
                    this.m_ToolUtilityClass.SetOptionSetAttribute(ref entity, "new_dedication_booking_status", 100000003); // 啟動失敗
                }

                // 記錄失敗的原始資料
                try
                {
                    var allFields = returnModel.GetAllFieldsDictionary();
                    var jsonData = Newtonsoft.Json.JsonConvert.SerializeObject(allFields, Newtonsoft.Json.Formatting.Indented);
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref entity, "new_mypay_failed_data", jsonData);
                }
                catch (Exception ex)
                {
                    string jsonError = $"JSON 序列化失敗: {ex.Message}";
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref entity, "new_mypay_failed_data", jsonError);
                }
            }
            catch (Exception ex)
            {
                string errorMessage = $"ProcessFailedMyPayReturn 錯誤: {ex.Message}";
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref entity, "new_explain", 
                    $"{this.m_ToolUtilityClass.GetEntityStringAttribute(entity, "new_explain")}\n{errorMessage}");
                
                String ErrorString = $"ERROR: {GetType().FullName} - {DateTime.Now} - {errorMessage}";
                throw new Exception(errorMessage, ex);
            }
        }

        /// <summary>
        /// 發送高鉅金流付款結果通知
        /// </summary>
        /// <param name="entity">實體</param>
        /// <param name="entityType">實體類型</param>
        /// <param name="returnModel">回傳資料</param>
        private async Task SendMyPaymentNotification(Entity entity, string entityType, MyPayReturnModel returnModel)
        {
            try
            {
                // 取得關聯聯絡人
                Guid contactId = Guid.Empty;

                if (entityType == "new_fee")
                {
                    contactId = this.m_ToolUtilityClass.GetEntityLookupAttribute(entity, "new_contact_new_fee");
                }
                else if (entityType == "new_dedication_booking")
                {
                    contactId = this.m_ToolUtilityClass.GetEntityLookupAttribute(entity, "new_contact_new_dedication_booking");
                }

                if (contactId != Guid.Empty)
                {
                    Entity contact = this.m_ToolUtilityClass.RetrieveEntity("contact", contactId);
                    if (contact != null)
                    {
                        string lineId = this.m_ToolUtilityClass.GetEntityStringAttribute(contact, "new_lineid");

                        if (!string.IsNullOrEmpty(lineId))
                        {
                            string contactName = this.m_ToolUtilityClass.GetEntityStringAttribute(contact, "fullname");
                            string message;

                            if (returnModel.retmsg == "付款完成")
                            {
                                // 付款成功訊息
                                message = $"親愛的 {contactName}，您好！\n\n" +
                                          $"您的奉獻已經成功完成！\n" +
                                          $"🎉 交易成功 🎉\n\n" +
                                          $"📋 交易詳情:\n" +
                                          $"• 交易號: {returnModel.transaction_id}\n";

                                // 處理金額顯示
                                if (!string.IsNullOrEmpty(returnModel.cost) && decimal.TryParse(returnModel.cost, out decimal amount))
                                {
                                    message += $"• 金額: {amount:C}\n";
                                }
                                else if (!string.IsNullOrEmpty(returnModel.actual_cost) && decimal.TryParse(returnModel.actual_cost, out decimal actualAmount))
                                {
                                    message += $"• 金額: {actualAmount:C}\n";
                                }

                                // 處理付款方式
                                if (!string.IsNullOrEmpty(returnModel.pay_type))
                                {
                                    message += $"• 付款方式: {returnModel.pay_type}\n";
                                }

                                // 處理完成時間
                                if (!string.IsNullOrEmpty(returnModel.finishtime) && returnModel.finishtime.Length == 14)
                                {
                                    if (DateTime.TryParseExact(returnModel.finishtime, "yyyyMMddHHmmss", 
                                        System.Globalization.CultureInfo.InvariantCulture, 
                                        System.Globalization.DateTimeStyles.None, out DateTime finishTime))
                                    {
                                        message += $"• 完成時間: {finishTime:yyyy-MM-dd HH:mm:ss}\n";
                                    }
                                }

                                message += $"\n🙏 感謝您的奉獻！\n" +
                                          $"願上帝賜福給您！";

                                // 如果是定期定額，加入特別說明
                                if (entityType == "new_dedication_booking")
                                {
                                    message += $"\n\n📅 定期定額扣款已成功啟動";
                                    if (!string.IsNullOrEmpty(returnModel.group_id))
                                    {
                                        message += $"\n扣款群組ID: {returnModel.group_id}";
                                    }
                                }
                            }
                            else
                            {
                                // 付款失敗訊息
                                message = $"親愛的 {contactName}，您好！\n\n" +
                                         $"很抱歉，您的奉獻交易處理失敗。\n" +
                                         $"❌ 交易失敗 ❌\n\n" +
                                         $"📋 失敗原因:\n" +
                                         $"• {returnModel.msg}\n";

                                if (!string.IsNullOrEmpty(returnModel.transaction_id))
                                {
                                    message += $"• 交易號: {returnModel.transaction_id}\n";
                                }

                                message += $"\n🔧 請嘗試以下解決方案:\n" +
                                          $"1. 檢查網路連線是否穩定\n" +
                                          $"2. 確認信用卡資訊是否正確\n" +
                                          $"3. 聯繫您的發卡銀行確認\n" +
                                          $"4. 稍後再試或使用其他付款方式\n\n" +
                                          $"📞 如需協助，請聯繫教會辦公室\n" +
                                          $"我們將竭誠為您服務！";
                            }

                            // 發送 LINE 訊息
                            await m_PushUtility.SendMessage(lineId, message);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // 推送失敗不影響主流程，只記錄錯誤
                String ErrorString = $"ERROR: SendMyPaymentNotification - {DateTime.Now} - {ex}";
                // 可以選擇記錄到日誌或資料庫，但不拋出異常
            }
        }
        #endregion // 關閉 高鉅金流 PayPage 回傳處理 區域
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

        /// <summary>
        /// 轉換定期定額總期數字串為數字
        /// </summary>
        /// <param name="DeductTotalNumber">定期定額總期數字串</param>
        /// <returns>轉換後的數字</returns>
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
