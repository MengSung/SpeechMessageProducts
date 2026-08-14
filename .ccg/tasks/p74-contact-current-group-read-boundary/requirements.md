# Requirements

Audit only `ORG-CALL-00052` (`contact.current.group.retrieve`). Trace the current-group read
through its production caller and adjacent membership/attendance/contact/Owner/notification
effects. Decide whether a separate server-authorized bounded DTO-only Gateway boundary is safe.
Fail closed if the current mutable Entity, ToolUtility/shared state, first-match semantics or
write adjacency cannot be eliminated. No CE, fixture, mutation, feature gate, traffic, P7.5 or
P8 operation is authorized.
