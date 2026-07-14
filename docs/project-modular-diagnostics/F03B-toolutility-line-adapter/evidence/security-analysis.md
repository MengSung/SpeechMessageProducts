# F03B Security Analysis

Status: COMPLETE
Mode: STATIC_READ_ONLY

## Confirmed Finding: Full Recipient And Message Data Is Persisted Before Delivery

`PushUtility.SendMessage(string,string)` and
`MultiCastTextMessageAsync` call CRM audit persistence before the F04 LINE
request (`ToolUtility/PushUtility.cs:58`, `ToolUtility/PushUtility.cs:64`,
`ToolUtility/PushUtility.cs:82`, `ToolUtility/PushUtility.cs:89`).

The audit flag is a compile-time constant set to `true`
(`ToolUtility/ToolUtilityPartials/ToolUtilityClass.Core.cs:60`). For each
recipient, `ToolUtilityClass.Line`:

- looks up a CRM contact by LINE ID
  (`ToolUtility/ToolUtilityPartials/ToolUtilityClass.Line.cs:33`,
  `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Line.cs:74`);
- stores the full outbound message in `letter.description`
  (`ToolUtility/ToolUtilityPartials/ToolUtilityClass.Line.cs:42`,
  `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Line.cs:83`);
- stores the recipient identifier in `letter.new_displayed_lineid`
  (`ToolUtility/ToolUtilityPartials/ToolUtilityClass.Line.cs:43`,
  `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Line.cs:84`);
- creates the record before LINE transport succeeds
  (`ToolUtility/ToolUtilityPartials/ToolUtilityClass.Line.cs:58`,
  `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Line.cs:99`).

The sole explicit F03B consumer supplies member names and weekly-report content
(`SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs:186`,
`SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs:191`,
`SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs:195`,
`SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs:473`,
`SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs:477`).

Security impact:

- message content and recipient identifiers cross from transient LINE delivery
  into durable CRM storage without a data-minimization contract;
- a failed LINE request still leaves a record shaped like a sent message;
- retention, redaction, and access-control policy are not represented in the
  adapter API.

## Guards And Counter-Evidence

- If no CRM contact matches a recipient, no record is created
  (`ToolUtility/ToolUtilityPartials/ToolUtilityClass.Line.cs:38`,
  `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Line.cs:79`).
  This is a lookup guard, not a content-minimization or delivery-status guard.
- Non-text media methods write an empty description, reducing payload exposure
  for those message kinds (`ToolUtility/PushUtility.cs:105`,
  `ToolUtility/PushUtility.cs:126`, `ToolUtility/PushUtility.cs:146`).
- The general `SendMessage(string,List<ISendMessage>)` overload performs no CRM
  audit (`ToolUtility/PushUtility.cs:40`, `ToolUtility/PushUtility.cs:44`).
  This counter-evidence proves the leakage is overload-dependent and the
  contract is inconsistent; it does not negate the text and multicast paths.
- No channel access token is stored or logged by F03B source. Token sourcing is
  outside this owner.

## Unsafe Boundary Review

`PushUtility` accepts raw recipient strings and raw message content, then
implicitly decides both delivery and persistence. It exposes no recipient kind,
data classification, audit policy, delivery status, retry key, or cancellation
contract (`ToolUtility/PushUtility.cs:34`, `ToolUtility/PushUtility.cs:54`,
`ToolUtility/PushUtility.cs:76`).

The F03B consumer calls multicast methods without awaiting them
(`SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs:111`,
`SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs:129`,
`SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs:477`).
Exceptions raised after the first await cannot be
handled by the surrounding synchronous `try/catch`, so delivery and audit
failures can become unobserved.

## Legacy RichMenu Risk, Not Promoted

`DeleteRichMenuMessage` resolves a user's current provider menu, unlinks the
user, and deletes the provider menu (`ToolUtility/PushUtility.cs:375`,
`ToolUtility/PushUtility.cs:377`, `ToolUtility/PushUtility.cs:379`). The F04 SDK
warns callers to confirm the menu is not shared
(`Line.Messaging/LineMessagingClient.cs:1865`,
`Line.Messaging/LineMessagingClient.cs:1866`).

This is a destructive cross-user integrity risk, but repository search found no
current consumer of the F03B method. It remains a rejected confirmed-security
candidate and an F07/B07 retirement handoff, not a retained active exploit
claim.

## Hypotheses Requiring Runtime Or Policy Evidence

1. CRM ACLs may limit who can read `letter.description` and
   `new_displayed_lineid`; static source does not prove effective role access.
2. A formal retention policy may authorize storage of full notification
   content; no such contract was found in F03B.
3. Production traffic may include additional sensitive content beyond the
   member/report examples. Sampling is required to quantify this without
   exposing values.

These hypotheses do not change the confirmed fact that F03B persists full data
before delivery and has no API-level minimization/status contract.
