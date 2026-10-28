# Ubiquitous language

The words this project uses for its domain, and what each one means. Use them as written here in code, prompts, the
UI, commits and conversation. When a term changes meaning, change it here first.

## People and places

| Term                  | Meaning                                                                                                                                                                                                                         | In code                                              |
|-----------------------|---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|------------------------------------------------------|
| **Author**            | The one person the knowledge base belongs to. Every piece of knowledge is recorded in relation to the author. Not "user" when talking about the domain; "user" is only the role in a conversation.                              | `ConversationRole.User`                              |
| **Wiki root**         | The directory that holds the wiki and all pipeline state (notes, facts, review, runs, conversations, keys). Every path an agent or the UI supplies is relative to it and may not leave it.                                    | `WikiConfigurationSettings.RootDirectory`, `WikiPaths` |
| **Wiki**              | The interlinked markdown pages under `wiki/` in the wiki root: the product the pipeline maintains.                                                                                                                              | `WikiDirectory`                                      |
| **Page**              | One markdown file in the wiki. Findings name the page they concern by its **page path**, relative to the wiki root.                                                                                                             | `ReviewFinding.PagePath`                             |
| **Section**           | A top-level folder of the wiki (`me`, `people`, `projects`, `learning`, `plans`, `decisions`, `reflections`), each with an `index.md` seeded on startup. Must match the folders described in the wiki rules.                     | `WikiSections`                                       |
| **Index page**        | The `index.md` of the wiki root or of a section, listing what it contains.                                                                                                                                                       | `WikiSections.RootIndex`, `SectionIndex`             |

## The pipeline

| Term                     | Meaning                                                                                                                                                                                                  | In code                                    |
|--------------------------|----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|--------------------------------------------|
| **Pipeline**             | The four agents (FactFinder, ThreadWeaver, Reviewer, Editor) that turn notes into a maintained wiki. File-system driven: each stage watches a folder and moves files along when it finishes.            | `pipeline.md`, `*/Watchers`                |
| **Stage**                | One agent's step in the pipeline, with a contract for what it consumes and what it produces.                                                                                                              |                                            |
| **Agent**                | A model-backed worker with a role, instructions and only the tools its role needs. There are seven: the four pipeline agents, the Librarian, the Pleb and the Scout.                                                | `KnownAgents`, `AIAgent`                   |
| **Provider**             | Where an agent's model runs: `Anthropic` or `OpenAICompatible` (for example a local llama.cpp server). Chosen per agent.                                                                                  | `AgentConfiguration`                       |
| **Tool**                 | A function an agent may call. Agents reach the disk only through their tools.                                                                                                                            | `*/Tools`                                  |
| **Watcher**              | Reacts to files appearing in a pipeline folder and starts the next stage.                                                                                                                               | `NoteWatcher`, `FactWatcher`, ...          |
| **Wiki lock**            | Lets only one wiki-touching run (ThreadWeaver, Reviewer, Editor) work at a time, so no agent sees pages half-changed by another.                                                                        | `WikiLock`                                 |

### Agents

| Agent            | Consumes            | Produces              | Boundary                                                        |
|------------------|---------------------|-----------------------|-----------------------------------------------------------------|
| **FactFinder**   | one note            | a fact document       | Never touches the wiki.                                         |
| **ThreadWeaver** | a fact document     | changed wiki pages    | The only stage that adds new knowledge to the wiki.             |
| **Reviewer**     | the whole wiki      | a report and findings | Never edits the wiki.                                           |
| **Editor**       | open findings       | changed wiki pages    | Restructures and corrects; never adds new knowledge.            |
| **Librarian**    | the wiki            | answers               | Outside the pipeline. Read-only.                                |
| **Pleb**         | a curation request  | a topic's brief and queries | Research. No read tools: code gives it the topic's page. Never names people or organisations. |
| **Scout**        | one fetched result  | a verdict             | Research. No read tools: code gives it the topic's brief and the page. Keeps or discards; never sees the wiki. |

"Weaving" is what the ThreadWeaver does: filing facts into the pages they belong to, creating pages where needed and
linking them.

## Notes

