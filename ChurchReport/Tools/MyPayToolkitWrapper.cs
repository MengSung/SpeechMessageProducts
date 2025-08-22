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

        public CreOrder CreateOrder(dynamic customData)
        {
            //StoreOrder simulator = new StoreOrder();
            //僅限走https的Tls 1.2以上版本
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            //發送至遠端
            //return simulator.Post(simulator.GetPostData());

            return ConvertToCreOrder(MyPayStoreOrder.Post(MyPayStoreOrder.GetPostData(customData)));

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
        #endregion
        #region 工具區
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
                CardParam = new CreOrderCardParamRes
                {
                    CardPayURL = response.url,
                }
            };

            return creOrder;
        }

        #endregion 其他方法
    }
}
