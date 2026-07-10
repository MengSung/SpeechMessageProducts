// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：LineMessagingProcessor/LineMessagingProcessorClass.cs
// 所屬區塊：LINE 訊息處理核心橋接層，將產品 workflow 與 LINE Messaging Client 串接。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class LineMessagingProcessorClass、class UserProfile
// 主要成員：NormalizeBearerToken、StripBearerPrefix、ResolveDefaultChannelAccessToken、ResolveChannelAccessToken、GetRequiredChannelAccessToken、Dispose、ProcessMessage_TEST、ProcessMessage、SendMessage、SendReliableMessageAsync
// 引用命名空間：System、System.Collections.Generic、System.IO、System.Linq、System.Text、System.Threading.Tasks、Line.Messaging、Newtonsoft.Json
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Line.Messaging;
using Newtonsoft.Json;
using Microsoft.Extensions.Configuration;

namespace LineMessagingProcessor
{
    public class LineMessagingProcessorClass : IDisposable
    {
        // LINE channel access token 是部署環境的機密資料，不能寫死在原始碼。
        // 建構式會統一正規化成 Authorization header 需要的 Bearer 格式。
        private readonly string _channelAccessToken;
        private readonly LineMessagingClient _lineMessagingClient;
        private readonly bool _requiresChannelAccessToken;
        private readonly bool _ownsLineMessagingClient;

        private static readonly Lazy<string> s_defaultChannelAccessToken = new Lazy<string>(ResolveDefaultChannelAccessToken);

        public String m_UserId = "";
        public String m_Message = "";

        public LineMessagingProcessorClass()
            : this(s_defaultChannelAccessToken.Value)
        {
        }

        public LineMessagingProcessorClass(string channelAccessToken)
        {
            _channelAccessToken = NormalizeBearerToken(channelAccessToken);
            _requiresChannelAccessToken = true;
#pragma warning disable CS0618 // 保留既有 token 建構流程；新的測試/DI 路徑可直接注入 LineMessagingClient。
            _lineMessagingClient = new LineMessagingClient(StripBearerPrefix(_channelAccessToken));
#pragma warning restore CS0618
            _ownsLineMessagingClient = true;
        }

        public LineMessagingProcessorClass(LineMessagingClient lineMessagingClient)
        {
            _lineMessagingClient = lineMessagingClient ?? throw new ArgumentNullException(nameof(lineMessagingClient));
            _channelAccessToken = string.Empty;
            _requiresChannelAccessToken = false;
            _ownsLineMessagingClient = false;
        }

        public LineMessagingProcessorClass(IConfiguration configuration)
            : this(ResolveChannelAccessToken(configuration))
        {
        }

        private static string NormalizeBearerToken(string channelAccessToken)
        {
            if (string.IsNullOrWhiteSpace(channelAccessToken))
            {
                return string.Empty;
            }

            return channelAccessToken.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? channelAccessToken
                : "Bearer " + channelAccessToken;
        }

        private static string StripBearerPrefix(string channelAccessToken)
        {
            if (string.IsNullOrWhiteSpace(channelAccessToken))
            {
                return string.Empty;
            }

            return channelAccessToken.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? channelAccessToken.Substring("Bearer ".Length)
                : channelAccessToken;
        }

        private static string ResolveDefaultChannelAccessToken()
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                .AddEnvironmentVariables()
                .Build();

            return ResolveChannelAccessToken(configuration);
        }

        private static string ResolveChannelAccessToken(IConfiguration configuration)
        {
            if (configuration == null)
            {
                return string.Empty;
            }

            var environmentToken = configuration["LINE_CHANNEL_ACCESS_TOKEN"];
            if (!string.IsNullOrWhiteSpace(environmentToken))
            {
                return environmentToken;
            }

            var defaultOrganization = configuration["LineMessaging:DefaultOrganization"] ?? "Jesus";
            var configuredToken = configuration[$"LineMessaging:{defaultOrganization}:ChannelAccessToken"];

            return configuredToken ?? string.Empty;
        }

        private string GetRequiredChannelAccessToken()
        {
            if (string.IsNullOrWhiteSpace(_channelAccessToken))
            {
                throw new InvalidOperationException(
                    "LINE channel access token is required. Pass it to LineMessagingProcessorClass or set LINE_CHANNEL_ACCESS_TOKEN.");
            }

            return _channelAccessToken;
        }