| Term              | Meaning                                                                                                                                                                       | In code                                |
|-------------------|-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|----------------------------------------|
| **Note**          | A free-form markdown file the author captures. The raw input of the pipeline.                                                                                                 | `CaptureWikiNoteCommand`               |
| **Capture**       | Writing a new note into `notes/new`.                                                                                                                                          | `CaptureNote`                          |
| **Processing slot** | Only one note is processed at a time. A note claims the slot by moving to `notes/processing` and holds it until its facts have been woven.                                  | `StartNoteProcessingCommandHandler`    |
| **Retry**         | Moving a failed note back to `notes/new` so it is processed again.                                                                                                            | `RetryNote`                            |
| **Requeue**       | On startup, moving a note left in processing with no facts waiting back to `notes/new`, because it was interrupted before its facts were written.                            | `NoteWatcher.RequeueInterruptedNotes`  |

A note moves `new` → `processing` → `processed`, or into `failed` when the FactFinder extracts no facts or the
ThreadWeaver fails on its facts.

## Facts

| Term                | Meaning                                                                                                                                                                                         | In code             |
|---------------------|-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|---------------------|
| **Fact document**   | The JSON the FactFinder writes to `facts/`: the facts extracted from one note, together with the **source note**. Moves to `facts/processed` once woven, or `facts/failed`.                    | `FactDocument`      |
| **Fact**            | One subject and the observations about it.                                                                                                                                                      | `Fact`              |
| **Subject**         | Who or what a fact is about, named as fully as the note allows (full names, organisations spelled out), because the ThreadWeaver matches subjects to existing pages by name.                    | `FactSubject`       |
| **Observation**     | One thing the note says about a subject: a summary, the supporting detail, a priority and the **related subjects**.                                                                             | `Observation`       |

"Fact" is the unit of knowledge between FactFinder and ThreadWeaver. Don't call it a "note" or an "entry".

## Research

| Term          | Meaning                                                                                                                                                                                                     | In code                         |
|---------------|-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|---------------------------------|
| **Deny-list** | The names of people and organisations that never go into a search query, stored in `research/denylist.json`. Filled automatically from the author's own notes and, once, from the wiki's `people/` folders. | `DenyList`, `DenyListMatcher`   |
| **Deny-list entry** | One name on the deny-list with its kind, its **source** (`Auto` from classification, `Manual` from the author in the UI) and its state (`Denied` or `Allowed`). Classification never changes a `Manual` entry. | `DenyListEntry`                 |
| **Topic** | A subject the wiki is kept informed about: every top-level folder or page under `projects/`, `learning/` and `plans/` (`projects/foo`, `learning/bar.md`). Discovered as agent runs change the wiki, pruned when its page is deleted or moved. Its state is in `research/topics/<slug>.json`, where the slug is the path with `/` replaced by `--`. | `ResearchTopic`, `TopicPaths` |
| **Muted** | A topic the author has stopped researching from the UI, without moving it in the wiki. Only the author changes it. | `ResearchTopic.Muted` |
| **Curation request** | A file in `research/curation/requests` asking the Pleb to revisit a topic's brief and queries. One per topic: a new request merges into a waiting one. | `CurationRequest` |
| **Brief** | A topic's short, de-identified description of what is worth finding, written by the Pleb for the web-facing agents. | `ResearchTopic.Brief` |
| **Curation** | One Pleb run over a curation request: it either sets the topic's brief and up to three queries, or keeps them. Requests are drained a few per **research cycle**, oldest first, and skipped while the model server is unhealthy. A curation without a verdict moves to `research/curation/failed`. | `CurateTopicsCommandHandler`, `CurationJob` |
| **Model gate** | Limits how many research agent runs use the model at once (one by default). A waiting Scout run goes before a waiting Pleb run. | `ModelGate` |
| **Verdict** | The Scout's judgement of one result, recorded on the result in the pending batch: kept (with a summary and short quotes) or discarded (with a reason). A judged result is skipped after a restart. A run that ends without one counts as a discard and is recorded as failed. | `ScoutVerdict` |
| **Triage** | The Scout judging a topic's fetched results, one run each. When all are judged, the kept ones are combined into one **research note** in `notes/new` and the batch moves to `research/batches/awaiting`; a batch with nothing kept moves to `batches/completed`. | `TriageResultsCommandHandler` |
| **Research note** | The note triage writes for a topic: a research header (origin, topic, source URLs), then the Scout's summaries and length-capped quotes, never raw page text. | `ResearchNote` |

