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

namespace ChurchReport.Tools
{
    public static class QPayToolkit
    {
        static ConfigurationBuilder m_ConfigurationBuilder = (ConfigurationBuilder)new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory()).AddJsonFile("appsettings.json");
        static IConfiguration m_Configuration = m_ConfigurationBuilder.Build();

        private static string _currentVersion = "1.0.0";

        // SANDBOX 測試用
        private static string _site = m_Configuration["Sandbox:Site"];

        private static String A1 = m_Configuration["Sandbox:A1"];
        private static String A2 = m_Configuration["Sandbox:A2"];
        private static String B1 = m_Configuration["Sandbox:B1"];
        private static String B2 = m_Configuration["Sandbox:B2"];
        private static String HASH_CODE = A1 + "," + A2 + "," + B1 + "," + B2;

        private static String X_KEY_ID = m_Configuration["Sandbox:XKeyID"];

        #region Static Constructor
        static QPayToolkit()
        {
            // 根據 Cash_Environment 配置決定使用正式環境還是測試環境
            string cashEnvironment = m_Configuration["Cash_Environment"];

            if (cashEnvironment == "正式環境")
            {
                // 永豐金流正式環境
                _site = m_Configuration["Sinopac:Site"];
                A1 = m_Configuration["Sinopac:A1"];
                A2 = m_Configuration["Sinopac:A2"];
                B1 = m_Configuration["Sinopac:B1"];
                B2 = m_Configuration["Sinopac:B2"];
                X_KEY_ID = m_Configuration["Sinopac:XKeyID"];
            }
            else
            {
                // SANDBOX 測試用
                _site = m_Configuration["Sandbox:Site"];
                A1 = m_Configuration["Sandbox:A1"];
                A2 = m_Configuration["Sandbox:A2"];
                B1 = m_Configuration["Sandbox:B1"];
                B2 = m_Configuration["Sandbox:B2"];
                X_KEY_ID = m_Configuration["Sandbox:XKeyID"];
            }

            // 重新計算 HASH_CODE
            HASH_CODE = A1 + "," + A2 + "," + B1 + "," + B2;
        }
        #endregion

