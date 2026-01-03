using ChurchReport.Models;
using ChurchReport.Tools;  // 加入以支援 IPayment, ServiceRequest 等類型
using Microsoft.Xrm.Sdk;
using QPay.Domain;
using PowerPlatform.Dataverse.Client.Wsdl;
using System;
using System.Collections;
using System.Dynamic;
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
            // 從配置取得金流提供商
            var payProvider = Configuration["PAY_PROVIDER"];

            // 使用策略模式根據提供商選擇建立方法
            return payProvider switch
            {
                "永豐金流" => await CreateQPayOrder(Amount, ProductName, OrderDate, FeeId, PayType, PayTypeSub, Staging, DeductTotalNum, PeriodType, DeductFreq, CreditCategory, CCToken),
                "高鉅金流" => await CreateMyPayOrder(Amount, ProductName, OrderDate, FeeId, PayType, PayTypeSub, LineLoginContact),
                "台新金流" => await CreateTspgOrder(Amount, ProductName, OrderDate, FeeId, PayType, PayTypeSub, LineLoginContact),
                _ => await CreateMyPayOrder(Amount, ProductName, OrderDate, FeeId, PayType, PayTypeSub, LineLoginContact) // 預設使用高鉅金流
            };
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
            // 建立永豐金流 ATM 訂單請求
            var creOrderReq = new CreOrderReq
            {
                ShopNo = ShopNo,                    // 商店編號
                OrderNo = "A" + OrderDate,          // ATM 訂單編號格式：A + 日期
                Amount = Amount * 100,              // 金額轉換為分
                CurrencyID = "TWD",                 // 貨幣：新台幣
                PrdtName = ProductName,             // 產品名稱
                ReturnURL = ReturnUrl,              // 返回 URL
                BackendURL = BackendUrl,            // 後端通知 URL
                PayType = "A",                      // 付款類型：ATM
                Param1 = FeeId,                     // 自訂參數1：收費單 ID
                Param2 = QPayOrganization,          // 自訂參數2：組織代碼
                Param3 = "收費單",                  // 自訂參數3：類別
                ATMParam = new CreOrderATMParamReq
                {
                    ExpireDate = DateTime.Now.AddDays(10).ToLocalTime().ToString("yyyyMMdd") // 到期日：10天後
                }
            };

            // 呼叫金流服務建立訂單
            return PaymentService.OrderCreate(creOrderReq);
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
            string CCToken)
        {
            // 建立永豐金流訂單請求物件
            var creOrderReq = new CreOrderReq
            {
                ShopNo = ShopNo,                    // 商店編號
                OrderNo = PayType + OrderDate,      // 訂單編號：付款類型 + 日期
                Amount = Amount * 100,              // 金額轉換為分
                CurrencyID = "TWD",                 // 貨幣：新台幣
                PrdtName = ProductName,             // 產品名稱
                ReturnURL = ReturnUrl,              // 用戶付款完成後返回的 URL
                BackendURL = BackendUrl,            // 金流後端通知的 URL
                PayType = PayType,                  // 付款類型
                Param1 = FeeId,                     // 自訂參數1：收費單 ID
                Param2 = QPayOrganization,          // 自訂參數2：組織代碼
                Param3 = CreditCategory,            // 自訂參數3：信用卡類別
                CardParam = new CreOrderCardParamReq
                {
                    AutoBilling = "Y",              // 自動扣款：是
                    PayTypeSub = PayTypeSub,        // 付款子類型
                    Staging = Staging,              // 分期資訊
                    DeductTotalNum = DeductTotalNum,// 扣款總期數
                    PeriodType = PeriodType,        // 週期類型
                    DeductFreq = DeductFreq,        // 扣款頻率
                    CCToken = CCToken               // 信用卡 Token
                }
            };

            // 呼叫金流服務建立訂單
            return PaymentService.OrderCreate(creOrderReq);
        }

        /// <summary>
        /// 查詢永豐金流付款結果（使用目前設定商店號）
        /// </summary>
        /// <param name="aPayToken">付款 Token</param>
        /// <returns>查詢結果物件</returns>
        public QryOrderPay OrderPayQuery(string aPayToken)
        {
            try
            {
                // 記錄查詢參數
                System.Diagnostics.Trace.WriteLine($"[QPayProcessor] OrderPayQuery: PayToken={aPayToken}, ShopNo={ShopNo}");

                // 建立查詢請求
                var orderPayQueryReq = new QryOrderPayReq
                {
                    ShopNo = ShopNo,        // 商店編號
                    PayToken = aPayToken    // 付款 Token
                };

                // 執行查詢
                var result = PaymentService.OrderPayQuery(orderPayQueryReq);

                // 記錄查詢結果
                System.Diagnostics.Trace.WriteLine($"[QPayProcessor] OrderPayQuery result: Status={result?.Status}, Description={result?.Description}");

                return result;
            }
            catch (Exception ex)
            {
                // 記錄錯誤並重新拋出
                System.Diagnostics.Trace.WriteLine($"[QPayProcessor] OrderPayQuery failed: {ex.Message}");
                throw new Exception($"查詢付款結果失敗: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 查詢永豐金流付款結果（指定商店號並帶入對應 HashCode/Site 資訊）
        /// </summary>
        /// <param name="aShopNo">商店編號</param>
        /// <param name="aPayToken">付款 Token</param>
        /// <returns>查詢結果物件</returns>
        public QryOrderPay OrderPayQuery(string aShopNo, string aPayToken)
        {
            try
            {
                // 記錄查詢參數
                System.Diagnostics.Trace.WriteLine($"[QPayProcessor] OrderPayQuery: ShopNo={aShopNo}, PayToken={aPayToken}");

                // 取得商店對應的認證資訊
                var hashCode = ConvertShopNoToHashCodeAndSite(aShopNo);
                System.Diagnostics.Trace.WriteLine($"[QPayProcessor] HashCode: {hashCode?.Substring(0, Math.Min(20, hashCode?.Length ?? 0))}...");

                // 建立查詢請求
                var orderPayQueryReq = new QryOrderPayReq
                {
                    ShopNo = aShopNo,       // 指定商店編號
                    PayToken = aPayToken    // 付款 Token
                };

                // 執行查詢（帶入 HashCode）
                var result = PaymentService.OrderPayQuery(orderPayQueryReq, hashCode);

                // 記錄查詢結果
                System.Diagnostics.Trace.WriteLine($"[QPayProcessor] OrderPayQuery result: Status={result?.Status}");

                return result;
            }
            catch (Exception ex)
            {
                // 記錄錯誤並重新拋出
                System.Diagnostics.Trace.WriteLine($"[QPayProcessor] OrderPayQuery failed: {ex.Message}");
                throw new Exception($"查詢付款結果失敗 (ShopNo: {aShopNo}): {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 依商店代號取得 HashCode/Site 認證字串
        /// 用於不同商店的認證資訊映射
        /// </summary>
        /// <param name="aShopNo">商店編號</param>
        /// <returns>HashCode/Site 字串</returns>
        private string ConvertShopNoToHashCodeAndSite(string aShopNo)
        {
            // 使用 switch expression 進行商店編號到認證資訊的映射
            return aShopNo switch
            {
                "DA1626_001" => "D1695F439A69448F,7E460E920A184845,DEA83EFB714943F3,DC237C5C69914F0C",
                "DA1626_003" => "2C5D55945FCF4767,76052054D7054EA6,13F282F8A0F5475D,D782B4F1893A4334",
                "DA2424_001" => "9825732578154B95,C89A75CD59D0430F,DAB73CB2A41E47FF,B09695CE58FA4774",
                "DA2659_001" => "C8DAEA50FFB64CF4,F141E5BBE21B4D47,A922E0C106D14C35,CA22A88D1032412F",
                "NA0149_001" => "5E854757C751413F,D743D0EB06904837,08169D5445644513,8E52B5A180EE4399",
                "DA2890_001" => "BDC962CCC8AB4AE2,946D46DBDDDE43E0,6038DFB03B4342AE,B1F64046CB2E44FC",
                "DA3033_001" => "4B1657DE6F3547A3,3AB478872D0A49C7,0748F400DD834C07,6506CD86B0174396",
                "DA3190_001" => "1E582BECE43F421A,8F6ACB29B8EF4C67,8C06D1D49C544C51,041D9136AA9647F2",
                "DA3189_001" => "A88FB80292D6420D,3844DD3B214D487C,27BC1983D2914C11,32D5A23910734C93",
                "DA3412_001" => "2B27264C1D794727,7C91CB903482427D,7360D573A5A34184,3C85541425624385",
                "DA3806_001" => "81F5DAFEAFD343EC,80BA10061E59467B,B5F2CBA592004D2D,D6D805E2CF514E12",
                "DA3855_002" => "08B9715C313F4ABB,E8AC362AB9174D3C,81D71D28D7E04414,927ADFBE9F854C81",
                "DA4001_001" => "B2FC3849C9F6487C,6ADDD7D7CCFC48BA,2F83CE17C6044E3D,48737E77D6864915",
                "DA4195_001" => "B83DCBFA2D994F19,6ED32787DA504871,13E56D7A39AB4768,163EC08BC1624854",
                "DA4272_001" => "00DC1BDACCB645C6,185B6F59F737462E,6F9C2936E8524F76,8BB48C2260304E29",
                _ => "5E854757C751413F,D743D0EB06904837,08169D5445644513,8E52B5A180EE4399" // 預設值
            };
        }

        #endregion

        #region ===== 高鉅金流 (MyPay) =====

        /// <summary>
        /// 建立高鉅金流訂單
        /// </summary>
        /// <param name="Amount">金額（元）</param>
        /// <param name="ProductName">產品名稱</param>
        /// <param name="OrderDate">訂單日期字串</param>
        /// <param name="FeeId">收費單 ID</param>
        /// <param name="PayType">付款類型</param>
        /// <param name="PayTypeSub">付款子類型</param>
        /// <param name="LineLoginContact">登入連絡人實體</param>
        /// <returns>CreOrder 物件</returns>
        private async Task<CreOrder> CreateMyPayOrder(
            int Amount,
            string ProductName,
            string OrderDate,
            string FeeId,
            string PayType,
            string PayTypeSub,
            Entity LineLoginContact)
        {
            // 取得高鉅金流所需的原始資料
            var rawData = GetMyPayRawData(Amount, ProductName, OrderDate, FeeId, PayType + OrderDate, PayType, PayTypeSub, LineLoginContact);

            // 取得高鉅金流服務設定
            var service = GetMyPayService();

            // 呼叫金流服務建立訂單
            return PaymentService.CreateOrder(rawData, service);
        }

        /// <summary>
        /// 取得高鉅金流服務設定
        /// </summary>
        /// <returns>ServiceRequest 物件，包含服務名稱和命令</returns>
        private ServiceRequest GetMyPayService()
        {
            return new ServiceRequest
            {
                service_name = Configuration["MyPay:ServiceName"],  // 服務名稱
                cmd = Configuration["MyPay:CMD"]                     // 命令
            };
        }

        /// <summary>
        /// 取得高鉅金流原始資料
        /// </summary>
        /// <param name="Amount">金額</param>
        /// <param name="ProductName">產品名稱</param>
        /// <param name="OrderDate">訂單日期</param>
        /// <param name="FeeId">收費單 ID</param>
        /// <param name="OrderId">訂單 ID</param>
        /// <param name="PayType">付款類型</param>
        /// <param name="PayTypeSub">付款子類型</param>
        /// <param name="LineLoginContact">連絡人實體</param>
        /// <returns>動態物件，包含所有必要欄位</returns>
        private dynamic GetMyPayRawData(
            int Amount,
            string ProductName,
            string OrderDate,
            string FeeId,
            string OrderId,
            string PayType,
            string PayTypeSub,
            Entity LineLoginContact)
        {
            // 建立商品項目列表
            var items = CreateProductItems(FeeId, ProductName, Amount);

            // 建立動態物件
            dynamic rawData = new ExpandoObject();

            // 設定高鉅金流所需的各種屬性
            SetMyPayRawDataProperties(rawData, Amount, FeeId, OrderId, items, LineLoginContact, ProductName);

            return rawData;
        }

        /// <summary>
        /// 建立高鉅金流商品項目列表
        /// </summary>
        /// <param name="FeeId">收費單 ID</param>
        /// <param name="ProductName">產品名稱</param>
        /// <param name="Amount">金額</param>
        /// <param name="imageUrl">圖片 URL（可選）</param>
        /// <returns>ArrayList 包含商品項目</returns>
        private ArrayList CreateProductItems(string FeeId, string ProductName, int Amount, string imageUrl = null)
        {
            var items = new ArrayList();

            // 建立商品項目（使用動態物件以支援高鉅金流格式）
            dynamic productItem = new ExpandoObject();
            productItem.id = FeeId;         // 商品 ID
            productItem.name = ProductName; // 商品名稱
            productItem.cost = Amount;      // 單價
            productItem.amount = 1;         // 數量
            productItem.total = Amount;     // 總價

            // 如果有圖片 URL，加入圖片欄位
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
        /// <param name="rawData">動態物件</param>
        /// <param name="Amount">金額</param>
        /// <param name="FeeId">收費單 ID</param>
        /// <param name="OrderId">訂單 ID</param>
        /// <param name="items">商品項目列表</param>
        /// <param name="LineLoginContact">連絡人實體</param>
        /// <param name="ProductName">產品名稱</param>
        private void SetMyPayRawDataProperties(
            dynamic rawData,
            int Amount,
            string FeeId,
            string OrderId,
            ArrayList items,
            Entity LineLoginContact,
            string ProductName)
        {
            // ===== 組織與商店資訊 =====
            rawData.echo_0 = QPayOrganization;                           // 組織代碼
            rawData.store_uid = Configuration["MyPay:Store_Id"];        // 商店代號

            // ===== 使用者資訊 =====
            // 使用者 ID：產品名稱 + 連絡人 ID
            rawData.user_id = LineLoginContact != null
                ? $"{ProductName}:{LineLoginContact.Id}"
                : Guid.Empty.ToString();

            // 姓名資訊
            string fullName = string.Empty;
            try
            {
                fullName = ToolUtility.GetEntityStringAttribute(ref LineLoginContact, "fullname") ?? "";
            }
            catch { }
            rawData.user_name = fullName;       // 姓名
            rawData.user_real_name = fullName;  // 真實姓名

            // ===== 地址資訊 =====
            var address1_line1 = ToolUtility.GetEntityStringAttribute(ref LineLoginContact, "address1_line1") ?? "";
            var address1_line2 = ToolUtility.GetEntityStringAttribute(ref LineLoginContact, "address1_line2") ?? "";
            var address1_line3 = ToolUtility.GetEntityStringAttribute(ref LineLoginContact, "address1_line3") ?? "";
            rawData.user_address = (address1_line1 + address1_line2 + address1_line3).Trim(); // 完整地址

            // ===== 身分與聯絡資訊 =====
            rawData.user_sn = ToolUtility.GetEntityStringAttribute(ref LineLoginContact, "new_personal_id") ?? "";     // 身分證號
            rawData.user_cellphone = ToolUtility.GetEntityStringAttribute(ref LineLoginContact, "mobilephone") ?? ""; // 手機
            rawData.user_email = ToolUtility.GetEntityStringAttribute(ref LineLoginContact, "emailaddress1") ?? "";   // Email

            // ===== 訂單基本資訊 =====
            rawData.cost = Amount;                                              // 總金額
            rawData.currency = Configuration["MyPay:Currency"] ?? "TWD";       // 貨幣
            rawData.enable_dcc = Convert.ToInt32(Configuration["MyPay:EnableDcc"] ?? "0"); // 是否啟用 DCC
            rawData.order_id = OrderId;                                         // 訂單 ID
            rawData.ip = Configuration["MyPay:IP"];                            // IP 位址
            rawData.item = items.Count.ToString();                             // 商品項目數量
            rawData.items = items;                                             // 商品項目列表

            // ===== 付款設定 =====
            rawData.pfn = "0";                                                 // 付款方式（0=一般付款）
            rawData.interface_type = Configuration["MyPay:InterfaceType"] ?? "app"; // 介面類型
            rawData.discount = Configuration["MyPay:Discount"] ?? "0";         // 折扣
            rawData.success_returl = Configuration["MyPay:SuccessReturl"] ?? ""; // 成功返回 URL
            rawData.failure_returl = Configuration["MyPay:FailureReturl"] ?? ""; // 失敗返回 URL
            rawData.notify_url = Configuration["MyPay:NotifyUrl"] ?? "";       // 通知 URL
            rawData.limit_pay_days = Convert.ToInt32(Configuration["MyPay:LimitPayDays"] ?? "7"); // 付款期限（天）
            rawData.shipping_fee = Configuration["MyPay:ShippingFee"] ?? "0";  // 運費

            // ===== Echo 參數（追蹤用）=====
            rawData.echo_1 = $"收費單 ID : {FeeId}";                                    // 收費單 ID
            rawData.echo_2 = ProductName;                                               // 產品名稱
            rawData.echo_3 = $"金額 : {Amount.ToString()}";                             // 金額
            rawData.echo_4 = $"建單時間 : {DateTime.Now:yyyyMMddHHmmss}";               // 建單時間
        }

        /// <summary>
        /// 處理高鉅金流回傳結果（佔位方法，目前僅回傳成功）
        /// </summary>
        /// <param name="returnModel">高鉅金流回傳模型</param>
        /// <returns>處理是否成功</returns>
        public async Task<bool> ProcessMyPayReturn(MyPayReturnModel returnModel)
        {
            // TODO: 實作簽章驗證、更新收費單狀態、寫入日誌、推播通知等邏輯
            // 目前僅為佔位，永遠回傳 true
            await Task.Yield();
            return true;
        }

        #endregion

        #region ===== 台新金流 (TSPG) =====

        /// <summary>
        /// 建立台新金流訂單
        /// </summary>
        /// <param name="Amount">金額（元）</param>
        /// <param name="ProductName">產品名稱</param>
        /// <param name="OrderDate">訂單日期字串</param>
        /// <param name="FeeId">收費單 ID</param>
        /// <param name="PayType">付款類型</param>
        /// <param name="PayTypeSub">付款子類型</param>
        /// <param name="LineLoginContact">登入連絡人實體</param>
        /// <returns>CreOrder 物件</returns>
        private async Task<CreOrder> CreateTspgOrder(
            int Amount,
            string ProductName,
            string OrderDate,
            string FeeId,
            string PayType,
            string PayTypeSub,
            Entity LineLoginContact)
        {
            // 建立台新金流請求資料
            var tspgRequest = GetTSPGPaymentRequestData(Amount, ProductName, OrderDate, FeeId, PayType, PayTypeSub, LineLoginContact);

            // 決定是否啟用 3D 驗證（目前固定為 false，可依需求調整）
            bool enable3D = false;

            // 呼叫台新金流 API 建立訂單（測試環境）
            PayPageResponse payPageResponse = TspgToolkit.OrderCreateTest(tspgRequest, enable3D);

            // 將 PayPageResponse 轉換為統一的 CreOrder 格式（適配器模式）
            return ConvertPayPageResponseToCreOrder(payPageResponse, PayType, PayType + OrderDate);
        }

        /// <summary>
        /// 建立台新金流請求資料
        /// 參考 GetRawData 參數建立 TSPGPaymentRequest
        /// </summary>
        /// <param name="Amount">金額（元）</param>
        /// <param name="ProductName">產品名稱</param>
        /// <param name="OrderDate">訂單日期字串</param>
        /// <param name="FeeId">收費單 ID</param>
        /// <param name="PayType">付款類型</param>
        /// <param name="PayTypeSub">付款子類型</param>
        /// <param name="LineLoginContact">連絡人實體</param>
        /// <returns>TSPGPaymentRequest 物件</returns>
        private TSPGPaymentRequest GetTSPGPaymentRequestData(
            int Amount,
            string ProductName,
            string OrderDate,
            string FeeId,
            string PayType,
            string PayTypeSub,
            Entity LineLoginContact)
        {
            // ===== 基本訂單資訊 =====
            string orderNo = (PayType ?? string.Empty) + OrderDate;        // 訂單編號
            string amtInMinorUnit = (Amount * 100).ToString();             // 金額轉換為分

            // ===== 從設定檔讀取商店資訊 =====
            string mid = Configuration["TSPG:MerchanID"] ?? string.Empty;   // 特店代號
            string tid = Configuration["TSPG:TerminaID"] ?? string.Empty;   // 端末代號
            string sMid = Configuration["TSPG:S_Mid"] ?? string.Empty;      // 子特店代號

            // ===== 回傳網址設定 =====
            string postBackUrl = Configuration["TSPG:POST_BACK_URL"] ?? string.Empty; // 用戶完成付款後的導向頁面
            string resultUrl = Configuration["TSPG:RESULT_URL"] ?? string.Empty;      // 接收交易結果的後端網址

            // ===== 交易參數設定 =====
            string captFlag = "0"; // 預設不自動請款（0: 不同步請款, 1: 同步請款）
            string layout = Configuration["TSPG:Layout"]; // 客戶端版面類型（1: 一般網頁, 2: 行動裝置網頁）

            // ===== 建立 TSPGPaymentRequest 物件 =====
            var request = new TSPGPaymentRequest
            {
                // --- REST API v2.14 結構 ---
                Sender = "rest",     // 固定值：REST API
                Ver = "1.0.0",       // 固定值：版本號
                Mid = mid,           // 特店代號（必填）
                S_Mid = !string.IsNullOrEmpty(sMid) ? sMid : null, // 子特店代號（選填）
                Tid = tid,           // 端末代號（必填）
                PayType = 1,         // 付款類別（1: 信用卡）
                TxType = 1,          // 交易類別（1: 授權）

                // --- 交易參數清單 ---
                Params = new TSPGPaymentParams
                {
                    // === 必填欄位 ===
                    Layout = layout,                     // 客戶端版面類型
                    OrderNo = orderNo,                   // 訂單號碼
                    Amt = amtInMinorUnit,                // 交易金額（包含兩位小數）
                    Cur = "NTD",                         // 幣別（新台幣）
                    OrderDesc = ProductName ?? "奉獻",   // 訂單說明
                    PostBackUrl = postBackUrl,           // 指定接續網址
                    ResultUrl = resultUrl,               // 交易結果回傳網址
                    CaptFlag = captFlag,                 // 授權同步請款標記
                    ResultFlag = "1"                     // 回傳訊息標記（1: 查詢交易詳情）
                }
            };

            return request;
        }

        /// <summary>
        /// 將 PayPageResponse 轉換為 CreOrder（適配器模式）
        /// 用於統一台新金流(TSPG)與其他金流系統的回傳格式
        /// </summary>
        /// <param name="payPageResponse">台新金流回應物件</param>
        /// <param name="payType">付款類型 (C=信用卡, A=ATM, M=行動支付, L=LinePay)</param>
        /// <param name="orderNo">訂單編號（如果 PayPageResponse 沒有提供，則使用此值）</param>
        /// <returns>統一的 CreOrder 格式物件</returns>
        private CreOrder ConvertPayPageResponseToCreOrder(PayPageResponse payPageResponse, string payType = "C", string orderNo = null)
        {
            try
            {
                // 檢查回應是否為空
                if (payPageResponse == null)
                {
                    return new CreOrder
                    {
                        OrderNo = orderNo ?? string.Empty,
                        Status = "F",    // 失敗
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
                string status = isSuccess ? "S" : "F"; // S=成功, F=失敗

                // 建立基本的 CreOrder 物件
                var creOrder = new CreOrder
                {
                    OrderNo = !string.IsNullOrEmpty(payPageResponse.order_no)
                        ? payPageResponse.order_no
                        : (payPageResponse.uid ?? orderNo ?? string.Empty), // 訂單編號
                    Status = status,                                        // 交易狀態
                    Description = payPageResponse.msg ?? "未知錯誤",       // 描述訊息
                    PayType = payType                                      // 付款類型
                };

                // 根據付款類型設定對應的參數物件
                switch (payType?.ToUpper())
                {
                    case "C": // 信用卡
                        creOrder.CardParam = new CreOrderCardParamRes
                        {
                            CardPayURL = isSuccess
                                ? (payPageResponse.url ?? string.Empty)    // 成功：返回付款 URL
                                : GetErrorPageUrl("目前暫時無法使用信用卡支付!", payPageResponse.msg ?? "，感謝您!") // 失敗：返回錯誤頁面
                        };
                        break;

                    case "A": // ATM 轉帳
                        creOrder.ATMParam = new CreOrderATMParamRes
                        {
                            AtmPayNo = isSuccess ? (payPageResponse.key ?? string.Empty) : string.Empty // ATM 帳號
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
                                ? (payPageResponse.url ?? string.Empty)    // 成功：返回行動支付 URL
                                : GetErrorPageUrl("行動支付失敗", payPageResponse.msg ?? "未知錯誤") // 失敗：返回錯誤頁面
                        };
                        break;

                    case "L": // LinePay
                        creOrder.MobileParam = new CreOrderMobileParamRes
                        {
                            MobilePayURL = isSuccess
                                ? (payPageResponse.url ?? string.Empty)    // 成功：返回 LinePay URL
                                : GetErrorPageUrl("LinePay付款失敗", payPageResponse.msg ?? "未知錯誤") // 失敗：返回錯誤頁面
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
                // 記錄轉換錯誤並返回失敗結果
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
                string baseErrorUrl = Configuration["ERROR_PAGE_URL"] ?? "error-page";

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

        #endregion
    }
}
