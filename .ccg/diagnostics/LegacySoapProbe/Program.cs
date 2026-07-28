// ============================================================================
// 暫時性 Dynamics 365 IFD / WS-Trust 診斷程式（不加入正式 Solution）
// ----------------------------------------------------------------------------
// 這個檔案只用來回答一個問題：舊 OnPremiseClient 在哪一個階段失敗？
//
// 執行流程：
// 1. 從 ChurchReport appsettings.json 讀取既有 ServerUrl、Username、Password。
// 2. 絕對不輸出完整 Username、Password、Token 或 SOAP 內容。
// 3. 建立與正式網站相同的 OnPremiseClient。
// 4. 只執行唯讀 WhoAmIRequest，不建立、修改或刪除任何 CRM 資料。
// 5. 若失敗，只輸出例外型別、HRESULT、來源與例外鏈訊息，協助判斷是
//    WSDL、ADFS token 簽發、WCF channel，或 CRM token 驗證階段失敗。
//
// 安全界線：
// - 本程式不接受命令列密碼，避免密碼出現在 shell history / process list。
// - 設定檔路徑由命令列傳入；密碼只存在目前程序記憶體。
// - 程式完成診斷後會從工作樹移除，不成為產品或正式測試的一部分。
// - 這不是把產品改為內網 Windows 驗證；它使用與正式網站相同的公開 IFD URL。
//
// 編碼要求：UTF-8 without BOM + CRLF。
// ============================================================================
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Client;
using PowerPlatform.Dataverse.Client;
using System;
using System.Collections.Generic;
using System.IO;
using System.ServiceModel.Description;
using System.Text.Json;
using System.Text.RegularExpressions;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 1 || args.Length > 2 || string.IsNullOrWhiteSpace(args[0]))
        {
            WriteResult(new ProbeResult
            {
                Ok = false,
                Stage = "arguments",
                Error = "Pass appsettings.json and optional mode: borrowed or official."
            });
            return 2;
        }

        var configPath = Path.GetFullPath(args[0]);
        if (!File.Exists(configPath))
        {
            WriteResult(new ProbeResult
            {
                Ok = false,
                Stage = "configuration",
                Error = "The configuration file does not exist."
            });
            return 2;
        }

        var configText = File.ReadAllText(configPath);
        var serverUrl = ReadJsonString(configText, "ServerUrl");
        var username = ReadJsonString(configText, "Username");
        var password = ReadJsonString(configText, "Password");

        if (string.IsNullOrWhiteSpace(serverUrl) ||
            string.IsNullOrWhiteSpace(username) ||
            string.IsNullOrWhiteSpace(password))
        {
            WriteResult(new ProbeResult
            {
                Ok = false,
                Stage = "configuration",
                Error = "ServerUrl, Username, and Password must all be configured."
            });
            return 2;
        }

        var mode = args.Length == 2 ? args[1] : "borrowed";
        if (string.Equals(mode, "official", StringComparison.OrdinalIgnoreCase))
        {
            return ProbeOfficialProxy(serverUrl, username, password);
        }

        return ProbeBorrowedClient(serverUrl, username, password);
    }

    /// <summary>
    /// 使用目前產品真正採用的 GitHub 借用 OnPremiseClient 執行 WhoAmI。
    /// 這一條是問題的基準線，用來確認網站錯誤能被獨立重現。
    /// </summary>
    private static int ProbeBorrowedClient(string serverUrl, string username, string password)
    {
        OnPremiseClient client;
        try
        {
            client = new OnPremiseClient(serverUrl, username, password);
        }
        catch (Exception ex)
        {
            WriteResult(CreateFailure("client-construction", serverUrl, username, ex));
            return 1;
        }

        try
        {
            var response = (WhoAmIResponse)client.Execute(new WhoAmIRequest());
            WriteResult(new ProbeResult
            {
                Ok = response.UserId != Guid.Empty,
                Stage = "whoami",
                ServerHost = new Uri(serverUrl).Host,
                UsernameDomain = GetUsernameDomain(username),
                UserIdPresent = response.UserId != Guid.Empty
            });
            return response.UserId == Guid.Empty ? 1 : 0;
        }
        catch (Exception ex)
        {
            WriteResult(CreateFailure("whoami", serverUrl, username, ex));
            return 1;
        }
    }

    /// <summary>
    /// 使用 Microsoft CRM SDK 的 OrganizationServiceProxy 執行同一個唯讀 WhoAmI。
    ///
    /// 這不是正式解法，也不會保留在產品中；它只用來做 A/B 判斷：
    /// - 官方 Proxy 成功、借用 Client 失敗：問題位於借用 Client 的 WCF/WS-Trust 組法。
    /// - 兩者都失敗：問題較可能位於 9.1 ADFS 主動式 WS-Trust 或 CRM token 驗證設定。
    ///
    /// IFD 的主動式 WS-Trust 使用 UserName credential，因此把已確認的完整登入名稱
    /// 原樣放入 ClientCredentials.UserName；不改成 Windows/內網驗證。
    /// </summary>
    private static int ProbeOfficialProxy(string serverUrl, string username, string password)
    {
        try
        {
            var serviceConfiguration =
                ServiceConfigurationFactory.CreateConfiguration<IOrganizationService>(new Uri(serverUrl));

            var credentials = new ClientCredentials();
            credentials.UserName.UserName = username;
            credentials.UserName.Password = password;

            using var proxy = new OrganizationServiceProxy(serviceConfiguration, credentials);
            proxy.EnableProxyTypes();

            var response = (WhoAmIResponse)proxy.Execute(new WhoAmIRequest());
            WriteResult(new ProbeResult
            {
                Ok = response.UserId != Guid.Empty,
                Stage = "official-whoami",
                ServerHost = new Uri(serverUrl).Host,
                UsernameDomain = GetUsernameDomain(username),
                UserIdPresent = response.UserId != Guid.Empty
            });
            return response.UserId == Guid.Empty ? 1 : 0;
        }
        catch (Exception ex)
        {
            WriteResult(CreateFailure("official-whoami", serverUrl, username, ex));
            return 1;
        }
    }

    private static ProbeResult CreateFailure(
        string stage,
        string serverUrl,
        string username,
        Exception exception)
    {
        var chain = new List<ExceptionInfo>();
        for (var current = exception; current is not null; current = current.InnerException)
        {
            chain.Add(new ExceptionInfo
            {
                Type = current.GetType().FullName ?? current.GetType().Name,
                Message = current.Message,
                HResult = current.HResult,
                Source = current.Source
            });
        }

        return new ProbeResult
        {
            Ok = false,
            Stage = stage,
            ServerHost = new Uri(serverUrl).Host,
            UsernameDomain = GetUsernameDomain(username),
            Error = exception.Message,
            Exceptions = chain
        };
    }

    private static string ReadJsonString(string text, string propertyName)
    {
        var match = Regex.Match(
            text,
            "\\\"" + Regex.Escape(propertyName) + "\\\"\\s*:\\s*\\\"([^\\\"]*)\\\"",
            RegexOptions.CultureInvariant);

        return match.Success
            ? Regex.Unescape(match.Groups[1].Value)
            : string.Empty;
    }

    private static string GetUsernameDomain(string username)
    {
        var separator = username.IndexOf('\\');
        if (separator > 0)
        {
            return username.Substring(0, separator);
        }

        var at = username.IndexOf('@');
        return at > 0 && at + 1 < username.Length
            ? username.Substring(at + 1)
            : "(unqualified)";
    }

    private static void WriteResult(ProbeResult result)
    {
        Console.WriteLine(JsonSerializer.Serialize(
            result,
            new JsonSerializerOptions { WriteIndented = true }));
    }

    private sealed class ProbeResult
    {
        public bool Ok { get; set; }

        public string Stage { get; set; } = string.Empty;

        public string? ServerHost { get; set; }

        public string? UsernameDomain { get; set; }

        public bool? UserIdPresent { get; set; }

        public string? Error { get; set; }

        public IReadOnlyList<ExceptionInfo>? Exceptions { get; set; }
    }

    private sealed class ExceptionInfo
    {
        public string Type { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;

        public int HResult { get; set; }

        public string? Source { get; set; }
    }
}
