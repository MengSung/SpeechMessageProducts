# F04 Performance Analysis

Status: COMPLETE
Mode: STATIC_READ_ONLY

## Confirmed: Nominal Stream Downloads Use Buffered Completion

The large-content path is documented as `GetContentStreamAsync`, but it calls
`HttpClient.GetAsync(url)` without `HttpCompletionOption.ResponseHeadersRead`
(`LineMessagingClient.cs:967-971`). The same pattern is used for content preview
and RichMenu image download (`:1107-1111`, `:2120-2124`).

The default completion option is response-content read. Therefore the async
operation completes only after content has been buffered, and
`ReadAsStreamAsync` does not provide true progressive network streaming.

Consequences:

- peak memory grows with media size;
- first-byte processing waits for the body;
- the advertised large-file alternative does not avoid buffering.

`GetContentBytesAsync` intentionally buffers and is not itself defective
(`LineMessagingClient.cs:1000-1004`).

## Confirmed: Request/Response Lifetime Is Not Explicit

Static counts across F04 production source:

- 48 awaited response creations;
- 29 explicit `HttpRequestMessage` constructions;
- 0 response/request disposal scopes;
- 0 `CancellationToken` references.

Representative paths:

- JSON send: `LineMessagingClient.cs:432-438`
- content stream: `LineMessagingClient.cs:967-971`
- upload: `LineMessagingClient.cs:2131-2148`
- token issue/revoke: `LineMessagingClient.cs:250-310`
- LIFF add/update/delete: `LiffClient.cs:76-125`

Buffered content often lets `HttpClient` return the connection after reading,
so the static evidence does not prove immediate socket exhaustion. It does prove
that disposable object ownership is absent and that streaming responses cannot
dispose the response through `ContentStream`.

`ContentStream.Dispose` disposes only `_baseStream`
(`ContentStream.cs:155-162`). It receives copied content headers, not the
`HttpResponseMessage` (`:62-65`).

## Confirmed: Cancellation Cannot Reach I/O

The concrete client exposes 99 distinct async method names and the interface 94,
but F04 production source has zero `CancellationToken` references.

Affected work includes:

- JSON serialization and all HTTP sends;
- media download/upload;
- webhook body reads and parsing;
- LIFF operations;
- token issue/revoke;
- profile, insight, coupon, membership, and RichMenu endpoints.

Host request cancellation, shutdown, and workflow timeout cannot stop F04 work
before the configured `HttpClient.Timeout` or remote completion.

## Serialization Cost And Cohesion

- Every outbound JSON request creates a new anonymous object/string and
  `StringContent`; serializer settings are cached per client.
- Error paths buffer the complete response body as a string before parsing.
- Webhook processing allocates body string + UTF-8 byte array + dynamic JSON
  object graph.
- `GetRichMenuListAsync` parses to `JObject`, then serializes each item back to
  text before deserializing typed models
  (`LineMessagingClient.cs:2155-2171`), adding avoidable per-item work.
- `CustomStringEnumConverter` uses `Any` followed by `First` for read lookup
  (`CustomStringEnumConverter.cs:45-47`). Its current mapping is tiny, so this
  was not promoted.

## Guards And Counter-Evidence

- The obsolete token-only constructors are marked as socket-exhaustion risks,
  while injected-client constructors avoid disposing externally owned clients
  (`LineMessagingClient.cs:101-132`, `LiffClient.cs:34-62`).
- Current F05B uses `IHttpClientFactory`, providing handler pooling and a new
  client instance per transient SDK client.
- `ContentStream` correctly disposes its base stream when callers use `using`.
- `GetContentBytesAsync` is explicitly documented for smaller content.

These guards do not provide true streaming, response ownership, or
cancellation.

## Runtime Hypotheses

1. Peak allocations scale near linearly with media size on the stream path.
2. First-byte latency equals most or all body-transfer time.
3. Repeated high-rate calls increase disposable wrapper retention and GC work.
4. Caller abandonment leaves HTTP work active until timeout.
5. RichMenu list double conversion becomes measurable for large catalogs.

No benchmark, test, build, or network call was run.
