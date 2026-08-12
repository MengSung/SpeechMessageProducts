本報告針對現有 repository 的 P7 coverage matrix、ChurchReport 呼叫點、Data8 連線能力與既有測試進行分析，為 **Slice D（donation lifecycle）**、**E（appointments）**、**F（contact onboarding）**、**G（fee lessons）**、**H（attendance）** 建立安全的本機 capability contract 實作與測試規劃。

---

### 1. 每個 Slice 對應的現有檔案與 Operation 盤點

依據 `preliminary-capability-inventory.json` 與 `coverage-matrix.json`，各 Slice 的現有檔案與對應的 Operation ID 盤點如下：

#### **Slice D: Donation Lifecycle (`churchreport.donation.lifecycle`)**
*   **ORG-CALL-00036**:
    *   **現有檔案**: `SpeechMessageProducts.ChurchReport/Tools/DonationFeePaymentProcessor.cs`
    *   **Symbol**: `payment success fee/contact/stor lesson updates`
    *   **Operation ID**: `payments.fee.update.after.payment` (Write)
*   **ORG-CALL-00037**:
    *   **現有檔案**: `SpeechMessageProducts.ChurchReport/Tools/RecurringDonationPaymentProcessor.cs`
    *   **Symbol**: `recurring dedication booking + fee create`
    *   **Operation ID**: `payments.dedication.complete.recurring` (Write)
*   **ORG-CALL-00038**:
    *   **現有檔案**: `SpeechMessageProducts.ChurchReport/Services/DonationContactService.cs`
    *   **Symbol**: `CreateEntity/RetrieveEntity/UpdateEntity contact`
    *   **Operation ID**: `payments.contact.create.or.update` (Write)
*   **ORG-CALL-00041**:
    *   **現有檔案**: `SpeechMessageProducts.ChurchReport/Services/DonationBookingService.cs`
    *   **Symbol**: `Retrieve dedication booking`
    *   **Operation ID**: `payments.dedication.booking.retrieve` (Read)
*   **ORG-CALL-00042**:
    *   **現有檔案**: `SpeechMessageProducts.ChurchReport/Services/DonationBookingService.cs`
    *   **Symbol**: `Cancel dedication booking`
    *   **Operation ID**: `payments.dedication.booking.cancel` (Write)
*   **ORG-CALL-00043**:
    *   **現有檔案**: `SpeechMessageProducts.ChurchReport/Services/DonationContactCreationService.cs`
    *   **Symbol**: `Create contact with dedication numbering`
    *   **Operation ID**: `payments.contact.create.with.numbering` (Write)
*   **ORG-CALL-00049**:
    *   **現有檔案**: `SpeechMessageProducts.ChurchReport/Models/DonationPaymentManager.cs`
    *   **Symbol**: `Update card profile`
    *   **Operation ID**: `payments.card.profile.update` (Write)
*   **ORG-CALL-00059**:
    *   **現有檔案**: `ToolUtility/QueryOperations/FetchXmlQueryService.cs`
    *   **Symbol**: `Retrieve dedication booking by contact`
    *   **Operation ID**: `payments.dedication.booking.retrieve.by.contact` (Read)
*   **ORG-CALL-00060**:
    *   **現有檔案**: `SpeechMessageProducts.ChurchReport/Services/DonationDedicationFeeFormService.cs`
    *   **Symbol**: `Resolve contact for dedication form`
    *   **Operation ID**: `payments.contact.resolve.for.form` (Read)

#### **Slice E: Appointments (`churchreport.appointments`)**
*   **ORG-CALL-00039**:
    *   **現有檔案**: `SpeechMessageProducts.ChurchReport/WebServiceConnector/AppointmentsDownUpLoader.cs`
    *   **Symbol**: `Create/Update appointment + AssignOwner`
    *   **Operation ID**: `appointments.entity.create.or.update` (Write)

#### **Slice F: Contact Onboarding (`churchreport.contact.onboarding`)**
*   **ORG-CALL-00044**:
    *   **現有檔案**: `SpeechMessageProducts.ChurchReport/Services/Contact/Impl/ContactService.cs`
    *   **Symbol**: `CreateNewContact orchestration`
    *   **Operation ID**: `newperson.contact.create.full.onboarding` (Write)

#### **Slice G: Fee Lessons (`churchreport.fee.lessons`)**
*   **ORG-CALL-00005**:
    *   **現有檔案**: `ToolUtility/FeeOperations/FeeService.cs`
    *   **Symbol**: `RetrieveMultiple new_fee`
    *   **Operation ID**: `fee.lessons.retrieve.multiple` (Read)
*   **ORG-CALL-00006**:
    *   **現有檔案**: `ToolUtility/FeeOperations/FeeService.cs`
    *   **Symbol**: `RetrieveDedicationBooking/RetrieveFee`
    *   **Operation ID**: `fee.lessons.retrieve.single` (Read)
*   **ORG-CALL-00027**:
    *   **現有檔案**: `SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs`
    *   **Symbol**: `LoadContactStorLessons`
    *   **Operation ID**: `memberinfo.lessons.retrieve.by.contact` (Read)
