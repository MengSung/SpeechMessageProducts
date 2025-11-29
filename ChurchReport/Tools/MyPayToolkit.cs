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
using ChurchReport.Models;

namespace ChurchReport.Tools
{
    public class MyPayStoreOrder
    {
        #region 設定與配置
        private static ConfigurationBuilder m_ConfigurationBuilder = (ConfigurationBuilder)new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory()).AddJsonFile("appsettings.json");
        private static IConfiguration m_Configuration = m_ConfigurationBuilder.Build();
        #endregion
        /// <summary>
        /// 特約商店商務代號
        /// </summary>
        public string storeUid = "200043350001";
        /// <summary>
        /// 特約商店金鑰或認證碼
        /// </summary>
        public string storeKey = "iFDYTvaj6AfsEYzA8oA1EdtGQwkLLbR5";
        /// <summary>
        /// 串接交易位置
        /// </summary>
        public string url = "https://ka.usecase.cc/api/init";

        /// <summary>
        /// 初始化 StoreOrder 實例，從設定檔載入 MyPay 相關設定
        /// </summary>
        public MyPayStoreOrder()
        {
            this.storeUid = m_Configuration["MyPay:Store_Id"];
            this.storeKey = m_Configuration["MyPay:Key"];
            this.url = m_Configuration["MyPay:Url"];
        }

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
        /// 嘗試從 Encrypt 的輸出（Base64 組合：IV + Cipher）還原明文
        /// 若解密失敗，會拋出例外以便定位問題
        /// </summary>
        /// <param name="combinedBase64">由 Encrypt() 回傳的 Base64 字串（IV + cipher）</param>
        /// <param name="key">storeKey 或 agentKey 原始字串</param>
        /// <returns>還原的明文字串</returns>
        private static string DecryptFromCombinedBase64(string combinedBase64, string key)
        {
            if (string.IsNullOrEmpty(combinedBase64))
                throw new ArgumentException("payload 為空", nameof(combinedBase64));

            byte[] combinedBytes;
            try
            {
                combinedBytes = Convert.FromBase64String(combinedBase64);
            }
            catch (FormatException fe)
            {
                throw new FormatException("payload 不是合法的 Base64 字串", fe);
            }

            if (combinedBytes.Length <= 16)
                throw new InvalidOperationException("payload 長度不足以包含 IV 與 cipher 資料");

            // 前 16 bytes 為 IV，餘下為 cipher
            byte[] iv = combinedBytes.Take(16).ToArray();
            byte[] cipher = combinedBytes.Skip(16).ToArray();

            // 取得 32 bytes 的 key：若原始 key bytes 不是 32，使用 SHA256 派生
            byte[] keyBytes = Encoding.UTF8.GetBytes(key ?? string.Empty);
            if (keyBytes.Length != 32)
            {
                using (var sha = SHA256.Create())
                {
                    keyBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(key ?? string.Empty));
                }
            }

            try
            {
                using (var aes = Aes.Create())
                {
                    aes.KeySize = 256;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;
                    aes.Key = keyBytes;
                    aes.IV = iv;

                    using (var decryptor = aes.CreateDecryptor())
                    {
                        var plainBytes = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
                        return Encoding.UTF8.GetString(plainBytes);
                    }
                }
            }
            catch (Exception ex)
            {
                throw new CryptographicException("解密失敗，可能是 key/IV/模式或填充不一致: " + ex.Message, ex);
            }
        }

        /// <summary>
        /// 取得送出欄位資料（含本地自檢，若自檢失敗會拋出例外）
        /// </summary>
        public NameValueCollection GetPostData(ExpandoObject customData, ServiceRequest Service)
        {
            //string data_json = JsonConvert.SerializeObject(GetRawData(customData), Formatting.None);
            string data_json = JsonConvert.SerializeObject(customData, Formatting.None);
            string svr_json = JsonConvert.SerializeObject(Service, Formatting.None);//依API種類調整

            //產生AES向量
            var IV = GetBytesIV();

            //進行加密
            var data_encode = Encrypt(data_json, this.storeKey, IV);
            var svr_encode = Encrypt(svr_json, this.storeKey, IV);

            // ========== 本地自檢：嘗試還原剛剛加密的內容，若失敗則拋出清楚錯誤 ==========
            try
            {
                // 若無法還原，會拋出例外，供呼叫端檢查
                var decodedData = DecryptFromCombinedBase64(data_encode, this.storeKey);
                var decodedService = DecryptFromCombinedBase64(svr_encode, this.storeKey);

                // 可選：在開發或偵錯模式下記錄（此處不寫入日誌以避免洩漏）
                // System.Diagnostics.Debug.WriteLine($"DecryptedData: {decodedData}");
            }
            catch (Exception ex)
            {
                // 將錯誤包裝並拋出，讓上層能夠取得更明確的錯誤訊息
                throw new InvalidOperationException("本地加密自檢失敗: " + ex.Message + "。請確認 storeKey、IV 生成與 URL encode 流程是否與 MyPay 規格相符。", ex);
            }

            //請注意使用的 Http Post 套件是否會自動加上UrlEncode，本Post範例為原始方式，故須加上UrlEncode
            //若自行使用的套件會自動補上UrlEncode，則請忽略下面的UrlEncode，避免做了兩次UrlEncode
            string data_toUrlEncode = HttpUtility.UrlEncode(data_encode);
            string svr_toUrlEncode = HttpUtility.UrlEncode(svr_encode);

            NameValueCollection postData = new NameValueCollection();
            postData["store_uid"] = this.storeUid;
            postData["agent_uid"] = this.storeUid;  // 添加必填的 agent_uid 欄位
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
            using (var aes = Aes.Create())
            {
                aes.KeySize = 256;
                aes.GenerateIV();
                return aes.IV;
            }
        }
        /// <summary>
        /// 資料 POST 到主機
        /// </summary>
        /// <param name="pars"></param>
        /// <returns></returns>
        public PayPageResponse Post(NameValueCollection pars)
        {
            string result = string.Empty;
            string param = string.Empty;

            PayPageResponse retObj;

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
                        // 將結果反序列 成為CLASS
                        retObj = JsonConvert.DeserializeObject<PayPageResponse>(result);
                    }
                }

                req = null;
            }
            catch (WebException ex)
            {
                throw new WebException(ex.Message + "params : " + param, ex, ex.Status, ex.Response);
            }
            return retObj;
        }


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
        //private static string _site = m_Configuration["Sandbox:Site"];

        //// SANDBOX 測試用
        //private static String A1 = m_Configuration["Sandbox:A1"];
        //private static String A2 = m_Configuration["Sandbox:A2"];
        //private static String B1 = m_Configuration["Sandbox:B1"];
        //private static String B2 = m_Configuration["Sandbox:B2"];
        //private static String HASH_CODE = A1 + "," + A2 + "," + B1 + "," + B2;

        //private static String X_KEY_ID = m_Configuration["Sandbox:XKeyID"];

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
        private static TResult GetQPayResponse<TReq, TResult>(TReq request, APIService apiService, string hashCode = null) where TReq : IQPayReq
        {
            try
            {
                TResult innerResult = Activator.CreateInstance<TResult>();

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

        #endregion
        #endregion
    }

}

