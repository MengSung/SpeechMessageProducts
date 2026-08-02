using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace SpeechMessage.Dynamics.WorkerProtocol;

internal static class WorkerEnvelopeValidator
{
    private static readonly string[] ForbiddenFieldTerms =
    {
        "password",
        "credential",
        "token",
        "cookie",
        "connectionstring",
        "authorization",
        "endpoint",
        "organizationuri",
        "username",
        "lineid",
        "session"
    };

    internal static void ValidateRequest(
        WorkerRequestV1 request,
        WorkerProtocolLimits limits)
    {
        if (request.RequestId == Guid.Empty)
        {
            throw InvalidEnvelope("The worker request identifier is invalid.");
        }

        ValidateIdentifier(request.ProcessNonce, limits, "process nonce");
        ValidateIdentifier(request.ProfileGenerationId, limits, "profile generation");
        ValidateIdentifier(
            request.OperationDefinitionRevision,
            limits,
            "operation revision");
        ValidateIdentifier(request.CapabilityOperationId, limits, "operation identifier");

        if (request.DeadlineUtcTicks <= 0 ||
            request.DeadlineUtcTicks > DateTime.MaxValue.Ticks)
        {
            throw InvalidEnvelope("The worker request deadline is invalid.");
        }

        if (request.Parameters.Count > limits.MaximumObjectMembers)
        {
            throw LimitExceeded("The worker request contains too many parameters.");
        }

        var state = new ValidationState(limits);
        foreach (var parameter in request.Parameters)
        {
            ValidateFieldName(parameter.Key, limits);
            ValidateValue(parameter.Value, depth: 1, state);
        }
    }

    internal static void ValidateStandaloneValue(
        WorkerValue value,
        WorkerProtocolLimits limits)
    {
        ValidateValue(
            value ?? throw new ArgumentNullException(nameof(value)),
            depth: 1,
            new ValidationState(limits ?? throw new ArgumentNullException(nameof(limits))));
    }

    private static void ValidateValue(
        WorkerValue? value,
        int depth,
        ValidationState state)
    {
        if (value is null)
        {
            throw InvalidEnvelope("The worker value is missing.");
        }

        if (depth > state.Limits.MaximumValueDepth)
        {
            throw LimitExceeded("The worker value nesting limit was exceeded.");
        }

        switch (value.Kind)
        {
            case WorkerValueKind.Null:
                RequireShape(value, scalar: false, items: false, members: false);
                return;
            case WorkerValueKind.Boolean:
                RequireShape(value, scalar: true, items: false, members: false);
                if (value.Scalar is not ("true" or "false"))
                {
                    throw InvalidEnvelope("The worker Boolean value is invalid.");
                }

                return;
            case WorkerValueKind.Int64:
                RequireShape(value, scalar: true, items: false, members: false);
                if (!long.TryParse(
                        value.Scalar,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var integer) ||
                    !string.Equals(
                        integer.ToString(CultureInfo.InvariantCulture),
                        value.Scalar,
                        StringComparison.Ordinal))
                {
                    throw InvalidEnvelope("The worker Int64 value is invalid.");
                }

                return;
            case WorkerValueKind.Decimal:
                RequireShape(value, scalar: true, items: false, members: false);
                if (!decimal.TryParse(
                        value.Scalar,
                        NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                        CultureInfo.InvariantCulture,
                        out var decimalValue) ||
                    !string.Equals(
                        decimalValue.ToString("G29", CultureInfo.InvariantCulture),
                        value.Scalar,
                        StringComparison.Ordinal))
                {
                    throw InvalidEnvelope("The worker Decimal value is invalid.");
                }

                return;
            case WorkerValueKind.String:
                RequireShape(value, scalar: true, items: false, members: false);
                ValidateString(value.Scalar!, state.Limits.MaximumStringUtf8Bytes);
                return;
            case WorkerValueKind.Guid:
                RequireShape(value, scalar: true, items: false, members: false);
                if (!Guid.TryParseExact(value.Scalar, "N", out _))
                {
                    throw InvalidEnvelope("The worker Guid value is invalid.");
                }

                return;
            case WorkerValueKind.UtcDateTime:
                RequireShape(value, scalar: true, items: false, members: false);
                if (!long.TryParse(
                        value.Scalar,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var ticks) ||
                    ticks < DateTime.MinValue.Ticks ||
                    ticks > DateTime.MaxValue.Ticks)
                {
                    throw InvalidEnvelope("The worker UTC date-time value is invalid.");
                }

                return;
            case WorkerValueKind.Array:
                RequireShape(value, scalar: false, items: true, members: false);
                if (value.Items!.Count > state.Limits.MaximumArrayItems)
                {
                    throw LimitExceeded("The worker array item limit was exceeded.");
                }

                state.AddItems(value.Items.Count);
                foreach (var item in value.Items)
                {
                    ValidateValue(item, depth + 1, state);
                }

                return;
            case WorkerValueKind.Object:
                RequireShape(value, scalar: false, items: false, members: true);
                if (value.Members!.Count > state.Limits.MaximumObjectMembers)
                {
                    throw LimitExceeded("The worker object member limit was exceeded.");
                }

                state.AddMembers(value.Members.Count);
                foreach (var member in value.Members)
                {
                    ValidateFieldName(member.Key, state.Limits);
                    ValidateValue(member.Value, depth + 1, state);
                }

                return;
            default:
                throw InvalidEnvelope("The worker value kind is unsupported.");
        }
    }