## Review

| Term               | Meaning                                                                                                                                                                                                                          | In code                              |
|--------------------|----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|--------------------------------------|
| **Wiki rules**     | The author's rules for how the wiki is organised and written. The Reviewer checks against them. Personal, so they live in the private `config/` submodule.                                                                       | `WikiRules.md`                       |
| **Review**         | One Reviewer run over the whole wiki.                                                                                                                                                                                            | `ReviewWikiCommand`                  |
| **Review request** | A file in `review/requests` asking for a review, written on a schedule or from the UI. Moves to `requests/completed` when the review is done.                                                                                    | `RequestReviewWikiCommand`, `ScheduledReviewJob` |
| **Report**         | The outcome of a review: its date, a summary of the state of the wiki and the ids of the findings it opened. Stored in `review/reports`.                                                                                          | `ReviewWikiResult`                   |
| **Finding**        | One problem the Reviewer found on a page: the page path, a description, a concrete recommendation the Editor can carry out without re-doing the review, and a priority (`Low`, `Medium`, `High`).                               | `ReviewFinding`                      |
| **Open / closed**  | A finding's state. Open findings live in `review/findings/open` and start the Editor; the Editor closes them, moving them to `review/findings/closed`.                                                                          | `FindingState`                       |
| **Resolution**     | What the Editor did to address a finding, recorded on it when it is closed.                                                                                                                                                    | `FindingResolution`                  |

## Conversations

| Term              | Meaning                                                                                                                                                       | In code                    |
|-------------------|---------------------------------------------------------------------------------------------------------------------------------------------------------------|----------------------------|
| **Conversation**  | A chat between the author and the Librarian: a title and its messages, stored in `conversations/`.                                                            | `Conversation`             |
| **Message**       | One turn in a conversation, from the `User` or the `Assistant`.                                                                                               | `ConversationMessage`      |
| **Session**       | The agent's own state that carries a conversation's context into the next question. Stored with the conversation; never sent to the UI.                     | `StoredConversation.Session` |

## Runs

| Term          | Meaning                                                                                                                                                       | In code                    |
|---------------|---------------------------------------------------------------------------------------------------------------------------------------------------------------|----------------------------|
| **Run**       | One invocation of an agent, recorded in `runs/` with its status (`Running`, `Succeeded`, `Failed`), summary or error, and tool calls.                         | `AgentRun`, `AgentRunTracker` |
| **Trigger**   | What started a run and the file it was started for: a `Note`, `Facts`, a `ReviewRequest` or `Findings`.                                                       | `AgentRunTrigger`          |
| **Tool call** | One tool an agent called during a run, the path it acted on and whether it succeeded.                                                                         | `AgentToolCall`            |
| **Interrupted run** | A run still `Running` when the API starts, which can only mean the API stopped mid-run. It is marked failed.                                            | `AgentRunStore.FailInterruptedRunsAsync` |

## Instructions and configuration

| Term                      | Meaning                                                                                                                                                         | In code                      |
|---------------------------|-----------------------------------------------------------------------------------------------------------------------------------------------------------------|------------------------------|
| **Instructions**          | An agent's system prompt, built in layers: the pipeline description, the shared instructions, the wiki rules, then the agent's role.                            | `AgentInstructions`          |
| **Pipeline description**  | `pipeline.md`: the public, embedded layer every agent shares. Nothing personal goes in it.                                                                     | `Agents/Instructions/pipeline.md` |
| **Shared instructions**   | The author's own instructions shared by every agent.                                                                                                             | `SharedInstructions.md`      |
| **Role**                  | One agent's own instructions.                                                                                                                                    | `{Agent}.Instructions.md`    |
| **Config**                | The private `config/` submodule holding the personal layers as markdown files. `config.template/` holds public, generic versions that **Scaffold config** copies in. | `ConfigFiles`                |

## Live updates

| Term       | Meaning                                                                                                                                                                   | In code                          |
|------------|---------------------------------------------------------------------------------------------------------------------------------------------------------------------------|----------------------------------|
| **Area**   | A top-level part of the wiki root whose changes the UI follows: `notes`, `facts`, `review`, `wiki`, `runs`, `conversations`. A change event names the area, and the UI refreshes its queries for it. | `ChangeNotifier.Areas`, `useLiveUpdates.ts` |
