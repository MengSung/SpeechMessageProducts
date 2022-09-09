using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using RestSharp;

using RestSharp.Serializers;
using RestSharp.Deserializers;

namespace LineMessagingProcessor
{
    public class LineMessagingProcessorClass : IDisposable
    {
        #region 設定 m_ChannelAccessToken，新增組織修改區

        // 音訊科技
        //String m_ChannelAccessToken = "Bearer RvnT/SCXqbHbGKSUm6y7PDW4G+KHMcsJPZdXqnEPg9JZiPrRcrYnn8jG/hn/Mvcher+IqARAc4B02aRzXCjrs+cI/VV7Gw2c3MsbhGlTJRSZntVfJeiKWejJqPT27dnstPcgaFER2FaW5sf9ipliQAdB04t89/1O/w1cDnyilFU=";

        // 咩咩俱樂部
        //String m_ChannelAccessToken = "Bearer zBJV+jmsWVq7fRDlqQthGK4Pb7lgj7L1P5Q7PUyq8lxhI2GTagRKLJx5ASK5FNjXUebUryqbWDj1CNU5s3iaQPlm1DBDOX4wke/sSawNMEgv7O2PqWRPc2qezlQqS6mhFm3OeIltJ7bYjPePi2eqLQdB04t89/1O/w1cDnyilFU=";

        // 理債一日便
        //String m_ChannelAccessToken = "Bearer " + "0NhRlPIi85qb3pfJbhcyP+Y4Tw+F/Jz0kjHqzfvduTtdzlNOf9NJQW8DZ2NXpEWmpGYvEUQwekGNaoGtwKlu3+ugco6lu8QNGs1P14YeFRG3OSuXktpRt7atnYqMEl7ABYxgBSCq52pMVx58F/RpzwdB04t89/1O/w1cDnyilFU=";

        // 台北基督之家
        // String m_ChannelAccessToken = "Bearer " + "YTd17Eep3V5/nSaI1lxLW5vx//gOfVr21kpnpZ6RBOfvFrjhJYpvtmCIy7yxDi2tQ2cfP/6qGJ9raS72VwN7xhGjneynJHpCRrgJbz4GqMGMMEjLAcVB+hRRNCTNkMOY3rYyyN/W+/sTAx3HzzhsPgdB04t89/1O/w1cDnyilFU=";

        // 樹林教會
        //String m_ChannelAccessToken = "Bearer " + "36PV/e/hoJ9+CAqRwzO34PRWTQJSmkkIH0uXrV0bFPOSYmvUpNa1xx0G+BKrDmoce77OdGsItv4dTaLY35iG+KiIYpmkOzklQWm4N6jedvJKj9ruarXG+JKpPzUY6UlS0I+NS+6iD5ahJ+UhNaYaMwdB04t89/1O/w1cDnyilFU=";

        // 順風美醫診所
        //String m_ChannelAccessToken = "Bearer " + "s+583b2Rgbv4APgXhkNVpmx+wlaU04wWh82c/6i5Tyjsqh6SBQdBUjLc3b9C9tk4XK+1/TOeetLqFR+KdNromuUaS1Ih/T7gfXS3U/IRY0XqiQCYhrOC0TYKjeFuiDhAHpGidPcimIb6oVkqo5jBDQdB04t89/1O/w1cDnyilFU=";

        // 台北基督之家(測試版)
        //String m_ChannelAccessToken = "Bearer " + "/iNy46gPp/ZXokg1Vr9RV/ZjodE3i7Q2o+k9nlH7l3pV8WzjAegGDduZc7gms8X5zrjSrDy2xSdNFud7JqjSDjwcTXZ6MJ/FF3NuhVg6WuXmMT34gAO7VZ0RWYrHXwAifVKpOyh2/8LiGgBpfo4ZXQdB04t89/1O/w1cDnyilFU=";

        // 台北基督之家
        //String m_ChannelAccessToken = "Bearer " + "s+583b2Rgbv4APgXhkNVpmx+wlaU04wWh82c/6i5Tyjsqh6SBQdBUjLc3b9C9tk4XK+1/TOeetLqFR+KdNromuUaS1Ih/T7gfXS3U/IRY0XqiQCYhrOC0TYKjeFuiDhAHpGidPcimIb6oVkqo5jBDQdB04t89/1O/w1cDnyilFU=";

        // 台北基督之家(進階版)
        //String m_ChannelAccessToken = "Bearer " + "a5bB4sunKwoZGjbf0HvFnenCpiABmzIT6rGU4rQ25QAqDhxj8Wa+RwXKQN2CZVC3lSk2sZ2n5bqzCcvaa8J/DIOzUdLUUgq1wF6SIvcd0sL0uFWn0+XyaQXdii1QHvA4Lm+NU5wehU4zIhdxZaMMsAdB04t89/1O/w1cDnyilFU=";

