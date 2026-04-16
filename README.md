# com.pie.agent

`com.pie.agent` is the Pie Unity host package built on top of [PuerTS](https://github.com/Tencent/puerts) V8.
It uses a runtime-first architecture: the agent core runs in Runtime, while the Editor provides a chat UI and authoring workflow.

## Highlights

- Streaming chat and tool execution inside Unity
- Runtime and Editor share the same agent core
- Project assets live under `Assets/Pie`
- Runtime state lives under `Application.persistentDataPath/Pie`
- Project skills, project extensions, and project memory are first-class package concepts

## Unity RPC Host

`com.pie.agent` exposes a local RPC/REST host for agent-driven Unity work.

Stable HTTP entrypoints:

- `GET /health`
- `GET /instances`
- `GET /manifest`
- `POST /tool/{name}`
- `POST /rpc/{method}`

Recommended host tools:

- `unity_scene_query`
- `unity_scene_object_inspect`
- `unity_scene_object_edit`
- `unity_refresh`
- `unity_log_read`
- `unity_script_run`

Tool naming rule:

- all model-visible tool names are lowercase
- only `a-z`, `0-9`, and `_` are used
- do not assume dot-delimited names like `unity.query`

This keeps provider compatibility predictable across OpenAI-compatible gateways and other hosts.

### Tool taxonomy / MECE boundary

PieChat's default agent surface should use one primary tool per concept:

- Files: `read_file`, `write_file`, `edit_file`, `list_dir`
- Search: `find_files`, `grep_text`
- Skills: `read_skill`, `read_resource`, `resolve_resource`
- Unity project/runtime context: `unity_project_inspect`
- Unity scene reads: `unity_scene_query`, `unity_scene_object_inspect`
- Unity scene object edits: `unity_scene_object_edit`
- Unity script tasks: `unity_script_run`
- Unity diagnostics: `unity_log_read`
- Interaction/state: `ask_user_multi`, `manage_todo_list`

Dev RPC no longer exposes duplicate project-file, target/result, skill, or editor convenience tools as primary manifest entries. External helper scripts should use local filesystem access for Unity project files and call `unity_refresh` when Unity must import changed assets; inside PieChat, prefer the normal file tools with project-root resolution.

`unity_scene_object_edit` is a scene object edit patch tool, including common component lifecycle/property patches such as add/remove/enable/set public field or property. Reads belong to `unity_scene_query` / `unity_scene_object_inspect`; logs belong to `unity_log_read`; short multi-frame Unity composition belongs to `unity_script_run`.

## Host Responsibility

`ARCHITECTURE.md` is the shared architecture source of truth for Pie.

`com.pie.agent` is responsible for mapping shared Pie semantics into Unity-native product experiences:

- shared commands become Unity buttons, menus, or runtime actions
- shared tool and status semantics become Unity panels, logs, or widgets
- shared interaction requests become Unity dialogs, selections, or custom editor/runtime flows

Unity can and should provide host-specific UX and richer workflows, but it should not redefine shared protocol meaning or push Unity-shaped UI APIs back into the shared layer.

Example:

- Positive: a shared `command` can be rendered as a toolbar action or menu item in Unity
- Negative: Unity-specific dialog or widget APIs should not become the shared cross-host contract

### PieChat reload / resume lifecycle guardrails

PieChat reload recovery is intentionally interrupt-and-resume, not transparent network-stream resume.

- Before Unity assembly reload, abort the active turn, cancel C# HTTP/file bridge work, close the Dev RPC listener, and wait only for a bounded time.
- After reload, create a fresh bridge and load the interrupted session state. Do not continue the old streaming response or reuse old AppDomain worker tasks.
- `SESSION_RECOVERY_WAS_STREAMING=true` means "the previous turn was interrupted by reload"; it does not mean "resume the old HTTP stream".
- `GET /health` must stay a readiness probe. It may check bounded main-thread responsiveness and request counts, but it must not call into PuerTS/JS or run agent/session code. Calling PuerTS from health previously caused native crash/reload hangs.
- During reload, connection refused, transient unavailable, or bounded `main_thread_timeout` health responses are acceptable. A socket that accepts requests but blocks indefinitely is not acceptable.
- PieChat rejects a second `send` while a turn is already busy; do not queue overlapping streaming turns unless a real queue is designed.
- Future `AgentSessionController` reuse must go through a Unity adapter. The adapter has to keep Unity's pending-user recovery as the source of truth before delegating to controller persistence, otherwise reload recovery can duplicate the interrupted user message.

## Requirements

`com.pie.agent` depends on the PuerTS Unity v3 runtime packages:

- Download `PuerTS_Core_3.0.2.tar.gz` and `PuerTS_V8_3.0.2.tar.gz` from the PuerTS `Unity_v3.0.2` release.
- Extract both archives locally and point Unity Package Manager at the extracted package directories.
- Do not install or depend on `PuerTS_Agent_3.0.2`, `Puerts.AI`, or `com.tencent.puerts.agent`; pie-unity owns the agent host layer.
- Do not use remote `https://*.tar.gz` dependencies or git tag/path dependencies for PuerTS in this setup.

Recommended `manifest.json` dependency values after extraction:

- `file:/absolute/path/to/extracted/PuerTS_Core_3.0.2/core`
- `file:/absolute/path/to/extracted/PuerTS_V8_3.0.2/v8`

If PuerTS Core or V8 is missing, `PieBridge` will not initialize and neither `Pie Chat` nor `PieRunner` will work.

## First Run Validation

After installation:

1. Open `Tools > Pie > Pie Chat`.
2. Run `/self-check` and confirm the model, tool route, web search, and file-understanding sections are healthy.
3. Send a short message and verify a response appears in PieChat.
4. Run a Unity scene query through PieChat.
5. Trigger an assembly reload and confirm PieChat reconnects without duplicating the interrupted prompt.

Release candidates should also run:

```bash
PIE_UNITY_REAL_RPC=1 npm run verify:unity:reliability
```

`unity_script_run` is a cooperative step-bounded runner: use generator tasks, yield for multi-frame work, and expect long synchronous loops to fail with `STEP_TIMEOUT` instead of being arbitrarily preempted.

## Installation

Install through Unity Package Manager:

- Local monorepo development:
  `Add package from disk...` and select `products/pie-unity/package.json`
- Git release repository:
  `Add package from git URL...`
- Current public Git URL:
  `https://github.com/Cydream-Tech/PieUnity.git#0.1.7`

After installation, you can import the `Extension Demo` sample from the Package Manager Samples section.

## Quick Start

### Editor

1. Open `Tools > Pie > Pie Chat`
2. Enter your `apiKey`, `provider`, `model`, and optional `baseUrl`
3. Start chatting

For CLI or agent-driven Unity control, use the built-in helper:

```bash
node products/cli/builtin/skills/pie-unity-rpc/pie-unity-rpc.js health --project "/abs/path/to/project"
node products/cli/builtin/skills/pie-unity-rpc/pie-unity-rpc.js manifest --project "/abs/path/to/project" --namespace unity
node products/cli/builtin/skills/pie-unity-rpc/pie-unity-rpc.js tool --project "/abs/path/to/project" --tool chat_send --data '{"text":"respond with exactly pong"}'
node products/cli/builtin/skills/pie-unity-rpc/pie-unity-rpc.js tool --project "/abs/path/to/project" --tool chat_get_state
```

Available local commands include:

- `/new`
- `/resume <sessionId>`
- `/tree`
- `/fork` or `/fork <sessionId>`
- `/self-check`
- `/session-check`
- `/model`
- `/skills`
- `/skills reload`
- `/memory`
- `/init`

### Runtime

1. Add `PieRunner` to a GameObject
2. Optionally assign `Settings Override`
3. Call `SetConfig(...)`
4. Call `SendMessage(...)`
5. Listen to `OnAssistantMessage`, `OnAssistantDelta`, `OnToolStart`, `OnToolEnd`, and `OnError`

Example:

```csharp
using System.IO;
using UnityEngine;

public class Demo : MonoBehaviour
{
    [SerializeField] private Pie.PieRunner runner;

    private void Start()
    {
        runner.Reinitialize(Path.Combine(Application.persistentDataPath, "PieWorkspace"));
        runner.SetConfig("YOUR_API_KEY", "openai", "gpt-4.1-mini", "https://api.openai.com/v1");
        runner.OnAssistantMessage += msg => Debug.Log("AI: " + msg);
        runner.SendMessage("Create a cube at (0,1,0).");
    }
}
```

`pie-unity` now reads models only from `~/.pie/models.json`. The file must use top-level `profiles`, not `providers`, and each profile must explicitly declare:

- `api`
- `baseUrl`
- `apiKey` or `apiKeyEnv`
- `models`

If no valid profile is configured, Unity starts in an unconfigured model state and asks you to configure `~/.pie/models.json` before sending messages.

## Paths

Project assets:

- `Assets/Pie/PieSettings.asset`
- `Assets/Pie/Skills`
- `Assets/Pie/Extensions`
- `Assets/Pie/AGENTS.md`

Runtime state:

- `Application.persistentDataPath/Pie/sessions`
- `Application.persistentDataPath/Pie/...`

Imported UPM samples are only staging content. If Unity imports a sample into `Assets/Samples/...`, the actual working files for `pie-unity` should still end up under `Assets/Pie/...`.

## Pie Settings

Create a settings asset from:

- `Tools > Pie > Create Pie Settings Asset`

`PieSettings` currently exposes:

- `Extension Search Paths`
- `Skill Search Paths`
- `File Tool Roots`
- `Default File Tool Root Editor`
- `Default File Tool Root Runtime`

File tool roots let you expose named filesystem roots to `read_file`, `write_file`, `edit_file`, `list_dir`, `grep_text`, and `find_files`.

Built-in roots:

- `persistent`: maps to `Application.persistentDataPath`
- `project`: maps to the Unity project root and is Editor-only

Each configured root can define:

- `Name`
- `Description`
- `Relative Path From Project Root` or `Absolute Path`
- `Available In Editor`
- `Available In Runtime`
- `Default Prefixes`

Paths are scanned in order, and later paths override earlier items with the same skill or extension name.

Connection settings:

- `apiKey` authenticates with the selected provider
- `provider` selects the built-in API family
- `model` selects the model ID
- `baseUrl` is optional; when set, it overrides the built-in provider endpoint

When changing config through `pie_chat.set_config` from the CLI helper, the helper waits briefly for the Unity-side bridge to apply the new settings before returning.

## Skills

Default project skill directory:

- `Assets/Pie/Skills`

Supported layouts:

- `Assets/Pie/Skills/<skill-name>/SKILL.md`
- `Assets/Pie/Skills/<skill-name>.md`

Skills are listed in the system prompt and can be read in full through the `read_skill` tool.

## Project Extensions

Default project extension directory:

- `Assets/Pie/Extensions`

Each extension is a `*.js` file and should export a module function. Use either:

- `module.exports = (api) => { ... }`
- `export default function (api) { ... }`

The `api` object is a Unity host binding over `@pie/agent-framework`'s runtime extension contract. It includes the shared runtime fields:

- `manifest`
- `dir`
- `config`
- `log()`
- `registerTool()`
- `registerBeforeToolCall()`
- `registerAfterToolCall()`
- `registerOnError()`
- `appendSystemPrompt()`
- `callHost()`

Unity also adds:

- `projectRoot`
- `isEditor`

Minimal example:

```js
module.exports = (api) => {
  api.appendSystemPrompt("Use the hello_world tool for greeting demos.");

  api.registerTool({
    name: "hello_world",
    label: "hello_world",
    description: "Return a greeting string.",
    parameters: {
      type: "object",
      properties: {},
    },
    async execute() {
      return {
        content: [{ type: "text", text: "Hello from project extension." }],
        details: {},
      };
    },
  });
};
```

## Recommended Smoke Test

After opening `Tools > Pie > Pie Chat`, run these local commands in order:

1. `/self-check`
2. `/skills`
3. `/extensions`
4. `/extensions reload`
5. `/session-check`

Then send two real prompts:

1. `演示一下 ask 工具`
   Expected: Unity should show an interaction UI instead of appearing to hang.
2. `演示一下 todo 工具，并持续执行直到 todo 结束`
   Expected: todo state should progress and clear when complete.

For repeatable regression steps and reliability runs, see:

- [TESTING.md](/Users/fonzie/Documents/AIProjects/Pie/products/pie-unity/TESTING.md)

## Sessions And Memory

Project memory:

- Stored at `Assets/Pie/AGENTS.md`
- Shared with the Unity project
- Intended for project conventions and persistent guidance

Session and runtime state:

- Stored under `Application.persistentDataPath/Pie`
- Intended for local session history and runtime-owned state

## Standalone Release Repository

`pie-unity` is developed inside the `pie` monorepo and exported to a standalone Git repository for distribution.

From the monorepo root:

```bash
npm run export:pie-unity -- \
  --target /absolute/path/to/pie-unity-release \
  --repo-url https://github.com/Cydream-Tech/PieUnity.git
```

This export flow:

- rebuilds `Resources/pie/core.js`
- copies only the distributable Unity package payload
- rewrites `package.json` for standalone distribution

## License

`pie-unity` is distributed under `Apache-2.0`.
