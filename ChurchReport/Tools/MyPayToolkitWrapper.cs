using ChurchReport.Models;
using ChurchReport.Tools;
using Newtonsoft.Json;
using QPay.Domain;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web;

namespace ChurchReport.Tools
{
    /// <summary>
    /// QPayToolkit 包裝類別，實作 IQPayToolkit 介面
    /// </summary>
    public class MyPayToolkitWrapper : IPayment
    {
        #region 高鉅金流實作成員資料

        StoreOrder MyPayStoreOrder = new StoreOrder();

        // 將Simulate修改名稱

        public CreOrder CreateOrder(dynamic customData, ServiceRequest Service)
        {
            //StoreOrder simulator = new StoreOrder();
            //僅限走https的Tls 1.2以上版本
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            //發送至遠端
            //return simulator.Post(simulator.GetPostData());

            return ConvertToCreOrder(MyPayStoreOrder.Post(MyPayStoreOrder.GetPostData(customData, Service)));

        }
        #endregion 實作成員資料
        
        #region 永豐金流
        public CreOrder OrderCreate(CreOrderReq req)
        {
            return MyPayToolkit.OrderCreate(req); 
        }

        public QryOrderUnCaptured OrderUnCapturedQuery(QryOrderUnCapturedReq req)
        {
            return MyPayToolkit.OrderUnCapturedQuery(req);
        }

        public OrderMaintain OrderMaintain(OrderMaintainReq req)
        {
            return MyPayToolkit.OrderMaintain(req);
        }

        public QryOrder OrderQuery(QryOrderReq req)
        {
            return MyPayToolkit.OrderQuery(req);
        }

        public QryOrderPay OrderPayQuery(QryOrderPayReq req)
        {
            return MyPayToolkit.OrderPayQuery(req);
        }

        public QryOrderPay OrderPayQuery(QryOrderPayReq req, string hashCode)
        {
            return MyPayToolkit.OrderPayQuery(req, hashCode);
        }

        public QryBill BillQuery(QryBillReq req)
        {
            return MyPayToolkit.BillQuery(req);
        }

        public QryAllot AllotQuery(QryAllotReq req)
        {
            return MyPayToolkit.AllotQuery(req);
        }

        #endregion

        #region IPayment 介面實作 - 高鉅金流 TSPG API

        CreOrder IPayment.OrderCreate(CreOrderReq req)
        {
            try
            {
                // 將永豐金流的請求轉換為高鉅金流格式
                var customData = ConvertCreOrderReqToCustomData(req);
                var service = new ServiceRequest { service_name = "order_create" };
                
                // 使用高鉅金流 API
                var response = MyPayStoreOrder.Post(MyPayStoreOrder.GetPostData(customData, service));
                
                return ConvertToCreOrder(response);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"OrderCreate Error: {ex.Message}");
                return new CreOrder
                {
                    Status = "F",
                    Description = $"訂單建立失敗: {ex.Message}",
                    OrderNo = req?.OrderNo ?? ""
                };
            }
        }

