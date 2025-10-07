using ChurchReport.Models;
using QPay.Domain;
using System;
using System.Collections.Generic;
using System.Net;

namespace ChurchReport.Tools
{
    /// <summary>
    /// TspgToolkit 包裝類別，實作 IPayment 介面
    /// 提供與永豐金流統一的介面，便於系統切換不同金流服務
    /// </summary>
    public class TspgToolkitWrapper : IPayment
    {
        #region 台新金流實作成員資料

        /// <summary>
        /// 建立訂單 (原有方法，保持相容性)
        /// </summary>
        public CreOrder CreateOrder(dynamic customData, ServiceRequest Service)
        {
            try
            {
                // 設定安全協定
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                
                // 轉換為 TSPG 請求格式
                var tspgRequest = ConvertToTSPGPaymentRequest(customData);
                
                // 呼叫 TSPG API
                var response = TspgToolkit.OrderCreate(tspgRequest);
                
                return ConvertToCreOrder(response);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"TspgToolkitWrapper.CreateOrder Error: {ex.Message}");
                return new CreOrder
                {
                    Status = "F",
                    Description = $"訂單建立失敗: {ex.Message}",
                    OrderNo = ExtractOrderId(customData)
                };
            }
        }

        /// <summary>
        /// 建立訂單 (依照 TSPGPaymentRequest) - 新增多載
        /// </summary>
        /// <param name="request">TSPGPaymentRequest 物件</param>
        /// <returns>CreOrder 結果</returns>
        public CreOrder CreateOrder(TSPGPaymentRequest request)
        {
            try
            {
                if (request == null)
                    throw new ArgumentNullException(nameof(request));
                if (request.Params == null)
                    throw new ArgumentException("request.Params 不可為空", nameof(request));

                // 呼叫 TSPG API
                var response = TspgToolkit.OrderCreate(request);

                // 建立對應 CreOrder (以原始請求的 OrderNo 為主)
                return new CreOrder
                {
                    OrderNo = request.Params.OrderNo,
                    Status = response.code == "0000" ? "S" : "F",
                    Description = response.msg,
                    CardParam = new CreOrderCardParamRes
                    {
                        CardPayURL = response.url
                    }
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"TspgToolkitWrapper.CreateOrder(TSPGPaymentRequest) Error: {ex.Message}");
                return new CreOrder
                {
                    OrderNo = request?.Params?.OrderNo ?? string.Empty,
                    Status = "F",
                    Description = $"訂單建立失敗: {ex.Message}"
                };
            }
        }

        #endregion

        #region IPayment 介面實作

