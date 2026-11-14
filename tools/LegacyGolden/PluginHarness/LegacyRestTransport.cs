/*
檔案整體說明
目的與定位：提供舊RestSharp呼叫的離線相容面，供執行時compiler取代缺少或不可安全攔截的舊傳輸組件。
主要功能與流程：RestRequest只保留方法、資源及JSON body，不保存Authorization等header；RestClient將Get或Execute導向相同的有界capture並回固定成功內容。RecordingRestClient保留語法改寫後的建構介面，不繼承任何真實網路實作。
使用邊界與維護：本檔是明列子集，不模擬完整RestSharp；不支援的簽章應讓編譯失敗而非默默發送。所有可變request只存活於單次域，沒有socket、背景工作、憑證快取或外部服務；recording上限與域清理由正式runner負責。
*/
using System;
using System.Net;

namespace RestSharp
{
    /// <summary>舊來源使用的方法識別，只用於離線記錄。</summary>
    public enum Method
    {
        /// <summary>查詢。</summary>
        GET,
        /// <summary>新增。</summary>
        POST,
        /// <summary>更新。</summary>
        PUT,
        /// <summary>刪除。</summary>
        DELETE
    }
    /// <summary>舊來源參數位置子集。</summary>
    public enum ParameterType
    {
        /// <summary>JSON本文。</summary>
        RequestBody,
        /// <summary>不保存傳輸header。</summary>
        HttpHeader
    }
    /// <summary>不保存秘密header的單次離線請求。</summary>
    public sealed class RestRequest
    {
        /// <summary>建立固定方法的請求。</summary>
        public RestRequest(Method method) { Method = method; }
        /// <summary>建立相對資源與方法。</summary>
        public RestRequest(string resource, Method method) { Resource = resource; Method = method; }
        /// <summary>目前方法。</summary>
        public Method Method { get; }
        /// <summary>相對資源，不在輸出保留query。</summary>
        public string Resource { get; } = "";
        /// <summary>供capture解析的JSON本文。</summary>
        internal string Body { get; private set; } = "";
        /// <summary>header刻意不保留，避免存活超過舊呼叫所需期間。</summary>
        public RestRequest AddHeader(string name, string value) => this;
        /// <summary>只接受JSON本文；其他參數位置明確拒絕。</summary>
        public RestRequest AddParameter(string name, object value, ParameterType type)
        {
            if (type != ParameterType.RequestBody) throw new NotSupportedException("Rest parameter unsupported.");
            Body = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "";
            return this;
        }
    }
    /// <summary>舊呼叫取得的固定成功回應子集。</summary>
    public interface IRestResponse
    {
        /// <summary>固定合成內容，不是LINE個資回應。</summary>
        string Content { get; }
        /// <summary>固定成功狀態。</summary>
        HttpStatusCode StatusCode { get; }
    }
    /// <summary>不含外部資源的固定成功回應。</summary>
    public sealed class RestResponse : IRestResponse
    {
        /// <summary>合成profile缺值保留舊端處理，不捏造真實使用者資料。</summary>
        public string Content => "{}";
        /// <summary>離線替身成功。</summary>
        public HttpStatusCode StatusCode => HttpStatusCode.OK;
    }
    /// <summary>沒有真實網路handler的RestClient子集。</summary>
    public class RestClient
    {
        /// <summary>建立尚未指定端點的替身。</summary>
        public RestClient() { }
        /// <summary>保存本次端點直到記錄完成。</summary>
        public RestClient(string url) { BaseUrl = new Uri(url); }
        /// <summary>保存URI端點。</summary>
        public RestClient(Uri url) { BaseUrl = url; }
        /// <summary>本案例端點，不保存header或token。</summary>
        public Uri? BaseUrl { get; set; }
        /// <summary>記錄GET並回固定合成成功。</summary>
        public IRestResponse Get(RestRequest request) => Execute(request);
        /// <summary>只寫本案例capture，沒有任何實際HTTP呼叫。</summary>
        public IRestResponse Execute(RestRequest request)
        {
            var endpoint = BaseUrl ?? throw new InvalidOperationException("Endpoint required.");
            if (!string.IsNullOrEmpty(request.Resource)) endpoint = new Uri(endpoint, request.Resource);
            LegacyGolden.PluginHarness.ExecutionCapture.Current.Record(request.Method.ToString(), endpoint, request.Body);
            return new RestResponse();
        }
    }
}

namespace LegacyGolden.PluginHarness
{
    /// <summary>語法改寫後的Rest建構式；基底也是本工具的純離線替身。</summary>
    public sealed class RecordingRestClient : RestSharp.RestClient
    {
        /// <summary>建立空端點替身。</summary>
        public RecordingRestClient() { }
        /// <summary>建立文字端點替身。</summary>
        public RecordingRestClient(string url) : base(url) { }
        /// <summary>建立URI端點替身。</summary>
        public RecordingRestClient(Uri url) : base(url) { }
    }
}
