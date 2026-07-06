# 金流呼叫檔案與 Function 索引

本索引用來對照 `payment-flow-deep-v10` 到 `payment-flow-deep-v14` 五張圖。圖中節點會盡量標示「檔案 -> function」，讓你可以從流程圖直接回到程式碼。

## 建立付款入口

- `ChurchReport/Controllers/DedicationController.cs`
  - `SaveQPayDedication(QpayModel QpayModel)`
- `ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.PaymentProcessing.cs`
  - `ProcessCreditCardPayment(Entity LineLoginContact, QpayModel QpayModel, string orderDate)`
  - `ProcessRecurringPayment(Entity LineLoginContact, QpayModel QpayModel, string orderDate)`
  - `ProcessMobilePayment(Entity LineLoginContact, QpayModel QpayModel, string orderDate)`
  - `ProcessLinePayPayment(Entity LineLoginContact, QpayModel QpayModel, string orderDate)`
  - `ProcessAtmPayment(Entity LineLoginContact, QpayModel QpayModel, string orderDate)`
  - `ProcessAtm(Guid aCreatedFeeId, Entity aFeeToUpdate, QpayModel QpayModel, string OrderId, string LineId, Entity LineLoginContact)`
- `ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.PaymentGateway.cs`
  - `CreOrderCard(...)`
  - `CreateOrderATM(int Amount, string ProductName, string OrderDate, string FeeId)`
  - `CreateDonationPaymentOrder(...)`
  - `GetRequiredDonationPaymentCreateGatewayAdapter()`

## ChurchReport 到通用金流核心的 Adapter

- `ChurchReport/Payments/DonationPaymentCreateGatewayAdapter.cs`
  - `CreateCardPaymentAsync(QPayCreatePaymentInput input, CancellationToken cancellationToken = default)`
  - `CreateLegacyOrderAsync(QPayCreatePaymentInput input, CancellationToken cancellationToken = default)`
  - `BuildMetadata(QPayCreatePaymentInput input)`
  - `ResolveItems(QPayCreatePaymentInput input)`
  - `ToLegacyCreOrder(QPayCreatePaymentInput input, PaymentCreateResult result)`
  - `ApplyLegacyPaymentUrl(CreOrder order, string paymentMethod, string paymentPageUrl, IReadOnlyDictionary<string, string> providerData)`
- `SpeechMessage.Payments.AspNetCore/PaymentCreateRequestFactory.cs`
  - `Create(PaymentCreateRequestInput input)`

## 通用金流核心

- `SpeechMessage.Payments/Gateway/PaymentGateway.cs`
  - `CreatePaymentAsync(PaymentCreateRequest request, CancellationToken cancellationToken = default)`
  - `QueryPaymentAsync(PaymentQueryRequest request, CancellationToken cancellationToken = default)`
  - `ParseCallbackAsync(PaymentCallbackRequest request, CancellationToken cancellationToken = default)`
  - `ResolveProvider(string? profileName, PaymentProviderKind? providerHint)`
- `SpeechMessage.Payments/Configuration/OptionsPaymentProfileResolver.cs`
  - `Resolve(string? profileName)`

## Provider 實作

- Sinopac / QPay
  - `SpeechMessage.Payments/Providers/Sinopac/SinopacPaymentProvider.cs`
    - `CreatePaymentAsync(...)`
    - `QueryPaymentAsync(...)`
    - `ParseCallbackAsync(...)`
    - `SendOrderAsync<TRequest, TResult>(...)`
    - `PostJsonAsync<TResponse>(...)`
    - `ResolveCreateResult(...)`
  - `SpeechMessage.Payments/Providers/Sinopac/SinopacRequestMapper.cs`
    - `MapCreateRequest(...)`
    - `MapOrderPayQuery(...)`
  - `SpeechMessage.Payments/Providers/Sinopac/SinopacCrypto.cs`
    - `BuildAesKey(...)`
    - `Encrypt(...)`
    - `Decrypt(...)`
  - `SpeechMessage.Payments/Providers/Sinopac/SinopacSigner.cs`
    - `GenerateSign(...)`
    - `GetSigningString(...)`
  - `SpeechMessage.Payments/Providers/Sinopac/SinopacCallbackParser.cs`
    - `Parse(...)`
  - `SpeechMessage.Payments/Providers/Sinopac/SinopacStatusMapper.cs`
    - `MapCreate(...)`
    - `Map(...)`

