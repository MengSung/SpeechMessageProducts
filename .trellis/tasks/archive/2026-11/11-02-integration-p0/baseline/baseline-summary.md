# P0-01 原始基準

起點：`73b6e6cf409daa7559fd3c11d482e3efc542c0d7`；尚未修改產品程式。

## SDK 與建置

SDK 10.0.403；完整資訊見 dotnet-info.txt、dotnet-sdks.txt。
Release 原始建置 exit 0：錯誤 0、警告 77。

| 警告代碼 | 建置摘要前出現次數 |
|---|---:|
| CS1572 | 1 |
| NU1902 | 20 |
| NU1903 | 56 |

統計取第一次「建置成功」之前的診斷，避免將尾端摘要重複計算。
既有套件弱點與 CS1572 僅盤點，沒有修改套件或抑制警告。

## 原始測試

| 專案 | 通過 | 失敗 | 略過 | 總計 |
|---|---:|---:|---:|---:|
| churchreport.memberinfo.tests | 450 | 21 | 0 | 471 |
| line.messaging.tests | 32 | 0 | 0 | 32 |
| linemessagingprocessor.aspnetcore.tests | 4 | 0 | 0 | 4 |
| linemessagingprocessor.richmenus.tests | 33 | 1 | 0 | 34 |
| linemessagingprocessor.tests | 33 | 0 | 0 | 33 |
| linemessagingprocessor.workflows.tests | 33 | 0 | 0 | 33 |
| speechmessage.payments.tests | 55 | 0 | 0 | 55 |
| toolutility.dataverse.tests | 74 | 2 | 0 | 76 |

合計：通過 714、失敗 24、略過 0、總計 738。原始命令 exit 1。

## 既有失敗清單

### LineMessagingProcessor.RichMenus.Tests.Boundary.RichMenuProjectBoundaryTests.RichMenu_project_does_not_reference_product_specific_dependencies

專案：linemessagingprocessor.richmenus.tests

```text
System.InvalidOperationException : Could not locate solution root.
```

### ToolUtility.Dataverse.Tests.FileToolUtilityTracerTests.Dispose_RemovesListenerFromGlobalCollection

專案：toolutility.dataverse.tests

```text
System.IO.FileNotFoundException : Could not find file 'C:\Users\Administrator\AppData\Local\Temp\toolutility-trace-48f46d737a0142fcbbbf502c99200a51.log'.
```

### ToolUtility.Dataverse.Tests.FileToolUtilityTracerTests.Write_uses_culture_invariant_legacy_timestamp_format

專案：toolutility.dataverse.tests

```text
System.IO.FileNotFoundException : Could not find file 'C:\Users\Administrator\AppData\Local\Temp\toolutility-trace-58bc939be1154be3aa01613a9559e686.log'.
```

### ChurchReport.MemberInfo.Tests.Payments.DonationPaymentServiceExtractionTests.DonationPaymentManager_should_delegate_contact_mapping_to_contact_service

專案：churchreport.memberinfo.tests

```text
System.IO.FileNotFoundException : 找不到測試需要讀取的專案原始碼檔案。
```

### ChurchReport.MemberInfo.Tests.Payments.DonationPaymentReturnProcessorNamingTests.New_donation_fee_payment_processor_exists_as_primary_fee_return_processor

專案：churchreport.memberinfo.tests

```text
Expected processorType not to be <null> because 收費單付款完成流程應由 DonationFeePaymentProcessor 作為主要入口.
```

### ChurchReport.MemberInfo.Tests.Payments.DonationPaymentServiceExtractionTests.DonationPaymentManager_should_delegate_donation_login_contact_workflow

專案：churchreport.memberinfo.tests

```text
System.IO.FileNotFoundException : 找不到測試需要讀取的專案原始碼檔案。
```

### ChurchReport.MemberInfo.Tests.Payments.DonationPaymentReturnProcessorNamingTests.New_payment_result_helper_and_debug_logger_exist

專案：churchreport.memberinfo.tests

```text
Expected Type.GetType("ChurchReport.Tools.DonationPaymentResultHelper, ChurchReport") not to be <null> because 付款結果判斷 helper 應使用中性命名.
```

### ChurchReport.MemberInfo.Tests.Payments.DonationPaymentProcessorNamingTests.New_donation_payment_processor_exists_as_primary_product_workflow_processor

專案：churchreport.memberinfo.tests

```text
Expected processorType not to be <null> because ChurchReport 產品層付款後流程應由 DonationPaymentProcessor 作為主要類別名稱.
```

### ChurchReport.MemberInfo.Tests.Payments.DonationPaymentServiceExtractionTests.DonationPaymentManager_should_delegate_contact_creation_numbering_workflow

專案：churchreport.memberinfo.tests

```text
System.IO.FileNotFoundException : 找不到測試需要讀取的專案原始碼檔案。
```

### ChurchReport.MemberInfo.Tests.Payments.DonationPaymentReturnWorkflowNamingTests.New_donation_payment_workflow_result_exists_after_rename

專案：churchreport.memberinfo.tests

