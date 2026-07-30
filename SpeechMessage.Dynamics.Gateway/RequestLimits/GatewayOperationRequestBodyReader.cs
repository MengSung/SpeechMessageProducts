using System.Buffers;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace SpeechMessage.Dynamics.Gateway.RequestLimits;

/// <summary>
/// 表示 bounded operation body reader 的穩定結果分類；endpoint 只依分類產生 400/413 或建立 executor request，
/// 不把 parser exception、長度、欄位名稱或 authorization metadata直接回傳給 caller。
/// </summary>
public enum GatewayOperationRequestBodyReadStatus
{
    /// <summary>Body wire bytes、JSON depth/shape 與 duplicate 規則皆通過。</summary>
    Success = 0,

    /// <summary>Declared 或實際 wire bytes 超過 deployment limit，endpoint 應回傳 413。</summary>
    PayloadTooLarge = 1,

    /// <summary>JSON 無效、超深、root shape 錯誤、unknown member 或 duplicate property，endpoint 應回傳受控 400。</summary>
    InvalidJson = 2,

    /// <summary>缺少或不符合 JSON-only 契約的 Content-Type，endpoint 應在 body I/O 前回傳受控 415。</summary>
    UnsupportedMediaType = 3
}

/// <summary>
/// 表示 operation body materialization 的不可變結果。成功時只保存 bounded request DTO 與實際 wire byte count；
/// 失敗時 request 為 null，避免 endpoint 誤用部分解析資料。結果不持有 rented buffer、request stream、HttpContext、principal、token、
/// credential、session 或 cancellation registration，reader return 前已完成所有 buffer cleanup，因此沒有額外 Dispose owner。
/// </summary>
/// <param name="Status">成功、媒體型別不支援、超限或無效 JSON 分類。</param>
/// <param name="Request">成功時的 bounded DTO；失敗時為 null。</param>
/// <param name="WireByteCount">實際讀取的 wire bytes；declared precheck 拒絕時為零。</param>
public sealed record GatewayOperationRequestBodyReadResult(
    GatewayOperationRequestBodyReadStatus Status,
    GatewayOperationHttpRequest? Request,
    int WireByteCount)
{
    /// <summary>建立成功結果；DTO 已脫離 JsonDocument 與 pooled buffer ownership。</summary>
    public static GatewayOperationRequestBodyReadResult Success(
        GatewayOperationHttpRequest request,
        int wireByteCount)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new(GatewayOperationRequestBodyReadStatus.Success, request, wireByteCount);
    }

    /// <summary>建立不含部分 body materialization 的 413 結果。</summary>
    public static GatewayOperationRequestBodyReadResult PayloadTooLarge(int wireByteCount)
        => new(GatewayOperationRequestBodyReadStatus.PayloadTooLarge, null, wireByteCount);

    /// <summary>建立不含 parser exception、欄位或部分 DTO 的受控 400 結果。</summary>
    public static GatewayOperationRequestBodyReadResult InvalidJson(int wireByteCount)
        => new(GatewayOperationRequestBodyReadStatus.InvalidJson, null, wireByteCount);

    /// <summary>
    /// 建立尚未讀取 request stream、尚未租用 pooled buffer 的 415 結果。結果不回顯原始 header，
    /// 避免把 caller-controlled 值帶入 log、exception 或 response，也沒有需要 Dispose 的資源 owner。
    /// </summary>
    public static GatewayOperationRequestBodyReadResult UnsupportedMediaType()
        => new(GatewayOperationRequestBodyReadStatus.UnsupportedMediaType, null, 0);
}

/// <summary>
/// Gateway operation JSON body 的 bounded DTO。它只包含冪等鍵與 operation registry 後續仍會驗證的 parameter map，
/// 不接受 workload、principal、CRM endpoint、credential、token 或 transport selection。Parameter value 以獨立 cloned
/// <see cref="JsonElement"/> 保存，確保 reader Dispose JsonDocument 並清零 pooled buffer 後仍可安全使用；DTO 只屬於單一 request，
/// 不可放入 singleton/cache，也沒有 stream、timer、subscription 或額外 cleanup owner。
/// </summary>
public sealed class GatewayOperationHttpRequest
{
    /// <summary>取得或設定寫入操作使用的 bounded 冪等鍵；null 表示 caller 未提供。</summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>取得或設定 caller parameters；value 為脫離 pooled buffer 的 cloned JsonElement 或 null。</summary>
    public Dictionary<string, object?>? Parameters { get; set; }
}

