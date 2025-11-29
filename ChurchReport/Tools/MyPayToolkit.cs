using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using QPay.Domain;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Collections;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using ChurchReport.Models;

/// <summary>
/// MyPay 金流工具類別命名空間
/// 提供 MyPay 支付服務的加密、解密與 API 調用功能
/// </summary>
namespace ChurchReport.Tools
{
    /// <summary>
    /// MyPay 商店訂單處理類別
    /// 負責處理 MyPay 支付請求的加密、發送與本地驗證
    /// </summary>
    public class MyPayStoreOrder
    {
        #region 私有靜態成員 - 配置管理

        /// <summary>
        /// 配置建構器：用於載入 appsettings.json 配置
        /// </summary>
        private static readonly IConfigurationBuilder m_ConfigurationBuilder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json");

        /// <summary>
        /// 配置實例：提供應用程式設定存取
        /// </summary>
        private static readonly IConfiguration m_Configuration = m_ConfigurationBuilder.Build();

        #endregion

        #region 公開屬性 - 商店資訊

        /// <summary>
        /// 特約商店商務代號
        /// 從配置檔案讀取，若無則使用預設值
        /// </summary>
        public string storeUid;

        /// <summary>
        /// 特約商店金鑰或認證碼
        /// 用於 AES 加密/解密，必須保密
        /// </summary>
        public string storeKey;

        /// <summary>
        /// 串接交易位置（API 端點 URL）
        /// MyPay 服務的初始化 API 地址
        /// </summary>
        public string url;

        #endregion

        #region 建構函式

        /// <summary>
        /// 初始化 StoreOrder 實例
        /// 從設定檔載入 MyPay 相關設定，若無設定則使用預設值
        /// </summary>
        public MyPayStoreOrder()
        {
            // 從配置讀取商店代號，帶預設值
            this.storeUid = m_Configuration["MyPay:Store_Id"] ?? "200043350001";

            // 從配置讀取商店金鑰，帶預設值
            this.storeKey = m_Configuration["MyPay:Key"] ?? "iFDYTvaj6AfsEYzA8oA1EdtGQwkLLbR5";

            // 從配置讀取 API URL，帶預設值
            this.url = m_Configuration["MyPay:Url"] ?? "https://ka.usecase.cc/api/init";
        }

        #endregion

        #region 私有方法 - 資料準備

        /// <summary>
        /// 取得串接欄位資料（範例資料，已被 GetPostData 覆蓋）
        /// 此方法返回硬編碼的測試資料，主要用於開發測試
        /// </summary>
        /// <returns>動態物件包含商店與商品資訊</returns>
        private dynamic GetRawData()
        {
            // 初始化商品清單
            ArrayList items = new ArrayList();

            // 建立測試商品項目
            dynamic item = new ExpandoObject();
            item.id = "1";
            item.name = "商品名稱";
            item.cost = "10";
            item.amount = "1";
            item.total = "10";

            items.Add(item);

            // 建立主要資料物件
            dynamic rawData = new ExpandoObject();
            rawData.store_uid = this.storeUid;
            rawData.items = items;
            rawData.cost = "10";
            rawData.user_id = "phper";
            rawData.order_id = "1234567890";
            rawData.ip = "127.0.0.1"; // 此為消費者 IP，會做為驗證用
            rawData.pfn = "0";

            return rawData;
        }

        /// <summary>
        /// 取得服務請求物件
        /// 定義 API 服務的名稱與命令
        /// </summary>
        /// <returns>服務請求實例</returns>
        private ServiceRequest GetService()
        {
            ServiceRequest rawData = new ServiceRequest();
            rawData.service_name = "api";
            rawData.cmd = "api/orders";
            return rawData;
        }

        #endregion

        #region 私有方法 - 加密與解密

