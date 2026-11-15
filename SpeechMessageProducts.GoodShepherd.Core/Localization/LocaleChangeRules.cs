/*
 * 檔案整體說明
 *
 * 目的與定位：提供 P5-04 ChurchLocaleChangedPlugin 共用的教會語系設定驗證與
 * RichMenuGenerate 待辦來源識別，供 net462 Plugin 與 net10.0 主機採相同純值契約。
 *
 * 主要功能與流程：輸入四個設定字串，先限制大小，再對照 LocalizationOptions 的
 * 預設發布政策、拒絕重複語言並依 Ordinal 排序，確認預設語言在啟用清單內；
 * 透過 ChurchClock 的既有 CLDR／作業系統解析驗證 IANA 時區，國碼正規化為兩個
 * ASCII 大寫字母。全部成功才發布不可變結果，無效設定只丟固定資源鍵而不帶原值。
 *
 * 使用邊界與維護：呼叫端先取得伺服器授權的教會與設定；本檔不執行授權或資料寫入。
 * 輸入與候選僅存活於單次同步呼叫，輸出由呼叫端持有；沒有教會快取、Session、
 * 背景工作、連線或待 Dispose 資源，也沒有取消、逾時或 drain 所有權。共享的發布
 * 政策只含固定不可變語言。SourceId 透過 DeterministicGuid 分隔用途、教會與全部
 * 設定，資料庫原子唯一性仍由外殼保證。修改須重跑雙宿主的離線規則與位元組檢查。
 */
