# Designated Driver: Getting started

Designated Driver is a Windows desktop tool that sets up GitHub repositories for a personal account to one convention, keeps them to it, and walks the day-to-day workflow: issue, branch, commit, pull request, merge, tidy up.  It runs `git` and `gh` and nothing else, and shows every command it runs.

`Docs\Designated Driver TDD.md` is the design.  This guide is the first hour.

## 1. What you need

- Windows 10 or 11 and the .NET 10 desktop runtime.  The release zip holds a single `DesignatedDriver.exe` that needs no installing.
- Git and the GitHub CLI:

```powershell
winget install Git.Git GitHub.cli
```

- A GitHub account.  Everything the tool does works on the Free plan; public repositories get a few features more (rulesets, code owner review, CodeQL), and the tool uses them where they exist.

## 2. Sign in once

Sign in to GitHub through the CLI, choosing GitHub.com and "Login with a web browser":

```powershell
gh auth login
```

The tool needs the `repo`, `workflow` and `project` scopes.  The Status screen lists them and shows the command for any that is missing.  Two more are optional:

- `user`, for the account's Actions minutes on Status.  Without it the tool shows this repository's minutes for the month instead.
- `delete_repo`, only if you want to delete throwaway repositories from the command line.

```powershell
gh auth refresh -h github.com -s user
```

### Clones and pushes: HTTPS or SSH

The tool clones and pushes with git itself, over HTTPS or SSH, and never lets gh pick.  The default follows gh's own setting for github.com.  With SSH, the host can be an alias from `~\.ssh\config` such as `github-paulfilkin`, which is how a machine with more than one GitHub account keeps a key per account; the wizard suggests the alias whose name or key file names your login.  The choice is remembered for the next repository.

## 3. Your first repository

Start the tool.  The first screen is **Repositories**.  You can add a clone you already have, scan any folder or repository to see what setting it up would involve, or press **Set up a repository**.  The set-up works on a new repository or one that already exists.

To work on a repository that is already on GitHub without changing it, press **Clone a repository**, give its owner/name and a parent folder, and press **Clone**.  Only `git clone` and reads from GitHub run.  The clone is added to the list as the current repository, with the default branch from GitHub and, if it was set up to the convention, the self-test command and required check from its manifest.  Settings then opens for it.  Scan offers the same as **Clone and work on it as it is** after a scan by name.

A repository without a manifest keeps its own settings in its entry in the tool's settings, its profile.  When you clone one, add one with **Add a clone**, or add a scanned clone from Scan, Settings reads what it already uses from GitHub and fills the boxes: the default branch, the required check from its ruleset or branch protection, its linked board and that board's columns for ready to pick up, in progress and in review, and the self-test command from its CI workflow.  Check them and press Save; nothing is kept until you do, and nothing is written to the repository or to GitHub.  **Read from GitHub** reads again later: an empty box is filled, and a value of yours that differs from GitHub's is replaced only if you say so.

The wizard has three pages:

1. **Where.**  A new repository, an existing folder on this machine, or an existing repository on GitHub.  Owner, name, description, visibility, the folder, the language (detected from an existing folder), the project to build, and the self-test command if the project has one.  An existing folder or repository is scanned here when you press Next; a blocker stops you, warnings are shown.  A repository that is already on GitHub keeps its visibility: the wizard shows it and you can't change it, since set-up never changes an existing repository's visibility.  Opened from Scan's **Apply the convention**, the page arrives filled in from the scan: owner, name, description, visibility, language and project.
2. **Options.**  Review required, Discussions, the release workflow for .NET projects, CodeQL on public repositories, the pull request checklist, and the board token (section 5).  The release workflow starts unticked, with the reason under it, when the repository already publishes releases another way.  A workflow of the repository's own that builds pull requests beside the convention's `ci.yml` is listed with a box to remove it in the set-up pull request, unticked.
3. **Plan.**  The plan as a checklist.  Nothing has changed until you press Start.  Press Start and watch it tick: the repository, its settings, labels, board, the convention files, hooks, Dependabot, and on public repositories push protection and the ruleset.  Every command appears in the activity panel.  If a step fails, fix the cause and press Retry; steps already done are not repeated.

A repository with history gets its convention files through an issue and a pull request, the convention's own rule applied to itself.  Merge that pull request from **Review**, then **Tidy up**.

