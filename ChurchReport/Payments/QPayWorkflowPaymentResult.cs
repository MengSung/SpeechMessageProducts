using System;

namespace ChurchReport.Payments;

/// <summary>
/// 舊 ChurchReport QPay 命名的付款 workflow DTO。
/// 新程式請使用 <see cref="DonationPaymentWorkflowResult"/>；此型別只作為相容層，
/// 讓尚未完成改名的 CRM、LINE 與結果頁處理器可以在遷移期間繼續編譯與運作。
/// </summary>
[Obsolete("Use DonationPaymentWorkflowResult. QPay naming is retained only for compatibility during the migration.")]
public sealed record QPayWorkflowPaymentResult : DonationPaymentWorkflowResult;
