---
name: pie-unity-rpc
description: Connect to a local pie-unity instance for Unity project discovery, health checks, manifest lookup, and a small set of stable RPC/tool calls. Use when working on a Unity project that imports pie-unity.
---

# Pie Unity RPC

Canonical source: this skill ships with the `com.pie.agent` Unity package under `Skills/pie-unity-rpc`. Use the copy from the current Unity project's resolved package so the helper, host manifest, and protocol version stay aligned. Do not use a copied global skill.

Use the helper script `pie-unity-rpc.js` to talk to local `pie-unity` instances. Prefer the shell-style host workflow:

1. Discover instances
2. Select the target project or instance
3. Check health
4. Read only the manifest slice you need, including protocol/schema metadata
5. Query or inspect Unity state
6. Edit the scene, read logs, refresh Unity after file changes, or run a frame-scheduled Unity script task
7. Verify the result

Do not dump the full manifest unless the user explicitly asks for it. If the helper prints `skillProtocolWarning`, report the warning and prefer read-only inspection until the host/helper mismatch is resolved.

Script resource: `pie-unity-rpc.js`

Command templates below use `<script>` as a placeholder for the resolved script path in the current host.

## Resolve the Package Skill

From a Unity project root that imports `com.pie.agent`, locate this skill in the resolved package:

1. `Packages/com.pie.agent/Skills/pie-unity-rpc/SKILL.md`
2. `Library/PackageCache/com.pie.agent@*/Skills/pie-unity-rpc/SKILL.md`
3. `Library/PackageCache/com.pie.agent*/Skills/pie-unity-rpc/SKILL.md`

If no package copy exists, ask the user to open the Unity project once so Unity resolves packages. Run the adjacent `pie-unity-rpc.js`; do not add a project-local launcher or install a user-skill copy.

## Discover

```bash
node <script> instances
node <script> instances --project "/abs/path/to/project"
```

Selection behavior:

- if there is only one active instance, the helper uses it automatically
- `--project` is treated as a matching hint, not only as an exact path
- for `tool` and `rpc` commands without `--project`, `--instance`, or `--port`, the helper inspects candidate manifests and selects the only host that exposes the requested capability
- if multiple candidates remain, the helper returns a structured candidate list; show it and ask the user which instance to use
- prefer `--instance` when the user already knows the target instance ID
- treat every pie-unity host as a tool host; do not infer intended use from whether the host reports editor or runtime mode

## Health

```bash
node <script> health --project "/abs/path/to/project"
node <script> health --instance "<instance-id>"
```

## Manifest

Read only what you need:

```bash
node <script> manifest --project "/abs/path/to/project"
node <script> manifest --project "/abs/path/to/project" --namespace chat
node <script> manifest --project "/abs/path/to/project" --name unity_project_inspect
```

The host manifest reports `manifestSchemaVersion` and `skillProtocolVersion`. For unfamiliar work, inspect the relevant namespace before calling mutating tools. Prefer project-specific namespaces, such as `voxmod`, over generic Unity editing tools when they are available.

## Host Workflow

Prefer the host commands over raw `tool` names:

```bash
node <script> query --project "/abs/path/to/project" --data '{"scope":"scene_object","name":"Main Camera","limit":1}'
node <script> inspect --project "/abs/path/to/project" --data '{"target":{"path":"Main Camera"}}'
node <script> edit --project "/abs/path/to/project" --data '{"action":"create_scene_object","name":"CubeA","primitiveType":"Cube","x":0,"y":1,"z":0}'
node <script> log-read --project "/abs/path/to/project" --data '{"source":"active","tailLines":120,"contains":"error"}'
```

For Unity project files, use normal Pie file tools rooted at the Unity project. The RPC helper deliberately does not provide separate project file read/write commands.

## Script Run

