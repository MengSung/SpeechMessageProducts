using ChurchReport.Models;
using Newtonsoft.Json;
using QPay.Domain;
using System;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
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
    /// QPayToolkit 介面，定義永豐金流 QPay API 的所有公開方法
    /// </summary>
    public interface IPayment
    {
        /// <summary>
        /// 取得 POST 資料
        /// </summary>
        /// <returns>POST 資料</returns>
        NameValueCollection GetPostData();

        /// <summary>
        /// Sends a POST request with the specified parameters and returns the server's response as a string.
        /// </summary>
        /// <param name="pars">A collection of key-value pairs representing the parameters to include in the POST request. Cannot be null.</param>
        /// <returns>The response from the server as a string.</returns>
        PayPageResponse Post(NameValueCollection pars);

        /// <summary>
        /// 訂單建立 (虛擬帳號、信用卡)
        /// </summary>
        /// <returns></returns>
        CreOrder CreateOrder();

        /// <summary>
        /// 訂單建立 (虛擬帳號、信用卡)
        /// </summary>
        /// <param name="req">訂單建立請求物件</param>
        /// <returns>訂單建立回應物件</returns>
        CreOrder OrderCreate(CreOrderReq req);

        /// <summary>
        /// 待請款訂單查詢
        /// </summary>
        /// <param name="req">待請款訂單查詢請求物件</param>
        /// <returns>待請款訂單查詢回應物件</returns>
        QryOrderUnCaptured OrderUnCapturedQuery(QryOrderUnCapturedReq req);

        /// <summary>
        /// 信用卡訂單維護
        /// </summary>
        /// <param name="req">訂單維護請求物件</param>
        /// <returns>訂單維護回應物件</returns>
        OrderMaintain OrderMaintain(OrderMaintainReq req);

        /// <summary>
        /// 訂單查詢
        /// </summary>
        /// <param name="req">訂單查詢請求物件</param>
        /// <returns>訂單查詢回應物件</returns>
        QryOrder OrderQuery(QryOrderReq req);

        /// <summary>
        /// 付款結果查詢服務
        /// </summary>
        /// <param name="req">付款結果查詢請求物件</param>
        /// <returns>付款結果查詢回應物件</returns>
        QryOrderPay OrderPayQuery(QryOrderPayReq req);

        /// <summary>
        /// 付款結果查詢服務 (使用自訂 HashCode)
        /// </summary>
        /// <param name="req">付款結果查詢請求物件</param>
        /// <param name="hashCode">自訂雜湊碼</param>
        /// <returns>付款結果查詢回應物件</returns>
        QryOrderPay OrderPayQuery(QryOrderPayReq req, string hashCode);

        /// <summary>
        /// 每日收(退)款查詢服務
        /// </summary>
        /// <param name="req">每日收(退)款查詢請求物件</param>
        /// <returns>每日收(退)款查詢回應物件</returns>
        QryBill BillQuery(QryBillReq req);

        /// <summary>
        /// 撥款檔查詢服務
        /// </summary>
        /// <param name="req">撥款檔查詢請求物件</param>
        /// <returns>撥款檔查詢回應物件</returns>
        QryAllot AllotQuery(QryAllotReq req);
    }
}