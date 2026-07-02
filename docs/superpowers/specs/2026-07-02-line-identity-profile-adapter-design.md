# LINE Identity/Profile Adapter Design

Date: 2026-07-02
Branch: Jesus_5.1.6.WorktreeRefactorLine
Status: Design draft after LINE usage surface inventory

## Goal

Add a small reusable LINE identity/profile adapter surface that future ASP.NET Core products can reuse without importing ChurchReport-specific CRM, routes, or LIFF page logic.

This is a P1 continuation slice after reliable payment notification support. It should not become a broad P2 official API expansion.

## Current Evidence

The inventory in `.ccg/tasks/line-usage-surface-inventory/inventory.md` shows three active LINE usage families:

1. Backend push notifications: payment, booking, and error notifications.
2. Identity and binding: `new_lineid`, `LineIdLogin`, and `RetrieveContactEntityByLineUserId(...)`.
3. LIFF browser flows: multiple views call `liff.init`, `liff.login`, `liff.permission`, and `liff.getProfile` before posting identity data back to ChurchReport.

The SDK already exposes profile-related official APIs:

- `LineMessagingClient.GetUserProfileAsync(userId)`.
- `LineMessagingClient.GetGroupMemberProfileAsync(groupId, userId)`.
- `LineMessagingClient.GetRoomMemberProfileAsync(roomId, userId)`.

`LineMessagingProcessorClass` still has an older RestSharp `GetUserProfile(string UserId)` path. That path is product-neutral in purpose but is not aligned with the newer SDK-backed pattern used by reliable notifications.

## Approved Architectural Direction

Use a thin adapter in `LineMessagingProcessor`:

```text
Future ASP.NET Core product / ChurchReport
    owns product login, CRM/member binding, and route behavior
        |
        v
LineMessagingProcessor identity/profile adapter
    validates LINE identifiers and calls SDK profile methods
        |
        v
Line.Messaging SDK
    owns official LINE HTTP endpoints and response models
        |
        v
LINE Messaging API
```

## Component Responsibilities

### Line.Messaging

- Owns official LINE protocol, endpoint URLs, HTTP request/response handling, and raw LINE model types.
- Already provides `UserProfile` and group/room profile methods.
- Should not receive ChurchReport CRM concepts, `new_lineid`, login routes, or LIFF view behavior.

### LineMessagingProcessor

Add or modernize a product-neutral profile lookup surface:

- `GetUserProfileAsync(string userId)` delegates to `LineMessagingClient.GetUserProfileAsync(userId)`.
- Optional next methods after the first test-backed slice:
  - `GetGroupMemberProfileAsync(string groupId, string userId)`.
  - `GetRoomMemberProfileAsync(string roomId, string userId)`.
- Validates only adapter-level requirements such as non-empty LINE IDs.
- Does not look up CRM contacts.
- Does not decide whether a user is a member, a donor, a group participant, or a Sunday school participant.
- Does not own LIFF JavaScript or MVC route behavior.

### ChurchReport

ChurchReport keeps all product-specific identity and binding behavior:

- CRM lookup by `new_lineid`.
- Member/contact binding and `RetrieveContactEntityByLineUserId(...)`.
- `LineIdLogin` flow decisions.
- QR code, small group, Sunday, dedication, appointment, and visitor-card route behavior.
- LIFF page rendering and browser-side `liff.getProfile()` usage.

## Initial Scope

Included:

1. Add test coverage for a SDK-backed `LineMessagingProcessorClass.GetUserProfileAsync(...)` path.
2. Implement the method by delegating to injected `LineMessagingClient`.
3. Keep the old `GetUserProfile(string UserId)` method compatible unless an implementation plan proves it can be safely redirected.
4. Add a small model or reuse `Line.Messaging.UserProfile` only if that avoids unnecessary mapping.

Excluded:

- No broad refactor of all login controllers.
- No LIFF JavaScript consolidation in this slice.
- No CRM binding extraction into `LineMessagingProcessor`.
- No LINE Login OAuth implementation changes.
- No P2 official API expansion such as rich menu, audience, coupon, membership, statistics, or full webhook management.

## Data Flow

### User profile lookup

```text
Product code has a LINE userId
    -> calls LineMessagingProcessor.GetUserProfileAsync(userId)
    -> processor validates userId is not blank
    -> processor delegates to LineMessagingClient.GetUserProfileAsync(userId)
    -> SDK calls /v2/bot/profile/{userId}
    -> processor returns the profile to product code
    -> product decides how to bind or display it
```

### Group / room member lookup (later in same P1 family)

```text
Product code has groupId/roomId + userId
    -> calls processor group/room profile adapter
    -> processor validates IDs
    -> processor delegates to SDK
    -> product decides how to bind membership context
```

## Error Handling

- Empty `userId`, `groupId`, or `roomId` should fail fast with `ArgumentException` before a network call.
- LINE API failures should propagate from SDK unless the existing processor convention requires logging and rethrowing.
- The adapter should not swallow failures that product code needs to diagnose during login or binding.
- The adapter should not convert LINE failures into CRM decisions.

## Testing Strategy

Minimum tests for the first implementation plan:

- `GetUserProfileAsync` calls SDK `/bot/profile/{userId}` and returns `displayName`, `userId`, `pictureUrl`, and `statusMessage`.
- Empty `userId` throws `ArgumentException` and does not call HTTP.
- Existing reliable notification tests still pass.
- Existing `Line.Messaging.Tests` still pass.
- Solution build passes.

Optional second wave:

- Group member profile lookup delegates to `/bot/group/{groupId}/member/{userId}`.
- Room member profile lookup delegates to `/bot/room/{roomId}/member/{userId}`.

## Linus-Style Maintenance Rules

- One layer owns one concern.
- SDK owns LINE protocol.
- Processor owns reusable LINE convenience methods.
- ChurchReport owns CRM and product binding decisions.
- Do not create a generic abstraction until at least two products need it.
- Do not move LIFF frontend behavior into backend SDK code.
- Prefer direct, deterministic data flow over hidden global state.

## Recommended Implementation Plan Shape

The next implementation plan should be small:

1. Add failing `LineMessagingProcessor.Tests` tests for `GetUserProfileAsync`.
2. Implement the SDK-backed method in `LineMessagingProcessorClass`.
3. Verify processor tests, LINE SDK tests, and solution build.
4. Record review.

Do not modify ChurchReport login controllers in the first implementation unless the tests prove a safe migration target.