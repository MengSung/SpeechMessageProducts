# CCG analyzer Task: p206-layer-direction-fix

## Repository
W:\

## Request
Active task: .trellis/tasks/11-08-11-08-p2-06-send-utilities
Analyze only; do not edit or spawn. Need BOTH Gemini and Claude analysis.
P2-06 clean clone f014a4aa fails existing R4 in Debug and Release: ToolUtility(rank40) -> LineMessagingProcessor.Workflows(rank40). ToolUtility already legitimately references LineMessagingProcessor(rank35). User PROP-005 requires ONE net10 LineLegacySendWorkflow for legacy arbitrary push; formal members remain Core. Do not relax architecture rules or add allowlist entries; no new packages/projects, no business changes.
Candidate minimal fix: MOVE existing LineLegacySendWorkflow.cs (no copy) from Workflows into LineMessagingProcessor/Legacy retaining full namespace LineMessagingProcessor.Workflows and behavior; add explicit System/Collections.Generic/Threading.Tasks usings because lower project disables implicit usings; old Workflows assembly forwards the same full type via TypeForwardedTo for compiled callers. Remove ToolUtility->Workflows ProjectReference. Both ToolUtility and higher Workflows consume same implementation from processor. Preserve wire, retry key, resource ownership, all public signatures. Add test checking implementation assembly and old assembly-qualified type resolution returns SAME type. Existing architecture red is captured. Keep P2-11 paused due timebox; do not edit its packaging files.
Read relevant files: LineMessagingProcessor.Workflows/LineLegacySendWorkflow.cs; ToolUtility/ToolUtility.csproj; LineMessagingProcessor/LineMessagingProcessor.csproj; LineMessagingProcessor.Workflows.Tests/LegacyWorkflowRouteTests.cs; SpeechMessage.Hosting.Tests/Architecture/ArchitectureRules.cs layer map. Output concise correctness/ABI/isolation risks and recommended minimal fix. No external review in this run (phase review occurs once later).


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.
