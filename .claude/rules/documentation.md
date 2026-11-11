# Documentation rules

- Put all project documentation (guides, references, matrices, specs) in the **`documentation/`**
  folder at the repo root. Do not scatter docs in other folders.
- Exceptions: per-project `README.md` files and `.claude/` rules and skills stay where they are.
- Implementation plans go in **`plans/`** (one Markdown file per feature). When work on a planned
  module starts, update its plan first (status, open questions, tables, steps) and keep it current.
- Mark progress in every plan: tick each step (`- [x]`) as it lands, and add a marker to each
  section heading when the whole section is done, e.g. `### Backend ✅ Done (2026-11-12)`.
  Use `🚧 In progress` while a section is partly done. Add a change-log row for each update.
- Keep docs in Markdown (`.md`) unless the user asks for another format.
- When code changes what a doc describes (tables, roles, endpoints), update the doc in the same change.
