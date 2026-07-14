# F05A Security Analysis

Status: COMPLETE
Mode: STATIC_READ_ONLY

## Confirmed: Legacy Helpers Disclose Full Exceptions To Recipients

`GetUserDisplayName` catches any exception, constructs a string with the
processor type, current time, and `e.ToString()`, and pushes it to the supplied
LINE user ID before rethrowing
(`LineMessagingProcessor/LineMessagingProcessorClass.cs:655-670`).

`NotifyLineBinding` calls that helper, then has a second catch with the same
exception-to-recipient behavior
(`LineMessagingProcessor/LineMessagingProcessorClass.cs:673-695`).

Confirmed flow:

```text
caller-selected LINE user ID
  -> profile/local failure
  -> Exception.ToString() + type + timestamp
  -> SendMessage(user ID, diagnostic string)
  -> exception rethrown
```

If `GetUserDisplayName` sends its error successfully and rethrows, the outer
`NotifyLineBinding` catch can send a second error message. If the inner error
send fails, the outer catch observes the send failure and can attempt another
diagnostic send.

Security impact:

- stack frames, namespaces, exception types, runtime/source details, and
  provider response messages can cross to an end user;
- user-facing messaging is coupled to internal diagnostics;
- there is no redaction, stable error code, or policy boundary.

Counter-evidence:

- repository search found no current caller of either legacy helper;
- no source evidence showed an Authorization header or channel token included
  in the exception text;
- current B07 has a separate binding service with stable product messaging
  (`SpeechMessageProducts.ChurchReport/Services/ChurchReportLineBindingNotificationService.cs:78-112`).

The absence of a current repository caller reduces likelihood and severity but
does not change the public method's confirmed disclosure behavior.

## Credential Boundary Review

F05A stores a normalized bearer token and creates a concrete F04 client in the
token path (`LineMessagingProcessorClass.cs:31-33,45-51`). No literal production
token was found. The explicit credential subject test searches for known old
token fragments (`Line.Messaging.Tests/LineMessagingProcessorCredentialTests.cs:25-39`).

Credential validation is inconsistent:

- `GetRequiredChannelAccessToken` is defined at
  `LineMessagingProcessorClass.cs:120-129`;
- only `SendMessage` invokes it
  (`LineMessagingProcessorClass.cs:275-278`);
- reliable/general send, reply, profile, and RichMenu methods do not invoke it.

F04 writes whatever constructor value it receives into the Authorization
header (`Line.Messaging/LineMessagingClient.cs:107-112,124-128`). Current
workflow factories can return an empty token on missing configuration and then
construct F05A
(`SpeechMessageProducts.ChurchReport/Services/ChurchReportLineAdminNotificationService.cs:142-165`,
`SpeechMessageProducts.ChurchReport/Services/PaymentNotificationService.cs:310-337`).

This is retained as F05A-EXT-002 rather than a standalone credential-leak
security issue: the confirmed effect is inconsistent fail-fast behavior and an
empty credential request, not secret disclosure.

## Event/Input Trust Review

`ProcessMessage(dynamic)` directly indexes event fields and chooses push side
effects by string values
(`LineMessagingProcessorClass.cs:168-253`). The postback path passes raw data
to a fixed-position split parser
(`LineMessagingProcessorClass.cs:193-208,699-711`).

Missing guards include:

- authenticated/signed event envelope;
- null/type/shape checks;
- supported event/message discriminated union;
- bounded postback field parsing;
- cancellation and typed dispatch result.

Counter-evidence and ownership:

- repository search found no caller of `ProcessMessage` or
  `ProcessMessage_TEST`;
- F05B owns ASP.NET webhook authentication/composition;
- no active signature bypass can be claimed from F05A source alone.

Disposition: documented unsafe compatibility API and extraction evidence, not
retained as an active confirmed security issue.

## Shared Mutable State Review

`m_UserId` and `m_Message` are public mutable fields
(`LineMessagingProcessorClass.cs:37-38`) and the dynamic dispatcher writes them
(`LineMessagingProcessorClass.cs:178,188,214,220`).

Counter-evidence:

- the only DI registration is transient
  (`LineMessagingProcessor.AspNetCore/LineMessagingProcessorServiceCollectionExtensions.cs:62-65`);
- the mutating dispatcher has no current caller;
- current send/profile/RichMenu pass-throughs do not use those fields.

Disposition: no current cross-request or cross-user leak promoted. Remove or
isolate the fields in the clean compatibility contract.

## Guards And Counter-Evidence

- Blank user/message/profile identifiers are guarded in active core methods
  (`LineMessagingProcessorClass.cs:257-265,319-327,341-349,577-580,617-625,642-650`).
- SDK message serialization is typed; F05A does not concatenate JSON.
- No token logging or source literal was found.
- The legacy error helpers rethrow after disclosure; they do not silently mark
  the operation successful.
- No current active caller was found for the unsafe dynamic dispatcher,
  postback parser, or exception-to-recipient helpers.

## Hypotheses Requiring Runtime Or External Evidence

1. Deployed exception strings may include provider response bodies, source file
   paths, or line numbers depending on build/runtime configuration.
2. External consumers outside this repository may call the public legacy
   dispatcher or exception helpers.
3. F05B may have webhook signature validation that fully protects current HTTP
   entry; this belongs to F05B diagnosis.
4. Credential rotation frequency and process-global default-constructor use
   cannot be measured statically.
