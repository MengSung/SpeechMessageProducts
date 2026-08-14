# P7.4 ORG-CALL-00028 final local-only review

Review the current uncommitted change set scoped to `.trellis/tasks/08-14-08-14-p74-memberinfo-contact-image-full-response`.

Required properties:
- `memberinfo.contact.retrieve.image.display` is a server-owned typed operation with one fixed CE 9.1 contact Retrieve projection: `entityimage`, `new_line_picture_url`, `gendercode`.
- Exact CRM entity logical-name/ID matching is required before any image/redirect/avatar projection; mismatched identity fails closed.
- Image > exact HTTPS allowlisted LINE hosts (`profile.line-scdn.net`, `obs.line-apps.com`) > optional gender avatar. No generic URL, non-default port, URL user-info, fragment or legacy fallback.
- Data8 and ChurchReport host validation must agree.
- ChurchReport route is gated false by default and orders gate -> scope -> GUID parse -> CanViewContact -> typed client -> dispatch.
- No SDK Entity, ToolUtility, cache, retry, caller-selected profile/connector/endpoint, CE request/mutation, traffic switch, P7.5 removal or P8 work.
- Cancellation must propagate; A/B request isolation and defensive image copies must hold; no resource/session/memory leak.
- Treat only actual evidence as findings. Return Critical/Warning/Info with exact file/line evidence. Do not propose unsafe shortcuts.

Evidence already run: focused Dynamics display tests 9/9, focused ChurchReport service tests 3/3, controller contract tests 4/4.