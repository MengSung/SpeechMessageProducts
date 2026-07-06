# Superpowers / Trellis / CCG 環境審核報告

日期：2026-07-06  
專案：ChurchReport

## 結論摘要

目前環境同時存在三套協作機制：

- **Trellis**：專案主工作流層，負責 task、spec、workflow-state、hook、finish-work。
- **Superpowers**：通用技能與方法論層，負責 brainstorming、TDD、debug、verification、review 等工程流程輔助。
- **CCG**：多模型升級層，負責 Gemini + Claude 雙模型分析、審查與 self-healing runner。

一般啟動順序是：

1. 專案指令與 user/plugin 設定先載入。
2. Trellis hook / workflow-state 最早自動介入。
3. 依任務類型選用 Trellis skill 或 Superpowers skill。
4. 若任務達到 M+ 複雜度、高風險、或需要外部交叉審查，才進入 CCG。

一句話：**Trellis 管專案流程，Superpowers 管工程方法，CCG 管多模型升級審查。**

## 1. Superpowers 定位

Superpowers 是使用者層級 plugin，不是專案本身的一部分。

已確認的 Superpowers skill 包含：

- `using-superpowers`
- `brainstorming`
- `test-driven-development`
- `systematic-debugging`
- `verification-before-completion`
- `requesting-code-review`
- `receiving-code-review`
- `writing-plans`
- `executing-plans`
- `dispatching-parallel-agents`
- `subagent-driven-development`
- `using-git-worktrees`
- `finishing-a-development-branch`
- `writing-skills`

它的作用是提供跨專案的開發方法論。例如：

- 新功能或需求不清楚時，用 brainstorming。
- 實作功能或 bugfix 時，用 TDD。
- 遇到 bug 或測試失敗時，用 systematic debugging。
- 要宣稱完成前，用 verification-before-completion。
- 完成較大變更後，用 requesting-code-review。

Superpowers 通常不會自己管理 `.trellis/tasks` 或 `.ccg/tasks`，除非目前任務需要讀取或修改專案檔案。

## 2. Trellis 定位

Trellis 是這個 repo 的專案工作流核心。

主要檔案與目錄：

- `.trellis/workflow.md`
- `.trellis/config.yaml`
- `.trellis/spec/`
- `.trellis/tasks/`
- `.trellis/workspace/`
- `.agents/skills/trellis-*`
- `.codex/hooks.json`
- `.codex/hooks/inject-workflow-state.py`

Trellis 負責：

- 建立與啟動任務。
- 維護 active task。
- 注入 workflow-state。
- 讀取專案 spec。
- 在實作前後引導 `trellis-before-dev`、`trellis-check`、`trellis-update-spec`、`trellis-finish-work`。
- 管理任務歸檔與 session journal。

目前 `.codex/hooks.json` 設定了 `UserPromptSubmit` hook：

```json
{
  "hooks": {
    "UserPromptSubmit": [
      {
        "hooks": [
          {
            "type": "command",
            "command": "python -X utf8 .codex/hooks/inject-workflow-state.py",
            "timeout": 15
          }
        ]
      }
    ]
  }
}
```

這代表每次送出 prompt 時，Trellis 會嘗試注入目前任務狀態。

目前 Trellis 在 Codex 的模式是 **inline**：

- 主 session 直接讀 context、實作、檢查。
- 不派出 Trellis implement/check sub-agent。

## 3. CCG 定位

CCG 是多模型協作與審查層。

主要檔案與目錄：

- `.ccg/tasks/`
- `.ccg/dual-model-runs/`
- `AGENTS.md`
- `docs/scripts/Start-CcgDualModelRun.ps1`
- `docs/scripts/Invoke-CcgDualModelWithSelfHealing.ps1`

CCG 的重點是：

- 依任務複雜度與風險決定是否升級。
- M+ 複雜度要求 Gemini + Claude 雙模型分析。
- 變更後若達到審查門檻，要 Gemini + Claude 雙模型 review。
- 目前專案規則要求不要直接呼叫 Gemini/Claude，而是使用 `Start-CcgDualModelRun.ps1` self-healing runner。

CCG 比較像「審查與外部模型協作制度」，不是第一層日常 task workflow。

## 4. 三者互相調用關係

### Trellis 與 Superpowers

Trellis 不會直接呼叫 Superpowers。  
Superpowers 也不會直接呼叫 Trellis。

實際關係是：

- Trellis 告訴 AI 現在處於哪個專案階段。
- Superpowers 在符合任務情境時提供工程方法。

例如：

- Trellis 說目前是 `in_progress-inline`。
- 若任務是 bugfix，AI 可能同時採用 Superpowers 的 `systematic-debugging`。
- 若要宣稱完成，AI 可能同時採用 Superpowers 的 `verification-before-completion` 與 Trellis 的 `trellis-check`。

### Trellis 與 CCG

Trellis 管理專案任務生命週期。  
CCG 管理外部雙模型分析與審查。

實際關係是：

- Trellis 的 active task 可能是目前主工作項。
- CCG 在需要外部 Gemini + Claude 交叉驗證時介入。
- CCG 的結果可以寫回 `.ccg/tasks/*/review.md` 或 `.ccg/dual-model-runs/*`。

### Superpowers 與 CCG

Superpowers 提供一般工程方法。  
CCG 提供特定的多模型制度。

例如：

