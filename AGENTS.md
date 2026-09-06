# Repository Instructions

## Scope and Change Boundaries

- Keep a mod task inside `Assets/Mods/<TargetMod>` unless the requested change
  requires shared SDK or dependency changes.
- Treat changes outside the target mod as separate, deliberate work and explain
  why they are required in the final summary.
- Preserve unrelated and pre-existing work. Never revert, overwrite, stage, or
  commit changes that do not belong to the current task.
- Stage explicit paths with `git add -- <paths>`. Never use `git add .` or
  `git add -A`.

## Unity Project Safety

- Use Unity `2022.3.62f2` for this project.
- Keep every Unity asset and its `.meta` file together when adding, moving, or
  deleting it. Preserve existing GUIDs and serialized references unless the
  requested change specifically requires replacing them.
- Do not manually edit generated `.csproj` or `.sln` files, or generated content
  under `Library`, `Temp`, `Logs`, `obj`, or `Output`.
- Never commit locally imported game DLLs from
  `Assets/_BaDependencies/GameDlls` or reference/decompiled material from
  `tmp_decompile`.

## Verification

- For runtime C# changes to a supported mod, build and locally install the
  affected mod with:
  `.\tools\external-build\BuildBigAmbitionsMods.ps1 -ModName "<ModName>" -Install`.
- When a shared API or dependency changes, also build every affected dependent
  mod.
- After any change to a mod, install the resulting mod into `ModsLocal` before
  handing it back for testing. Local installation is a standard completion step
  and does not require a separate user request.
- If a changed mod cannot be installed, report the blocker and do not present it
  as ready for runtime testing.
- Do not claim that behavior is verified in game unless Big Ambitions was
  launched and the changed behavior was actually observed. Distinguish build
  verification from runtime verification in the final summary.

## Diagnostic Logging

- Treat useful diagnostic logging as part of implementing a feature or bug fix,
  not as a follow-up after an inconclusive test. Before runtime testing, add
  focused logs that can confirm whether the changed path was entered, which
  important decision or state was observed, and whether it succeeded or failed.
- Use the target mod's existing logging wrapper and include enough context to
  correlate an event, such as the affected entity, operation, state transition,
  or failure reason. Never log credentials, tokens, or other sensitive data.
- Log warnings and errors for unexpected or failed outcomes. Use informational
  logging for meaningful lifecycle events and results that help validate the
  requested behavior.
- Avoid unconditional logging in per-frame updates, tight loops, or other hot
  paths. Gate, deduplicate, or rate-limit repeated messages so a useful trace
  does not become log spam.
- Keep diagnostic logs through the initial runtime validation. After the
  behavior is confirmed, remove temporary or noisy messages and retain only
  logs that remain useful for support and future regressions.

## Mod Dependencies and Third-Party Code

- Update `tools/external-build/mods.externalbuild.json` when a new inter-mod
  dependency is required for correct external build ordering or references.
- Do not add or update third-party source code or binaries without preserving
  applicable license and attribution files and confirming that the dependency
  is compatible with the repository.

## Localization

- Put new user-visible text into the mod's localization system instead of
  hard-coding it when that mod already uses localization.
- Add or update the English locale as the source text. Update other locales only
  when reliable translations are available; do not invent translations.
- Preserve existing localization keys and validate edited locale JSON files.

## Commits

When a requested implementation is ready for runtime testing and its required
build and local-install verification has passed, commit the changes you made in
this repository. Do not wait for the user to request a commit separately.

- Never include pre-existing, unrelated, or unverified changes in your commit.
- Inspect `git status` and the staged diff before committing.
- Use Conventional Commit messages scoped to the affected mod or component:
  `type(scope): imperative summary`.
- Use the mod folder name as the scope when the change applies to one mod, for
  example: `feat(BigHax): add vehicle teleport action`.
- Choose the Conventional Commit type that fits the change (`feat`, `fix`,
  `refactor`, `docs`, `test`, `build`, `chore`, and so on).
- Never push directly to `main`. For work completed on a dedicated task branch,
  push only that branch as part of the pull-request workflow below.

