# LINE P1 Closeout and Next Slice Inventory

Date: 2026-07-02
Branch: Jesus_5.1.6.WorktreeRefactorLine
Status: P1 closeout inventory after reliable notification, identity profile, and group/room profile adapters

## Purpose

This document records the actual LINE usage surface after the completed P1 slices. It is intentionally an inventory and recommendation document, not an implementation plan. The goal is to choose the next small, maintainable slice instead of jumping into a broad P2 API expansion.

## Completed P1 Reusable Adapter Slices

### Reliable payment notification adapter

- `LineMessagingProcessorClass.SendReliableMessageAsync(...)` now gives product code a reusable reliable push entry point.
- `Line.Messaging` remains responsible for the official `X-Line-Retry-Key` header.
- ChurchReport payment notifications can use deterministic retry keys without making payment core depend on ChurchReport.

### Identity/profile adapter

- `LineMessagingProcessorClass.GetUserProfileAsync(string UserId)` delegates to `LineMessagingClient.GetUserProfileAsync(...)`.
- Blank `UserId` is rejected before HTTP.
- The legacy `GetUserProfile(string UserId)` entry remains available and maps SDK profile data into the old processor-local `UserProfile` type.

### Group/room member profile adapter

- `LineMessagingProcessorClass.GetGroupMemberProfileAsync(string groupId, string userId)` delegates to the SDK group member profile endpoint.
- `LineMessagingProcessorClass.GetRoomMemberProfileAsync(string roomId, string userId)` delegates to the SDK room member profile endpoint.
- Blank `groupId`, `roomId`, or `userId` is rejected before HTTP.

## Actual Usage Surface Found

### Reusable processor still has one important legacy RestSharp path

`LineMessagingProcessor/LineMessagingProcessorClass.cs` still contains:

- `_restClient`
- `RestClientOptions("https://api.line.me/v2/bot")`
- `SendMessage(string UserId, string Message)` using `RestRequest("message/push")`

This is now the clearest remaining P1 cleanup candidate inside the reusable processor. The profile lookups have already moved to SDK-backed paths, but ordinary non-retry text push still uses RestSharp in this reusable module.

### ChurchReport still has product-layer LINE utilities

ChurchReport still directly constructs or uses `LineMessagingClient` in product utilities such as:

- `ChurchReport/Tools/PushUtility.cs`
- `ChurchReport/Tools/ReplyUtility.cs`
- `ChurchReport/Tools/LineUtilityClass.cs`
- `ChurchReport/Tools/DonationFeePaymentProcessor.cs`
- `ChurchReport/Tools/RecurringDonationPaymentProcessor.cs`
- `ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.Core.cs`
- `ChurchReport/Services/PaymentNotificationService.cs`
- `ChurchReport/Controllers/MemberInfoController.cs`

These should not all be moved now. Many are ChurchReport product workflows, UI-specific helpers, or legacy utility surfaces. They should be handled only through small slices with tests.

### ReplyUtility already uses group/room profile SDK calls

`ChurchReport/Tools/ReplyUtility.cs` directly calls:

- `m_LineMessagingClient.GetGroupMemberProfileAsync(ev.Source.Id, ev.Source.UserId)`
- `m_LineMessagingClient.GetRoomMemberProfileAsync(ev.Source.Id, ev.Source.UserId)`

This is a future migration candidate, but not the first one. It is product-layer webhook reply behavior, so it should be migrated only after a tested adapter usage pattern exists and only if the change remains small.

### PushUtility swallows many LINE push failures

`ChurchReport/Tools/PushUtility.cs` has many methods that catch exceptions, build an `ErrorString`, and do not rethrow. This behavior may be intentional for optional notifications, but it is risky for required delivery paths. A safer future slice is to add explicit reliable/throwing paths for required notifications, not to globally change every method.

### LIFF usage remains product-specific

Multiple `.cshtml` and JavaScript files still use:

- `liff.init`
- `liff.login`
- `liff.permission`
- `liff.getProfile`
- posted `UserLineId`, `GroupId`, `RoomId`, and `ViewType`

This is browser/product flow logic. It should not be moved into `Line.Messaging` or `LineMessagingProcessor`. If it is refactored later, it should become a ChurchReport frontend or product helper slice, not reusable backend SDK work.

## Recommended Next Slice

### Recommendation: SDK-backed non-retry text push in LineMessagingProcessor

The next implementation slice should replace the reusable processor's legacy RestSharp `SendMessage(string UserId, string Message)` path with SDK-backed logic.

Why this is the best next slice:

- It removes the last obvious RestSharp protocol call from the reusable processor.
- It matches the already completed reliable push adapter pattern.
- It keeps scope small and testable.
- It does not require modifying ChurchReport controllers, LIFF views, CRM binding, or payment workflows.
- It improves maintainability without forcing a broad P2 API framework.

Expected behavior:

- `SendMessage(string UserId, string Message)` validates blank `UserId` and blank `Message` before HTTP.
- Normal messages delegate to `_lineMessagingClient.PushMessageAsync(UserId, new List<ISendMessage> { new TextMessage(Message) })`.
- The existing special-case legacy message behavior must be inspected and either preserved with a regression test or intentionally removed only with explicit approval.
- `_restClient` should be removed from `LineMessagingProcessorClass` only if no remaining method needs it after the slice.

## Deferred Candidates

### ReplyUtility group/room profile migration

After the processor text push path is cleaned, migrate `ReplyUtility` group/room profile lookups to the processor adapter only if it does not complicate construction or introduce product coupling.

### PushUtility reliable required delivery paths

Add explicit throwing/reliable methods for required user-facing notifications, but do not globally change optional notification behavior without a targeted test plan.

### LIFF browser helper consolidation

Treat as a ChurchReport frontend/product slice. Do not place LIFF browser behavior inside reusable backend LINE modules.

### P2 official API expansion

Do not start broad P2 coverage until a concrete product use case requires it. Rich menu, audience, statistics, coupon, membership, and webhook management should each be separate slices if needed.

## Boundary Rules For Next Work

- `Line.Messaging` owns official LINE endpoint paths, HTTP headers, JSON parsing, and SDK model types.
- `LineMessagingProcessor` owns reusable convenience methods and input validation.
- ChurchReport owns CRM, controller, route, UI, LIFF, payment, and member binding decisions.
- No hidden global state.
- No large abstractions without a second product proving the need.
- Prefer one small tested adapter over a broad speculative framework.

## Proposed Next Implementation Plan Name

If approved, the next plan should be:

```text
docs/superpowers/plans/2026-07-02-line-processor-sdk-backed-send-message.md
```

The implementation should be TDD-first:

1. Add failing tests for `SendMessage` normal text push through SDK.
2. Add validation tests for blank `UserId` and `Message`.
3. Decide and test the current special-case legacy message behavior.
4. Replace RestSharp send logic with SDK delegation.
5. Remove `_restClient` only if no remaining code uses it.
6. Run processor tests, SDK tests, solution build, boundary scan, encoding check, output cleanup, and review.
