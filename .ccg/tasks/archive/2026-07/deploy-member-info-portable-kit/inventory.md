# MemberInfo Target Inventory

## Repository

- Worktree: `D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.1.WorkTreeMemberInfo`
- Branch: `1.0.0.1.WorkTreeMemberInfo`
- Base HEAD: `1c68743a1da360ac2e8385cf424cf47f0e6abfcf`
- Main application: `SpeechMessageProducts.ChurchReport/SpeechMessageProducts.ChurchReport.csproj`
- Namespace compatibility: project folder/assembly is renamed, while source namespaces remain `ChurchReport`.

## Portable Inputs

- ZIP: `docs/portable/member-info-portable-kit.zip`
- ZIP SHA-256: `AE26C46885500CE45F6FBF3DC9134B513C71566E8BFE9222D39EE1255140618A`
- Verifier: `docs/portable/member-info-portable-kit/verify-package.ps1`
- Verifier SHA-256: `BF522D4FEA4DFC7A5E6FB3106F1F54698E260D773F08EE7F9109DB81B53AB621`
- Executable: none found.
- Script signature: unsigned; source inspected before execution.
- Read-only verification result: 73 files, 73 strict UTF-8 files, 73 hashes, 290 relative Markdown links.

## Runtime And Libraries

- Target framework: `net10.0`.
- ASP.NET style: MVC with endpoint routing disabled.
- JSON: `Microsoft.AspNetCore.Mvc.NewtonsoftJson 10.0.0`, `Newtonsoft.Json 13.0.3`, `DefaultContractResolver` (PascalCase).
- DevExtreme server wrapper: `DevExtreme.AspNet.Core 23.1.5`.
- DevExtreme data package: `DevExtreme.AspNet.Data 5.1.0`.
- Actual browser client: DevExtreme `22.1.6`, from the header of `wwwroot/js/devextreme/dx.all.js`.
- Dataverse client: `Microsoft.PowerPlatform.Dataverse.Client 1.2.10` plus existing CRM SDK references.
- Image processing: `SixLabors.ImageSharp 3.1.6`.
- Cache/session: registered `IMemoryCache` and ASP.NET Session.
- UI stack: Razor, jQuery, Bootstrap, DevExtreme DataGrid/Popup.

## Current MemberInfo Surface

- Existing routes: `Index`, `LoadMemberInfoList`, `Detail`, present-record and lesson loaders, single/batch contact image, LINE candidate/resync, image upload, and contact update.
- Existing UI: one legacy flat remote DataGrid with adaptive column hiding.
- Existing avatar behavior: CRM primary image -> normalized LINE URL -> gender/default SVG, protected by contact checks and batch authorization.
- Existing detail behavior: popup, stale-response token, upload toolbar, present/lesson child grids.
- Existing LINE integration: profile reads use `LineMessagingProcessor.LineMessagingProcessorClass`; preserve this host-specific workflow.
- Existing authorization: `new_church_jobtitle` plus `ListManager.LoginType`; Church and Shepherd access, Session cache, current/non-closed contact checks, and batch contact authorization.
- Existing shared protections: global authorization filter, strict no-cache filter, response no-store policy, and session middleware.

## CRM Contracts Observed In Source

- Contact: `contactid`, `fullname`, `mobilephone`, `address2_line1`, `gendercode`, `birthdate`, `statecode`, `customertypecode`, `new_spiriitual_identity`, `entityimage`, `new_line_picture_url`.
- List/listmember: `list`, `listmember`, `listid`, `listname`, `entityid`, `new_app_named`, `purpose`.
- Group metadata required by the kit: `new_group_time`, `new_group_place`.
- Relations: `connection`, `record1id`, `record2id`, `record1roleid`, `record2roleid`.
- Membership ordering authority: `PicklistAttributeMetadata.OptionSet.Options` collection order for `contact.customertypecode`.

## Target/Reference Differences To Preserve

1. The target controller constructor/base call no longer accepts `IPayment`.
2. LINE profile lookup uses the extracted `LineMessagingProcessorClass`, not the older direct SDK client.
3. The target MemberInfo popup has a toolbar upload button and current stale-response protection beyond the reference patch baseline.
4. Main project paths are `SpeechMessageProducts.ChurchReport/**`; new static tests must not restore obsolete `ChurchReport/ChurchReport.csproj` assumptions.
5. Existing AI Traditional-Chinese file headers are retained unless a touched file's established formatting requires a narrow update.

## Missing Feature Set

- District/group/member tree DTOs and builders.
- Authoritative requested-list guard for both Church and Shepherd.
- Tree skeleton, lazy group members, Church-only ungrouped remote paging, and authorized search routes.
- Group count/time/place data flow.
- RelationGoals formatter and tree-row relation mapping.
- Gender/birthdate detail contract.
- Search/loading/cancel/restore state machine.
- Shared exact nine-column factory, fixed 72px/62px columns, widget resizing, single sorting, remote calculated-field guards, and 22.1.6 touch bridge.
- Dynamics metadata-rank provider, Configured/Unknown/Empty sorter, aggregate counts, and segmented ungrouped paging.

## Baseline Verification

- Untouched `dotnet test ChurchReport.MemberInfo.Tests` result: 212 passed, 23 failed.
- Known inherited failure classes: historical `ChurchReport.sln`/`ChurchReport` path assumptions and payment-neutral naming targets.
- Migration verification must prove no additional failures and must not repair unrelated payment tests as part of this scope.

## Review Policy

The owner explicitly waived Gemini and Claude because both providers have no remaining quota. This task uses full local verification and inline zero-trust diff review. No failed or skipped external call will be represented as a passed review.