        /// <summary>
        /// 從 Encrypt 的輸出（Base64 組合：IV + Cipher）還原明文
        /// 用於本地驗證加密資料是否可正確解密
        /// </summary>
        /// <param name="combinedBase64">由 Encrypt() 回傳的 Base64 字串（IV + cipher）</param>
        /// <param name="key">storeKey 或 agentKey 原始字串</param>
        /// <returns>還原的明文字串</returns>
        /// <exception cref="ArgumentException">payload 為空時拋出</exception>
        /// <exception cref="FormatException">payload 不是合法的 Base64 時拋出</exception>
        /// <exception cref="InvalidOperationException">payload 長度不足時拋出</exception>
        /// <exception cref="CryptographicException">解密失敗時拋出</exception>
        private static string DecryptFromCombinedBase64(string combinedBase64, string key)
        {
            // 驗證輸入參數
            if (string.IsNullOrEmpty(combinedBase64))
                throw new ArgumentException("payload 為空", nameof(combinedBase64));

            // 解碼 Base64 字串
            byte[] combinedBytes;
            try
            {
                combinedBytes = Convert.FromBase64String(combinedBase64);
            }
            catch (FormatException fe)
            {
                throw new FormatException("payload 不是合法的 Base64 字串", fe);
            }

            // 檢查資料長度是否足夠
            if (combinedBytes.Length <= 16)
                throw new InvalidOperationException("payload 長度不足以包含 IV 與 cipher 資料");

            // 拆分 IV 與 cipher：前 16 bytes 為 IV，餘下為 cipher
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

            // 執行 AES 解密
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
        /// AES 256 加密
        /// 將明文與 IV 結合後進行加密，返回 Base64 編碼字串
        /// </summary>
        /// <param name="data">要加密的明文字串</param>
        /// <param name="key">加密金鑰字串</param>
        /// <param name="byteIV">初始化向量</param>
        /// <returns>Base64 編碼的加密字串（IV + cipher）</returns>
        private string Encrypt(string data, string key, byte[] byteIV)
        {
            // 將金鑰轉換為 byte 陣列
            var byteKey = Encoding.UTF8.GetBytes(key);

            // 執行 AES 加密
            var enBytes = AES_Encrypt(data, byteKey, byteIV);

            // 將 IV 與加密資料結合並 Base64 編碼
            return Convert.ToBase64String(BytesAdd(byteIV, enBytes));
        }

        /// <summary>
        /// AES 256 加密處理核心
        /// 使用 CBC 模式與 PKCS7 填充進行加密
        /// </summary>
        /// <param name="original">原始明文字串</param>
        /// <param name="key">金鑰 byte 陣列</param>
        /// <param name="iv">初始化向量 byte 陣列</param>
        /// <returns>加密後的 byte 陣列</returns>
        private byte[] AES_Encrypt(string original, byte[] key, byte[] iv)
        {
            try
            {
                // 將明文轉換為 byte 陣列
                var data = Encoding.UTF8.GetBytes(original);

                // 建立 AES 加密器
                using (var aes = Aes.Create())
                {
                    aes.KeySize = 256;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;
                    aes.Key = key;
                    aes.IV = iv;

                    using (var encryptor = aes.CreateEncryptor())
                    {
                        // 執行最終區塊轉換
                        var result = encryptor.TransformFinalBlock(data, 0, data.Length);
                        return result;
                    }
                }
            }
            catch
            {
                // 加密失敗時返回 null（應由呼叫端處理）
                return null;
            }
        }

        /// <summary>
        /// 合併 byte 陣列
        /// 將多個 byte 陣列合併為單一陣列
        /// </summary>
        /// <param name="a">第一個 byte 陣列</param>
        /// <param name="arryB">其他要合併的 byte 陣列</param>
        /// <returns>合併後的 byte 陣列</returns>
        private byte[] BytesAdd(byte[] a, params byte[][] arryB)
        {
            List<byte> combined = new List<byte>();
            combined.AddRange(a);
            foreach (var arr in arryB)
            {
                combined.AddRange(arr);
            }
            return combined.ToArray();
        }

        /// <summary>
        /// 產生 AES 的初始化向量 (IV)
        /// 使用系統隨機生成器產生 16 bytes IV
        /// </summary>
        /// <returns>16 bytes 的 IV 陣列</returns>
        private static byte[] GetBytesIV()
        {
            using (var aes = Aes.Create())
            {
                aes.KeySize = 256;
                aes.GenerateIV();
                return aes.IV;
            }
        }

        #endregion

        #region 公開方法 - API 調用

        /// <summary>
        /// 取得送出欄位資料（含本地自檢）
        /// 將自訂資料加密並準備 POST 參數，同時進行本地解密驗證
        /// </summary>
        /// <param name="customData">自訂交易資料（ExpandoObject）</param>
        /// <param name="Service">服務請求物件</param>
        /// <returns>包含所有 POST 參數的 NameValueCollection</returns>
        /// <exception cref="InvalidOperationException">本地加密自檢失敗時拋出</exception>
        public NameValueCollection GetPostData(ExpandoObject customData, ServiceRequest Service)
        {
            // 將自訂資料序列化為 JSON
            string data_json = JsonConvert.SerializeObject(customData, Formatting.None);

            // 將服務請求序列化為 JSON
            string svr_json = JsonConvert.SerializeObject(Service, Formatting.None);

            // 產生 AES 初始化向量
            var IV = GetBytesIV();

            // 對資料與服務請求進行加密
            var data_encode = Encrypt(data_json, this.storeKey, IV);
            var svr_encode = Encrypt(svr_json, this.storeKey, IV);

            // ========== 本地自檢：嘗試還原剛剛加密的內容，若失敗則拋出清楚錯誤 ==========
            try
            {
                // 驗證資料加密是否正確
                var decodedData = DecryptFromCombinedBase64(data_encode, this.storeKey);

                // 驗證服務請求加密是否正確
                var decodedService = DecryptFromCombinedBase64(svr_encode, this.storeKey);

                // 可選：在開發或偵錯模式下記錄（此處不寫入日誌以避免洩漏）
                // System.Diagnostics.Debug.WriteLine($"DecryptedData: {decodedData}");
            }
            catch (Exception ex)
            {
                // 將錯誤包裝並拋出，讓上層能夠取得更明確的錯誤訊息
                throw new InvalidOperationException("本地加密自檢失敗: " + ex.Message + "。請確認 storeKey、IV 生成與 URL encode 流程是否與 MyPay 規格相符。", ex);
            }

            // 對加密資料進行 URL 編碼（避免傳輸問題）
            // 注意：使用的 Http Post 套件若會自動加上 UrlEncode，則請忽略此步驟，避免雙重編碼
            string data_toUrlEncode = HttpUtility.UrlEncode(data_encode);
            string svr_toUrlEncode = HttpUtility.UrlEncode(svr_encode);

            // 準備 POST 參數集合
            NameValueCollection postData = new NameValueCollection();
            postData["store_uid"] = this.storeUid;
            postData["agent_uid"] = this.storeUid;  // 添加必填的 agent_uid 欄位
            postData["service"] = svr_toUrlEncode;
            postData["encry_data"] = data_toUrlEncode;

            return postData;
        }

        /// <summary>
        /// 將資料 POST 到 MyPay 主機
        /// 發送 HTTP POST 請求並接收 JSON 回應
        /// </summary>
        /// <param name="pars">POST 參數集合</param>
        /// <returns>MyPay API 回應物件</returns>
        /// <exception cref="WebException">網路請求失敗時拋出</exception>
        public PayPageResponse Post(NameValueCollection pars)
        {
            string result = string.Empty;
            string param = string.Empty;

            PayPageResponse retObj;

            // 將參數集合轉換為 URL 編碼字串
            if (pars.Count > 0)
            {
                foreach (string key in pars.AllKeys)
                {
                    param += key + "=" + pars[key] + "&";
                }
                // 移除最後一個 '&'
                if (param.EndsWith("&"))
                {
                    param = param.Remove(param.Length - 1);
                }
            }

            // 將參數字串轉換為 byte 陣列
            byte[] bs = Encoding.UTF8.GetBytes(param);

            try
            {
                // 建立 HTTP 請求
                HttpWebRequest req = (HttpWebRequest)HttpWebRequest.Create(this.url);
                req.Method = "POST";
                req.ContentType = "application/x-www-form-urlencoded";
                req.ContentLength = bs.Length;

                // 寫入請求內容
                using (Stream reqStream = req.GetRequestStream())
                {
                    reqStream.Write(bs, 0, bs.Length);
                }

                // 取得回應並讀取內容
                using (WebResponse wr = req.GetResponse())
                {
                    Encoding myEncoding = Encoding.GetEncoding("UTF-8");
                    using (StreamReader myStreamReader = new StreamReader(wr.GetResponseStream(), myEncoding))
                    {
                        result = myStreamReader.ReadToEnd();
                        // 將結果反序列化為 PayPageResponse 物件
                        retObj = JsonConvert.DeserializeObject<PayPageResponse>(result);
                    }
                }

                // 清理請求物件
                req = null;
            }
            catch (WebException ex)
            {
                // 網路異常時拋出詳細錯誤
                throw new WebException(ex.Message + " params: " + param, ex, ex.Status, ex.Response);
            }

            return retObj;
        }

        #endregion
    }

