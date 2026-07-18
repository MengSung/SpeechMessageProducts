# B05 Performance Analysis

Status: DRAFT
Nested agent count: 0

## Performance / Design Findings

### B05-PERF-001 Synchronous wait on LINE notification workflow in payment callback path

`PaymentNotificationService.SendLineMessage` invokes `_lineNotificationWorkflow.SendOrThrowAsync(request).GetAwaiter().GetResult()` (`PaymentNotificationService.cs:113-129`). Payment notification is called from the post-payment handler after CRM update and fee-type resolution (`ChurchReportPaymentPostPaymentHandlers.cs:84-123`) and from callback execution in `MyPayController.PaymentNotify` through `_postPaymentWorkflow.ExecuteAsync` (`MyPayController.cs:139-146`).

Performance impact: sync-over-async can block request threads during provider callbacks, increase tail latency, and make retry behavior harder to reason about. The callback path is latency-sensitive because it must acknowledge provider notifications reliably.

Recommended handling: make B05 notification handlers async end-to-end and pass cancellation tokens to the LINE workflow. If provider acknowledgement must not wait for user notification, enqueue notification with an idempotent outbox/retry contract.

### B05-PERF-002 Legacy donation processor creates CRM/LINE dependencies directly per processor instance

`DonationPaymentProcessor` builds configuration lazily, constructs LINE clients/utilities, and calls `ToolUtilityFactory.GetInstance("DYNAMICS365-9.0")` in constructors (`DonationPaymentProcessor.Core.cs:45-145`). This makes resource lifetime, pooling, and dependency reuse harder to control from DI.

Performance impact: direct factory construction can duplicate CRM/LINE clients and bypass host lifetime policies. It also makes hot donation/payment paths harder to test and optimize.

Recommended handling: continue extracting host-owned interfaces for CRM fee operations, donation order creation, and payment notification sending. Keep provider-specific behavior in F08/F09.

### B05-PERF-003 Payment notification timeout branches detach background continuations

Manual dedication and ATM notification flows use `Task.WhenAny` with a 500 ms display timeout and continue notification in the background (`DonationPaymentProcessor.FeeManagement.cs:292-360`, `DonationPaymentProcessor.PaymentProcessing.cs:328-405`). This improves UI responsiveness but creates detached completion paths with only trace logging on failure.

Performance/design impact: callback/user response latency improves, but delivery failure observability and retry semantics are weak. Detached continuations also complicate shutdown and request lifetime behavior.

Recommended handling: replace detached continuations with a bounded queue/outbox or explicit background service owned by X01/B07 transport contracts, while B05 owns notification payload and idempotency key.

### B05-PERF-004 CRM write path performs multiple sequential fee operations

Legacy fee creation calls `CreateEntity`, retrieves the entity, assigns owner, and emits performance trace messages (`DonationPaymentProcessor.FeeManagement.cs:77-112`). Payment result update also mutates multiple CRM fields and appends provider details (`PaymentCrmService.cs:39-82`).

Performance impact: sequential CRM round trips and broad entity updates can dominate donation checkout latency under load.

Recommended handling: measure CRM call counts and latency first. Future optimization should consolidate field updates where safe and preserve ownership/rollback boundaries.
# B05 Performance Analysis

Status: DEGRADED_REVIEW_PENDING
Nested agent count: 0

## Performance / Design Findings

### B05-PERF-001: Payment callback path synchronously blocks on async LINE notification

Evidence:
- `PaymentNotificationService.SendLineMessage` calls `_lineNotificationWorkflow.SendOrThrowAsync(request).GetAwaiter().GetResult()` (`PaymentNotificationService.cs:113-129`).
- B05 post-payment notification is reached from `ChurchReportPaymentPostPaymentHandlers.NotifyAsync` (`ChurchReportPaymentPostPaymentHandlers.cs:84-123`).
- `MyPayController.PaymentNotify` awaits `_postPaymentWorkflow.ExecuteAsync` before returning the provider acknowledgement (`MyPayController.cs:139-146`).

Impact:
- Callback request threads can be blocked by LINE transport latency or failure.
- Provider acknowledgement latency becomes coupled to post-payment user notification.
- Under provider retry pressure, blocked request threads can amplify duplicate callback traffic.

Recommended handling:
- Make B05 notification service and handler async end-to-end with cancellation tokens.
- If acknowledgement must be fast, persist a B05 notification decision/outbox item and acknowledge provider callback before transport delivery.

### B05-PERF-002: Legacy donation processor constructs CRM/LINE dependencies directly

Evidence:
- `DonationPaymentProcessor` keeps static/lazy configuration and has LINE client/utility fields (`DonationPaymentProcessor.Core.cs:45-81`).
- Constructors call `ToolUtilityFactory.GetInstance("DYNAMICS365-9.0")` and construct/use LINE utilities (`DonationPaymentProcessor.Core.cs:92-145`).

Impact:
- Resource lifetime, connection pooling, cancellation, and telemetry are hidden from host DI.
- This makes hot donation checkout paths harder to optimize and test.

Recommended handling:
- Extract B05 ports for CRM fee operations, payment order creation, and notification decision delivery.
- Keep F03A/B07/F09 implementations injected behind those ports.

### B05-PERF-003: Notification timeout handling uses detached continuations

Evidence:
- Manual dedication notification waits 500 ms, then attaches a background `ContinueWith` when LINE send is slower (`DonationPaymentProcessor.FeeManagement.cs:292-360`).
- ATM instruction notification uses the same `Task.WhenAny` / continuation pattern (`DonationPaymentProcessor.PaymentProcessing.cs:328-405`).

Impact:
- UI latency improves, but delivery completion is not durable.
- Shutdown, request cancellation, and failure observability are weak.
- Failures are trace-only and hard to aggregate.

Recommended handling:
- Replace detached continuations with a bounded background queue or outbox owned by a host/background service.
- Let B05 own notification payload/idempotency and B07/F06 own transport.

### B05-PERF-004: CRM fee writes are sequential and broad

Evidence:
- Fee creation performs create, retrieve, owner assignment, and trace timing in sequence (`DonationPaymentProcessor.FeeManagement.cs:77-112`).
- Payment result update mutates multiple fee fields and appends provider information to CRM text (`PaymentCrmService.cs:39-82`).
- ATM update appends new ATM order/payment number text and writes multiple CRM attributes (`DonationPaymentProcessor.FeeManagement.cs:153-199`).

Impact:
- Multiple CRM round trips can dominate checkout latency.
- Broad updates complicate idempotency and rollback.

Recommended handling:
- Measure CRM call count and latency before optimizing.
- Consolidate fee updates where safe, while preserving ownership and audit boundaries.

## Runtime Measurement Needed

- Callback p50/p95 duration with LINE workflow delayed and failed.
- CRM call count per donation checkout and per callback.
- Duplicate callback behavior for already-paid fee entities.
