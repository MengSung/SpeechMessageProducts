# B04B Performance Analysis

## Summary

B04B has obvious CRM N+1 data-loading problems in equipment lesson/status flows,
and automatic UI detail expansion can multiply backend calls. Repeated event
handler allocation remains a runtime-only candidate rather than part of the
confirmed B04B-PERF-002 issue.

## Findings

### B04B-PERF-001 Equipment lesson/status retrieval performs nested CRM calls

- Evidence:
  - `LoadEquipmentStorLessons` retrieves lesson relationship data for one contact, loops through `storLessons.Entities`, and retrieves each `new_disciple_lessons` entity individually at SpeechMessageProducts.ChurchReport/Controllers/EquipmentController.cs:303-429.
  - `DownloadEquipment.ProcessEquipmenSmallGroupList` loops groups, `GetEachContact` loops members, retrieves each contact, then `GetEachStorLesson` retrieves relationship rows, each stor lesson entity, and each disciple lesson entity at SpeechMessageProducts.ChurchReport/WebServiceConnector/DownloadEquipment.cs:255-356.
  - `EquipmentStatusCalculator.CalculateEquipmentStatusForMembers` loops members, retrieves contacts by full name, and calls per-contact status calculation, which itself performs relationship and entity retrievals at SpeechMessageProducts.ChurchReport/WebServiceConnector/EquipmentStatusCalculator.cs:43-168.
- Design problem:
  - This creates a nested CRM call pattern roughly proportional to groups x members x lessons, with extra per-lesson entity fetches.
  - Full-name contact lookup is also a poor key for performance and correctness.
- Impact:
  - Equipment pages can become slow or unstable for large groups.
  - Server-side debug/profiling already wraps these phases, indicating known hot-path sensitivity.
- Recommended action:
  - Replace nested per-row retrieval with batched FetchXML/query projection containing the contact, stor lesson, and disciple lesson fields needed by the grid.
  - Use contact id rather than full name for status calculation.
  - Add runtime measurement before/after using the existing `PerfPhase` labels.

### B04B-PERF-002 Equipment UI can fan out detail loads

- Evidence:
  - `EquipmentContactView` sets master detail `AutoExpandAll(true)`, causing all visible contacts to load lesson details by default at SpeechMessageProducts.ChurchReport/Views/Equipment/EquipmentContactView.cshtml:79.
- Design problem:
  - Auto-expanding detail rows can trigger many `LoadEquipmentStorLessons` calls, amplifying the CRM N+1 issue.
- Recommended action:
  - Disable default detail expansion for contacts or lazy-load only selected rows.
  - Validate with a group containing many contacts and lessons.

### B04B-PERF-RV-001 Repeated row-handler allocation needs runtime proof

- Disposition: runtime-only; not a ranked confirmed issue.
- Evidence: row-prepared handlers attach mouse or hover listeners in
  `EquipmentContactView.cshtml:97-101`, `EquipmentView.cshtml:224-228`, and
  `EquipmentStorLessonsView.cshtml:250`.
- Required proof: instrument handler counts across repeated grid renders and
  compare them with delegated or CSS-only hover behavior before promotion.

## Runtime Evidence Status

- No `dotnet build`, tests, restore, or runtime profiling were run due diagnostic-only and review constraints.
- The findings are source-confirmed design issues; performance magnitude requires runtime validation.