Use `script-run` only for JavaScript generator tasks that should execute inside Unity. The script must define a generator entrypoint. For multi-frame work, yield `ctx.nextFrame()`, `ctx.waitFrames(n)`, or `ctx.waitSeconds(s)`. The command returns after the task completes, fails, or times out.

```bash
node <script> script-run --project "/abs/path/to/project" --data '{"script":"export function* run(ctx, args) { return ctx.query({ scope: \"scene_object\", limit: 3 }); }"}'
node <script> script-run --project "/abs/path/to/project" --data '{"script":"export function* run(ctx, args) { for (let i = 0; i < 60; i++) { ctx.log(\"frame\", i); yield ctx.nextFrame(); } return { ok: true }; }","totalTimeoutMs":10000,"maxFrames":120}'
```

Do not pass C#, shader source, or raw file contents to `script-run`. Use normal Pie file tools for Unity project files, then call `unity_refresh` through `tool` if Unity must import changed assets. Do not write long synchronous loops. Express long goals as short generator steps that yield between frames.

## Raw Tool / RPC

Use raw `tool` or `rpc` when the shell-style commands do not cover what you need. Prefer manifest-discovered project namespaces over generic Unity editing tools when they are available. Do not hand-edit Unity prefab YAML or serialized asset references when an RPC tool exists.

## Retry Behavior

The helper automatically retries when `pie-unity` reports temporary unavailability during compilation or domain reload. Use `--wait-ms` to override the default polling delay.

When using `rpc --method pie_chat.set_config`, the helper waits for the Unity-side `configAppliedVersion` ack when available, then adds a short settle window before returning. Use `--config-settle-ms` to override that delay if needed.

## Guidance

- Start with `instances` if the target Unity project is not yet confirmed.
- Start with `health` if the project is known.
- Read this skill contract first. Do not start by scanning the local repo or helper source code unless the task is explicitly about the repo/tooling itself.
- If a command returns an ambiguous instance error, present the candidate list and ask the user to choose one instance.
- Read `manifest --namespace unity` or `manifest --namespace unity.script` before using unfamiliar host commands.
- Read capability descriptions instead of assuming behavior from host mode. Editor and runtime hosts are both just live tool hosts.
- Once the target `projectPath` is known, keep filesystem exploration scoped to that Unity project. Do not search unrelated repo directories to rediscover the project.
- For scene setup with named objects such as `Player`, `Goal`, or `Main Camera`, start with `query` for exact names before creating anything.
- If `query` finds exactly one match, reuse and update it instead of creating a duplicate.
- If `query` finds multiple matches, do not create another duplicate; inspect/select one explicit result or ask the user.
- Prefer `query -> inspect -> edit -> inspect` over ad-hoc menu execution. Use `edit` actions `add_component`, `remove_component`, `set_component_enabled`, and `set_component_property` for common scene object component changes instead of writing one-off script-run code.
- Use normal file tools with the Unity project root for project text files; do not route file access through Unity RPC.
- Read helper source code only when command behavior is unclear after reading this skill contract or manifest output.
- After editing C# scripts under `Assets`, call `tool --tool unity_refresh --data {}` if needed, then expect a brief compile/domain-reload window. Re-check `health` or retry the next host command instead of assuming Unity is stuck. If compile/runtime errors appear, use `log-read` instead of guessing.
- Prefer `script-run` when the task needs filtering, derived values, or multi-frame composition inside Unity.

## Smoke Test

Recommended minimum verification sequence:

```bash
node <script> health --project "/abs/path/to/project"
node <script> manifest --project "/abs/path/to/project" --namespace unity
node <script> tool --project "/abs/path/to/project" --tool chat_send --data '{"text":"respond with exactly pong"}'
node <script> tool --project "/abs/path/to/project" --tool chat_get_state
node <script> tool --project "/abs/path/to/project" --tool chat_get_messages
```

Expected result:

- `chat_get_state` eventually returns `isBusy: false`
- the final assistant message contains the expected response
