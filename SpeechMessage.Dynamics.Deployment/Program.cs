using System;
using System.IO;
using System.Threading;
using Microsoft.Extensions.Configuration;
using ToolUtilityNameSpace.Diagnostics;

namespace SpeechMessage.Dynamics.Deployment;

/// <summary>
/// 部署 CLI 的程序組合根。組態與 Ctrl+C 註冊由 Main 持有至命令結束；
/// 每一命令另持有唯一例外診斷 owner 與一個可確定釋放的組織連線租約。
/// 本工具只寫本機 Exception.log，不建立 LINE sender，也不保留跨命令憑證或連線狀態。
/// </summary>
internal static class Program
{
    /// <summary>
    /// 建立唯一 User Secrets 組態與 Ctrl+C 取消來源，執行一個命令後先解除事件訂閱，
    /// 再 Dispose 取消來源與組態。初始化失敗只輸出固定訊息及例外型別，不輸出原始訊息。
    /// </summary>
    /// <param name="args">使用者提供的 CLI 參數。</param>
    /// <returns>部署 CLI 的程序結束碼。</returns>
    internal static int Main(string[] args)
    {
        IConfigurationRoot? configuration = null;
        CancellationTokenSource? cancellation = null;
        ConsoleCancelEventHandler? cancelHandler = null;
        try
        {
            configuration = DeploymentConfiguration.Create(typeof(Program).Assembly);
            cancellation = new CancellationTokenSource();
            cancelHandler = (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cancellation.Cancel();
            };

            Console.CancelKeyPress += cancelHandler;
            var connector = new UserSecretsOrganizationConnector(configuration);
            return RunCommand(
                args,
                connector,
                Console.Out,
                configuration,
                Path.Combine(AppContext.BaseDirectory, "Logs"),
                cancellation.Token);
        }
        catch (Exception exception)
        {
            WriteInitializationFailure(exception.GetType().Name);
            return 1;
        }
        finally
        {
            if (cancelHandler is not null)
            {
                Console.CancelKeyPress -= cancelHandler;
            }

            cancellation?.Dispose();
            (configuration as IDisposable)?.Dispose();
        }
    }

    /// <summary>
    /// 為單次命令掛載共用檔案診斷 owner，明確停用 LINE，並在命令完成時解除全域註冊後
    /// drain／Dispose owner。日誌目錄由程序組合根提供，不接受命令列、組態或 request 決定。
    /// </summary>
    /// <param name="args">已由 CLI parser 驗證的命令列輸入。</param>
    /// <param name="connector">每次連線建立新租約的組織 connector。</param>
    /// <param name="output">命令安全摘要的輸出 writer。</param>
    /// <param name="configuration">Main 擁有、於整個命令期間有效的 User Secrets 組態。</param>
    /// <param name="logDirectory">本次程序的固定本機診斷目錄；測試可注入獨立暫存目錄。</param>
    /// <param name="cancellationToken">由 Main 的 Ctrl+C 註冊所擁有的 caller 取消訊號。</param>
    /// <returns>成功為 0、caller 取消為 130，其他失敗為非零值。</returns>
    internal static int RunCommand(
        string[] args,
        IOrganizationConnector connector,
        TextWriter output,
        IConfiguration configuration,
        string logDirectory,
        CancellationToken cancellationToken)
    {
        ExceptionDiagnostics? diagnostics = null;
        IDisposable? registration = null;
        try
        {
            diagnostics = new ExceptionDiagnostics(
                logDirectory,
                outputOptions: new ExceptionOutputOptions(writeExceptionLog: true, sendLine: false));
            registration = ExceptionReporting.Attach(diagnostics);
            return DeploymentCli.Run(
                args,
                connector,
                output,
                exception => ExceptionReporting.Report(exception, "SpeechMessage.Dynamics.Deployment"),
                secretReader: name => configuration[
                    "Deployment:Organizations:goodshepherdback:" + name] ?? string.Empty,
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            WriteOutputLine(output, "Command cancelled.");
            return 130;
        }
        catch (Exception exception)
        {
            if (diagnostics is not null)
            {
                diagnostics.Report(exception, "SpeechMessage.Dynamics.Deployment.Program.RunCommand");
            }

            WriteOutputLine(output,
                "Command failed (" + exception.GetType().Name + "). Sensitive details are not shown.");
            return 1;
        }
        finally
        {
            try
            {
                registration?.Dispose();
            }
            catch
            {
                WriteEmergency("DiagnosticRegistrationDisposeFailed");
            }
            finally
            {
                if (diagnostics is not null)
                {
                    try
                    {
                        diagnostics.DisposeAsync().AsTask().GetAwaiter().GetResult();
                    }
                    catch
                    {
                        WriteEmergency("DiagnosticOwnerDisposeFailed");
                    }
                }
            }
        }
    }

    /// <summary>組態尚未建立診斷 owner 時，只輸出固定初始化狀態與型別名稱。</summary>
    /// <param name="exceptionType">CLR 例外型別簡稱，不包含訊息或例外內容。</param>
    private static void WriteInitializationFailure(string exceptionType) =>
        WriteOutputLine(Console.Error, "Deployment initialization failed (" + exceptionType + ").");

    /// <summary>以固定 stderr 代碼處理輸出 writer 失敗，不展開原始例外或組態內容。</summary>
    /// <param name="code">程式碼內固定的診斷狀態名稱。</param>
    private static void WriteEmergency(string code)
    {
        try
        {
            Console.Error.WriteLine("[Deployment] " + code);
        }
        catch
        {
        }
    }

    /// <summary>輸出安全摘要；writer 本身故障時僅使用固定 stderr 狀態。</summary>
    /// <param name="output">主控台或離線測試提供的輸出 writer。</param>
    /// <param name="message">由程式固定文字與型別名稱組成，不含原始例外訊息。</param>
    private static void WriteOutputLine(TextWriter output, string message)
    {
        try
        {
            output.WriteLine(message);
        }
        catch
        {
            WriteEmergency("CommandOutputFailed");
        }
    }
}