/// <summary>
/// 在 Gateway 完成 authentication 與 immutable principal→workload→alias→operation authorization 後，讀取一次 request body。
/// Reader 先以 fail-closed JSON-only 規則驗證 Content-Type，再檢查 Content-Length，最後以單一 <see cref="ArrayPool{T}"/>
/// buffer 最多讀取 limit+1 wire bytes；exact limit 可接受，
/// 第 limit+1 byte 立即轉為 413。它以明確 MaxDepth、root allowlist 與所有 object 的 case-insensitive duplicate 檢查 materialize DTO。
/// ASP.NET Core 是 request stream 的唯一 owner，reader 絕不 Dispose；reader 只在 Rent 到 Return 之間擁有 buffer，且所有成功、錯誤、
/// cancellation、Kestrel 413 或 I/O failure 路徑都先清零整個陣列再歸還。Singleton reader 不保存 request/principal/body/token/session，
/// 只有 immutable options 與 thread-safe pool reference，可由並行 request 無鎖共享；每次 request 最多一個 bounded rent，避免 unbounded MemoryStream/string allocation。
/// </summary>
public sealed class GatewayOperationRequestBodyReader
{
    private static readonly JsonDocumentOptions DefaultDocumentOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow
    };

    private readonly GatewayRequestBodyLimitOptions _limits;
    private readonly ArrayPool<byte> _bufferPool;

    /// <summary>
    /// 建立 stateless singleton reader；Options 已由 startup validation 限制在 hard ceiling 內，ArrayPool 由 DI/Host 擁有且必須支援並行 Rent/Return。
    /// Constructor 不租 buffer、不讀 configuration、不建立 background task 或 cancellation registration。
    /// </summary>
    /// <param name="limits">已驗證的 deployment limit snapshot。</param>
    /// <param name="bufferPool">thread-safe byte pool；reader 對每次 Rent 的 buffer 負責清零與 Return，但不 Dispose pool。</param>
    public GatewayOperationRequestBodyReader(
        IOptions<GatewayRequestBodyLimitOptions> limits,
        ArrayPool<byte> bufferPool)
    {
        ArgumentNullException.ThrowIfNull(limits);
        _limits = limits.Value;
        _limits.Validate();
        _bufferPool = bufferPool ?? throw new ArgumentNullException(nameof(bufferPool));
    }

    /// <summary>
    /// 讀取並 materialize 一個已授權 operation request body。缺少或不支援的 Content-Type 先在 Rent/Read 前回傳 415，
    /// Content-Length 超限則在 Rent/Read 前回傳 413；
    /// unknown/chunked body 只讀至 limit+1。Cancellation 保留原始 <see cref="OperationCanceledException"/> 語意並由 endpoint/Host 處理，
    /// 但 finally 仍先清零/歸還 buffer；其他 malformed/depth/unknown/duplicate/I/O failure 統一成不含細節的 InvalidJson。
    /// </summary>
    /// <param name="request">ASP.NET Core 擁有的 request 與 body stream；本方法不 Dispose 或保存 reference。</param>
    /// <param name="cancellationToken">request-aborted token；取消只中止當前 read，不建立 linked source 或 registration。</param>
    /// <returns>不持有 pooled buffer/stream 的 bounded result。</returns>
    public async ValueTask<GatewayOperationRequestBodyReadResult> ReadAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // 媒體型別是 body parsing 的信任邊界，必須在任何 stream I/O 或 ArrayPool.Rent 之前 fail closed。
        // 此順序不會形成未授權 oracle，因為 endpoint 只在 authentication 與 alias/operation authorization 成功後呼叫 reader。
        // Validator 只解析 bounded request header，不保存 header/request/principal，也不建立 cancellation registration 或其他 cleanup owner。
        if (!IsSupportedJsonContentType(request.ContentType))
        {
            return GatewayOperationRequestBodyReadResult.UnsupportedMediaType();
        }

        var declaredLength = request.ContentLength;
        if (declaredLength is < 0)
        {
            return GatewayOperationRequestBodyReadResult.InvalidJson(0);
        }

        if (declaredLength > _limits.MaxRequestBodyBytes)
        {
            return GatewayOperationRequestBodyReadResult.PayloadTooLarge(0);
        }

        byte[]? rentedBuffer = null;
        var totalBytes = 0;
        try
        {
            var readCapacity = checked(_limits.MaxRequestBodyBytes + 1);
            rentedBuffer = _bufferPool.Rent(readCapacity);
            while (totalBytes <= _limits.MaxRequestBodyBytes)
            {
                var read = await request.Body.ReadAsync(
                    rentedBuffer.AsMemory(totalBytes, readCapacity - totalBytes),
                    cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                totalBytes += read;
                if (totalBytes > _limits.MaxRequestBodyBytes)
                {
                    return GatewayOperationRequestBodyReadResult.PayloadTooLarge(totalBytes);
                }
            }

            return TryMaterialize(rentedBuffer.AsMemory(0, totalBytes), totalBytes);
        }
        catch (BadHttpRequestException exception)
            when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            // Kestrel/IIS 可能在 chunked stream 讀到 transport limit 時先拋 413；轉為相同 application result，
            // 不把 hosting-specific exception 洩漏給 endpoint，但 finally 仍負責清零/Return 已租 buffer。
            return GatewayOperationRequestBodyReadResult.PayloadTooLarge(totalBytes);
        }
        catch (JsonException)
        {
            return GatewayOperationRequestBodyReadResult.InvalidJson(totalBytes);
        }
        catch (IOException)
        {
            return GatewayOperationRequestBodyReadResult.InvalidJson(totalBytes);
        }
        finally
        {
            if (rentedBuffer is not null)
            {
                // 必須清零整個實際 array（pool 可能回傳大於 requested capacity），才能防止下一個租用者觀察 body/token-like content；
                // reader 不依賴 Return(clearArray) 的 pool-specific 行為，並在所有 return/throw/cancel 路徑執行相同 cleanup。
                CryptographicOperations.ZeroMemory(rentedBuffer.AsSpan());
                _bufferPool.Return(rentedBuffer, clearArray: false);
            }
        }
    }

    /// <summary>
    /// 驗證 operation endpoint 的封閉 JSON 媒體型別契約。只接受不分大小寫的 <c>application/json</c>，
    /// 並允許省略參數或只宣告一個 UTF-8 charset；structured suffix、未知參數、重複參數、非 UTF-8 charset、
    /// 缺漏與無法解析的 header 全部拒絕。方法是無 I/O、無共享 mutable state 的純驗證，不租 buffer、不建立 timer、
    /// cache、subscription 或 background work；解析物件只存在於目前 stack frame，回傳後即可由 GC 回收。
    /// </summary>
    /// <param name="contentType">ASP.NET Core 已界定 header 長度的 Content-Type 字串；不得寫入 log 或 exception。</param>
    /// <returns>只有完全符合 Gateway JSON-only 契約時才為 true。</returns>
    private static bool IsSupportedJsonContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType) ||
            !MediaTypeHeaderValue.TryParse(contentType, out var parsed) ||
            !string.Equals(parsed.MediaType, "application/json", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var parameterCount = 0;
        foreach (var parameter in parsed.Parameters)
        {
            parameterCount++;
            if (parameterCount > 1 ||
                !string.Equals(parameter.Name, "charset", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (parameterCount == 0)
        {
            return true;
        }

        var charset = parsed.CharSet?.Trim().Trim('"');
        return string.Equals(charset, "utf-8", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 以明確 MaxDepth 解析 bounded UTF-8 bytes，拒絕所有 object 的 case-insensitive duplicate property，
    /// 並只允許 root 的 idempotencyKey/parameters。JsonDocument 在方法結束前 Dispose；所有 parameter JsonElement 都先 Clone，
    /// 所以 caller 不會引用即將清零的 pooled buffer。方法不記錄 JSON 或 exception details，避免敏感 body 進入長生命週期 log。
    /// </summary>
    private GatewayOperationRequestBodyReadResult TryMaterialize(
        ReadOnlyMemory<byte> utf8Json,
        int wireByteCount)
    {
        var documentOptions = DefaultDocumentOptions;
        documentOptions.MaxDepth = _limits.JsonMaxDepth;
        using var document = JsonDocument.Parse(utf8Json, documentOptions);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !HasNoDuplicateProperties(document.RootElement))
        {
            return GatewayOperationRequestBodyReadResult.InvalidJson(wireByteCount);
        }

        string? idempotencyKey = null;
        Dictionary<string, object?>? parameters = null;
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (property.Name.Equals("idempotencyKey", StringComparison.OrdinalIgnoreCase))
            {
                if (property.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                {
                    return GatewayOperationRequestBodyReadResult.InvalidJson(wireByteCount);
                }

                idempotencyKey = property.Value.ValueKind == JsonValueKind.Null
                    ? null
                    : property.Value.GetString();
                continue;
            }

            if (property.Name.Equals("parameters", StringComparison.OrdinalIgnoreCase))
            {
                if (property.Value.ValueKind == JsonValueKind.Null)
                {
                    parameters = null;
                    continue;
                }

                if (property.Value.ValueKind != JsonValueKind.Object)
                {
                    return GatewayOperationRequestBodyReadResult.InvalidJson(wireByteCount);
                }

                parameters = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var parameter in property.Value.EnumerateObject())
                {
                    parameters.Add(
                        parameter.Name,
                        parameter.Value.ValueKind == JsonValueKind.Null
                            ? null
                            : parameter.Value.Clone());
                }

                continue;
            }

            return GatewayOperationRequestBodyReadResult.InvalidJson(wireByteCount);
        }

        return GatewayOperationRequestBodyReadResult.Success(
            new GatewayOperationHttpRequest
            {
                IdempotencyKey = idempotencyKey,
                Parameters = parameters
            },
            wireByteCount);
    }

    /// <summary>
    /// 深度優先檢查每個 JSON object 的 property name 是否 case-insensitive 唯一；array 只遞迴其元素。
    /// 每個 object 建立一個短生命週期 HashSet，總配置受 body byte ceiling 與 JsonMaxDepth 雙重限制；
    /// 不使用 static/mutable cache，避免不同 request/tenant 的 property name 被跨界保留。false 只代表 controlled 400，不回傳衝突名稱。
    /// </summary>
    private static bool HasNoDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name) || !HasNoDuplicateProperties(property.Value))
                {
                    return false;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (!HasNoDuplicateProperties(item))
                {
                    return false;
                }
            }
        }

        return true;
    }
}
