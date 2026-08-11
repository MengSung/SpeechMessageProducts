using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace SpeechMessage.Dynamics.WorkerProtocol;

/// <summary>
/// 對 WorkerValue tree 與 request metadata 執行無 I/O、無共享可變狀態的 fail-closed 驗證。
/// 除 shape 與 canonical scalar 外，還會拒絕 credential／token／cookie／endpoint／Session 等敏感欄位名稱，
/// 並以全樹累計上限防止巢狀小集合繞過 allocation bound。
/// </summary>
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

    /// <summary>
    /// 驗證 request identity、deadline、operation metadata 與完整 parameter tree；失敗時不修改外部狀態或取得任何 connector resource。
    /// </summary>
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

    /// <summary>驗證獨立 result/value tree，供 response serialization 在跨 process 前套用相同資源與敏感資料邊界。</summary>
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
        // 每個 branch 先驗證 kind 對應的唯一 shape，再解析 scalar 或遞迴集合；這可阻止
        // 一個 WorkerValue 同時保留 Scalar 與 Members，造成隱藏資料跨 IPC 邊界。
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
        // Identifier 僅允許可預測的 ASCII subset，可讓 UTF-8 byte 上限與 canonical encoding
        // 保持穩定，並排除路徑、引號、空白與 shell/protocol 控制字元。
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
        // Denylist 在移除 '_'/'-' 後比較，避免以 password、pass_word 或 pass-word 等拼法
        // 把祕密/Session/路由欄位偽裝成一般 parameter 帶入 Worker。
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

    /// <summary>
    /// 單次 value-tree 驗證專用的累計狀態。它只保存 immutable limits 與兩個整數 counter，
    /// 不保存 WorkerValue、request、session、profile 或 credential；每次 public validation 都建立新實例，
    /// 因此不同要求、使用者、profile 與 process generation 之間不會共享或殘留驗證狀態。
    /// </summary>
    private sealed class ValidationState
    {
        private int _totalItems;
        private int _totalMembers;

        /// <summary>
        /// 建立 invocation-local 累計器。<paramref name="limits"/> 是 immutable policy，可安全共用；
        /// item/member 計數則從零開始且只活到本次同步驗證完成。
        /// </summary>
        /// <param name="limits">部署端固定的深度、字串與集合資源上限。</param>
        internal ValidationState(WorkerProtocolLimits limits)
        {
            // State 僅屬於一次 validation call，不進入 static/cache；因此不同要求不會共享
            // 累計 counter 或殘留先前 payload 參考。
            Limits = limits;
        }

        /// <summary>
        /// 取得本次驗證使用的 immutable protocol limits；屬性不暴露或修改累計 counter。
        /// </summary>
        internal WorkerProtocolLimits Limits { get; }

        /// <summary>
        /// 在走訪 array branch 時累加全樹 item 數。使用 checked arithmetic 防止 overflow，且總量超限
        /// 立即 fail closed，避免多個小陣列分別合法卻合計造成無界配置。
        /// </summary>
        /// <param name="count">目前 array 已先通過單一集合上限檢查的元素數。</param>
        internal void AddItems(int count)
        {
            _totalItems = checked(_totalItems + count);
            if (_totalItems > Limits.MaximumArrayItems)
            {
                throw LimitExceeded("The worker total array item limit was exceeded.");
            }
        }

        /// <summary>
        /// 在走訪 object branch 時累加全樹 member 數。counter 只屬於本次驗證；overflow 或總量超限
        /// 立即拒絕，不會把部分 object graph 放入 cache、session 或後續 IPC。
        /// </summary>
        /// <param name="count">目前 object 已先通過單一集合上限檢查的 member 數。</param>
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
