using FluentAssertions;
using SpeechMessage.Payments.Diagnostics;
using Xunit;

namespace SpeechMessage.Payments.Tests.Diagnostics;

public sealed class PaymentDiagnosticsSanitizerTests
{
    [Fact]
    public void Sanitizer_masks_sensitive_provider_fields_and_preserves_safe_fields()
    {
        var input = new Dictionary<string, string>
        {
            ["PayToken"] = "1234567890abcdef",
            ["signature"] = "ABCDEF123456",
            ["StoreKey"] = "secret-key",
            ["cardno"] = "4111111111111111",
            ["atm_pay_no"] = "12345678901234",
            ["ret_code"] = "00",
            ["order_no"] = "F202606250001"
        };

        var sanitized = PaymentDiagnosticsSanitizer.Sanitize(input);

        sanitized["PayToken"].Should().Be("1234...cdef");
        sanitized["signature"].Should().Be("***");
        sanitized["StoreKey"].Should().Be("***");
        sanitized["cardno"].Should().Be("411111******1111");
        sanitized["atm_pay_no"].Should().Be("12345678901234");
        sanitized["ret_code"].Should().Be("00");
        sanitized["order_no"].Should().Be("F202606250001");
    }
}
