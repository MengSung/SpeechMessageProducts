using ChurchReport.Models;
using ChurchReport.Tools;
using QPay.Domain;
using System;
using System.Collections;
using System.Collections.Specialized;
using System.Dynamic;
using System.Net;

namespace ChurchReport.Tools
{
    /// <summary>
    /// QPayToolkit 包裝類別，實作 IQPayToolkit 介面
    /// </summary>
    public class QPayToolkitWrapper : IPayment
    {
        #region 實作成員資料
        public CreOrder CreateOrder(dynamic customData, ServiceRequest Service)
        {
            // 只是讓編譯過關
            return new CreOrder();
        }

        public CreOrder OrderCreate(CreOrderReq req)
        {
            return QPayToolkit.OrderCreate(req);
        }

        public QryOrderUnCaptured OrderUnCapturedQuery(QryOrderUnCapturedReq req)
        {
            return QPayToolkit.OrderUnCapturedQuery(req);
        }

        public OrderMaintain OrderMaintain(OrderMaintainReq req)
        {
            return QPayToolkit.OrderMaintain(req);
        }

        public QryOrder OrderQuery(QryOrderReq req)
        {
            return QPayToolkit.OrderQuery(req);
        }

        public QryOrderPay OrderPayQuery(QryOrderPayReq req)
        {
            try
            {
                System.Diagnostics.Trace.WriteLine($"[QPayToolkitWrapper] OrderPayQuery (single param) called");
                System.Diagnostics.Trace.WriteLine($"  - ShopNo: {req?.ShopNo ?? "(null)"}");
                System.Diagnostics.Trace.WriteLine($"  - PayToken: {req?.PayToken ?? "(null)"}");
                
                return QPayToolkit.OrderPayQuery(req);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[QPayToolkitWrapper] OrderPayQuery failed: {ex.Message}");
                throw;
            }
        }

        public QryOrderPay OrderPayQuery(QryOrderPayReq req, string hashCode)
        {
            try
            {
                System.Diagnostics.Trace.WriteLine($"[QPayToolkitWrapper] OrderPayQuery (with hashCode) called");
                System.Diagnostics.Trace.WriteLine($"  - ShopNo: {req?.ShopNo ?? "(null)"}");
                System.Diagnostics.Trace.WriteLine($"  - PayToken: {req?.PayToken ?? "(null)"}");
                System.Diagnostics.Trace.WriteLine($"  - HashCode length: {hashCode?.Length ?? 0}");
                
                return QPayToolkit.OrderPayQuery(req, hashCode);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[QPayToolkitWrapper] OrderPayQuery (with hashCode) failed: {ex.Message}");
                throw;
            }
        }

        public QryBill BillQuery(QryBillReq req)
        {
            return QPayToolkit.BillQuery(req);
        }

        public QryAllot AllotQuery(QryAllotReq req)
        {
            return QPayToolkit.AllotQuery(req);
        }

        #endregion
        }
    }