- Superpowers 的 `requesting-code-review` 是一般 code review 方法。
- CCG 的 review 是專案規定的 Gemini + Claude 雙模型審查。

若兩者同時適用，CCG 規則通常更具體，應優先滿足 CCG 的雙模型審查要求。

## 5. 一開始通常會調用哪一個

通常順序如下：

1. **Trellis hook / workflow-state**
   - 最早自動介入。
   - 透過 `.codex/hooks.json` 和 `inject-workflow-state.py` 注入 active task 與 workflow 階段。

2. **Trellis skill**
   - 若是專案開發任務，通常接著用 `trellis-start`、`trellis-before-dev`、`trellis-check` 等。

3. **Superpowers skill**
   - 若任務符合特定工程方法，例如 brainstorming、TDD、debug、verification，就會疊加使用。

4. **CCG**
   - 當任務達到 M+ 複雜度、高風險、或需要外部審查時才進入。

所以一開始最常見的是：**Trellis 先進來，Superpowers 視任務類型輔助，CCG 最後在需要升級時介入。**

## 6. 目前環境已確認狀態

已確認：

- Superpowers plugin 已啟用。
- Trellis 專案目錄存在：`.trellis/`。
- Trellis skills 存在：`.agents/skills/trellis-*`。
- Codex hook 設定存在：`.codex/hooks.json`。
- CCG 任務目錄存在：`.ccg/tasks/`。
- CCG dual model run 目錄存在：`.ccg/dual-model-runs/`。
- Gemini 與 Claude 指令在目前 PowerShell 環境可找到。
- `docs/scripts/Start-CcgDualModelRun.ps1` 與 self-healing runner 存在。

需要注意：

- 目前 PowerShell PATH 查不到 `codex` 指令。
- 目前 PowerShell PATH 查不到 `trellis` 指令。
- user-level Codex config 裡有 hook trusted hash，但未看到明確 `[features].hooks = true`。
- 這不代表桌面 runtime 內不能運作，只代表一般 PowerShell 直接呼叫 `codex` / `trellis` 會失敗。

## 7. 主要風險

### 7.1 Trellis task 與 CCG task 可能不同步

目前上下文曾出現不同 active task：

- Trellis workflow-state 指向 `payment-module-extraction`。
- CCG state 曾指向 LINE RichMenu / Word 文件任務。

這代表兩套任務系統可能同時存在但沒有自動同步。

風險：

- AI 可能根據 Trellis 做一件事，根據 CCG 又做另一件事。
- review、archive、finish-work 的對象可能不一致。
- 長任務中容易發生任務漂移。

建議：

- 每次正式實作前，先明確確認當前主任務。
- 若使用 Trellis 作為主流程，CCG task 應該對齊同一需求。
- 若使用 CCG 作為主流程，也應同步或至少記錄 Trellis active task 狀態。

### 7.2 Hook 啟用狀態需要再確認

專案 hook 檔案存在，但是否每輪都穩定觸發，取決於：

- user-level config 是否啟用 hooks。
- `/hooks` TUI 是否批准專案 hook。
- Python 是否在 hook 執行環境可用。

建議：

- 在 Codex 介面中檢查 `/hooks`。
- 確認專案 hook 已批准。
- 若 hook 沒觸發，檢查 user-level `[features].hooks = true`。

### 7.3 PowerShell PATH 與 runtime PATH 不一致

PowerShell 查不到：

- `codex`
- `trellis`
- `python`

但專案 hook 使用：

```powershell
python -X utf8 .codex/hooks/inject-workflow-state.py
```

風險：

- 若 hook 執行環境沒有自己的 Python resolution，hook 可能失敗。
- Trellis CLI 若只存在於特定 runtime，不適合在一般 PowerShell 中直接假設可用。

建議：

- 確認 hook 執行時的 Python 是否可用。
- 若需要人工 CLI 操作，使用專案已存在的 `.trellis/scripts/*.py` 或明確 runtime 路徑。

## 8. 建議操作準則

建議把三者分工固定如下：

1. **日常專案流程以 Trellis 為主**
   - task、spec、workflow、finish-work 都走 Trellis。

2. **工程方法用 Superpowers 補強**
   - 需求不清楚：brainstorming。
   - 寫功能或 bugfix：test-driven-development。
   - 查 bug：systematic-debugging。
   - 完成前：verification-before-completion。

3. **複雜/高風險/需要審查時升級 CCG**
   - M+ 複雜度：雙模型分析。
   - 重要變更：雙模型 review。
   - 使用 `Start-CcgDualModelRun.ps1`，不要手動直接呼叫 Gemini/Claude。

4. **開始實作前先對齊 task**
   - 確認 Trellis active task。
   - 確認 CCG task 是否同一件事。
   - 若不同，先選定本輪主任務。

5. **避免三套流程同時搶主導權**
   - Trellis 是主流程。
   - Superpowers 是方法論。
   - CCG 是升級審查。

## 9. 最終判斷

目前環境不是壞掉，而是「功能很多、層級重疊」。

最穩定的使用模型是：

```text
Trellis 先決定目前專案任務與階段
  -> Superpowers 依任務類型提供工程方法
    -> CCG 在 M+ 或高風險時做 Gemini + Claude 雙模型分析/審查
```

也就是：

- **先 Trellis**
- **再 Superpowers**
- **需要升級時才 CCG**

目前最值得修正的是：**讓 Trellis active task 與 CCG active task 在每次工作前明確對齊。**
