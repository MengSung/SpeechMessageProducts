using System.IO.Compression;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

Console.OutputEncoding = Encoding.UTF8;

var taskDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
var outPath = Path.Combine(taskDir, "Subagent_Goal_保母級教學手冊.docx");
var workDir = Path.Combine(taskDir, "docx-work-csharp");

if (Directory.Exists(workDir))
{
    Directory.Delete(workDir, recursive: true);
}

Directory.CreateDirectory(workDir);
Directory.CreateDirectory(Path.Combine(workDir, "_rels"));
Directory.CreateDirectory(Path.Combine(workDir, "word"));
Directory.CreateDirectory(Path.Combine(workDir, "word", "_rels"));

static string E(string text) => SecurityElement.Escape(text) ?? "";

static string Paragraph(string text, string style = "BodyText")
{
    return $"""
<w:p>
  <w:pPr><w:pStyle w:val="{style}"/></w:pPr>
  <w:r><w:t xml:space="preserve">{E(text)}</w:t></w:r>
</w:p>
""";
}

static string Heading(string text, int level) => Paragraph(text, $"Heading{level}");

static string Bullet(string text)
{
    return $"""
<w:p>
  <w:pPr><w:pStyle w:val="ListBullet"/></w:pPr>
  <w:r><w:t xml:space="preserve">• {E(text)}</w:t></w:r>
</w:p>
""";
}

static string Numbered(int number, string text)
{
    return $"""
<w:p>
  <w:pPr><w:pStyle w:val="ListNumber"/></w:pPr>
  <w:r><w:t xml:space="preserve">{number}. {E(text)}</w:t></w:r>
</w:p>
""";
}

static string CodeBlock(string text)
{
    var parts = new List<string>();
    foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
    {
        parts.Add($"""
<w:p>
  <w:pPr><w:pStyle w:val="Code"/></w:pPr>
  <w:r><w:t xml:space="preserve">{E(line)}</w:t></w:r>
</w:p>
""");
    }
    return string.Join("\n", parts);
}

static string Callout(string label, string text, string fill = "F4F6F9")
{
    return $"""
<w:tbl>
  <w:tblPr>
    <w:tblW w:w="9360" w:type="dxa"/>
    <w:tblInd w:w="120" w:type="dxa"/>
    <w:tblBorders>
      <w:top w:val="single" w:sz="8" w:space="0" w:color="B7C2D0"/>
      <w:left w:val="single" w:sz="8" w:space="0" w:color="B7C2D0"/>
      <w:bottom w:val="single" w:sz="8" w:space="0" w:color="B7C2D0"/>
      <w:right w:val="single" w:sz="8" w:space="0" w:color="B7C2D0"/>
      <w:insideH w:val="single" w:sz="4" w:space="0" w:color="E0E6ED"/>
      <w:insideV w:val="single" w:sz="4" w:space="0" w:color="E0E6ED"/>
    </w:tblBorders>
    <w:tblCellMar>
      <w:top w:w="120" w:type="dxa"/><w:left w:w="160" w:type="dxa"/>
      <w:bottom w:w="120" w:type="dxa"/><w:right w:w="160" w:type="dxa"/>
    </w:tblCellMar>
  </w:tblPr>
  <w:tblGrid><w:gridCol w:w="1680"/><w:gridCol w:w="7680"/></w:tblGrid>
  <w:tr>
    <w:tc>
      <w:tcPr><w:tcW w:w="1680" w:type="dxa"/><w:shd w:fill="{fill}"/></w:tcPr>
      <w:p><w:pPr><w:pStyle w:val="CalloutLabel"/></w:pPr><w:r><w:t>{E(label)}</w:t></w:r></w:p>
    </w:tc>
    <w:tc>
      <w:tcPr><w:tcW w:w="7680" w:type="dxa"/><w:shd w:fill="{fill}"/></w:tcPr>
      <w:p><w:pPr><w:pStyle w:val="CalloutText"/></w:pPr><w:r><w:t xml:space="preserve">{E(text)}</w:t></w:r></w:p>
    </w:tc>
  </w:tr>
</w:tbl>
""";
}

