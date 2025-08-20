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
        StoreOrder simulator = new StoreOrder();

        public NameValueCollection GetPostData()
        {
            return simulator.GetPostData();
        }

        public PayPageResponse Post(NameValueCollection pars)
        {
            return simulator.Post(pars);
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

        CreOrder IPayment.OrderCreate(CreOrderReq req)
        {
            return MyPayToolkit.OrderCreate(req);
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
    }
}
