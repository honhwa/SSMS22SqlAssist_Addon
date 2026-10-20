# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## ภาษา / Language

- ตอบกลับผู้ใช้เป็น **ภาษาไทย** เสมอ (Always respond in Thai).
- คำศัพท์ทางเทคนิค (เช่น completion source, commit manager, MEF, VSIX, reflection, schema cache, tool window)
  ให้คงคำภาษาอังกฤษไว้ได้ ไม่ต้องแปล
- ใน repo นี้คอมเมนต์ในโค้ดเป็น **ภาษาอังกฤษ** — เขียนเพิ่มเป็นภาษาอังกฤษให้กลมกลืนกับของเดิม
  ส่วนข้อความที่ผู้ใช้เห็น (README, ข้อความในตัวติดตั้ง `SqlAssist.Setup`) เป็นภาษาไทย

## What this is

SqlAssist: an SSMS 22 extension (VSIX) that replaces the parts of dbForge SQL Complete / dbForge Search the user
actually uses — `*` expansion on Tab, auto table aliases, FK-based JOIN suggestions, fuzzy completion, snippets, and
a dockable object-search tool window. `README.md` (Thai) is the user manual; `CONTEXT.md` is the domain glossary
(Active Connection, Schema, Search Index, Search Item, Definition, Table Script, Match…) — use those terms.

## Commands

```powershell
dotnet test tests\SqlAssist.Core.Tests                                   # all tests (xUnit, Core only)
dotnet test tests\SqlAssist.Core.Tests --filter "FullyQualifiedName~SearchMatcherTests"   # one class
dotnet test tests\SqlAssist.Core.Tests --filter "FullyQualifiedName~CompletionTests.<Method>"  # one test
dotnet build src\SqlAssist.Vsix -c Release      # -> src\SqlAssist.Vsix\bin\Release\net48\SqlAssist.vsix
.\build-setup.ps1                               # icons -> tests -> Setup exe -> dist\SqlAssistSetup-<version>.exe
.\install.ps1 / .\uninstall.ps1                 # install the built .vsix into SSMS 22 via SSMS's VSIXInstaller
.\clear-ssms-cache.ps1                          # when SqlAssist menus don't appear after install
```

- **Version lives only in `Directory.Build.props`.** It flows to assemblies, the .vsix manifest (the
  `StampVsixVersion` target in `SqlAssist.Vsix.csproj` overwrites the placeholder in the merged manifest and fails
  the build on mismatch) and the setup exe name. Bump it for every release.
- Building needs no SSMS installed. Debugging: SSMS 22 has no experimental instance — install the extension first,
  then F5 (start program is `Ssms.exe /log`) or attach.
- Runtime log: `%LOCALAPPDATA%\SqlAssist\SqlAssist.log` (Tools › SqlAssist › Open Log). User files:
  `%APPDATA%\SqlAssist\snippets.xml`.

## Architecture

Three projects (`SqlAssist.sln`) plus tests:

- **`src/SqlAssist.Core`** (netstandard2.0) — pure logic, no VS/SSMS references: `Sql/` (tokenizer, context
  analyzer that figures out which tables/aliases are in scope and what kind of completion the caret wants),
  `Completion/` (fuzzy matcher, alias generator, JOIN suggester, star expander), `Snippets/`, `Schema/` (schema model,
  SQL type formatting), `Search/` (search matcher with match-case/whole-word/wildcards, `CREATE TABLE` preview
  builder). All unit tests target this layer; put testable logic here, not in the Vsix project.
- **`src/SqlAssist.Vsix`** (net48, WPF) — SSMS glue. It does **not** reference the Core assembly: Core's `.cs`
  files are compiled in via a linked `<Compile Include="..\SqlAssist.Core\**\*.cs">`, so the extension ships as one
  DLL. Pieces:
  - `Editor/` — MEF exports for the async-completion API (`SqlCompletionSource`, `SqlItemManager`,
    `SqlCommitManager`, `SqlCompletionPresenter`) bound to the SQL content type, plus `TabCommandFilter`, which owns
    the Tab key order: snippet → commit open completion → expand `*` → plain Tab.
  - `Connection/ActiveConnection.cs` — reads the active query window's server/database/login from SSMS's
    `SQLEditors` assembly **by reflection** (no public API; no SSMS DLL referenced at build). Every step must
    tolerate members disappearing in later SSMS builds. Falls back to the last known connection. Windows/SQL auth
    only (no Entra ID). DB access uses the in-box `System.Data.SqlClient`.
  - `Schema/` — `SchemaLoader` queries catalog views; `SchemaCache` keys by server+login+database, 10-minute TTL,
    and reloads (≥30s apart) when a query names a table the cache doesn't know.
  - `Search/` — the Search tool window (`SearchToolWindow`/`SearchControl`), `SearchIndexLoader`/`SearchIndexCache`
    (per-database in-memory index, 10-minute stale-while-revalidate, loads databases one by one and streams results),
    `SearchSettings` (object-type/match options, in memory only: every SSMS session starts from the defaults).
  - `Update/` — online update: `UpdateService` reads GitHub's `releases/latest` for `kriangdet/SSMS22SqlAssist_Addon`
    (daily on startup via background `ProvideAutoLoad`, or Tools › SqlAssist › Check for Updates), shows
    `UpdateInfoBar`, and on request downloads the release's `SqlAssistSetup-*.exe` (SHA-256 checked against the
    asset's GitHub digest) and starts it — the setup window then waits for SSMS to close. Version/asset parsing and
    the check/skip policy live in Core `Update/`. State: `%LOCALAPPDATA%\SqlAssist\update.txt`.
    Releases are published with `.\publish-release.ps1` (needs `$env:GITHUB_TOKEN`).
  - `SqlAssistPackage.cs` + `SqlAssistPackage.vsct` — the AsyncPackage, Tools-menu commands, Ctrl+Shift+R refresh.
- **`src/SqlAssist.Setup`** (net48 WinForms) — `SqlAssistSetup.exe`. Its build depends on the Vsix project and
  embeds `SqlAssist.vsix` as a resource (`EmbedVsix` target); `SetupEngine` detects SSMS 22, the installed version
  (per-user under `%LOCALAPPDATA%\Microsoft\SSMS\22.0_*\Extensions`), whether SSMS is running, and drives SSMS's own
  `VSIXInstaller.exe` (uninstalls first so same-version reinstall works). CLI: `/install`, `/uninstall`
  (exit codes 0 ok, 1 fail, 2 SSMS running, 3 no SSMS 22). After installing it copies itself to
  `%LOCALAPPDATA%\SqlAssist\SqlAssistSetup.exe` and writes an HKCU `...\Uninstall\SqlAssist` key so it shows in
  Programs and Features; that entry runs the copy with `/remove` (the window, "uninstall" preselected).

Conventions that matter inside SSMS: exceptions must never escape into the editor — catch and write to `Log`
instead. Connection/reflection code runs on the UI thread (`ThreadHelper.ThrowIfNotOnUIThread`).

Statement boundaries for completion are `;`, `GO` or a blank line — except a blank line inside parentheses or right
after a CTE (`WITH x AS (...)` ⏎⏎ `SELECT`). CTE and derived-table columns come from the query text
(`DerivedTable`, linked to `TableRef.Derived`; `SchemaModel.Find(TableRef)` resolves them, borrowing types and
MS_Description from the columns they pass through). Known v1 gaps (don't assume they work): TVF columns, `USE`
switching the database, data search, index/constraint/UDT search.
