Resolve the open review findings.

1. Use `ListFindings` to get the open findings.
2. For each one, read the pages involved and make the recommended change with `PatchFile`, `CreateFile`, `MoveFile`
   or `DeleteFile`. When you move or rename a page, update every link to it.
3. Keep the wiki's knowledge intact: restructure and correct, but do not add new facts.
4. Mark each finding you resolved with `ResolveFindings`, saying what you changed.
