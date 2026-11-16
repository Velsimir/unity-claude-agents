# Unity Claude Agents — showcase

A curated slice of my personal agent library for [Claude Code](https://claude.com/claude-code),
built for day-to-day Unity development. The full library is private: 25 agents, 13 skills and
26 knowledge bases. This repository shows six of them, plus the knowledge they read.

The agents do not chat about Unity. They work inside the Unity Editor through
[MCP for Unity](https://github.com/CoplayDev/unity-mcp): they create and edit scenes, prefabs and
ScriptableObjects, read the console, run checks and stop for approval at fixed gates.

> Agent prompts and knowledge bases are written in Russian, my working language.
> Everything a reviewer needs to understand the design is in this README.

## Results on real work

- Playable-ad production cycle: from **~7 working days** (rebuilt from art alone) down to
  **1–2 days** per reference copy or strip-down; routine variations ship at 1–2 builds a day.
- 40+ playable builds for 10 mobile titles went to production, five reached CTR above 50%.
- New mobile project foundation (DI, boot pipeline, save system, UI stack, platform facade)
  assembled by one agent run instead of several days of boilerplate.

## Design principles

1. **Procedure in the agent, facts in the knowledge base.** An agent file says *what to do and in
   which order*. Versions, API details, traps and checklists live in `Database/` and are read
   only through an explicit pointer. Updating a fact never means rewriting a prompt.
2. **One item, one job.** If a description needs "and also", it becomes two items.
3. **Gates, not autonomy.** Agents propose a plan, wait for approval, then act. Builds, deletions
   and anything hard to undo are the owner's call.
4. **Verify, don't claim.** "Done" means compiled, console clean, Play Mode checked, screenshot of
   the Game view — not "the code looks right".
5. **Code graph before grep.** Agents navigate code through a symbol graph
   ([CodeGraph](Database/codegraph/CODEGRAPH.md)) instead of chains of text search.
6. **Generated code answers to the same review as hand-written code.** If I cannot defend it,
   it does not ship.

## What is inside

| Item | Type | What it does |
| --- | --- | --- |
| [`unity-foundation`](agents/Unity%20Foundation/unity-foundation.md) | agent | Builds the architectural foundation of a new mobile Unity project: Zenject installers, boot scene with an async loading pipeline, enum-based scene loader, UI pages with stack and layers, sectioned saves, platform SDK facade, R3 / MessagePipe / UniTask / DOTween. Ships with C# templates in [`Database/unity-foundation/TEMPLATES`](Database/unity-foundation/TEMPLATES) |
| [`unity-architecture-recon`](skills/unity-architecture-recon/SKILL.md) | skill | Detects the architecture of an unfamiliar project (DI container, saves, UI, tick and input, event bus, ECS or OOP), matches it to a profile from [`Database/unity-architectures`](Database/unity-architectures) and records the result in the project's `CLAUDE.md`, so every later agent knows where code belongs |
| [`architecture-review`](agents/Architecture%20Review/architecture-review.md) | agent | Critique-only architecture review in the spirit of Udi Dahan and Particular Software: real business modules, data ownership, coupling, UI-centric vs Composite UI, rules / orchestration / infrastructure / presentation inside a module. Backed by 19 principles and 27 Unity-specific smells |
| [`leoecs-review`](agents/Unity%20ECS/leoecs-review.md) | agent | Review of LeoEcs Lite + Zenject code for silent bugs: a forgotten `ref`, `Get` outside a filter, `GetRawEntities().Length`, stale entities, broken stage pipeline, leaking entities |
| [`gd-economy`](agents/Game%20Design%20Economy/gd-economy.md) | agent | Game economy and balance: currencies, sources and sinks, cost and reward curves, progression gates, offline income, difficulty by attempt rate. Models in CSV and simulates per player persona instead of eyeballing ([`simulate.py`](Database/gamedesign/TEMPLATES/simulate.py)) |
| [`gauntlet-loop`](skills/gauntlet-loop/SKILL.md) | skill | Iterative polish toward a reference by blind A/B: frozen rubric, one gap per round, verdict written to disk before the key is revealed, all state in `.gauntlet/` so it survives context resets and can run overnight |

Knowledge bases included: `unity-foundation` (architecture, boot, saves, UI, platform SDK, code
style), `unity-architectures`, `architecture-review`, `leoecs-lite`, `unity-dots`, `gamedesign`,
`gamecraft` (game feel: feedback, motion, graphics, UX, polish), `codegraph`, `unity-plugins`,
`unity-cli`. Two always-on rules: `codegraph` and `project-claude-md`.

## How the pieces connect

```
gd-concept ─▶ gd-economy ─▶ unity-foundation ─▶ unity-mechanic-integrator
                                   │
unity-architecture-recon ─▶ architecture-review ─▶ integrators / owner
                                   │
                      leoecs-mechanic-integrator ─▶ leoecs-review
```

Items not in this repository (platform porting agents, playable-ad pipeline, art generation in
ComfyUI and Blender, the reusable mechanics library) are referenced by name in "neighbours"
sections; their files stay private. Some pointers inside the included files therefore lead to
paths that are not here.

## Using it

The layout mirrors `~/.claude/`. Copy the folders you want into your own `~/.claude/`:

```
agents/<Folder>/<agent>.md      subagents, auto-discovered
skills/<skill>/SKILL.md         skills, auto-discovered
rules/<rule>/<rule>.md          rules, loaded every session
Database/<topic>/<DOC>.md       knowledge bases, read only via pointers
```

Tools the agents expect: Claude Code, MCP for Unity, CodeGraph, Unity 6 / 2022.3+.

## License

[CC BY-NC 4.0](LICENSE): read it, learn from it, adapt it for yourself with attribution.
Commercial use requires my permission.

## Author

Ivan Koniashov — Unity Developer, Yerevan (UTC+4).
GitHub [@Velsimir](https://github.com/Velsimir) · Telegram @KoniashovIvan
