using Line.Messaging;
using Line.Messaging.Webhooks;
using LineMessagingProcessor;
using LineMessagingProcessor.Workflows;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;

namespace ChurchReport.Tools
{
    public class ReplyUtility
    {
        #region 初始化設定
        private LineMessagingClient m_LineMessagingClient { get; }

        private LineMessagingProcessorClass m_LineMessagingProcessor { get; }

        private ILineReplyWorkflow m_LineReplyWorkflow { get; }

        //private PushUtility m_PushUtility { get; }

        public ReplyUtility(LineMessagingClient LineMessagingClient)
            : this(
                  LineMessagingClient,
                  new LineMessagingProcessorClass(LineMessagingClient),
                  CreateDefaultReplyWorkflow(LineMessagingClient))
        {
        }

        /// <summary>
        /// 產品流程只需要提供 LINE client 與共用 reply workflow 時使用的建構式。
        /// 直接 new LineMessagingProcessorClass 的 adapter 建立細節留在 ReplyUtility 內部，
        /// 避免 ChurchReport 的付款、奉獻、控制器流程到處知道 processor adapter 的建立方式。
        /// </summary>
        public ReplyUtility(
            LineMessagingClient LineMessagingClient,
            ILineReplyWorkflow? lineReplyWorkflow)
            : this(
                  LineMessagingClient,
                  new LineMessagingProcessorClass(LineMessagingClient),
                  lineReplyWorkflow)
        {
        }

        public ReplyUtility(
            LineMessagingClient LineMessagingClient,
            LineMessagingProcessorClass LineMessagingProcessor)
            : this(LineMessagingClient, LineMessagingProcessor, null)
        {
        }

        public ReplyUtility(
            LineMessagingClient LineMessagingClient,
            LineMessagingProcessorClass LineMessagingProcessor,
            ILineReplyWorkflow? lineReplyWorkflow)
        {
            this.m_LineMessagingClient = LineMessagingClient ?? throw new ArgumentNullException(nameof(LineMessagingClient));
            this.m_LineMessagingProcessor = LineMessagingProcessor ?? throw new ArgumentNullException(nameof(LineMessagingProcessor));
            this.m_LineReplyWorkflow = lineReplyWorkflow ?? CreateDefaultReplyWorkflow(LineMessagingClient);

            //m_PushUtility = new PushUtility(LineMessagingClient);

        }

        private static ILineReplyWorkflow CreateDefaultReplyWorkflow(LineMessagingClient lineMessagingClient)
        {
            return new LineReplyWorkflow(new LineMessagingProcessorClass(lineMessagingClient));
        }
        #endregion
        #region Line Messagin Api Reply SDK傳送
        public async Task EchoAsyncProcessor(MessageEvent ev)
        {
            //var userProfile = await m_LineMessagingClient.GetUserProfileAsync(ev.Source.UserId);
            //String UserName = userProfile?.DisplayName ?? "";

            //String Answer = UserName + " 您剛剛說了: " + ((TextEventMessage)ev.Message).Text + "，我會努力協助您的!";

            //var userProfile = await m_LineMessagingClient.GetUserProfileAsync(ev.Source.UserId);
            //String UserName = userProfile?.DisplayName ?? "";

            String UserName = "";
            if (ev.Source.Type == EventSourceType.Group)
            {
                var userProfile = await m_LineMessagingProcessor.GetGroupMemberProfileAsync(ev.Source.Id, ev.Source.UserId);
                UserName = userProfile?.DisplayName ?? "";

                //ConfirmMessage(ev.Source.Id);

                //CarouselMessage(ev.Source.Id);

                //ChurchCarouselMessage(ev.Source.Id);
                //SendMessage(ev.Source.Id, "耶和華愛我");

                //IList<UserProfile> aListOfProfile = await m_LineMessagingClient.GetGroupMemberProfilesAsync(ev.Source.Id);

                //foreach( UserProfile aUserProfile  in aListOfProfile )
                //{
                //    String LocalUserName = userProfile?.DisplayName ?? "";
                //    UserName += LocalUserName + "，";
                //}
            }
            else if (ev.Source.Type == EventSourceType.Room)
            {
                var userProfile = await m_LineMessagingProcessor.GetRoomMemberProfileAsync(ev.Source.Id, ev.Source.UserId);
                UserName = userProfile?.DisplayName ?? "";

                //ConfirmMessage(ev.Source.Id);

                //CarouselMessage(ev.Source.Id);

                //ChurchCarouselMessage(ev.Source.Id);

                //SendMessage(ev.Source.Id, "耶和華愛我!");

                //IList<UserProfile> aListOfProfile = await m_LineMessagingClient.GetRoomMemberProfilesAsync(ev.Source.Id);

                //foreach (UserProfile aUserProfile in aListOfProfile)
                //{
                //    String LocalUserName = userProfile?.DisplayName ?? "";
                //    UserName += LocalUserName + "，";
                //}

            }
            else
            {
                //var userProfile = await m_LineMessagingClient.GetUserProfileAsync(ev.Source.UserId);
                //UserName = userProfile?.DisplayName ?? "";

                //m_PushUtility.SendMessage(ev.Source.Id, "耶和華愛我!" + UserName);
            }
            String Answer = UserName + "您剛剛說了: " + ((TextEventMessage)ev.Message).Text + "，願耶穌與您同在!";

            await EchoAsync(ev.ReplyToken, Answer);
        }
        public async Task ReplyMessage(string replyToken, List<ISendMessage> MessageToSend)
        {
            try
            {
                await ReplyBestEffortSdkMessagesAsync(
                    replyToken,
                    MessageToSend,
                    "ChurchReport.ReplyUtility.ReplyMessage");

                return;
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw;
            }
        }

