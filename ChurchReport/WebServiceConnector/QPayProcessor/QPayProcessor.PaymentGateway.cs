using ChurchReport.Models;
using ChurchReport.Payments;


using Microsoft.Xrm.Sdk;

using System;


using System.Threading.Tasks;

namespace ChurchReport.WebServiceConnector
{
    /// <summary>
    /// 金流處理器 - 金流閘道整合模組
    ///
    /// 【職責】
    /// - 永豐金流(QPay)整合
    /// - 高鉅金流(MyPay)整合
    /// - 台新金流(TSPG)整合
    /// - 訂單建立與查詢
    /// - 金流回傳處理
    ///
    /// 【設計模式】
    /// - 適配器模式：統一不同金流介面
    /// - 工廠模式：根據配置選擇金流
    /// - 策略模式：動態選擇金流提供商
    /// </summary>
    public partial class QPayProcessor
    {
        #region ===== 建立訂單（統一介面）=====

        /// <summary>
        /// 建立信用卡/行動支付訂單（多金流支援）
        /// 根據配置的 PAY_PROVIDER 動態選擇金流提供商
        /// </summary>
        /// <param name="Amount">金額（元）</param>
        /// <param name="ProductName">產品名稱</param>
        /// <param name="OrderDate">訂單日期字串</param>
        /// <param name="FeeId">收費單 ID</param>
        /// <param name="PayType">付款類型 (C=信用卡, M=行動支付, L=LinePay)</param>
        /// <param name="PayTypeSub">付款子類型 (ONE=一次付清, STAGING=分期, REGULAR=定期定額)</param>
        /// <param name="Staging">分期資訊</param>
        /// <param name="DeductTotalNum">扣款總期數</param>
        /// <param name="PeriodType">週期類型</param>
        /// <param name="DeductFreq">扣款頻率</param>
        /// <param name="CreditCategory">信用卡類別</param>
        /// <param name="LineLoginContact">登入連絡人實體</param>
        /// <param name="CCToken">信用卡 Token（可選）</param>
        /// <returns>統一的 CreOrder 物件</returns>
        public async Task<CreOrder> CreOrderCard(
            int Amount,
            string ProductName,
            string OrderDate,
            string FeeId,
            string PayType,
            string PayTypeSub,
            string Staging,
            int DeductTotalNum,
            string PeriodType,
            int DeductFreq,
            string CreditCategory,
            Entity LineLoginContact,
            string CCToken = null)
        {
            // QPayProcessor 是 ChurchReport 既有費用/奉獻流程的入口；
            // 這裡只擷取 CRM contact 顯示名稱，實際建立付款交給 QPayCreatePaymentGatewayAdapter 與通用金流核心。
            var customerName = ToolUtility.GetEntityStringAttribute(ref LineLoginContact, "fullname");
            return await CreateQPayOrder(Amount, ProductName, OrderDate, FeeId, PayType, PayTypeSub, Staging, DeductTotalNum, PeriodType, DeductFreq, CreditCategory, customerName, CCToken);
        }

        /// <summary>
        /// 建立 ATM 訂單（永豐金流專用）
        /// </summary>
        /// <param name="Amount">金額（元）</param>
        /// <param name="ProductName">產品名稱</param>
        /// <param name="OrderDate">訂單日期字串</param>
        /// <param name="FeeId">收費單 ID</param>
        /// <returns>CreOrder 物件，包含 ATM 付款資訊</returns>
        public async Task<CreOrder> CreateOrderATM(int Amount, string ProductName, string OrderDate, string FeeId)
        {
            // ATM/轉帳的 provider protocol 已移到 SpeechMessage.Payments.Sinopac。
            // ChurchReport 只提供產品訂單、fee id、回呼 URL 與到期日，並接收 legacy CreOrder 相容結果。
            return await GetRequiredQPayCreatePaymentGatewayAdapter().CreateLegacyOrderAsync(
                new QPayCreatePaymentInput
                {
                    Amount = Amount,
                    ProductName = ProductName,
                    ProductOrderId = "A" + OrderDate,
                    ProductEntityId = FeeId,
                    PaymentOrganization = QPayOrganization,
                    PaymentCategory = "fee",
                    PaymentMethod = "A",
                    ReturnUrl = ReturnUrl,
                    BackendUrl = BackendUrl,
                    ExpireDate = DateTime.Now.AddDays(10).ToLocalTime().ToString("yyyyMMdd")
                });
        }

        private QPayCreatePaymentGatewayAdapter GetRequiredQPayCreatePaymentGatewayAdapter()
        {
            // Fail fast：若 DI 沒有註冊 adapter，就不要退回舊 toolkit 或硬編 credential。
            // 這能確保付款建立一律走新的抽離核心。
            if (QPayCreatePaymentGatewayAdapter == null)
            {
                throw new InvalidOperationException(
                    "QPay create payment gateway adapter is required. Register the payment core adapter before creating QPay orders.");
            }

            return QPayCreatePaymentGatewayAdapter;
        }

        #endregion

        #region ===== 永豐金流 (QPay) =====

        /// <summary>
        /// 建立永豐金流訂單
        /// </summary>
        /// <param name="Amount">金額（元）</param>
        /// <param name="ProductName">產品名稱</param>
        /// <param name="OrderDate">訂單日期字串</param>
        /// <param name="FeeId">收費單 ID</param>
        /// <param name="PayType">付款類型</param>
        /// <param name="PayTypeSub">付款子類型</param>
        /// <param name="Staging">分期資訊</param>
        /// <param name="DeductTotalNum">扣款總期數</param>
        /// <param name="PeriodType">週期類型</param>
        /// <param name="DeductFreq">扣款頻率</param>
        /// <param name="CreditCategory">信用卡類別</param>
        /// <param name="CCToken">信用卡 Token</param>
        /// <returns>CreOrder 物件</returns>
        private async Task<CreOrder> CreateQPayOrder(
            int Amount,
            string ProductName,
            string OrderDate,
            string FeeId,
            string PayType,
            string PayTypeSub,
            string Staging,
            int DeductTotalNum,
            string PeriodType,
            int DeductFreq,
            string CreditCategory,
            string CustomerName,
            string CCToken)
        {
            // 將 QPayProcessor 的舊參數包成 ChurchReport adapter input。
            // adapter 再轉成 PaymentCreateRequest，provider-specific payload 由核心決定。
            return await GetRequiredQPayCreatePaymentGatewayAdapter().CreateLegacyOrderAsync(
                new QPayCreatePaymentInput
                {
                    Amount = Amount,
                    ProductName = ProductName,
                    ProductOrderId = PayType + OrderDate,
                    ProductEntityId = FeeId,
                    PaymentOrganization = QPayOrganization,
                    PaymentCategory = CreditCategory,
                    PaymentMethod = PayType,
                    PaymentMethodSubType = PayTypeSub,
                    ReturnUrl = ReturnUrl,
                    BackendUrl = BackendUrl,
                    AutoBilling = "Y",
                    Staging = Staging,
                    DeductTotalNum = DeductTotalNum,
                    PeriodType = PeriodType,
                    DeductFreq = DeductFreq,
                    Customer = new SpeechMessage.Payments.Models.PaymentCustomer
                    {
                        Name = CustomerName
                    },
                    CreditCardToken = CCToken
                });
        }

        #endregion
    }
}
