# SSMS AI Assistant — proposed design

Status: approved by the user's instruction to implement the plan; implementation added without installation. See `docs/ssms-ai-assistant.md` for actual verification results and remaining live/SSMS validation limits.

Latest user revision: remove Gemini entirely from the client, provider chooser and settings. Only OpenRouter and Ollama remain; their persisted numeric IDs stay 1 and 2. Legacy provider 0/Gemini falls back to OpenRouter without overwriting the remaining provider settings. The original Gemini-specific design below is superseded by this revision. The header now follows the supplied reference with New Thread, a new-chat action and a plain ellipsis menu; mouse clicks do not leave a blue focus outline, while keyboard navigation retains a focus visual.

## Purpose and scope

SqlAssist is a C# extension for SSMS 22 providing SQL IntelliSense, snippets, formatting, risk checks and object search. Add an interactive assistant for writing, explaining and reviewing SQL using Gemini API, OpenRouter and Ollama. The attached user specification is authoritative.

Do not implement SQL execution, agent tools, automatic database/schema inspection, RAG, file attachments, durable chat history or other providers. Do not publish, install, commit or push without a separate user instruction.

## Existing implementation and worktree

- VSIX targets net48 with WPF and Microsoft.VisualStudio.SDK 17.14; Core targets netstandard2.0; xUnit tests target net8.0.
- SqlAssistPackage registers commands and a dockable SearchToolWindow. Add a distinct assistant command/window using this pattern and a new GUID/unused command ID; increment the menu resource revision.
- SearchControl uses dynamic EnvironmentColors resources for host theme integration.
- SettingsProvider persists non-secret settings as XML under AppData and returns copies through XML round-tripping.
- ActiveSqlEditor.Find resolves a T-SQL view. Its RunScope falls back to the whole document and MUST NOT be used to attach SQL.
- Existing staged and unstaged changes include deletion of the earlier AI feature, local-table completion changes and documentation edits. Preserve these changes; introduce the new assistant without restoring deleted AI files wholesale or changing existing local-table behavior. Inspect each shared-file diff before editing.
- Graph metadata has changed since indexing; current source reads, rather than graph absence claims, ground this design.

## Approaches

Proposed choice: native WPF ToolWindowPane, using the extension's existing SDK and theme resources. Use HttpClient REST adapters and a constrained native Markdown renderer. This fits the existing architecture, avoids a browser runtime and prevents remote resources or scripts by construction.

A browser/WebView UI could offer broader Markdown rendering but adds a runtime, packaging and navigation/security surface. A standalone modal chat dialog loses the requested dockable workflow. Neither is proposed for this first version. The native WPF choice is the assistant's recommendation, not a user-approved decision yet.

## Components and intended files

- `src/SqlAssist.Core/Assistant/`: common messages, provider settings, context selection, conversation lifecycle, transport mapping and stream parsing; isolate network transport behind a testable interface where needed.
- `src/SqlAssist.Vsix/Assistant/`: AssistantToolWindow, AssistantControl, AssistantSettingsWindow, safe Markdown view, editor bridge, provider factory and encrypted secret store.
- `src/SqlAssist.Core/Settings/SqlAssistSettings.cs`: add non-secret assistant options and XML round-trip support.
- `src/SqlAssist.Vsix/SqlAssistPackage.cs` and `.vsct`: assistant registration/menu entry; retain unrelated pending edits.
- `src/SqlAssist.Vsix/Settings/OptionsWindow.cs`: add an entry to assistant settings using the shared dialog-host pattern.
- Project references: add only framework references or a small JSON dependency if existing assemblies do not supply a supported JSON implementation. Verify deployment in the VSIX rather than assuming host assembly availability.
- `tests/SqlAssist.Core.Tests/Assistant*Tests.cs`: mapping, parsing, lifecycle, limits, settings and error-redaction tests. Add transport test coverage in a suitable test project if net48-only boundaries require it.
- README: configuration, privacy boundary and limitations, merged around existing documentation edits.

Exact class/file splits can be adjusted during implementation without changing the approved behavior.

## Chat UI and lifecycle

Header: Provider, editable Model selector, Settings and New Chat. Body: distinct User/Assistant entries, native Markdown headings/lists/emphasis and fenced code. SQL fences expose Copy and Insert into Editor. Unsupported Markdown remains readable text; images and HTML are never loaded or executed, and links do not navigate automatically.

