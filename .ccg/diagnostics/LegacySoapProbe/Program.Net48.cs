// ============================================================================
// Dynamics 365 CE 9.1 IFD 官方 SDK Proxy 對照診斷（.NET Framework 4.8）
// ----------------------------------------------------------------------------
// 為什麼需要這個暫時檔案：
// - 正式 ChurchReport 目前跑在 .NET 10。
// - Microsoft CRM SDK 的 OrganizationServiceProxy 在 .NET 10 直接回報
//   「ServiceModel metadata support is limited for this target framework」，
//   因此無法拿來與 GitHub 借用的 OnPremiseClient 做公平 A/B 測試。
// - Dynamics 365 9.1 時代的官方 SOAP Proxy 原生支援 .NET Framework，
//   所以用 net48 做一次隔離、唯讀 WhoAmI，才能判斷伺服器 WS-Trust 是否正常。
//
// 安全規則：
// 1. 密碼只從 appsettings.json 讀入目前程序記憶體，不接受命令列密碼。
// 2. 不輸出完整 Username、Password、Token、SOAP 或 Connection String。
// 3. 只呼叫 WhoAmI，不建立、修改或刪除任何 Dynamics 資料。
// 4. 此專案不加入 SpeechMessageProducts.sln，診斷完成後移除。
// 5. 仍然呼叫公網 HTTPS IFD URL，絕不改成內網 AD/Windows 驗證路徑。
//
// 判讀方式：
// - net48 官方 Proxy 成功：9.1 IFD/WS-Trust 可用，借用的 net10 Client 不相容。
// - net48 官方 Proxy 也出現 ID3242：需查 ADFS 主動式端點與 CRM token 驗證設定。
// ============================================================================
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Client;
using System;
using System.IO;
using System.ServiceModel.Description;
using System.Text.RegularExpressions;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length != 1 || !File.Exists(args[0]))
        {
            Console.WriteLine("{\"ok\":false,\"stage\":\"arguments\",\"error\":\"configuration path required\"}");
            return 2;
        }

        var text = File.ReadAllText(args[0]);
        var serverUrl = ReadValue(text, "ServerUrl");
        var username = ReadValue(text, "Username");
        var password = ReadValue(text, "Password");

        try
        {
            var configuration =
                ServiceConfigurationFactory.CreateConfiguration<IOrganizationService>(new Uri(serverUrl));

            var credentials = new ClientCredentials();
            credentials.UserName.UserName = username;
            credentials.UserName.Password = password;

            using (var proxy = new OrganizationServiceProxy(configuration, credentials))
            {
                var response = (WhoAmIResponse)proxy.Execute(new WhoAmIRequest());
                Console.WriteLine(
                    "{\"ok\":" + (response.UserId != Guid.Empty ? "true" : "false") +
                    ",\"stage\":\"official-net48-whoami\"" +
                    ",\"serverHost\":\"" + Escape(new Uri(serverUrl).Host) + "\"" +
                    ",\"usernameDomain\":\"" + Escape(GetDomain(username)) + "\"}");
                return response.UserId == Guid.Empty ? 1 : 0;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                "{\"ok\":false,\"stage\":\"official-net48-whoami\"" +
                ",\"serverHost\":\"" + Escape(new Uri(serverUrl).Host) + "\"" +
                ",\"usernameDomain\":\"" + Escape(GetDomain(username)) + "\"" +
                ",\"exceptions\":" + ExceptionChain(ex) + "}");
            return 1;
        }
    }

    private static string ReadValue(string text, string name)
    {
        var match = Regex.Match(
            text,
            "\\\"" + Regex.Escape(name) + "\\\"\\s*:\\s*\\\"([^\\\"]*)\\\"");
        return match.Success ? Regex.Unescape(match.Groups[1].Value) : string.Empty;
    }

    private static string GetDomain(string username)
    {
        var slash = username.IndexOf('\\');
        return slash > 0 ? username.Substring(0, slash) : "(unqualified)";
    }

    private static string ExceptionChain(Exception exception)
    {
        var result = "[";
        var first = true;
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (!first)
            {
                result += ",";
            }

            first = false;
            result +=
                "{\"type\":\"" + Escape(current.GetType().FullName) + "\"" +
                ",\"message\":\"" + Escape(current.Message) + "\"" +
                ",\"hresult\":" + current.HResult + "}";
        }

        return result + "]";
    }

    private static string Escape(string value)
    {
        return (value ?? string.Empty)
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n");
    }
}
