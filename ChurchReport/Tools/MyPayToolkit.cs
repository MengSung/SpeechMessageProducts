using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using QPay.Domain;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Hex = ChurchReport.Tools.QPayCommon.HexEncoding;

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Collections;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using System.Dynamic;


namespace MyPay
{
}

namespace ChurchReport.Tools
{
    public class StoreOrder
    {
        /// <summary>
        /// 特約商店商務代號
        /// </summary>
        public string storeUid = "289151880002";
        /// <summary>
        /// 特約商店金鑰或認證碼
        /// </summary>
        public string storeKey = "KYTjd9ACcjGaTK6V3zWmMkyrQS08Ndcx";
        /// <summary>
        /// 串接交易位置
        /// </summary>
        public string url = "https://ka.usecase.cc/api/init";
        /// 取得串接欄位資料
        /// </summary>
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
            rawData.store_uid = this.storeUid;
            rawData.items = items;
            rawData.cost = "10";
            rawData.user_id = "phper";
            rawData.order_id = "1234567890";
            rawData.ip = "127.0.0.1"; // 此為消費者IP，會做為驗證用
            rawData.pfn = "0";

            return rawData;
        }
        /// <summary>
        /// 取得服務位置
        /// </summary>
        private ServiceRequest GetService()
        {
            ServiceRequest rawData = new ServiceRequest();
            rawData.service_name = "api";
            rawData.cmd = "api/orders";
            return rawData;
        }
        /// <summary>
        /// 取得送出欄位資料
        /// </summary>
        public NameValueCollection GetPostData()
        {
            string data_json = JsonConvert.SerializeObject(GetRawData(), Formatting.None);
            string svr_json = JsonConvert.SerializeObject(GetService(), Formatting.None); ; //依API種類調整

            //產生AES向量
            var IV = GetBytesIV();

            //進行加密
            var data_encode = Encrypt(data_json, this.storeKey, IV);
            var svr_encode = Encrypt(svr_json, this.storeKey, IV);

            //請注意使用的 Http Post 套件是否會自動加上UrlEncode，本Post範例為原始方式，故須加上UrlEncode
            //若自行使用的套件會自動補上UrlEncode，則請忽略下面的UrlEncode，避免做了兩次UrlEncode
            string data_toUrlEncode = HttpUtility.UrlEncode(data_encode);
            string svr_toUrlEncode = HttpUtility.UrlEncode(svr_encode);

            NameValueCollection postData = new NameValueCollection();
            postData["store_uid"] = this.storeUid;
            postData["service"] = svr_toUrlEncode;
            postData["encry_data"] = data_toUrlEncode;
            return postData;
        }
        /// <summary>
        /// AES 256 加密
        /// </summary>
        /// <param name="data"></param>
        /// <param name="key"></param>
        /// <param name="byteIV"></param>
        /// <returns></returns>
        private string Encrypt(string data, string key, byte[] byteIV)
        {
            var byteKey = System.Text.Encoding.UTF8.GetBytes(key);
            var enBytes = AES_Encrypt(data, byteKey, byteIV);
            return Convert.ToBase64String(BytesAdd(byteIV, enBytes));
        }
        /// <summary>
        /// AES 256 加密處理
        /// </summary>
        /// <param name="original"></param>
        /// <param name="key"></param>
        /// <param name="iv"></param>
        /// <returns></returns>
        private byte[] AES_Encrypt(string original, byte[] key, byte[] iv)
        {
            try
            {
                var data = Encoding.UTF8.GetBytes(original);

                var cipher = Aes.Create().CreateEncryptor(key, iv);

                var de = cipher.TransformFinalBlock(data, 0, data.Length);
                return de;
            }
            catch
            {
                return null;
            }
        }
        /// <summary>
        /// 轉換Bytes
        /// </summary>
        /// <param name="a"></param>
        /// <param name="arryB"></param>
        /// <returns></returns>
        private byte[] BytesAdd(byte[] a, params byte[][] arryB)
        {
            List<byte> c = new List<byte>();
            c.AddRange(a);
            arryB.ToList().ForEach(b => {
                c.AddRange(b);
            });
            return c.ToArray();
        }
        /// <summary>
        /// 產生AES的IV
        /// </summary>
        /// <returns></returns>
        private static byte[] GetBytesIV()
        {
            var aes = System.Security.Cryptography.AesCryptoServiceProvider.Create();
            aes.KeySize = 256;
            aes.GenerateIV();
            return aes.IV;
        }
        /// <summary>
        /// 資料 POST 到主機
        /// </summary>
        /// <param name="pars"></param>
        /// <returns></returns>
        public string Post(NameValueCollection pars)
        {
            string result = string.Empty;
            string param = string.Empty;
            if (pars.Count > 0)
            {
                pars.AllKeys.ToList().ForEach(key => {
                    param += key + "=" + pars[key] + "&";
                });
                if (param[param.Length - 1] == '&')
                {
                    param = param.Remove(param.Length - 1);
                }
            }
            byte[] bs = Encoding.UTF8.GetBytes(param);

            try
            {
                HttpWebRequest req = (HttpWebRequest)HttpWebRequest.Create(this.url);
                req.Method = "POST";
                req.ContentType = "application/x-www-form-urlencoded";
                req.ContentLength = bs.Length;
                using (Stream reqStream = req.GetRequestStream())
                {
                    reqStream.Write(bs, 0, bs.Length);
                }
                using (WebResponse wr = req.GetResponse())
                {
                    Encoding myEncoding = Encoding.GetEncoding("UTF-8");
                    using (StreamReader myStreamReader = new StreamReader(wr.GetResponseStream(), myEncoding))
                    {
                        result = myStreamReader.ReadToEnd();
                    }
                }

                req = null;
            }
            catch (WebException ex)
            {
                throw new WebException(ex.Message + "params : " + param, ex, ex.Status, ex.Response);
            }
            return result;
        }


    }
    /// <summary>
    /// 串接服務請求欄位
    /// </summary>
    public class ServiceRequest
    {
        public string service_name { get; set; }
        public string cmd { get; set; }
    }

    public static class MyPayToolkit
    {
        static ConfigurationBuilder m_ConfigurationBuilder = (ConfigurationBuilder)new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory()).AddJsonFile("appsettings.json");
        static IConfiguration m_Configuration = m_ConfigurationBuilder.Build();

        private static string _currentVersion = "1.0.0";

        #region
        /// <summary>
        /// 特約商店串接-PayPage金流交易
        /// </summary>
        #endregion






















        // SANDBOX 測試用
        //private static string _site = "https://sandbox.sinopac.com/QPay.WebAPI/api/";
        //private static string _site = "https://apisbx.sinopac.com/funBIZ-Sbx/QPay.WebAPI/api/";
        //private static string _site = m_Configuration["Sinopac:Site"];
        //private static string _site = m_Configuration["Sandbox:Site_Xkey"];
        private static string _site = m_Configuration["Sandbox:Site"];

        //// SANDBOX 測試用
        private static String A1 = m_Configuration["Sandbox:A1"];
        private static String A2 = m_Configuration["Sandbox:A2"];
        private static String B1 = m_Configuration["Sandbox:B1"];
        private static String B2 = m_Configuration["Sandbox:B2"];
        private static String HASH_CODE = A1 + "," + A2 + "," + B1 + "," + B2;

        private static String X_KEY_ID = m_Configuration["Sandbox:XKeyID"];

        // 永豐金流正式環境
        //m_LinePayClient = new LinePayClient(configuration["LinePay:ChannelId"], configuration["LinePay:ChannelSecret"], bool.Parse(configuration["LinePay:IsSandbox"]));

        //永豐金流正式環境
        //永豐金流寄給聖谷行道會的HASH CODE
        //private static String A1 = m_Configuration["Sinopac:A1"];
        //private static String A2 = m_Configuration["Sinopac:A2"];
        //private static String B1 = m_Configuration["Sinopac:B1"];
        //private static String B2 = m_Configuration["Sinopac:B2"];
        //private static String HASH_CODE = A1 + "," + A2 + "," + B1 + "," + B2;

        //private static String X_KEY_ID = m_Configuration["Sinopac:XKeyID"];


        #region Public method
        #region 訂單建立 (虛擬帳號、信用卡)
        /// <summary>
        /// 訂單建立 (虛擬帳號、信用卡)
        /// </summary>
        /// <param name="req"></param>
        /// <example>
        /// 串接範例如下:
        /// CreOrder retObj = QPayToolkit.OrderCreate(new CreOrderReq() { ... });
        /// </example>
        public static CreOrder OrderCreate(CreOrderReq req)
        {
            return GetQPayResponse<CreOrderReq, CreOrder>(req, APIService.OrderCreate);
        }
        #endregion

        #region 待請款訂單查詢
        /// <summary>
        /// 待請款訂單查詢
        /// </summary>
        /// <param name="req"></param>
        /// <example>
        /// 串接範例如下:
        /// QryOrderUnCaptured retObj = QPayToolkit.OrderUnCapturedQuery(new QryOrderUnCapturedReq() { ... });
        /// </example>
        public static QryOrderUnCaptured OrderUnCapturedQuery(QryOrderUnCapturedReq req)
        {
            return GetQPayResponse<QryOrderUnCapturedReq, QryOrderUnCaptured>(req, APIService.OrderUnCapturedQuery);
        }
        #endregion

        #region 信用卡訂單維護
        /// <summary>
        /// 信用卡訂單維護
        /// </summary>
        /// <param name="req"></param>
        /// <example>
        /// 串接範例如下:
        /// OrderMaintain retObj = QPayToolkit.OrderMaintain(new OrderMaintainReq() { ... });
        /// </example>
        public static OrderMaintain OrderMaintain(OrderMaintainReq req)
        {
            return GetQPayResponse<OrderMaintainReq, OrderMaintain>(req, APIService.OrderMaintain);
        }
        #endregion

        #region 訂單查詢
        /// <summary>
        /// 訂單查詢
        /// </summary>
        /// <param name="req"></param>
        /// <example>
        /// 串接範例如下:
        /// QryOrder retObj = QPayToolkit.OrderQuery(new QryOrderReq() { ... });
        /// </example>
        public static QryOrder OrderQuery(QryOrderReq req)
        {
            return GetQPayResponse<QryOrderReq, QryOrder>(req, APIService.OrderQuery);
        }
        #endregion

        #region 付款結果查詢服務
        /// <summary>
        /// 付款結果查詢服務
        /// </summary>
        /// <param name="req"></param>
        /// <example>
        /// 串接範例如下:
        /// QryOrderPay retObj = QPayToolkit.OrderPayQuery(new QryOrderPayReq() { ... });
        /// </example>
        public static QryOrderPay OrderPayQuery(QryOrderPayReq req)
        {
            return GetQPayResponse<QryOrderPayReq, QryOrderPay>(req, APIService.OrderPayQuery);
        }
        public static QryOrderPay OrderPayQuery(QryOrderPayReq req, String HashCode)
        {
            return GetQPayResponse<QryOrderPayReq, QryOrderPay>(req, APIService.OrderPayQuery, HashCode);
        }
        #endregion

        #region 每日收(退)款查詢服務
        /// <summary>
        /// 每日收(退)款查詢服務
        /// </summary>
        /// <param name="req"></param>
        /// <example>
        /// 串接範例如下:
        /// QryBill retObj = QPayToolkit.BillQuery(new QryBillReq() { ... });
        /// </example>
        public static QryBill BillQuery(QryBillReq req)
        {
            return GetQPayResponse<QryBillReq, QryBill>(req, APIService.BillQuery);
        }
        #endregion

        #region 撥款檔查詢服務
        /// <summary>
        /// 撥款檔查詢服務
        /// </summary>
        /// <param name="req"></param>
        /// <example>
        /// 串接範例如下:
        /// QryAllot retObj = QPayToolkit.AllotQuery(new QryAllotReq() { ... });
        /// </example>
        public static QryAllot AllotQuery(QryAllotReq req)
        {
            return GetQPayResponse<QryAllotReq, QryAllot>(req, APIService.AllotQuery);
        }
        #endregion
        #endregion

        #region Private method
        #region 取得QPay Web API response
        private static TResult GetQPayResponse<TReq, TResult>(TReq request, APIService apiService) where TReq : IQPayReq
        {
            //string shopNo = request.ShopNo;
            string shopNo = request.ShopNo;
            //由appSettings取得指定商店雜湊值  ex <add key="AA0001" value="...,...,...,..."/>
            //string apiKeyData = ConfigurationManager.AppSettings.Get(shopNo);
            //if (string.IsNullOrEmpty(apiKeyData))
            //    throw new Exception("AppSettings.config 中不存在指定商店API Keys");

            //將取得雜湊值以逗號(,)分隔並轉小寫，產生string陣列
            //string[] apiKeys = apiKeyData.ToLower().Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            string[] apiKeys = HASH_CODE.ToLower().Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            //string[] apiKeys = "5E854757C751413F,D743D0EB06904837,08169D5445644513,8E52B5A180EE4399".ToLower().Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            //string[] apiKeys = "D1695F439A69448F,7E460E920A184845,DEA83EFB714943F3,DC237C5C69914F0C".ToLower().Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

            //產生取Nonce Request
            NonceReq nonceReq = new NonceReq(shopNo);

            //發送Request並取得Nonce Responce
            NonceRes nonceRes = GetNonce(nonceReq).Result;

            if (string.IsNullOrEmpty(nonceRes.Nonce))
                throw new Exception("Nonce值為null或空值");

            int i;
            //1.移除雜湊中的"-"
            //2.取得雜湊的前16碼
            //3.將步驟2結果轉為16進制byte陣列
            List<byte[]> keyList = apiKeys.ToList().Select(x => Hex.GetBytes(x.Replace("-", "").Substring(0, 16), out i)).ToList();

            string
                sha256,
                iv,
                //1.分別將 雜湊A1 XOR 雜湊A2, 雜湊B1 XOR 雜湊B2
                //2.將步驟1的兩個結果各自轉為16進制字串 S1, S2
                //3.AESKey = S1 + S2
                aesKey = Hex.ToString(QPayCommon.XOR(keyList[0], keyList[1])) + Hex.ToString(QPayCommon.XOR(keyList[2], keyList[3])),
                //之前取得之Nonce
                nonce = nonceRes.Nonce,
                //序列化之Request物件
                innerJson = QPayCommon.SerializeToJson(request),
                //利用 AESKey, Nonce進行AESCBC加密，加密內文(提供out SHA256及 out iv可供後續驗證)
                msg = QPayCommon.EncryptAesData(aesKey, innerJson, nonce, out sha256, out iv);

            //產生WebAPIMessage
            WebAPIMessage req = new WebAPIMessage()
            {
                Version = _currentVersion,
                ShopNo = shopNo,
                APIService = apiService.ToString(),
                Nonce = nonce,
                Message = msg,
                //利用Request物件, AESKey及Nonce組成Sign值
                Sign = request.GenerateSign(aesKey, nonce)
            };

            try
            {
                QPayCommon.InfoLog(string.Format("呼叫商業收付API Order/{0} , Request:{1}", req.APIService, QPayCommon.SerializeToJson(req)));

                //呼叫商業收付Web API
                WebAPIMessage result = NewAPI<WebAPIMessage>("Order", req).Result;

                QPayCommon.InfoLog(string.Format("呼叫商業收付API Order/{0} , Response:{1}", req.APIService, QPayCommon.SerializeToJson(result)));

                //利用 AESKey, Nonce進行AESCBC解密，解密內文(提供out SHA256及 out iv可供後續驗證)
                string decodedMsg = QPayCommon.DecryptAesData(aesKey, result.Message, result.Nonce, out sha256, out iv);

                QPayCommon.InfoLog("Response Message:" + decodedMsg);

                //反序列化取得Response物件
                TResult innerResult = JsonConvert.DeserializeObject<TResult>(decodedMsg);

                //Sign值驗證
                string responseSign = innerResult.GenerateSign(aesKey, result.Nonce);
                if (responseSign != result.Sign)
                {
                    string validateFailMsg = "sign value validate fail!! response sign value:" + result.Sign + ", calculate sign value:" + responseSign;

                    QPayCommon.ExceptionLog(validateFailMsg);
                    throw new Exception(validateFailMsg);
                }

                return innerResult;
            }
            catch (Exception ex)
            {
                QPayCommon.ExceptionLog(null, ex);
                throw ex;
            }
        }
        private static TResult GetQPayResponse<TReq, TResult>(TReq request, APIService apiService, String HashCode) where TReq : IQPayReq
        {
            //string shopNo = request.ShopNo;
            string shopNo = request.ShopNo;
            //由appSettings取得指定商店雜湊值  ex <add key="AA0001" value="...,...,...,..."/>
            //string apiKeyData = ConfigurationManager.AppSettings.Get(shopNo);
            //if (string.IsNullOrEmpty(apiKeyData))
            //    throw new Exception("AppSettings.config 中不存在指定商店API Keys");

            //將取得雜湊值以逗號(,)分隔並轉小寫，產生string陣列
            //string[] apiKeys = apiKeyData.ToLower().Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            //string[] apiKeys = "5E854757C751413F,D743D0EB06904837,08169D5445644513,8E52B5A180EE4399".ToLower().Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            //string[] apiKeys = apiKeyData.ToLower().Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            string[] apiKeys = HashCode.ToLower().Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            //string[] apiKeys = "5E854757C751413F,D743D0EB06904837,08169D5445644513,8E52B5A180EE4399".ToLower().Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            //string[] apiKeys = "D1695F439A69448F,7E460E920A184845,DEA83EFB714943F3,DC237C5C69914F0C".ToLower().Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

            //產生取Nonce Request
            NonceReq nonceReq = new NonceReq(shopNo);

            //發送Request並取得Nonce Responce
            NonceRes nonceRes = GetNonce(nonceReq).Result;

            if (string.IsNullOrEmpty(nonceRes.Nonce))
                throw new Exception("Nonce值為null或空值");

            int i;
            //1.移除雜湊中的"-"
            //2.取得雜湊的前16碼
            //3.將步驟2結果轉為16進制byte陣列
            List<byte[]> keyList = apiKeys.ToList().Select(x => Hex.GetBytes(x.Replace("-", "").Substring(0, 16), out i)).ToList();

            string
                sha256,
                iv,
                //1.分別將 雜湊A1 XOR 雜湊A2, 雜湊B1 XOR 雜湊B2
                //2.將步驟1的兩個結果各自轉為16進制字串 S1, S2
                //3.AESKey = S1 + S2
                aesKey = Hex.ToString(QPayCommon.XOR(keyList[0], keyList[1])) + Hex.ToString(QPayCommon.XOR(keyList[2], keyList[3])),
                //之前取得之Nonce
                nonce = nonceRes.Nonce,
                //序列化之Request物件
                innerJson = QPayCommon.SerializeToJson(request),
                //利用 AESKey, Nonce進行AESCBC加密，加密內文(提供out SHA256及 out iv可供後續驗證)
                msg = QPayCommon.EncryptAesData(aesKey, innerJson, nonce, out sha256, out iv);

            //產生WebAPIMessage
            WebAPIMessage req = new WebAPIMessage()
            {
                Version = _currentVersion,
                ShopNo = shopNo,
                APIService = apiService.ToString(),
                Nonce = nonce,
                Message = msg,
                //利用Request物件, AESKey及Nonce組成Sign值
                Sign = request.GenerateSign(aesKey, nonce)
            };

            try
            {
                QPayCommon.InfoLog(string.Format("呼叫商業收付API Order/{0} , Request:{1}", req.APIService, QPayCommon.SerializeToJson(req)));

                //呼叫商業收付Web API
                WebAPIMessage result = NewAPI<WebAPIMessage>("Order", req).Result;

                QPayCommon.InfoLog(string.Format("呼叫商業收付API Order/{0} , Response:{1}", req.APIService, QPayCommon.SerializeToJson(result)));

                //利用 AESKey, Nonce進行AESCBC解密，解密內文(提供out SHA256及 out iv可供後續驗證)
                string decodedMsg = QPayCommon.DecryptAesData(aesKey, result.Message, result.Nonce, out sha256, out iv);

                QPayCommon.InfoLog("Response Message:" + decodedMsg);

                //反序列化取得Response物件
                TResult innerResult = JsonConvert.DeserializeObject<TResult>(decodedMsg);

                //Sign值驗證
                string responseSign = innerResult.GenerateSign(aesKey, result.Nonce);
                if (responseSign != result.Sign)
                {
                    string validateFailMsg = "sign value validate fail!! response sign value:" + result.Sign + ", calculate sign value:" + responseSign;

                    QPayCommon.ExceptionLog(validateFailMsg);
                    throw new Exception(validateFailMsg);
                }

                return innerResult;
            }
            catch (Exception ex)
            {
                QPayCommon.ExceptionLog(null, ex);
                throw ex;
            }
        }
        #endregion

        #region APIClient
        #region 呼叫Nonce API(一次性數值)
        private static async Task<NonceRes> GetNonce(NonceReq req)
        {
            string url = _site + "Nonce";

            HttpResponseMessage responce;

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("X-KeyID", X_KEY_ID);
                responce = client.PostAsJsonAsync(url, req).Result;
            }

            NonceRes res = new NonceRes();

            if (responce.IsSuccessStatusCode)
            {
                res = await responce.Content.ReadAsAsync<NonceRes>();
            }
            else
            {
                QPayCommon.ExceptionLog("Get nonce failed. StatusCode : " + responce.StatusCode);
                res = new NonceRes();
            }

            return res;
        }
        #endregion

        #region 呼叫商店API
        private static async Task<T> NewAPI<T>(string route, WebAPIMessage req) where T : new()
        {
            string url = _site + route;

            HttpResponseMessage response;

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("X-KeyID", X_KEY_ID);
                response = client.PostAsJsonAsync(url, req).Result;
            }

            T res;
            if (response.IsSuccessStatusCode)
            {
                res = await response.Content.ReadAsAsync<T>();
            }
            else
            {
                QPayCommon.ExceptionLog(string.Format("Call API {0} failed. StatusCode : {1}", req.APIService, response.StatusCode));
                throw new Exception(response.Content.ReadAsStringAsync().Result);
            }

            return res;
        }
        #endregion
        #endregion
        #endregion
    }

}

