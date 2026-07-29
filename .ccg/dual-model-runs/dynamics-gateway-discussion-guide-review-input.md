# Review request: Dynamics Gateway discussion guide update

Review only the newly added or changed content in:

`docs/dynamics-gateway-central-local-82-91-guide.zh-TW.md`

Use the repository diff and compare the document against:

- `.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md`
- `.trellis/tasks/07-23-dynamics-connection-compatibility/prd.md`
- `.trellis/tasks/07-23-dynamics-connection-compatibility/design.md`
- `.trellis/tasks/07-23-dynamics-connection-compatibility/implement.md`

The user asked to preserve the complete discussion in the Traditional Chinese explanation manual. Check:

1. Technical correctness for D365 CE 8.2 IFD and CE 9.1.
2. Clear distinction between the checked-in Data8 `PowerPlatform.Dataverse.Client` project and Microsoft's official `Microsoft.PowerPlatform.Dataverse.Client` NuGet package.
3. Whether the document fairly explains that legacy CRM SDK syntax is not inherently worse, while still justifying Gateway centralization.
4. Correct roles of Central Gateway, Local Gateway, and deferred Embedded mode.
5. Correct configuration split between product JSON, Gateway profile/registry, and secret provider.
6. Correct connection-pool ownership versus organization-wide admission coordination.
7. Correct Phase 4/5/6 preservation and Data8 removal gates.
8. Session isolation, memory/resource lifecycle, and safe sustained-performance requirements.
9. Traditional Chinese clarity, internal consistency, and misleading or overly absolute wording.

Do not modify files. Return a Critical / Warning / Info review. Cite exact headings or phrases for every actionable finding. If there are no Critical or Warning findings, state that explicitly.
