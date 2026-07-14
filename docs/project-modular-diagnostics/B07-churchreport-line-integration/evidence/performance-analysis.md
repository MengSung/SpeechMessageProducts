# B07 Performance and Design Analysis

## Ranked Findings
1. LineNotifyUtility fire-and-forget sends: calls at lines 111,129,146,163,195,226,477 do not await MultiCastTextMessageAsync, so request paths can leak unbounded async work and lose failures.
2. Sync-over-async admin facade: ChurchReportLineAdminNotificationService.cs:100-110 uses GetAwaiter().GetResult(), blocking request threads during LINE latency.
3. Repeated LINE client/workflow construction: LineNotifyUtility.cs:61-67 and LineUtilityClass.cs:188-203 construct LineMessagingClient and workflow graphs per legacy utility instance, leaving lifetime and socket pressure ambiguous.
4. Rejected ranked candidate: ChurchReportLegacyRichMenuCatalog.cs:44 hard-codes
   `D:\暫存區\richmenu.PNG` and line 60 opens it during catalog definition, but
   no reachable production caller was proven. Retain only as catalog preflight
   debt unless reachability is established.
5. Rejected dormant candidate: ReplyUtility.cs:253-256 obtains
   stream/Image/thumbnail objects without visible disposal, but the media path
   has no proven reachable caller. Re-open only if the path is reactivated.

## Design Outcome
B07 has actionable request-path and lifecycle improvements. Runtime measurement remains pending because this task forbids restore/build/test.
