# Pie Unity Agent

`pie-unity` is a Unity AI agent package built on top of [PuerTS](https://github.com/Tencent/puerts) V8.
It uses a runtime-first architecture: the agent core runs in Runtime, while the Editor provides a chat UI and authoring workflow.

## Highlights

- Streaming chat and tool execution inside Unity
- Runtime and Editor share the same agent core
- Project assets live under `Assets/Pie`
- Runtime state lives under `Application.persistentDataPath/Pie`
- Project skills, project extensions, and project memory are first-class package concepts

## Host Responsibility

`ARCHITECTURE.md` is the shared architecture source of truth for Pie.

`pie-unity` is responsible for mapping shared Pie semantics into Unity-native product experiences:

- shared commands become Unity buttons, menus, or runtime actions
- shared tool and status semantics become Unity panels, logs, or widgets
- shared interaction requests become Unity dialogs, selections, or custom editor/runtime flows

Unity can and should provide host-specific UX and richer workflows, but it should not redefine shared protocol meaning or push Unity-shaped UI APIs back into the shared layer.

Example:

- Positive: a shared `command` can be rendered as a toolbar action or menu item in Unity
- Negative: Unity-specific dialog or widget APIs should not become the shared cross-host contract

## Requirements

`pie-unity` depends on PuerTS:

- Install `com.tencent.puerts.core`
- Use the PuerTS V8 backend
- Include the native V8 plugin for your target platform

If PuerTS or the V8 runtime is missing, `PieBridge` will not initialize and neither `Pie Chat` nor `PieRunner` will work.

## Installation

Install through Unity Package Manager:

- Local monorepo development:
  `Add package from disk...` and select `products/pie-unity/package.json`
- Git release repository:
  `Add package from git URL...`
- Current public Git URL:
  `https://github.com/Cydream-Tech/PieUnity.git#0.1.6`

After installation, you can import the `Extension Demo` sample from the Package Manager Samples section.

## Quick Start

### Editor

1. Open `Tools > Pie > Pie Chat`
2. Enter your `apiKey`, `provider`, `model`, and optional `baseUrl`
3. Start chatting

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

File tool roots let you expose named filesystem roots to `read_file`, `write_file`, `edit_file`, `list_directory`, `search_file_content`, and `search_files`.

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