using System;
using System.Collections.Generic;
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Rules;
namespace SpeechMessageProducts.GoodShepherd.Core.Localization;
/// <summary>
/// 完整驗證後的不可變語系設定快照，只含四個字串，不保存教會實體、授權或外部資源。
/// 呼叫端決定保存期間；快照本身不授予教會資料權限，也不負責建立待辦。
/// </summary>
public sealed class ValidatedLocaleSettings
{
    /// <summary>由規則一次發布完整候選；參數均為已驗證純字串，不保留可變集合。</summary>
    /// <param name="defaultLanguage">已發布且在啟用清單內的標準語言名稱。</param>
    /// <param name="enabledLanguages">依 Ordinal 排序、無重複的逗號分隔語言。</param>
    /// <param name="timeZone">通過共用映射與本機規則解析的 IANA 識別。</param>
    /// <param name="countryCode">兩個 ASCII 大寫字母。</param>
    internal ValidatedLocaleSettings(string defaultLanguage, string enabledLanguages, string timeZone, string countryCode)
    {
        DefaultLanguage = defaultLanguage;
        EnabledLanguages = enabledLanguages;
        TimeZone = timeZone;
        CountryCode = countryCode;
    }
    /// <summary>依發布政策採標準大小寫的預設語言，必定存在於啟用清單。</summary>
    public string DefaultLanguage { get; }
    /// <summary>依 Ordinal 排序的標準語言名稱，以逗號連接且無空值或重複項。</summary>
    public string EnabledLanguages { get; }
    /// <summary>保留共用映射要求之精確大小寫的 IANA 時區，不回落到伺服器時區。</summary>
    public string TimeZone { get; }
    /// <summary>兩個 ASCII 大寫字母的國碼；此格式驗證不宣稱核對國家存在與否。</summary>
    public string CountryCode { get; }
}
/// <summary>
/// 同步驗證設定並建立分教會的穩定來源鍵；不記住任何呼叫輸入，不建立外部資源。
/// 固定發布政策與共用時區規則不含使用者狀態，允許不同教會並行呼叫。
/// </summary>
public static class LocaleChangeRules
{
    /// <summary>固定預設發布政策，僅含不可變公共語言清單；不允許各請求自行開放英文或偽語系。</summary>
    private static readonly LocalizationOptions ReleasedPolicy = new LocalizationOptions();
    /// <summary>
    /// 驗證有界設定並一次發布結果；語言忽略大小寫與首尾空白，重複項明確拒絕。
    /// 時區沿用嚴格 IANA 契約，不修剪或猜測；所有預期設定錯誤使用固定資源鍵。
    /// </summary>
    /// <param name="defaultLanguage">教會預設語言，最多 128 字元。</param>
    /// <param name="enabledLanguages">最多 64 個、總長至多 8192 的逗號分隔語言。</param>
    /// <param name="timeZone">共用映射支援且可解析的精確 IANA 時區。</param>
    /// <param name="countryCode">兩個 ASCII 字母，輸出統一大寫，不接受空白。</param>
    /// <returns>全部驗證成功後的不可變設定；失敗不發布部分結果。</returns>
    /// <exception cref="BusinessRuleException">輸入不符合發布、集合、時區或國碼契約。</exception>
    public static ValidatedLocaleSettings Validate(string defaultLanguage, string enabledLanguages, string timeZone, string countryCode)
    {
        if (string.IsNullOrWhiteSpace(enabledLanguages) || enabledLanguages.Length > 8192)
            throw InvalidSettings();
        var defaultName = ReleasedLanguage(defaultLanguage);
        var parts = enabledLanguages.Split(',');
        if (parts.Length > 64) throw InvalidSettings();
        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var part in parts)
            if (!names.Add(ReleasedLanguage(part))) throw InvalidSettings();
        if (!names.Contains(defaultName)) throw InvalidSettings();
        // 先驗 ASCII 再轉大寫，避免 Unicode 大小寫映射把非 ASCII 輸入變為合法國碼。
        if (countryCode is null || countryCode.Length != 2
            || !IsAsciiLetter(countryCode[0]) || !IsAsciiLetter(countryCode[1]))
            throw InvalidSettings();
        try
        {
            // 固定 UTC 瞬間只為觸發既有公共映射與宿主解析；不讀取系統現在時間。
            _ = new ChurchClock().ToChurchTime(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), timeZone);
        }
        catch (ArgumentException) { throw InvalidSettings(); }
        catch (TimeZoneNotFoundException) { throw InvalidSettings(); }
        catch (InvalidTimeZoneException) { throw InvalidSettings(); }
        return new ValidatedLocaleSettings(defaultName, string.Join(",", names), timeZone, countryCode.ToUpperInvariant());
    }
    /// <summary>
    /// 以固定 RichMenuGenerate 用途、正式教會識別及全部標準設定建立穩定來源鍵。
    /// 相同設定重算不改鍵；此為純識別契約，不能代替資料庫唯一鍵或外殼的原子建立。
    /// </summary>
    /// <param name="churchId">呼叫端先經伺服器授權的非空正式教會識別。</param>
    /// <param name="settings">本規則驗證完成的不可變設定。</param>
    /// <returns>跨產品目標一致且分教會的待辦來源識別。</returns>
    /// <exception cref="BusinessRuleException">教會識別為空或設定缺少。</exception>
    public static Guid SourceId(Guid churchId, ValidatedLocaleSettings settings)
    {
        if (churchId == Guid.Empty || settings is null) throw InvalidSettings();
        return DeterministicGuid.Create("RichMenuGenerate", churchId, settings.DefaultLanguage,
            settings.EnabledLanguages, settings.TimeZone, settings.CountryCode);
    }
    /// <summary>只在固定發布清單中比對，回傳政策持有的標準名稱，不建立依輸入增長的文化快取。</summary>
    /// <param name="value">單次呼叫持有的語言候選，禁止空值及超過 128 字元。</param>
    /// <returns>既有政策的標準名稱，未發布或格式不符則固定錯誤拒絕。</returns>
    private static string ReleasedLanguage(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128) throw InvalidSettings();
        var candidate = value.Trim();
        foreach (var released in ReleasedPolicy.ReleasedCultures)
            if (string.Equals(candidate, released, StringComparison.OrdinalIgnoreCase)) return released;
        throw InvalidSettings();
    }
    /// <summary>以字元範圍判斷 ASCII 字母，避免文化或 Unicode 類別擴張國碼集合。</summary>
    private static bool IsAsciiLetter(char value) => value is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
    /// <summary>每次建立獨立固定業務錯誤，不帶原始值、內層例外或任何可變參數。</summary>
    private static BusinessRuleException InvalidSettings() => new BusinessRuleException(new TextKey("Common.Plugin.InvalidConfiguration"));
}