    /// <summary>
    /// MyPay 工具類別（靜態）
    /// 提供 QPay 相關的 API 調用方法
    /// </summary>
    public static class MyPayToolkit
    {
        #region 私有靜態成員

        /// <summary>
        /// 配置建構器：用於載入 appsettings.json 配置
        /// </summary>
        private static readonly IConfigurationBuilder m_ConfigurationBuilder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json");

        /// <summary>
        /// 配置實例：提供應用程式設定存取
        /// </summary>
        private static readonly IConfiguration m_Configuration = m_ConfigurationBuilder.Build();

        /// <summary>
        /// 當前版本號
        /// </summary>
        private static readonly string _currentVersion = "1.0.0";

        #endregion

        #region 公開靜態方法 - QPay API 調用

        #region 訂單建立 (虛擬帳號、信用卡)
        /// <summary>
        /// 訂單建立 (虛擬帳號、信用卡)
        /// 建立新的支付訂單
        /// </summary>
        /// <param name="req">訂單建立請求物件</param>
        /// <returns>訂單建立回應物件</returns>
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
        /// 查詢尚未請款的訂單
        /// </summary>
        /// <param name="req">待請款訂單查詢請求物件</param>
        /// <returns>待請款訂單查詢回應物件</returns>
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
        /// 維護信用卡訂單狀態
        /// </summary>
        /// <param name="req">訂單維護請求物件</param>
        /// <returns>訂單維護回應物件</returns>
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
        /// 查詢訂單詳細資訊
        /// </summary>
        /// <param name="req">訂單查詢請求物件</param>
        /// <returns>訂單查詢回應物件</returns>
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
        /// 查詢付款交易結果
        /// </summary>
        /// <param name="req">付款結果查詢請求物件</param>
        /// <returns>付款結果查詢回應物件</returns>
        /// <example>
        /// 串接範例如下:
        /// QryOrderPay retObj = QPayToolkit.OrderPayQuery(new QryOrderPayReq() { ... });
        /// </example>
        public static QryOrderPay OrderPayQuery(QryOrderPayReq req)
        {
            return GetQPayResponse<QryOrderPayReq, QryOrderPay>(req, APIService.OrderPayQuery);
        }