- MyPay
  - `SpeechMessage.Payments/Providers/MyPay/MyPayPaymentProvider.cs`
    - `CreatePaymentAsync(...)`
    - `QueryPaymentAsync(...)`
    - `ParseCallbackAsync(...)`
  - `SpeechMessage.Payments/Providers/MyPay/MyPayRequestMapper.cs`
    - `MapCreatePayload(...)`
    - `MapCreateForm(...)`
    - `Encrypt(...)`
  - `SpeechMessage.Payments/Providers/MyPay/MyPayCallbackParser.cs`
    - `Parse(...)`
  - `SpeechMessage.Payments/Providers/MyPay/MyPaySignatureVerifier.cs`
    - `Validate(...)`
  - `SpeechMessage.Payments/Providers/MyPay/MyPayStatusMapper.cs`
    - `Map(...)`

- Taishin / TSPG
  - `SpeechMessage.Payments/Providers/Taishin/TaishinPaymentProvider.cs`
    - `CreatePaymentAsync(...)`
    - `QueryPaymentAsync(...)`
    - `ParseCallbackAsync(...)`
    - `PostAsync(...)`
  - `SpeechMessage.Payments/Providers/Taishin/TaishinRequestMapper.cs`
    - `MapCreatePayload(...)`
    - `MapQueryPayload(...)`
  - `SpeechMessage.Payments/Providers/Taishin/TaishinCallbackParser.cs`
    - `Parse(...)`
  - `SpeechMessage.Payments/Providers/Taishin/TaishinHashVerifier.cs`
    - `Validate(...)`
  - `SpeechMessage.Payments/Providers/Taishin/TaishinStatusMapper.cs`
    - `Map(...)`

## 回呼與付款完成處理

- Sinopac / legacy QPay return
  - `ChurchReport/Controllers/QPayCardController.cs`
    - `QPayReturnUrl(string ShopNo, string PayToken)`
  - `ChurchReport/Controllers/PaymentReturnController.cs`
    - `Return(string ShopNo, string PayToken)`
    - `ReturnCore(string ShopNo, string PayToken, string traceSource)`
    - `EnsureReturnFields(PaymentCallbackRequest request, string shopNo, string payToken)`
  - `SpeechMessage.Payments.AspNetCore/PaymentHttpRequestMapper.cs`
    - `MapAsync(HttpRequest request, string profileName, PaymentProviderKind? providerHint = null, CancellationToken cancellationToken = default)`
  - `SpeechMessage.Payments.AspNetCore/PaymentAcknowledgementResultMapper.cs`
    - `ToActionResult(PaymentCallbackAcknowledgement acknowledgement)`

- MyPay callback
  - `ChurchReport/Controllers/MyPayController.cs`
    - `PaymentNotify()`
    - `ResolveContactEntity(Entity feeEntity, out string fullName)`

- Taishin / TSPG callback
  - `ChurchReport/Controllers/TSPGController.cs`
    - `PostBack()`
    - `ResultUrl()`
    - `CreatePayment([FromBody] PaymentCreateRequest request)`
    - `QueryOrderStatus(string orderId)`
    - `ParseTaishinCallbackAsync()`
    - `UpdateFeeEntityByOrderNo(PaymentWorkflowResult result)`
    - `SendPaymentNotificationToContact(Entity feeEntity, PaymentWorkflowResult result, decimal amount)`

## 產品後處理：CRM 與 LINE

- `ChurchReport/Payments/DonationPaymentReturnWorkflow.cs`
  - `HandleReturn(string shopNo, string payToken, PaymentStatusResult statusResult)`
  - `CreateWorkflowPaymentResult(...)`
- `ChurchReport/Payments/DonationPaymentProductWorkflowDispatcher.cs`
  - `HandleFeeReturn(...)`
  - `HandleDedicationBookingReturn(...)`
- `ChurchReport/Payments/ChurchReportPaymentPostPaymentHandlers.cs`
  - `ChurchReportPaymentRecordUpdater.UpdateAsync(PaymentPostPaymentContext context, CancellationToken cancellationToken = default)`
  - `ChurchReportPaymentPayerNotifier.NotifyAsync(PaymentPostPaymentContext context, CancellationToken cancellationToken = default)`
- `SpeechMessage.Payments.Workflows/PaymentWorkflowResultMapper.cs`
  - `Map(PaymentCallbackResult result)`
- `SpeechMessage.Payments.Workflows/PaymentPostPaymentWorkflow.cs`
  - `ExecuteAsync(PaymentPostPaymentContext context, CancellationToken cancellationToken = default)`
- `ChurchReport/Services/PaymentCrmService.cs`
  - `UpdateFeeEntityWithPaymentResult(...)`
- `ChurchReport/Services/PaymentNotificationService.cs`
  - `SendLineNotificationByType(...)`
  - `SendLineFailureNotificationByType(...)`
  - `SendLineMessage(string lineId, string message)`
