// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：Line.Messaging/Webhooks/WebhookApplication.cs
// 所屬區塊：LINE Messaging SDK 封裝層，定義 LINE API DTO、Client 呼叫與訊息模型。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class WebhookApplication
// 主要成員：RunAsync、OnMessageAsync、OnJoinAsync、OnLeaveAsync、OnFollowAsync、OnUnfollowAsync、OnBeaconAsync、OnPostbackAsync、OnAccountLinkAsync、OnMemberJoinAsync
// 引用命名空間：System.Collections.Generic、System.Threading.Tasks
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Line.Messaging.Webhooks
{
    /// <summary>
    /// Inherit this class to implement LINE Bot. Then override each event handler.
    /// </summary>
    public abstract class WebhookApplication
    {
        public async Task RunAsync(IEnumerable<WebhookEvent> events)
        {
            foreach (var ev in events)
            {
                switch (ev)
                {
                    case MessageEvent message:
                        await OnMessageAsync(message).ConfigureAwait(false);
                        break;
                    case JoinEvent join:
                        await OnJoinAsync(join).ConfigureAwait(false);
                        break;
                    case LeaveEvent leave:
                        await OnLeaveAsync(leave).ConfigureAwait(false);
                        break;
                    case FollowEvent follow:
                        await OnFollowAsync(follow).ConfigureAwait(false);
                        break;
                    case UnfollowEvent unFollow:
                        await OnUnfollowAsync(unFollow).ConfigureAwait(false);
                        break;
                    case PostbackEvent postback:
                        await OnPostbackAsync(postback).ConfigureAwait(false);
                        break;
                    case BeaconEvent beacon:
                        await OnBeaconAsync(beacon).ConfigureAwait(false);
                        break;
                    case AccountLinkEvent accountLink:
                        await OnAccountLinkAsync(accountLink).ConfigureAwait(false);
                        break;
                    case MemberJoinEvent memberJoin:
                        await OnMemberJoinAsync(memberJoin).ConfigureAwait(false);
                        break;
                    case MemberLeaveEvent memberLeave:
                        await OnMemberLeaveAsync(memberLeave).ConfigureAwait(false);
                        break;
                    case DeviceLinkEvent deviceLink:
                        await OnDeviceLinkAsync(deviceLink).ConfigureAwait(false);
                        break;
                    case DeviceUnlinkEvent deviceUnlink:
                        await OnDeviceUnlinkAsync(deviceUnlink).ConfigureAwait(false);
                        break;

                }
            }
        }

        protected virtual Task OnMessageAsync(MessageEvent ev) => Task.CompletedTask;

        protected virtual Task OnJoinAsync(JoinEvent ev) => Task.CompletedTask;

        protected virtual Task OnLeaveAsync(LeaveEvent ev) => Task.CompletedTask;

        protected virtual Task OnFollowAsync(FollowEvent ev) => Task.CompletedTask;

        protected virtual Task OnUnfollowAsync(UnfollowEvent ev) => Task.CompletedTask;

        protected virtual Task OnBeaconAsync(BeaconEvent ev) => Task.CompletedTask;

        protected virtual Task OnPostbackAsync(PostbackEvent ev) => Task.CompletedTask;

        protected virtual Task OnAccountLinkAsync(AccountLinkEvent ev) => Task.CompletedTask;

        protected virtual Task OnMemberJoinAsync(MemberJoinEvent ev) => Task.CompletedTask;

        protected virtual Task OnMemberLeaveAsync(MemberLeaveEvent ev) => Task.CompletedTask;

        protected virtual Task OnDeviceLinkAsync(DeviceLinkEvent ev) => Task.CompletedTask;

        protected virtual Task OnDeviceUnlinkAsync(DeviceUnlinkEvent ev) => Task.CompletedTask;
    }
}
