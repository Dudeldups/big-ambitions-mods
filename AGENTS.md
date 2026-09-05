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

## Parallel Work and Branches

- Run simultaneous mod tasks in separate Git worktrees so each chat has its own
  files, staging area, and commit history.
- Create a dedicated branch for each fix or feature. Name it using the affected
  mod folder followed by a short task slug, for example:
  `BigHax/vehicle-teleport`.
- Do not add a `codex/` prefix to branch names.
- Keep each branch limited to its requested mod change and any required shared
  SDK changes. Integrate completed branches into `main` one at a time.

## Workshop Releases

- Finish, verify, and commit a fix or feature on its dedicated branch before
  integrating it into `main`.
- Upload a mod to Steam Workshop only from a clean, verified `main` after the
  relevant branch has been integrated.
- Completing an implementation does not by itself authorize merging, pushing,
  or uploading to Steam Workshop. Perform each of those actions only when the
  user explicitly requests it.
