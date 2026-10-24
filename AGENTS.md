# AGENTS.md

Guidance for coding agents working in this repository. See [README.md](README.md) for what the app does and how to
configure it, and [docs/ubiquitous-language.md](docs/ubiquitous-language.md) for the domain terms to use in code,
prompts and the UI.

## Layout

- `src/KnowledgeBase.AppHost`: Aspire AppHost (`AppHost.cs`). All configuration enters here as parameters and is
  passed to the API as environment variables.
- `src/KnowledgeBase.Agent`: the API and the agents, organised by feature folder (`Notes`, `Facts`, `Review`, `Wiki`,
  `Conversations`, `Runs`, `Files`, `Auth`, ...). `Program.cs` registers the five agents and their tools.
- `src/KnowledgeBase.Agent.Tests`: unit tests for the API, mirroring its feature folders.
- `src/KnowledgeBase.Agent.IntegrationTests`: the API's services and pipeline against a temporary wiki root, with
  scripted agents.
- `src/KnowledgeBase.Web`: React 19 + TypeScript + Vite UI. The API client is in `src/api.ts`, pages in `src/pages`.
- `.agents/skills`: Aspire skills for agents. `.mcp.json` registers the Aspire MCP server (`aspire agent mcp`).

## Commands

Run these before declaring work done; CI runs exactly these and fails on any warning.

```shell
dotnet restore KnowledgeBase.slnx --locked-mode
dotnet format KnowledgeBase.slnx --verify-no-changes --no-restore
dotnet build KnowledgeBase.slnx --no-restore --configuration Release
dotnet test --solution KnowledgeBase.slnx --no-build --configuration Release
```

```shell
cd src/KnowledgeBase.Web
pnpm install --frozen-lockfile
pnpm run lint    # oxlint --deny-warnings
pnpm run build   # tsc -b && vite build
```

- If `dotnet format --verify-no-changes` fails, run `dotnet format KnowledgeBase.slnx` and review the diff.
- Run the app with `aspire run` (prefer the Aspire skills / MCP tools for starting, stopping and reading logs). It needs
  real secrets and Entra sign-in, so it may not start in an agent environment; say so rather than guessing.

## Dependencies

- NuGet versions live only in `Directory.Packages.props` (central package management). `PackageReference` items carry
  no `Version`.
- Every project has a `packages.lock.json`. After adding or changing a package, run `dotnet restore` (without
  `--locked-mode`) and commit the updated lock files, or CI fails.
- The UI uses pnpm with a committed `pnpm-lock.yaml`; never add a `package-lock.json` or use npm to install. The pnpm
  version is pinned (with its hash) in `package.json`'s `packageManager` field, which CI and the deployed image
  both follow. `pnpm audit --audit-level high` must pass.
- `TreatWarningsAsErrors` is on for every .NET project.

## C# conventions

`.editorconfig` is the source of truth; these are the patterns it does not capture.

- Explicit types everywhere; `var` is an error. File-scoped namespaces, primary constructors, collection expressions.
- LF line endings; `System` usings first, with using groups separated by a blank line.
- Registration and endpoint mapping use C# 14 `extension(...)` blocks in `*DependencyInjection.cs` and
  `*Endpoints.cs` per feature, chained fluently (`builder.AddWiki()`, `api.MapNotesEndpoints()`).
- Use cases are `XxxCommand`/`XxxQuery` records with an `XxxCommandHandler`/`XxxQueryHandler` registered as a
  singleton. Handlers return `Result<T, Error>` from CSharpFunctionalExtensions and compose with `Bind`, `Map`,
  `Ensure`, `Tap` and `TapError`. Do not throw for expected failures.
- Errors derive from `Error` (`src/KnowledgeBase.Agent/Error.cs`), one sealed class per error, in an `Errors` folder
  or next to the handler. Endpoints map error types to status codes with a `switch` and return `Results.Problem`.
- Minimal API endpoints declare `WithName`, `WithSummary` and `Produces` for OpenAPI. Everything under `/api` requires
  sign-in and a `X-Requested-With` header on state-changing calls (see `Auth/AuthDependencyInjection.cs`).
- Logging uses source-generated `[LoggerMessage]` extension methods in a `LoggerMessages.cs` per feature, never
  `logger.LogXxx(...)` with string templates.
- Inject `TimeProvider` for the current time; do not call `DateTime.Now`/`UtcNow`.
- Comments explain why, not what; XML doc comments on non-obvious types and members. Match the existing density.

## Tests

- TUnit runs the tests (on Microsoft.Testing.Platform, selected in `global.json`), Shouldly asserts and NSubstitute
  fakes dependencies. Do not add TUnit's own assertions, xUnit or Moq.
- Unit tests stay pure: no LLM calls, network or real wiki directory. Logic that needs the disk or an agent belongs
  behind something a test can substitute, or in the integration tests.
