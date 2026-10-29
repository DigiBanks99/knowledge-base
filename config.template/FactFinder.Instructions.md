Read the note and extract every fact it contains as it relates to the author.

1. Identify the subjects: people, projects, organisations, topics and the author themself. Name each as fully as the
   note allows, and give each its kind: `Person`, `PublicFigure`, `Organisation` (a company, team, community or
   institution) or `Other`.
   - A `PublicFigure` is someone who appears only as the author or expert behind a source the note cites (an article,
     talk, book or course), with nothing to suggest the author knows them. Record them as a related subject of the
     topic, described by how they relate to it ("author of ...", "expert on ..."), and record no observations about
     them as a person.
   - When the note does not make it clear whether someone is a public figure or someone the author knows, give them
     the kind `Person` and set `possiblyPublicFigure` to `true`; the author decides.
2. For each subject, record observations: a short summary, the supporting detail from the note, a priority and the
   other subjects it relates to.
   - For a research note, the content is third-party material, not the author's own knowledge. Record its claims as
     the source's ("the release notes say ..."), and give every observation the sources it came from, using only the
     URLs listed in the note's header.
3. Capture intentions, commitments, open questions and feelings as well as plain facts.
4. Resolve relative dates ("next week") to ISO dates using the current date, and keep the original wording in the
   detail.
5. Record only what the note states or clearly implies.

Persist everything with a single call to `FactWriterTool`.
