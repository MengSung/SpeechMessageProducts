/*
檔案整體說明

目的與定位：組合 bounded source reader、分類器及 attendance 黃金驗證，形成一次完整的 PluginHarness 候選；本檔不負責檔案發布或外部服務。

主要功能與流程：讀取完整第一層專案清單，逐一分類並排序，建立固定三筆 attendance 情境，最後確認結果沒有重複專案名稱、沒有可疑輸出欄位且至少有一個來源專案。任何操作錯誤在候選交給 writer 前拋出，避免部分結果被發布。

使用邊界與維護：所有輸入、結果與 JSON payload 都只在單次同步呼叫中存在；沒有共享快取、Session、credential、timer、task 或網路連線。來源名稱只保留必要的穩定專案名稱與雜湊，原始碼與診斷本文永不進入候選。
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace LegacyGolden.PluginHarness;

/// <summary>PluginHarness 的單次離線組合根。</summary>
internal static class HarnessEngine
{
    /// <summary>建立完整候選；任何來源邊界或候選驗證失敗都不回傳半成品。</summary>
    internal static HarnessCandidate Build(string sourceRoot)
    {
        var validatedRoot = BoundedSourceReader.ValidateRoot(sourceRoot);
        var inputs = BoundedSourceReader.ReadProjects(validatedRoot);
        var projects = inputs.Select(LegacyClassifier.Classify).OrderBy(item => item.Name, StringComparer.Ordinal).ToList();
        if (projects.Count == 0 || projects.Select(item => item.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != projects.Count)
        {
            throw new InvalidDataException("Project identity validation failed.");
        }

        var attendance = AttendanceGolden.CreateAndValidate();
        if (attendance.Count < 3 || attendance.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != attendance.Count)
        {
            throw new InvalidDataException("Attendance candidate validation failed.");
        }

        return new HarnessCandidate(Path.GetFileName(validatedRoot), projects, attendance);
    }
}

/// <summary>將已驗證候選轉成固定排序、無來源本文的 JSON。</summary>
internal static class HarnessJson
{
    /// <summary>序列化完整候選，執行最終秘密掃描並固定使用 UTF-8 無 BOM CRLF。</summary>
    internal static string Serialize(HarnessCandidate candidate)
    {
        if (candidate == null || candidate.Projects.Count == 0 || candidate.Attendance.Count < 3)
        {
            throw new InvalidDataException("Candidate is incomplete.");
        }

        var payload = new
        {
            schemaVersion = 1,
            tool = "LegacyGolden.PluginHarness",
            sourceRoot = candidate.SourceRootName,
            externalServicesExecuted = false,
            networkApiPolicy = "refuse-before-compile",
            secretScan = "passed",
            sameNameStableIdentity = "passed",
            projects = candidate.Projects.Select(project => new
            {
                name = project.Name,
                classification = project.Classification.ToString(),
                sourceFileCount = project.SourceFileCount,
                sourceByteCount = project.SourceByteCount,
                sourceSha256 = project.SourceSha256,
                diagnostics = project.Diagnostics
            }).ToList(),
            attendance = candidate.Attendance.Select(item => new
            {
                id = item.Id,
                legacy = new
                {
                    outcome = item.Legacy.Outcome,
                    date = item.Legacy.Date,
                    churchLocalDate = item.Legacy.ChurchLocalDate,
                    rows = item.Legacy.Rows.Select(row => new
                    {
                        presentRecordId = row.PresentRecordId,
                        displayName = row.DisplayName,
                        present = row.Present
                    }).ToList()
                },
                expected = new
                {
                    outcome = item.Expected.Outcome,
                    date = item.Expected.Date,
                    churchLocalDate = item.Expected.ChurchLocalDate,
                    rows = item.Expected.Rows.Select(row => new
                    {
                        presentRecordId = row.PresentRecordId,
                        displayName = row.DisplayName,
                        present = row.Present
                    }).ToList()
                },
                declaredDifferences = item.DeclaredDifferences
            }).ToList()
        };

        var json = JsonConvert.SerializeObject(payload, Formatting.Indented, new JsonSerializerSettings
        {
            StringEscapeHandling = StringEscapeHandling.EscapeNonAscii
        });
        json = json.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n") + "\r\n";
        SafetyScanner.EnsureSafeOutput(json);
        if (json.Length > 16 * 1024 * 1024)
        {
            throw new InvalidDataException("Candidate JSON exceeds the bounded limit.");
        }

        return json;
    }
}