static string SimpleTable(string[] headers, string[][] rows, int[] widths)
{
    var grid = string.Join("", widths.Select(width => $"""<w:gridCol w:w="{width}"/>"""));
    var headerCells = new StringBuilder();
    for (var i = 0; i < headers.Length; i++)
    {
        headerCells.Append($"""<w:tc><w:tcPr><w:tcW w:w="{widths[i]}" w:type="dxa"/><w:shd w:fill="E8EEF5"/></w:tcPr><w:p><w:pPr><w:pStyle w:val="TableHeader"/></w:pPr><w:r><w:t>{E(headers[i])}</w:t></w:r></w:p></w:tc>""");
    }

    var rowXml = new StringBuilder();
    foreach (var row in rows)
    {
        rowXml.Append("<w:tr>");
        for (var i = 0; i < row.Length; i++)
        {
            rowXml.Append($"""<w:tc><w:tcPr><w:tcW w:w="{widths[i]}" w:type="dxa"/></w:tcPr><w:p><w:pPr><w:pStyle w:val="TableText"/></w:pPr><w:r><w:t xml:space="preserve">{E(row[i])}</w:t></w:r></w:p></w:tc>""");
        }
        rowXml.AppendLine("</w:tr>");
    }

    return $"""
<w:tbl>
  <w:tblPr>
    <w:tblW w:w="9360" w:type="dxa"/>
    <w:tblInd w:w="120" w:type="dxa"/>
    <w:tblBorders>
      <w:top w:val="single" w:sz="4" w:space="0" w:color="C7D0DA"/>
      <w:left w:val="single" w:sz="4" w:space="0" w:color="C7D0DA"/>
      <w:bottom w:val="single" w:sz="4" w:space="0" w:color="C7D0DA"/>
      <w:right w:val="single" w:sz="4" w:space="0" w:color="C7D0DA"/>
      <w:insideH w:val="single" w:sz="4" w:space="0" w:color="D7DEE8"/>
      <w:insideV w:val="single" w:sz="4" w:space="0" w:color="D7DEE8"/>
    </w:tblBorders>
    <w:tblCellMar>
      <w:top w:w="100" w:type="dxa"/><w:left w:w="120" w:type="dxa"/>
      <w:bottom w:w="100" w:type="dxa"/><w:right w:w="120" w:type="dxa"/>
    </w:tblCellMar>
  </w:tblPr>
  <w:tblGrid>{grid}</w:tblGrid>
  <w:tr>{headerCells}</w:tr>
  {rowXml}
</w:tbl>
""";
}

