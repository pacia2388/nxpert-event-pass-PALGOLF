# Git

- Start work from a freshly fetched `origin/DEV_1818`; if a worktree starts from another commit, merge `origin/DEV_1818` in first.
- Open PRs from `DEV_1818`: merge the work branch into `DEV_1818`, push `DEV_1818`, then open the PR from `DEV_1818` into `main`. Worktree and feature branches stay local.

# Worktrees

- In a new worktree, run `scripts\setup-worktree.cmd` before other work. It copies the untracked local files from the main checkout (database connection, face-engine settings, `start-all.ps1`, `CLAUDE.md`) and links `node_modules`, the face engine's `.venv`, and its model weights. Keep the copies uncommitted and their contents out of output.
- Before deleting a worktree, `rmdir` its junctions (`facerog-engine\.venv`, `frontend\web\node_modules`): a recursive delete follows them into the main checkout.

# Review

- Review rules: `documentation/coding-standards.md`.