What the repository already has is kept.  Its own issue forms stay, and the convention's are not added beside them.  GitHub's `enhancement` and `documentation` labels are renamed `feature` and `docs`, so the issues they label keep them.  A CONTRIBUTING.md of its own stays, with the convention's section on how a change gets in added at the end if it hasn't one.  A name that now leads to a renamed or transferred repository stops the set-up before anything changes.

## 4. The screens

| Screen | What it does |
|---|---|
| Repositories | The repositories the tool knows; filter by text or by your GitHub stars; add, clone as it is, switch, scan, check, remove |
| Scan | A read-only look at any folder or repository against the convention, ending with the next step: apply the convention, open Check, or fix the blockers.  **Work on it in Designated Driver** adds a scanned clone to the Repositories list; **Clone and work on it as it is** clones a repository scanned by name |
| Set up a repository | The wizard, for a new repository or an existing one |
| Status | Prerequisites, the repository, what the plan allows, the branch, open pull requests and issues, Dependabot alerts, Actions minutes, and two board actions |
| Check | What has drifted from the convention; tick and Apply |
| Start work | Write a new issue from the issue forms' fields, or pick one from the board's Todo column (or the profile's ready column), assign it to yourself, move its card to In Progress, make the branch |
| Save and share | Commit with the message checks, push |
| Submit | Update the branch from main if it is behind, open the pull request from the template with its `Closes #n` line filled in, move the issue's card to the profile's In review column if it has one, watch the check |
| Review | Approve, request changes, merge; admins only.  A merge re-reads the pull request first and stops if main has moved under it.  Picking another pull request while one loads shows the newest |
| Tidy up | Back to main, pull, delete merged branches |
| Fix a problem | The common situations, each with its commands |

Every screen loads what GitHub says now each time you open it.  You can move to another screen while one is loading: the loading screen stops, the new one loads at once, and the activity panel says which screen was stopped.  The one you left loads again when you come back.  Something you started yourself, such as a merge or a push, is never stopped this way; the new screen loads when it finishes.

## 5. The board

Each repository gets its own project board, linked to it, with GitHub's own columns: Todo, In Progress and Done.  The columns are left as GitHub made them, so GitHub's built-in automations do the moving: a new issue lands in Todo, a linked pull request moves it to In Progress, a merge or a close moves it to Done.

A repository that keeps its own board, with columns such as Ready, In progress and In review, works through its profile (section 3): Start work lists the issues in its ready column and moves a card to its in-progress column, and Submit moves the card to its in-review column when the pull request opens.  Column names are matched without regard to case.

Adding issues to the board from a workflow needs a token, because the workflow's own token cannot reach a user-owned project.  The wizard and the Status screen take a classic personal access token with the `repo` and `project` scopes and store it as the repository secret `PROJECT_TOKEN`; it is never written anywhere else.  Without it, the tool adds the issues it creates itself, and **Add missing issues to the board** on Status does the rest.

## 6. From a terminal

Three switches print what the screens show, with every command echoed:

```powershell
DesignatedDriver.exe --scan <folder or owner/name>
```

```powershell
DesignatedDriver.exe --setup <answers.json>
```

```powershell
DesignatedDriver.exe --check <folder> [--apply [--all]]
```

The answers file for `--setup` is a `SetupAnswers` object in camelCase; the board token, if wanted, comes from the `DESIGNATED_DRIVER_BOARD_TOKEN` environment variable (`REPO_STARTER_BOARD_TOKEN`, its name before the rename, is read when it is not set).  `--self-test` runs the built-in checks and prints PASS or FAIL per check.

## 7. Where things live

- Settings: `%AppData%\DesignatedDriver\settings.json`, the repositories the tool knows and the clone protocol.  No token is ever stored there.  On first start, settings from `%AppData%\RepoStarter`, where the tool kept them before it was renamed, are copied across; the old folder is left as it is.
- The convention manifest: `.github\designated-driver.json` in each repository, so a second machine needs no settings beyond the folder.  A repository set up before the rename has `.github\repo-starter.json`; it is read until Check renames it.
- The hooks: `.githooks\` in each repository, pointed to by `core.hooksPath` on every clone the tool sets up.  To push to `main` once on purpose, set `DESIGNATED_DRIVER_ALLOW_MAIN=1` for that one command (`REPO_STARTER_ALLOW_MAIN` still works).
