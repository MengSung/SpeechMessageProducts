/*
檔案整體說明
目的與定位：解析平台 Plugin 部署的完整 unsecureConfig，建立精確組織名稱到三產品字串的不可變白名單；不接受 Target、使用者或組織名稱推測產品。
主要功能與流程：建構時以有界完整 JSON 解析，拒絕重複屬性、額外根欄位、非字串產品與未知產品；複製到私有唯讀映射。Execute 只以平台 OrganizationName 精確 ordinal 查詢，查不到即固定錯誤拒絕。
使用邊界與維護：配置不得含憑證，最多十六 KiB、六十四組映射，生命期與可共用 Plugin 相同；不保存 SDK、context、租戶或請求結果，沒有背景工作或 Dispose 資源。設定更新需由平台重新建立 Plugin；離線測試必須保護完整 JSON 而非正規表示式片段匹配。
*/
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using LineMessagingProcessor.Core.ChannelPool;
using Microsoft.Xrm.Sdk;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
namespace SpeechMessage.Dynamics.Platform.Plugins;

/// <summary>僅供兩個核定 Plugin 入口內部使用；只保留部署純值的不可變組織映射；組織權威只能取自平台 context，不能來自 caller 欄位。</summary>
internal sealed class PluginProductMap
{
    private readonly ReadOnlyDictionary<string, string> products;
    /// <summary>完整解析配置並拒絕歧義；JSON 錯誤保留真實 InnerException，但對外訊息固定且不含設定原文。</summary>
    public PluginProductMap(string unsecureConfig)
    {
        if (string.IsNullOrWhiteSpace(unsecureConfig) || unsecureConfig.Length > 16384)
            throw new InvalidPluginExecutionException("F3 invalid product configuration.");
        JObject root;
        try { root = JObject.Parse(unsecureConfig, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error }); }
        catch (JsonException exception) { throw new InvalidPluginExecutionException("F3 invalid product configuration.", exception); }
        var values = root["products"] as JObject;
        if (root.Count != 1 || values == null || values.Count == 0 || values.Count > 64)
            throw new InvalidPluginExecutionException("F3 invalid product mapping.");
        var candidate = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in values.Properties())
        {
            if (property.Name.Length == 0 || property.Name.Length > 100 || !string.Equals(property.Name, property.Name.Trim(), StringComparison.Ordinal) ||
                property.Value.Type != JTokenType.String || !PoolProduct.IsSupported((string?)property.Value))
                throw new InvalidPluginExecutionException("F3 invalid product mapping.");
            candidate.Add(property.Name, (string)property.Value!);
        }
        products = new ReadOnlyDictionary<string, string>(candidate);
    }
    /// <summary>查詢平台提供的精確組織名稱；未知或大小寫不符均拒絕，沒有預設或 caller 產品後備值。</summary>
    public string Resolve(string organizationName)
    {
        if (organizationName == null || !products.TryGetValue(organizationName, out var product))
            throw new InvalidPluginExecutionException("F3 organization is not configured.");
        return product;
    }
}
