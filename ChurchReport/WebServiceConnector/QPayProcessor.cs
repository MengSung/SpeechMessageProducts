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

namespace ChurchReport.WebServiceConnector
{
    public class QPayProcessor
    {
        #region 資料區
        string m_ShopNo = "NA0149_001";

        private const String RETURN_URL = "https://yhchurchback.speechmessage.com.tw:454/api/QPayCard/QPayReturnUrl";

        //private const String BACKEND_URL = "http://yhchurchback.speechmessage.com.tw:80/api/QPayAtm/PushSuccess";
        //private const String BACKEND_URL = "http://yhchurchback.speechmessage.com.tw/api/QPayAtm/PushSuccess";
        //private const String BACKEND_URL = "http://yhchurchback.speechmessage.com.tw/api/QPayAtm/QPayBackendUrl";
        //private const String BACKEND_URL = "http://QPaybackend.speechmessage.com.tw/api/QPayAtm/QPayBackendUrl"; // 雲端機房
        private const String BACKEND_URL = "http://QPbackendback.speechmessage.com.tw/api/QPayAtm/QPayBackendUrl";// 公司內部開發

        // 客製化
        // 永和禮拜堂
        private const String CHANNEL_ACCESS_TOKEN = @"HeuLkSEF5CX7hdZo4956IPpgJNdb8VqRZeL1Gu37kFFm+1F7DObAGjfeVYaggzwjZ5H4qraesvquODt7Y81jbtspNZkEq5n3oLDG+G32xQsRx1jCobkABL/Z7RKjkSACNT6h72bPQXsVn9aCuI5OogdB04t89/1O/w1cDnyilFU=";

        //private LinePayClient m_LinePayClient { get; }

        private LineMessagingClient m_LineMessagingClient { get; }
        ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass("DYNAMICS365");

        //private LineNotifyUtility m_LineNotifyUtility = new LineNotifyUtility();

        private PushUtility m_PushUtility { get; }
        private ReplyUtility m_ReplyUtility { get; }

        //private String m_LocalCardOrderNo = "";
        //private String m_LocalAtmOrderNo = "";

        //private DateTime m_AtmExpireDate;

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
        public async Task<string> CreateFeeAsync(String LineId, QpayModel QpayModel)
        {
            try
            {
                #region 非同步建立收費單
                Entity LineLoginContact = this.m_ToolUtilityClass.RetrieveContactByLineId(LineId);

                Guid aCreatedFeeId = CreateFee(LineId, QpayModel);
                Entity aFeeToUpdate = this.m_ToolUtilityClass.RetrieveEntity("new_fee", aCreatedFeeId);

                if (QpayModel.PayWay == "信用卡")
                {
                    CreOrder CreatedCardOrder = await CreOrderCard(QpayModel.Amount, QpayModel.Category, DateTime.Now.ToString("yyyyMMddhhmmssfff"), aCreatedFeeId.ToString(), GetLastCCToken(LineLoginContact));

                    // 用剛剛建立的收費單，填寫訂單編號
                    UpdateFee(ref aFeeToUpdate, CreatedCardOrder.OrderNo, "", "");

                    return CreatedCardOrder.CardParam.CardPayURL;
                }
                else
                {
                    return await ProcessAtm( aCreatedFeeId,  aFeeToUpdate,  QpayModel,  LineId,  LineLoginContact);
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

                return this.m_ToolUtilityClass.CreateEntity(aFeeToCreated);
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

                // 收費單付款方式，預設是ATM轉帳
                this.m_ToolUtilityClass.SetOptionSetAttribute(ref aFeeToCreated, "new_pay_way", 100000002);

                // 帳戶後六碼
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aFeeToCreated, "new_last_six_digit", QpayModel.SerialNumber);

                // 收費單收費日期
                this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aFeeToCreated, "new_pay_date", QpayModel.DedicationDate.ToLocalTime());

                // 奉獻類別
                SetFeePayWay(QpayModel.Category, ref aFeeToCreated);

                // 收費單奉獻其他類別
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aFeeToCreated, "new_others", QpayModel.Others);

                #endregion

            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //Monitor.Exit(this);
                throw e;
            }
        }
        public void SetFeePayWay(String Value, ref Entity aFeeEntity)
        {

            switch (Value)
            {
                case "十一":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_category", 100000000);
                    break;
                case "感恩":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_category", 100000001);
                    break;
                case "建堂":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_category", 100000002);
                    break;
                case "宣教":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_category", 100000003);
                    break;
                case "急難救助":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_category", 100000004);
                    break;
                case "青年事工":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_category", 100000005);
                    break;
                case "萬軍":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_category", 100000006);
                    break;
                case "其他":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_category", 100000007);
                    break;
                default:
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_category", 100000007);
                    break;

            }
        }
        public void UpdateFee(ref Entity aFeeToUpdate, String CardOrderNo, String AtmOrderNo, String AtmPayNo )
        {
            try
            {
                #region 更新收費單
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
                    this.m_ToolUtilityClass.SetOptionSetAttribute(ref aFeeToCreated, "new_pay_way", 100000001); // 100000001 = 信用卡

                    // 信用卡訂單編號
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aFeeToCreated, "new_q_pay_card_order_no", CardOrderNo);
                }
                if (AtmOrderNo != "")
                {
                    // 收費單付款方式
                    this.m_ToolUtilityClass.SetOptionSetAttribute(ref aFeeToCreated, "new_pay_way", 100000002); // 100000002 = ATM轉帳/匯款

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

                CreOrder CreatedAtmOrder = await CreateOrderATM(QpayModel.Amount, QpayModel.Category, DateTime.Now.ToString("yyyyMMddhhmmssfff"), aCreatedFeeId.ToString());

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

                m_PushUtility.SendMessage(LineId, AtmInfoToLine);

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
        #endregion
        #region 永豐金流工具區
        public async Task<CreOrder> CreOrderCard(int Amount, String ProductName, String OrderDate, String FeeId, String CCToken = null)
        {
            //設定參數
            CreOrderReq creOrderReq = new CreOrderReq()
            {
                ShopNo = m_ShopNo,
                OrderNo = "C" + OrderDate,
                Amount = Amount * 100,
                CurrencyID = "TWD",
                PrdtName = ProductName,
                ReturnURL = RETURN_URL,
                BackendURL = BACKEND_URL,
                PayType = "C",
                Param1 = FeeId,
                CardParam = new CreOrderCardParamReq()
                {
                    AutoBilling = "Y",
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
    }
}
