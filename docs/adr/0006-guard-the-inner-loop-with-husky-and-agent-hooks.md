# Guard the inner loop with Husky and agent hooks

## Status

Accepted

## Created / Updated

18-Oct-2026

## Deciders

Marnix van Valen

## Work Item

None; the change was made on the `inner-loop-guardrails` branch.

## Context

Developers and coding agents change this repository. The team mostly uses GitHub Copilot (the CLI, the app and VS Code); some use Claude Code. CI checks the build, formatting and Bicep, but finding a problem there costs a round trip. Agents also tend to end a turn without running all checks, and each agent tool configures hooks in its own file.

## Decision drivers

* One definition of each check, the same locally and in CI
* Feedback before a commit, and before an agent says it is done
* Copilot first; Claude Code support is a bonus
* No duplicate runs when a tool reads several hook files

## Considered options

### CI only

**Pro:**
* Nothing to install locally

**Con:**
* Slow feedback; broken commits reach the remote

### Instructions in AGENTS.md only

**Pro:**
* No tooling

**Con:**
* Agents don't always follow them, and nothing checks that they did

### Husky.Net tasks, run by Git and by agent Stop hooks

Husky.Net (a local dotnet tool) defines every check once in `.husky/task-runner.json`, in two groups: `pre-commit` for staged files, and `uncommitted` for everything changed since the last commit. Git runs `pre-commit`. A Stop hook, `scripts/agent-stop-hook.sh`, runs `uncommitted` when an agent ends a turn and returns any failure to the agent.

**Pro:**
* The same checks for people and agents, defined in one place
* Agents fix problems before they report back

**Con:**
* Developers must install the hooks and the tools the checks need (gitleaks, the Bicep CLI)
* Agent hook formats differ per tool and change often

## Decision

The decision has been made to define the checks as Husky.Net tasks and run them from Git and from agent Stop hooks, because it gives people and agents the same fast feedback from one definition.

The Stop hook is registered in `.github/hooks/poolcar.json`, which Copilot CLI, the Copilot app, VS Code and the Copilot cloud agent read. Claude Code reads `.claude/settings.json` instead. Copilot CLI reads that file too, so the hook skips itself there when it runs under Copilot. The hook remembers the working tree it last checked and doesn't check it again, so an agent that can't fix a failure can still stop.

### Consequences

* Banned APIs (`BannedSymbols.txt`), formatting, Bicep lint and Markdown links fail locally before CI sees them.
* A check that is added to one group must also be added to the other; Husky.Net tasks belong to one group only.
* After an agent ends a turn with changed code, the hook takes about 10 seconds; with no changes it returns at once.

## Links

* [Development: checks and coding agents](../development.md#checks)
* [Husky.Net task configuration](https://alirezanet.github.io/Husky.Net/guide/task-configuration.html)
* [GitHub Copilot hooks reference](https://docs.github.com/en/copilot/reference/hooks-reference)
* [Claude Code hooks](https://docs.claude.com/en/docs/claude-code/hooks)
