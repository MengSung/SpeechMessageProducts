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
        StoreOrder simulator = new StoreOrder();
        public CreOrder CreateOrder(dynamic customData)
        {
            return new CreOrder();
        }

        public NameValueCollection GetPostData()
        {
            return simulator.GetPostData(GetRawData());
        }
        private dynamic GetRawData()
        {

            ArrayList items = new ArrayList();

            dynamic item = new ExpandoObject();
            item.id = "1";
            item.name = "商品名稱";
            item.cost = "10";
            item.amount = "1";
            item.total = "10";

            items.Add(item);

            dynamic rawData = new ExpandoObject();
            rawData.store_uid = "130544850001";
            rawData.items = items;
            rawData.cost = "10";
            rawData.user_id = "phper";
            rawData.order_id = "1234567890";
            rawData.ip = "127.0.0.1"; // 此為消費者IP，會做為驗證用
            rawData.pfn = "0";

            return rawData;
        }

        public PayPageResponse Post(NameValueCollection pars)
        {
            return simulator.Post(pars);
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
        #endregion
        }
    }
