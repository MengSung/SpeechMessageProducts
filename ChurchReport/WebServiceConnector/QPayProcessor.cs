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
        // 商店編號
        // SANDBOX 測試用
        string m_ShopNo = "NA0149_001";
        // 永豐金流正式環境
        //string m_ShopNo = "DA3412_001";

        #region 公司內部開發
        // 使用 ChurchReport 當作 WebHook
        private const String RETURN_URL = "https://tpehocback.speechmessage.com.tw:466/api/QPayCard/QPayReturnUrl";
        private const String BACKEND_URL = "http://QPbackendback.speechmessage.com.tw/api/QPayAtm/QPayBackendUrl";// 公司內部開發
        #endregion
        #region 雲端機房
        //private const String RETURN_URL = "https://frenchhorn.speechmessage.com.tw:396/api/QPayCard/QPayReturnUrl";
        //private const String BACKEND_URL = "http://QPaybackend.speechmessage.com.tw/api/QPayAtm/QPayBackendUrl"; // 雲端機房
        #endregion

        // 客製化
        // 台北基督之家
        private const String CHANNEL_ACCESS_TOKEN = @"MW7xRUVOMqzX651Akvg2cI8Z8oaX61lPAyL3QdSA94/pD61/FmU0wxj8rJ3CBp6Kle1qoDGIPXnMQuV5fhtYLELP+3nfPPiTdvvud9wrDp0uB204ovkDM3CE6wKpcpS2RUILadDWc4FXX6e8lyr+HQdB04t89/1O/w1cDnyilFU=";

        //private LinePayClient m_LinePayClient { get; }

        private LineMessagingClient m_LineMessagingClient { get; }
        ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass("DYNAMICS365");

        //private LineNotifyUtility m_LineNotifyUtility = new LineNotifyUtility();

        private PushUtility m_PushUtility { get; }
        private ReplyUtility m_ReplyUtility { get; }

        //private String m_LocalCardOrderNo = "";
        //private String m_LocalAtmOrderNo = "";

        //private DateTime m_AtmExpireDate;

        // 客製化
        private const String QPAY_ORGANIZATION = "tpehocback";

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
                    Guid aCreatedFeeId = CreateFee(LineLoginContact, QpayModel);
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
                        return "信用卡繳費失敗!";
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

                            return "信用卡繳費失敗!";
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

                        return "信用卡定期定額建立失敗!";
                    }
                }
                else if (QpayModel.PayWay == "行動支付")
                {
                    //ONE 一次付清
                    //STAGING 分期付款
                    //BONUS 紅利折抵
                    //CUP 銀聯卡一次付清
                    //REGULAR 定期定額扣款

                    Guid aCreatedFeeId = CreateFee(LineLoginContact, QpayModel);
                    Entity aFeeToUpdate = this.m_ToolUtilityClass.RetrieveEntity("new_fee", aCreatedFeeId);

                    CreOrder CreatedCardOrder = await CreOrderCard(QpayModel.Amount, QpayModel.Category + "-" + QpayModel.FullName, DateTime.Now.ToString("yyyyMMddhhmmssfff"), aCreatedFeeId.ToString(), "M", "ONE", "", 0, "M", 1, "收費單", QpayModel.SelectedCreditCard);

                    // 用剛剛建立的收費單，填寫訂單編號
                    UpdateFee(ref aFeeToUpdate, CreatedCardOrder.OrderNo, "", "");

                    return CreatedCardOrder.MobileParam.MobilePayURL;
                }
                else if (QpayModel.PayWay == "ATM轉帳/匯款")
                {
                    Guid aCreatedFeeId = CreateFee(LineLoginContact, QpayModel);
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
        public Guid CreateFee(String LineId, QpayModel QpayModel)
        {
            try
            {
                #region 建立收費單

                Entity aFeeToCreated = new Entity("new_fee");

                SetFeeParameter(LineId, aFeeToCreated, QpayModel);

                // 新增收費單
                Guid aFeeId = this.m_ToolUtilityClass.CreateEntity(aFeeToCreated);
                Entity aRetrievedFee = this.m_ToolUtilityClass.RetrieveEntity("new_fee", aFeeId);

                //指派負責人
                Entity aContact = this.m_ToolUtilityClass.RetrieveContactEntityByLineUserId(LineId);
                try
                {
                    this.m_ToolUtilityClass.AssignOwner("new_fee", aRetrievedFee, this.m_ToolUtilityClass.GetOwnerId(aContact));
                }
                catch (System.Exception e)
                {
                    String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
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
        public void SetFeeParameter(String LineId, Entity aFeeToCreated, QpayModel QpayModel)
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

                // 奉獻類別
                SetFeePayCategory(QpayModel.Category, ref aFeeToCreated);

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
                case "十一奉獻":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_category", 100000000);
                    break;
                case "感恩奉獻":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_category", 100000001);
                    break;
                case "愛心奉獻":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_category", 100000002);
                    break;
                case "建堂奉獻":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_category", 100000003);
                    break;
                case "代收代轉":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_category", 100000004);
                    break;
                case "宣教奉獻":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_category", 100000005);
                    break;
                case "其他奉獻":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_category", 100000006);
                    break;
                case "紓困奉獻":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_category", 100000007);
                    break;
                case "特別奉獻":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_category", 100000008);
                    break;
                default:
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_category", 100000000);
                    break;
            }
        }
        public void SetPayCategory(String Value, String EntityName, ref Entity aFeeEntity)
        {

            switch (Value)
            {
                case "十一奉獻":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, EntityName, 100000000);
                    break;
                case "感恩奉獻":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, EntityName, 100000001);
                    break;
                case "愛心奉獻":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, EntityName, 100000002);
                    break;
                case "建堂奉獻":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, EntityName, 100000003);
                    break;
                case "代收代轉":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, EntityName, 100000004);
                    break;
                case "宣教奉獻":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, EntityName, 100000005);
                    break;
                case "其他奉獻":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, EntityName, 100000006);
                    break;
                case "紓困奉獻":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, EntityName, 100000007);
                    break;
                case "特別奉獻":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, EntityName, 100000008);
                    break;
                default:
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, EntityName, 100000000);
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
                    Guid aCreatedFeeId = CreateFee(aContact, QpayModel);

                    SendGratitudeLineMessage(aContact, QpayModel);

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
        public Guid CreateFee(Entity aContact, QpayModel QpayModel)
        {
            try
            {
                #region 建立收費單

                Entity aFeeToCreated = new Entity("new_fee");

                SetFeeParameter(aContact, aFeeToCreated, QpayModel);

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
        public void SetFeeParameter(Entity aContact, Entity aFeeToCreated, QpayModel QpayModel)
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

                    // 收費單付款狀態，預設是現金已繳費
                    SetPayStatus("銀行轉帳已繳費", ref aFeeToCreated);

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
