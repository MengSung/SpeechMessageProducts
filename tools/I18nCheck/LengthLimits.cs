using System.Text.Json;

namespace I18nCheck;

/// <summary>
/// 單次讀入並驗證的長度政策，只保存合成最長名稱，不含真實教會或個資。
/// 不允許透過改配置放寬核定Sender20/ChatBar14；JSON reader由本次呼叫立即Dispose。
/// </summary>
/// <param name="LongestChurchName">完整保留配置字面，長度不得從名稱描述猜測。</param>
internal sealed record LengthLimits(string LongestChurchName)
{
    /// <summary>檢查JSON重複／未知屬性、型別及固定門檻；缺檔不得跳過驗證。</summary>
    /// <param name="root">固定工作樹根。</param>
    /// <returns>最長名稱上限1024 UTF16且不得空白；無可變JSON參照。</returns>
    internal static LengthLimits Load(string root)
    {
        var path = LocalFiles.Resolve(root, "tools/I18nCheck/limits.json");
        LocalFiles.CheckFile(path);
        using var document = JsonDocument.Parse(File.ReadAllBytes(path), new JsonDocumentOptions { MaxDepth = 8 });
        try { return ReadCandidate(document.RootElement); }
        catch (InvalidOperationException exception)
        {
            // JsonDocument可先接受逃逸surrogate，直到Name/GetString才拒絕。
            // 只在此JSON解碼邊界分類為輸入錯誤，保留原InnerException且不印來源，不能擴大全域catch吞程式故障。
            throw new InvalidDataException("I18N619: limits contains invalid JSON string data.", exception);
        }
    }

    /// <summary>從尚未Dispose的JSON文件建立完整純字串候選；所有JsonElement只在此stack內，禁止逃出文件owner。</summary>
    private static LengthLimits ReadCandidate(JsonElement root)
    {
        var properties = UniqueProperties(root);
        if (properties.Count != 2 || !properties.TryGetValue("longestChurchName", out var name) || name.ValueKind != JsonValueKind.String
            || !properties.TryGetValue("limits", out var limits))
            throw new InvalidDataException("I18N606: limits schema is invalid.");
        var text = name.GetString()!;
        if (string.IsNullOrWhiteSpace(text) || text.Length > 1024 || text.Any(char.IsControl))
            throw new InvalidDataException("I18N607: longest church name is invalid.");
        var rules = UniqueProperties(limits);
        if (rules.Count != 2 || !rules.TryGetValue("Line.Sender.Template", out var sender) || sender.ValueKind != JsonValueKind.Number || !sender.TryGetInt32(out int senderLimit) || senderLimit != 20
            || !rules.TryGetValue("RichMenu.ChatBar.*", out var chatBar) || chatBar.ValueKind != JsonValueKind.Number || !chatBar.TryGetInt32(out int chatBarLimit) || chatBarLimit != 14)
            throw new InvalidDataException("I18N608: required platform limits cannot be changed.");
        return new LengthLimits(text);
    }

    /// <summary>拒絕重複屬性而非讓JSON最後值勝出；集合只存在於讀取期間且最多十六項。</summary>
    private static Dictionary<string, JsonElement> UniqueProperties(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new InvalidDataException("I18N609: limits must be an object.");
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            if (!result.TryAdd(property.Name, property.Value) || result.Count > 16)
                throw new InvalidDataException("I18N610: limits property identity is invalid or duplicated.");
        return result;
    }
}