var content = new List<string>
{
    """
<w:p>
  <w:pPr><w:pStyle w:val="Title"/></w:pPr>
  <w:r><w:t>Subagent 與 Goal 保母級教學手冊</w:t></w:r>
</w:p>
<w:p>
  <w:pPr><w:pStyle w:val="Subtitle"/></w:pPr>
  <w:r><w:t>從 brainstorming、writeplan 到 dispatch 的完整實戰指南</w:t></w:r>
</w:p>
""",
    Callout("核心結論", "Subagent 不是等到要動手時才臨時叫出來的工具。正確做法是在 brainstorming 階段先判斷是否需要代理，在 writeplan 階段把代理的角色、輸入、邊界、驗收標準與回報格式寫清楚，真正 dispatch 時才不會失控。", "E8EEF5"),
    Heading("1. 先建立正確心智模型", 1),
    Paragraph("Subagent 可以理解為「被主會話派出去執行特定工作包的專責代理」。主會話負責判斷方向、拆分任務、整合結果與把關品質；subagent 負責在明確邊界內完成研究、實作、檢查或整理。"),
    Paragraph("Goal 則是派工契約。Goal 寫得越完整，subagent 越容易做對；Goal 寫得模糊，subagent 很容易偏離範圍、漏讀上下文、做出未授權修改，或交付無法驗證的結果。"),
    Bullet("主會話是 owner：負責任務定義、風險判斷、最終驗收與整合。"),
    Bullet("Subagent 是 worker / reviewer / researcher：只處理被明確委派的工作包。"),
    Bullet("Goal 是 contract：必須包含目的、上下文、邊界、禁止事項、驗收方式與回報格式。"),
    Bullet("Plan 是 dispatch map：決定哪些工作可並行、哪些工作必須依序完成。"),
    Heading("2. 什麼時候該用 subagent", 1),
    SimpleTable(
        ["判斷", "說明"],
        [
            ["適合使用", "多個互不重疊的工作包，例如不同模組、不同檔案群、研究與檢查可以分開處理。"],
            ["適合使用", "需要獨立審查或交叉驗證，例如安全、架構、測試覆蓋、規格一致性。"],
            ["適合使用", "上下文很多，主會話容易被細節淹沒，需要代理整理依賴、呼叫點或風險清單。"],
            ["不適合使用", "只改一兩行、問題定位清楚、派出代理的溝通成本比直接做更高。"],
            ["不適合使用", "需求還沒定義清楚，PRD 或設計仍在大幅變動。"],
            ["不適合使用", "多個代理會同時修改同一批檔案，容易產生衝突或互相覆蓋。"]
        ],
        [1800, 7560]),
    Heading("3. Brainstorming 階段就要先考慮 subagent 嗎", 1),
    Callout("答案", "要，但不是馬上 dispatch。Brainstorming 階段要先判斷「未來是否可能需要 subagent」，並把可獨立驗收的 deliverables、風險、未知問題與可能的工作包記下來。真正派工要等需求、設計與計畫足夠穩定。"),
    Numbered(1, "先問：這個需求是否能拆成多個獨立驗收的結果？例如研究、設計、實作、測試、文件、審查。"),
    Numbered(2, "再問：每個結果是否有明確 owner、輸入資料、修改範圍與完成標準？如果沒有，先補 PRD，不要 dispatch。"),
    Numbered(3, "最後問：哪些工作必須依序完成，哪些工作可以並行？依賴關係要寫進需求或計畫，不要只留在腦中。"),
    Bullet("Brainstorming 產物應包含：問題背景、使用者目標、不可做事項、成功標準、可能的 task tree。"),
    Bullet("若需求跨多個可驗證成果，應考慮 parent / child task，而不是把所有內容丟給一個 agent。"),
    Bullet("若需要研究第三方 API、既有架構或測試策略，可以先規劃 research agent，但仍要先定義研究問題。"),
    Heading("4. Writeplan 階段如何預備 subagent", 1),
    Paragraph("Writeplan 是最適合把 subagent 工作包寫清楚的階段。這時應該已經知道要改什麼、為什麼改、哪些檔案或模組受影響，以及驗收方式。"),
    SimpleTable(
        ["欄位", "writeplan 必須寫清楚的內容"],
        [
            ["工作包名稱", "例如 Research: 盤點支付模組依賴、Implement: 抽出核心服務、Check: 驗證規格與測試。"],
            ["依賴順序", "標明 Layer 1 可並行、Layer 2 必須等 Layer 1 完成。"],
            ["檔案邊界", "每個 subagent 可讀哪些資料、可改哪些檔案、不可碰哪些檔案。"],
            ["驗收命令", "例如 dotnet build、dotnet test、lint、型別檢查、文件 render QA。"],
            ["回報格式", "要求列出 Files Touched、Key Decisions、Verification Result、Open Risks。"]
        ],
        [2100, 7260]),
    Paragraph("如果 writeplan 無法寫出清楚工作包，代表還不適合派 subagent。這時應回到 brainstorming 或 design，把需求與架構補清楚。"),
    Heading("5. 一個保母級 Goal 必須包含什麼", 1),
    Paragraph("Goal 的目標不是「描述大方向」，而是讓 subagent 在沒有你腦中背景的情況下，也能安全、精準、可驗證地完成工作。"),
    SimpleTable(
        ["Goal 區塊", "應填內容"],
        [
            ["Task Identity", "Active task path、任務名稱、代理角色、目前階段。"],
            ["Objective", "一句話說明這次代理要完成什麼，不要同時塞入多個不相干目標。"],
            ["Context", "必讀文件、PRD / design / implement、spec、研究資料與相關檔案。"],
            ["Scope", "允許修改的檔案、允許新增的檔案、允許執行的命令。"],
            ["Boundaries", "明確禁止 git commit / push、禁止碰非目標檔、禁止改公共契約、禁止用臨時 hack。"],
            ["Acceptance Criteria", "完成後必須滿足的行為、測試、品質條件與文件條件。"],
            ["Verification", "必跑命令與期望結果，包含失敗時要如何回報。"],
            ["Output Format", "交付格式：摘要、修改檔案、決策、測試結果、風險、後續建議。"]
        ],
        [2200, 7160]),
    Heading("6. Goal 撰寫範本：保母級完整版", 1),
    CodeBlock("""
Active task: <task path>

[Role]
You are the <agent_type> subagent. You are already inside the delegated role.
Do not spawn another agent.

[Objective]
Complete exactly this work package:
<single, concrete objective>

[Required Context]
Read these files before acting, in this order:
1. <task>/prd.md
2. <task>/design.md if present
3. <task>/implement.md if present
4. <specific spec/research files>

[Allowed Scope]
You may read:
- <paths>
You may modify only:
- <file path 1>
- <file path 2>

[Forbidden]
- Do not run git commit, git push, git reset, or destructive cleanup.
- Do not modify files outside the allowed list.
- Do not introduce type suppressions, placeholder implementations, or hidden fallbacks.
- Do not change public contracts unless explicitly listed above.

[Acceptance Criteria]
- <behavioral requirement 1>
- <behavioral requirement 2>
- <test/quality requirement>

[Verification]
Before reporting completion, run:
- <command 1>
- <command 2>
If a command fails, report the exact failure and stop.

[Report Format]
Return:
- Status: PASSED / BLOCKED / NEEDS_REVIEW
- Files Touched
- Key Decisions
- Verification Output
- Risks or Follow-ups
"""),
    Heading("7. 三種常用 subagent prompt 範本", 1),
    Heading("7.1 Research / Investigation Agent", 2),
    CodeBlock("""
Active task: .trellis/tasks/<TASK_NAME>

[Role]
You are the research subagent for this task.

[Objective]
Investigate <specific question> and produce a decision-ready report.

[Required Reads]
- .trellis/tasks/<TASK_NAME>/prd.md
- .trellis/tasks/<TASK_NAME>/design.md if present
- <relevant spec or source paths>

[Scope]
- Read-only. Do not edit files.
- Focus only on <module/API/workflow>.

[Output]
Write findings as:
1. Summary
2. Evidence with file paths
3. Risks
4. Recommended next step
5. Open questions
"""),
    Heading("7.2 Implementation Agent", 2),
    CodeBlock("""
Active task: .trellis/tasks/<TASK_NAME>

[Role]
You are the implementation subagent. Implement directly; do not spawn other agents.

[Objective]
Implement Step <N>: <step description>.

[Required Reads]
1. .trellis/tasks/<TASK_NAME>/prd.md
2. .trellis/tasks/<TASK_NAME>/design.md
3. .trellis/tasks/<TASK_NAME>/implement.md
4. <spec files>

[Allowed Edits]
- <file 1>
- <file 2>

[Forbidden]
- No git commit or push.
- No edits outside Allowed Edits.
- No unrelated refactors.

[Verification]
Run:
- dotnet build <solution-or-project>
- dotnet test <test-project>

[Report]
- Files Touched
- Behavior Implemented
- Tests Run
- Remaining Risks
"""),
    Heading("7.3 Check / Review Agent", 2),
    CodeBlock("""
Active task: .trellis/tasks/<TASK_NAME>

[Role]
You are the quality check subagent.

[Objective]
Review the current diff against the task artifacts and project specs.

[Required Reads]
- .trellis/tasks/<TASK_NAME>/prd.md
- .trellis/tasks/<TASK_NAME>/implement.md
- Relevant .trellis/spec indexes
- git diff

[Checkpoints]
- Scope matches the task.
- No forbidden file changes.
- Tests and type checks pass.
- Error handling, logging, and architecture follow specs.

[Output]
Use this format:
- Status: PASSED / FAILED / NEEDS_REVISION
- Critical Findings
- Warnings
- Info
- Verification Commands and Results
"""),
    Heading("8. 常見錯誤與修正方式", 1),
    SimpleTable(
        ["錯誤", "後果", "修正方式"],
        [
            ["Goal 只有一句話", "代理不知道邊界與驗收標準，容易亂改。", "補上 Objective、Scope、Forbidden、Verification、Report Format。"],
            ["多個代理改同一檔案", "產生衝突、互相覆蓋或邏輯不一致。", "按檔案歸屬拆分；同一檔案同一時間只給一個代理。"],
            ["需求未穩定就 dispatch", "代理依錯誤方向實作，後續返工成本高。", "PRD / design / implement 未確認前只做研究，不做實作。"],
            ["沒有驗收命令", "看似完成但無法證明品質。", "每個 implementation / check goal 都要列命令與期望結果。"],
            ["沒有回報格式", "主會話難以整合結果。", "固定要求 Files Touched、Decisions、Verification、Risks。"]
        ],
        [2200, 3600, 3560]),
    Heading("9. Dispatch 前檢查清單", 1),
    Bullet("[ ] 我知道這個 subagent 的單一目標是什麼。"),
    Bullet("[ ] 我已寫清楚 Active task path 與必讀文件順序。"),
    Bullet("[ ] 我已限定可修改檔案，且不同代理不會修改同一檔案。"),
    Bullet("[ ] 我已列出禁止操作，例如 commit、push、reset、改非目標檔。"),
    Bullet("[ ] 我已列出驗收標準與必跑命令。"),
    Bullet("[ ] 我已定義回報格式，方便主會話整合。"),
    Bullet("[ ] 如果需求還不穩定，我只派 research，不派 implementation。"),
    Heading("10. 最佳實務總結", 1),
    Numbered(1, "Brainstorming 階段：先判斷是否需要 subagent，辨識可獨立驗收的 deliverables。"),
    Numbered(2, "Writeplan 階段：把 subagent 工作包、依賴順序、檔案邊界與驗收命令寫進計畫。"),
    Numbered(3, "Dispatch 階段：每次只派一個清楚工作包，goal 必須能讓代理獨立完成與回報。"),
    Numbered(4, "Review 階段：主會話整合代理結果，不盲信代理聲稱，仍要檢查 diff、測試與規格。"),
    Numbered(5, "收尾階段：記錄學到的規範、常見坑與可重用 prompt，讓下一次工作更穩。")
};