Composer: multiline text, removable SQL attachment preview, Attach Selected SQL, Explain SQL, Review SQL, Suggest Optimization, Send and Stop. Enter sends; Shift+Enter inserts a newline. Shortcuts prepare editable drafts only. Reject empty sends; allow only one active request.

State: idle, connecting, responding, stopped or error. Keep chat in memory. Each request captures immutable provider, model, endpoint and context plus a monotonically increasing conversation/request identity. Every progress/completion callback checks identity and cancellation before modifying state. Cancellation invalidates callbacks immediately and cancels/disposes the underlying HTTP operation/stream. Preserve received text and mark it incomplete. New Chat and window disposal cancel outstanding work. Event handlers and network resources are detached/disposed with their owners.

Changing provider/model retains the visible conversation and displays a notice that previous messages and explicitly attached SQL in this conversation will be sent to the newly selected destination on the next Send. New Chat is the way to start without prior context. Disable destination changes while a generation is running, or require Stop first.

## Context limits

Use a conservative bounded character budget, explicitly described as an approximation rather than an exact token count. Retain newest complete user/assistant turns and the latest user message; trim oldest complete turns. Show a persistent notice of omitted turns before generation. Reject an oversized newest message/attachment with an actionable message rather than silently truncating SQL. Keep visible chat intact when selecting a smaller outbound context. Do not treat failed/empty assistant placeholders as valid history; clearly mark any included partial assistant response.

The exact conservative limit is an implementation choice and must be documented and tested. Model discovery limits may refine it, but do not hardcode changing model catalogs or prices.

## Provider boundaries

`IAiChatProvider` exposes model discovery, real connection/model validation and cancellable generation with incremental text callbacks using a common message model. Use a shared long-lived HttpClient per transport configuration, per-request headers and bounded linked cancellation/timeout scopes. Never mutate shared default authorization headers. Disable automatic cross-origin redirects for authenticated requests. No automatic generation retries.

Gemini: REST `v1beta/models/{id}:streamGenerateContent?alt=sse`, key in `x-goog-api-key`; convert assistant role to `model` and text into `parts`. List models through the models endpoint, handle pagination and filter generation-capable models. Support the nonstreaming generateContent form explicitly if needed.

OpenRouter: REST `/api/v1/chat/completions`, Bearer authentication, common roles, `stream: true`; list model IDs using `/api/v1/models`. Parse SSE deltas, comments, multi-line events, completion marker and in-stream error objects. A successful HTTP status does not establish successful generation.

Ollama: configured base URL with `/api/chat` and `/api/tags`; no key required for normal local service. Parse NDJSON content, `done` and in-stream errors. Validate HTTP/HTTPS URL without embedded credentials/query secrets; remote use is explicitly configured by the user. Select a model returned by that instance and refresh validation before generation if necessary. Preserve optional path prefixes consistently when constructing endpoints.

All adapters handle fragmented UTF-8, bounded event/response sizes, EOF without a completion signal and full JSON responses when an endpoint explicitly uses nonstreaming mode. Do not silently repeat a partially completed generation to switch modes.

Test Connection performs a small generation against the selected model, separately from discovery or saving settings. State clearly that this sends a small test prompt and may consume provider quota. Surface authentication, quota, rate limit, missing model, timeout, network and unavailable-instance failures using safe categories. Do not show raw HTTP bodies, URLs containing secrets or exception details to logs/UI.

## Settings and secrets

### API and key management screen — explicit user requirement

Provide a dedicated, reusable "AI API & Keys" management screen, accessible from both SqlAssist Options and the assistant header's Settings button. Both entry points open the same settings implementation. This screen is part of the first-version deliverable, not just environment-variable setup instructions.

Use separate tabs or provider sections for Gemini API, OpenRouter and Ollama. Each provider retains its own configuration when switching sections:

- Gemini API and OpenRouter: masked API Key field with Show/Hide, Save Key and Delete Saved Key actions; show whether the effective key comes from an environment variable or Windows Credential Manager without exposing its value.
- All providers: editable Model ID, Load Models, Test Connection and Cancel controls, with distinct loading, validation and error states. Save non-secret configuration independently from saving a key or testing a connection. Explain when a required key or model is missing.
- Ollama: editable Base URL for local/remote instances, load the actual installed models from that instance, and select an available model. Normal local use requires no API key. In this first version, do not imply support for an authenticated remote gateway unless that capability is implemented and tested explicitly.
- Saving a key must confirm only that the credential was stored; Test Connection reports success only after a real selected-model request. Deletion clears the saved credential and its field, while clearly showing if an environment key remains effective.
- Show unsaved changes and provide explicit Save/Cancel behavior. Dismissing without Save discards uncommitted non-secret edits. Save Key/Delete Saved Key are immediate explicit credential actions, labeled as such, and are not reversed by canceling the dialog. Clear secret fields on close; never prepopulate the UI with an environment secret or log secret values.
- Explain the outbound data boundary and that Test Connection sends a small test prompt which may consume quota. Discovery and connection testing remain cancellable, and closing the screen invalidates pending callbacks.