        /// <summary>
        /// 建立訂單
        /// </summary>
        CreOrder IPayment.OrderCreate(CreOrderReq req)
        {
            try
            {
                var tspgRequest = ConvertCreOrderReqToTSPGRequest(req);
                var response = TspgToolkit.OrderCreate(tspgRequest);
                return ConvertToCreOrder(response);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"TspgToolkitWrapper.OrderCreate Error: {ex.Message}");
                return new CreOrder
                {
                    Status = "F",
                    Description = $"訂單建立失敗: {ex.Message}",
                    OrderNo = req?.OrderNo ?? ""
                };
            }
        }

        /// <summary>
        /// 查詢未請款訂單
        /// </summary>
        QryOrderUnCaptured IPayment.OrderUnCapturedQuery(QryOrderUnCapturedReq req)
        {
            try
            {
                var response = TspgToolkit.OrderQuery(req.OrderNo);
                
                return new QryOrderUnCaptured
                {
                    ShopNo = req.ShopNo,
                    Date = DateTime.Now.ToString("yyyyMMddHHmm"),
                    Status = response.code == "0000" ? "S" : "F",
                    Description = response.msg,
                    OrderList = new List<OrderUnCapturedInfo>
                    {
                        new OrderUnCapturedInfo
                        {
                            OrderNo = req.OrderNo,
                            TSNo = response.uid,
                            PayStatus = response.code == "0000" ? "Y" : "N"
                        }
                    }
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"TspgToolkitWrapper.OrderUnCapturedQuery Error: {ex.Message}");
                return new QryOrderUnCaptured
                {
                    ShopNo = req?.ShopNo ?? "",
                    Date = DateTime.Now.ToString("yyyyMMddHHmm"),
                    Status = "F",
                    Description = $"查詢失敗: {ex.Message}",
                    OrderList = new List<OrderUnCapturedInfo>()
                };
            }
        }

        /// <summary>
        /// 訂單維護
        /// </summary>
        OrderMaintain IPayment.OrderMaintain(OrderMaintainReq req)
        {
            try
            {
                PayPageResponse response;
                
                switch (req.Command)
                {
                    case "P": // 請款
                        response = TspgToolkit.CaptureOrder(req.OrderNo, req.Amount);
                        break;
                    case "C": // 取消授權
                        response = TspgToolkit.CancelOrder(req.OrderNo);
                        break;
                    case "R": // 退款
                        var refundRequest = new TSPGRefundRequest
                        {
                            OrderId = req.OrderNo,
                            RefundAmount = req.Amount,
                            Reason = req.Remark
                        };
                        response = TspgToolkit.RefundOrder(refundRequest);
                        break;
                    default:
                        response = new PayPageResponse
                        {
                            code = "9999",
                            msg = $"不支援的操作類型: {req.Command}",
                            uid = req.OrderNo
                        };
                        break;
                }
                
                return new OrderMaintain
                {
                    OrderNo = req.OrderNo,
                    ShopNo = req.ShopNo,
                    TSNo = response.uid,
                    Command = req.Command,
                    Amount = req.Amount,
                    Date = DateTime.Now.ToString("yyyyMMddHHmm"),
                    Status = response.code == "0000" ? "S" : "F",
                    Description = response.msg
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"TspgToolkitWrapper.OrderMaintain Error: {ex.Message}");
                return new OrderMaintain
                {
                    OrderNo = req?.OrderNo ?? "",
                    ShopNo = req?.ShopNo ?? "",
                    Command = req?.Command ?? "",
                    Date = DateTime.Now.ToString("yyyyMMddHHmm"),
                    Status = "F",
                    Description = $"維護操作失敗: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// 查詢訂單
        /// </summary>
        QryOrder IPayment.OrderQuery(QryOrderReq req)
        {
            try
            {
                var response = TspgToolkit.OrderQuery(req.OrderNo);
                
                return new QryOrder
                {
                    ShopNo = req.ShopNo,
                    Date = DateTime.Now.ToString("yyyyMMddHHmm"),
                    Status = response.code == "0000" ? "S" : "F",
                    Description = response.msg,
                    OrderList = new List<OrderInfo>
                    {
                        new OrderInfo
                        {
                            OrderNo = req.OrderNo,
                            TSNo = response.uid,
                            PayType = req.PayType,
                            PayStatus = response.code == "0000" ? "Y" : "N"
                        }
                    }
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"TspgToolkitWrapper.OrderQuery Error: {ex.Message}");
                return new QryOrder
                {
                    ShopNo = req?.ShopNo ?? "",
                    Date = DateTime.Now.ToString("yyyyMMddHHmm"),
                    Status = "F",
                    Description = $"查詢失敗: {ex.Message}",
                    OrderList = new List<OrderInfo>()
                };
            }
        }

        /// <summary>
        /// 查詢付款結果
        /// </summary>
        QryOrderPay IPayment.OrderPayQuery(QryOrderPayReq req)
        {
            try
            {
                var response = TspgToolkit.OrderQuery(req.PayToken);
                
                return new QryOrderPay
                {
                    ShopNo = req.ShopNo,
                    PayToken = req.PayToken,
                    Date = DateTime.Now.ToString("yyyyMMddHHmm"),
                    Status = response.code == "0000" ? "S" : "F",
                    Description = response.msg,
                    TSResultContent = new TSResult
                    {
                        OrderNo = response.uid,
                        Amount = "0",
                        Status = response.code == "0000" ? "SUCCESS" : "FAIL"
                    }
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"TspgToolkitWrapper.OrderPayQuery Error: {ex.Message}");
                return new QryOrderPay
                {
                    ShopNo = req?.ShopNo ?? "",
                    PayToken = req?.PayToken ?? "",
                    Date = DateTime.Now.ToString("yyyyMMddHHmm"),
                    Status = "F",
                    Description = $"付款查詢失敗: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// 查詢付款結果 (帶自訂檢查碼)
        /// </summary>
        QryOrderPay IPayment.OrderPayQuery(QryOrderPayReq req, string hashCode)
        {
            // TSPG 使用內建的檢查碼驗證，這裡直接呼叫標準方法
            return ((IPayment)this).OrderPayQuery(req);
        }

        /// <summary>
        /// 查詢對帳檔
        /// </summary>
        QryBill IPayment.BillQuery(QryBillReq req)
        {
            try
            {
                // TSPG 的對帳檔查詢邏輯
                var billData = QueryTSPGBillData(req.ShopNo, req.BillDate);
                
                return new QryBill
                {
                    ShopNo = req.ShopNo,
                    Date = DateTime.Now.ToString("yyyyMMddHHmm"),
                    BillDate = req.BillDate,
                    Status = "S",
                    Description = "查詢成功",
                    OrderList = billData
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"TspgToolkitWrapper.BillQuery Error: {ex.Message}");
                return new QryBill
                {
                    ShopNo = req?.ShopNo ?? "",
                    Date = DateTime.Now.ToString("yyyyMMddHHmm"),
                    BillDate = req?.BillDate ?? "",
                    Status = "F",
                    Description = $"對帳檔查詢失敗: {ex.Message}",
                    OrderList = new List<BillInfo>()
                };
            }
        }

        /// <summary>
        /// 查詢撥款檔
        /// </summary>
        QryAllot IPayment.AllotQuery(QryAllotReq req)
        {
            try
            {
                // TSPG 的撥款檔查詢邏輯
                var allotData = QueryTSPGAllotData(req.ShopNo, req.AllotDateS, req.AllotDateE, req.PayType);
                
                return new QryAllot
                {
                    ShopNo = req.ShopNo,
                    Date = DateTime.Now.ToString("yyyyMMddHHmm"),
                    Status = "S",
                    Description = "查詢成功",
                    Allot = allotData
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"TspgToolkitWrapper.AllotQuery Error: {ex.Message}");
                return new QryAllot
                {
                    ShopNo = req?.ShopNo ?? "",
                    Date = DateTime.Now.ToString("yyyyMMddHHmm"),
                    Status = "F",
                    Description = $"撥款檔查詢失敗: {ex.Message}",
                    Allot = new List<AllotMain>()
                };
            }
        }

        #endregion

        #region 轉換輔助方法

        /// <summary>
        /// 將動態資料轉換為 TSPG 付款請求 - 支援 REST API v2.14
        /// </summary>
        private TSPGPaymentRequest ConvertToTSPGPaymentRequest(dynamic customData)
        {
            var request = new TSPGPaymentRequest();
            
            // 設定 REST API v2.14 必要欄位
            // Mid 和 Tid 會在 TspgToolkit 中從配置檔案設定
            
            // 設定交易參數
            request.Params = new TSPGPaymentParams
            {
                OrderNo = customData?.order_id ?? Guid.NewGuid().ToString("N"),
                Amt = ConvertAmountToString(customData?.cost),
                OrderDesc = customData?.order_desc ?? "商品",
                Cur = customData?.currency ?? "NTD",
                Layout = customData?.pay_type == "mobile" ? "2" : "1", // 映射付款方式到版面類型
                PostBackUrl = customData?.return_url ?? "",
                ResultUrl = customData?.notify_url ?? "",
                CardholderName = customData?.user_name ?? "",
                CardholderEmail = customData?.user_email ?? ""
            };

            // 設定持卡人手機號碼
            if (!string.IsNullOrEmpty(customData?.user_phone?.ToString()))
            {
                request.Params.CardholderMobilePhone = new TSPGCardholderMobilePhone
                {
                    CountryCode = "886", // 台灣地區代碼
                    PhoneNumber = customData.user_phone.ToString()
                };
            }


            return request;
        }

        /// <summary>
        /// 將永豐金流請求轉換為 TSPG 請求 - 支援 REST API v2.14
        /// </summary>
        private TSPGPaymentRequest ConvertCreOrderReqToTSPGRequest(CreOrderReq req)
        {
            var request = new TSPGPaymentRequest();
            
            // 設定 REST API v2.14 必要欄位
            // Mid 和 Tid 會在 TspgToolkit 中從配置檔案設定
            
            // 設定交易參數
            request.Params = new TSPGPaymentParams
            {
                OrderNo = req.OrderNo,
                Amt = (req.Amount * 100).ToString(), // 永豐金流以元為單位，轉換為分
                OrderDesc = req.PrdtName ?? "商品",
                Cur = req.CurrencyID ?? "NTD",
                Layout = req.PayType == "C" ? "1" : "2", // C=信用卡用一般網頁, 其他用行動版
                PostBackUrl = req.ReturnURL,
                ResultUrl = req.BackendURL
            };

            // 設定信用卡特定參數
            if (req.PayType == "C" && req.CardParam != null)
            {
                // 如果有信用卡參數，可在此設定相關欄位
                if (!string.IsNullOrEmpty(req.CardParam.AutoBilling) && req.CardParam.AutoBilling == "Y")
                {
                    request.Params.CaptFlag = "1"; // 同步請款
                }
            }

            return request;
        }

        /// <summary>
        /// 將 TSPG 回應轉換為永豐金流格式
        /// </summary>
        private CreOrder ConvertToCreOrder(PayPageResponse response)
        {
            if (response == null)
            {
                throw new ArgumentNullException(nameof(response), "PayPageResponse cannot be null");
            }

            return new CreOrder
            {
                OrderNo = response.uid,
                Status = response.code == "0000" ? "S" : "F",
                Description = response.msg,
                CardParam = new CreOrderCardParamRes
                {
                    CardPayURL = response.url
                }
            };
        }

        /// <summary>
        /// 從動態資料中提取訂單編號
        /// </summary>
        private string ExtractOrderId(dynamic customData)
        {
            try
            {
                return customData?.order_id ?? "";
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// 查詢 TSPG 對帳檔資料
        /// </summary>
        private List<BillInfo> QueryTSPGBillData(string shopNo, string billDate)
        {
            // TODO: 實作 TSPG 對帳檔查詢邏輯
            // 這裡可以呼叫 TspgToolkit.GetTransactionHistory 或其他相關方法
            return new List<BillInfo>();
        }

        /// <summary>
        /// 查詢 TSPG 撥款檔資料
        /// </summary>
        private List<AllotMain> QueryTSPGAllotData(string shopNo, string allotDateS, string allotDateE, string payType)
        {
            // TODO: 實作 TSPG 撥款檔查詢邏輯
            return new List<AllotMain>();
        }

        /// <summary>
        /// 轉換金額為字串格式 (以分為單位)
        /// </summary>
        /// <param name="amount">金額 (可能是 decimal, double, string 等)</param>
        /// <returns>以分為單位的字串</returns>
        private string ConvertAmountToString(dynamic amount)
        {
            try
            {
                if (amount == null) return "0";
                
                decimal amountDecimal = Convert.ToDecimal(amount);
                int amountInCents = (int)(amountDecimal * 100);
                return amountInCents.ToString();
            }
            catch
            {
                return "0";
            }
        }

        #endregion
    }
}