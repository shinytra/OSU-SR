# Model routing (read before every request)

The main session runs on **Sonnet** (`.claude/settings.json`). Before doing anything for a request,
decide in one line **who executes it** and **who answers**, then act. The goal is to save tokens
without lowering code quality.

## 1. Who executes

| Request | Executes | Why |
|---|---|---|
| Question answerable from what is already in context, tiny edit (a few lines, one file) | main session (Sonnet), directly | a subagent starts cold and would re-read everything: more tokens, not fewer |
| Find where something is, read/summarize files or logs, list usages | `explorer` (Haiku) | cheap, read-only, keeps file dumps out of the main context |
| Well-specified code change, tests, small refactor, clear bug fix | `implementer` (Sonnet), or main session if the files are already open | routine work, Sonnet is enough |
| Algorithm or architecture: skillset/pattern detection, strain/difficulty math, segment scoring, transitions, audio/timing sync, cross-cutting refactor, a bug that survived one fix | `architect` (Opus) | where quality matters most and a wrong design costs more than the tokens |
| Non-trivial diff about to be committed or put in a PR | `reviewer` (Sonnet) | catches bugs before lucas tests on his PC |

Rules of thumb:
- Mixed request: split it. `explorer` gathers the facts, `architect` designs, `implementer` writes the code.
- Do not delegate when the cost of explaining the task is close to the cost of doing it.
- Give the subagent everything it needs in the prompt (files, goal, constraints): it does not see this conversation.
- If a long back-and-forth design discussion is coming (not a single task), suggest lucas switch the
  main session to Opus with `/model opus` instead of relaying through `architect` many times.

## 2. Who answers

- **The main session always writes the reply to lucas**, in French, from the subagent's report.
  Subagent output is not shown to the user, so relay what matters: the result, what was verified, what is left.
- Keep the reply short; do not paste the subagent's whole report.
- Start the reply with one line saying who did the work when a subagent was used,
  e.g. `(explorer/Haiku)` or `(architect/Opus)`, so lucas can see where tokens go.
