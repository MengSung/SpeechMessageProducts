/*
檔案整體說明
目的與定位：本檔屬於 `SpeechMessage.Dynamics.Deployment` 的離線契約工具，只驗證單一 Workflow 活動
由打包 DLL `[Input]` metadata 讀出的身份與情境 JSON 輸入；本檔不建立或產生 Workflow XAML。
主要流程：呼叫端提供 `ActivityInputContractReader` 解析的活動契約及 strict scenario reader
提供的輸入 map；本 validator 核對固定 Workflows 組件名稱、版本與強式名稱 token，再以有界數量、
序數名稱、JSON 種類及 CLR 型別規則拒絕未知輸入、缺少必要輸入或型別不符，並可格式化活動的
assembly-qualified identity。驗證成功只證明此輸入子契約符合 metadata，不代表完整樣板或平台可執行。
信任邊界與不變條件：活動契約須來自受控打包 DLL；本類別不載入或執行活動程式碼、不呼叫 SDK、
不連接 Dataverse、不讀取記錄、不解析秘密，也不把呼叫端資料視為組織或租戶授權。
資源與資料生命週期：輸入 map 與其中的 `JsonElement` 由呼叫端持有，本類別同步檢查後立即返回，
不複製或保留參考、不建立快取、背景工作、連線或需 Dispose 的資源；跨請求與跨組織沒有共享狀態。
維護要求：呼叫端仍須由 server-side manifest／catalog 決定活動及組織範圍；驗證通過不可視為
使用者授權或 Dataverse 寫入核准。新增 JSON／CLR 型別映射前，須補精確型別測試；複雜型別須先定義
獨立的安全序列化與完整 XAML 生成契約，不可把本 validator 誤當成樣板 builder。
失敗處理與驗證：輸入超量、metadata 名稱無效、輸入未知、缺少必要輸入或 JSON 種類／CLR 型別不符時，
以固定 `InvalidDataException` 拒絕，例外不包含參數值或活動資料。離線測試使用合成 PE／JSON 驗證拒絕
及身份格式；它不驗證 XAML shape、流程執行或 Dataverse 實機行為。
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SpeechMessage.Dynamics.Deployment.Live;

/// <summary>依打包 Workflow 活動 metadata 驗證情境 JSON 輸入及活動身份，不產生 XAML。</summary>
internal static class ActivityInputContractValidator
{
    private const int MaximumInputCount = 128;
    private const string WorkflowsAssemblyName = "SpeechMessage.Dynamics.Workflows";
    private static readonly Version WorkflowsAssemblyVersion = new(1, 0, 0, 0);

    /// <summary>
    /// 以部署 DLL 的強式名稱契約格式化活動的完整組件限定身份。
    /// 不接受弱名稱、其他組件、非核定版本或不完整 token，避免平台載入不同活動組件。
    /// </summary>
    /// <param name="contract">從受控 Workflow DLL PE metadata 讀出的活動契約。</param>
    /// <returns>型別、組件、固定版本、neutral culture 及強式名稱 token 組成的完整身份。</returns>
    /// <exception cref="InvalidDataException">活動或組件身份不完整，或不符合核定新版組件契約。</exception>
    internal static string AssemblyQualifiedActivityName(WorkflowActivityContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        var token = contract.PublicKeyToken;
        var activityType = contract.ActivityType;
        if (!string.Equals(contract.AssemblyName, WorkflowsAssemblyName, StringComparison.Ordinal) ||
            contract.Version != WorkflowsAssemblyVersion ||
            string.IsNullOrWhiteSpace(activityType) ||
            string.IsNullOrWhiteSpace(token))
            throw Invalid();
        if (activityType.Length > 512 ||
            !string.Equals(activityType, activityType.Trim(), StringComparison.Ordinal) ||
            activityType.Contains(',', StringComparison.Ordinal) || activityType.Any(char.IsControl) ||
            token.Length != 16 || !token.All(char.IsAsciiHexDigit))
            throw Invalid();

        return activityType + ", " + WorkflowsAssemblyName +
            ", Version=1.0.0.0, Culture=neutral, PublicKeyToken=" + token.ToLowerInvariant();
    }

    /// <summary>
    /// 確認情境只提供 metadata 宣告的輸入、包含所有必要輸入，且每個 JSON 值與 CLR 引數型別相符。
    /// 僅驗證不可變契約，不複製 map、不讀取 seed 資料，也不保留任何值供後續命令使用。
    /// </summary>
    /// <param name="contract">由受控部署 PE metadata reader 產生的單一活動契約。</param>
    /// <param name="inputs">strict scenario reader 提供的本案例輸入；值由該案例 owner 持有。</param>
    /// <exception cref="InvalidDataException">輸入未知、重複、缺少必要欄位、型別不符或超出有界數量。</exception>
    internal static void ValidateInputs(
        WorkflowActivityContract contract,
        IReadOnlyDictionary<string, JsonElement> inputs)
    {
        ArgumentNullException.ThrowIfNull(contract);
        _ = AssemblyQualifiedActivityName(contract);
        ArgumentNullException.ThrowIfNull(contract.Inputs);
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.Count > MaximumInputCount || contract.Inputs.Count > MaximumInputCount)
            throw Invalid();

        foreach (var metadataInput in contract.Inputs)
        {
            var definition = metadataInput.Value;
            if (definition is null || string.IsNullOrWhiteSpace(metadataInput.Key) ||
                !string.Equals(metadataInput.Key, definition.Name, StringComparison.Ordinal) ||
                !string.Equals(definition.Name, definition.Name.Trim(), StringComparison.Ordinal))
                throw Invalid();
        }

        foreach (var input in inputs)
        {
            if (!contract.Inputs.TryGetValue(input.Key, out var definition) ||
                !string.Equals(input.Key, definition.Name, StringComparison.Ordinal) ||
                !MatchesJsonKind(input.Value, definition.ValueType))
                throw Invalid();
        }

        foreach (var definition in contract.Inputs.Values)
            if (definition.Required && !inputs.ContainsKey(definition.Name))
                throw Invalid();
    }

    /// <summary>只接受明確支援的純量 JSON 表示，不猜測 complex SDK 型別或做隱式轉換。</summary>
    private static bool MatchesJsonKind(JsonElement value, string valueType) => valueType switch
    {
        "System.String" => value.ValueKind == JsonValueKind.String,
        "System.Boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "System.Byte" => value.ValueKind == JsonValueKind.Number && value.TryGetByte(out _),
        "System.Int16" => value.ValueKind == JsonValueKind.Number && value.TryGetInt16(out _),
        "System.Int32" => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out _),
        "System.Int64" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
        "System.Single" => value.ValueKind == JsonValueKind.Number && value.TryGetSingle(out _),
        "System.Double" => value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out _),
        "System.Decimal" => value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out _),
        "System.Guid" => value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out _),
        _ => false
    };

    /// <summary>固定錯誤不包含活動身份、參數名稱、JSON 值或組織資料。</summary>
    private static InvalidDataException Invalid() =>
        new("Workflow activity inputs do not match the packaged metadata contract.");
}
