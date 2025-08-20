using ChurchReport.Models;
using ChurchReport.Tools;
using QPay.Domain;
using System;
using System.Collections.Specialized;
using System.Net;

namespace ChurchReport.Tools
{
    /// <summary>
    /// QPayToolkit 包裝類別，實作 IQPayToolkit 介面
    /// </summary>
    public class QPayToolkitWrapper : IPayment
    {
        StoreOrder simulator = new StoreOrder();

        public NameValueCollection GetPostData()
        {
            return simulator.GetPostData();
        }

        public PayPageResponse Post(NameValueCollection pars)
        {
            return simulator.Post(pars);
        }

        public CreOrder OrderCreate(CreOrderReq req)
        {
            return QPayToolkit.OrderCreate(req);
        }
        public PayPageResponse Simulate()
        {
            //StoreOrder simulator = new StoreOrder();
            //僅限走https的Tls 1.2以上版本
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            //發送至遠端
            return simulator.Post(simulator.GetPostData());
            //return MyPayToolkit.OrderCreate(req); 

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

        CreOrder IPayment.OrderCreate(CreOrderReq req)
        {
            return QPayToolkit.OrderCreate(req);
            //throw new System.NotImplementedException();
        }

        QryOrderUnCaptured IPayment.OrderUnCapturedQuery(QryOrderUnCapturedReq req)
        {
            throw new System.NotImplementedException();
        }

        OrderMaintain IPayment.OrderMaintain(OrderMaintainReq req)
        {
            throw new System.NotImplementedException();
        }

        QryOrder IPayment.OrderQuery(QryOrderReq req)
        {
            throw new System.NotImplementedException();
        }

        QryOrderPay IPayment.OrderPayQuery(QryOrderPayReq req)
        {
            throw new System.NotImplementedException();
        }

        QryOrderPay IPayment.OrderPayQuery(QryOrderPayReq req, string hashCode)
        {
            throw new System.NotImplementedException();
        }

        QryBill IPayment.BillQuery(QryBillReq req)
        {
            throw new System.NotImplementedException();
        }

        QryAllot IPayment.AllotQuery(QryAllotReq req)
        {
            throw new System.NotImplementedException();
        }

        PayPageResponse IPayment.Post(NameValueCollection pars)
        {
            throw new NotImplementedException();
        }

    }
}