        /// <summary>
        /// 付款結果查詢服務（帶自訂 HashCode）
        /// 查詢付款交易結果，使用指定的 HashCode
        /// </summary>
        /// <param name="req">付款結果查詢請求物件</param>
        /// <param name="hashCode">自訂 HashCode</param>
        /// <returns>付款結果查詢回應物件</returns>
        public static QryOrderPay OrderPayQuery(QryOrderPayReq req, string hashCode)
        {
            return GetQPayResponse<QryOrderPayReq, QryOrderPay>(req, APIService.OrderPayQuery, hashCode);
        }
        #endregion

        #region 每日收(退)款查詢服務
        /// <summary>
        /// 每日收(退)款查詢服務
        /// 查詢每日收退款記錄
        /// </summary>
        /// <param name="req">每日收退款查詢請求物件</param>
        /// <returns>每日收退款查詢回應物件</returns>
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
        /// 查詢撥款檔案資訊
        /// </summary>
        /// <param name="req">撥款檔查詢請求物件</param>
        /// <returns>撥款檔查詢回應物件</returns>
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

        #region 私有靜態方法

        #region 取得 QPay Web API 回應
        /// <summary>
        /// 取得 QPay Web API 回應
        /// 通用方法處理 QPay API 請求與回應
        /// </summary>
        /// <typeparam name="TReq">請求型別</typeparam>
        /// <typeparam name="TResult">回應型別</typeparam>
        /// <param name="request">請求物件</param>
        /// <param name="apiService">API 服務類型</param>
        /// <param name="hashCode">選用的 HashCode</param>
        /// <returns>API 回應物件</returns>
        private static TResult GetQPayResponse<TReq, TResult>(TReq request, APIService apiService, string hashCode = null)
            where TReq : IQPayReq
        {
            try
            {
                // 建立回應物件實例
                TResult innerResult = Activator.CreateInstance<TResult>();
                return innerResult;
            }
            catch (Exception ex)
            {
                // 記錄異常並重新拋出
                QPayCommon.ExceptionLog(null, ex);
                throw ex;
            }
        }
        #endregion

        #endregion
    }
}

