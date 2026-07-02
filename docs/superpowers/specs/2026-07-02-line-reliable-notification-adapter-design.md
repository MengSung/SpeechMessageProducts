# LINE Reliable Notification Adapter P1 Continuation Design

Date: 2026-07-02
Branch: Jesus_5.1.6.WorktreeRefactorLine
Status: Draft design for user-approved A+B direction

## Goal

Continue the completed LINE SDK retry-key P1 slice by adding a small reusable reliable-notification adapter surface and wiring one ChurchReport payment-notification path through it.

The purpose is not to expand the whole LINE official API surface. The purpose is to make important payment-related LINE notifications reusable and safer for ChurchReport and future ASP.NET Core products.

## Approved Direction

Use **A+B**:

- **A: Processor reusable adapter** - add a product-neutral method in `LineMessagingProcessor` that accepts a caller-provided retry key and delegates to the `Line.Messaging` SDK retry-key overload.
- **B: One ChurchReport vertical slice** - connect the ChurchReport payment notification flow to that adapter only where the notification is important enough to need idempotent retry behavior.

## Current Evidence

The current code has these relevant notification paths:

- `Line.Messaging` already owns the protocol-level `X-Line-Retry-Key` header.
- `LineMessagingProcessor/LineMessagingProcessorClass.cs` currently sends push messages with RestSharp and does not expose retry-key behavior.
- `ChurchReport/Services/PaymentNotificationService.cs` sends payment LINE notifications through `PushUtility.SendMessage(...)`.
- `ChurchReport/Tools/DonationFeePaymentProcessor.cs` still contains older direct `m_PushUtility.SendMessage(...)` payment-result notification paths.
- `ChurchReport/Services/DonationBookingService.cs` also sends LINE messages, but booking notification is not the first reliable-payment slice.

## Architecture

Keep the layers strict:

```text
ChurchReport payment workflow
    owns business event identity and retry-key value
        |
        v
LineMessagingProcessor reliable adapter
    owns reusable notification convenience method
    does not know CRM, fee type, donation category, or payment provider
        |
        v
Line.Messaging SDK
    owns LINE endpoint, JSON payload, and X-Line-Retry-Key header
        |
        v
LINE Messaging API
```

## Component Responsibilities

### Line.Messaging

Already completed in P1:

- Builds the official LINE HTTP request.
- Applies `X-Line-Retry-Key` on the per-request `HttpRequestMessage`.
- Preserves old overload behavior when retry key is null, empty, or whitespace.

No new protocol behavior should be added in this slice unless tests prove a missing SDK boundary.

### LineMessagingProcessor

Add a thin reliable-notification adapter:

- Accepts `userId`, `message`, and `retryKey`.
- Validates only basic adapter-level requirements such as non-empty user ID and message.
- Calls `LineMessagingClient.PushMessageAsync(userId, messages, retryKey)`.
- Does not construct `X-Line-Retry-Key` directly.
- Does not contain ChurchReport-specific identifiers such as fee IDs, donation categories, CRM fields, or payment provider names.

This keeps the processor reusable by future ASP.NET Core products.

### ChurchReport Payment Notification

For the first vertical slice, ChurchReport should generate retry keys only from business identifiers it already owns.

Recommended initial key shape:

```text
churchreport:payment:{orderId}:{status}:payer-line-notice
```

Fallback shape if `orderId` is unavailable:

```text
churchreport:payment:{productOrderId}:{status}:payer-line-notice
```

Rules:

- Retry key generation stays in ChurchReport because it is product/business context.
- The retry key should be deterministic for the same payment event.
- The retry key should not include secrets, card tokens, raw payer personal data, or full message text.
- If a required ID is missing, fall back to the existing non-retry send path rather than inventing a random key.

## Initial Scope

Included:

1. Add one reliable push method to `LineMessagingProcessorClass`.
2. Add tests around the processor adapter if the project test structure allows isolated request capture.
3. Update `PaymentNotificationService.SendLineMessage(...)` or an adjacent payment-specific method to pass a deterministic retry key.
4. Preserve old `SendLineMessage(lineId, message)` behavior for compatibility.
5. Keep all CRM lookup, fee-type mapping, and payment-message composition in ChurchReport.

Excluded:

- Do not implement P2 official API expansion.
- Do not add Audience, Narrowcast, quote token, sender, or mention APIs.
- Do not touch LINE login, LIFF, webhook handling, or general reply-message flows.
- Do not move ChurchReport payment business logic into `Line.Messaging` or `LineMessagingProcessor`.
- Do not refactor all existing `PushUtility.SendMessage(...)` call sites in this slice.
- Do not change payment provider callback behavior except the single selected LINE notification send path.

## Data Flow

Payment success/failure callback flow:

1. Payment provider callback is normalized into the existing payment workflow result.
2. ChurchReport resolves payer contact and `new_lineid`.
3. ChurchReport builds the user-visible payment LINE message.
4. ChurchReport builds a deterministic retry key from payment event identity.
5. ChurchReport calls the reliable notification adapter.
6. `LineMessagingProcessor` delegates to `Line.Messaging`.
7. `Line.Messaging` applies `X-Line-Retry-Key`.
8. LINE API receives the idempotent push request.

## Error Handling

- Existing payment notification failure strategy should remain intact.
- LINE send failure should be logged with context, but should not cause a successful payment callback to be treated as unpaid.
- Missing LINE ID should continue to skip notification.
- Missing deterministic payment identifier should fall back to existing non-retry send behavior.
- Empty retry key must not send the retry header.

## Testing Strategy

Minimum test expectations:

- Processor reliable send passes retry key to the SDK overload.
- Existing processor `SendMessage(...)` path remains compatible.
- Payment notification retry-key builder returns the same key for the same payment event.
- Payment notification retry-key builder does not include card token, payer name, raw LINE ID, or full message content.
- Existing `Line.Messaging.Tests` still pass.
- Solution build still passes.

## Linus-Style Maintenance Rules

- One layer owns one concern.
- SDK owns LINE protocol.
- Processor owns reusable messaging convenience.
- ChurchReport owns business event identity and retry-key semantics.
- Avoid broad sweeping call-site migration.
- Prefer small overloads and deterministic data flow over hidden global state.
- Tests should prove the boundary instead of testing private implementation details.

## Implementation Plan Hand-Off

The next step should be a focused implementation plan, not direct coding. The plan should name exact files and tests before edits.

Recommended first implementation files:

- `LineMessagingProcessor/LineMessagingProcessorClass.cs`
- `ChurchReport/Services/PaymentNotificationService.cs`
- A focused test file under the most appropriate existing test project, or a new small test project only if no suitable test host exists.

The plan should explicitly decide whether `DonationFeePaymentProcessor` direct `m_PushUtility.SendMessage(...)` paths are in this slice. Default recommendation: do not modify them until `PaymentNotificationService` reliable adapter is proven.
