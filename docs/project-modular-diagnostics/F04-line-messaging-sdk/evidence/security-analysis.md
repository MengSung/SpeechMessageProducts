# F04 Security Analysis

Status: COMPLETE
Mode: STATIC_READ_ONLY

## Confirmed: Caller-Owned HttpClient Receives Global Bearer State

`LineMessagingClient(HttpClient, token, uri)` and
`LiffClient(HttpClient, token, requestUri)` both:

1. retain the externally supplied client;
2. state that F04 will not dispose it;
3. write the token into `DefaultRequestHeaders.Authorization`.

Evidence:

- `Line.Messaging/LineMessagingClient.cs:107-115`
- `Line.Messaging/Liff/LiffClient.cs:40-47`

The state is mutable and belongs to the `HttpClient` instance, not one request.
Two SDK clients sharing the same instance can overwrite each other's token.
This is a confirmed credential-isolation defect in the public constructor
contract.

Counter-evidence:

- The current F05B composition calls `IHttpClientFactory.CreateClient` for each
  transient SDK client (`LineMessagingProcessorServiceCollectionExtensions.cs:54-60`).
  Each call returns a separate `HttpClient` instance, so current composition is
  less likely to collide even though handlers are pooled.
- Most repository tests also create one `HttpClient` per SDK client.

The guard reduces observed likelihood but does not repair the reusable SDK
contract.

## Confirmed: Webhook Verification Performs Unbounded Pre-Auth Work

`GetWebhookEventsAsync` reads the complete body as a string before checking the
signature (`WebhookRequestMessageHelper.cs:40-45`). `VerifySignature` then
allocates UTF-8 bytes for the complete string (`:75-80`), and successful
verification is followed by full dynamic JSON parsing (`:48-57`).

The helper has:

- no content-length/body-size limit;
- no `CancellationToken`;
- no JSON depth or event-count limit;
- a missing-signature path using `Headers.GetValues`, which can throw before
  the intended `InvalidSignatureException`.

The confirmed contract is the absence of bounds. Exploitability depends on host
request limits and is therefore part of runtime/host validation.

## Signature Algorithm Guards

The signature implementation itself was not retained as vulnerable:

- HMAC-SHA256 is used with the channel secret
  (`WebhookRequestMessageHelper.cs:78-82`);
- malformed base64 or crypto input is caught and returns false (`:73-88`);
- byte comparison includes length and all overlapping bytes (`:96-101`);
- invalid signatures throw before webhook parsing (`:42-48`).

Recommended modernization to `CryptographicOperations.FixedTimeEquals` avoids
maintaining custom crypto comparison, but there is no evidence of a bypass in
the current code.

## Input And Serialization Safety Review

- `WebhookEventParser` and event factories use dynamic access and silently skip
  unknown types (`WebhookEventParser.cs:21-31`,
  `WebhookEvent.cs:47-60`, `:113-115`). Malformed shapes may throw binder,
  cast, or JSON exceptions. This is captured primarily as a version-tolerant
  model/error contract issue rather than a separate exploit.
- Raw JSON send overloads interpolate caller strings directly
  (`LineMessagingClient.cs:499-505`, `:586-592`, `:678-685`). They are an unsafe
  API shape if callers pass untrusted values, but repository consumers were not
  found using these overloads, and their documentation explicitly grants full
  JSON control. Deprecation is recommended; a confirmed injection finding was
  rejected.
- Custom API base URI support can direct bearer requests to another host
  (`LineMessagingClient.cs:134-155`). Tests deliberately cover an internal
  gateway. Production allowlisting belongs to X04A; arbitrary user input was
  not found.
- `HttpResponseMessageExtensions` can retain raw provider response content in
  exception objects (`HttpResponseMessageExtensions.cs:36-45`). LINE error
  responses may echo invalid fields. Logging/redaction should be part of the
  common error contract, but no token logging was found in F04.

## Security Hypotheses

1. A host without request-size enforcement can be memory/CPU exhausted before
   webhook authentication.
2. External or future multi-channel consumers may reuse one `HttpClient` and
   observe cross-channel token state.
3. Production custom gateway configuration may need certificate/host
   allowlisting.
4. Consumer logging of `LineResponseException.ToString()` may persist provider
   error details.

These require host/configuration/runtime evidence; no secret values were read
or recorded.
