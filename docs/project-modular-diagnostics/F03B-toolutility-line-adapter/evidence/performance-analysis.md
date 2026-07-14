# F03B Performance Analysis

Status: COMPLETE
Mode: STATIC_READ_ONLY

## Confirmed: Multicast Audit Performs Serial Per-Recipient CRM I/O

`MultiCastTextMessageAsync` audits the entire recipient list before issuing one
LINE multicast request (`ToolUtility/PushUtility.cs:82`,
`ToolUtility/PushUtility.cs:89`).

The audit method loops sequentially (`ToolUtility/ToolUtilityPartials/ToolUtilityClass.Line.cs:72`).
For every recipient it performs:

1. one contact lookup (`ToolUtility/ToolUtilityPartials/ToolUtilityClass.Line.cs:74`);
2. one CRM letter creation when a contact is found
   (`ToolUtility/ToolUtilityPartials/ToolUtilityClass.Line.cs:81`,
   `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Line.cs:99`).

Therefore an N-recipient multicast performs up to `2N` synchronous CRM network
operations before the single LINE network operation. A slow or failing CRM
prevents the LINE request from starting, even though transport could otherwise
send the recipient list in one serialized request.

## Confirmed: Concrete Client Ownership Permits Repeated Internally-Owned HttpClient

F03B accepts only the concrete `LineMessagingClient`
(`ToolUtility/PushUtility.cs:29`, `ToolUtility/PushUtility.cs:34`) and provides
no ownership marker or disposal behavior.

F04 documents the token-only constructor as backward-compatible but not
recommended for production because it creates an internal `HttpClient`
(`Line.Messaging/LineMessagingClient.cs:118`,
`Line.Messaging/LineMessagingClient.cs:123`,
`Line.Messaging/LineMessagingClient.cs:126`). Disposal occurs only if the owner
calls `LineMessagingClient.Dispose`
(`Line.Messaging/LineMessagingClient.cs:2823`,
`Line.Messaging/LineMessagingClient.cs:2825`,
`Line.Messaging/LineMessagingClient.cs:2827`).

The sole explicit F03B consumer constructs that token-only client and stores it,
but is not disposable and exposes no disposal path
(`SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs:38`,
`SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs:45`,
`SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs:61`,
`SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs:65`,
`SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs:67`).

This confirms unmanaged client lifetime at the current boundary. Actual socket
exhaustion rate is a runtime hypothesis, not a static conclusion.

## Serialization And Network Call Review

- F04 serializes typed push once per push request
  (`Line.Messaging/LineMessagingClient.cs:561`,
  `Line.Messaging/LineMessagingClient.cs:563`).
- F04 serializes typed multicast once per multicast request
  (`Line.Messaging/LineMessagingClient.cs:659`,
  `Line.Messaging/LineMessagingClient.cs:661`).
- F03B does not perform duplicate JSON serialization itself.
- The material repeated work in F03B is CRM lookup/create, object construction,
  and separate RichMenu network steps, not duplicate JSON encoding.

## Legacy RichMenu Lifecycle

`AddRichMenuMessage` always creates a provider resource, then reads a fixed
local file, uploads, links, and sends a notification
(`ToolUtility/PushUtility.cs:321`, `ToolUtility/PushUtility.cs:327`,
`ToolUtility/PushUtility.cs:329`, `ToolUtility/PushUtility.cs:336`,
`ToolUtility/PushUtility.cs:338`, `ToolUtility/PushUtility.cs:348`).

If file read/upload/link fails, there is no compensating delete for the newly
created resource. The method is currently unreferenced in F03B consumer search,
so provider orphan count is not promoted as a current performance issue.

## Guards And Counter-Evidence

- Empty multicast lists skip both CRM and LINE work
  (`ToolUtility/PushUtility.cs:80`).
- Missing CRM contacts skip record creation, but the lookup cost is still paid
  (`ToolUtility/ToolUtilityPartials/ToolUtilityClass.Line.cs:74`,
  `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Line.cs:79`).
- `ToolUtilityFactory` returns one `ToolUtilityClass` singleton after
  initialization, so F03B does not create a fresh CRM client for every message
  (`ToolUtility/Factory/ToolUtilityFactory.cs:83`,
  `ToolUtility/Factory/ToolUtilityFactory.cs:89`,
  `ToolUtility/Factory/ToolUtilityFactory.cs:94`).
- The F04 injected-`HttpClient` constructor correctly leaves disposal to the
  external owner (`Line.Messaging/LineMessagingClient.cs:107`,
  `Line.Messaging/LineMessagingClient.cs:110`). F03B simply does not expose an
  interface/factory contract that makes this preferred path easy to enforce.

## Runtime Hypotheses

1. Multicast latency grows linearly with recipient count and CRM latency.
2. Fire-and-forget calls can overlap and increase concurrent CRM/HTTP work.
3. Repeated `LineNotifyUtility` construction can accumulate live sockets until
   finalization/connection expiry.

The runtime validation plan defines counters for these hypotheses; no benchmark,
build, or test was run during diagnosis.