        #region Public method
        #region 訂單建立 (虛擬帳號、信用卡)
        /// <summary>
        /// 訂單建立 (虛擬帳號、信用卡) - 同步版本
        /// </summary>
        /// <remarks>
        /// ⚠️ 建議使用 OrderCreateAsync 非同步版本以避免執行緒阻塞
        /// </remarks>
        public static CreOrder OrderCreate(CreOrderReq req)
        {
            return GetQPayResponseAsync<CreOrderReq, CreOrder>(req, APIService.OrderCreate).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 訂單建立 (虛擬帳號、信用卡) - 非同步版本
        /// ✅ Phase 7: 新增非同步 API
        /// </summary>
        public static async Task<CreOrder> OrderCreateAsync(CreOrderReq req)
        {
            return await GetQPayResponseAsync<CreOrderReq, CreOrder>(req, APIService.OrderCreate);
        }
        #endregion

        #region 待請款訂單查詢
        /// <summary>
        /// 待請款訂單查詢 - 同步版本
        /// </summary>
        public static QryOrderUnCaptured OrderUnCapturedQuery(QryOrderUnCapturedReq req)
        {
            return GetQPayResponseAsync<QryOrderUnCapturedReq, QryOrderUnCaptured>(req, APIService.OrderUnCapturedQuery).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 待請款訂單查詢 - 非同步版本
        /// ✅ Phase 7: 新增非同步 API
        /// </summary>
        public static async Task<QryOrderUnCaptured> OrderUnCapturedQueryAsync(QryOrderUnCapturedReq req)
        {
            return await GetQPayResponseAsync<QryOrderUnCapturedReq, QryOrderUnCaptured>(req, APIService.OrderUnCapturedQuery);
        }
        #endregion

        #region 信用卡訂單維護
        /// <summary>
        /// 信用卡訂單維護 - 同步版本
        /// </summary>
        public static OrderMaintain OrderMaintain(OrderMaintainReq req)
        {
            return GetQPayResponseAsync<OrderMaintainReq, OrderMaintain>(req, APIService.OrderMaintain).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 信用卡訂單維護 - 非同步版本
        /// ✅ Phase 7: 新增非同步 API
        /// </summary>
        public static async Task<OrderMaintain> OrderMaintainAsync(OrderMaintainReq req)
        {
            return await GetQPayResponseAsync<OrderMaintainReq, OrderMaintain>(req, APIService.OrderMaintain);
        }
        #endregion

        #region 訂單查詢
        /// <summary>
        /// 訂單查詢 - 同步版本
        /// </summary>
        public static QryOrder OrderQuery(QryOrderReq req)
        {
            return GetQPayResponseAsync<QryOrderReq, QryOrder>(req, APIService.OrderQuery).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 訂單查詢 - 非同步版本
        /// ✅ Phase 7: 新增非同步 API
        /// </summary>
        public static async Task<QryOrder> OrderQueryAsync(QryOrderReq req)
        {
            return await GetQPayResponseAsync<QryOrderReq, QryOrder>(req, APIService.OrderQuery);
        }
        #endregion

        #region 付款結果查詢服務
        /// <summary>
        /// 付款結果查詢服務 - 同步版本
        /// </summary>
        public static QryOrderPay OrderPayQuery(QryOrderPayReq req)
        {
            return GetQPayResponseAsync<QryOrderPayReq, QryOrderPay>(req, APIService.OrderPayQuery).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 付款結果查詢服務 - 非同步版本
        /// ✅ Phase 7: 新增非同步 API
        /// </summary>
        public static async Task<QryOrderPay> OrderPayQueryAsync(QryOrderPayReq req)
        {
            return await GetQPayResponseAsync<QryOrderPayReq, QryOrderPay>(req, APIService.OrderPayQuery);
        }

        /// <summary>
        /// 付款結果查詢服務（指定 HashCode）- 同步版本
        /// </summary>
        public static QryOrderPay OrderPayQuery(QryOrderPayReq req, String HashCode)
        {
            return GetQPayResponseAsync<QryOrderPayReq, QryOrderPay>(req, APIService.OrderPayQuery, HashCode).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 付款結果查詢服務（指定 HashCode）- 非同步版本
        /// ✅ Phase 7: 新增非同步 API
        /// </summary>
        public static async Task<QryOrderPay> OrderPayQueryAsync(QryOrderPayReq req, String HashCode)
        {
            return await GetQPayResponseAsync<QryOrderPayReq, QryOrderPay>(req, APIService.OrderPayQuery, HashCode);
        }
        #endregion

        #region 每日收(退)款查詢服務
        /// <summary>
        /// 每日收(退)款查詢服務 - 同步版本
        /// </summary>
        public static QryBill BillQuery(QryBillReq req)
        {
            return GetQPayResponseAsync<QryBillReq, QryBill>(req, APIService.BillQuery).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 每日收(退)款查詢服務 - 非同步版本
        /// ✅ Phase 7: 新增非同步 API
        /// </summary>
        public static async Task<QryBill> BillQueryAsync(QryBillReq req)
        {
            return await GetQPayResponseAsync<QryBillReq, QryBill>(req, APIService.BillQuery);
        }
        #endregion

        #region 撥款檔查詢服務
        /// <summary>
        /// 撥款檔查詢服務 - 同步版本
        /// </summary>
        public static QryAllot AllotQuery(QryAllotReq req)
        {
            return GetQPayResponseAsync<QryAllotReq, QryAllot>(req, APIService.AllotQuery).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 撥款檔查詢服務 - 非同步版本
        /// ✅ Phase 7: 新增非同步 API
        /// </summary>
        public static async Task<QryAllot> AllotQueryAsync(QryAllotReq req)
        {
            return await GetQPayResponseAsync<QryAllotReq, QryAllot>(req, APIService.AllotQuery);
        }
        #endregion
        #endregion

        #region Private method
        #region 取得QPay Web API response (非同步版本)
        /// <summary>
        /// 取得 QPay Web API Response - 非同步版本
        /// ✅ Phase 7: 核心方法改為完全非同步
        /// </summary>
        private static async Task<TResult> GetQPayResponseAsync<TReq, TResult>(TReq request, APIService apiService) where TReq : IQPayReq
        {
            string shopNo = request.ShopNo;
            string[] apiKeys = HASH_CODE.ToLower().Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

            //產生取Nonce Request
            NonceReq nonceReq = new NonceReq(shopNo);

            //發送Request並取得Nonce Responce - 使用 await
            NonceRes nonceRes = await GetNonce(nonceReq);

            if (string.IsNullOrEmpty(nonceRes.Nonce))
                throw new Exception("Nonce值為null或空值");

            int i;
            List<byte[]> keyList = apiKeys.ToList().Select(x => Hex.GetBytes(x.Replace("-", "").Substring(0, 16), out i)).ToList();

            string
                sha256,
                iv,
                aesKey = Hex.ToString(QPayCommon.XOR(keyList[0], keyList[1])) + Hex.ToString(QPayCommon.XOR(keyList[2], keyList[3])),
                nonce = nonceRes.Nonce,
                innerJson = QPayCommon.SerializeToJson(request),
                msg = QPayCommon.EncryptAesData(aesKey, innerJson, nonce, out sha256, out iv);

            WebAPIMessage req = new WebAPIMessage()
            {
                Version = _currentVersion,
                ShopNo = shopNo,
                APIService = apiService.ToString(),
                Nonce = nonce,
                Message = msg,
                Sign = request.GenerateSign(aesKey, nonce)
            };

            try
            {
                QPayCommon.InfoLog(string.Format("呼叫商業收付API Order/{0} , Request:{1}", req.APIService, QPayCommon.SerializeToJson(req)));

                //呼叫商業收付Web API - 使用 await
                WebAPIMessage result = await NewAPI<WebAPIMessage>("Order", req);

                QPayCommon.InfoLog(string.Format("呼叫商業收付API Order/{0} , Response:{1}", req.APIService, QPayCommon.SerializeToJson(result)));

                string decodedMsg = QPayCommon.DecryptAesData(aesKey, result.Message, result.Nonce, out sha256, out iv);

                QPayCommon.InfoLog("Response Message:" + decodedMsg);

                TResult innerResult = JsonConvert.DeserializeObject<TResult>(decodedMsg);

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
                throw;
            }
        }

        /// <summary>
        /// 取得 QPay Web API Response（指定 HashCode）- 非同步版本
        /// ✅ Phase 7: 核心方法改為完全非同步
        /// </summary>
        private static async Task<TResult> GetQPayResponseAsync<TReq, TResult>(TReq request, APIService apiService, String HashCode) where TReq : IQPayReq
        {
            string shopNo = request.ShopNo;
            string[] apiKeys = HashCode.ToLower().Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

            NonceReq nonceReq = new NonceReq(shopNo);

            //發送Request並取得Nonce Responce - 使用 await
            NonceRes nonceRes = await GetNonce(nonceReq);

            if (string.IsNullOrEmpty(nonceRes.Nonce))
                throw new Exception("Nonce值為null或空值");

            int i;
            List<byte[]> keyList = apiKeys.ToList().Select(x => Hex.GetBytes(x.Replace("-", "").Substring(0, 16), out i)).ToList();

            string
                sha256,
                iv,
                aesKey = Hex.ToString(QPayCommon.XOR(keyList[0], keyList[1])) + Hex.ToString(QPayCommon.XOR(keyList[2], keyList[3])),
                nonce = nonceRes.Nonce,
                innerJson = QPayCommon.SerializeToJson(request),
                msg = QPayCommon.EncryptAesData(aesKey, innerJson, nonce, out sha256, out iv);

            WebAPIMessage req = new WebAPIMessage()
            {
                Version = _currentVersion,
                ShopNo = shopNo,
                APIService = apiService.ToString(),
                Nonce = nonce,
                Message = msg,
                Sign = request.GenerateSign(aesKey, nonce)
            };

            try
            {
                QPayCommon.InfoLog(string.Format("呼叫商業收付API Order/{0} , Request:{1}", req.APIService, QPayCommon.SerializeToJson(req)));

                //呼叫商業收付Web API - 使用 await
                WebAPIMessage result = await NewAPI<WebAPIMessage>("Order", req);

                QPayCommon.InfoLog(string.Format("呼叫商業收付API Order/{0} , Response:{1}", req.APIService, QPayCommon.SerializeToJson(result)));

                string decodedMsg = QPayCommon.DecryptAesData(aesKey, result.Message, result.Nonce, out sha256, out iv);

                QPayCommon.InfoLog("Response Message:" + decodedMsg);

                TResult innerResult = JsonConvert.DeserializeObject<TResult>(decodedMsg);

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
                throw;
            }
        }
        #endregion

        #region APIClient
        #region 呼叫Nonce API(一次性數值)
        private static async Task<NonceRes> GetNonce(NonceReq req)
        {
            string url = _site + "Nonce";

            HttpResponseMessage responce;

            // ✅ 使用靜態 HttpClient 單例
            responce = await SharedHttpClient.PostAsJsonAsync(url, req);

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

            // ✅ 使用靜態 HttpClient 單例
            response = await SharedHttpClient.PostAsJsonAsync(url, req);

            T res;
            if (response.IsSuccessStatusCode)
            {
                res = await response.Content.ReadAsAsync<T>();
            }
            else
            {
                QPayCommon.ExceptionLog(string.Format("Call API {0} failed. StatusCode : {1}", req.APIService, response.StatusCode));
                // ✅ 修復：使用 await 而非 .Result
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new Exception(errorContent);
            }

            return res;
        }
        #endregion
        #endregion
        #endregion

        // ========================================
        // 🔧 修復記憶體洩漏：使用靜態 HttpClient 單例
        // ========================================
        private static readonly Lazy<System.Net.Http.HttpClient> _lazyHttpClient = new Lazy<System.Net.Http.HttpClient>(() =>
        {
            var client = new System.Net.Http.HttpClient();
            client.DefaultRequestHeaders.Add("X-KeyID", X_KEY_ID);
            // 設定合理的 Timeout
            client.Timeout = TimeSpan.FromSeconds(30);
            return client;
        });

        private static System.Net.Http.HttpClient SharedHttpClient => _lazyHttpClient.Value;
    }

    public static class QPayCommon
    {
        #region 共用方法
        /// <summary>
        /// 寫入Info log
        /// </summary>
        /// <remarks>可自行實作</remarks>
        public static void InfoLog(string message)
        {
            //Info Log
            //throw new NotImplementedException();
        }

        /// <summary>
        /// 寫入Exception log
        /// </summary>
        /// <remarks>可自行實作</remarks>
        public static void ExceptionLog(string exMessage, Exception ex = null)
        {
            //Excetpion Log
            //throw new NotImplementedException();
        }

        /// <summary>
        /// 序列化物件(忽略null或default值之properties)
        /// </summary>
        public static string SerializeToJson<T>(this T data)
        {
            //value為null或空值則不序列化(空值則需在property上加attribute)
            string result = JsonConvert.SerializeObject(data, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore, DefaultValueHandling = DefaultValueHandling.Ignore });
            return result;
        }

        /// <summary>
        /// XOR
        /// </summary>
        /// <remarks>陣列需同長度</remarks>
        public static byte[] XOR(byte[] arr1, byte[] arr2)
        {
            if (arr1.Length != arr2.Length)
                throw new ArgumentException("arr1 and arr2 are not the same length");

            byte[] result = new byte[arr1.Length];

            for (int i = 0; i < arr1.Length; ++i)
                result[i] = (byte)(arr1[i] ^ arr2[i]);

            return result;
        }

        /// <summary>
        /// byte[] 轉為 16進制字串
        /// </summary>
        public static string ToHex(this byte[] bytes, bool uppercase = false)
        {
            return string.Concat(from x in bytes
                                 select x.ToString(uppercase ? "X2" : "x2"));
        }
        #endregion

        #region Hash, Sign

        /// <summary>
        /// 加密演算法列舉
        /// </summary>
        public enum HashAlgorithmName
        {
            MD5,
            SHA1,
            SHA256,
            SHA512
        }
        /// <summary>
        /// 產生Sign值
        /// </summary>
        /// <remarks>Hash(Sign字串 + Nonce + AESKey), 再將結果轉為16進制字串(小寫)</remarks>
        public static string GenerateSign<T>(this T o, string key, string nonce, [CallerMemberName] string callerMethod = "")
        {
            string sign = string.Concat(GetSigningString(o, callerMethod), nonce, key).Hash(HashAlgorithmName.SHA256, Encoding.UTF8).ToHex(false);
            return sign.ToUpper();
        }

        /// <summary>
        /// 物件產生Sign字串
        /// </summary>
        /// <remarks>忽略有SignExcludeAttribute之properties</remarks>
        public static string GetSigningString<T>(this T obj, [CallerMemberName] string callerMethod = "")
        {
            if (obj == null)
                throw new ArgumentNullException("Request Object is null");
            var dic = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var type = obj.GetType();
            foreach (var p in type.GetMembers().Where(x => x.MemberType == MemberTypes.Property))
            {
                var m = p as PropertyInfo;
                //排除SignExcludeAttribute
                if (m.GetCustomAttribute<SignExcludeAttribute>() == null)
                {
                    var x = type.GetProperty(m.Name).GetValue(obj);
                    if (x != null)
                    {
                        if (m.PropertyType.Assembly != type.Assembly && x.GetType().Name != "List`1" && x.GetType().Name != "Dictionary`2")
                        {
                            dic[m.Name] = x.ToString();
                        }
                    }
                }
            }

            string signString = string.Join("&", dic.Where(x => !string.IsNullOrWhiteSpace(x.Value)).Select(x => string.Format("{0}={1}", x.Key, x.Value)));   //value為null或空值則不加入sign值計算
            return signString;
        }

        public static byte[] Hash(this string s, HashAlgorithmName name, Encoding encoding = null)
        {
            bool flag = string.IsNullOrWhiteSpace(s);
            if (flag)
            {
                throw new ArgumentNullException();
            }
            bool flag2 = encoding == null;
            if (flag2)
            {
                encoding = Encoding.UTF8;
            }
            return Hash(encoding.GetBytes(s), name);
        }

        public static byte[] Hash(byte[] bytes, HashAlgorithmName name)
        {
            bool flag = bytes == null || bytes.Length == 0;
            if (flag)
            {
                throw new ArgumentNullException();
            }
            byte[] result;
            using (HashAlgorithm algorithm = GetAlgorithm(name))
            {
                result = algorithm.ComputeHash(bytes);
            }
            return result;
        }

        /// <summary>
        /// 取得加密演算法
        /// </summary>
        public static HashAlgorithm GetAlgorithm(HashAlgorithmName algoName)
        {
            HashAlgorithm result;
            switch (algoName)
            {
                case HashAlgorithmName.MD5:
                    result = new MD5CryptoServiceProvider();
                    break;
                case HashAlgorithmName.SHA1:
                    result = new SHA1Managed();
                    break;
                case HashAlgorithmName.SHA256:
                    result = new SHA256Managed();
                    break;
                case HashAlgorithmName.SHA512:
                    result = new SHA512Managed();
                    break;
                default:
                    result = null;
                    break;
            }
            return result;
        }
        #endregion

        #region Encrypt
        /// <summary>
        /// AES CBC加密
        /// </summary>
        /// <param name="aesKey">AESKey</param>
        /// <param name="data">欲加密內文</param>
        /// <param name="nonce">Nonce</param>
        /// <param name="sha256O">SHA值, 由Nonce 進行SHA256加密後取得</param>
        /// <param name="ivO">iv值, SHA值末16碼</param>
        public static string EncryptAesData(string aesKey, string data, string nonce, out string sha256O, out string ivO)
        {
            string sha256 = sha256O = GetSHA256Hash(nonce), iv = ivO = sha256.Substring(sha256.Length - 16, 16), msg = EncryptAesCBC(data, aesKey, iv);
            return msg;
        }

        /// <summary>
        /// AES CBC解密
        /// </summary>
        /// <param name="aesKey">AESKey</param>
        /// <param name="cipherText">已加密內容</param>
        /// <param name="nonce">Nonce</param>
        /// <param name="sha256O">SHA值, 由Nonce 進行SHA256加密後取得</param>
        /// <param name="ivO">iv值, SHA值末16碼</param>
        public static string DecryptAesData(string aesKey, string cipherText, string nonce, out string sha256O, out string ivO)
        {
            string sha256 = sha256O = GetSHA256Hash(nonce), iv = ivO = sha256.Substring(sha256.Length - 16, 16), data = DecryptAesCBC(cipherText, aesKey, iv);
            return data;
        }

        /// <summary>
        /// 字串進行SHA256加密
        /// </summary>
        public static string GetSHA256Hash(string input)
        {
            SHA256 sha256Hasher = SHA256.Create();

            byte[] data = sha256Hasher.ComputeHash(Encoding.Default.GetBytes(input));

            return EncodingByteArraytoHexString(data);
        }

        /// <summary>
        /// Byte陣列轉為16進制字串
        /// </summary>
        private static string EncodingByteArraytoHexString(byte[] data)
        {
            StringBuilder sBuilder = new StringBuilder();

            for (int i = 0; i < data.Length; i++)
            {
                sBuilder.Append(data[i].ToString("X2"));
            }

            return sBuilder.ToString();
        }

        /// <summary>
        /// AES CBC加密
        /// </summary>
        private static string EncryptAesCBC(string source, string key, string iv)
        {
            StringBuilder sb = new StringBuilder();
            AesCryptoServiceProvider aes = new AesCryptoServiceProvider();
            byte[] keyB = Encoding.ASCII.GetBytes(key);
            byte[] ivB = Encoding.ASCII.GetBytes(iv);
            byte[] dataByteArray = Encoding.UTF8.GetBytes(source);

            aes.Key = keyB;
            aes.IV = ivB;

            string encrypt = "";
            using (MemoryStream ms = new MemoryStream())
            using (CryptoStream cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
            {
                cs.Write(dataByteArray, 0, dataByteArray.Length);
                cs.FlushFinalBlock();
                //輸出資料
                foreach (byte b in ms.ToArray())
                {
                    sb.AppendFormat("{0:X2}", b);
                }
                encrypt = sb.ToString();
            }
            return encrypt;
        }

        /// <summary>
        /// AES CBC解密
        /// </summary>
        /// <param name="cipherText"></param>
        /// <param name="key"></param>
        /// <param name="iv"></param>
        /// <returns></returns>
        private static string DecryptAesCBC(string cipherText, string key, string iv)
        {
            byte[] dataByteArray = new byte[cipherText.Length / 2];
            for (int x = 0; x < cipherText.Length / 2; x++)
            {
                int i = (Convert.ToInt32(cipherText.Substring(x * 2, 2), 16));
                dataByteArray[x] = (byte)i;
            }

            AesCryptoServiceProvider aes = new AesCryptoServiceProvider();
            byte[] keyB = Encoding.ASCII.GetBytes(key);
            byte[] ivB = Encoding.ASCII.GetBytes(iv);
            aes.Key = keyB;
            aes.IV = ivB;

            using (MemoryStream ms = new MemoryStream())
            {
                using (CryptoStream cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Write))
                {
                    cs.Write(dataByteArray, 0, dataByteArray.Length);
                    cs.FlushFinalBlock();

                    return Encoding.UTF8.GetString(ms.ToArray());
                }
            }
        }

        //16進制相關方法
        #region HexEncoding
        public class HexEncoding
        {
            public HexEncoding()
            {
            }

            public static int GetByteCount(string hexString)
            {
                int numHexChars = 0;
                char c;

                for (int i = 0; i < hexString.Length; i++)
                {
                    c = hexString[i];
                    if (IsHexDigit(c))
                        numHexChars++;
                }
                if (numHexChars % 2 != 0)
                {
                    numHexChars--;
                }
                return numHexChars / 2;
            }

            public static byte[] GetBytes(string hexString, out int discarded)
            {
                discarded = 0;
                string newString = "";
                char c;
                for (int i = 0; i < hexString.Length; i++)
                {
                    c = hexString[i];
                    if (IsHexDigit(c))
                        newString += c;
                    else
                        discarded++;
                }
                if (newString.Length % 2 != 0)
                {
                    discarded++;
                    newString = newString.Substring(0, newString.Length - 1);
                }

                int byteLength = newString.Length / 2;
                byte[] bytes = new byte[byteLength];
                string hex;
                int j = 0;
                for (int i = 0; i < bytes.Length; i++)
                {
                    hex = new string(new char[] { newString[j], newString[j + 1] });
                    bytes[i] = HexToByte(hex);
                    j = j + 2;
                }
                return bytes;
            }
            public static string ToString(byte[] bytes)
            {
                string hexString = "";
                for (int i = 0; i < bytes.Length; i++)
                {
                    hexString += bytes[i].ToString("X2");
                }
                return hexString;
            }

            public static bool InHexFormat(string hexString)
            {
                bool hexFormat = true;

                foreach (char digit in hexString)
                {
                    if (!IsHexDigit(digit))
                    {
                        hexFormat = false;
                        break;
                    }
                }
                return hexFormat;
            }

            public static bool IsHexDigit(char c)
            {
                int numChar;
                int numA = Convert.ToInt32('A');
                int num1 = Convert.ToInt32('0');
                c = char.ToUpper(c);
                numChar = Convert.ToInt32(c);
                if (numChar >= numA && numChar < (numA + 6))
                    return true;
                if (numChar >= num1 && numChar < (num1 + 10))
                    return true;
                return false;
            }

            private static byte HexToByte(string hex)
            {
                if (hex.Length > 2 || hex.Length <= 0)
                    throw new ArgumentException("hex must be 1 or 2 characters in length");
                byte newByte = byte.Parse(hex, System.Globalization.NumberStyles.HexNumber);
                return newByte;
            }
        }
        #endregion
        #endregion        
    }
}

namespace QPay.Domain
{
    #region SignExcludeAttribute
    /// <summary>
    /// SignExclude
    /// </summary>
    /// <remarks>有此Attribute的properties不加入Sign計算</remarks>
    [AttributeUsage(AttributeTargets.Property)]
    public class SignExcludeAttribute : Attribute
    {
    }
    #endregion

    #region APIService
    /// <summary>
    /// 商業收付API列舉
    /// </summary>
    public enum APIService
    {
        OrderCreate = 1,
        OrderQuery = 2,
        OrderUnCapturedQuery = 3,
        OrderMaintain = 4,
        OrderPayQuery = 5,
        BillQuery = 6,
        AllotQuery = 7,
    }
    #endregion

    #region Request物件        
    #region BaseReq
    public interface IQPayReq
    {
        string ShopNo { get; set; }
    }
    #endregion

    #region NonceReq
    public class NonceReq
    {
        [DataMember]
        public string ShopNo { get; set; }

        public NonceReq(string shopNo)
        {
            ShopNo = shopNo;
        }
    }
    #endregion

    #region CreOrderReq
    [DataContract]
    public class CreOrderReq : IQPayReq
    {
        /// <summary>
        /// 會員編號，例如AA0001_001
        /// </summary>
        [DataMember]
        public string ShopNo { get; set; }

        /// <summary>
        /// 訂單編號
        /// </summary>
        /// <remarks>
        /// 不可有單引號、雙引號、百分比, 最大長度50
        /// </remarks>
        [DataMember]
        public string OrderNo { get; set; }

        /// <summary>
        /// 訂單金額
        /// </summary>
        [DataMember]
        public int Amount { get; set; }

        /// <summary>
        /// 幣別
        /// </summary>
        [DataMember]
        public string CurrencyID { get; set; }

        /// <summary>
        /// 收款方式(A:ATM轉帳,C:信用卡)
        /// </summary>
        [DataMember]
        public string PayType { get; set; }

        /// <summary>
        /// ATM參數
        /// </summary>
        [DataMember]
        public CreOrderATMParamReq ATMParam { get; set; }

        /// <summary>
        /// 信用卡參數
        /// </summary>
        [DataMember]
        public CreOrderCardParamReq CardParam { get; set; }

        /// <summary>
        /// 收款名稱
        /// </summary>
        /// <remarks>
        /// 不可有單引號、雙引號、百分比, 最大長度60
        /// </remarks>
        [DataMember]
        public string PrdtName { get; set; }

        /// <summary>
        /// 付款完成轉入URL
        /// </summary>
        /// <remarks>
        /// 不可有單引號、雙引號、百分比, 最大長度255
        /// </remarks>
        [DataMember]
        public string ReturnURL { get; set; }

        /// <summary>
        /// 付款完成背端通知URL
        /// </summary>
        /// <remarks>
        /// 不可有單引號、雙引號、百分比, 最大長度255
        /// </remarks>
        [DataMember]
        public string BackendURL { get; set; }

        /// <summary>
        /// 備註
        /// </summary>
        /// <remarks>
        /// 不可有單引號、雙引號、百分比, 最大長度30
        /// </remarks>
        [DataMember]
        public string Memo { get; set; }

        /// <summary>
        /// 自訂參數一
        /// </summary>
        /// <remarks>
        /// 不可有單引號、雙引號、百分比, 最大長度255
        /// </remarks>
        [DataMember]
        public string Param1 { get; set; }

        /// <summary>
        /// 自訂參數二
        /// </summary>
        /// <remarks>
        /// 不可有單引號、雙引號、百分比, 最大長度255
        /// </remarks>
        [DataMember]
        public string Param2 { get; set; }

        /// <summary>
        /// 自訂參數三
        /// </summary>
        /// <remarks>
        /// 不可有單引號、雙引號、百分比, 最大長度255
        /// </remarks>
        [DataMember]
        public string Param3 { get; set; }
    }
    #endregion

    #region CreOrderATMParamReq
    /// <summary>
    /// 訂單建立ATM參數
    /// </summary>
    [DataContract]
    public class CreOrderATMParamReq
    {
        /// <summary>
        /// 付款截止日期
        /// </summary>
        [DataMember]
        public string ExpireDate { get; set; }
    }
    #endregion

    #region CreOrderCardParamReq
    /// <summary>
    /// 訂單建立信用卡參數
    /// </summary>
    [DataContract]
    public class CreOrderCardParamReq
    {
        /// <summary>
        /// 自動請款(信用卡)
        /// </summary>
        [DataMember]
        public string AutoBilling { get; set; }

        /// <summary>
        /// 預計自動請款天數
        /// </summary>
        [DataMember]
        public int? ExpBillingDays { get; set; }

        /// <summary>
        /// 訂單有效時間(分鐘)
        /// </summary>
        [DataMember]
        public int? ExpMinutes { get; set; }

        /// <summary>
        /// 收款方式-子項
        /// </summary>
        [DataMember]
        public string PayTypeSub { get; set; }

        /// <summary>
        /// 期數
        /// 使用此參數時PayTypeSub 必須 為「 STAGING 分期付款 」 。
        /// </summary>
        [DataMember]
        //public int? Staging { get; set; }
        public string Staging { get; set; }

        /// <summary>
        /// 總期數
        /// </summary>
        [DataMember]
        public int? DeductTotalNum { get; set; }

        /// <summary>
        /// 扣款週期
        /// </summary>
        [DataMember]
        public string PeriodType { get; set; }

        /// <summary>
        /// 扣款頻率
        /// </summary>
        [DataMember]
        public int? DeductFreq { get; set; }

        /// <summary>
        /// 快速付款 Token
        /// </summary>
        [DataMember]
        public string CCToken { get; set; }
    }
    #endregion

    #region QryOrderReq
    [DataContract]
    public class QryOrderReq : IQPayReq
    {
        /// <summary>
        /// 會員編號，例如AA0001
        /// </summary>
        [DataMember]
        public string ShopNo { get; set; }

        /// <summary>
        /// 用戶訂單編號(不可有單引號、雙引號、百分比)
        /// </summary>
        [DataMember]
        public string OrderNo { get; set; }

        /// <summary>
        /// 收款方式
        /// </summary>
        /// <remarks>
        /// A: ATM轉帳
        /// C: 信用卡
        /// </remarks>
        [DataMember]
        public string PayType { get; set; }

        /// <summary>
        /// 交易日期(起)，例如2017/5/3 00:00則帶201705030000
        /// </summary>
        [DataMember]
        public string OrderDateTimeS { get; set; }

        /// <summary>
        /// 交易日期(迄)，例如2017/5/3 23:59則帶201705032359
        /// </summary>
        [DataMember]
        public string OrderDateTimeE { get; set; }

        /// <summary>
        /// 付款日期(起)，例如2017/5/3 00:00則帶201705030000
        /// </summary>
        [DataMember]
        public string PayDateTimeS { get; set; }

        /// <summary>
        /// 付款日期(迄)，例如2017/5/3 23:59則帶201705032359
        /// </summary>
        [DataMember]
        public string PayDateTimeE { get; set; }

        /// <summary>
        /// 依付款狀態為條件查詢
        /// </summary>
        /// <remarks>
        /// 1.若 PayType 為“A” (ATM 轉帳)
        ///     Y：完成付款 (已轉帳)
        ///     N：未完成付款（未轉帳、逾期未轉）
        /// 2.若 Paytype 為“C” (信用卡)
        ///     Y：已請款的訂單
        ///     N：未請款的訂單（含：未付款、待請款、取消授權、授權逾期）
        /// </remarks>
        [DataMember]
        public string PayFlag { get; set; }
    }
    #endregion

    #region QryOrderUnCapturedReq
    [DataContract]
    public class QryOrderUnCapturedReq : IQPayReq
    {
        /// <summary>
        /// 會員編號，例如AA0001_001
        /// </summary>
        [DataMember]
        public string ShopNo { get; set; }

        /// <summary>
        /// 訂單編號
        /// </summary>
        /// <remarks>
        /// 不可有單引號、雙引號、百分比, 最大長度50
        /// </remarks>
        [DataMember]
        public string OrderNo { get; set; }
    }
    #endregion

    #region OrderMaintainReq
    [DataContract]
    public class OrderMaintainReq : IQPayReq
    {
        /// <summary>
        /// 會員編號，例如AA0001_001
        /// </summary>
        [DataMember]
        public string ShopNo { get; set; }

        /// <summary>
        /// 訂單編號
        /// </summary>
        /// <remarks>
        /// 不可有單引號、雙引號、百分比
        /// </remarks>
        [DataMember]
        public string OrderNo { get; set; }

        /// <summary>
        /// 發送要求特定值,P:請款要求/C:取消授權/R:退款要求
        /// </summary>
        [DataMember]
        public string Command { get; set; }

        /// <summary>
        /// 申請退款金額
        /// </summary>
        /// <remarks>
        /// 若不帶金額則代表退總訂單金額
        /// </remarks>
        [DataMember]
        public int? Amount { get; set; }

        /// <summary>
        /// 退款原因
        /// </summary>
        /// <remarks>
        /// 不可有單引號、雙引號、百分比
        /// </remarks>
        [DataMember]
        public string Remark { get; set; }
    }
    #endregion

    #region QryOrderPayReq
    [DataContract]
    public class QryOrderPayReq : IQPayReq
    {
        /// <summary>
        /// 會員編號，例如AA0001
        /// </summary>
        [DataMember]
        public string ShopNo { get; set; }

        /// <summary>
        /// Token值
        /// </summary>
        [DataMember]
        public string PayToken { get; set; }
    }
    #endregion

    #region QryBillReq
    [DataContract]
    public class QryBillReq : IQPayReq
    {
        /// <summary>
        /// 會員編號，例如AA0001
        /// </summary>        
        [DataMember]
        public string ShopNo { get; set; }

        /// <summary>
        /// 對帳日期
        /// </summary>
        [DataMember]
        public string BillDate { get; set; }
    }
    #endregion

    #region QryAllotReq
    [DataContract]
    public class QryAllotReq : IQPayReq
    {
        [DataMember]
        public string ShopNo { get; set; }

        [DataMember]
        public string AllotDateS { get; set; }

        [DataMember]
        public string AllotDateE { get; set; }

        [DataMember]
        public string PayType { get; set; }
    }
    #endregion
    #endregion

    #region Response, Domain物件
    #region NonceRes
    public class NonceRes
    {
        [DataMember]
        public string Nonce { get; set; }
    }
    #endregion

    #region WebAPIMessage
    [DataContract]
    public class WebAPIMessage
    {
        /// <summary>
        /// API版本
        /// </summary>
        [DataMember]
        public string Version { get; set; }

        /// <summary>
        /// 會員編號，例如AA0001_001
        /// </summary>
        [DataMember]
        public string ShopNo { get; set; }

        [DataMember]
        public string APIService { get; set; }

        /// <summary>
        /// 簽章值
        /// </summary>
        [DataMember]
        [SignExclude]
        public string Sign { get; set; }

        /// <summary>
        /// 一次性數值
        /// </summary>
        [DataMember]
        public string Nonce { get; set; }

        [DataMember]
        public string Message { get; set; }
    }
    #endregion

    #region CreOrder
    [DataContract]
    public class CreOrder
    {
        /// <summary>
        /// 訂單編號
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string OrderNo { get; set; }

        /// <summary>
        /// 會員編號，例如AA0001
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string ShopNo { get; set; }

        /// <summary>
        /// 網易收交易編號，例如AA00010000001
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string TSNo { get; set; }

        /// <summary>
        /// 收款方式
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string PayType { get; set; }

        /// <summary>
        /// 訂單金額
        /// </summary>
        [DataMember]
        public int Amount { get; set; }

        /// <summary>
        /// 處理狀態(S:處理成功(正常), F:處理失敗(錯誤))
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string Status { get; set; }

        /// <summary>
        /// 錯誤訊息
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string Description { get; set; }

        /// <summary>
        /// 自訂參數一
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string Param1 { get; set; }

        /// <summary>
        /// 自訂參數二
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string Param2 { get; set; }

        /// <summary>
        /// 自訂參數三
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string Param3 { get; set; }

        /// <summary>
        /// ATM參數
        /// </summary>
        [DataMember]
        public CreOrderATMParamRes ATMParam { get; set; }

        /// <summary>
        /// 信用卡參數
        /// </summary>
        [DataMember]
        public CreOrderCardParamRes CardParam { get; set; }

        /// <summary>
        /// 信用卡參數
        /// </summary>
        [DataMember]
        public CreOrderMobileParamRes MobileParam { get; set; }
    }
    #endregion

    #region CreOrderATMParamRes
    /// <summary>
    /// 訂單建立ATM參數
    /// </summary>
    [DataContract]
    public class CreOrderATMParamRes
    {
        /// <summary>
        /// 轉帳號碼
        /// </summary>
        [DataMember]
        public string AtmPayNo { get; set; }

        /// <summary>
        /// WebATM URL
        /// </summary>
        [DataMember]
        public string WebAtmURL { get; set; }

        /// <summary>
        /// OTP URL
        /// </summary>
        [DataMember]
        public string OtpURL { get; set; }
    }
    #endregion

    #region CreOrderCardParamRes
    /// <summary>
    /// 訂單建立信用卡參數
    /// </summary>
    [DataContract]
    public class CreOrderCardParamRes
    {
        /// <summary>
        /// 刷卡頁URL
        /// </summary>
        [DataMember]
        public string CardPayURL { get; set; }
    }
    #endregion
    #region CreOrderCardParamRes
    /// <summary>
    /// 訂單建立信用卡參數
    /// </summary>
    [DataContract]
    public class CreOrderMobileParamRes
    {
        /// <summary>
        /// 刷卡頁URL
        /// </summary>
        [DataMember]
        public string MobilePayURL { get; set; }
    }
    #endregion

    #region QryOrder
    [DataContract]
    public class QryOrder
    {
        /// <summary>
        /// 會員編號，例如AA0001_001
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string ShopNo { get; set; }

        /// <summary>
        /// 訊息時間, yyyyMMddHHmm
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string Date { get; set; }

        /// <summary>
        /// 處理狀態, S:處理成功(正常) / F:處理失敗(錯誤)
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string Status { get; set; }

        /// <summary>
        /// 描述
        /// </summary>
        /// <remarks>
        /// 若Status為F時，則帶入錯誤訊息，為s時，則為S00000或S00001
        /// </remarks>
        [DataMember]
        [DefaultValue("")]
        public string Description { get; set; }

        /// <summary>
        /// 訂單資訊
        /// </summary>
        [DataMember]
        public IList<OrderInfo> OrderList { get; set; }
    }
    #endregion

    #region OrderInfo
    [DataContract]
    public class OrderInfo
    {
        /// <summary>
        /// 訂單編號
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string OrderNo { get; set; }

        /// <summary>
        /// 網易收交易編號
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string TSNo { get; set; }

        /// <summary>
        /// 交易時間, yyyyMMddHHmm
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string TSDate { get; set; }

        /// <summary>
        /// 信用卡授權時間, yyyyMMddHHmm
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string ApprovedDate { get; set; }

        /// <summary>
        /// 付款時間/請款時間, yyyyMMddHHmm
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string PayDate { get; set; }

        /// <summary>
        /// 訂單金額
        /// </summary>
        /// <remarks>
        /// 包含小數二位，例如180元則會回傳18000
        /// </remarks>
        [DataMember]
        public int Amount { get; set; }

        /// <summary>
        /// 收款方式
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string PayType { get; set; }

        /// <summary>
        /// 訂單付款狀態
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string PayStatus { get; set; }

        /// <summary>
        /// 訂單有效期限, yyyyMMddHHmm
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string ExpireDate { get; set; }

        /// <summary>
        /// 退款註記, Y：有退款/N：無退款
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string RefundFlag { get; set; }

        /// <summary>
        /// 收款名稱
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string PrdtName { get; set; }

        /// <summary>
        /// 備註
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string Memo { get; set; }

        /// <summary>
        /// 自訂參數一
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string Param1 { get; set; }

        /// <summary>
        /// 自訂參數二
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string Param2 { get; set; }

        /// <summary>
        /// 自訂參數三
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string Param3 { get; set; }

        /// <summary>
        /// ATM參數
        /// </summary>
        [DataMember]
        public OrderInfoATMParamRes ATMParam { get; set; }

        /// <summary>
        /// 信用卡參數
        /// </summary>
        [DataMember]
        public OrderInfoCardParamRes CardParam { get; set; }
    }
    #endregion

    #region OrderInfoATMParamRes
    /// <summary>
    /// 訂單建立ATM參數
    /// </summary>
    [DataContract]
    public class OrderInfoATMParamRes
    {
        /// <summary>
        /// 轉帳號碼
        /// </summary>
        [DataMember]
        public string AtmPayNo { get; set; }

        /// <summary>
        /// WebATM URL
        /// </summary>
        [DataMember]
        public string WebAtmURL { get; set; }

        /// <summary>
        /// OTP URL
        /// </summary>
        [DataMember]
        public string OtpURL { get; set; }

        /// <summary>
        /// 金融機構代碼
        /// </summary>
        [DataMember]
        public string BankNo { get; set; }

        /// <summary>
        /// 轉帳帳號末5碼
        /// </summary>
        [DataMember]
        public string AcctNo { get; set; }
    }
    #endregion

    #region OrderInfoCardParamRes
    /// <summary>
    /// 訂單建立信用卡參數
    /// </summary>
    [DataContract]
    public class OrderInfoCardParamRes
    {
        /// <summary>
        /// 刷卡頁URL
        /// </summary>
        [DataMember]
        public string CardPayURL { get; set; }

        /// <summary>
        /// 分期付款首期金額
        /// </summary>
        [DataMember]
        public string StagingFirstAmount { get; set; }

        /// <summary>
        /// 分期付款每期金額
        /// </summary>
        [DataMember]
        public string StagingEachAmount { get; set; }

        /// <summary>
        /// 紅利折抵點數
        /// </summary>
        [DataMember]
        public string BonusCount { get; set; }

        /// <summary>
        /// 紅利折抵金額
        /// </summary>
        [DataMember]
        public string BonusAmount { get; set; }

        /// <summary>
        /// 紅利折抵實付金額
        /// </summary>
        [DataMember]
        public string BonusPayAmount { get; set; }

        /// <summary>
        /// 紅利折抵剩餘點數
        /// </summary>
        [DataMember]
        public string BonusLastCount { get; set; }

        /// <summary>
        /// 授權卡號前6碼
        /// </summary>
        [DataMember]
        public string LeftCCNo { get; set; }

        /// <summary>
        /// 授權卡號後4碼
        /// </summary>
        [DataMember]
        public string RightCCNo { get; set; }

        /// <summary>
        /// 授權碼
        /// </summary>
        [DataMember]
        public string AuthCode { get; set; }

        /// <summary>
        /// 卡號有效期限
        /// </summary>
        [DataMember]
        public string CCExpDate { get; set; }

        /// <summary>
        /// 快速付款Token
        /// </summary>
        [DataMember]
        public string CCToken { get; set; }
    }
    #endregion

    #region QryOrderUnCaptured
    [DataContract]
    public class QryOrderUnCaptured
    {
        /// <summary>
        /// 會員編號，例如AA0001_001
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string ShopNo { get; set; }

        /// <summary>
        /// 訊息時間, yyyyMMddHHmm
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string Date { get; set; }

        /// <summary>
        /// 處理狀態, S:處理成功(正常) / F:處理失敗(錯誤)
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string Status { get; set; }

        /// <summary>
        /// 描述
        /// </summary>
        /// <remarks>
        /// 若Status為F時，則帶入錯誤訊息，為s時，則為S00000或S00001
        /// </remarks>
        [DataMember]
        [DefaultValue("")]
        public string Description { get; set; }

        /// <summary>
        /// 訂單資訊
        /// </summary>
        [DataMember]
        public IList<OrderUnCapturedInfo> OrderList { get; set; }
    }
    #endregion

    #region OrderUnCapturedInfo
    [DataContract]
    public class OrderUnCapturedInfo
    {
        /// <summary>
        /// 訂單編號
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string OrderNo { get; set; }

        /// <summary>
        /// 網易收交易編號
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string TSNo { get; set; }

        /// <summary>
        /// 交易時間, yyyyMMddHHmm
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string TSDate { get; set; }

        /// <summary>
        /// 信用卡授權時間, yyyyMMddHHmm
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string ApprovedDate { get; set; }

        /// <summary>
        /// 訂單有效期限, yyyyMMddHHmm
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string ExpireDate { get; set; }

        /// <summary>
        /// 訂單金額
        /// </summary>
        /// <remarks>
        /// 包含小數二位，例如180元則會回傳18000
        /// </remarks>
        [DataMember]
        public int Amount { get; set; }

        /// <summary>
        /// 收款方式
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string PayType { get; set; }

        /// <summary>
        /// 收款名稱
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string PrdtName { get; set; }

        /// <summary>
        /// 備註
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string Memo { get; set; }

        /// <summary>
        /// 訂單付款狀態
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string PayStatus { get; set; }
    }
    #endregion

    #region OrderMaintain
    [DataContract]
    public class OrderMaintain
    {
        /// <summary>
        /// 訂單編號
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string OrderNo { get; set; }

        /// <summary>
        /// 會員編號，例如AA0001
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string ShopNo { get; set; }

        /// <summary>
        /// 驗証ID
        /// </summary>
        /// <remarks>
        /// Edited by Ray 2018.03.02 : WebAPI傳入Req調整為IntraAPIReq(InnerReq) - KeyNum移出
        /// </remarks>
        //[DataMember]
        //pubilc int KeyNum { get; set; }

        /// <summary>
        /// 網易收交易編號，例如AA00010000001
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string TSNo { get; set; }

        /// <summary>
        /// 發送要求特定值,P:請款要求/C:取消授權/R:退款要求
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string Command { get; set; }

        /// <summary>
        /// 申請退款金額
        /// </summary>
        /// <remarks>
        /// 若不帶金額則代表退總訂單金額
        /// </remarks>
        [DataMember]
        public int? Amount { get; set; }

        /// <summary>
        /// 訊息時間, yyyyMMddHHmm
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string Date { get; set; }

        /// <summary>
        /// 處理狀態, S:處理成功(正常) / F:處理失敗(錯誤)
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string Status { get; set; }

        /// <summary>
        /// 描述
        /// </summary>
        /// <remarks>
        /// 若Status為F時，則帶入錯誤訊息，為s時，則為S00000
        /// </remarks>
        [DataMember]
        [DefaultValue("")]
        public string Description { get; set; }
    }
    #endregion

    #region QryOrderPay
    [DataContract]
    public class QryOrderPay
    {
        [DataMember]
        [DefaultValue("")]
        public string ShopNo { get; set; }

        [DataMember]
        [DefaultValue("")]
        public string PayToken { get; set; }

        [DataMember]
        [DefaultValue("")]
        public string Date { get; set; }

        [DataMember]
        [DefaultValue("")]
        public string Status { get; set; }

        [DataMember]
        [DefaultValue("")]
        public string Description { get; set; }

        [DataMember]
        public TSResult TSResultContent { get; set; }
    }
    #endregion

    #region TSResult
    [DataContract]
    public class TSResult
    {
        /// <summary>
        /// 訊息類型
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string APType { get; set; }

        /// <summary>
        /// 交易編號
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string TSNo { get; set; }

        /// <summary>
        /// 訂單編號
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string OrderNo { get; set; }

        /// <summary>
        /// 商店代號
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string ShopNo { get; set; }

        /// <summary>
        /// 收款方式
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string PayType { get; set; }

        /// <summary>
        /// 訂單金額
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string Amount { get; set; }

        /// <summary>
        /// 處理狀態
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string Status { get; set; }

        /// <summary>
        /// 處理訊息
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string Description { get; set; }

        /// <summary>
        /// 自訂參數一
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string Param1 { get; set; }

        /// <summary>
        /// 自訂參數二
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string Param2 { get; set; }

        /// <summary>
        /// 自訂參數三
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string Param3 { get; set; }

        /// <summary>
        /// 授權卡號前6碼
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string LeftCCNo { get; set; }

        /// <summary>
        /// 授權卡號後4碼
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string RightCCNo { get; set; }

        /// <summary>
        /// 有效期限
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string CCExpDate { get; set; }

        /// <summary>
        /// 快速付款Token
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string CCToken { get; set; }
    }
    #endregion

    #region QryBill
    [DataContract]
    public class QryBill
    {
        /// <summary>
        /// 會員編號，例如AA0001
        /// </summary>        
        [DataMember]
        [DefaultValue("")]
        public string ShopNo { get; set; }

        /// <summary>
        /// 訊息時間, yyMMddHHmm
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string Date { get; set; }

        /// <summary>
        /// 對帳日期, yyMMdd
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string BillDate { get; set; }

        /// <summary>
        /// S:處理成功(正常), F:處理失敗(錯誤)
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string Status { get; set; }

        /// <summary>
        /// 串接處理訊息, 請參考代碼表
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string Description { get; set; }

        /// <summary>
        /// 對帳檔清單
        /// </summary>
        [DataMember]
        public IList<BillInfo> OrderList { get; set; }
    }
    #endregion

    #region BillInfo
    public class BillInfo
    {
        /// <summary>
        /// 網易收交易編號，例如AC00010000001
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string TSNo { get; set; }

        /// <summary>
        /// 用戶訂單編號
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string OrderNo { get; set; }

        /// <summary>
        /// P:收款, R:退款
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string TSType { get; set; }

        /// <summary>
        /// 交易時間，2017/5/12 22:32格式201705122232(yyyyMMddHmm)
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string TSDate { get; set; }

        /// <summary>
        /// 付(請)款時間/退款時間，2017/5/12 22:32格式201705122232
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string PayDate { get; set; }

        /// <summary>
        /// 付(請)款時間/退款時間，2017/5/12 22:32格式201705122232
        /// </summary>
        [DataMember]
        [DefaultValue("")]
        public string PayType { get; set; }

        /// <summary>
        /// 收(退)款總金額，包含小數二位，例如180元則會回傳18000
        /// </summary>
        [DataMember]
        public int Amount { get; set; }

        /// <summary>
        /// 自訂參數2
        /// </summary>
        [DataMember]
        [StringLength(255)]
        public string Param1 { get; set; }

        /// <summary>
        /// 自訂參數3
        /// </summary>
        [DataMember]
        [StringLength(255)]
        public string Param2 { get; set; }

        [DataMember]
        [StringLength(255)]
        public string Param3 { get; set; }
    }
    #endregion

    #region QryAllot
    [DataContract]
    public class QryAllot
    {
        /// <summary>
        /// 會員編號，例如AA0001
        /// </summary>        
        [DataMember]
        public string ShopNo { get; set; }

        /// <summary>
        /// 訊息時間, yyMMddHHmm
        /// </summary>
        [DataMember]
        public string Date { get; set; }

        /// <summary>
        /// S:處理成功(正常), F:處理失敗(錯誤)
        /// </summary>
        [DataMember]
        public string Status { get; set; }

        /// <summary>
        /// 串接處理訊息, 請參考代碼表
        /// </summary>
        [DataMember]
        public string Description { get; set; }

        /// <summary>
        /// 撥款檔清單
        /// </summary>
        [DataMember]
        public List<AllotMain> Allot { get; set; }
    }
    #endregion

    #region AllotMain
    [DataContract]
    public class AllotMain
    {
        [DataMember]
        public string AllotNo { get; set; }

        [DataMember]
        public int AllotDate { get; set; }

        [DataMember]
        public string PayType { get; set; }

        [DataMember]
        public decimal PayAmount { get; set; }

        [DataMember]
        public decimal RefundAmount { get; set; }

        [DataMember]
        public decimal FeeAmount { get; set; }

        [DataMember]
        public decimal ExtAllotAmount { get; set; }

        [DataMember]
        public decimal AllotAmount { get; set; }

        [DataMember]
        public string Account { get; set; }

        [DataMember]
        public List<AllotDetail> List { get; set; }
    }
    #endregion

    #region AllotDetail
    [DataContract]
    public class AllotDetail
    {
        [DataMember]
        public string TSNO { get; set; }

        [DataMember]
        public string OrderNO { get; set; }

        [DataMember]
        public string PayDate { get; set; }

        [DataMember]
        public decimal Amount { get; set; }

        [DataMember]
        public decimal Fee { get; set; }

        [DataMember]
        public string TSType { get; set; }
    }
    #endregion
    #endregion
}

