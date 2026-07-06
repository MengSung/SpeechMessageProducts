// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.PaymentGateway.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於付款相關流程，註解重點在說明 provider 邊界、金流狀態、錯誤處理與不可改變的外部契約。
// 主要型別：class DonationPaymentProcessor
// 主要成員：CreOrderCard、CreateOrderATM、GetRequiredDonationPaymentCreateGatewayAdapter、CreateDonationPaymentOrder
// 引用命名空間：ChurchReport.Models、ChurchReport.Payments、Microsoft.Xrm.Sdk、System、System.Threading.Tasks
// 閱讀路徑：閱讀此檔案時應先確認金額、訂單編號、付款狀態、provider profile、callback acknowledgement 與錯誤訊息是否跨層保持一致。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using ChurchReport.Models;
using ChurchReport.Payments;


using Microsoft.Xrm.Sdk;

using System;


using System.Threading.Tasks;

namespace ChurchReport.WebServiceConnector
{
    /// <summary>
    /// ChurchReport 奉獻付款處理器 - 金流閘道整合模組
    ///
    /// 【職責】
    /// - 將 ChurchReport 的奉獻付款資料轉交給中性金流 adapter
    /// - 保留既有 CreOrder 回傳形狀，避免一次破壞舊 Razor/View 流程
    /// - provider 差異由 SpeechMessage.Payments 與 adapter 處理，本類別不直接分辨永豐/高鉅/台新 protocol
    ///
    /// 【設計模式】
    /// - 適配器模式：統一不同金流介面
    /// - 工廠模式：根據配置選擇金流
    /// - 策略模式：動態選擇金流提供商
    /// </summary>
    public partial class DonationPaymentProcessor
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
            // DonationPaymentProcessor 是 ChurchReport 既有費用/奉獻流程的入口。
            // 這裡只擷取 CRM contact 顯示名稱，實際 provider 建單交給中性 adapter 與通用金流核心。
            var customerName = ToolUtility.GetEntityStringAttribute(ref LineLoginContact, "fullname");
            return await CreateDonationPaymentOrder(Amount, ProductName, OrderDate, FeeId, PayType, PayTypeSub, Staging, DeductTotalNum, PeriodType, DeductFreq, CreditCategory, customerName, CCToken);
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
            return await GetRequiredDonationPaymentCreateGatewayAdapter().CreateLegacyOrderAsync(
                new DonationPaymentCreateInput
                {
                    Amount = Amount,
                    ProductName = ProductName,
                    ProductOrderId = "A" + OrderDate,
                    ProductEntityId = FeeId,
                    PaymentOrganization = PaymentOrganization,
                    PaymentCategory = "fee",
                    PaymentMethod = "A",
                    ReturnUrl = ReturnUrl,
                    BackendUrl = BackendUrl,
                    ExpireDate = DateTime.Now.AddDays(10).ToLocalTime().ToString("yyyyMMdd")
                });
        }

        private IDonationPaymentCreateGatewayAdapter GetRequiredDonationPaymentCreateGatewayAdapter()
        {
            // Fail fast：若 DI 沒有註冊 adapter，就不要退回舊 toolkit 或硬編 credential。
            // 這能確保付款建立一律走抽離後的金流核心，而不是散落在 ChurchReport 的歷史程式。
            if (DonationPaymentCreateGatewayAdapter == null)
            {
                throw new InvalidOperationException(
                    "Donation payment create gateway adapter is required. Register the payment core adapter before creating donation payment orders.");
            }

            return DonationPaymentCreateGatewayAdapter;
        }

        #endregion

        #region ===== 建立奉獻付款訂單 =====

        /// <summary>
        /// 建立奉獻付款訂單。
        /// 方法仍回傳舊 <see cref="CreOrder"/>，是為了讓既有頁面與測試保持相容；
        /// 真正的 provider request 已由中性 adapter 轉交給抽離後的金流核心。
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
        private async Task<CreOrder> CreateDonationPaymentOrder(
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
            // 將 ChurchReport 舊 UI 傳入的分散參數包成產品層 input。
            // adapter 再轉成 PaymentCreateRequest，provider-specific payload 由核心決定。
            return await GetRequiredDonationPaymentCreateGatewayAdapter().CreateLegacyOrderAsync(
                new DonationPaymentCreateInput
                {
                    Amount = Amount,
                    ProductName = ProductName,
                    ProductOrderId = PayType + OrderDate,
                    ProductEntityId = FeeId,
                    PaymentOrganization = PaymentOrganization,
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
