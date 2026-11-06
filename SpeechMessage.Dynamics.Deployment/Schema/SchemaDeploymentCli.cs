/*
 * 檔案整體說明
 *
 * 目的與定位：本檔承接DeploymentCli明確--schema與export-metadata命令，提供不依賴Plugin DLL的P1結構操作路由。
 * 主要功能：嚴格解析plan/apply/verify/export-metadata及唯一--org/--schema/--plan組合；只有goodshepherdback可進入connector。
 * 輸入與流程：schema限定本repo d365/schema，plan限定該組織artifacts；先核對路徑祖先link/junction、讀strict JSON，再由既有connector取得一次lease。
 * apply沿用既有OrganizationWriteLock，持有期間重新盤點／驗plan／保存before／新增；唯讀匯出寫到d365/snapshots/組織的唯一操作目錄。
 * 輸出：plan同時提供JSON與可閱讀Markdown；verify有差異為1、key未Active為3、拒絕為2、caller取消為130，只有完整一致才為0。
 * 生命週期與保密：每指令使用本地候選及單一lease，using先歸還服務再釋放寫鎖；沒有共用request、credential、結果cache或無界背景工作。
 * 真正故障傳原Exception給Program注入的共用report，摘要不回顯訊息、參數或機密；正常caller取消不告警，內部timeout仍是失敗。
 * 重要限制：連線成功／plan產生不等於實機寫入授權；沒有隔離、備份與可驗證復原證據前不可自動apply。
 * 維護驗證：SchemaCliTests用真路由／引擎及合成SDK驗完整生命週期、租約歸還、路徑拒絕、取消、真共用ExceptionDiagnostics與敏感本文不落檔。
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;

namespace SpeechMessage.Dynamics.Deployment.Schema;

/// <summary>明確--schema的結構命令路由；共用P0connector、單次lease與組織寫鎖，不依賴Plugin DLL，不讀request或機密。</summary>
internal static class SchemaDeploymentCli
{
    /// <summary>執行一個結構指令；全部輸入／白名單／路徑先驗，只有apply取得寫鎖，lease在終端report前已歸還。</summary>
    /// <param name="args">Host命令列，不回顯未知內容。</param><param name="connector">既有合法User Secrets connector。</param>
    /// <param name="output">Host擁有的安全摘要writer。</param><param name="report">真Exception共用入口，不可捕捉request。</param>
    /// <param name="artifactRoot">Host固定根，只有離線測試注入替代。</param><param name="writeLockTimeout">同組織寫鎖等待上限。</param>
    /// <param name="cancellationToken">命令owner取消。</param><returns>0成功、1差異／故障、2拒絕、3key建置未完成、130caller取消。</returns>
    internal static int Run(string[] args, IOrganizationConnector connector, TextWriter output, Action<Exception> report,
        string? artifactRoot, TimeSpan? writeLockTimeout, CancellationToken cancellationToken)
    {
        if (!Parse(args, out var command, out var schemaPath, out var planPath))
        {
            output.WriteLine("Schema command arguments are invalid or not allowlisted.");
            return 2;
        }
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var root = DeploymentCli.FindRepositoryRoot();
            var artifacts = Path.GetFullPath(artifactRoot ?? Path.Combine(root, "artifacts", "d365"));
            var organizationArtifacts = Path.Combine(artifacts, "goodshepherdback");
            if (schemaPath is not null && !DeploymentCli.IsContainedPath(Path.Combine(root, "d365", "schema"), schemaPath, out schemaPath)
                || planPath is not null && !DeploymentCli.IsContainedPath(organizationArtifacts, planPath, out planPath))
            {
                output.WriteLine("Schema or plan path is outside its authorized directory.");
                return 2;
            }
            if (schemaPath is not null) EnsureNoLinks(schemaPath);
            if (planPath is not null) EnsureNoLinks(planPath);
            EnsureNoLinks(organizationArtifacts);
            var definition = schemaPath is null ? null : SchemaFiles.Load(schemaPath);
            var approved = planPath is null ? null : SchemaFiles.ReadPlan(planPath);
            using var writeLock = command == "apply"
                ? OrganizationWriteLock.Acquire(organizationArtifacts, writeLockTimeout ?? TimeSpan.FromMinutes(5), cancellationToken) : null;
            using var lease = connector.Connect("goodshepherdback");
            var destination = command == "export-metadata"
                ? Path.Combine(root, "d365", "snapshots", "goodshepherdback") : organizationArtifacts;
            EnsureNoLinks(destination);
            var operationRoot = DeploymentCli.CreateOperationDirectory(destination);
            var engine = new SchemaDeploymentEngine(lease.Service, operationRoot, TimeProvider.System);
            if (command == "export-metadata")
            {
                _ = engine.Export(cancellationToken);
                output.WriteLine("Complete metadata-only snapshot created.");
                return 0;
            }
            if (command == "plan")
            {
                var plan = engine.Plan(definition!, cancellationToken);
                _ = SchemaFiles.WriteNew(operationRoot, "schema-plan.json", plan);
                _ = SchemaFiles.WritePlanMarkdown(operationRoot, plan);
                output.WriteLine("Schema plan created; additions=" + plan.Operations.Count.ToString(CultureInfo.InvariantCulture));
                return 0;
            }
            var result = command == "apply" ? engine.Apply(approved!, definition!, cancellationToken)
                : engine.Verify(definition!, cancellationToken);
            if (result.Operations.Count != 0)
            {
                output.WriteLine("Schema differs from the definition; no implicit changes were made.");
                return 1;
            }
            if (result.PendingKeys.Count != 0)
            {
                output.WriteLine("Alternate keys are not Active; verification is incomplete.");
                return 3;
            }
            output.WriteLine("Schema metadata matches the definition and all alternate keys are Active.");
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            output.WriteLine("Schema command cancelled.");
            return 130;
        }
        catch (Exception exception)
        {
            try { report(exception); }
            catch { try { Console.Error.WriteLine("Schema diagnostic reporting failed."); } catch { } }
            output.WriteLine("Schema command failed (" + exception.GetType().Name + "). Sensitive details are not shown.");
            return 1;
        }
    }

    /// <summary>只接受唯一組織與明確命令／選項組合，任何重複、秘密選項或未辨識欄位皆在連線前拒絕。</summary>
    private static bool Parse(string[] args, out string command, out string? schema, out string? plan)
    {
        command = args[0]; schema = null; plan = null;
        if (command is not ("plan" or "apply" or "verify" or "export-metadata")) return false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? organization = null;
        for (var index = 1; index < args.Length; index++)
        {
            var option = args[index];
            if (option is not ("--org" or "--schema" or "--plan") || !seen.Add(option) || index + 1 >= args.Length
                || string.IsNullOrWhiteSpace(args[index + 1]) || args[index + 1].StartsWith("--", StringComparison.Ordinal)) return false;
            var value = args[++index];
            if (option == "--org") organization = value;
            else if (option == "--schema") schema = value;
            else plan = value;
        }
        if (!string.Equals(organization, "goodshepherdback", StringComparison.OrdinalIgnoreCase)) return false;
        return command switch
        {
            "export-metadata" => seen.Count == 1 && schema is null && plan is null,
            "plan" or "verify" => seen.Count == 2 && schema is not null && plan is null,
            "apply" => seen.Count == 3 && schema is not null && plan is not null,
            _ => false
        };
    }

    /// <summary>逐層拒絕既有link/junction，不只作字串containment；不存在的命令專屬目錄可由owner之後建立。</summary>
    internal static void EnsureNoLinks(string path)
    {
        var current = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Schema paths must not traverse links.");
            current = Path.GetDirectoryName(current);
        }
    }
}