- Integration tests never call a model either. `TestWiki` builds the API's real services (`AddWiki`) over a temporary
  root and replaces every agent with a `FakeAgent` whose `Behaviour` a test scripts, usually calling the agent's real
  tools through `TestWiki.CallToolAsync` so runs record tool calls as in production. `Start()` runs `UseWiki`, so
  laying out files first and then starting simulates a restart. Assert on where files end up, and wait with
  `Eventually.Until` rather than fixed delays.
- HTTP tests (`Api/`) start the real `Program` through `TestApi` (`WebApplicationFactory`). Only the outside world is
  replaced: Entra sign-in by a test scheme (`TestApi.Client(objectId)` signs in, `null` signs out), Redis by an
  in-memory output cache and agents by `FakeAgent`. Settings `Program` reads while building its services must be
  passed with `UseSetting`, and the environment is `Testing` so the author's user secrets are never loaded.
- One test class per class under test (`WikiPathsTests`), its subject built in the constructor. A test method names
  the scenario, never the expectation (`APathLeavesTheWiki`, not `ResolveThrowsWhenOutsideTheWiki`). It arranges and
  acts, then calls local functions named for each expectation (`ItShouldBeRefused()`), declared below the calls,
  which only assert. Capture an expected exception with `Catch.Exception` in the act step rather than throwing
  inside an `ItShould...` function.
- The API grants `InternalsVisibleTo` to the test project and to NSubstitute's proxy assembly, so internal types can
  be tested and substituted. Sealed classes cannot be substituted; depend on an interface where a test needs to.

## Agent pipeline rules

- The pipeline is file-system driven: watchers in `*/Watchers` react to files in folders defined by
  `WikiConfigurationSettings`. Keep state transitions as file moves so a restart can recover (see
  `NoteWatcher.RequeueInterruptedNotes` and `AgentRunStore.FailInterruptedRunsAsync`).
- Agents only reach the disk through their tools. Every path an agent or the UI supplies must go through `WikiPaths`,
  which refuses anything outside the wiki directory. Keep that guarantee when adding tools.
- Respect each agent's boundaries from `pipeline.md`: FactFinder never touches the wiki, Reviewer and Librarian never
  edit it, Editor does not add knowledge. Give an agent only the tools its role needs.
- Run agents through `AgentRunTracker.RunAsync` so runs and tool calls are recorded.
- `pipeline.md` is embedded and public. Anything personal (about the author, wiki rules, agent role prompts) belongs
  in configuration, never in the repository.
- Agents are created in `Agents/AgentConfiguration.cs` for the provider each one is configured with (`Anthropic` or
  `OpenAICompatible`). Callers depend only on `AIAgent`; keep provider-specific code in that file. Local models are
  weaker at tool calling, so tool descriptions and errors must be explicit enough to correct a bad call.
- Adding an agent means updating `KnownAgents`, `Program.cs`, the `agents` array in `AppHost.cs` and the
  `{Agent}Provider` default in the AppHost's `appsettings.json`.
- `WikiSections` must stay in sync with the folders described in the wiki rules.

## UI conventions

- Data fetching through TanStack Query; live updates come from the `/api/events` server-sent event stream
  (`useLiveUpdates.ts`), which invalidates queries by area.
- All API calls go through `src/api.ts`, which sends the `X-Requested-With` header the API requires.
- The Vite dev server proxies `/api`, `/auth` and the OIDC callbacks to the API so the browser sees one origin.
  Keep new server routes under those prefixes.

## Deployment

- `.github/workflows/cd.yml` deploys after CI succeeds on `main`, on the self-hosted runner labelled
  `knowledge-base`. Keep its `if:` guard: `workflow_run` also fires for pull request CI runs, including forks, and
  those must never reach the self-hosted runner. CI itself stays on GitHub-hosted runners.
- The Compose shape lives in `AppHost.cs` (`PublishAsDockerComposeService`); never hand-edit generated
  `aspire-output/`. The API uses host networking and is configured through loopback ports defined at the bottom of
  `AppHost.cs`. Preview changes with `aspire publish -o <scratch dir>`.
- A new AppHost parameter must also be added to the CD workflow's `env:` (as `Parameters__<Name>`) and to
  `scripts/setup-github.ps1`. Anything secret is a GitHub secret, not a variable: the logs are public.
- Personal text (instructions, wiki rules) never goes in this repository or in GitHub settings. It lives in the private
  `config/` submodule as markdown files loaded by `Common/ConfigFiles.cs`. The submodule may be absent (public CI,
  other people's clones); the build must keep working without it. `config.template/` holds the public, generic
  versions that the AppHost's `scaffold-config` command copies in; a new config file needs a template too.

## Git

- Conventional Commits with a scope where it helps: `feat(api): ...`, `refactor(web): ...`, `ci: ...`, `style: ...`.
- Do not commit secrets, `.env` files or anything from the wiki root directory.
