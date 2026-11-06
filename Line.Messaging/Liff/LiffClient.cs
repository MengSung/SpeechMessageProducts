/*
 * 檔案整體說明
 *
 * 目的與定位：LINE LIFF 管理 API，保留既有 JSON 與公開入口。 一個實例只代表一個伺服器端 channel；與 Messaging client 共用隔離 transport， 不修改 HttpClient
 * 預設 credential、不快取 request／response 或 LIFF 使用者資料。 方法完成即釋放 request/content/response，原始失敗保持向外傳遞。
 *
 * 主要功能與流程：
 * 本檔宣告 LiffClient。
 * - 借入由 Host 管理生命週期及有限 Timeout 的 HttpClient；本實例 Dispose 不會關閉其他 channel 共用的 transport。
 * - 舊式自有 HttpClient 入口；保留相容性，呼叫者必須先排空所有操作再 Dispose，建議改用主機共用 transport。
 * - 新增 LIFF app；保留既有 view 包裝序列化，讀完 app ID 才釋放回應，不保留新 app 資料。
 * - 更新 LIFF view；成功或失敗都釋放 response，真實失敗保持向外傳遞，不偽裝成功。
 * 主要操作入口包含 AddLiffAppAsync、UpdateLiffAppAsync、GetAllLiffAppAsync、DeleteLiffAppAsync、JsonRequest、Dispose。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 Line.Messaging.Tests/LiffAndStreamLifecycleTests.cs 追查目前工作樹中的合作／使用位置。
 */
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Line.Messaging.Liff
{
    /// <summary>
    /// LINE LIFF 管理 API，保留既有 JSON 與公開入口。
    /// 一個實例只代表一個伺服器端 channel；與 Messaging client 共用隔離 transport，
    /// 不修改 HttpClient 預設 credential、不快取 request／response 或 LIFF 使用者資料。
    /// 方法完成即釋放 request/content/response，原始失敗保持向外傳遞。
    /// </summary>
    public class LiffClient : IDisposable
    {
        private readonly ChannelHttpTransport _transport;
        private readonly JsonSerializerSettings _settings = new CamelCaseJsonSerializerSettings();
        private readonly string _requestUri;

        /// <summary>
        /// 借入由 Host 管理生命週期及有限 Timeout 的 HttpClient；本實例 Dispose 不會關閉其他 channel 共用的 transport。
        /// </summary>
        /// <param name="httpClient">不得含使用者 cookie 或預設認證的共用 transport。</param>
        /// <param name="channelAccessToken">此伺服器端 channel 的 credential，不接受使用者輸入當授權。</param>
        /// <param name="requestUri">由受信任組合根指定的 LIFF API 端點，不是 request 路由輸入。</param>
        public LiffClient(HttpClient httpClient, string channelAccessToken, string requestUri = "https://api.line.me/liff/v1/apps")
        {
            _transport = new ChannelHttpTransport(httpClient, channelAccessToken);
            _requestUri = requestUri;
        }

        /// <summary>
        /// 舊式自有 HttpClient 入口；保留相容性，呼叫者必須先排空所有操作再 Dispose，建議改用主機共用 transport。
        /// </summary>
        /// <param name="channelAccessToken">只供此實例使用的 channel credential。</param>
        /// <param name="requestUri">受信任的 LIFF API 端點。</param>
        [Obsolete("Use the constructor accepting HttpClient to avoid socket exhaustion.")]
        public LiffClient(string channelAccessToken, string requestUri = "https://api.line.me/liff/v1/apps")
        {
            _transport = new ChannelHttpTransport(channelAccessToken);
            _requestUri = requestUri;
        }

        /// <summary>
        /// 新增 LIFF app；保留既有 view 包裝序列化，讀完 app ID 才釋放回應，不保留新 app 資料。
        /// </summary>
        /// <param name="viewType">既有 LIFF 尺寸。</param>
        /// <param name="url">呼叫端已驗證的 HTTPS app 網址。</param>
        /// <returns>LINE 回傳的 LIFF ID。</returns>
        public async Task<string> AddLiffAppAsync(ViewType viewType, string url)
        {
            using var response = await _transport.SendAsync(JsonRequest(HttpMethod.Post, _requestUri, new { view = new View(viewType, url) })).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
            var result = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return JsonConvert.DeserializeAnonymousType(result, new { liffId = "" }).liffId;
        }

        /// <summary>
        /// 更新 LIFF view；成功或失敗都釋放 response，真實失敗保持向外傳遞，不偽裝成功。
        /// </summary>
        /// <param name="liffId">呼叫端已授權的 app ID。</param>
        /// <param name="viewType">尺寸。</param>
        /// <param name="url">呼叫端已驗證的 HTTPS app 網址。</param>
        /// <returns>完整更新作業。</returns>
        public async Task UpdateLiffAppAsync(string liffId, ViewType viewType, string url)
        {
            using var response = await _transport.SendAsync(JsonRequest(HttpMethod.Put, $"{_requestUri}/{liffId}/view", new { type = viewType, url })).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 取得本 channel 的 app 清單；結果只屬此次操作，不保存為共用集合，response 解析後確定性關閉。
        /// </summary>
        /// <returns>呼叫端擁有的 detached app 清單。</returns>
        public async Task<IList<LiffApp>> GetAllLiffAppAsync()
        {
            using var response = await _transport.SendAsync(new HttpRequestMessage(HttpMethod.Get, _requestUri)).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return JsonConvert.DeserializeAnonymousType(content, new { apps = new LiffApp[0] }).apps;
        }

        /// <summary>
        /// 刪除已授權 app；等待並驗證 HTTP 結果，不再把未檢查的 response 當成功且遺失 owner。
        /// </summary>
        /// <param name="liffId">呼叫端已授權的 app ID。</param>
        /// <returns>完整、已清理 response 的刪除作業。</returns>
        public async Task DeleteLiffAppAsync(string liffId)
        {
            using var response = await _transport.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"{_requestUri}/{liffId}")).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }

        /// <summary>先完成可能失敗的 JSON 組裝再配置 request；成功後唯一轉交 transport，避免序列化錯誤留下 request。</summary>
        /// <param name="method">固定 API 方法。</param>
        /// <param name="url">受信任且已組裝的端點。</param>
        /// <param name="value">僅屬當次操作的 body model。</param>
        /// <returns>呼叫者必須立刻交給 transport 的 request。</returns>
        private HttpRequestMessage JsonRequest(HttpMethod method, string url, object value)
        {
            var body = JsonConvert.SerializeObject(value, _settings);
            return new HttpRequestMessage(method, url) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }

        /// <summary>
        /// 冪等清除 channel credential 並拒絕新請求；只有舊建構式的自有 transport 會被釋放。呼叫端必須先排空既有作業。
        /// </summary>
        public void Dispose() => _transport.Dispose();
    }
}