Independent per-provider model ID, Ollama base URL, active provider and non-secret preferences are persisted. Model catalogs are loaded, not hardcoded. Empty configuration prompts for setup instead of choosing a changing cloud model automatically.

Gemini reads `GEMINI_API_KEY`; OpenRouter reads `OPENROUTER_API_KEY`. UI explains environment precedence over a saved key. Show/Hide is explicit. Save secrets only through Windows Credential Manager, with separate application/provider targets, and provide Delete Saved Key. Deleting a saved key must explain when an environment key remains effective. No secrets in XML, source, telemetry or exceptions. Use Windows DPAPI only if Credential Manager integration proves unsuitable and document that choice before implementation.

Settings discovery/test operations have cancellation, disabled duplicate buttons and timeouts; closing the dialog cancels work and rejects stale UI callbacks. Report successful save and successful live connection as separate outcomes.

## Editor integration and data boundary

Attach reads only actual selected spans from the current T-SQL editor. Empty selection reports that SQL must be selected. For box selections join only selected fragments, not intervening unselected text. Preview attachment before Send and allow removal. Do not inspect connections, schema, query results or the rest of the file.

Insert resolves the current T-SQL view at click time, inserts only after an explicit action and groups the edit into one undo transaction. If a selection exists, ask explicitly before replacing it or refuse with instructions to clear it; default proposal is refusal to avoid accidental replacement. Reject read-only/closed/unavailable views and show a safe message. No Execute commands are called.

Only user messages, explicitly attached SQL and conversation replies cross the provider boundary. There is no automatic database context or disk chat history. Transport logs include safe categories only.

## Verification and delivery

1. Capture baseline test/build results without resetting pending work.
2. Test each request mapping and discovery parsing using clearly identified test fixtures/fake transports; exercise fragmented SSE/NDJSON, terminal errors, cancellation before headers/during read, timeout, no retries, secret redaction, bounded context and stale progress after New Chat.
3. Test settings round-trips do not persist keys and preserve other options. Verify secret-store failures and deletion semantics at the Windows boundary when available.
4. Build the actual Release VSIX configuration with the project's working build tools and inspect dependency packaging. Run relevant tests and then the core suite once changes stabilize.
5. Check whether a compatible already-installed extension can exercise this implementation. Since installation is forbidden in this request, do not install or replace it; if updated code cannot be loaded without installation, explicitly report in-SSMS UI validation as unavailable. A standalone WPF preview is not proof of SSMS integration.
6. When credentials/instance are available, test live model discovery, selected-model generation and Stop without printing secrets. Otherwise record each untested live provider explicitly. Never infer live connectivity from build or fixtures.
7. Final Thai report: behavior added, key files, setup per provider, actual verification outcomes and remaining limitations. Leave work uncommitted/unpublished/uninstalled.

## Official references checked through Context7

- https://ai.google.dev/api/generate-content
- https://ai.google.dev/api/models
- https://ai.google.dev/api
- https://github.com/openrouterteam/docs/blob/main/api_reference/errors-and-debugging.mdx
- https://github.com/openrouterteam/docs/blob/main/api_reference/authentication.mdx
- https://github.com/openrouterteam/docs/blob/main/api_reference/overview.mdx
- https://docs.ollama.com/api/chat
- https://docs.ollama.com/api/streaming
- https://docs.ollama.com/api/tags
- https://docs.ollama.com/api/errors

OpenRouter Bearer authentication and chat endpoint, and Ollama chat schema, were checked in a second focused documentation lookup. Recheck the exact OpenRouter model-discovery schema and Ollama authentication documentation during implementation planning. Never substitute remembered syntax for missing current documentation.

## Design check

A fresh read-only builder review reported no material unanswered questions before implementation planning. User approval is still pending.

The user subsequently explicitly required an API/key management screen. The Settings section above now specifies its entry points, per-provider controls, key lifecycle and save/cancel semantics. This addition has not yet undergone another builder review.
