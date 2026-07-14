# F04 Extraction Analysis

Status: COMPLETE
Mode: STATIC_READ_ONLY

## Current Boundary

F04 is physically isolated but internally combines several independently
testable contracts:

1. credential and HTTP request construction;
2. JSON serialization and model validation;
3. provider error/retry classification;
4. binary response streaming;
5. webhook authentication/parsing;
6. LIFF transport;
7. endpoint-specific models for messaging, RichMenu, insights, audiences,
   coupons, membership, and profiles.

The central concrete client is 2,832 lines and the interface is 921 lines.
Twelve audience methods are interface members but always throw
`NotImplementedException` (`LineMessagingClient.cs:2675-2776`), while
`GetFollowersAsync` returns an empty list without provider I/O (`:2785-2789`).
The abstraction therefore combines stable operations, placeholders, and a
synthetic extension under one capability surface.

## Retry And Error Contract

Retry support is endpoint-specific overload plumbing rather than a reusable
transport/result module:

- push, multicast, broadcast accept unvalidated string keys;
- narrowcast has no key overload;
- 409 accepted duplicate, 429 throttling, and response correlation are not
  modeled;
- `EnsureSuccessStatusCodeAsync` discards success/error headers;
- `GetStringAsync` changes HTTP 401 to `UnauthorizedAccessException`;
- LIFF delete does not validate status.

The clean boundary should return typed provider outcomes, while F06/B07 decide
business continuation or alerting.

## Webhook Contract

Signature verification, JSON parsing, event projection, and application
dispatch are separate responsibilities but currently coupled through dynamic
objects. The typed event model drops provider-level identity/redelivery/mode and
mark-as-read data, so downstream modules must accept information loss or reparse
raw JSON.

A reusable webhook module should own:

- bounded raw-body capture;
- HMAC verification;
- version-tolerant envelope parsing;
- unknown-event preservation;
- immutable provider event identity/redelivery metadata;
- typed event/message payloads.

It must not own recipient/business decisions.

## Recommended Reusable Modules

### 1. Transport

Input:

- method/path;
- request body/content;
- channel credential;
- retry UUID;
- cancellation;
- completion mode.

Output:

- typed status/result;
- LINE request ID;
- accepted request ID;
- retry/throttle metadata;
- response-owned stream where applicable.

### 2. Serialization And Models

- one shared serializer configuration;
- typed request DTOs instead of string interpolation;
- validation at model/request boundaries;
- version-tolerant response DTOs with extension data;
- capability-specific interfaces instead of one all-endpoint interface.

### 3. Error And Retry

- sanitized provider error DTO;
- consistent status classification;
- accepted duplicate as a distinct outcome;
- no automatic retry until caller supplies idempotency/replay policy.

### 4. Webhooks

- bounded verified envelope;
- provider identity/redelivery/read token;
- unknown-event support;
- fixture-driven parsing.

### 5. LIFF

LIFF may remain a separate endpoint client but should reuse transport,
credential, error, disposal, and cancellation infrastructure.

## Project And Test Boundary

`Line.Messaging.csproj` and `Line.Messaging_Net10.csproj` define the same net10
project. Only the canonical file is referenced; historical scripts mention the
duplicate. F01A should record the lifecycle decision.

`Line.Messaging.Tests` references F05A only for
`LineMessagingProcessorCredentialTests`. Test ownership follows the directly
tested subject, so that file and dependency belong to F05A. A clean F04 provider
gate must not require a downstream processor.

## Consumer Compatibility

Read-only consumer inspection found:

- F05B already uses `IHttpClientFactory`;
- F03B/F05A/F06/F07 consume concrete/interface SDK methods and
  `LineResponseException`;
- some legacy ChurchReport consumers still use obsolete token-only
  constructors.

Recommended migration:

1. add cancellation and typed-result overloads;
2. keep legacy methods as adapters;
3. move consumers module by module;
4. preserve message/model JSON shape;
5. validate provider gate, then every mapped consumer gate.

## Counter-Evidence And Rejected Extraction Claims

- The SDK already has `ILineMessagingClient`; the issue is its breadth and false
  capability surface, not total absence of an interface.
- Serializer settings are cached per client, so there is no per-call settings
  construction.
- Current F05B client creation is compatible with request-scoped auth migration.
- Product recipient policy and notification results must remain in F06/B07, not
  be pulled into F04.
- RichMenu business orchestration remains F07 even though F04 owns its protocol
  DTOs/endpoints.
