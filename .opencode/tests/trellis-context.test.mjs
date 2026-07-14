import assert from "node:assert/strict"
import { mkdtempSync, mkdirSync, readFileSync, rmSync, writeFileSync } from "node:fs"
import { tmpdir } from "node:os"
import { join } from "node:path"
import test from "node:test"

import { TrellisContext } from "../lib/trellis-context.js"
import injectSubagentContext from "../plugins/inject-subagent-context.js"


function createSoleSessionRepository() {
  const repository = mkdtempSync(join(tmpdir(), "trellis-context-"))
  mkdirSync(join(repository, ".trellis", "tasks", "foreign"), { recursive: true })
  mkdirSync(join(repository, ".trellis", ".runtime", "sessions"), { recursive: true })
  writeFileSync(
    join(repository, ".trellis", ".runtime", "sessions", "foreign.json"),
    JSON.stringify({ current_task: ".trellis/tasks/foreign" }),
  )
  return repository
}

function createFixtureRepository({ sessions = [], tasks = [] } = {}) {
  const repository = mkdtempSync(join(tmpdir(), "trellis-context-"))
  for (const task of tasks) {
    mkdirSync(join(repository, ".trellis", "tasks", task), { recursive: true })
  }
  for (const { key, payload } of sessions) {
    const sessionDirectory = join(repository, ".trellis", ".runtime", "sessions")
    mkdirSync(sessionDirectory, { recursive: true })
    writeFileSync(
      join(sessionDirectory, `${key}.json`),
      typeof payload === "string" ? payload : JSON.stringify(payload),
    )
  }
  return repository
}

test("OpenCode does not adopt a sole session without explicit recovery", () => {
  const repository = createSoleSessionRepository()
  try {
    const result = new TrellisContext(repository).getActiveTask()
    assert.deepEqual(result, { taskPath: null, source: "none", stale: false })
  } finally {
    rmSync(repository, { recursive: true, force: true })
  }
})

test("OpenCode permits the explicit sole-session recovery option", () => {
  const repository = createSoleSessionRepository()
  try {
    const result = new TrellisContext(repository).getActiveTask(null, { useSoleSession: true })
    assert.deepEqual(result, {
      taskPath: ".trellis/tasks/foreign",
      source: "session-fallback:foreign",
      stale: false,
    })
  } finally {
    rmSync(repository, { recursive: true, force: true })
  }
})

test("OpenCode matches the canonical active-task fixture matrix", () => {
  const cases = [
    {
      name: "zero sessions",
      fixture: {},
      input: null,
      options: {},
      expected: { taskPath: null, source: "none", stale: false },
    },
    {
      name: "one session without identity",
      fixture: {
        tasks: ["foreign"],
        sessions: [{ key: "foreign", payload: { current_task: ".trellis/tasks/foreign" } }],
      },
      input: null,
      options: {},
      expected: { taskPath: null, source: "none", stale: false },
    },
    {
      name: "multiple sessions with explicit recovery",
      fixture: {
        tasks: ["first", "second"],
        sessions: [
          { key: "first", payload: { current_task: ".trellis/tasks/first" } },
          { key: "second", payload: { current_task: ".trellis/tasks/second" } },
        ],
      },
      input: null,
      options: { useSoleSession: true },
      expected: { taskPath: null, source: "none", stale: false },
    },
    {
      name: "explicit identity",
      fixture: {
        tasks: ["owned"],
        sessions: [{ key: "opencode_worker", payload: { current_task: ".trellis/tasks/owned" } }],
      },
      input: { sessionId: "worker" },
      options: {},
      expected: {
        taskPath: ".trellis/tasks/owned",
        source: "session:opencode_worker",
        stale: false,
      },
    },
    {
      name: "stale explicit identity",
      fixture: {
        sessions: [{ key: "opencode_worker", payload: { current_task: ".trellis/tasks/missing" } }],
      },
      input: { sessionId: "worker" },
      options: {},
      expected: {
        taskPath: ".trellis/tasks/missing",
        source: "session:opencode_worker",
        stale: true,
      },
    },
    {
      name: "malformed session state",
      fixture: { sessions: [{ key: "foreign", payload: "{not-json" }] },
      input: null,
      options: { useSoleSession: true },
      expected: { taskPath: null, source: "none", stale: false },
    },
  ]

  for (const fixtureCase of cases) {
    const repository = createFixtureRepository(fixtureCase.fixture)
    try {
      assert.deepEqual(
        new TrellisContext(repository).getActiveTask(fixtureCase.input, fixtureCase.options),
        fixtureCase.expected,
        fixtureCase.name,
      )
    } finally {
      rmSync(repository, { recursive: true, force: true })
    }
  }
})

test("subagent injector accepts only an explicit Active task dispatch hint", async () => {
  const repository = createFixtureRepository({ tasks: ["hinted"] })
  try {
    writeFileSync(
      join(repository, ".trellis", "tasks", "hinted", "prd.md"),
      "# Hint fixture\n",
    )
    const hooks = await injectSubagentContext({ directory: repository })
    const output = {
      args: {
        subagent_type: "trellis-implement",
        prompt: "Active task: .trellis/tasks/hinted\n\nUse the explicit task.",
      },
    }

    await hooks["tool.execute.before"]({ tool: "task" }, output)

    assert.match(output.args.prompt, /trellis-hook-injected/)
    assert.match(output.args.prompt, /Hint fixture/)
  } finally {
    rmSync(repository, { recursive: true, force: true })
  }
})

test("subagent injector has no independent sole-session fallback", () => {
  const source = readFileSync(
    new URL("../plugins/inject-subagent-context.js", import.meta.url),
    "utf-8",
  )
  assert.doesNotMatch(source, /_resolveSingleSessionFallback\s*\(/)
})
