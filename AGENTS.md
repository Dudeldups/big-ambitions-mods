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

- For runtime C# changes to a supported mod, build the affected mod with:
  `.\tools\external-build\BuildBigAmbitionsMods.ps1 -ModName "<ModName>"`.
- When a shared API or dependency changes, also build every affected dependent
  mod.
- Use the build script's `-Install` option only when the user requests local
  installation or in-game testing.
- Do not claim that behavior is verified in game unless Big Ambitions was
  launched and the changed behavior was actually observed. Distinguish build
  verification from runtime verification in the final summary.

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
