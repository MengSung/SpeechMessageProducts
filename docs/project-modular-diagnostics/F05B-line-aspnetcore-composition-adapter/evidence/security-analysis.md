# F05B Security Analysis

Status: COMPLETE
Mode: STATIC_READ_ONLY

## Confirmed: Credential Destination And Presence Are Not Validated

F05B exposes two mutable values:

- an empty-by-default channel token
  (`LineMessagingProcessorOptions.cs:19-23`);
- a configurable API base URI
  (`LineMessagingProcessorOptions.cs:23`).

`AddLineMessagingProcessor` registers the action with `Configure`, then reads
`IOptions<LineMessagingProcessorOptions>.Value` in a transient factory and
passes both values directly to F04
(`LineMessagingProcessorServiceCollectionExtensions.cs:53-60`).

There is no:

- nonblank-token validation;
- absolute-URI validation;
- HTTPS requirement;
- approved-host policy;
- explicit custom-endpoint opt-in;
- `ValidateOnStart`.

F04 then:

1. places the supplied token in
   `HttpClient.DefaultRequestHeaders.Authorization`
   (`Line.Messaging/LineMessagingClient.cs:107-112`);
2. normalizes the endpoint only by trimming `/` and appending `/v2`
   (`LineMessagingClient.cs:134-145`);
3. sends absolute requests to that endpoint
   (`LineMessagingClient.cs:432-437`).

Confirmed flow:

```text
configuration token + endpoint
  -> F05B IOptions.Value
  -> transient client factory
  -> Authorization default header
  -> absolute request to configured endpoint
```

ChurchReport reads a configured organization token and explicitly falls back
to `string.Empty` (`SpeechMessageProducts.ChurchReport/Startup.cs:503-510`).

Security impact:

- a valid bearer token can be sent to a mistyped, compromised, or non-LINE
  endpoint;
- an HTTP endpoint can expose the token in transit;
- missing configuration is not rejected at startup.

This is retained as F05B-SEC-001. It is not described as user-controlled SSRF:
the value comes from trusted host configuration, not an HTTP request. The risk
is a missing composition security boundary around a bearer credential.

## DI Lifetime And Shared-State Review

F05B registers:

- concrete client: transient;
- concrete processor: transient;
- F06 workflows: transient;
- F07 processor/workflows: transient;
- RichMenu ID cache: singleton;
- RichMenu state store: singleton.

No active processor state leak was confirmed:

- every F05A processor resolution creates a new transient instance
  (`LineMessagingProcessorServiceCollectionExtensions.cs:62-65`);
- no F05B singleton directly captures the processor;
- the RichMenu ID cache uses a lock
  (`InMemoryLineRichMenuIdCache.cs:25-31,43-52`);
- the RichMenu state store uses `ConcurrentDictionary` keyed by normalized LINE
  user ID (`InMemoryRichMenuStateStore.cs:23-35`).

The singleton state stores can retain data process-wide, but eviction,
expiration, and multi-organization semantics belong to F07. F05B owns only
their default lifetime registration. Without a confirmed collision or unsafe
access path, no F05B security issue is promoted.

## Service Override Boundary

The core transport and workflow registrations use unconditional
`AddTransient`, while RichMenu registrations use `TryAdd*`
(`LineMessagingProcessorServiceCollectionExtensions.cs:55-68,100-111`).

Consequences:

- a custom service registered before the extension is followed by the F05B
  concrete service and is not the direct-resolution winner;
- consumers must register after F05B or use `RemoveAll`;
- repeated calls append duplicate descriptors.

This is retained in F05B-EXT-001 rather than as a security issue. It can become
security-relevant if a host expects a hardened custom client but registration
order silently selects the default client.

## Guards And Counter-Evidence

- No literal production channel token exists in the owned source.
- F05B does not log the token.
- No webhook route, signature validation, controller, request body, or
  authorization policy is owned by F05B.
- No singleton directly holds request, session, claims, tenant, or
  `HttpContext`.
- `IHttpClientFactory` is used, so the DI path does not create an internally
  owned raw handler per request.
- The default endpoint is HTTPS and points to LINE.

## Hypotheses Requiring Runtime Or Product Evidence

1. Whether deployed configuration ever overrides `ApiBaseUri`.
2. Whether credentials rotate while the host remains running.
3. Whether a startup health check currently resolves a LINE capability.
4. Whether external consumers intentionally use HTTP/custom emulator endpoints.
5. Whether multiple LINE organizations must share one host process.
