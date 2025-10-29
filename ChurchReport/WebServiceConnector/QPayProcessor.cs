using ChurchReport.Models;
using ChurchReport.Tools;
using Line.Messaging;
using Line.Pay;
using Line.Pay.Models;
using Microsoft.Extensions.Configuration;
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
                case "主日奉獻":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_category", 100000010);
                    break;
                case "十一奉獻":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_category", 100000000);
                    break;
                case "感恩奉獻":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_category", 100000002);
                    break;
                case "建堂奉獻":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_category", 100000006);
                    break;
                case "宣教奉獻":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_category", 100000007);
                    break;
                case "愛心奉獻":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_category", 100000019);
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
            if (Value == "十一奉獻" || Value == "主日奉獻" || Value == "聖餐獻金" || Value == "節期獻金" || Value == "感恩奉獻" || Value == "特別獻金" || Value == "利息收入" || Value == "對內獻金" || Value == "其他收入")
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
                case "十一奉獻":
                    this.m_ToolUtilityClass.SetEntityStringAttribute(aFeeEntity, "new_accounting_code", "4111100");
                    break;
                case "建堂奉獻":
                    this.m_ToolUtilityClass.SetEntityStringAttribute(aFeeEntity, "new_accounting_code", "4113100");
                    break;
                case "感恩奉獻":
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
                case "主日奉獻":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_category", 100000010);
                    break;
                case "十一奉獻":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_category", 100000000);
                    break;
                case "感恩奉獻":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_category", 100000002);
                    break;
                case "建堂奉獻":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_category", 100000006);
                    break;
                case "宣教奉獻":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_category", 100000007);
                    break;
                case "愛心奉獻":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_category", 100000019);
                    break;
                case "特別獻金":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_category", 100000008);
                    break;
                default:
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_category", 100000000);
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
        
        /// <summary>
        /// 將 PayPageResponse 轉換為 CreOrder
        /// 用於統一台新金流(TSPG)與其他金流系統的回傳格式
        /// </summary>
        /// <param name="payPageResponse">PayPageResponse 金流回應</param>
        /// <param name="payType">付款類型 (C=信用卡, A=ATM, M=行動支付, L=LinePay)</param>
        /// <param name="orderNo">訂單編號 (如果 PayPageResponse 沒有提供，則使用此值)</param>
        /// <returns>CreOrder 統一格式</returns>
        private CreOrder ConvertPayPageResponseToCreOrder(PayPageResponse payPageResponse, string payType = "C", string orderNo = null)
        {
            try
            {
                if (payPageResponse == null)
                {
                    return new CreOrder
                    {
                        OrderNo = orderNo ?? string.Empty,
                        Status = "F",
                        Description = "PayPageResponse 為 null",
                        CardParam = new CreOrderCardParamRes
                        {
                            CardPayURL = GetErrorPageUrl("系統錯誤", "金流回應為空值，請稍後再試或聯繫客服")
                        },
                        ATMParam = null,
                        MobileParam = null
                    };
                }

                // 判斷交易是否成功
                // TSPG: code="0000" 表示成功
                // 永豐: Status="S" 表示成功
                bool isSuccess = payPageResponse.code == "0000" || payPageResponse.code == "00";

                // TODO:因為尚未上線，目前暫時無法使用信用卡支付，所以還原為null
                payPageResponse.url = null;

                string status = isSuccess ? "S" : "F";
                
                // 建立基本的 CreOrder 物件
                var creOrder = new CreOrder
                {
                    OrderNo = !string.IsNullOrEmpty(payPageResponse.order_no) 
                        ? payPageResponse.order_no 
                        : (payPageResponse.uid ?? orderNo ?? string.Empty),
                    Status = status,
                    Description = payPageResponse.msg ?? "未知錯誤",
                    PayType = payType
                };

                // 根據付款類型設定對應的參數物件
                switch (payType?.ToUpper())
                {
                    case "C": // 信用卡
                        creOrder.CardParam = new CreOrderCardParamRes
                        {
                            CardPayURL = isSuccess 
                                ? (payPageResponse.url ?? string.Empty)
                                //: GetErrorPageUrl("信用卡付款失敗", payPageResponse.msg ?? "未知錯誤")
                                : GetErrorPageUrl("目前暫時無法使用信用卡支付!", payPageResponse.msg ?? "，感謝您!")
                        };
                        break;

                    case "A": // ATM 轉帳
                        creOrder.ATMParam = new CreOrderATMParamRes
                        {
                            AtmPayNo = isSuccess 
                                ? (payPageResponse.key ?? string.Empty)
                                : string.Empty
                        };
                        // ATM 失敗時也可以提供錯誤頁面 URL
                        if (!isSuccess)
                        {
                            creOrder.CardParam = new CreOrderCardParamRes
                            {
                                CardPayURL = GetErrorPageUrl("ATM轉帳建立失敗", payPageResponse.msg ?? "未知錯誤")
                            };
                        }
                        break;

                    case "M": // 行動支付
                        creOrder.MobileParam = new CreOrderMobileParamRes
                        {
                            MobilePayURL = isSuccess 
                                ? (payPageResponse.url ?? string.Empty)
                                : GetErrorPageUrl("行動支付失敗", payPageResponse.msg ?? "未知錯誤")
                        };
                        break;

                    case "L": // LinePay
                        creOrder.MobileParam = new CreOrderMobileParamRes
                        {
                            MobilePayURL = isSuccess 
                                ? (payPageResponse.url ?? string.Empty)
                                : GetErrorPageUrl("LinePay付款失敗", payPageResponse.msg ?? "未知錯誤")
                        };
                        break;

                    default:
                        // 預設當作信用卡處理
                        creOrder.CardParam = new CreOrderCardParamRes
                        {
                            CardPayURL = isSuccess 
                                ? (payPageResponse.url ?? string.Empty)
                                : GetErrorPageUrl("付款失敗", payPageResponse.msg ?? "未知錯誤")
                        };
                        break;
                }

                // 記錄轉換日誌
                System.Diagnostics.Trace.WriteLine($"[QPayProcessor] ConvertPayPageResponseToCreOrder:");
                System.Diagnostics.Trace.WriteLine($"  - PayType: {payType}");
                System.Diagnostics.Trace.WriteLine($"  - OrderNo: {creOrder.OrderNo}");
                System.Diagnostics.Trace.WriteLine($"  - Status: {creOrder.Status}");
                System.Diagnostics.Trace.WriteLine($"  - Code: {payPageResponse.code}");
                System.Diagnostics.Trace.WriteLine($"  - Message: {payPageResponse.msg}");
                if (isSuccess && !string.IsNullOrEmpty(payPageResponse.url))
                {
                    System.Diagnostics.Trace.WriteLine($"  - PayURL: {payPageResponse.url}");
                }
                else if (!isSuccess)
                {
                    System.Diagnostics.Trace.WriteLine($"  - ErrorURL: {creOrder.CardParam?.CardPayURL ?? creOrder.MobileParam?.MobilePayURL}");
                }

                return creOrder;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[QPayProcessor] ConvertPayPageResponseToCreOrder Error: {ex.Message}");
                return new CreOrder
                {
                    OrderNo = orderNo ?? string.Empty,
                    Status = "F",
                    Description = $"轉換失敗: {ex.Message}",
                    CardParam = new CreOrderCardParamRes
                    {
                        CardPayURL = GetErrorPageUrl("系統錯誤", $"轉換失敗: {ex.Message}")
                    },
                    ATMParam = null,
                    MobileParam = null
                };
            }
        }

        /// <summary>
        /// 產生錯誤頁面 URL，包含錯誤標題和錯誤訊息
        /// </summary>
        /// <param name="errorTitle">錯誤標題</param>
        /// <param name="errorMessage">錯誤詳細訊息</param>
        /// <returns>錯誤頁面 URL</returns>
        private string GetErrorPageUrl(string errorTitle, string errorMessage)
        {
            try
            {
                // 從設定檔取得錯誤頁面基礎 URL
                string baseErrorUrl = m_Configuration["ERROR_PAGE_URL"] ?? "error-page";
                
                // URL 編碼錯誤訊息，避免特殊字元問題
                string encodedTitle = Uri.EscapeDataString(errorTitle ?? "付款失敗");
                string encodedMessage = Uri.EscapeDataString(errorMessage ?? "未知錯誤");
                
                // 組合完整的錯誤頁面 URL
                string errorPageUrl = $"{baseErrorUrl}?title={encodedTitle}&message={encodedMessage}&timestamp={DateTime.Now:yyyyMMddHHmmss}";
                
                // 記錄錯誤頁面 URL 產生
                System.Diagnostics.Trace.WriteLine($"[QPayProcessor] Generated Error Page URL:");
                System.Diagnostics.Trace.WriteLine($"  - Title: {errorTitle}");
                System.Diagnostics.Trace.WriteLine($"  - Message: {errorMessage}");
                System.Diagnostics.Trace.WriteLine($"  - URL: {errorPageUrl}");
                
                return errorPageUrl;
            }
            catch (Exception ex)
            {
                // 如果產生錯誤頁面 URL 時發生例外，回傳基本的錯誤頁面
                System.Diagnostics.Trace.WriteLine($"[QPayProcessor] GetErrorPageUrl Exception: {ex.Message}");
                return "/payment-error?title=系統錯誤&message=無法產生錯誤頁面";
            }
        }
        public async Task<CreOrder> CreOrderCard(int Amount, String ProductName, String OrderDate, String FeeId, String PayType, String PayTypeSub, String Staging, int DeductTotalNum, String PeriodType, int DeductFreq, String CreditCategory, Entity LineLoginContact, String CCToken = null)
        {
            if (m_Configuration["PAY_PROVIDER"] == "永豐金流")
            {
                // 永豐金流
                CreOrderReq creOrderReq = new CreOrderReq()
                {
                    ShopNo = m_ShopNo,
                    OrderNo = PayType + OrderDate,
                    Amount = Amount * 100,
                    CurrencyID = "TWD",
                    PrdtName = ProductName,
                    ReturnURL = RETURN_URL,
                    BackendURL = BACKEND_URL,
                    PayType = PayType,
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
                return m_PaymentService.OrderCreate(creOrderReq);
            }
            else if (m_Configuration["PAY_PROVIDER"] == "高鉅金流")
            {
                CreOrder ret = m_PaymentService.CreateOrder(GetRawData(Amount, ProductName, OrderDate, FeeId, PayType, PayTypeSub, LineLoginContact), GetService());
                return ret;
            }
            else if (m_Configuration["PAY_PROVIDER"] == "台新金流")
            {
                // 使用 TSPG 金流 (台新)
                // 依照輸入參數建立 TSPGPaymentRequest
                var tspgRequest = GetTSPGPaymentRequestData(Amount, ProductName, OrderDate, FeeId, PayType, PayTypeSub, LineLoginContact);
                
                // 決定是否啟用 3D (此處簡單依 PayTypeSub 是否為 ONE 判斷，可依實際需求調整)
                bool enable3D = false; // 可改為設定檔或條件判斷

                // 呼叫 TSPG API (測試環境)
                PayPageResponse payPageResponse = TspgToolkit.OrderCreateTest(tspgRequest, enable3D);
                
                // 使用新的轉換函數將 PayPageResponse 轉換為 CreOrder
                return ConvertPayPageResponseToCreOrder(payPageResponse, PayType, PayType + OrderDate);
            }
            else
            {
                CreOrder ret = m_PaymentService.CreateOrder(GetRawData(Amount, ProductName, OrderDate, FeeId, PayType, PayTypeSub, LineLoginContact), GetService());
                return ret;
            }
        }

        // 取得高鉅金流服務設定
        private ServiceRequest GetService()
        {
            ServiceRequest rawData = new ServiceRequest();
            rawData.service_name = m_Configuration["MyPay:ServiceName"];
            rawData.cmd = m_Configuration["MyPay:CMD"];
            return rawData;
        }

        // 原 GetRawData 及其使用的輔助方法保留在下方 (確保在本檔案內宣告)
        private dynamic GetRawData(int Amount, String ProductName, String OrderDate, String FeeId, String PayType, String PayTypeSub, Entity LineLoginContact)
        {
            ArrayList items = CreateProductItems(FeeId, ProductName, Amount);
            dynamic rawData = new ExpandoObject();
            SetRawDataProperties(rawData, Amount, FeeId, items, LineLoginContact);
            return rawData;
        }

        // 參考 GetRawData 參數建立 TSPGPaymentRequest
        private TSPGPaymentRequest GetTSPGPaymentRequestData(int Amount, String ProductName, String OrderDate, String FeeId, String PayType, String PayTypeSub, Entity LineLoginContact)
        {
            // ===== 基本訂單資訊 =====
            string orderNo = (PayType ?? string.Empty) + OrderDate;
            string amtInMinorUnit = (Amount * 100).ToString(); // 金額轉換為分（如 100 元 = 10000）
            
            // ===== 從設定檔讀取商店資訊 =====
            string mid = m_Configuration["TSPG:MerchanID"] ?? string.Empty; // 特店代號
            string tid = m_Configuration["TSPG:TerminaID"] ?? string.Empty; // 端末代號
            string sMid = m_Configuration["TSPG:S_Mid"] ?? string.Empty; // 子特店代號（可選）
            
            // ===== 持卡人基本資訊 =====
            string cardholderName = string.Empty;
            string cardholderEmail = string.Empty;
            string mobilePhone = string.Empty;
            string homeTel = string.Empty;
            string officeTel = string.Empty;
            //string custId = string.Empty; // 身分證號
            string birthday = string.Empty; // 生日
            
            // 從 LineLoginContact 取得持卡人資訊
            if (LineLoginContact != null)
            {
                try 
                { 
                    // 姓名
                    //cardholderName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref LineLoginContact, "fullname") ?? string.Empty;
                    
                    // Email
                    cardholderEmail = this.m_ToolUtilityClass.GetEntityStringAttribute(ref LineLoginContact, "emailaddress1") ?? string.Empty;
                    
                    // 手機
                    mobilePhone = this.m_ToolUtilityClass.GetEntityStringAttribute(ref LineLoginContact, "mobilephone") ?? string.Empty;
                    // 移除手機號碼的非數字字元
                    mobilePhone = System.Text.RegularExpressions.Regex.Replace(mobilePhone, @"[^\d]", "");
                    // 若開頭是 0，移除 0（因為要搭配國碼 886）
                    if (mobilePhone.StartsWith("0")) mobilePhone = mobilePhone.Substring(1);
                    
                    // 居家電話
                    homeTel = this.m_ToolUtilityClass.GetEntityStringAttribute(ref LineLoginContact, "telephone1") ?? string.Empty;
                    homeTel = System.Text.RegularExpressions.Regex.Replace(homeTel, @"[^\d]", "");
                    
                    // 公司電話
                    officeTel = this.m_ToolUtilityClass.GetEntityStringAttribute(ref LineLoginContact, "telephone2") ?? string.Empty;
                    officeTel = System.Text.RegularExpressions.Regex.Replace(officeTel, @"[^\d]", "");
                    
                    // 身分證號
                    //custId = this.m_ToolUtilityClass.GetEntityStringAttribute(ref LineLoginContact, "new_personal_id") ?? string.Empty;
                    //if (!string.IsNullOrEmpty(custId) && custId.Length > 0)
                    //{
                    //    // 確保首字母大寫
                    //    custId = custId.ToUpper();
                    //}
                    
                    // 生日（轉換為 MMddyyyy 格式）
                    DateTime birthDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(ref LineLoginContact, "birthdate");
                    if (birthDate != DateTime.MinValue)
                    {
                        birthday = birthDate.ToString("MMddyyyy");
                    }
                } 
                catch (Exception ex) 
                {
                    // 如果取得資料失敗，記錄錯誤但繼續處理
                    String errorLog = $"取得持卡人資訊時發生錯誤: {ex.Message}";
                    // 可以選擇記錄到日誌
                }
            }

            // ===== 回傳網址設定 ===== POST_BACK_URL
            string postBackUrl = m_Configuration["TSPG:POST_BACK_URL"] ?? string.Empty; // 使用者完成付款後的導向頁面
            string resultUrl = m_Configuration["TSPG:RESULT_URL"] ?? string.Empty; // 接收交易結果的後端網址
            
            // ===== 交易參數設定 =====
            string captFlag = "0"; // 預設不自動請款（0: 不同步請款, 1: 同步請款)
            //string layout = "1"; // 預設一般網頁（1: 一般網頁, 2: 行動裝置網頁）
            string layout = m_Configuration["TSPG:Layout"]; // 預設一般網頁（1: 一般網頁, 2: 行動裝置網頁）

            // 根據 UserAgent 或其他條件判斷是否為行動裝置（可選）
            // 這裡可以加入判斷邏輯，例如：
            // if (IsMobileDevice()) layout = "2";

            // ===== 建立 TSPGPaymentRequest 物件 =====
            var request = new TSPGPaymentRequest
            {
                // --- REST API v2.14 結構 ---
                Sender = "rest", // 固定值
                Ver = "1.0.0", // 固定值
                Mid = mid, // 特店代號（必填）
                S_Mid = !string.IsNullOrEmpty(sMid) ? sMid : null, // 子特店代號（選填）
                Tid = tid, // 端末代號（必填）
                PayType = 1, // 付款類別（1: 信用卡）
                TxType = 1, // 交易類別（1: 授權）
                
                // --- 交易參數清單 ---
                Params = new TSPGPaymentParams
                {
                    // === 必填欄位 ===
                    Layout = layout, // 客戶端版面類型
                    OrderNo = orderNo, // 訂單號碼
                    Amt = amtInMinorUnit, // 交易金額（包含兩位小數）
                    Cur = "NTD", // 幣別（新台幣）
                    OrderDesc = ProductName ?? "奉獻", // 訂單說明
                    PostBackUrl = postBackUrl, // 指定接續網址
                    ResultUrl = resultUrl, // 交易結果回傳網址
                    CaptFlag = captFlag, // 授權同步請款標記
                    ResultFlag = "1", // 回傳訊息標記（1: 查詢交易詳情）
                    
                    // === 持卡人資訊 ===
                    CardholderName = !string.IsNullOrEmpty(cardholderName) ? cardholderName : null,
                    CardholderEmail = !string.IsNullOrEmpty(cardholderEmail) ? cardholderEmail : null,
                    
                    // === 手機號碼資訊 ===
                    CardholderMobilePhone = !string.IsNullOrEmpty(mobilePhone) ? new TSPGCardholderMobilePhone
                    {
                        CountryCode = "886", // 台灣國碼
                        PhoneNumber = mobilePhone
                    } : null,
                    
                    // === 持卡人聯絡資訊 ===
                    CellPhoneNo = !string.IsNullOrEmpty(mobilePhone) ? mobilePhone : null, // 手機號碼
                    HomeTelNo = !string.IsNullOrEmpty(homeTel) ? homeTel : null, // 居家電話
                    OfficeTelNo = !string.IsNullOrEmpty(officeTel) ? officeTel : null, // 公司電話
                    
                    // === 持卡人身分驗證資訊 ===
                    //CustId = !string.IsNullOrEmpty(custId) ? custId : null, // 身分證號
                    BDay = !string.IsNullOrEmpty(birthday) ? birthday : null, // 生日
                    
                    // === 分期付款資訊（根據 PayTypeSub 設定）===
                    // 如果需要支援分期，在此處理
                    // InstallPeriod = PayTypeSub == "STAGING" ? "3" : null, // 分期期數範例
                    
                    // === 紅利折抵資訊（根據 PayTypeSub 設定）===
                    // UseRedeem = PayTypeSub == "BONUS" ? "1" : null, // 紅利交易標記範例
                    
                    // === 國旅卡相關欄位（如果適用）===
                    // City = null, // 縣市群組代碼
                    // StartDate = null, // 啟程日 (MMddyyyy)
                    // EndDate = null, // 回程日 (MMddyyyy)
                    
                    // === 3D 驗證與綁卡相關（如果適用）===
                    // ThreeDSMc = null, // 綁卡類型
                    // ThreeDSRa = null, // 綁卡類別
                    
                    // === 行動裝置身分驗證（如果需要）===
                    // CbrIndicatorFlag = "0", // 不啟用
                    
                    // === 其他選填欄位 ===
                    // TicketNo = null, // 機票號碼
                    // Pan = null, // 卡號（使用 HPP 則不需填）
                    // ExpDate = null, // 到期日 (YYMM)
                    // Cvv2 = null, // CVC2/CVV2
                }
            };

            // ===== 特殊處理：定期定額扣款 (REGULAR) =====
            if (PayTypeSub == "REGULAR")
            {
                // 台新 TSPG 的定期定額實作方式需要查閱台新文件
                // 這裡預留擴充空間，可能需要設定：
                // - 綁卡類型 (ThreeDSMc)
                // - 綁卡類別 (ThreeDSRa)
                // - 或使用其他台新提供的定期定額 API
                
                // 範例（需根據實際台新文件調整） :
                // request.Params.ThreeDSMc = "01"; // 交易中綁卡
                // request.Params.ThreeDSRa = "04"; // 新增卡片
                
                // 注意：台新的定期定額可能需要先進行「綁卡」交易，
                // 然後使用卡片 Token 進行後續扣款
            }
            
            // ===== 特殊處理：分期付款 (STAGING) =====
            if (PayTypeSub == "STAGING")
            {
                // 分期期數設定（需根據實際需求調整）
                // 範例：3期、6期、12期等
                // request.Params.InstallPeriod = "3"; // 3期分期
                
                // 注意：分期付款可能需要額外的手續費計算
            }
            
            // ===== 特殊處理：紅利折抵 (BONUS) =====
            if (PayTypeSub == "BONUS")
            {
                // 啟用紅利交易
                // request.Params.UseRedeem = "1";
            }
            
            // ===== 特殊處理：銀聯卡 (CUP) =====
            if (PayTypeSub == "CUP")
            {
                // 銀聯卡交易（台新 TSPG 可能有特殊設定）
                // 需根據台新文件調整
            }
            
            return request;
        }

        // ====== 補回遺失的方法 (永豐金流) ======
        
        /// <summary>
        /// 建立 ATM 訂單 (永豐金流)
        /// </summary>
        public async Task<CreOrder> CreateOrderATM(int Amount, String ProductName, String OrderDate, String FeeId)
        {
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

        /// <summary>
        /// 建立高鉅金流商品項目列表
        /// </summary>
        private ArrayList CreateProductItems(String FeeId, String ProductName, int Amount, String imageUrl = null)
        {
            ArrayList items = new ArrayList();
            
            // 建立商品項目 (使用 dynamic 以支援高鉅金流的格式)
            dynamic productItem = new ExpandoObject();
            productItem.id = FeeId;
            productItem.name = ProductName;
            productItem.cost = Amount;
            productItem.amount = 1;
            productItem.total = Amount;
            
            if (!string.IsNullOrEmpty(imageUrl))
            {
                productItem.image_url = imageUrl;
            }
            
            items.Add(productItem);
            return items;
        }

        /// <summary>
        /// 設定高鉅金流原始資料屬性
        /// </summary>
        private void SetRawDataProperties(dynamic rawData, int Amount, String FeeId, ArrayList items, Entity LineLoginContact)
        {
            // 組織代碼
            rawData.echo_0 = QPAY_ORGANIZATION;
            
            // 商店代號
            rawData.store_uid = m_Configuration["MyPay:Store_Id"];
            
            // 使用者 ID
            rawData.user_id = LineLoginContact != null ? LineLoginContact.Id.ToString() : Guid.Empty.ToString();
            
            // 姓名 / 真實姓名
            string fullName = string.Empty;
            try 
            { 
                fullName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref LineLoginContact, "fullname") ?? ""; 
            } 
            catch { }
            
            rawData.user_name = fullName;
            rawData.user_real_name = fullName;
            
            // 地址
            string address1_line1 = this.m_ToolUtilityClass.GetEntityStringAttribute(ref LineLoginContact, "address1_line1") ?? "";
            string address1_line2 = this.m_ToolUtilityClass.GetEntityStringAttribute(ref LineLoginContact, "address1_line2") ?? "";
            string address1_line3 = this.m_ToolUtilityClass.GetEntityStringAttribute(ref LineLoginContact, "address1_line3") ?? "";
            rawData.user_address = (address1_line1 + address1_line2 + address1_line3).Trim();
            
            // 身分證 / 手機 / Email
            rawData.user_sn = this.m_ToolUtilityClass.GetEntityStringAttribute(ref LineLoginContact, "new_personal_id") ?? "";
            rawData.user_cellphone = this.m_ToolUtilityClass.GetEntityStringAttribute(ref LineLoginContact, "mobilephone") ?? "";
            rawData.user_email = this.m_ToolUtilityClass.GetEntityStringAttribute(ref LineLoginContact, "emailaddress1") ?? "";
            
            // 基本訂單資訊
            rawData.cost = Amount;
            rawData.currency = m_Configuration["MyPay:Currency"] ?? "TWD";
            rawData.enable_dcc = Convert.ToInt32(m_Configuration["MyPay:EnableDcc"] ?? "0");
            rawData.order_id = FeeId;
            rawData.ip = m_Configuration["MyPay:IP"];
            rawData.item = items.Count.ToString();
            rawData.items = items;
            
            // 付款設定
            rawData.pfn = "0";
            rawData.interface_type = m_Configuration["MyPay:InterfaceType"] ?? "app";
            rawData.discount = m_Configuration["MyPay:Discount"] ?? "0";
            rawData.success_returl = m_Configuration["MyPay:SuccessReturl"] ?? "";
            rawData.failure_returl = m_Configuration["MyPay:FailureReturl"] ?? "";
            rawData.notify_url = m_Configuration["MyPay:ReturnUrl"] ?? "";
            rawData.limit_pay_days = Convert.ToInt32(m_Configuration["MyPay:LimitPayDays"] ?? "7");
            rawData.shipping_fee = m_Configuration["MyPay:ShippingFee"] ?? "0";
        }
        
        #endregion
        #region 高鉅金流 PayPage 回傳處理
        // TODO: 實作高鉅金流(MyPay) PayPage 回傳與驗證邏輯 (目前僅為佔位區塊)
        // 先行提供必要的工具方法，避免編譯錯誤。

        /// <summary>
        /// 驗證並處理高鉅金流回傳結果 (暫時骨架，回傳 true 代表成功)
        /// </summary>
        /// <param name="returnModel">高鉅金流回傳模型</param>
        /// <returns>是否處理成功</returns>
        public async Task<bool> ProcessMyPayReturn(MyPayReturnModel returnModel)
        {
            // 可於此加入: 簽章驗證、更新收費單/認獻單狀態、寫入日誌、推播通知等
            await Task.Yield();
            return true;
        }

        /// <summary>
        /// 查詢永豐金流付款結果 (使用目前設定商店號)
        /// </summary>
        public QryOrderPay OrderPayQuery(String aPayToken)
        {
            QryOrderPayReq orderPayQueryReq = new QryOrderPayReq()
            {
                ShopNo = m_ShopNo,
                PayToken = aPayToken
            };
            return m_PaymentService.OrderPayQuery(orderPayQueryReq);
        }

        /// <summary>
        /// 查詢永豐金流付款結果 (指定商店號並帶入對應 HashCode/Site 資訊)
        /// </summary>
        public QryOrderPay OrderPayQuery(String aShopNo, String aPayToken)
        {
            QryOrderPayReq orderPayQueryReq = new QryOrderPayReq()
            {
                ShopNo = aShopNo,
                PayToken = aPayToken
            };
            return m_PaymentService.OrderPayQuery(orderPayQueryReq, ConvertShopNoToHashCodeAndSite(aShopNo));
        }

        /// <summary>
        /// 依商店代號取得 HashCode/Site 認證字串
        /// </summary>
        private string ConvertShopNoToHashCodeAndSite(String aShopNo)
        {
            switch (aShopNo)
            {
                case "DA1626_001": return "D1695F439A69448F,7E460E920A184845,DEA83EFB714943F3,DC237C5C69914F0C";
                case "DA1626_003": return "2C5D55945FCF4767,76052054D7054EA6,13F282F8A0F5475D,D782B4F1893A4334";
                case "DA2424_001": return "9825732578154B95,C89A75CD59D0430F,DAB73CB2A41E47FF,B09695CE58FA4774";
                case "DA2659_001": return "C8DAEA50FFB64CF4,F141E5BBE21B4D47,A922E0C106D14C35,CA22A88D1032412F";
                case "NA0149_001": return "5E854757C751413F,D743D0EB06904837,08169D5445644513,8E52B5A180EE4399";
                case "DA2890_001": return "BDC962CCC8AB4AE2,946D46DBDDDE43E0,6038DFB03B4342AE,B1F64046CB2E44FC";
                case "DA3033_001": return "4B1657DE6F3547A3,3AB478872D0A49C7,0748F400DD834C07,6506CD86B0174396";
                case "DA3190_001": return "1E582BECE43F421A,8F6ACB29B8EF4C67,8C06D1D49C544C51,041D9136AA9647F2";
                case "DA3189_001": return "A88FB80292D6420D,3844DD3B214D487C,27BC1983D2914C11,32D5A23910734C93";
                case "DA3412_001": return "2B27264C1D794727,7C91CB903482427D,7360D573A5A34184,3C85541425624385";
                case "DA3806_001": return "81F5DAFEAFD343EC,80BA10061E59467B,B5F2CBA592004D2D,D6D805E2CF514E12";
                case "DA3855_002": return "08B9715C313F4ABB,E8AC362AB9174D3C,81D71D28D7E04414,927ADFBE9F854C81";
                case "DA4001_001": return "B2FC3849C9F6487C,6ADDD7D7CCFC48BA,2F83CE17C6044E3D,48737E77D6864915";
                case "DA4195_001": return "B83DCBFA2D994F19,6ED32787DA504871,13E56D7A39AB4768,163EC08BC1624854";
                case "DA4272_001": return "00DC1BDACCB645C6,185B6F59F737462E,6F9C2936E8524F76,8BB48C2260304E29";
                default: return "5E854757C751413F,D743D0EB06904837,08169D5445644513,8E52B5A180EE4399";
            }
        }
        #endregion
        #region 工具區(原本遺失的工具方法補回)
        /// <summary>
        /// 實現阿拉伯數字到大寫中文的轉換，金額轉為大寫金額
        /// </summary>
        public string MoneyToChinese(string LowerMoney)
        {
            string functionReturnValue = null;
            bool IsNegative = false; // 是否是負數

            if (string.IsNullOrWhiteSpace(LowerMoney)) return "零圓整";

            if (LowerMoney.Trim().StartsWith("-"))
            {
                // 是負數則先轉為正數
                LowerMoney = LowerMoney.Trim().Substring(1);
                IsNegative = true;
            }

            string strLower;
            string strUpart;
            string strUpper;
            int iTemp;

            double parsed;
            if (!double.TryParse(LowerMoney, out parsed)) return "零圓整";

            // 保留兩位小數
            LowerMoney = Math.Round(parsed, 2).ToString();

            if (LowerMoney.IndexOf('.') > 0)
            {
                if (LowerMoney.IndexOf('.') == LowerMoney.Length - 2)
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
                    case ".": strUpart = "圓"; break;
                    case "0": strUpart = "零"; break;
                    case "1": strUpart = "壹"; break;
                    case "2": strUpart = "貳"; break;
                    case "3": strUpart = "叄"; break;
                    case "4": strUpart = "肆"; break;
                    case "5": strUpart = "伍"; break;
                    case "6": strUpart = "陸"; break;
                    case "7": strUpart = "柒"; break;
                    case "8": strUpart = "捌"; break;
                    case "9": strUpart = "玖"; break;
                    default: strUpart = ""; break;
                }

                switch (iTemp)
                {
                    case 1: strUpart += "分"; break;
                    case 2: strUpart += "角"; break;
                    case 3: strUpart += ""; break;
                    case 4: strUpart += ""; break;
                    case 5: strUpart += "拾"; break;
                    case 6: strUpart += "佰"; break;
                    case 7: strUpart += "仟"; break;
                    case 8: strUpart += "萬"; break;
                    case 9: strUpart += "拾"; break;
                    case 10: strUpart += "佰"; break;
                    case 11: strUpart += "仟"; break;
                    case 12: strUpart += "億"; break;
                    case 13: strUpart += "拾"; break;
                    case 14: strUpart += "佰"; break;
                    case 15: strUpart += "仟"; break;
                    case 16: strUpart += "萬"; break;
                    default: strUpart += ""; break;
                }

                strUpper = strUpart + strUpper;
                iTemp++;
            }

            strUpper = strUpper.Replace("零拾", "零")
                               .Replace("零佰", "零")
                               .Replace("零仟", "零")
                               .Replace("零零零", "零")
                               .Replace("零零", "零")
                               .Replace("零角零分", "整")
                               .Replace("零分", "整")
                               .Replace("零角", "零")
                               .Replace("零億零萬零圓", "億圓")
                               .Replace("億零萬零圓", "億圓")
                               .Replace("零億零萬", "億")
                               .Replace("零萬零圓", "萬圓")
                               .Replace("零億", "億")
                               .Replace("零萬", "萬")
                               .Replace("零圓", "圓")
                               .Replace("零零", "零");

            if (strUpper.StartsWith("圓")) strUpper = strUpper.Substring(1);
            if (strUpper.StartsWith("零")) strUpper = strUpper.Substring(1);
            if (strUpper.StartsWith("角")) strUpper = strUpper.Substring(1);
            if (strUpper.StartsWith("分")) strUpper = strUpper.Substring(1);
            if (strUpper.StartsWith("整")) strUpper = "零圓整";

            functionReturnValue = strUpper.Length == 0 ? "零圓整" : strUpper;
            return IsNegative ? ("負" + functionReturnValue) : functionReturnValue;
        }

        /// <summary>
        /// 轉換定期定額總期數字串為數字
        /// </summary>
        private int TransferToDeductTotalNum(string DeductTotalNumber)
        {
            switch (DeductTotalNumber)
            {
                case "3個月": return 3;
                case "6個月": return 6;
                case "12個月": return 12;
                case "18個月": return 18;
                case "24個月": return 24;
                default: return 0;
            }
        }
        #endregion
    }
}
