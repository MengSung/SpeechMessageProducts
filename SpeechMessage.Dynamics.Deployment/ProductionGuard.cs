/*
檔案整體說明
目的與定位：部署CLI在任何命令分流及連線之前執行的正式組織關卡，不把白名單或plan雜湊當授權。
主要流程：讀唯一org與production旗標，查catalog並拒絕測試專用命令，正式寫入再讀一行名稱確認。
資源與隔離：輸入reader由命令owner借入，僅消費確認行；最多256字元，真正stdin沿既有
ChannelSecretInput的30秒總deadline與確定性釋放，不建立背景Task或保留文字到下一個命令。
輸出只有本次不可變組織描述及移除已驗旗標的參數候選；不讀User Secrets、不連SDK、不輸出輸入。
驗證方式：OrganizationsTests用合成組織／有限StringReader驗旗標、命令表及錯配、共用Channel輸入順序。
*/
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
namespace SpeechMessage.Dynamics.Deployment;

/// <summary>只屬本次命令的授權結果，參數不含名稱確認或秘密，不能跨命令快取。</summary>
internal sealed record OrganizationAuthorization(OrganizationDefinition Organization, string[] Arguments);

/// <summary>純授權關卡，在connector之前拒絕所有不相容目標與旗標。</summary>
internal static class ProductionGuard
{
    /// <summary>核對白名單與正式旗標、受限命令及寫入確認；reader由caller擁有，不在此Dispose。</summary>
    internal static OrganizationAuthorization Authorize(string[] args, OrganizationCatalog catalog, TextReader? input, CancellationToken ct)
    {
        var authorization = Inspect(args, catalog, ct);
        if (authorization.Organization.Kind == "production" && IsWrite(args)) Confirm(authorization.Organization.Name, input, ct);
        return authorization;
    }
    /// <summary>先完整驗旗標與目標且不讀stdin；caller確認需正式寫入才建立唯一的有界reader。</summary>
    internal static OrganizationAuthorization Inspect(string[] args, OrganizationCatalog catalog, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (args.Length == 0) throw Invalid();
        var stripped = new List<string>(args.Length); string? name = null; var production = false;
        for (var index = 0; index < args.Length; index++)
        {
            var value = args[index];
            if (value == "--production") { if (production) throw Invalid(); production = true; continue; }
            if (value == "--org")
            {
                if (name is not null || index + 1 == args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal)) throw Invalid();
                name = args[index + 1];
            }
            stripped.Add(value);
        }
        var organization = catalog.Get(name ?? "");
        if ((organization.Kind == "production") != production) throw Invalid();
        if (production)
        {
            if (args[0] is "smoke" or "shadow" or "live-check" or "ui-export" ||
                args[0] == "switch" && args.Contains("--module", StringComparer.Ordinal)) throw Invalid();
        }
        var normalized = stripped.ToArray();
        for (var index = 0; index < normalized.Length - 1; index++)
            if (normalized[index] == "--org") normalized[index + 1] = organization.Name;
        return new OrganizationAuthorization(organization, normalized);
    }
    /// <summary>正式連線後第一個只讀訊息核server正式ID及UniqueName；回錯目標不得繼續盤點或寫入，服務仍由外層lease釋放。</summary>
    internal static void VerifyServer(IOrganizationService service, OrganizationDefinition target, CancellationToken ct)
    {
        if (target.Kind != "production") return;
        ct.ThrowIfCancellationRequested();
        var response = service.Execute(new RetrieveCurrentOrganizationRequest());
        ct.ThrowIfCancellationRequested();
        if (response is not RetrieveCurrentOrganizationResponse { Detail: { } detail } || detail.OrganizationId == Guid.Empty ||
            !string.Equals(detail.UniqueName, target.Name, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Server organization does not match the authorized target.");
    }
    /// <summary>所有已知寫入列在同一處；後續新增命令須明確加入，不能用「非唯讀」猜測未知命令。</summary>
    internal static bool IsWrite(string[] args) => args.Length > 0 &&
        (args[0] is "apply" or "rollback" or "switch" or "smoke" or "shadow" or "ui-import" or
            "protect-token" or "assign-channel" or "complete-created-audit" or "live-check" ||
            args[0] == "church-defaults" && args.Contains("--apply", StringComparer.Ordinal));
    /// <summary>同步只讀確認一行，最多256字元加CRLF；caller須提供有界reader，不建立無法回收的讀取Task。</summary>
    private static void Confirm(string name, TextReader? input, CancellationToken ct)
    {
        if (input is null) throw Invalid();
        var buffer = new char[257];
        try
        {
            var count = 0;
            while (true)
            {
                ct.ThrowIfCancellationRequested(); var value = input.Read();
                if (value is -1 or '\n') break;
                if (count == buffer.Length) throw Invalid();
                buffer[count++] = (char)value;
            }
            if (count > 0 && buffer[count - 1] == '\r') count--;
            if (count > 256 || !buffer.AsSpan(0, count).SequenceEqual(name.AsSpan())) throw Invalid();
        }
        finally { Array.Clear(buffer); }
    }
    /// <summary>固定拒絕不回顯輸入、目標、秘密或原始例外。</summary>
    private static InvalidDataException Invalid() => new("Organization production authorization rejected.");
}