var body = string.Join("\n", content);

const string contentTypes = """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
  <Default Extension="xml" ContentType="application/xml"/>
  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
  <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
  <Override PartName="/word/settings.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml"/>
</Types>
""";

const string rels = """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
</Relationships>
""";

const string docRels = """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"/>
""";

const string styles = """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
  <w:docDefaults>
    <w:rPrDefault><w:rPr><w:rFonts w:ascii="Calibri" w:hAnsi="Calibri" w:eastAsia="Microsoft JhengHei"/><w:sz w:val="22"/></w:rPr></w:rPrDefault>
    <w:pPrDefault><w:pPr><w:spacing w:after="120" w:line="300" w:lineRule="auto"/></w:pPr></w:pPrDefault>
  </w:docDefaults>
  <w:style w:type="paragraph" w:default="1" w:styleId="BodyText"><w:name w:val="Body Text"/><w:qFormat/><w:pPr><w:spacing w:after="120" w:line="300" w:lineRule="auto"/></w:pPr><w:rPr><w:rFonts w:ascii="Calibri" w:hAnsi="Calibri" w:eastAsia="Microsoft JhengHei"/><w:sz w:val="22"/></w:rPr></w:style>
  <w:style w:type="paragraph" w:styleId="Title"><w:name w:val="Title"/><w:qFormat/><w:pPr><w:spacing w:before="0" w:after="120"/></w:pPr><w:rPr><w:rFonts w:ascii="Calibri" w:hAnsi="Calibri" w:eastAsia="Microsoft JhengHei"/><w:b/><w:color w:val="0B2545"/><w:sz w:val="40"/></w:rPr></w:style>
  <w:style w:type="paragraph" w:styleId="Subtitle"><w:name w:val="Subtitle"/><w:qFormat/><w:pPr><w:spacing w:after="240"/></w:pPr><w:rPr><w:rFonts w:ascii="Calibri" w:hAnsi="Calibri" w:eastAsia="Microsoft JhengHei"/><w:color w:val="555555"/><w:sz w:val="24"/></w:rPr></w:style>
  <w:style w:type="paragraph" w:styleId="Heading1"><w:name w:val="heading 1"/><w:basedOn w:val="BodyText"/><w:next w:val="BodyText"/><w:qFormat/><w:pPr><w:keepNext/><w:spacing w:before="320" w:after="160"/></w:pPr><w:rPr><w:rFonts w:ascii="Calibri" w:hAnsi="Calibri" w:eastAsia="Microsoft JhengHei"/><w:b/><w:color w:val="2E74B5"/><w:sz w:val="32"/></w:rPr></w:style>
  <w:style w:type="paragraph" w:styleId="Heading2"><w:name w:val="heading 2"/><w:basedOn w:val="BodyText"/><w:next w:val="BodyText"/><w:qFormat/><w:pPr><w:keepNext/><w:spacing w:before="240" w:after="120"/></w:pPr><w:rPr><w:rFonts w:ascii="Calibri" w:hAnsi="Calibri" w:eastAsia="Microsoft JhengHei"/><w:b/><w:color w:val="2E74B5"/><w:sz w:val="26"/></w:rPr></w:style>
  <w:style w:type="paragraph" w:styleId="ListBullet"><w:name w:val="List Bullet"/><w:basedOn w:val="BodyText"/><w:pPr><w:ind w:left="360" w:hanging="0"/><w:spacing w:after="80" w:line="300" w:lineRule="auto"/></w:pPr><w:rPr><w:rFonts w:eastAsia="Microsoft JhengHei"/><w:sz w:val="22"/></w:rPr></w:style>
  <w:style w:type="paragraph" w:styleId="ListNumber"><w:name w:val="List Number"/><w:basedOn w:val="BodyText"/><w:pPr><w:ind w:left="360" w:hanging="0"/><w:spacing w:after="80" w:line="300" w:lineRule="auto"/></w:pPr><w:rPr><w:rFonts w:eastAsia="Microsoft JhengHei"/><w:sz w:val="22"/></w:rPr></w:style>
  <w:style w:type="paragraph" w:styleId="Code"><w:name w:val="Code"/><w:basedOn w:val="BodyText"/><w:pPr><w:spacing w:before="0" w:after="20" w:line="240" w:lineRule="auto"/><w:shd w:fill="F2F4F7"/></w:pPr><w:rPr><w:rFonts w:ascii="Consolas" w:hAnsi="Consolas" w:eastAsia="Microsoft JhengHei"/><w:sz w:val="18"/></w:rPr></w:style>
  <w:style w:type="paragraph" w:styleId="TableHeader"><w:name w:val="Table Header"/><w:pPr><w:spacing w:after="40"/></w:pPr><w:rPr><w:rFonts w:eastAsia="Microsoft JhengHei"/><w:b/><w:sz w:val="20"/></w:rPr></w:style>
  <w:style w:type="paragraph" w:styleId="TableText"><w:name w:val="Table Text"/><w:pPr><w:spacing w:after="40" w:line="260" w:lineRule="auto"/></w:pPr><w:rPr><w:rFonts w:eastAsia="Microsoft JhengHei"/><w:sz w:val="19"/></w:rPr></w:style>
  <w:style w:type="paragraph" w:styleId="CalloutLabel"><w:name w:val="Callout Label"/><w:pPr><w:spacing w:after="40"/></w:pPr><w:rPr><w:rFonts w:eastAsia="Microsoft JhengHei"/><w:b/><w:color w:val="1F3A5F"/><w:sz w:val="20"/></w:rPr></w:style>
  <w:style w:type="paragraph" w:styleId="CalloutText"><w:name w:val="Callout Text"/><w:pPr><w:spacing w:after="40" w:line="280" w:lineRule="auto"/></w:pPr><w:rPr><w:rFonts w:eastAsia="Microsoft JhengHei"/><w:sz w:val="20"/></w:rPr></w:style>
</w:styles>
""";

