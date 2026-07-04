# ReplyUtility Group/Room Profile Adapter Design

Date: 2026-07-03
Branch: `Jesus_5.1.6.WorktreeRefactorLine`
Task: `reply-utility-group-room-profile-adapter`

## Purpose

This slice makes one small ChurchReport LINE call site use the reusable processor layer that already exists. `ReplyUtility.EchoAsyncProcessor(...)` currently calls `LineMessagingClient.GetGroupMemberProfileAsync(...)` and `LineMessagingClient.GetRoomMemberProfileAsync(...)` directly. Those protocol-facing calls should be routed through `LineMessagingProcessorClass`, whose group and room member profile methods are already SDK-backed and tested.

The goal is not to remove every direct `LineMessagingClient` usage from ChurchReport in one pass. The goal is to prove the migration pattern on a small, testable lookup path.

## Current Shape

`ReplyUtility` owns webhook reply behavior for ChurchReport. It receives a `LineMessagingClient` in its constructor and uses that client for:

- group member profile lookup,
- room member profile lookup,
- reply messages,
- media content retrieval,
- template/image/sticker replies.

Only the first two lookup calls are in scope. Reply sending and content retrieval remain direct SDK calls for now because this slice is about profile lookup routing, not reply API abstraction.

## Target Architecture

The layer responsibilities remain:

- `Line.Messaging`: owns official LINE endpoints, HTTP headers, JSON serialization, response parsing, and SDK models.
- `LineMessagingProcessor`: owns reusable convenience operations and input validation, including group and room member profile lookup.
- `ChurchReport.Tools.ReplyUtility`: owns ChurchReport webhook/reply workflow and text composition.

`ReplyUtility` should receive or create a processor dependency for profile lookups while continuing to use its existing `LineMessagingClient` for reply sending. The cleanest implementation is a small constructor overload:

```csharp
public ReplyUtility(LineMessagingClient lineMessagingClient)
    : this(lineMessagingClient, new LineMessagingProcessorClass(lineMessagingClient))
{
}

internal ReplyUtility(LineMessagingClient lineMessagingClient, LineMessagingProcessorClass lineMessagingProcessor)
{
    ...
}
```

This keeps existing production call sites working and gives tests a way to inject a processor that uses a captured `HttpClient`.

## Data Flow

Group source:

```text
MessageEvent
  -> ReplyUtility.EchoAsyncProcessor
  -> LineMessagingProcessorClass.GetGroupMemberProfileAsync(source.Id, source.UserId)
  -> LineMessagingClient.GetGroupMemberProfileAsync(...)
  -> LINE SDK HTTP layer
  -> UserProfile.DisplayName
  -> ReplyUtility reply text
```

Room source:

```text
MessageEvent
  -> ReplyUtility.EchoAsyncProcessor
  -> LineMessagingProcessorClass.GetRoomMemberProfileAsync(source.Id, source.UserId)
  -> LineMessagingClient.GetRoomMemberProfileAsync(...)
  -> LINE SDK HTTP layer
  -> UserProfile.DisplayName
  -> ReplyUtility reply text
```

User/direct source stays unchanged and does not add a profile lookup in this slice.

## Error Handling

Blank `groupId`, `roomId`, or `userId` validation stays in `LineMessagingProcessorClass`. If LINE rejects the profile lookup, the exception should propagate as it does today from the direct SDK call. This slice must not add broad catch-and-swallow behavior to `ReplyUtility`.

## Testing

Add focused tests around `ReplyUtility.EchoAsyncProcessor(...)` with a captured HTTP handler:

- group source calls `/bot/group/{groupId}/member/{userId}` through the processor-backed SDK path and replies with the returned display name.
- room source calls `/bot/room/{roomId}/member/{userId}` through the processor-backed SDK path and replies with the returned display name.
- direct user source does not perform group/room profile lookup and still replies.

The tests should avoid real LINE network calls and should assert both the profile lookup request and the reply request where practical.

## Boundaries And Non-Goals

Do not refactor `PushUtility`, rich menu handling, LIFF browser logic, or webhook event modeling in this slice. Do not add broad P2 official API support. Do not introduce ChurchReport, CRM, controller, payment, or UI dependencies into `Line.Messaging` or `LineMessagingProcessor`.

## Review Notes

This design follows the same pattern used by the completed processor profile adapter slices: protocol handling stays in the SDK, reusable validation stays in the processor, and ChurchReport keeps product workflow decisions.
