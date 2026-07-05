// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：Line.Messaging/Messages/Action/RichMenuSwitchTemplateAction.cs
// 所屬區塊：LINE Messaging SDK 封裝層，定義 LINE API DTO、Client 呼叫與訊息模型。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class RichMenuSwitchTemplateAction
// 主要成員：CreateFrom、Type、RichMenuAliasId、Label、Data
// 引用命名空間：Newtonsoft.Json
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using Newtonsoft.Json;

namespace Line.Messaging
{
    /// <summary>
    /// RichMenu 切換動作。
    /// 使用者點擊綁定此 action 的區域時，LINE 會切換到 <c>richMenuAliasId</c> 指向的 RichMenu。
    /// https://developers.line.biz/en/reference/messaging-api/#richmenu-switch-action
    /// 此 action 依賴 RichMenu alias，因此佈建流程必須先建立或更新 alias，使用者點擊時才會成功切換。
    /// </summary>
    public class RichMenuSwitchTemplateAction : ITemplateAction
    {
        /// <summary>
        /// 取得序列化到 LINE JSON 時使用的 RichMenu switch action 類型識別值。
        /// </summary>
        public TemplateActionType Type { get; } = TemplateActionType.RichMenuSwitch;

        /// <summary>
        /// 要切換到的 RichMenu alias ID。
        /// LINE 會在使用者點擊當下解析 alias，因此佈署可輪替底層 richMenuId，而 action payload 仍維持穩定。
        /// </summary>
        [JsonProperty("richMenuAliasId")]
        public string RichMenuAliasId { get; set; }

        /// <summary>
        /// action 標籤。
        /// LINE 限制最長 20 個字元；在 RichMenu 上不顯示，但 template message 仍可能需要。
        /// iOS 與 Android 的 LINE 8.11.0 以後支援此欄位；即使未來重用在 RichMenu 以外的介面，也應保持簡短。
        /// </summary>
        [JsonProperty("label")]
        public string Label { get; set; }

        /// <summary>
        /// 使用者點擊後，LINE webhook 會放在 postback event 的 <c>postback.data</c> 內回傳的字串。
        /// 最長 300 個字元，可用於應用程式路由、稽核或後續流程判斷。
        /// </summary>
        [JsonProperty("data")]
        public string Data { get; set; }

        /// <summary>
        /// 建立 RichMenu switch action。
        /// </summary>
        /// <param name="richMenuAliasId">RichMenu alias ID。</param>
        /// <param name="data">postback data。</param>
        /// <param name="label">選填 action 標籤。</param>
        public RichMenuSwitchTemplateAction(string richMenuAliasId, string data, string label = null)
        {
            RichMenuAliasId = richMenuAliasId;
            Data = data;
            Label = label;
        }

        internal static RichMenuSwitchTemplateAction CreateFrom(dynamic dynamicObject)
        {
            // LINE 回傳 malformed 或不完整 response 時，action payload 可能是 null；
            // 這裡保留既有 nullable 行為，避免與其他 template-action parser 的相容性分歧。
            if (dynamicObject == null) return null;
            return new RichMenuSwitchTemplateAction(
                (string)dynamicObject?.richMenuAliasId,
                (string)dynamicObject?.data,
                (string)dynamicObject?.label
            );
        }
    }
}