    private static void RequireShape(
        WorkerValue value,
        bool scalar,
        bool items,
        bool members)
    {
        if ((value.Scalar is not null) != scalar ||
            (value.Items is not null) != items ||
            (value.Members is not null) != members)
        {
            throw InvalidEnvelope("The worker value shape does not match its kind.");
        }
    }

    internal static void ValidateIdentifier(
        string value,
        WorkerProtocolLimits limits,
        string category)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw InvalidEnvelope($"The worker {category} is missing.");
        }

        ValidateString(value, limits.MaximumIdentifierUtf8Bytes);
        if (value.Any(character =>
                !(character is >= 'a' and <= 'z') &&
                !(character is >= 'A' and <= 'Z') &&
                !(character is >= '0' and <= '9') &&
                character is not '.' and not '_' and not '-'))
        {
            throw InvalidEnvelope($"The worker {category} contains an invalid character.");
        }
    }

    private static void ValidateFieldName(string value, WorkerProtocolLimits limits)
    {
        ValidateIdentifier(value, limits, "field name");
        var normalized = value.Replace("_", string.Empty).Replace("-", string.Empty);
        if (ForbiddenFieldTerms.Any(term =>
                normalized.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0))
        {
            throw InvalidEnvelope("The worker field name is not permitted by the IPC contract.");
        }
    }

    private static void ValidateString(string value, int maximumUtf8Bytes)
    {
        if (Encoding.UTF8.GetByteCount(value) > maximumUtf8Bytes)
        {
            throw LimitExceeded("The worker string limit was exceeded.");
        }
    }

    private static WorkerProtocolException InvalidEnvelope(string message) =>
        new WorkerProtocolException(WorkerProtocolFailureCategory.InvalidEnvelope, message);

    private static WorkerProtocolException LimitExceeded(string message) =>
        new WorkerProtocolException(
            WorkerProtocolFailureCategory.EnvelopeLimitExceeded,
            message);

    private sealed class ValidationState
    {
        private int _totalItems;
        private int _totalMembers;

        internal ValidationState(WorkerProtocolLimits limits)
        {
            Limits = limits;
        }

        internal WorkerProtocolLimits Limits { get; }

        internal void AddItems(int count)
        {
            _totalItems = checked(_totalItems + count);
            if (_totalItems > Limits.MaximumArrayItems)
            {
                throw LimitExceeded("The worker total array item limit was exceeded.");
            }
        }

        internal void AddMembers(int count)
        {
            _totalMembers = checked(_totalMembers + count);
            if (_totalMembers > Limits.MaximumObjectMembers)
            {
                throw LimitExceeded("The worker total object member limit was exceeded.");
            }
        }
    }
}
