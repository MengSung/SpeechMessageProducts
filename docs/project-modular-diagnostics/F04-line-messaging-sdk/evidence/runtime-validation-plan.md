# F04 Runtime Validation Plan

Status: DEFERRED_UNTIL_OPTIMIZATION_APPROVAL
Mode: DIAGNOSIS_ONLY

No restore, build, test, package, generation, formatting, migration, benchmark,
coverage, or external LINE call was run.

## Gate Prerequisites

1. F01A decides that `Line.Messaging.csproj` is canonical and retires/archives
   `Line.Messaging_Net10.csproj`.
2. F01D/F05A moves `LineMessagingProcessorCredentialTests` and removes the F05A
   project reference from the F04 test project.
3. Tests use capturing/slow handlers and synthetic secrets only.
4. Provider fixtures are pinned to the official protocol version being
   supported.
5. Consumer gates are available for F03B, F05A-F07, F05B, and the host.

## Security Contract Tests

1. Two SDK clients sharing one `HttpClient` issue concurrent requests with
   distinct bearer tokens and no cross-request leakage.
2. An unrelated request on that client inherits no LINE token.
3. Null/blank token fails before transport.
4. Missing, malformed, and mismatched webhook signatures fail closed.
5. Oversized webhook body is rejected before full allocation/HMAC/JSON parsing.
6. Webhook read cancellation is observed.
7. JSON depth/event-count limits reject pathological payloads.
8. Error/log projections redact configured sensitive fields and cap body size.

## Retry And Error Matrix

For push, multicast, narrowcast, and broadcast:

- valid UUID retry key -> correct header;
- invalid UUID -> local validation failure;
- 200/202 -> success with request ID;
- 409 -> accepted duplicate with `X-Line-Accepted-Request-Id`;
- 400/401/403/404 -> typed permanent/auth/provider error;
- 429 -> typed throttle metadata;
- 500/502/503/504 -> typed transient classification;
- timeout after request transmission -> ambiguous result retaining retry key.

LIFF add/update/get/delete must use the same error matrix; delete 4xx/5xx must
not complete successfully.

Content-transcoding preparation must include an HTTP 200 fixture whose body is
malformed JSON. `VerifyContentPreparationAsync` must not classify that response
as ready after deserialization fails.

## Webhook Fixture Tests

1. First delivery and redelivery preserve `webhookEventId`,
   `deliveryContext.isRedelivery`, and mode.
2. Message events expose `markAsReadToken` and quote token when present.
3. Unknown future event/message types are preserved, not silently discarded.
4. Member join/leave, postback, beacon, things/device, file, media, and location
   payloads tolerate optional fields.
5. Malformed shape returns a typed parse error with event index.

## Streaming And Disposal Tests

Use a slow loopback/capturing handler:

1. `GetContentStreamAsync` returns after headers and before the full body.
2. First byte can be consumed while the server is still producing content.
3. Peak allocation remains bounded for 1 MB, 10 MB, and 100 MB bodies.
4. Disposing `ContentStream` disposes response/content/network stream exactly
   once.
5. Buffered byte-array path remains intentional and documented.
6. Request/response disposal is observed on success and error.
7. Upload cancellation stops reading the source stream; caller stream ownership
   matches the documented contract.

## Cancellation Tests

Representative methods:

- push JSON request;
- profile GET;
- media stream download;
- RichMenu upload;
- LIFF operation;
- webhook body read.

Each must propagate a supplied token and finish promptly after cancellation
without converting cancellation into `LineResponseException`.

## API/Model Parity Tests

- every public interface method has a non-placeholder implementation;
- unsupported APIs are absent or explicitly capability-gated;
- `GetFollowersAsync` cannot return synthetic success;
- message/model JSON fixtures cover nulls, lengths, enums, URLs, extension data,
  and round trip;
- endpoint inventory proves canonical URI/host and no duplicate `/v2`.

## Performance Measurements

Capture:

- time to headers and first byte;
- total latency;
- peak allocated bytes and GC collections;
- active requests after caller cancellation;
- response/request disposal counts;
- serialization allocation for representative message/Flex/RichMenu payloads;
- webhook allocation by body/event count.

## Consumer Gates

After F04 provider tests:

1. F03B adapter compile/tests and retry/error mapping.
2. F05A processor tests.
3. F06 notification/reply workflow tests.
4. F07 RichMenu tests.
5. F05B DI/service-resolution tests.
6. Host compile and relevant B07 integration tests.

## Rollback Boundaries

1. Add transport/result/cancellation overloads beside legacy methods.
2. Switch request-scoped authorization internally without constructor changes.
3. Extend `ContentStream` ownership without changing its public return type.
4. Add webhook metadata as optional immutable properties.
5. Move one consumer module at a time.
6. Revert by consumer/module; never restore duplicate project ownership.

## Pending Runtime Hypotheses

- actual media allocation/first-byte improvement;
- current host webhook request-size enforcement;
- frequency of accepted duplicate and throttle responses;
- multi-channel shared-client usage;
- external `_Net10.csproj` users;
- magnitude of exception/log data retention.
