# Repository Instructions

## Commits

When a requested change is complete and verified, commit the changes you made in
this repository. Do not wait for the user to request a commit separately.

- Never include pre-existing, unrelated, or unverified changes in your commit.
- Inspect `git status` and the staged diff before committing.
- Use Conventional Commit messages scoped to the affected mod or component:
  `type(scope): imperative summary`.
- Use the mod folder name as the scope when the change applies to one mod, for
  example: `feat(BigHax): add vehicle teleport action`.
- Choose the Conventional Commit type that fits the change (`feat`, `fix`,
  `refactor`, `docs`, `test`, `build`, `chore`, and so on).
- Commit locally only; do not push unless the user explicitly asks.
