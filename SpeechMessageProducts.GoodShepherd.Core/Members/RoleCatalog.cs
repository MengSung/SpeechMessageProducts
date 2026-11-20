/*
檔案整體說明

目的與定位：提供 GoodShepherd.Core 會籍角色的 metadata 解析、固定字典鍵及 lookup 投影，
讓 ChurchReport 和後續設定頁共用穩定代碼，而不依賴中文顯示名稱作權限判斷。

主要功能與流程：呼叫端將 D365 contact.customertypecode metadata 的 value/label 候選交給
建構式；catalog 從 I18n 生成資源表取得預設標籤，只以唯一、精確 label 對應已核定角色，
保留完整 metadata 清單，重複 option
value 會拒絕整份候選，同名多值則保留資料但不賦予角色代碼。DisplayName 透過 ITextCatalog 解析
Role.*；BuildItems 保留 D365 label 為 value，字典文字只放在 text，未知選項 code 為空。
沒有完整伺服器授權 TextContext 時，呼叫端必須傳 null 並以原始 label 顯示。

使用邊界與維護：catalog 只持有複製後的純 metadata 與固定角色定義；一份由生成資源建立、
大小固定為 11 筆的唯讀預設名稱索引隨組件存活，不保存 request、Session、使用者、教會、
SDK Entity、連線、憑證或可變來源集合。沒有 I/O、timer、背景工作或 disposable owner。
OptionSet metadata 由呼叫端的短生命週期 SDK owner 取得並釋放。
未知／未對應選項不取得角色代碼或權限；教會覆寫只由完整 TextContext 導引，不能由 request
提供教會 ID。

失敗處理與驗證：空白標籤及重複整數拒絕整份候選，不發布部分表；同名多值逐筆保留，
但不映射成角色，避免依標籤覆蓋合法 option 身分。字典缺鍵或 scope 錯誤由 ITextCatalog
原樣拋出，不以別教會資料或猜測值補足。雙目標測試
驗證 metadata 黃金對照、未知選項、角色顯示覆寫與 D365 label 存檔投影。
*/
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Resources;

namespace SpeechMessageProducts.GoodShepherd.Core.Members;

/// <summary>單一 D365 OptionSet metadata 選項的純值副本，不含來源 Entity 或其他組織資料。</summary>
public sealed class RoleMetadataOption
{
    /// <summary>D365 提供的 option value；不是角色代碼。</summary>
    public int Value { get; }
    /// <summary>D365 提供的語系 1028 標籤。</summary>
    public string Label { get; }

    /// <summary>建立已複製的 option metadata 值。</summary>
    /// <param name="value">由 D365 metadata 取得的 option value。</param>
    /// <param name="label">同一 metadata option 的語系 1028 標籤。</param>
    public RoleMetadataOption(int value, string label)
    {
        if (string.IsNullOrWhiteSpace(label))
            throw new ArgumentException("Role metadata label cannot be empty.", nameof(label));
        Value = value;
        Label = label;
    }
}

/// <summary>供舊表單相容消費的純 lookup 項目；value 永遠是 D365 標籤而不是顯示名稱。</summary>
public sealed class RoleListItem
{
    /// <summary>原始 D365 option label，保持既有表單存檔契約。</summary>
    public string Value { get; }
    /// <summary>依本次完整授權範圍解析的角色呈現文字。</summary>
    public string Text { get; }
    /// <summary>穩定 Role.* 字典鍵；未知選項為空。</summary>
    public string Code { get; }

    /// <summary>建立與單次 API 回應脫離的純值項目。</summary>
    /// <param name="value">D365 option label。</param>
    /// <param name="text">顯示文字。</param>
    /// <param name="code">穩定角色資源鍵或空字串。</param>
    public RoleListItem(string value, string text, string code)
    {
        Value = value ?? throw new ArgumentNullException(nameof(value));
        Text = text ?? throw new ArgumentNullException(nameof(text));
        Code = code ?? throw new ArgumentNullException(nameof(code));
    }
}