        // 思恩堂豐富教會
        String m_ChannelAccessToken = "Bearer " + "PhC1ibjhqnR1CiDPyRsO6yvTmB1pWRiZAEQEsdTc0ibRd9hn3j1u3yOZf6IFneDsy3x1TBJgL1ODRxhpm9nTjELXi6uK3NFBapHXlogGsZryEIq6rZAVQ37cwquPr6sruwmkvRjQrxIvubS50aXBEwdB04t89/1O/w1cDnyilFU=";

        #endregion

        public String m_UserId = "";
        public String m_Message = "";

        #region 釋放記憶體
        private bool _disposed = false;

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                //m_Crm2011ProcessorClass.Dispose();
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
            // Do not re-create Dispose clean-up code here.
            // Calling Dispose(false) is optimal in terms of
            // readability and maintainability.
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

                //SendMessage(m_UserId, "歡迎加入音訊科技粉絲團");
                SendMessage(m_UserId, "歡迎加入台北基督之家");
                //SendMessage(m_UserId, "歡迎加入樹林教會");
                //await SendMessage(m_UserId, "順風美醫診所");

                //await m_Crm2011ProcessorClass.ProcessFollow(this.m_UserId);
                #endregion
            }
            else if (EventType == "unfollow")
            {
                #region unfollow

                m_UserId = aEvent["source"]["userId"];
                //SendMessage(m_UserId, "期待您隨時回來音訊科技粉絲團");
                SendMessage(m_UserId, "期待您隨時回來台北基督之家粉絲團");
                //SendMessage(m_UserId, "期待您隨時回來樹林教會");
                //await SendMessage(m_UserId, "期待您隨時回來順風美醫診所");


                //await m_Crm2011ProcessorClass.ProcessUnFollow(this.m_UserId);

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
                    //SendMessage(UserId, "LetterEntityId = " + LetterEntityId + Environment.NewLine );
                    //await m_Crm2011ProcessorClass.SetLetterEntitySelection(UserId, LetterEntityId, Selection);

                    //SendMessage(UserId, "處理已完成了!");
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
                    //await m_Crm2011ProcessorClass.ProcessLineMessage(this.m_UserId, this.m_Message);
                    //this.SendMessage(this.m_UserId, "您輸入了 : " + m_Message);
                }
                else if (MessageType == "image")
                {
                    String MessageId = aEvent["message"]["id"];
                    //await m_Crm2011ProcessorClass.GetMessageContent(this.m_UserId, MessageId);
                }
                else if (MessageType == "video")
                {
                    String MessageId = aEvent["message"]["id"];
                    //await m_Crm2011ProcessorClass.GetMessageContent(this.m_UserId, MessageId);
                }
                else if (MessageType == "audio")
                {
                    String MessageId = aEvent["message"]["id"];
                    //await m_Crm2011ProcessorClass.GetMessageContent(this.m_UserId, MessageId);
                }
                else if (MessageType == "location")
                {
                    String MessageId = aEvent["message"]["id"];
                    String Title = aEvent["message"]["title"];
                    String Address = aEvent["message"]["address"];
                    String Latitude = aEvent["message"]["latitude"];
                    String Longitude = aEvent["message"]["longitude"];
                    //await m_Crm2011ProcessorClass.CreateLineLocationMessage(this.m_UserId, MessageId, Title, Address, Latitude, Longitude);
                }
                else if (MessageType == "sticker")
                {
                    String MessageId = aEvent["message"]["id"];
                    String PackageId = aEvent["message"]["packageId"];
                    String StickerId = aEvent["message"]["stickerId"];
                    //await m_Crm2011ProcessorClass.CreateLineStickerMessage(this.m_UserId, MessageId, PackageId, StickerId);
                }
                else { }

