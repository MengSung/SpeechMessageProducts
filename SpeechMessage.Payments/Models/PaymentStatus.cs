namespace SpeechMessage.Payments.Models;

/// <summary>
/// 通用付款狀態。
/// Pending 表示建單成功或等待 provider 最終狀態，不等同已付款成功。
/// </summary>
public enum PaymentStatus
{
    Unknown = 0,
    Pending = 1,
    Succeeded = 2,
    Failed = 3,
    Cancelled = 4
}