/// <summary>以完整 D365 metadata 建立的不可變角色目錄，未知選項不會取得角色語意。</summary>
public sealed class RoleCatalog
{
    private static readonly RoleDefinition[] Definitions =
    {
        new(RoleCode.PastorCouple),
        new(RoleCode.ZoneShepherd),
        new(RoleCode.ZoneLeader),
        new(RoleCode.GroupLeader),
        new(RoleCode.AssistantGroupLeader),
        new(RoleCode.CoreWorker),
        new(RoleCode.Member),
        new(RoleCode.NotInGroup),
        new(RoleCode.Newcomer),
        new(RoleCode.OtherChurch),
        new(RoleCode.Closed)
    };

    private static readonly ReadOnlyDictionary<RoleCode, string> DefaultLabels = CreateDefaultLabels();
    private readonly ReadOnlyCollection<RoleMetadataOption> metadataOptions;
    private readonly ReadOnlyDictionary<int, RoleCode> codesByValue;
    private readonly ReadOnlyDictionary<RoleCode, int> valuesByCode;

    /// <summary>本目錄持有的唯讀 metadata 副本；僅含 option value 與語系 1028 label。</summary>
    public IReadOnlyList<RoleMetadataOption> MetadataOptions => metadataOptions;

    /// <summary>只依呼叫端提供的 D365 metadata 建立角色對照，不以未知整數或相似標籤猜測身份。</summary>
    /// <param name="options">單次完整 metadata 候選；必須為唯讀來源的純值副本。</param>
    public RoleCatalog(IEnumerable<RoleMetadataOption> options)
    {
#if NET10_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(options);
#else
        if (options is null) throw new ArgumentNullException(nameof(options));
#endif

        var candidate = options.ToArray();
        var values = new Dictionary<int, RoleCode>();
        var roleValues = new Dictionary<RoleCode, int>();
        var seenValues = new HashSet<int>();
        foreach (var option in candidate)
        {
            if (option is null || !seenValues.Add(option.Value))
                throw new InvalidOperationException("Role metadata option values must be unique and non-null.");
        }

        var labelCounts = candidate.GroupBy(item => item.Label, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        foreach (var option in candidate)
        {
            if (labelCounts[option.Label] != 1) continue;
            var definition = Definitions.FirstOrDefault(item =>
                string.Equals(DefaultLabels[item.Code], option.Label, StringComparison.Ordinal));
            if (definition is null) continue;
            values.Add(option.Value, definition.Code);
            roleValues.Add(definition.Code, option.Value);
        }

        metadataOptions = new ReadOnlyCollection<RoleMetadataOption>(
            candidate.Select(item => new RoleMetadataOption(item.Value, item.Label)).ToArray());
        codesByValue = new ReadOnlyDictionary<int, RoleCode>(values);
        valuesByCode = new ReadOnlyDictionary<RoleCode, int>(roleValues);
    }

    /// <summary>未知或未對應的整數回傳 false 與 null，不提供任何權限身份。</summary>
    /// <param name="optionValue">D365 option value。</param>
    /// <param name="roleCode">對應穩定代碼；失敗時為 null。</param>
    /// <returns>metadata 精確對應已知角色時為 true。</returns>
    public bool TryGetCode(int optionValue, out RoleCode? roleCode)
    {
        if (codesByValue.TryGetValue(optionValue, out var code))
        {
            roleCode = code;
            return true;
        }
        roleCode = null;
        return false;
    }

    /// <summary>取得角色在目前 metadata 快照中的 option value；未對應角色保持空值。</summary>
    /// <param name="roleCode">固定角色代碼。</param>
    /// <returns>本次 metadata 含有精確標籤時的 option value，否則為 null。</returns>
    public int? GetOptionValue(RoleCode roleCode) =>
        valuesByCode.TryGetValue(roleCode, out var value) ? value : (int?)null;

    /// <summary>依角色資源鍵解析呈現文字；本方法不自行建立或授權 TextContext。</summary>
    /// <param name="roleCode">固定角色代碼。</param>
    /// <param name="context">由呼叫端伺服器授權後建立的完整文字範圍。</param>
    /// <param name="catalog">Host 擁有的產品文字目錄。</param>
    /// <returns>該 scope 的角色文字。</returns>
    public static string DisplayName(RoleCode roleCode, TextContext context, ITextCatalog catalog)
    {
#if NET10_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(catalog);
#else
        if (context is null) throw new ArgumentNullException(nameof(context));
        if (catalog is null) throw new ArgumentNullException(nameof(catalog));
#endif
#if NET10_0_OR_GREATER
        if (!Enum.IsDefined(roleCode))
#else
        if (!Enum.IsDefined(typeof(RoleCode), roleCode))
#endif
            throw new ArgumentOutOfRangeException(nameof(roleCode));
        return catalog.GetText(context, new TextKey("Role." + roleCode));
    }

    /// <summary>建立單次 API 回應投影；D365 標籤保留為 value，代碼和顯示文字分開輸出。</summary>
    /// <param name="context">伺服器已確認教會範圍時的文字 context；無範圍時傳 null。</param>
    /// <param name="catalog">可用文字目錄；無有效 context 時不呼叫。</param>
    /// <returns>每個 metadata option 對應一筆、沒有跨請求共享的唯讀候選。</returns>
    public IReadOnlyList<RoleListItem> BuildItems(TextContext? context, ITextCatalog? catalog)
    {
        var items = new List<RoleListItem>(metadataOptions.Count);
        foreach (var option in metadataOptions)
        {
            if (!TryGetCode(option.Value, out var roleCode) || !roleCode.HasValue)
            {
                items.Add(new RoleListItem(option.Label, option.Label, string.Empty));
                continue;
            }

            var definition = Definitions.First(item => item.Code == roleCode.Value);
            var text = context is not null && catalog is not null
                ? DisplayName(roleCode.Value, context, catalog)
                : option.Label;
            items.Add(new RoleListItem(option.Label, text, "Role." + definition.Code));
        }
        return new ReadOnlyCollection<RoleListItem>(items);
    }

    /// <summary>建立資料來源失效時的顯示備援；沒有 metadata 就不提供角色代碼或授權身份。</summary>
    /// <param name="context">伺服器已確認教會範圍時的文字 context；無範圍時傳 null。</param>
    /// <param name="catalog">可用文字目錄；無有效 context 時不呼叫。</param>
    /// <returns>固定 11 個生成資源標籤的唯讀顯示項目；每筆 code 都是空字串。</returns>
    public static IReadOnlyList<RoleListItem> BuildFallbackItems(TextContext? context, ITextCatalog? catalog)
    {
        var items = new List<RoleListItem>(Definitions.Length);
        foreach (var definition in Definitions)
        {
            var key = new TextKey("Role." + definition.Code);
            var text = context is not null && catalog is not null
                ? catalog.GetText(context, key)
                : DefaultLabels[definition.Code];
            items.Add(new RoleListItem(DefaultLabels[definition.Code], text, string.Empty));
        }
        return new ReadOnlyCollection<RoleListItem>(items);
    }

    /// <summary>從同組件的 I18n 生成資料複製固定角色預設名稱，不在程式碼維護第二份文案。</summary>
    /// <returns>恰含每個穩定角色代碼一筆的唯讀繁中預設名稱。</returns>
    private static ReadOnlyDictionary<RoleCode, string> CreateDefaultLabels()
    {
        var generatedLabels = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in GoodShepherdTextData.Rows)
        {
            if (row.Length <= 1 || string.IsNullOrWhiteSpace(row[0])
                || !row[0]!.StartsWith("Role.", StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(row[1]))
                continue;
            generatedLabels.Add(row[0]!, row[1]!);
        }
        var labels = Definitions.ToDictionary(definition => definition.Code, definition =>
        {
            var key = "Role." + definition.Code;
            if (!generatedLabels.TryGetValue(key, out var label) || string.IsNullOrWhiteSpace(label))
                throw new InvalidOperationException("Generated role resource is missing or empty.");
            return label;
        });
        return new ReadOnlyDictionary<RoleCode, string>(labels);
    }

    private sealed class RoleDefinition
    {
        internal RoleCode Code { get; }
        internal RoleDefinition(RoleCode code) { Code = code; }
    }
}