                #endregion
            }
            else { }
        }

        public async Task SendMessage(string UserId, string Message)
        {
            //Uri aUri = new Uri(@"https://api.line.me/v2/bot/message/push");

            //var client = new HttpClient();
            //client.Po
            //
            //           client.BaseAddress = newUri("http://localhost:55587/"); 
            //
            //           //isRock.LineBot.Utility.PushMessage("U4a2271a6d741feb88d70be27f6aaca63", "不斷的歌唱", "whZPARvpPAe3VMkCGdasb0ITlJCDncda2StsfAKso8kgWMyDhewAjc5sey0j1RUIer+IqARAc4B02aRzXCjrs+cI/VV7Gw2c3MsbhGlTJRROI4+I+e1Uk3WYshxidk9R+8bS9lNrBy/VJCrbBh4ryAdB04t89/1O/w1cDnyilFU=");
            //
            //           //         //XDocument xml = XDocument.Load(Server.MapPath("~/App_Data/confidential.xml"));
            //           //
            //           //         // https://api.line.me/v2/bot/message/push
            //           //         //RestClient restClient = new RestClient(@"https://trialbot-api.line.me/v1/events");
            RestClient restClient = new RestClient(@"https://api.line.me/v2/bot/message/push");
            RestRequest restRequest = new RestRequest(Method.POST);
            //           //
            restRequest.AddHeader("Content-Type", "application/json; charset=UTF-8");
            restRequest.AddHeader("Authorization", m_ChannelAccessToken );



            if (Message == "顯示認證")
            {
                var locations = new Dictionary<string, object>();
                locations.Add("type", "text");
                //locations.Add("text", "JESUS IS A SAVIOR");
                locations.Add("text", "認證:" + UserId);

                JsonObject o = new JsonObject();

                foreach (var kvp in locations)
                {
                    o.Add(kvp);
                }

                JsonArray arr = new JsonArray();
                arr.Add(o);


                restRequest.AddJsonBody
                (
                    new
                    {
                        //to = "U4a2271a6d741feb88d70be27f6aaca63",
                        to = UserId,
                        messages = arr
                    }
                );


                restClient.PostAsync(restRequest, (response, handle) =>
                {
                    //Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
                });
                //var restResponse = await restClient.ExecuteTaskAsync(restRequest, cancellationTokenSource.Token);
            }
            else
            {
                var locations = new Dictionary<string, object>();
                locations.Add("type", "text");
                //locations.Add("text", "JESUS IS A SAVIOR");
                locations.Add("text", Message);

                JsonObject o = new JsonObject();

                foreach (var kvp in locations)
                {
                    o.Add(kvp);
                }

                JsonArray arr = new JsonArray();
                arr.Add(o);


                restRequest.AddJsonBody
                (
                    new
                    {
                            //to = "U4a2271a6d741feb88d70be27f6aaca63",
                            to = UserId,
                            messages = arr
                    }
                );

                restClient.PostAsync(restRequest, (response, handle) =>
                {
                    //Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
                });
                //var restResponse = await restClient.ExecuteTaskAsync(restRequest, cancellationTokenSource.Token);

            }
        }

        public List<UserProfile> GetUserProfile(string UserId)
        {
            RestClient restClient = new RestClient(@"https://api.line.me/v2/bot/profile/" + UserId);
            RestRequest restRequest = new RestRequest(Method.GET);
            restRequest.AddHeader("Content-Type", "application/json; charset=UTF-8");
            restRequest.AddHeader("Authorization", m_ChannelAccessToken );

            var restResponse = restClient.Get(restRequest);

            var deserializer = new JsonDeserializer();

            return deserializer.Deserialize<List<UserProfile>>(restResponse);

            //List<UserProfile> aList = deserializer.Deserialize<List<UserProfile>>(restResponse);

            //JsonConvert.DeserializeObject<T>(restResponse.Content);
            //JO aJObject = restResponse.Content;
            //
            //
            //dynamic json = Newtonsoft.Json.Linq.JObject.Parse(response.Content);
            //restClient.GetAsyncPostAsync(restRequest, (response, handle) =>
            //{
            //    //Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            //});
            //var restResponse = await restClient.ExecuteTaskAsync(restRequest, cancellationTokenSource.Token);
        }

        public String GetUserDisplayName(string UserId)
        {
            try
            {
                List<UserProfile> aList = this.GetUserProfile(UserId);

                return aList[0].DisplayName;

            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                SendMessage(UserId, ErrorString);

                //Monitor.Exit(this);
                throw e;
            }
        }

        public void NotifyLineBinding(string UserId)
        {
            try
            {
                #region 通知住綁定的輸入格式
                String EncodeName = System.Net.WebUtility.UrlEncode(GetUserDisplayName(UserId)) + "," + System.Net.WebUtility.UrlEncode(UserId);
                String CombineEncodeName = "https://tpehoc.speechmessage.com.tw:200/Home/LineBindingView/" + EncodeName;

                //string DeCoded = System.Net.WebUtility.UrlDecode(CombineEncodeName);

                //SendMessage(UserId,  "請點擊以下網址進行牧養系統與Line的註冊:");

                SendMessage(
                    UserId,
                    "請點擊以下網址進行牧養系統與Line的註冊:" + Environment.NewLine + CombineEncodeName
                    );
                #endregion

            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                SendMessage(UserId, ErrorString);

                //Monitor.Exit(this);
                throw e;
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
        //"displayName":"LINE taro",
        //"userId":"Uxxxxxxxxxxxxxx...",
        //"pictureUrl":"http://obs.line-apps.com/...",
        //"statusMessage":"Hello, LINE!"
        public string DisplayName { get; set; }
        public string UserId { get; set; }
        public string PictureUrl { get; set; }
        public string StatusMessage { get; set; }
    }

}
