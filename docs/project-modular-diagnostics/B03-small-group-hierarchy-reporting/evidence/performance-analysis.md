# B03 Performance Analysis

Status: LOCAL_DIAGNOSIS_COMPLETE_CCG_PENDING
Module: B03
Mode: DIAGNOSIS_ONLY

## Finding: Weekly Report Paths Use Unbatched CRM Calls

Severity: High

Evidence:

- `SpeechMessageProducts.ChurchReport/Tools/WeeklyReportProcessor.cs:145` through
  `WeeklyReportProcessor.cs:172` loops every list and calls
  `QueryWeeklyReportBySunday(this.m_Sunday, ListEntity.Id)` inside the loop.
- `WeeklyReportProcessor.cs:259` through `WeeklyReportProcessor.cs:294` performs
  six role-specific `QueryListByContactId` calls and merge passes.
- `WeeklyReportProcessor.cs:313` through `WeeklyReportProcessor.cs:327` uses a
  nested duplicate scan while merging collections.
- `WeeklyReportProcessor.cs:393` through `WeeklyReportProcessor.cs:447` creates a
  weekly report, retrieves/assigns owner, creates present records, and records
  the created ID.
- `WeeklyReportProcessor.cs:548` through `WeeklyReportProcessor.cs:628` retrieves
  list members, then for each member retrieves contact data, creates a present
  record, assigns owner, and retrieves the created record.
- `SpeechMessageProducts.ChurchReport/WebServiceConnector/UploadIntegrateData.AsyncWrapper.cs:51`
  through `UploadIntegrateData.AsyncWrapper.cs:72` runs synchronous upload work on
  the thread pool.
- `SpeechMessageProducts.ChurchReport/WebServiceConnector/UploadIntegrateData.WeeklyReport.cs:152`
  through `UploadIntegrateData.WeeklyReport.cs:185` creates weekly reports and
  present records, then updates weekly-report state.
- `UploadIntegrateData.WeeklyReport.cs:225` through
  `UploadIntegrateData.WeeklyReport.cs:252` loops present records to recalculate
  totals.
- `UploadIntegrateData.WeeklyReport.cs:255` through
  `UploadIntegrateData.WeeklyReport.cs:311` retrieves and updates weekly-report
  records and can send LINE notification.
- `UploadIntegrateData.WeeklyReport.cs:457` through
  `UploadIntegrateData.WeeklyReport.cs:470` retrieves a list and member
  collection just to count members.
- `SpeechMessageProducts.ChurchReport/WebServiceConnector/WeeklyReportManager.cs:135`
  through `WeeklyReportManager.cs:150` loops lists and retrieves weekly-report
  relationships per list.
- `WeeklyReportManager.cs:212` through `WeeklyReportManager.cs:250` loops present
  lists and repeats many-to-one weekly-report retrieval.
- `WeeklyReportManager.cs:330` through `WeeklyReportManager.cs:353` loops lists
  again for update and notification.
- `WeeklyReportManager.cs:505` through `WeeklyReportManager.cs:531` merges two
  collections with a nested loop.

Assessment:

The hot path scales by role-query count, list count, member count, and
present-record count. The code repeatedly performs CRM retrieval/write/assign
operations inside loops and hides synchronous I/O behind `Task.Run`, which can
increase latency, CRM API usage, and ASP.NET thread-pool pressure.

Recommended next action:

Introduce a B03 weekly-report query/upload service with batchable inputs:
contact ID, permitted list IDs, Sunday date, and projection columns. Batch CRM
weekly-report, membership, contact, and present-record retrieval. Replace nested
duplicate scans with dictionaries/sets and move upload execution to a durable
worker with cancellation, retry, idempotency, and observable status.

## Rejected Candidates

- Direct chart-cache set without `RegisterCacheKey`: downgraded. Deterministic
  removal exists in `SmallGroupCacheManager.cs:109` through
  `SmallGroupCacheManager.cs:166`, and no stale-cache bug was proven.
- Static CSS payload: not retained for B03 because shared UI/vendor asset
  governance belongs to X03.
- Runtime magnitude claims: not made because build/test/benchmark commands are
  prohibited in this diagnosis-only pass.
