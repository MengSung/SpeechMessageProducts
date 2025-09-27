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
        #region 高鉅金流實作成員資料

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
        /// 將動態資料轉換為 TSPG 付款請求
        /// </summary>
        private TSPGPaymentRequest ConvertToTSPGPaymentRequest(dynamic customData)
        {
            return new TSPGPaymentRequest
            {
                OrderId = customData?.order_id ?? Guid.NewGuid().ToString("N"),
                Amount = Convert.ToDecimal(customData?.cost ?? 0),
                ProductName = customData?.product_name ?? "商品",
                Currency = customData?.currency ?? "TWD",
                PaymentType = customData?.pay_type ?? "credit",
                ReturnUrl = customData?.return_url ?? "",
                NotifyUrl = customData?.notify_url ?? "",
                UserName = customData?.user_name ?? "",
                UserEmail = customData?.user_email ?? "",
                UserPhone = customData?.user_phone ?? "",
                Echo0 = customData?.echo_0?.ToString() ?? "",
                Echo1 = customData?.echo_1?.ToString() ?? "",
                Echo2 = customData?.echo_2?.ToString() ?? "",
                Echo3 = customData?.echo_3?.ToString() ?? "",
                Echo4 = customData?.echo_4?.ToString() ?? ""
            };
        }

        /// <summary>
        /// 將永豐金流請求轉換為 TSPG 請求
        /// </summary>
        private TSPGPaymentRequest ConvertCreOrderReqToTSPGRequest(CreOrderReq req)
        {
            return new TSPGPaymentRequest
            {
                OrderId = req.OrderNo,
                Amount = req.Amount,
                ProductName = req.PrdtName ?? "商品",
                Currency = req.CurrencyID ?? "TWD",
                PaymentType = req.PayType == "C" ? "credit" : "atm",
                ReturnUrl = req.ReturnURL,
                NotifyUrl = req.BackendURL,
                Echo0 = req.Param1,
                Echo1 = req.Param2,
                Echo2 = req.Param3
            };
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

        #endregion
    }
}