*   **ORG-CALL-00048**:
    *   **現有檔案**: `SpeechMessageProducts.ChurchReport/WebServiceConnector/FeeDownUpLoader.cs`
    *   **Symbol**: `Load fee records`
    *   **Operation ID**: `fee.lessons.load.records` (Read)
*   **ORG-CALL-00050**:
    *   **現有檔案**: `SpeechMessageProducts.ChurchReport/Models/FeeList.cs`
    *   **Symbol**: `Staging method linked to FeeDownUpLoader`
    *   **Operation ID**: `fee.lessons.staging.update` (Write)
*   **ORG-CALL-00061**:
    *   **現有檔案**: `ToolUtility/QueryOperations/FetchXmlQueryService.cs`
    *   **Symbol**: `Retrieve fee by contact`
    *   **Operation ID**: `fee.lessons.retrieve.by.contact` (Read)
*   **ORG-CALL-00062**:
    *   **現有檔案**: `ToolUtility/QueryOperations/FetchXmlQueryService.cs`
    *   **Symbol**: `Retrieve lessons by contact`
    *   **Operation ID**: `fee.lessons.retrieve.lessons.by.contact` (Read)
*   **ORG-CALL-00064**:
    *   **現有檔案**: `ToolUtility/QueryOperations/FetchXmlQueryService.cs`
    *   **Symbol**: `Retrieve fee records by date range`
    *   **Operation ID**: `fee.lessons.retrieve.by.date.range` (Read)
*   **ORG-CALL-00066**:
    *   **現有檔案**: `SpeechMessageProducts.ChurchReport/WebServiceConnector/FeeDownUpLoader.cs`
    *   **Symbol**: `Create fee record`
    *   **Operation ID**: `fee.lessons.create.record` (Write)
*   **ORG-CALL-00067**:
    *   **現有檔案**: `SpeechMessageProducts.ChurchReport/WebServiceConnector/FeeDownUpLoader.cs`
    *   **Symbol**: `Update fee record`
    *   **Operation ID**: `fee.lessons.update.record` (Write)

#### **Slice H: Attendance (`churchreport.attendance`)**
*   **ORG-CALL-00026**:
    *   **現有檔案**: `SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs`
    *   **Symbol**: `LoadContactPresentRecords`
    *   **Operation ID**: `memberinfo.present.retrieve.by.contact` (Read)
*   **ORG-CALL-00068**:
    *   **現有檔案**: `SpeechMessageProducts.ChurchReport/WebServiceConnector/DownloadIntegrateData.PresentRecord.cs`
    *   **Symbol**: `CreatePresentRecord`
    *   **Operation ID**: `presentrecord.create.on.download` (Write)
*   **ORG-CALL-00069**:
    *   **現有檔案**: `SpeechMessageProducts.ChurchReport/WebServiceConnector/UploadIntegrateData.PresentRecord.cs`
    *   **Symbol**: `CreatePresentRecord / UpdateSinglePresentRecord`
    *   **Operation ID**: `presentrecord.upsert.on.upload` (Write)

---

### 2. 最小且安全的本機實作順序

由於 Slice C 尚未具備完整 CE 實機 evidence，且安全約束要求「僅限本機實作與測試」，實作順序應遵循「先讀後寫、先單一實體後複雜編排、先無狀態後有狀態對帳」的原則，以確保本機 Data8 模擬器或 Mock 能夠在不依賴真實 CE 的情況下，逐步建立穩固的契約。

1.  **第一階段：唯讀能力基線 (Slice G & H Read Operations)**
    *   實作 `fee.lessons.retrieve.*` (ORG-CALL-00005, 00006, 00027, 00048, 00061, 00062, 00064) 與 `memberinfo.present.retrieve.by.contact` (ORG-CALL-00026)。
    *   *理由*：唯讀操作不涉及資料變更與 cleanup，最適合用來驗證本機 Data8 模擬器的 schema 映射與 OData 查詢解析。
2.  **第二階段：單一實體寫入與冪等性 (Slice E & H Write Operations)**
    *   實作 `appointments.entity.create.or.update` (ORG-CALL-00039) 與 `presentrecord.create.on.download` / `presentrecord.upsert.on.upload` (ORG-CALL-00068, 00069)。
    *   *理由*：這些寫入操作結構相對單純，主要為單一實體的 Create/Update，適合用來建立本機的 deterministic cleanup 與 timeout no-replay 測試機制。
3.  **第三階段：財務與生命週期寫入 (Slice D - Donation Lifecycle)**
    *   實作 `payments.fee.update.after.payment` (ORG-CALL-00036)、`payments.dedication.complete.recurring` (ORG-CALL-00037)、`payments.contact.create.or.update` (ORG-CALL-00038) 等。
    *   *理由*：涉及金流與奉獻狀態，需要嚴格的 partial-completion policy（例如：金流成功但 CRM 更新失敗時的對帳機制）與 read-back 驗證。
4.  **第四階段：複雜業務編排 (Slice F - Contact Onboarding)**
    *   實作
