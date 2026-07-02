# LINE Group and Room Profile Adapter Design

Date: 2026-07-02
Branch: Jesus_5.1.6.WorktreeRefactorLine
Status: Approved P1 follow-up slice

## Goal

Add a small reusable `LineMessagingProcessor` adapter surface for LINE group-member and room-member profile lookups.

This extends the completed identity/profile adapter without broadening into P2 Messaging API coverage. Future ASP.NET Core products can reuse these methods when they already have a LINE `groupId` or `roomId` plus `userId`, while each product keeps its own binding, route, CRM, and LIFF decisions.

## Scope

Included:

- `LineMessagingProcessorClass.GetGroupMemberProfileAsync(string groupId, string userId)`.
- `LineMessagingProcessorClass.GetRoomMemberProfileAsync(string roomId, string userId)`.
- Adapter-level validation for blank IDs before any HTTP call.
- Tests proving delegation to the existing SDK endpoints and no-HTTP validation.

Excluded:

- No ChurchReport controller or LIFF page refactor.
- No CRM or contact binding logic.
- No LINE Login OAuth changes.
- No broad P2 official API expansion such as rich menu, audience, statistics, coupon, webhook management, or membership management.
- No new abstraction layer until a second product proves it is needed.

## Architecture

```text
ASP.NET Core product / ChurchReport
    owns product identity, CRM binding, routing, and LIFF behavior
        |
        v
LineMessagingProcessor group/room profile adapter
    validates IDs and delegates to SDK
        |
        v
Line.Messaging SDK
    owns official LINE endpoint paths and HTTP behavior
        |
        v
LINE Messaging API
```

## Component Responsibilities

### Line.Messaging

- Already owns `GetGroupMemberProfileAsync(groupId, userId)`.
- Already owns `GetRoomMemberProfileAsync(roomId, userId)`.
- Owns endpoint paths:
  - `/bot/group/{groupId}/member/{userId}`
  - `/bot/room/{roomId}/member/{userId}`

### LineMessagingProcessor

- Adds product-neutral convenience methods.
- Validates `groupId`, `roomId`, and `userId`.
- Returns `Line.Messaging.UserProfile` directly.
- Does not decide whether the profile belongs to a church member, group member, donor, visitor, or classroom participant.

### Product Layer

- Supplies the LINE identifiers.
- Decides how the returned profile is used.
- Keeps CRM fields, database lookups, MVC routes, and LIFF pages outside reusable LINE modules.

## Error Handling

- Blank `groupId`, `roomId`, or `userId` throws `ArgumentException`.
- Validation happens before SDK delegation, so invalid input does not send HTTP.
- LINE API failures propagate from the SDK.
- The processor does not convert LINE failures into product binding outcomes.

## Testing Strategy

- Group member profile test verifies the SDK endpoint URL and returned profile fields.
- Room member profile test verifies the SDK endpoint URL and returned profile fields.
- Blank group/room/user IDs throw `ArgumentException` and do not call HTTP.
- Existing identity profile and reliable notification tests still pass.
- Existing `Line.Messaging.Tests` still pass.
- Full solution build passes.

## Maintenance Rules

- Keep direct data flow: product input -> processor validation -> SDK call -> product decision.
- Keep one concern per layer.
- Do not hide identifiers in global state.
- Do not add product-specific naming or CRM concepts to reusable modules.
- Prefer a thin adapter over a broad generic identity framework.
