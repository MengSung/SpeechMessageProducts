using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace Line.Messaging.Liff
{
    /// <summary>
    /// HTTP Client for the LINE Front-end Framework (LIFF) API
    /// </summary>
    public class LiffClient : IDisposable
    {
        private readonly HttpClient _client;
        private readonly bool _disposeClient;
        private JsonSerializerSettings _jsonSerializerSettings;
        private string _requestUri;

        /// <summary>
        /// Constructor - 使用外部提供的 HttpClient（建議用於 DI 情境）
        /// </summary>
        /// <param name="httpClient">外部提供的 HttpClient 實例（不會被 Dispose）</param>
        /// <param name="channelAccessToken">Channel access token</param>
        /// <param name="requestUri">Request base URL</param>
        public LiffClient(HttpClient httpClient, string channelAccessToken, string requestUri = "https://api.line.me/liff/v1/apps")
        {
            _client = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _disposeClient = false;
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", channelAccessToken);
            _jsonSerializerSettings = new CamelCaseJsonSerializerSettings();
            _requestUri = requestUri;
        }

        /// <summary>
        /// Constructor - 建立內部 HttpClient（向後相容，但不建議用於生產環境）
        /// </summary>
        /// <param name="channelAccessToken">Channel access token</param>
        /// <param name="requestUri">Request base URL</param>
        [Obsolete("建議使用接受 HttpClient 參數的建構函式，以避免 Socket 耗盡問題")]
        public LiffClient(string channelAccessToken, string requestUri = "https://api.line.me/liff/v1/apps")
        {
            _client = new HttpClient();
            _disposeClient = true;
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", channelAccessToken);
            _jsonSerializerSettings = new CamelCaseJsonSerializerSettings();
            _requestUri = requestUri;
        }

        /// <summary>
        /// Adds an app to LIFF. You can add up to 10 LIFF apps on one channel.
        /// </summary>
        /// <param name="viewType">
        /// Size of the LIFF app view. Specify one of the following values
        /// </param>
        /// <param name="url">
        /// URL of the LIFF app. Must start with HTTPS.    
        /// </param>
        /// <returns>
        /// LIFF app ID
        /// </returns>
        public async Task<string> AddLiffAppAsync(ViewType viewType, string url)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, _requestUri);
            var content = JsonConvert.SerializeObject(new { view = new View(viewType, url) }, _jsonSerializerSettings);
            request.Content = new StringContent(content, Encoding.UTF8, "application/json");

            var response = await _client.SendAsync(request);
            await response.EnsureSuccessStatusCodeAsync();
            var result = await response.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeAnonymousType(result, new { liffId = "" }).liffId;
        }

        /// <summary>
        /// Updates LIFF app settings.
        /// </summary>
        /// <param name="liffId">ID of the LIFF app to be updated</param>
        /// <param name="viewType">
        /// Size of the LIFF app view. Specify one of the following values
        /// </param>
        /// <param name="url">
        /// URL of the LIFF app. Must start with HTTPS.    
        /// </param>
        public async Task UpdateLiffAppAsync(string liffId, ViewType viewType, string url)
        {
            var request = new HttpRequestMessage(HttpMethod.Put, $"{_requestUri}/{liffId}/view");
            var content = JsonConvert.SerializeObject(new { type = viewType, url }, _jsonSerializerSettings);
            request.Content = new StringContent(content, Encoding.UTF8, "application/json");

            var response = await _client.SendAsync(request);
            await response.EnsureSuccessStatusCodeAsync();
        }

        /// <summary>
        /// Gets information on all the LIFF apps registered in the channel.
        /// </summary>
        /// <returns>A JSON object with the following properties.</returns>
        public async Task<IList<LiffApp>> GetAllLiffAppAsync()
        {
            var content = await _client.GetStringAsync(_requestUri);
            return JsonConvert.DeserializeAnonymousType(content, new { apps = new LiffApp[0] }).apps;
        }

        /// <summary>
        /// Deletes a LIFF app.
        /// </summary>
        /// <param name="liffId">ID of the LIFF app to be deleted</param>
        public Task DeleteLiffAppAsync(string liffId)
        {
            return _client.DeleteAsync($"{_requestUri}/{liffId}");
        }

        /// <summary>
        /// 釋放資源
        /// </summary>
        public void Dispose()
        {
            if (_disposeClient)
            {
                _client?.Dispose();
            }
        }
    }
}
