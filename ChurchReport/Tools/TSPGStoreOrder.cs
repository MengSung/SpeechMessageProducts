using ChurchReport.Models;
using Newtonsoft.Json;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using System.Dynamic;
using System.Collections;
using System.Linq;

namespace ChurchReport.Tools
{
    /// <summary>
    /// 高鉅金流 (TSPG) 商店訂單處理類別
    /// </summary>
    public class TSPGStoreOrder
    {
        #region 基本設定參數
        
        private readonly string _storeId;
        private readonly string _storeKey;
        private readonly string _storeIV;
        private readonly string _apiBaseUrl;
        private readonly string _queryUrl;

        #endregion

        #region 建構函式
        
        public TSPGStoreOrder()
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                .Build();

            _storeId = config["TSPG:StoreId"] ?? "your_store_id";
            _storeKey = config["TSPG:StoreKey"] ?? "your_store_key";
            _storeIV = config["TSPG:StoreIV"] ?? "your_store_iv";
            _apiBaseUrl = config["TSPG:ApiBaseUrl"] ?? "https://www.paymypay.com/doPay.php";
            _queryUrl = config["TSPG:QueryUrl"] ?? "https://www.paymypay.com/queryOrder.php";
        }

        #endregion

        #region 公開方法

        /// <summary>
        /// 產生訂單資料並提交到高鉅金流
        /// </summary>
        public PayPageResponse Post(NameValueCollection postData)
        {
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                
                using (var client = new WebClient())
                {
                    client.Headers[HttpRequestHeader.ContentType] = "application/x-www-form-urlencoded";
                    client.Headers[HttpRequestHeader.UserAgent] = "ChurchReport/1.0";
                    
                    byte[] responseBytes = client.UploadValues(_apiBaseUrl, "POST", postData);
                    string responseString = Encoding.UTF8.GetString(responseBytes);
                    
                    return ParseResponse(responseString);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"TSPGStoreOrder.Post Error: {ex.Message}");
                return new PayPageResponse { code = "9999", msg = $"系統錯誤: {ex.Message}" };
            }
        }

        /// <summary>
        /// 取得 POST 資料
        /// </summary>
        public NameValueCollection GetPostData(dynamic customData, ServiceRequest service)
        {
            var postData = new NameValueCollection();
            
            postData["store_uid"] = _storeId;
            postData["order_id"] = customData?.order_id ?? Guid.NewGuid().ToString("N");
            postData["cost"] = customData?.cost?.ToString() ?? "0";
            postData["product_name"] = customData?.product_name ?? "商品";
            postData["return_url"] = customData?.return_url ?? "";
            postData["notify_url"] = customData?.notify_url ?? "";
            postData["pay_type"] = customData?.pay_type ?? "credit";
            postData["currency"] = customData?.currency ?? "TWD";

            if (customData?.user_name != null) postData["user_name"] = customData.user_name;
            if (customData?.user_email != null) postData["user_email"] = customData.user_email;
            if (customData?.user_phone != null) postData["user_phone"] = customData.user_phone;
            if (customData?.echo_0 != null) postData["echo_0"] = customData.echo_0.ToString();
            if (customData?.echo_1 != null) postData["echo_1"] = customData.echo_1.ToString();
            if (customData?.echo_2 != null) postData["echo_2"] = customData.echo_2.ToString();
            
            postData["hash"] = GenerateHash(postData);
            
            return postData;
        }

        /// <summary>
        /// 查詢訂單狀態
        /// </summary>
        public PayPageResponse QueryOrder(string orderId)
        {
            try
            {
                var postData = new NameValueCollection();
                postData["store_uid"] = _storeId;
                postData["order_id"] = orderId;
                
                string hashString = $"{_storeKey}{_storeId}{orderId}{_storeIV}";
                postData["hash"] = CalculateSHA256(hashString);
                
                using (var client = new WebClient())
                {
                    client.Headers[HttpRequestHeader.ContentType] = "application/x-www-form-urlencoded";
                    byte[] responseBytes = client.UploadValues(_queryUrl, "POST", postData);
                    string responseString = Encoding.UTF8.GetString(responseBytes);
                    
                    return ParseResponse(responseString);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"TSPGStoreOrder.QueryOrder Error: {ex.Message}");
                return new PayPageResponse { code = "9999", msg = $"查詢錯誤: {ex.Message}", uid = orderId };
            }
        }

        /// <summary>
        /// 驗證回傳資料的檢查碼
        /// </summary>
        public bool VerifyReturnHash(MyPayReturnModel returnData)
        {
            try
            {
                string hashString = $"{_storeKey}{returnData.transaction_id}{returnData.order_id}{returnData.state}{_storeIV}";
                string calculatedHash = CalculateSHA256(hashString);
                return calculatedHash.Equals(returnData.hash, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"TSPGStoreOrder.VerifyReturnHash Error: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 產生檢查碼 (Hash)
        /// </summary>
        private string GenerateHash(NameValueCollection postData)
        {
            string storeUid = postData["store_uid"] ?? "";
            string orderId = postData["order_id"] ?? "";
            string cost = postData["cost"] ?? "";
            string hashString = $"{_storeKey}{storeUid}{orderId}{cost}{_storeIV}";
            return CalculateSHA256(hashString);
        }

        /// <summary>
        /// 計算 SHA256
        /// </summary>
        private string CalculateSHA256(string input)
        {
            using (SHA256 sha256Hash = SHA256.Create())
            {
                byte[] bytes = sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(input));
                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < bytes.Length; i++)
                {
                    builder.Append(bytes[i].ToString("x2"));
                }
                return builder.ToString().ToUpper();
            }
        }

        /// <summary>
        /// 解析回應資料
        /// </summary>
        private PayPageResponse ParseResponse(string responseString)
        {
            try
            {
                if (responseString.StartsWith("{"))
                {
                    return JsonConvert.DeserializeObject<PayPageResponse>(responseString);
                }
                
                var queryParams = HttpUtility.ParseQueryString(responseString);
                return new PayPageResponse
                {
                    code = queryParams["code"] ?? "9999",
                    msg = queryParams["msg"] ?? "未知錯誤",
                    uid = queryParams["uid"] ?? "",
                    key = queryParams["key"] ?? "",
                    url = queryParams["url"] ?? ""
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"TSPGStoreOrder.ParseResponse Error: {ex.Message}");
                return new PayPageResponse { code = "9999", msg = $"回應解析錯誤: {ex.Message}" };
            }
        }

        #endregion
    }
}
