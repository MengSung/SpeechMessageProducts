# B04C Performance Analysis

## Per-Record CRM IO In QR Attendance Paths

Personal QR flow retrieves meeting statistics and present records, then iterates each present record and retrieves/updates entities one by one. Related weekly report lookup/update may happen inside that loop.

Evidence:

- Meeting statistics retrieval: `SpeechMessageProducts.ChurchReport/Tools/PersonalQrCodeUtility.cs:202`.
- Present record retrieval by user/date: `SpeechMessageProducts.ChurchReport/Tools/PersonalQrCodeUtility.cs:261`.
- Per-record loop: `SpeechMessageProducts.ChurchReport/Tools/PersonalQrCodeUtility.cs:268`.
- Per-record retrieve: `SpeechMessageProducts.ChurchReport/Tools/PersonalQrCodeUtility.cs:270`.
- Per-record signing process: `SpeechMessageProducts.ChurchReport/Tools/PersonalQrCodeUtility.cs:273`.
- Per-record update: `SpeechMessageProducts.ChurchReport/Tools/PersonalQrCodeUtility.cs:285`.
- Related weekly report retrieve/update: `SpeechMessageProducts.ChurchReport/Tools/PersonalQrCodeUtility.cs:292` and `:296`.

## Serial CRM Reads And Writes In Poll Flow

PollManager serially retrieves contact, lesson, stored lesson, updates contact and stored lesson, creates new stored lesson, retrieves/assigns owner, and constructs results in one flow.

Evidence:

- Contact retrieval: `SpeechMessageProducts.ChurchReport/Models/PollManager.cs:76`.
- Lesson retrieval from QR id: `SpeechMessageProducts.ChurchReport/Models/PollManager.cs:85`.
- Stored lesson fetch and retrieve: `SpeechMessageProducts.ChurchReport/Models/PollManager.cs:116` and `:121`.
- Contact update: `SpeechMessageProducts.ChurchReport/Models/PollManager.cs:393`.
- Stored lesson update: `SpeechMessageProducts.ChurchReport/Models/PollManager.cs:414`.
- Create and owner assignment: `SpeechMessageProducts.ChurchReport/Models/PollManager.cs:450` and `:455`.

## Optimization Opportunities

- Batch validation: resolve QR target, user, attendance/poll record, and existing write state in one query shape per scan type.
- Batch writes: use F03A CRM batch support where available for attendance/poll updates.
- Cache only stable metadata: QR category mappings and schedule settings are candidates; user/session-specific scan data should not be cached globally.
- Add cancellation and timeout boundaries for QR POSTs to reduce request pileups under scan bursts.

## Measurement Plan

- Count CRM retrieve/update/create calls for course, poll, small-group, Sunday, and personal QR scans.
- Record p50/p95 latency per scan path before and after batching.
- Measure duplicate/replayed scan behavior under concurrent requests.
