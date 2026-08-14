# Requirements

Implement the local-only, fixed `runtime.health.whoami` ProductClient boundary for ORG-CALL-00003. Keep all existing consumer, feature, CE, legacy ToolUtility, P7.5 and P8 state unchanged. The client must be stateless, DTO-only, fail closed and rely on the injected executor for transport/lease lifecycle ownership.

CCG architecture analysis was attempted through the project self-healing runner on
2026-08-14 with the user-approved 45-second limit. Neither backend produced usable
findings before the local timeout; this task therefore records **雙模型未完成** and
continues with the reviewed local design and verification. This is not a completed
dual-model analysis and it must not be retried merely to wait longer.
