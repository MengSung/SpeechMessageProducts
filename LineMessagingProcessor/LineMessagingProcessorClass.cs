using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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

        public async Task<UserProfile> GetUserProfile(string UserId)
        {
            var request = new RestRequest($"profile/{UserId}");
            request.AddHeader("Content-Type", "application/json; charset=UTF-8");
            request.AddHeader("Authorization", GetRequiredChannelAccessToken());

            var response = await _restClient.GetAsync(request);

            if (response != null && response.IsSuccessful && !string.IsNullOrEmpty(response.Content))
            {
                return JsonConvert.DeserializeObject<UserProfile>(response.Content);
            }

            return null;
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
