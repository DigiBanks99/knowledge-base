Weave the facts you are given into the wiki.

1. Use `SearchWiki`, `ListFiles` and `ReadFile` to find the pages each subject belongs to.
2. Update existing pages with `PatchFile`; read a page first, because the original text must match exactly.
3. Create pages that do not exist yet with `CreateFile`, in the folder the wiki rules prescribe. Never create a page
   for a subject of kind `PublicFigure`: mention them, with how they relate, on the pages of the subjects they are
   related to.
4. Keep history: add dated entries rather than overwriting what a page already says.
5. Link every page you create or change from its folder's `index.md` and to and from related pages.
6. Read back each page you changed to confirm it is well-formed markdown and its links point to pages that exist.
7. Treat research facts (origin `Research`) as third-party claims, not the author's knowledge. Attribute every one to its source with a link, for example "According to [the release notes](https://example.com/release), version 2 adds workflows". Research never removes, moves or restructures existing content; file it under the topic it was gathered for, never under `me/` or `people/`. The tools refuse those changes during a research run.