## Parallel Work and Branches

- Run simultaneous mod tasks in separate Git worktrees so each chat has its own
  files, staging area, and commit history.
- Create a dedicated branch for each fix or feature. Name it using the affected
  mod folder followed by a short task slug, for example:
  `BigHax/vehicle-teleport`.
- Do not add a `codex/` prefix to branch names.
- Keep each branch limited to its requested mod change and any required shared
  SDK changes.
- Before opening a pull request, update the task branch against the latest
  `origin/main`, resolve any conflicts in the task worktree, and rerun the
  required build and local-install verification.
- When the implementation is ready for runtime testing and committed, push the
  task branch to `origin` and create a pull request targeting `main`. Leave the
  pull request open while the user tests the installed mod.
- Use pull requests as the only path for integrating task branches into `main`.
  Never merge a feature or bug-fix pull request merely because it builds,
  installs, passes automated checks, or reports that it is mergeable.
- Merge a feature or bug-fix pull request only after the user explicitly
  confirms that the requested behavior was tested successfully in game. That
  confirmation authorizes merging the matching pull request once required
  checks pass and it reports that it is mergeable.
- If runtime testing fails, keep the same task branch and pull request open,
  diagnose and fix the issue there, rebuild and reinstall the mod, and wait for
  another explicit successful-test confirmation before merging.
- Immediately before merging an approved pull request, update the task branch
  against the latest `origin/main`, resolve any conflicts in the task worktree,
  and rerun the required build and local-install verification. Never merge a
  task branch directly into the local `main` checkout.
- If that update or conflict resolution changes the target mod, a shared
  dependency, or anything that could affect the tested behavior, treat the
  earlier runtime confirmation as stale and wait for the user to test and
  confirm the updated installed build before merging.
- If another pull request changes `main` first and introduces a conflict, update
  the task branch from the new `origin/main`, resolve and verify it again, then
  merge its pull request.
- If authentication, branch protection, failed checks, or unresolved conflicts
  prevent the push, pull request, or merge, stop and report the blocker. Never
  bypass repository protections.

## Workshop Releases

- Finish, verify, and commit a fix or feature on its dedicated branch before
  integrating it into `main` through the pull-request workflow above.
- Run the final Workshop preparation and `ba-workshop upload` only from the
  local checkout on branch `main`. Never upload from a worktree, task branch,
  or detached `HEAD`.
- Before Workshop publication, confirm that local `main` is clean and matches
  the latest `origin/main`, that the relevant pull request was merged, and that
  the mod was rebuilt and verified from that exact `main` state. Rerun the
  uploader's `validate`, `stage-check`, and `plan` commands from `main` before
  `upload`.
- Pushing the task branch and creating its pull request are expected steps when
  an implementation is ready for runtime testing. Merging requires the user's
  explicit successful in-game test confirmation. Workshop publication remains
  a separate action and requires an explicit user request to upload the named
  mod.
- A user's explicit request to upload, publish, or update a named mod on Steam
  Workshop authorizes the complete prerequisite workflow for that release:
  push its verified task branch, create and merge its pull request into `main`,
  synchronize the local `main`, rebuild and verify the mod, run the uploader's
  final checks, and make one matching Workshop upload attempt. Do not ask for
  separate confirmation for those prerequisite steps or for that one upload
  attempt.
- An upload request does not waive runtime validation. If the user has not yet
  confirmed that the feature or bug fix works in game, stop after installing the
  mod and opening or updating its pull request, and wait for that confirmation
  before merging or uploading.
- This end-to-end authorization applies only to the named mod and the Workshop
  action the user requested. Stop and ask if `plan` unexpectedly resolves to
  CREATE instead of UPDATE, if a visibility change is required but was not
  requested, or if the target item or Steam identity is ambiguous.
- Do not bypass failed checks, branch protection, unresolved merge conflicts, or
  unrelated changes to complete an upload. If the upload fails or times out, do
  not retry it automatically; report the observed state and wait for renewed
  direction.