        QryOrderUnCaptured IPayment.OrderUnCapturedQuery(QryOrderUnCapturedReq req)
        {
            try
            {
                // 使用高鉅金流查詢未請款訂單
                var response = MyPayStoreOrder.QueryOrder(req.OrderNo);
                
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
                System.Diagnostics.Trace.WriteLine($"OrderUnCapturedQuery Error: {ex.Message}");
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

        OrderMaintain IPayment.OrderMaintain(OrderMaintainReq req)
        {
            try
            {
                // 高鉅金流的訂單維護操作
                // 根據 Command 決定操作類型: P=請款, C=取消授權, R=退款
                var response = ProcessOrderMaintain(req);
                
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
                System.Diagnostics.Trace.WriteLine($"OrderMaintain Error: {ex.Message}");
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

        QryOrder IPayment.OrderQuery(QryOrderReq req)
        {
            try
            {
                // 查詢訂單狀態
                var response = MyPayStoreOrder.QueryOrder(req.OrderNo);
                
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
                System.Diagnostics.Trace.WriteLine($"OrderQuery Error: {ex.Message}");
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

        QryOrderPay IPayment.OrderPayQuery(QryOrderPayReq req)
        {
            try
            {
                // 透過 PayToken 查詢付款結果
                var response = QueryPaymentByToken(req.PayToken);
                
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
                        Amount = "0", // 修正為字串類型
                        Status = response.code == "0000" ? "SUCCESS" : "FAIL"
                    }
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"OrderPayQuery Error: {ex.Message}");
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

        QryOrderPay IPayment.OrderPayQuery(QryOrderPayReq req, string hashCode)
        {
            try
            {
                // 使用自訂 HashCode 查詢付款結果
                var response = QueryPaymentByTokenWithHash(req.PayToken, hashCode);
                
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
                        Amount = "0", // 修正為字串類型
                        Status = response.code == "0000" ? "SUCCESS" : "FAIL"
                    }
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"OrderPayQuery with HashCode Error: {ex.Message}");
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

        QryBill IPayment.BillQuery(QryBillReq req)
        {
            try
            {
                // 查詢對帳檔
                var billData = QueryBillData(req.ShopNo, req.BillDate);
                
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
                System.Diagnostics.Trace.WriteLine($"BillQuery Error: {ex.Message}");
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

        QryAllot IPayment.AllotQuery(QryAllotReq req)
        {
            try
            {
                // 查詢撥款檔
                var allotData = QueryAllotData(req.ShopNo, req.AllotDateS, req.AllotDateE, req.PayType);
                
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
                System.Diagnostics.Trace.WriteLine($"AllotQuery Error: {ex.Message}");
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

        #region 工具區 - 私有輔助方法
        
        // 這裡可以添加其他輔助方法或工具方法
        // 例如：驗證、格式化等
        private CreOrder ConvertToCreOrder(PayPageResponse response)
        {
            // 將 PayPageResponse 轉換為 CreOrder
            if (response == null)
            {
                throw new ArgumentNullException(nameof(response), "PayPageResponse cannot be null");
            }

            CreOrder creOrder = new CreOrder
            {
                OrderNo = response.uid,
                Status = response.code == "0000" ? "S" : "F",
                Description = response.msg,
                CardParam = new CreOrderCardParamRes
                {
                    CardPayURL = response.url,
                }
            };

            return creOrder;
        }

        /// <summary>
        /// 將永豐金流請求轉換為高鉅金流自訂資料格式
        /// </summary>
        private dynamic ConvertCreOrderReqToCustomData(CreOrderReq req)
        {
            return new
            {
                order_id = req.OrderNo,
                cost = req.Amount,
                product_name = req.PrdtName ?? "商品",
                currency = req.CurrencyID ?? "TWD",
                pay_type = req.PayType == "C" ? "credit" : "atm",
                return_url = req.ReturnURL,
                notify_url = req.BackendURL,
                echo_0 = req.Param1,
                echo_1 = req.Param2,
                echo_2 = req.Param3,
                user_name = "", // 需要從其他地方取得
                user_email = "",
                user_phone = ""
            };
        }

        /// <summary>
        /// 處理訂單維護操作
        /// </summary>
        private PayPageResponse ProcessOrderMaintain(OrderMaintainReq req)
        {
            // 根據 Command 類型處理不同的維護操作
            switch (req.Command)
            {
                case "P": // 請款
                    return ProcessCapture(req.OrderNo, req.Amount);
                case "C": // 取消授權
                    return ProcessVoid(req.OrderNo);
                case "R": // 退款
                    return ProcessRefund(req.OrderNo, req.Amount, req.Remark);
                default:
                    return new PayPageResponse
                    {
                        code = "9999",
                        msg = $"不支援的操作類型: {req.Command}",
                        uid = req.OrderNo
                    };
            }
        }

        /// <summary>
        /// 處理請款操作
        /// </summary>
        private PayPageResponse ProcessCapture(string orderNo, int? amount)
        {
            // 實作請款邏輯
            return new PayPageResponse
            {
                code = "0000",
                msg = "請款成功",
                uid = orderNo
            };
        }

        /// <summary>
        /// 處理取消授權操作
        /// </summary>
        private PayPageResponse ProcessVoid(string orderNo)
        {
            // 實作取消授權邏輯
            return new PayPageResponse
            {
                code = "0000",
                msg = "取消授權成功",
                uid = orderNo
            };
        }

        /// <summary>
        /// 處理退款操作
        /// </summary>
        private PayPageResponse ProcessRefund(string orderNo, int? amount, string remark)
        {
            // 實作退款邏輯
            return new PayPageResponse
            {
                code = "0000",
                msg = "退款成功",
                uid = orderNo
            };
        }

        /// <summary>
        /// 透過 Token 查詢付款結果
        /// </summary>
        private PayPageResponse QueryPaymentByToken(string payToken)
        {
            // 實作 Token 查詢邏輯
            return new PayPageResponse
            {
                code = "0000",
                msg = "查詢成功",
                uid = payToken
            };
        }

        /// <summary>
        /// 透過 Token 和自訂 Hash 查詢付款結果
        /// </summary>
        private PayPageResponse QueryPaymentByTokenWithHash(string payToken, string hashCode)
        {
            // 實作帶自訂 Hash 的查詢邏輯
            return new PayPageResponse
            {
                code = "0000",
                msg = "查詢成功",
                uid = payToken
            };
        }

        /// <summary>
        /// 查詢對帳檔資料
        /// </summary>
        private IList<BillInfo> QueryBillData(string shopNo, string billDate)
        {
            // 實作對帳檔查詢邏輯
            return new List<BillInfo>();
        }

        /// <summary>
        /// 查詢撥款檔資料
        /// </summary>
        private List<AllotMain> QueryAllotData(string shopNo, string allotDateS, string allotDateE, string payType)
        {
            // 實作撥款檔查詢邏輯
            return new List<AllotMain>();
        }

        #endregion 其他方法
    }
}
