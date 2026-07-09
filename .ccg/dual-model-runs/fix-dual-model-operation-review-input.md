# Review request: fix CCG dual-model operation

Please review the following changes for correctness, maintainability, and failure-mode handling. Focus on:

- Claude default model handling via CLAUDE_MODEL=sonnet.
- Gemini quota/billing classification and diagnostics.
- Health/smoke summary fields and degraded fallback behavior.
- Any risk that provider failures could be misreported as full dual-model success.

Return findings as Critical / Warning / Info.

```diff
System.Object[]
```