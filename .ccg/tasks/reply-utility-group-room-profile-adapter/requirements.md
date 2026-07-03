# ReplyUtility Group/Room Profile Adapter Requirements

## Goal

Move `ChurchReport/Tools/ReplyUtility.cs` group and room member profile lookups away from direct `LineMessagingClient` calls and onto the existing `LineMessagingProcessorClass` SDK-backed adapter methods.

## Scope

- Target only `ReplyUtility.EchoAsyncProcessor(MessageEvent ev)`.
- Replace direct calls to:
  - `LineMessagingClient.GetGroupMemberProfileAsync(ev.Source.Id, ev.Source.UserId)`
  - `LineMessagingClient.GetRoomMemberProfileAsync(ev.Source.Id, ev.Source.UserId)`
- Use the existing processor methods:
  - `LineMessagingProcessorClass.GetGroupMemberProfileAsync(groupId, userId)`
  - `LineMessagingProcessorClass.GetRoomMemberProfileAsync(roomId, userId)`
- Keep reply sending through `LineMessagingClient.ReplyMessageAsync(...)` unchanged in this slice.

## Non-Goals

- Do not refactor `PushUtility`.
- Do not refactor rich menu methods.
- Do not change LIFF/browser flows.
- Do not add broad LINE P2 official API coverage.
- Do not move ChurchReport CRM, controller, payment, or UI behavior into `Line.Messaging` or `LineMessagingProcessor`.

## Acceptance Criteria

- Group message profile lookup goes through the processor adapter.
- Room message profile lookup goes through the processor adapter.
- User/direct message branch remains behaviorally unchanged.
- Existing reply behavior remains unchanged.
- Tests prove group and room lookup routing without real LINE network calls.
- Build and LINE-related tests pass.