const string settings = """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:settings xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
  <w:zoom w:percent="100"/>
  <w:defaultTabStop w:val="720"/>
</w:settings>
""";

var document = $"""
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
  <w:body>
{body}
    <w:sectPr>
      <w:pgSz w:w="12240" w:h="15840"/>
      <w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440" w:header="708" w:footer="708" w:gutter="0"/>
    </w:sectPr>
  </w:body>
</w:document>
""";

await File.WriteAllTextAsync(Path.Combine(workDir, "[Content_Types].xml"), contentTypes, Encoding.UTF8);
await File.WriteAllTextAsync(Path.Combine(workDir, "_rels", ".rels"), rels, Encoding.UTF8);
await File.WriteAllTextAsync(Path.Combine(workDir, "word", "_rels", "document.xml.rels"), docRels, Encoding.UTF8);
await File.WriteAllTextAsync(Path.Combine(workDir, "word", "styles.xml"), styles, Encoding.UTF8);
await File.WriteAllTextAsync(Path.Combine(workDir, "word", "settings.xml"), settings, Encoding.UTF8);
await File.WriteAllTextAsync(Path.Combine(workDir, "word", "document.xml"), document, Encoding.UTF8);

if (File.Exists(outPath))
{
    File.Delete(outPath);
}

ZipFile.CreateFromDirectory(workDir, outPath, CompressionLevel.Optimal, includeBaseDirectory: false, Encoding.UTF8);

using var zip = ZipFile.OpenRead(outPath);
var requiredParts = new[] { "[Content_Types].xml", "_rels/.rels", "word/document.xml", "word/styles.xml", "word/settings.xml" };
foreach (var part in requiredParts)
{
    if (zip.GetEntry(part) is null)
    {
        throw new InvalidOperationException($"Missing DOCX part: {part}");
    }
}

var documentXml = XDocument.Load(Path.Combine(workDir, "word", "document.xml"));
var stylesXml = XDocument.Load(Path.Combine(workDir, "word", "styles.xml"));
XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

var metadata = new
{
    output = outPath,
    bytes = new FileInfo(outPath).Length,
    paragraphCount = documentXml.Descendants(w + "p").Count(),
    tableCount = documentXml.Descendants(w + "tbl").Count(),
    styleCount = stylesXml.Descendants(w + "style").Count(),
    renderQa = "Skipped: Python, LibreOffice, pdftoppm, and usable Word COM were unavailable in this shell.",
    structuralQa = "Passed: required DOCX package parts exist and document/styles XML parse successfully."
};

var validationPath = Path.Combine(taskDir, "docx-validation.json");
await File.WriteAllTextAsync(validationPath, JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);

Console.WriteLine(JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));