        public async Task ReplyMessageAsync(string replyToken, string TextMessage)
        {
            List<ISendMessage> MessageToSend = new List<ISendMessage>
                {
                    new TextMessage(TextMessage)
                };

            await ReplyBestEffortSdkMessagesAsync(
                replyToken,
                MessageToSend,
                "ChurchReport.ReplyUtility.ReplyMessageAsync");

            return;
        }
        public Task EchoAsync(string replyToken, string userMessage)
        {
            return ReplyBestEffortTextAsync(
                replyToken,
                userMessage,
                "ChurchReport.ReplyUtility.EchoAsync");
        }

        public async Task PostSerializedConfirm(string replyToken, String AltText, String Text, List<ITemplateAction> aITemplateAction)
        {
            try
            {
                ISendMessage ConfirmTemplateMessage = new TemplateMessage
                (
                    AltText,
                    new ConfirmTemplate(Text, actions: aITemplateAction)
                );

                List<ISendMessage> MessageToSend = new List<ISendMessage>
                {
                    ConfirmTemplateMessage,
                };

                await ReplyBestEffortSdkMessagesAsync(
                    replyToken,
                    MessageToSend,
                    "ChurchReport.ReplyUtility.PostSerializedConfirm");
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw;
            }
        }
        public async Task PostSerializedImageMap(string replyToken, string AltText, string ImageUrl, int BaseWidth, int Basehight, List<IImagemapAction> aImagemapAction)
        {
            try
            {
                ISendMessage ImageMapTemplateMessage = new ImagemapMessage
                    (
                        ImageUrl, AltText,
                        new ImagemapSize(BaseWidth, Basehight),
                        aImagemapAction
                    );

                List<ISendMessage> MessageToSend = new List<ISendMessage>
                {
                    ImageMapTemplateMessage,
                };

                await ReplyBestEffortSdkMessagesAsync(
                    replyToken,
                    MessageToSend,
                    "ChurchReport.ReplyUtility.PostSerializedImageMap");

            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw;
            }
        }

        public async Task EchoImageAsync(string replyToken, string messageId, string blobDirectoryName)
        {
            var imageName = messageId + ".jpeg";
            var previewImageName = messageId + "_preview.jpeg";

            var imageStream = await this.m_LineMessagingClient.GetContentStreamAsync(messageId);

            var image = Image.FromStream(imageStream);
            var previewImage = image.GetThumbnailImage((int)(image.Width * 0.25), (int)(image.Height * 0.25), () => false, IntPtr.Zero);

            //var blobImagePath = await BlobStorage.UploadImageAsync(image, blobDirectoryName, imageName);
            //var blobPreviewPath = await BlobStorage.UploadImageAsync(previewImage, blobDirectoryName, previewImageName);
        }
        public async Task UploadMediaContentAsync(string replyToken, string messageId, string blobDirectoryName, string blobName)
        {
            //var stream = await this.m_LineMessagingClient.GetContentStreamAsync(messageId);
            //var ext = GetFileExtension(stream.ContentHeaders.ContentType.MediaType);
            //var uri = await BlobStorage.UploadFromStreamAsync(stream, blobDirectoryName, blobName + ext);
        }
        public async Task ReplyRandomStickerAsync(string replyToken)
        {
            //Sticker ID of bssic stickers (packge ID =1)
            //see https://devdocs.line.me/files/sticker_list.pdf
            var stickerids = Enumerable.Range(1, 17)
                .Concat(Enumerable.Range(21, 1))
                .Concat(Enumerable.Range(100, 139 - 100 + 1))
                .Concat(Enumerable.Range(401, 430 - 400 + 1)).ToArray();

            var rand = new Random(Guid.NewGuid().GetHashCode());
            var stickerId = stickerids[rand.Next(stickerids.Length - 1)].ToString();
            await ReplyBestEffortSdkMessagesAsync(
                replyToken,
                new ISendMessage[] { new StickerMessage("1", stickerId) },
                "ChurchReport.ReplyUtility.ReplyRandomStickerAsync");
        }

        /// <summary>
        /// ChurchReport 的 reply-token 發送集中點。
        /// 所有 helper 都走共用 LINE reply workflow，讓 product code 不再直接呼叫 LINE reply SDK。
        /// </summary>
        private async Task ReplyBestEffortSdkMessagesAsync(
            string replyToken,
            IReadOnlyList<ISendMessage> messages,
            string source)
        {
            await m_LineReplyWorkflow.ReplyAsync(new LineReplyRequest
            {
                ReplyToken = replyToken,
                Messages = messages,
                Metadata = new Dictionary<string, string>
                {
                    ["source"] = source
                }
            });
        }

        private Task ReplyBestEffortTextAsync(
            string replyToken,
            string message,
            string source)
        {
            return ReplyBestEffortSdkMessagesAsync(
                replyToken,
                new ISendMessage[] { new TextMessage(message) },
                source);
        }
        #endregion

    }
}
