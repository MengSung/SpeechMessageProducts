/*
 * 檔案整體說明
 *
 * 目的與定位：本檔提供可繼承的webhook事件分派骨架，將已解析事件交給各類型handler；產品身分及業務授權由handler前置流程負責。
 *
 * 主要功能與流程：
 * 本檔宣告 WebhookApplication。
 * 主要操作入口包含 RunAsync、OnMessageAsync、OnJoinAsync、OnLeaveAsync、OnFollowAsync、OnUnfollowAsync、OnBeaconAsync、
 * OnPostbackAsync；其餘入口見下方成員契約。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 */
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
