# B07 Extraction and Acceleration Analysis

## Ranked Candidates
1. Extract a B07 LINE facade/options contract for ChurchReport routing, recipients, binding URL, profile lookup, push/reply send semantics, and legacy catalog configuration. Keep F04-F07 generic SDK/workflow internals out of B07.
2. Move hard-coded admin recipient, binding host, default organization, and legacy LINE IDs into validated B07 options. B01 keeps login/session decisions.
3. Conditional/rejected candidate: if a reachable production consumer for
   ChurchReportLegacyRichMenuCatalog is proven, isolate it from local filesystem
   assumptions through managed content/configuration and provisioning preflight;
   otherwise do not rank or extract it.
4. Convert LineNotifyUtility to explicit async methods or a bounded best-effort queue. Stage migration because UploadData, NewPerson, PersonalInfomatioManager, WeeklyReportManager, and assignment flows consume it.
5. Preserve B05 ownership of payment timing/content. B07 should only provide transport facade and retry-key propagation used by payment consumers.

## Extraction Outcome
Best first step is B07 options plus facade contract, followed by staged async notification migration. No product extraction was performed.
