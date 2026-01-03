using ChurchReport.Models;
using ChurchReport.Tools;  // ? 加入以支援 IPayment, ServiceRequest
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
    /// </summary>
    public partial class QPayProcessor
    {
        #region ===== 建立訂單（統一介面）=====

        /// <summary>
        /// 建立信用卡/行動支付訂單（多金流支援）
        /// </summary>
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
            var payProvider = Configuration["PAY_PROVIDER"];

            return payProvider switch
            {
                "永豐金流" => await CreateQPayOrder(Amount, ProductName, OrderDate, FeeId, PayType, PayTypeSub, Staging, DeductTotalNum, PeriodType, DeductFreq, CreditCategory, CCToken),
                "高鉅金流" => await CreateMyPayOrder(Amount, ProductName, OrderDate, FeeId, PayType, PayTypeSub, LineLoginContact),
                "台新金流" => await CreateTspgOrder(Amount, ProductName, OrderDate, FeeId, PayType, PayTypeSub, LineLoginContact),
                _ => await CreateMyPayOrder(Amount, ProductName, OrderDate, FeeId, PayType, PayTypeSub, LineLoginContact)
            };
        }

        /// <summary>
        /// 建立 ATM 訂單（永豐金流）
        /// </summary>
        public async Task<CreOrder> CreateOrderATM(int Amount, string ProductName, string OrderDate, string FeeId)
        {
            var creOrderReq = new CreOrderReq
            {
                ShopNo = ShopNo,
                OrderNo = "A" + OrderDate,
                Amount = Amount * 100,
                CurrencyID = "TWD",
                PrdtName = ProductName,
                ReturnURL = ReturnUrl,
                BackendURL = BackendUrl,
                PayType = "A",
                Param1 = FeeId,
                Param2 = QPayOrganization,
                Param3 = "收費單",
                ATMParam = new CreOrderATMParamReq
                {
                    ExpireDate = DateTime.Now.AddDays(10).ToLocalTime().ToString("yyyyMMdd")
                }
            };

            return PaymentService.OrderCreate(creOrderReq);
        }

        #endregion

        #region ===== 永豐金流 (QPay) =====

        /// <summary>
        /// 建立永豐金流訂單
        /// </summary>
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
            var creOrderReq = new CreOrderReq
            {
                ShopNo = ShopNo,
                OrderNo = PayType + OrderDate,
                Amount = Amount * 100,
                CurrencyID = "TWD",
                PrdtName = ProductName,
                ReturnURL = ReturnUrl,
                BackendURL = BackendUrl,
                PayType = PayType,
                Param1 = FeeId,
                Param2 = QPayOrganization,
                Param3 = CreditCategory,
                CardParam = new CreOrderCardParamReq
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

            return PaymentService.OrderCreate(creOrderReq);
        }

        /// <summary>
        /// 查詢永豐金流付款結果（使用目前設定商店號）
        /// </summary>
        public QryOrderPay OrderPayQuery(string aPayToken)
        {
            try
            {
                System.Diagnostics.Trace.WriteLine($"[QPayProcessor] OrderPayQuery: PayToken={aPayToken}, ShopNo={ShopNo}");

                var orderPayQueryReq = new QryOrderPayReq
                {
                    ShopNo = ShopNo,
                    PayToken = aPayToken
                };

                var result = PaymentService.OrderPayQuery(orderPayQueryReq);

                System.Diagnostics.Trace.WriteLine($"[QPayProcessor] OrderPayQuery result: Status={result?.Status}, Description={result?.Description}");

                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[QPayProcessor] OrderPayQuery failed: {ex.Message}");
                throw new Exception($"查詢付款結果失敗: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 查詢永豐金流付款結果（指定商店號）
        /// </summary>
        public QryOrderPay OrderPayQuery(string aShopNo, string aPayToken)
        {
            try
            {
                System.Diagnostics.Trace.WriteLine($"[QPayProcessor] OrderPayQuery: ShopNo={aShopNo}, PayToken={aPayToken}");

                var hashCode = ConvertShopNoToHashCodeAndSite(aShopNo);
                var orderPayQueryReq = new QryOrderPayReq
                {
                    ShopNo = aShopNo,
                    PayToken = aPayToken
                };

                var result = PaymentService.OrderPayQuery(orderPayQueryReq, hashCode);

                System.Diagnostics.Trace.WriteLine($"[QPayProcessor] OrderPayQuery result: Status={result?.Status}");

                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[QPayProcessor] OrderPayQuery failed: {ex.Message}");
                throw new Exception($"查詢付款結果失敗 (ShopNo: {aShopNo}): {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 依商店代號取得 HashCode/Site 認證字串
        /// </summary>
        private string ConvertShopNoToHashCodeAndSite(string aShopNo)
        {
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
                _ => "5E854757C751413F,D743D0EB06904837,08169D5445644513,8E52B5A180EE4399"
            };
        }

        #endregion

        #region ===== 高鉅金流 (MyPay) =====

        /// <summary>
        /// 建立高鉅金流訂單
        /// </summary>
        private async Task<CreOrder> CreateMyPayOrder(
            int Amount,
            string ProductName,
            string OrderDate,
            string FeeId,
            string PayType,
            string PayTypeSub,
            Entity LineLoginContact)
        {
            var rawData = GetMyPayRawData(Amount, ProductName, OrderDate, FeeId, PayType + OrderDate, PayType, PayTypeSub, LineLoginContact);
            var service = GetMyPayService();

            return PaymentService.CreateOrder(rawData, service);
        }

        /// <summary>
        /// 取得高鉅金流服務設定
        /// </summary>
        private ServiceRequest GetMyPayService()
        {
            return new ServiceRequest
            {
                service_name = Configuration["MyPay:ServiceName"],
                cmd = Configuration["MyPay:CMD"]
            };
        }

        /// <summary>
        /// 取得高鉅金流原始資料
        /// </summary>
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
            var items = CreateProductItems(FeeId, ProductName, Amount);
            dynamic rawData = new ExpandoObject();
            SetMyPayRawDataProperties(rawData, Amount, FeeId, OrderId, items, LineLoginContact, ProductName);
            return rawData;
        }

        /// <summary>
        /// 建立高鉅金流商品項目列表
        /// </summary>
        private ArrayList CreateProductItems(string FeeId, string ProductName, int Amount, string imageUrl = null)
        {
            var items = new ArrayList();

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
        private void SetMyPayRawDataProperties(
            dynamic rawData,
            int Amount,
            string FeeId,
            string OrderId,
            ArrayList items,
            Entity LineLoginContact,
            string ProductName)
        {
            // 組織代碼
            rawData.echo_0 = QPayOrganization;

            // 商店代號
            rawData.store_uid = Configuration["MyPay:Store_Id"];

            // 使用者 ID
            rawData.user_id = LineLoginContact != null
                ? $"{ProductName}:{LineLoginContact.Id}"
                : Guid.Empty.ToString();

            // 姓名
            string fullName = string.Empty;
            try
            {
                fullName = ToolUtility.GetEntityStringAttribute(ref LineLoginContact, "fullname") ?? "";
            }
            catch { }

            rawData.user_name = fullName;
            rawData.user_real_name = fullName;

            // 地址
            var address1_line1 = ToolUtility.GetEntityStringAttribute(ref LineLoginContact, "address1_line1") ?? "";
            var address1_line2 = ToolUtility.GetEntityStringAttribute(ref LineLoginContact, "address1_line2") ?? "";
            var address1_line3 = ToolUtility.GetEntityStringAttribute(ref LineLoginContact, "address1_line3") ?? "";
            rawData.user_address = (address1_line1 + address1_line2 + address1_line3).Trim();

            // 身分證 / 手機 / Email
            rawData.user_sn = ToolUtility.GetEntityStringAttribute(ref LineLoginContact, "new_personal_id") ?? "";
            rawData.user_cellphone = ToolUtility.GetEntityStringAttribute(ref LineLoginContact, "mobilephone") ?? "";
            rawData.user_email = ToolUtility.GetEntityStringAttribute(ref LineLoginContact, "emailaddress1") ?? "";

            // 基本訂單資訊
            rawData.cost = Amount;
            rawData.currency = Configuration["MyPay:Currency"] ?? "TWD";
            rawData.enable_dcc = Convert.ToInt32(Configuration["MyPay:EnableDcc"] ?? "0");
            rawData.order_id = OrderId;
            rawData.ip = Configuration["MyPay:IP"];
            rawData.item = items.Count.ToString();
            rawData.items = items;

            // 付款設定
            rawData.pfn = "0";
            rawData.interface_type = Configuration["MyPay:InterfaceType"] ?? "app";
            rawData.discount = Configuration["MyPay:Discount"] ?? "0";
            rawData.success_returl = Configuration["MyPay:SuccessReturl"] ?? "";
            rawData.failure_returl = Configuration["MyPay:FailureReturl"] ?? "";
            rawData.notify_url = Configuration["MyPay:NotifyUrl"] ?? "";
            rawData.limit_pay_days = Convert.ToInt32(Configuration["MyPay:LimitPayDays"] ?? "7");
            rawData.shipping_fee = Configuration["MyPay:ShippingFee"] ?? "0";

            // Echo 參數（追蹤用）
            rawData.echo_1 = $"收費單 ID : {FeeId}";
            rawData.echo_2 = ProductName;
            rawData.echo_3 = $"金額 : {Amount}";
            rawData.echo_4 = $"建單時間 : {DateTime.Now:yyyyMMddHHmmss}";
        }

        /// <summary>
        /// 處理高鉅金流回傳結果（佔位方法）
        /// </summary>
        public async Task<bool> ProcessMyPayReturn(MyPayReturnModel returnModel)
        {
            await Task.Yield();
            return true;
        }

        #endregion

        #region ===== 台新金流 (TSPG) =====

        /// <summary>
        /// 建立台新金流訂單
        /// </summary>
        private async Task<CreOrder> CreateTspgOrder(
            int Amount,
            string ProductName,
            string OrderDate,
            string FeeId,
            string PayType,
            string PayTypeSub,
            Entity LineLoginContact)
        {
            var tspgRequest = GetTSPGPaymentRequestData(Amount, ProductName, OrderDate, FeeId, PayType, PayTypeSub, LineLoginContact);
            bool enable3D = false;

            PayPageResponse payPageResponse = TspgToolkit.OrderCreateTest(tspgRequest, enable3D);

            return ConvertPayPageResponseToCreOrder(payPageResponse, PayType, PayType + OrderDate);
        }

        /// <summary>
        /// 建立台新金流請求資料
        /// </summary>
        private TSPGPaymentRequest GetTSPGPaymentRequestData(
            int Amount,
            string ProductName,
            string OrderDate,
            string FeeId,
            string PayType,
            string PayTypeSub,
            Entity LineLoginContact)
        {
            string orderNo = (PayType ?? string.Empty) + OrderDate;
            string amtInMinorUnit = (Amount * 100).ToString();

            string mid = Configuration["TSPG:MerchanID"] ?? string.Empty;
            string tid = Configuration["TSPG:TerminaID"] ?? string.Empty;
            string sMid = Configuration["TSPG:S_Mid"] ?? string.Empty;

            string postBackUrl = Configuration["TSPG:POST_BACK_URL"] ?? string.Empty;
            string resultUrl = Configuration["TSPG:RESULT_URL"] ?? string.Empty;

            string captFlag = "0";
            string layout = Configuration["TSPG:Layout"];

            var request = new TSPGPaymentRequest
            {
                Sender = "rest",
                Ver = "1.0.0",
                Mid = mid,
                S_Mid = !string.IsNullOrEmpty(sMid) ? sMid : null,
                Tid = tid,
                PayType = 1,
                TxType = 1,
                Params = new TSPGPaymentParams
                {
                    Layout = layout,
                    OrderNo = orderNo,
                    Amt = amtInMinorUnit,
                    Cur = "NTD",
                    OrderDesc = ProductName ?? "奉獻",
                    PostBackUrl = postBackUrl,
                    ResultUrl = resultUrl,
                    CaptFlag = captFlag,
                    ResultFlag = "1"
                }
            };

            return request;
        }

        /// <summary>
        /// 將 PayPageResponse 轉換為 CreOrder（適配器模式）
        /// </summary>
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
                        }
                    };
                }

                bool isSuccess = payPageResponse.code == "0000" || payPageResponse.code == "00";
                string status = isSuccess ? "S" : "F";

                var creOrder = new CreOrder
                {
                    OrderNo = !string.IsNullOrEmpty(payPageResponse.order_no)
                        ? payPageResponse.order_no
                        : (payPageResponse.uid ?? orderNo ?? string.Empty),
                    Status = status,
                    Description = payPageResponse.msg ?? "未知錯誤",
                    PayType = payType
                };

                switch (payType?.ToUpper())
                {
                    case "C":
                        creOrder.CardParam = new CreOrderCardParamRes
                        {
                            CardPayURL = isSuccess
                                ? (payPageResponse.url ?? string.Empty)
                                : GetErrorPageUrl("目前暫時無法使用信用卡支付!", payPageResponse.msg ?? "，感謝您!")
                        };
                        break;
                    case "A":
                        creOrder.ATMParam = new CreOrderATMParamRes
                        {
                            AtmPayNo = isSuccess ? (payPageResponse.key ?? string.Empty) : string.Empty
                        };
                        break;
                    case "M":
                    case "L":
                        creOrder.MobileParam = new CreOrderMobileParamRes
                        {
                            MobilePayURL = isSuccess
                                ? (payPageResponse.url ?? string.Empty)
                                : GetErrorPageUrl($"{(payType == "L" ? "LinePay" : "行動支付")}失敗", payPageResponse.msg ?? "未知錯誤")
                        };
                        break;
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
                    }
                };
            }
        }

        /// <summary>
        /// 產生錯誤頁面 URL
        /// </summary>
        private string GetErrorPageUrl(string errorTitle, string errorMessage)
        {
            try
            {
                string baseErrorUrl = Configuration["ERROR_PAGE_URL"] ?? "error-page";
                string encodedTitle = Uri.EscapeDataString(errorTitle ?? "付款失敗");
                string encodedMessage = Uri.EscapeDataString(errorMessage ?? "未知錯誤");

                return $"{baseErrorUrl}?title={encodedTitle}&message={encodedMessage}&timestamp={DateTime.Now:yyyyMMddHHmmss}";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[QPayProcessor] GetErrorPageUrl Exception: {ex.Message}");
                return "/payment-error?title=系統錯誤&message=無法產生錯誤頁面";
            }
        }

        #endregion
    }
}
