using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Line.Messaging;
using RestSharp;
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

        private static readonly Lazy<string> s_defaultChannelAccessToken = new Lazy<string>(ResolveDefaultChannelAccessToken);

        public String m_UserId = "";
        public String m_Message = "";

        private readonly RestClient _restClient;

        public LineMessagingProcessorClass()
            : this(s_defaultChannelAccessToken.Value)
        {
        }

        public LineMessagingProcessorClass(string channelAccessToken)
        {
            _channelAccessToken = NormalizeBearerToken(channelAccessToken);
            var options = new RestClientOptions("https://api.line.me/v2/bot");
            _restClient = new RestClient(options);
#pragma warning disable CS0618 // 保留既有 token 建構流程；新的測試/DI 路徑可直接注入 LineMessagingClient。
            _lineMessagingClient = new LineMessagingClient(StripBearerPrefix(_channelAccessToken));
#pragma warning restore CS0618
        }

        public LineMessagingProcessorClass(LineMessagingClient lineMessagingClient)
        {
            _lineMessagingClient = lineMessagingClient ?? throw new ArgumentNullException(nameof(lineMessagingClient));
            _channelAccessToken = string.Empty;
            var options = new RestClientOptions("https://api.line.me/v2/bot");
            _restClient = new RestClient(options);
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
                // RestClient in v112.x doesn't implement IDisposable
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
            var request = new RestRequest("message/push");
            
            request.AddHeader("Content-Type", "application/json; charset=UTF-8");
            request.AddHeader("Authorization", GetRequiredChannelAccessToken());

            if (Message == "顯示認證")
            {
                var messageData = new
                {
                    to = UserId,
                    messages = new[]
                    {
                        new
                        {
                            type = "text",
                            text = "認證:" + UserId
                        }
                    }
                };

                request.AddJsonBody(messageData);
            }
            else
            {
                var messageData = new
                {
                    to = UserId,
                    messages = new[]
                    {
                        new
                        {
                            type = "text",
                            text = Message
                        }
                    }
                };

                request.AddJsonBody(messageData);
            }

            await _restClient.PostAsync(request);
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