```text
Expected resultType not to be <null> because 新的產品中性 DTO 會成為 ChurchReport 付款回傳流程的主要型別.
```

### ChurchReport.MemberInfo.Tests.Payments.DonationPaymentReturnProcessorNamingTests.New_recurring_donation_payment_processor_exists_as_primary_recurring_return_processor

專案：churchreport.memberinfo.tests

```text
Expected processorType not to be <null> because 定期定額付款完成流程應由 RecurringDonationPaymentProcessor 作為主要入口.
```

### ChurchReport.MemberInfo.Tests.Payments.DonationPaymentManagerNamingTests.Legacy_qpay_manager_remains_as_compatibility_alias

專案：churchreport.memberinfo.tests

```text
Expected managerType not to be <null>.
```

### ChurchReport.MemberInfo.Tests.Payments.DonationPaymentFormModelNamingTests.Provider_specific_names_are_confined_to_provider_code_or_legacy_route_templates

專案：churchreport.memberinfo.tests

```text
System.IO.DirectoryNotFoundException : 找不到 ChurchReport.sln，無法判斷 repository root。
```

### ChurchReport.MemberInfo.Tests.Payments.DonationPaymentServiceExtractionTests.DonationPaymentManager_should_delegate_booking_workflow

專案：churchreport.memberinfo.tests

```text
System.IO.FileNotFoundException : 找不到測試需要讀取的專案原始碼檔案。
```

### ChurchReport.MemberInfo.Tests.Payments.DonationPaymentFormModelNamingTests.Donation_payment_form_model_is_the_primary_churchreport_form_state_type

專案：churchreport.memberinfo.tests

```text
Expected formModelType not to be <null> because 奉獻付款表單是 ChurchReport 的產品層狀態，應使用 DonationPaymentFormModel 這種中性名稱，讓信用卡、ATM、高鉅、台新與未來產品都不需要依附單一 provider 名稱.
```

### ChurchReport.MemberInfo.Tests.Payments.DonationPaymentProcessorNamingTests.Donation_payment_processor_constructors_require_neutral_gateway_create_adapter

專案：churchreport.memberinfo.tests

```text
Expected processorType not to be <null>.
```

### ChurchReport.MemberInfo.Tests.Payments.PaymentProductServiceNamingTests.Product_payment_services_use_provider_neutral_names

專案：churchreport.memberinfo.tests

```text
Expected Type.GetType(expectedServiceName) not to be <null> because ChurchReport 付款後流程服務應以 provider-neutral 名稱呈現，避免把 CRM/LINE/收費單流程誤認為 MyPay 協定實作.
```

### ChurchReport.MemberInfo.Tests.Payments.DonationPaymentServiceExtractionTests.DonationPaymentManager_should_delegate_dedication_fee_form_refresh

專案：churchreport.memberinfo.tests

```text
System.IO.FileNotFoundException : 找不到測試需要讀取的專案原始碼檔案。
```

### ChurchReport.MemberInfo.Tests.Payments.PaymentPostPaymentArchitectureTests.ChurchReport_specific_handlers_do_not_move_to_reusable_workflow_project

專案：churchreport.memberinfo.tests

```text
Expected typeof(ChurchReportPaymentRecordUpdater).Assembly.GetName().Name to be "ChurchReport" with a length of 12, but "SpeechMessageProducts.ChurchReport" has a length of 34, differs near "Spe" (index 0).
```

### ChurchReport.MemberInfo.Tests.Payments.DonationPaymentManagerNamingTests.New_donation_payment_manager_exists_as_primary_ui_payment_state_manager

專案：churchreport.memberinfo.tests

```text
Expected managerType not to be <null> because ChurchReport 奉獻付款的 UI 狀態管理應以 DonationPaymentManager 作為主要名稱.
```

### ChurchReport.MemberInfo.Tests.Payments.DonationPaymentFormModelNamingTests.Product_layer_file_names_should_not_contain_provider_brand_names

專案：churchreport.memberinfo.tests

```text
System.IO.DirectoryNotFoundException : 找不到 ChurchReport.sln，無法判斷 repository root。
```

### ChurchReport.MemberInfo.Tests.Payments.DonationPaymentServiceExtractionTests.DonationPaymentManager_should_delegate_payment_model_assembly

專案：churchreport.memberinfo.tests

```text
System.IO.FileNotFoundException : 找不到測試需要讀取的專案原始碼檔案。
```

### ChurchReport.MemberInfo.Tests.Payments.DonationPaymentNamingCompatibilityTests.New_payment_return_controller_exists_after_rename

專案：churchreport.memberinfo.tests

```text
Expected newType not to be <null> because 新的主要回傳 Controller 應該使用 provider-neutral 命名.
```

### ChurchReport.MemberInfo.Tests.Payments.DonationPaymentServiceExtractionTests.DonationPaymentManager_should_delegate_key_in_dedication_workflow

專案：churchreport.memberinfo.tests

```text
System.IO.FileNotFoundException : 找不到測試需要讀取的專案原始碼檔案。
```