        #region 釋放記憶體
        private bool _disposed = false;

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                if (_ownsLineMessagingClient)
                {
                    _lineMessagingClient.Dispose();
                }
            }

            _disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~LineMessagingProcessorClass()
        {
            Dispose(false);
        }
        #endregion

        public async Task ProcessMessage_TEST(dynamic aEvent )
        {
            String DisplayUserId = aEvent["source"]["userId"];

            String EventType = aEvent["type"];

            await Task.Delay(0);

        }

        public async Task ProcessMessage(dynamic aEvent)
        {
            String DisplayUserId = aEvent["source"]["userId"];

            String EventType = aEvent["type"];

            if (EventType == "follow")
            {
                #region follow

                m_UserId = aEvent["source"]["userId"];

                await SendMessage(m_UserId, "歡迎加入好牧人");

                #endregion
            }
            else if (EventType == "unfollow")
            {
                #region unfollow

                m_UserId = aEvent["source"]["userId"];
                await SendMessage(m_UserId, "期待您隨時回來好牧人粉絲團");

                #endregion
            }
            else if (EventType == "postback")
            {
                #region postback

                string UserId = aEvent["source"]["userId"];
                string Data = aEvent["postback"]["data"];

                String MessageType = "";
                String Selection = "";
                String LetterEntityId = "";
                ParsePostBackString(Data, ref MessageType, ref Selection, ref LetterEntityId);

                if (MessageType == "模板" || MessageType == "確認")
                {
                    await SendMessage(UserId, "您選擇了 : " + Selection + Environment.NewLine + "正在處理中....");
                }
                #endregion
            }
            else if (EventType == "message")
            {
                #region message
                m_UserId = aEvent["source"]["userId"];

                string MessageType = aEvent["message"]["type"];

                if (MessageType == "text")
                {
                    m_Message = aEvent["message"]["text"];
                }
                else if (MessageType == "image")
                {
                    String MessageId = aEvent["message"]["id"];
                }
                else if (MessageType == "video")
                {
                    String MessageId = aEvent["message"]["id"];
                }
                else if (MessageType == "audio")
                {
                    String MessageId = aEvent["message"]["id"];
                }
                else if (MessageType == "location")
                {
                    String MessageId = aEvent["message"]["id"];
                    String Title = aEvent["message"]["title"];
                    String Address = aEvent["message"]["address"];
                    String Latitude = aEvent["message"]["latitude"];
                    String Longitude = aEvent["message"]["longitude"];
                }
                else if (MessageType == "sticker")
                {
                    String MessageId = aEvent["message"]["id"];
                    String PackageId = aEvent["message"]["packageId"];
                    String StickerId = aEvent["message"]["stickerId"];
                }
                else { }

                #endregion
            }
            else { }
        }

        public async Task SendMessage(string UserId, string Message)
        {
            if (string.IsNullOrWhiteSpace(UserId))
            {
                throw new ArgumentException("UserId is required.", nameof(UserId));
            }

            if (string.IsNullOrWhiteSpace(Message))
            {
                throw new ArgumentException("Message is required.", nameof(Message));
            }

            // 舊版 ChurchReport 流程曾用這個特殊字串要求系統回傳 LINE 使用者 ID。
            // 這不是 LINE 官方 Messaging API 的協定；此處只保留既有文字轉換，
            // 實際 HTTP endpoint、Authorization header 與 JSON 序列化全部交給 Line.Messaging SDK。
            if (Message == "顯示認證")
            {
                Message = "認證:" + UserId;
            }

            if (_requiresChannelAccessToken)
            {
                GetRequiredChannelAccessToken();
            }

            var messages = new List<ISendMessage> { new TextMessage(Message) };
            await _lineMessagingClient.PushMessageAsync(UserId, messages).ConfigureAwait(false);
        }

        /// <summary>
        /// 發送可重試的 LINE 推播訊息。
        /// 此方法只負責「可重用的 LINE 推播入口」：檢查必要欄位、建立文字訊息，
        /// 然後把呼叫交給 Line.Messaging SDK。真正的 X-Line-Retry-Key header
        /// 仍由 SDK 統一處理，避免 Processor 與 SDK 各自實作一份 LINE 協定細節。
        /// </summary>
        /// <param name="UserId">LINE 使用者 ID、群組 ID 或聊天室 ID。</param>
        /// <param name="Message">要推播給付款者的純文字訊息。</param>
        /// <param name="retryKey">由產品端產生的冪等重試鍵；空白時沿用非重試行為。</param>
        public async Task SendReliableMessageAsync(string UserId, string Message, string? retryKey)
        {
            if (string.IsNullOrWhiteSpace(UserId))
            {
                throw new ArgumentException("UserId is required.", nameof(UserId));
            }

            if (string.IsNullOrWhiteSpace(Message))
            {
                throw new ArgumentException("Message is required.", nameof(Message));
            }

            var messages = new List<ISendMessage> { new TextMessage(Message) };
            await _lineMessagingClient.PushMessageAsync(UserId, messages, retryKey).ConfigureAwait(false);
        }

        /// <summary>
        /// 共用 workflow 使用的低階發送入口。
        /// 這個方法只接受 LINE user id 與已組好的 SDK 訊息，避免 workflow 反射讀取 private client，
        /// 也讓未來產品可以重用同一條 SDK-backed push 路徑。
        /// </summary>
        /// <param name="userId">LINE user id。空白時在進入 HTTP 前即拒絕。</param>
        /// <param name="messages">要送出的 LINE SDK 訊息集合。空集合代表呼叫端沒有建立有效內容。</param>
        /// <param name="retryKey">LINE retry key；可為 null，保留一般 push 行為。</param>
        public async Task SendMessagesAsync(string userId, IList<ISendMessage> messages, string? retryKey = null)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                throw new ArgumentException("userId is required.", nameof(userId));
            }

            if (messages == null || messages.Count == 0)
            {
                throw new ArgumentException("messages are required.", nameof(messages));
            }

            await _lineMessagingClient.PushMessageAsync(userId, messages, retryKey).ConfigureAwait(false);
        }

        /// <summary>
        /// 透過 LINE reply token 回覆 webhook 事件。
        /// 這是共用 reply workflow 的最底層 adapter：只包住 SDK 呼叫與基本參數驗證，
        /// 不放 ChurchReport 的回覆文字、CRM 判斷或控制器流程，避免共用 LINE 專案反向依賴產品。
        /// </summary>
        /// <param name="replyToken">LINE webhook 事件提供的一次性 reply token。</param>
        /// <param name="messages">要回覆給 LINE 使用者的 SDK message 清單。</param>
        public async Task ReplyMessagesAsync(string replyToken, IList<ISendMessage> messages)
        {
            if (string.IsNullOrWhiteSpace(replyToken))
            {
                throw new ArgumentException("replyToken is required.", nameof(replyToken));
            }

            if (messages == null || messages.Count == 0)
            {
                throw new ArgumentException("messages are required.", nameof(messages));
            }

            await _lineMessagingClient.ReplyMessageAsync(replyToken, messages).ConfigureAwait(false);
        }

        // RichMenu 相關方法是 LINE SDK 的薄封裝，故意不放產品 catalog、alias 決策或狀態儲存邏輯。
        // 上層共用 workflow 會負責將 ChurchReport 或其他產品的 menu key 轉成這裡需要的 provider richMenuId。

        /// <summary>
        /// 建立 LINE RichMenu 並回傳 LINE 產生的 richMenuId。
        /// Processor 只包住 SDK 與必要參數驗證；RichMenu 版面、圖片與產品套用規則由產品端或 workflow 決定。
        /// </summary>
        public async Task<string> CreateRichMenuAsync(RichMenu richMenu)
        {
            if (richMenu == null)
            {
                throw new ArgumentNullException(nameof(richMenu));
            }

            return await _lineMessagingClient.CreateRichMenuAsync(richMenu).ConfigureAwait(false);
        }

        /// <summary>
        /// 上傳 RichMenu PNG 圖片。
        /// 圖片來源可能是產品專案檔案、Blob 或其他儲存體；Processor 不處理路徑，只接收已開啟的 stream。
        /// </summary>
        public async Task UploadRichMenuPngImageAsync(string richMenuId, Stream imageStream)
        {
            if (string.IsNullOrWhiteSpace(richMenuId))
            {
                throw new ArgumentException("richMenuId is required.", nameof(richMenuId));
            }

            if (imageStream == null)
            {
                throw new ArgumentNullException(nameof(imageStream));
            }

            await _lineMessagingClient.UploadRichMenuPngImageAsync(imageStream, richMenuId).ConfigureAwait(false);
        }

        /// <summary>
        /// 將 RichMenu 綁定到單一 LINE 使用者。
        /// </summary>
        public async Task LinkRichMenuToUserAsync(string userId, string richMenuId)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                throw new ArgumentException("userId is required.", nameof(userId));
            }

            if (string.IsNullOrWhiteSpace(richMenuId))
            {
                throw new ArgumentException("richMenuId is required.", nameof(richMenuId));
            }

            await _lineMessagingClient.LinkRichMenuToUserAsync(userId, richMenuId).ConfigureAwait(false);
        }

        /// <summary>
        /// 查詢使用者目前綁定的 RichMenu ID。
        /// </summary>
        public async Task<string> GetRichMenuIdOfUserAsync(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                throw new ArgumentException("userId is required.", nameof(userId));
            }

            return await _lineMessagingClient.GetRichMenuIdOfUserAsync(userId).ConfigureAwait(false);
        }

        /// <summary>
        /// 解除使用者目前綁定的 RichMenu。
        /// </summary>
        public async Task UnlinkRichMenuFromUserAsync(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                throw new ArgumentException("userId is required.", nameof(userId));
            }

            await _lineMessagingClient.UnLinkRichMenuFromUserAsync(userId).ConfigureAwait(false);
        }

        /// <summary>
        /// 刪除指定 RichMenu。
        /// </summary>
        public async Task DeleteRichMenuAsync(string richMenuId)
        {
            if (string.IsNullOrWhiteSpace(richMenuId))
            {
                throw new ArgumentException("richMenuId is required.", nameof(richMenuId));
            }

            await _lineMessagingClient.DeleteRichMenuAsync(richMenuId).ConfigureAwait(false);
        }

        /// <summary>
        /// 查詢目前 LINE 官方帳號底下已建立的 RichMenu 清單。
        /// 這裡只包住 LINE SDK 的查詢入口，不在 processor 內決定哪些選單要給哪個產品使用；
        /// 產品可透過上層 catalog / provisioning workflow 做自己的部署與比對策略。
        /// </summary>
        public async Task<IList<ResponseRichMenu>> GetRichMenuListAsync()
        {
            return await _lineMessagingClient.GetRichMenuListAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 將指定 RichMenu 設為 LINE 官方帳號的預設選單。
        /// 預設選單是帳號層級設定，影響範圍比單一使用者綁定更大，因此只做參數驗證與 SDK 呼叫，
        /// 是否允許設定預設選單由上層產品或 provisioning workflow 決定。
        /// </summary>
        public async Task SetDefaultRichMenuAsync(string richMenuId)
        {
            if (string.IsNullOrWhiteSpace(richMenuId))
            {
                throw new ArgumentException("richMenuId is required.", nameof(richMenuId));
            }

            await _lineMessagingClient.SetDefaultRichMenuAsync(richMenuId).ConfigureAwait(false);
        }

        /// <summary>
        /// 查詢 LINE 官方帳號目前設定的預設 RichMenu ID。
        /// 呼叫端可用這個值判斷 provisioning 結果，或在切換預設選單前做稽核紀錄。
        /// </summary>
        public async Task<string> GetDefaultRichMenuIdAsync()
        {
            return await _lineMessagingClient.GetDefaultRichMenuIdAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 取消 LINE 官方帳號目前的預設 RichMenu。
        /// 這是帳號層級操作；processor 不做產品規則判斷，只把明確要求交給 SDK 執行。
        /// </summary>
        public async Task CancelDefaultRichMenuAsync()
        {
            await _lineMessagingClient.CancelDefaultRichMenuAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 建立 RichMenu alias，讓產品端可以用穩定的 alias 指向 LINE 產生的 richMenuId。
        /// alias 是未來多產品共用 RichMenu 的重要邊界：產品記住 alias，而不是記住每次部署產生的 ID。
        /// </summary>
        public async Task CreateRichMenuAliasAsync(string richMenuId, string richMenuAliasId)
        {
            if (string.IsNullOrWhiteSpace(richMenuId))
            {
                throw new ArgumentException("richMenuId is required.", nameof(richMenuId));
            }

            if (string.IsNullOrWhiteSpace(richMenuAliasId))
            {
                throw new ArgumentException("richMenuAliasId is required.", nameof(richMenuAliasId));
            }

            await _lineMessagingClient.CreateRichMenuAliasAsync(richMenuId, richMenuAliasId).ConfigureAwait(false);
        }

        /// <summary>
        /// 更新既有 RichMenu alias 指向的新 richMenuId。
        /// provisioning workflow 可以透過這個方法做到「穩定 alias，不穩定實體 ID」的部署模式。
        /// </summary>
        public async Task UpdateRichMenuAliasAsync(string richMenuAliasId, string richMenuId)
        {
            if (string.IsNullOrWhiteSpace(richMenuAliasId))
            {
                throw new ArgumentException("richMenuAliasId is required.", nameof(richMenuAliasId));
            }

            if (string.IsNullOrWhiteSpace(richMenuId))
            {
                throw new ArgumentException("richMenuId is required.", nameof(richMenuId));
            }

            await _lineMessagingClient.UpdateRichMenuAliasAsync(richMenuAliasId, richMenuId).ConfigureAwait(false);
        }

        /// <summary>
        /// 刪除指定 RichMenu alias。
        /// 清除 alias 時不假設產品流程，避免共用 LINE 模組知道任何 ChurchReport 或其他產品語意。
        /// </summary>
        public async Task DeleteRichMenuAliasAsync(string richMenuAliasId)
        {
            if (string.IsNullOrWhiteSpace(richMenuAliasId))
            {
                throw new ArgumentException("richMenuAliasId is required.", nameof(richMenuAliasId));
            }

            await _lineMessagingClient.DeleteRichMenuAliasAsync(richMenuAliasId).ConfigureAwait(false);
        }

        /// <summary>
        /// 查詢單一 RichMenu alias 的官方資料。
        /// adapter 會把官方 404 轉成共用 RichMenu 專案可理解的 alias-not-found 例外。
        /// </summary>
        public async Task<RichMenuAlias> GetRichMenuAliasAsync(string richMenuAliasId)
        {
            if (string.IsNullOrWhiteSpace(richMenuAliasId))
            {
                throw new ArgumentException("richMenuAliasId is required.", nameof(richMenuAliasId));
            }

            return await _lineMessagingClient.GetRichMenuAliasAsync(richMenuAliasId).ConfigureAwait(false);
        }

        /// <summary>
        /// 查詢 LINE 官方帳號底下所有 RichMenu alias。
        /// provisioning workflow 可用這份清單判斷 alias 是否需要新增、更新或維持不變。
        /// </summary>
        public async Task<RichMenuAliasList> GetRichMenuAliasListAsync()
        {
            return await _lineMessagingClient.GetRichMenuAliasListAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 以 SDK 取得 LINE 使用者個人資料。
        /// 這一層只負責「可重用的 LINE 身分查詢」：先確認 UserId 有值，再交給
        /// Line.Messaging SDK 呼叫官方 /bot/profile/{userId} API。
        /// 特定產品的資料庫查詢、會員欄位綁定、登入流程與 LIFF 頁面都不放在這裡，
        /// 避免未來其他 ASP.NET Core 產品重用 LINE 模組時，被某一個產品的流程綁住。
        /// </summary>
        /// <param name="UserId">LINE 使用者 ID。不可為 null、空字串或只包含空白。</param>
        /// <returns>LINE 官方回傳的使用者個人資料。</returns>
        /// <exception cref="ArgumentException">UserId 空白時拋出，且不發出 HTTP request。</exception>
        public async Task<Line.Messaging.UserProfile> GetUserProfileAsync(string UserId)
        {
            if (string.IsNullOrWhiteSpace(UserId))
            {
                throw new ArgumentException("UserId is required.", nameof(UserId));
            }

            return await _lineMessagingClient.GetUserProfileAsync(UserId).ConfigureAwait(false);
        }

        /// <summary>
        /// 舊版同步命名的相容入口。
        /// 保留這個方法是為了不一次破壞既有 ChurchReport 呼叫端；實際資料流已改走
        /// GetUserProfileAsync，讓新舊入口共用同一份 SDK-backed 實作。
        /// </summary>
        /// <param name="UserId">LINE 使用者 ID。</param>
        /// <returns>LINE 官方回傳的使用者個人資料。</returns>
        public async Task<UserProfile> GetUserProfile(string UserId)
        {
            var profile = await GetUserProfileAsync(UserId).ConfigureAwait(false);

            return new UserProfile
            {
                DisplayName = profile.DisplayName,
                UserId = profile.UserId,
                PictureUrl = profile.PictureUrl,
                StatusMessage = profile.StatusMessage
            };
        }

        /// <summary>
        /// 以 SDK 取得 LINE 群組中的成員個人資料。
        /// 這個方法只負責共用 LINE 查詢入口需要的最小工作：驗證 groupId 與 userId，
        /// 然後把官方 API 呼叫交給 Line.Messaging SDK。群組成員是否要綁定到會員、
        /// 小組、課程或任何產品資料，必須由呼叫端產品自己決定，不能放進共用 LINE 模組。
        /// </summary>
        /// <param name="groupId">LINE 群組 ID。不可為 null、空字串或只包含空白。</param>
        /// <param name="userId">LINE 使用者 ID。不可為 null、空字串或只包含空白。</param>
        /// <returns>LINE 官方回傳的群組成員個人資料。</returns>
        /// <exception cref="ArgumentException">groupId 或 userId 空白時拋出，且不發出 HTTP request。</exception>
        public async Task<Line.Messaging.UserProfile> GetGroupMemberProfileAsync(string groupId, string userId)
        {
            if (string.IsNullOrWhiteSpace(groupId))
            {
                throw new ArgumentException("groupId is required.", nameof(groupId));
            }

            if (string.IsNullOrWhiteSpace(userId))
            {
                throw new ArgumentException("userId is required.", nameof(userId));
            }

            return await _lineMessagingClient.GetGroupMemberProfileAsync(groupId, userId).ConfigureAwait(false);
        }

        /// <summary>
        /// 以 SDK 取得 LINE 聊天室中的成員個人資料。
        /// 這個方法與群組成員查詢維持同一個邊界：Processor 只驗證 roomId 與 userId，
        /// 實際 endpoint、HTTP header、JSON 解析都交給 Line.Messaging SDK 統一處理。
        /// 產品端仍然負責判斷這個聊天室成員資料要如何對應到自己的會員或流程。
        /// </summary>
        /// <param name="roomId">LINE 聊天室 ID。不可為 null、空字串或只包含空白。</param>
        /// <param name="userId">LINE 使用者 ID。不可為 null、空字串或只包含空白。</param>
        /// <returns>LINE 官方回傳的聊天室成員個人資料。</returns>
        /// <exception cref="ArgumentException">roomId 或 userId 空白時拋出，且不發出 HTTP request。</exception>
        public async Task<Line.Messaging.UserProfile> GetRoomMemberProfileAsync(string roomId, string userId)
        {
            if (string.IsNullOrWhiteSpace(roomId))
            {
                throw new ArgumentException("roomId is required.", nameof(roomId));
            }

            if (string.IsNullOrWhiteSpace(userId))
            {
                throw new ArgumentException("userId is required.", nameof(userId));
            }

            return await _lineMessagingClient.GetRoomMemberProfileAsync(roomId, userId).ConfigureAwait(false);
        }

        public async Task<String> GetUserDisplayName(string UserId)
        {
            try
            {
                var profile = await GetUserProfile(UserId);

                return profile?.DisplayName ?? "";
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                await SendMessage(UserId, ErrorString);

                throw;
            }
        }

        public async Task NotifyLineBinding(string UserId)
        {
            try
            {
                #region 通知住綁定的輸入格式
                String displayName = await GetUserDisplayName(UserId);
                String EncodeName = System.Net.WebUtility.UrlEncode(displayName) + "," + System.Net.WebUtility.UrlEncode(UserId);
                String CombineEncodeName = "https://tpehoc.speechmessage.com.tw:200/Home/LineBindingView/" + EncodeName;

                await SendMessage(
                    UserId,
                    "請點擊以下網址進行牧養系統與Line的註冊:" + Environment.NewLine + CombineEncodeName
                );
                #endregion
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                await SendMessage(UserId, ErrorString);

                throw;
            }
        }

        #region 工具區
        public void ParsePostBackString(String PostBackString, ref String MessageType, ref String Selection, ref String LetterEntityId)
        {
            String[] SubStrings = PostBackString.Split('&');

            String[] MessageTypeStringArray = SubStrings[0].Split('=');
            MessageType = MessageTypeStringArray[1];

            String[] SelectionStringArray = SubStrings[1].Split('=');
            Selection = SelectionStringArray[1];

            String[] LetterStringArray = SubStrings[2].Split('=');
            LetterEntityId = LetterStringArray[1];
        }

        #endregion
    }

    public class UserProfile
    {
        [JsonProperty("displayName")]
        public string DisplayName { get; set; } = "";

        [JsonProperty("userId")]
        public string UserId { get; set; } = "";

        [JsonProperty("pictureUrl")]
        public string PictureUrl { get; set; } = "";

        [JsonProperty("statusMessage")]
        public string StatusMessage { get; set; } = "";
    }
}
