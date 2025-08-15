using ChurchReport.Tools;
using QPay.Domain;

namespace ChurchReport.Tools
{
    /// <summary>
    /// QPayToolkit 包裝類別，實作 IQPayToolkit 介面
    /// </summary>
    public class QPayToolkitWrapper : IQPayToolkit
    {
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
            return QPayToolkit.OrderPayQuery(req);
        }

        public QryOrderPay OrderPayQuery(QryOrderPayReq req, string hashCode)
        {
            return QPayToolkit.OrderPayQuery(req, hashCode);
        }

        public QryBill BillQuery(QryBillReq req)
        {
            return QPayToolkit.BillQuery(req);
        }

        public QryAllot AllotQuery(QryAllotReq req)
        {
            return QPayToolkit.AllotQuery(req);
        }

        CreOrder IQPayToolkit.OrderCreate(CreOrderReq req)
        {
            throw new System.NotImplementedException();
        }

        QryOrderUnCaptured IQPayToolkit.OrderUnCapturedQuery(QryOrderUnCapturedReq req)
        {
            throw new System.NotImplementedException();
        }

        OrderMaintain IQPayToolkit.OrderMaintain(OrderMaintainReq req)
        {
            throw new System.NotImplementedException();
        }

        QryOrder IQPayToolkit.OrderQuery(QryOrderReq req)
        {
            throw new System.NotImplementedException();
        }

        QryOrderPay IQPayToolkit.OrderPayQuery(QryOrderPayReq req)
        {
            throw new System.NotImplementedException();
        }

        QryOrderPay IQPayToolkit.OrderPayQuery(QryOrderPayReq req, string hashCode)
        {
            throw new System.NotImplementedException();
        }

        QryBill IQPayToolkit.BillQuery(QryBillReq req)
        {
            throw new System.NotImplementedException();
        }

        QryAllot IQPayToolkit.AllotQuery(QryAllotReq req)
        {
            throw new System.NotImplementedException();
        }
    }
}