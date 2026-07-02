# LINE Usage Surface Inventory

Date: 2026-07-02
Branch: Jesus_5.1.6.WorktreeRefactorLine

## Purpose

Inventory the actual ChurchReport and LineMessagingProcessor LINE usage before choosing the next SDK/refactor slice. This avoids expanding the SDK broadly before the product has a real use case.

## Actual Backend Messaging Surface

### Payment notifications

- `ChurchReport/Services/PaymentNotificationService.cs` now sends payment success/failure LINE notices through `SendLineMessage(lineId, message, retryKey)`.
- When a deterministic payment retry key exists, it uses `LineMessagingProcessorClass.SendReliableMessageAsync(...)`.
- When no retry key exists, it preserves the old `PushUtility.SendMessage(...)` path.

### General processor push flow

- `LineMessagingProcessor/LineMessagingProcessorClass.cs` still owns older generic push behavior through `SendMessage(userId, message)` using RestSharp.
- The new reliable method delegates to `Line.Messaging.LineMessagingClient.PushMessageAsync(..., retryKey)`.
- `LineMessagingProcessorClass.GetUserProfile(...)` still calls LINE `profile/{userId}` through RestSharp.

## Actual Identity / Binding Surface

- ChurchReport stores and reads LINE IDs through CRM fields such as `new_lineid`.
- Several product flows still use `LineIdLogin` and `RetrieveContactEntityByLineUserId(...)` for LINE-based login or lookup.
- QR code binding endpoints receive `UserLineId`, `GroupId`, `RoomId`, and `ViewType` from LIFF pages.

## Actual LIFF Surface

- QR code / group / Sunday views load `https://static.line-scdn.net/liff/edge/2/sdk.js`.
- These views use `liff.init`, `liff.login`, `liff.permission`, and `liff.getProfile`.
- The LIFF pages post identity data back to ChurchReport endpoints such as `QrCodeGetLineId`, `SmallGroupQrCodeGetLineId`, and `SundayQrCodeGetLineId`.

## SDK Capability Already Present

`Line.Messaging.LineMessagingClient` already contains many official Messaging API methods, including:

- Reply / push / multicast / broadcast / narrowcast.
- Retry-key overloads for push, multicast, and broadcast.
- Message content download / preview / preparation verification.
- User, group, and room profile/member APIs.
- Webhook endpoint management.
- Rich menu APIs.
- Delivery, quota, statistics, coupon, membership, audience, and follower APIs.

## Boundary Assessment

### Reusable SDK / Processor candidates

- Product-neutral push notification adapter already exists as the first reliable notification slice.
- The next reusable candidate is a small identity/profile adapter around `GetUserProfileAsync` and possibly group/room member profile lookups.
- Another candidate is LIFF/Login configuration helpers, but LIFF itself is mostly frontend/browser behavior and should not be forced into the Messaging SDK.

### ChurchReport-specific behavior that should stay in ChurchReport

- CRM contact lookup by `new_lineid`.
- Mapping LINE user IDs to members, groups, Sunday school, or appointment flows.
- UI routes and LIFF pages.
- Payment notification message text and payer context.

## Recommended Next Slice

Recommended next step: do a small P1 design for a `LineMessagingProcessor` identity/profile adapter, not broad P2 official API expansion.

Why:

- ChurchReport already uses LINE identity/profile data in multiple login and QR binding flows.
- The SDK already has `GetUserProfileAsync`, group member profile, and room member profile methods.
- A thin processor adapter can make future ASP.NET Core products reuse LINE identity/profile lookup without importing ChurchReport CRM concepts.
- This keeps data flow simple: product supplies LINE ID, processor fetches LINE profile, product decides how to bind it.

## Not Recommended Yet

- Do not start broad P2 official API coverage until a product use case requires it.
- Do not move CRM binding logic into `Line.Messaging` or `LineMessagingProcessor`.
- Do not refactor all LIFF views in the next slice; first isolate backend profile/binding boundaries.