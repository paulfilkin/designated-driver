# Designated Driver: technical design

A Windows desktop tool that sets up GitHub repositories for a personal account to one consistent convention, keeps them to it, and then walks the day-to-day workflow (issue, branch, commit, pull request, merge, tidy up) the way Git Helper does.  It targets what GitHub Free gives a personal account, on private and public repositories alike, and uses the paid-only features only when the repository happens to allow them.

Date: 19 November 2026.  Sections 1 to 11 are the proposal as written that day.  Section 12 records what was built from it, to v0.2.3 on 21 November 2026.

## 1. Goals

1. Create a new repository, or bring an existing one in, so that it has the same issues, labels, board, templates, settings and automation as every other repository the owner has set up with the tool.
2. Use every piece of free capability GitHub offers a personal account, and nothing that needs a paid plan unless it is available for the repository in hand (public repositories get more).
3. Keep repositories to the convention over time: show what has drifted and bring it up to date in one action.
4. Work with any number of repositories, switching between them.
5. Carry Git Helper's workflow screens across, with the Partner Manager specifics turned into settings.
6. Stay a single `.exe` that runs `git` and `gh` and nothing else, with every command visible, as Git Helper does.

Non-goals: organisation repositories (they work where the commands are the same, but nothing is designed for them), GitHub Enterprise, anything that needs a GitHub App or a server.

## 2. What GitHub Free gives a personal account

Checked against GitHub's documentation on 19 November 2026.  "Private" and "public" are the repository's visibility under a personal account on the Free plan.

| Capability | Private | Public | How the tool uses it |
|---|---|---|---|
| Issues, labels, milestones, issue forms | Yes | Yes | Labels and issue forms are part of the convention. |
| Projects (user-owned boards), linked to repositories | Yes | Yes | One board per repository, columns set by the tool. |
| Project built-in workflows (closed → Done, merged → Done) | Yes, on by default | Yes | Left on.  Auto-add is limited to one per project on Free and cannot be set by the API, so an Actions workflow does the adding instead. |
| GitHub Actions | Included compute allowance per month ($12 on Free at the time of writing), standard runners | Free for standard runners | Every automation below is an Actions workflow.  The tool shows the month's usage on Status. |
| Repository secrets and variables | Yes | Yes | One secret for the board token. |
| Dependabot alerts, security updates, version updates | Yes | Yes | Turned on, with a `dependabot.yml` for the package ecosystems found. |
| Merge settings: squash only, delete branch on merge, allow update branch | Yes | Yes | Set on every repository. |
| Pull request template, CONTRIBUTING, issue template chooser | Yes | Yes | Written from the convention. |
| Discussions | Yes | Yes | Optional, off by default. |
| Rulesets and protected branches (required review, required checks, no direct push) | **No** | Yes | Created on public repositories.  On private ones the tool enforces the same rules itself, with local hooks and a guard workflow (section 7). |
| CODEOWNERS and required code owner review | **No** | Yes | The file is written anyway, so it takes effect if the repository goes public or the plan changes. |
| Draft pull requests | **No** | Yes | Not relied on. |
| Auto-merge | Needs rules | Yes | Not relied on. |
| Wiki, Pages | **No** | Yes | Not used.  The convention keeps documentation in `Docs\`. |
| Secret scanning push protection | **No** (paid add-on) | Yes | Turned on for public repositories.  The CI workflow has a committed-secrets check for every repository. |
| Code scanning (CodeQL) | **No** | Yes | A CodeQL workflow is written for public repositories only. |
| Repository templates | Yes | Yes | Not used as the source of the convention (section 3.6 says why). |

Two consequences shape the design.  Policy cannot be enforced by GitHub on a private repository, so the tool and the repository's own hooks and workflows enforce it.  And since visibility can change, every piece is written in a form that works on both, and the tool re-checks what applies when it checks the convention.

## 3. The convention

The convention is versioned.  Each repository records the version it was set up to in `.github/repo-starter.json` (section 3.7), so the tool can tell what has drifted.

### 3.1 Files

Written into the repository on set-up, with placeholders filled from the answers in the wizard.  Existing files are not overwritten without the user ticking them in the Check screen.

| File | Purpose | Placeholders |
|---|---|---|
| `README.md` | Skeleton: name, one-line purpose, requirements, running it, layout.  Only when the repository has none. | name, description |
| `CONTRIBUTING.md` | How issues become pull requests: branch names, commit messages, the pull request template, the checks, who merges.  Written from Partner Manager's with the project specifics removed. | name, self-test command, default branch |
| `Docs\Working with Git.md` | The step-by-step Git and GitHub guide, including the common problems section that Fix a problem is built on. | name |
| `.github/pull_request_template.md` | Issue, what and why, how it was tested, checklist, AI assistance. | checklist items chosen in the wizard |
| `.github/ISSUE_TEMPLATE/bug.yml`, `feature.yml`, `config.yml` | Issue forms, each applying its label.  Blank issues off. | name |
| `.github/CODEOWNERS` | `* @owner` | owner login |
| `.github/workflows/ci.yml` | Build and test (section 3.5). | project file, test command |
| `.github/workflows/guard.yml` | Pull request hygiene and direct-push detection (section 7). | default branch |
| `.github/workflows/board.yml` | Adds new issues and pull requests to the board (section 3.3). | board number |
| `.github/workflows/release.yml` | Optional: on a version tag, publishes the build as a release with the exe attached.  .NET desktop projects only. | project file |
| `.github/workflows/codeql.yml` | Public repositories only. | language |
| `.github/dependabot.yml` | Weekly version updates for the ecosystems found (nuget, github-actions, npm, pip). | ecosystems |
| `.githooks/commit-msg`, `.githooks/pre-push` | Local enforcement (section 7). | default branch |
| `.gitignore`, `.gitattributes`, `.editorconfig` | By language, from the tool's own copies.  `.gitignore` only when the repository has none. | language |
| `.github/repo-starter.json` | The convention manifest (section 3.7). | all |

### 3.2 Labels

A fixed set, created with `gh label create --force` so colours and descriptions stay consistent:

`bug`, `feature`, `docs`, `chore`, `question`, `good first issue`, `blocked`, `wontfix`.

The branch prefix rule from Git Helper stays: `bug` gives `fix/`, `docs` gives `docs/`, `chore` gives `chore/`, everything else `feature/`.  Labels not in the set are left alone.

### 3.3 The board

One user-owned project per repository, linked to it.

- Created with `gh project create --owner @me --title "<name>"`, then `gh project link`.
- The Status field's options are set to `Ready`, `In progress`, `Done` by a GraphQL `updateProjectV2Field` call through `gh api graphql`, replacing the default `Todo`, `In Progress`, `Done`.  Git Helper already looks the field and options up by name, so the same names keep the workflow screens working.
- The two built-in workflows that move closed issues and merged pull requests to Done are on by default and left on.
- Adding items: `board.yml` runs `actions/add-to-project` on `issues: opened, reopened` and `pull_request: opened`.  It needs a token with the `project` scope, because the workflow's own `GITHUB_TOKEN` cannot reach user-owned projects.  The wizard asks for a classic personal access token with `repo` and `project` scopes once, stores it with `gh secret set PROJECT_TOKEN`, and the token itself is never written to settings or the activity panel.  If the user skips this, the workflow is still written but disabled, and the Status screen offers "Add missing issues to the board", which does the same with the user's own `gh` login.

### 3.4 Repository settings

Set with `gh repo edit` and `gh api`:

- Squash merge only, with the pull request title as the commit message (`--squash-merge-commit-message pr-title`).  Merge commits and rebase merges off.
- Delete branch on merge.  Allow update branch.
- Issues on, projects on, wiki off (not available on private Free anyway).
- Dependabot alerts (`PUT /repos/{o}/{r}/vulnerability-alerts`) and security updates (`PUT /repos/{o}/{r}/automated-security-fixes`) on.
- Secret scanning and push protection on when the API accepts it (public repositories).
- Topics and description from the wizard.
- On public repositories, or when the account's plan allows it (`gh api user --jq .plan.name` is not `free`): a ruleset on the default branch requiring a pull request, one approving review when the wizard says so, the `Build and test` check, no force pushes and no deletion, with the owner as a bypass actor.  Created with `POST /repos/{o}/{r}/rulesets` using the same JSON shape Partner Manager's ruleset returns.  On private Free repositories this step is recorded as "not available" in the manifest, not as a failure.

### 3.5 CI

`ci.yml` runs on pull requests to the default branch and on pushes to it, with a job named `Build and test` so the required check name is the same everywhere.  Steps: check out, the committed-secrets check from Partner Manager's workflow (data files, keys, certificates, `.env`), then the language steps.  For .NET: set up the SDK, `dotnet build -warnaserror`, `dotnet test` if a test project exists, then the self-test command if one was given.  For other languages the wizard asks for a build command and a test command and the job runs those.  The job fails the pull request's check either way; what GitHub does with a failed check depends on whether rules exist (section 7).

### 3.6 Where the convention lives

In the tool, as embedded resources, one folder per convention version.  Not in a GitHub template repository, for three reasons: a template repository copies files once and offers no way to bring repositories up to date later; labels, settings, the board and secrets are not part of a template; and a template cannot fill placeholders.  The tool's convention is also what the Check screen compares against, so there is one source.

### 3.7 The manifest

`.github/repo-starter.json` records the convention version, the date, the language, the options chosen in the wizard (release workflow, CodeQL, discussions, review required), the board owner and number, and which steps were "not available" on this plan.  The Check screen reads it to know what the repository was set up to; the workflow screens read the board number from it, so a second machine needs no settings beyond the folder.

## 4. Architecture

A new solution that starts from Git Helper's code and keeps its shape: WPF on `net10.0-windows`, warnings as errors, no NuGet packages, strings in `Strings.resx`, a `--self-test` mode, a command runner that shows every `git` and `gh` call.

```
Core\            Pure logic.  From Git Helper: branch names, commit checks, review rules, template
                 filling, conflict markers.  New: Convention (the file set and placeholders),
                 Manifest, Plan (the ordered steps and what each needs), Drift (what differs).
Convention\      Embedded resources: v1\files\..., v1\labels.json, v1\settings.json.
Models\          JSON models for gh output.  New: repository, label, project field, ruleset, plan.
Services\        CommandRunner, GitService, GitHubService (extended), SetupService (runs a Plan),
                 ConventionService (reads embedded files, fills placeholders, compares).
Screens\         Repositories, New repository, Check, then Status, Start work, Save and share,
                 Submit, Review, Tidy up, Fix a problem from Git Helper.
Windows\         Settings, About, Text viewer, Token prompt.
SelfTest\        Checks with sample gh output, as in Git Helper.
Tools\           Update-Strings.ps1, New-Icon.ps1.
```

The Partner Manager specifics in Git Helper become settings or are read from the repository: the label to prefix map (section 3.2), the board column names (read from the board), the pull request template (read from the repository, the convention's copy as the fallback), the self-test command and required check name (from the manifest, editable), and the nine strings that name Partner Manager.

### 4.1 Plans and steps

Set-up is a `Plan`: an ordered list of `Step` records, each with a name, the commands it will run, a `Check` that says whether it is already done, and an `Apply`.  The wizard builds the plan from the answers, shows it as a checklist, and `SetupService` runs it: for each step, check, skip if done, otherwise apply, then check again.  Every step is idempotent, so a plan that failed part way (no network, a refused token) is rerun from the start and only does what is left.  The same `Plan` type drives the Check screen: it builds the plan for the current convention version, runs every `Check`, and offers to apply the ones that fail.

Steps, in order:

1. Create the repository (`gh repo create`, with `--source` and `--push` for an existing folder, or `--clone` for a new one), or confirm an existing one.
2. Repository settings (section 3.4, without the ruleset).
3. Labels.
4. Board: create, link, set the Status options.
5. Board token secret (optional).
6. Convention files, committed on a branch `chore/repo-starter-v1` and merged through a pull request if the repository already has history, or committed straight to the default branch if the repository is empty.  The pull request path is the convention's own rule applied to itself.
7. Local hooks: `git config core.hooksPath .githooks`.
8. Dependabot and secret scanning.
9. Ruleset, where available.
10. Write the manifest (part of step 6's commit, updated at the end).
11. Add the repository to the tool's settings and open Status.

### 4.2 Capability probe

Before building a plan the tool reads `gh api user --jq .plan.name` and the repository's visibility, and records which steps apply.  It also tries nothing that the probe says is unavailable, so the activity panel stays free of expected failures.  If an API call still returns 403 or 422 with an upgrade message, the step is recorded as "not available" and the plan continues.

## 5. Screens

| Screen | What it does |
|---|---|
| Repositories | The repositories the tool knows, with convention version, drift (from the last Check), open pull request count.  Add an existing clone.  Start the wizard.  Switch the current repository. |
| New repository | Three pages.  Where: owner (`@me` by default), name, description, visibility, new or existing folder, language.  Options: review required, release workflow, CodeQL (public only), discussions, which checklist items, the board token.  Create: the plan as a checklist that ticks as it runs, with the activity panel below. |
| Check | Every step's check for the current repository, with what differs (a file diff, a missing label, a setting).  Tick the ones to apply, then Apply.  Also "Update to convention v2" when the tool is newer than the manifest. |
| Status | As Git Helper, plus the convention version, the Actions usage for the month (`GET /users/{user}/settings/billing/actions`), Dependabot alert count, and "Add missing issues to the board". |
| Start work, Save and share, Submit, Review, Tidy up, Fix a problem | As Git Helper, with the generalisations in section 4.  Review appears for the repository's admins, which on a personal repository is the owner. |

A repository switcher in the header replaces Git Helper's single folder.  Settings keeps per-repository overrides (self-test command, check name, default branch) and the tool-wide ones.

## 6. Settings

`%AppData%\RepoStarter\settings.json`:

```json
{
  "current": "paulfilkin/example",
  "repositories": [
    { "folder": "C:\\Users\\paul\\Documents\\GIT\\example", "owner": "paulfilkin", "name": "example",
      "selfTestCommand": "", "requiredCheck": "Build and test", "defaultBranch": "main" }
  ]
}
```

Board owner and number come from the manifest, not settings.  No token is ever stored; `gh` holds the sign-in and the board token lives only as a repository secret.

## 7. Enforcing the rules without rulesets

On a private Free repository GitHub will accept a direct push to `main`, a merge with failing checks and a merge without review.  Three layers stand in for the ruleset, and all three are written on every repository so nothing changes when visibility does:

1. **The tool.**  Review merges only with `--squash`, only when the required check passed, and only when the review rule is met or the user gives a bypass reason, as Git Helper does.  Save and share never commits on the default branch; it sends the user to Fix a problem.
2. **Local hooks** in `.githooks\`, pointed to by `core.hooksPath` on every clone the tool sets up.  `commit-msg` applies the commit message rules (subject length, imperative first word, no trailing full stop, no em-dash).  `pre-push` refuses a push to the default branch unless the environment variable `REPO_STARTER_ALLOW_MAIN` is set, which the tool never sets and the user can set from a shell when they mean it.  Hooks are plain `sh`, so Git for Windows, macOS and Linux all run them.
3. **The guard workflow.**  On `pull_request` it fails the check if the body has no `Closes #<n>` line, if the branch name does not match the convention, or if the head is behind the base by more than the wizard's limit.  On `push` to the default branch it looks up the pull request for the commit (`GET /repos/{o}/{r}/commits/{sha}/pulls`); if there is none, the workflow fails and opens an issue labelled `chore` titled "Direct push to main: <sha>", so the slip is visible on the board.  Detection, not prevention, but the owner is the only one who can push, and the Check screen shows the open guard issues.

Where the ruleset does exist (public, or a paid plan), the same files are present and GitHub adds prevention on top.

## 8. Self-test

`--self-test`, as in Git Helper, extended with:

- Every convention file fills its placeholders with no placeholder left (`{{` absent from the output).
- The plan for each combination of visibility and plan name contains exactly the expected steps.
- The ruleset JSON round-trips through the model and matches the shape of a saved real one.
- The Status options GraphQL mutation is built with the right field and option names.
- Drift detection: a changed label colour, a missing file and a changed setting are each reported, and an identical repository reports nothing.
- The commit-msg and pre-push hooks, run with `sh` against sample inputs, accept and refuse the right cases.
- The existing Git Helper checks (branch names, commit rules, review rules, Fix a problem commands).

## 9. Testing it on itself

The first repository the tool creates is its own: run the wizard with the tool's folder as the existing folder, private, .NET, release workflow on.  That exercises the existing-folder path, the pull request path for the convention files, hooks on a real clone, the private-Free capability path, and the Actions workflows on the first pull request.  Then a second run with a throwaway public repository covers the ruleset, CodeQL and push protection steps, after which the repository is deleted with `gh repo delete`.  Later, Check is run against both after editing a label and a workflow by hand, and must show exactly those two differences.

## 10. Open questions

1. Convention version 1's exact file contents, in particular the pull request checklist and `Working with Git.md` with the Partner Manager specifics removed.  To be written as the first task of the new project, from the Partner Manager originals.
2. Whether the board token step is worth its friction.  Without it the tool adds issues to the board itself, which covers issues created through the tool and through "Add missing issues" on Status, but not issues created on the web between runs.
3. Whether `release.yml` publishes a framework-dependent single-file exe (small, needs the runtime) or self-contained (large, runs anywhere).  Default: framework-dependent, matching Git Helper.
4. Column names: `Ready`, `In progress`, `Done` as in Git Helper, or add `Backlog` so that Ready means ready to pick up.  Default: add `Backlog`; Start work lists Ready only.

## 11. Phases

1. **Foundation.**  New solution from Git Helper's code, multi-repository settings, the generalisations in section 4, the capability probe.  All existing screens working against any repository.  Self-test green.
2. **Convention v1 and set-up.**  Embedded files, labels, board, settings, hooks, guard and board workflows, the plan runner, the New repository wizard.  Tested on the tool's own repository.
3. **Check.**  Drift detection and apply, manifest versioning, the public-repository steps (ruleset, CodeQL, push protection), tested on a throwaway public repository.
4. **Finish.**  Actions usage and Dependabot on Status, release workflow, getting started guide, icon.

## 12. As built

All four phases were built on 20 November 2026 and released as v0.1.0, with two further releases the same day and six more on 21 November.  The design held except where this section says otherwise.  Each point names the section it changes.

### 12.1 The open questions, answered

1. The convention files were written from Partner Manager's originals with the project specifics removed, as the first task of phase 2.
2. The board token stays, and stays optional.  Without it the tool adds the issues it creates itself, and "Add missing issues to the board" on Status does the rest.
3. The release is framework-dependent.  It is published with `-p:SelfContained=false`, not the `--self-contained false` switch, which SDK 10.0.100 ignored on the build machine.  The tag's number, without the `v`, is passed as the `Version` property, so the exe and the About window carry it whatever the project file says; v0.1.1 and v0.2.0 were built before that and report 0.1.0.
4. The columns are GitHub's own `Todo`, `In Progress`, `Done`, with no `Backlog`.  `Ready` is accepted as an alias of `Todo` for boards made before the convention, such as Partner Manager's.  An issue is ready to pick up when it is in Todo and not labelled `blocked`.

### 12.2 Where the build differs from the design

**The board's Status field is never rewritten** (3.3).  The first live run showed that `updateProjectV2Field` regenerates every option id, even for options passed unchanged, and GitHub's built-in project workflows reference those ids: rewriting the field disabled five of the six.  The tool therefore leaves the columns as GitHub makes them and relies on the built-in automations to move items.  `board.yml` only adds opened issues and pull requests.  The board step reuses an open user project with the repository's name before creating one, so a failed `gh project link` cannot leave an orphan for a rerun to duplicate.

**The capability probe does not read the plan name** (4.2).  `gh api user --jq .plan.name` returns nothing without the `user` scope, which the sign-in does not grant by default.  The probe reads the repository's visibility and, on a private repository, the branch rules endpoint: a 403 with an upgrade message means no rulesets, a 200 means they exist.  The same scope gap reaches Status (5): without `user` the account's Actions minutes are hidden, so the tool sums the billable time of this repository's runs since the first of the month instead, capped at 50 runs, and says which it is showing.

**Status reads only what it shows** (5).  Opening the app on this public repository ran about 66 commands and took about 25 seconds, 51 of them for Actions minutes: the runs list, whose full output filled the activity panel, and a timing call for each of 50 runs, every one 0 billable milliseconds, since Actions minutes are free on a public repository.  The plan name and billing calls came back empty and 404, as they always do without the `user` scope.  Status now skips both when `gh auth status` lists the token's scopes without `user` (`AuthStatus.Lacks`, `ActionsUsage.ReadsAccount`); when gh lists no scopes it still asks.  A public repository's runs aren't counted and the card says its minutes are free (`ActionsUsage.CountsRuns`); a private one's runs list is cut to the run ids with `--jq` (`ActionsUsage.RunIdsOnly`).  On this repository Status now runs 13 commands and loads in about 6 seconds.

**An organisation's repository isn't judged by a personal plan** (3.4, 4.2).  Scanning an organisation's private repository listed push protection and code scanning under not available on this plan, though for an organisation they depend on its GitHub Advanced Security licence, which the tool doesn't read; and the organisation warning said the board would be organisation-owned when a usable one was already linked.  For a private repository an organisation owns (`ScanFacts.IsOrganisationPrivate`), Scan now has one worth-knowing line saying both depend on the organisation's licence and that set-up leaves them alone, in place of the two not-available lines, and Check's push protection note says the same.  The organisation warning mentions an organisation-owned board only when set-up would create one, and otherwise says the linked board is used as it is.

**Clones and pushes have a protocol setting** (6).  `gitProtocol` (https or ssh) and `sshHost` (github.com or an alias from `~\.ssh\config`) are tool-wide settings, empty until chosen, with the default following gh's own per-host setting and the alias whose name or key file names the login.  The create step sets origin itself and pushes with git; gh never picks the URL.  Settings also gained a per-repository board override (owner and number), used only while the repository has no manifest, and `starredOnly` for the Repositories filter.

**A Scan screen and three switches** (5).  Scan is a read-only look at the current repository, any folder, or an owner/name with no clone, against the convention.  `--scan`, `--setup <answers.json>` and `--check <folder> [--apply [--all]]` print what the screens show, with every command echoed, and exit with a code that says what they found.  The answers file is a `SetupAnswers` object in camelCase; the board token comes from the `REPO_STARTER_BOARD_TOKEN` environment variable.

**Convention files reach a repository with history through an issue** (4.1).  The set-up opens an issue labelled `chore`, commits the files on `chore/<n>-repo-starter-v1` and opens a pull request with `Closes #<n>`.  An apply from Check does the same with an issue "Bring the repository up to the convention" and a branch `chore/<n>-convention-update`, one pull request per Apply.  Hooks are pointed at `.githooks` after that first push, so the push is never refused by the hook it installs.

**The manifest records the wizard's answers** (3.7).  Description, project file, test project, build and test commands, checklist, behind limit and topics are kept under `answers`, so Check can render every file again and compare.  A manifest without them is a note on Check, not an item to apply; a set-up rerun records them, and rewrites the manifest whenever its answers or options differ from the ones given.  The set-up date is kept on an update.

**Check's defaults** (5).  Files a project is expected to tailor (`CONTRIBUTING.md`, `Docs\Working with Git.md`, the pull request template, `ci.yml`) are reported but unticked; everything else is ticked.  Line endings, trailing spaces and the version pin on a workflow's `uses:` line do not count as drift, so Dependabot's bumps leave Check quiet.  Labels apply as a set, since they are cheap and fixed by the convention.  `README.md` is written only when the repository has none and is never compared.

**Guard and Dependabot** (3.1, 7).  Guard's pull request job skips `dependabot[bot]`, whose branches have their own names and no issue.  GitHub links a commit to its pull request a few seconds after a merge, and the direct-push job once ran inside that gap and reported a merged Dependabot pull request, so the job now reads the pull request number from the merge commit's subject and accepts the commit when that pull request's merge commit is this one, and retries the lookup twice, twenty seconds apart, before it opens an issue.  `dependabot.yml` lists an ecosystem only when its manifest is among the repository's files; `github-actions` is always listed.  The public probe repository showed that a new public repository already has secret scanning and push protection on, that the ruleset POST in Partner Manager's shape is accepted, and that CodeQL fails on a repository with no source, as expected.

**Rules already in force can stand for the ruleset** (3.4, 5, 7).  Scanning a repository whose own ruleset already protected main with a check of its own name, the convention's ruleset showed only as worth knowing, "added alongside", and set-up would have added it requiring Build and test, a check the repository's kept `ci.yml` never reports, so every pull request would have waited for good.  `RuleCoverage` reads the merged rules from `rules/branches/{branch}`: no deletion, no force pushes, pull requests and a required check cover the convention's ruleset (linear history is its taste, not a protection).  Then Scan says so under already in place, naming the check; the set-up's ruleset step counts as done and its plan line says none is needed; and Check doesn't call the ruleset missing.  Otherwise Scan lists the ruleset as a change, beside a warning when some rules exist, and it requires the check those rules already name (`RuleCoverage.CheckFor`), which the wizard now takes as the required check, else the convention's Build and test.

**The convention folder holds the files directly** (4).  There is no `labels.json` or `settings.json`; labels and settings are tables in `Core`.  Extensionless dot files are stored with a leading underscore (`_gitignore`, `_gitattributes`, `_editorconfig`) so they do not act on the tool's own repository, and `release.yml` and `codeql.yml` are offered only where their options apply.  Placeholders are `{{name}}` with letters only, so GitHub's `${{ }}` expressions pass through untouched.

**The command runner** (4).  Output is collected through an `ActivityBuffer` and flushed to the panel on the UI thread, so the window stays responsive while a command runs and the busy strip names the running command.

**The activity panel masks tokens** (4).  A private repository's details carry a `temp_clone_token`, and the panel printed it in full; its text is copied into chats and issues.  Every line the panel shows now passes through `ActivityRedaction`: a JSON field whose name ends in `token`, or is `client_secret` or `password`, has its value shown as `[hidden]`, and so does anything shaped like a GitHub token (`gho_`, `ghp_`, `ghu_`, `ghs_`, `ghr_`, `github_pat_`).  Only what the panel shows changes; the commands still get the real output.

**Merging** (7).  Review re-reads the pull request before merging and stops if `main` has moved under it; an unknown merge state is a blocker, not a warning.  Submit updates a branch that is behind `main` before opening the pull request.

**Review confirms the repository** (5, 6).  Review's confirmations were Windows Yes/No boxes that named the pull request but not the repository, so someone who doesn't read the detail could approve or merge in the wrong repository without noticing.  Approve, request changes, merge and merge with bypass now open `ConfirmActionWindow`: the repository's owner and name in large type in a coloured banner, with a note under it when the owner isn't the signed-in login or the repository keeps its own conventions; the action in words; and a button that names the repository, such as "Merge into owner/name".  Cancel has the focus and there is no default button, so Enter alone never confirms.  Merge and merge with bypass also need the owner and name typed, matched without regard to case or surrounding spaces, before the button enables (`ReviewConfirm.NeedsTypedName`, `ReviewConfirm.Matches`), as GitHub asks before a repository is deleted.  `RepositorySettings.TypeNameToMerge`, a box in the repository's Settings, turns the typing off; it is on by default, for entries saved before it existed too.

**A repository can turn off merge with bypass** (5, 6).  An owner of an organisation can bypass its repositories' rules.  Where a team's ruleset asks for someone else's review, every pull request of the owner's own is blocked, so Review offered merge with bypass each time; bypassing is routine on a personal repository, so typing the name and a reason becomes habit.  `RepositorySettings.OfferBypass`, a box in the repository's Settings, decides whether Review offers it.  Until it is set by hand it follows Keeps its own conventions (`OffersBypass`): off for a repository that keeps its own, an entry marked before the setting existed included, and on for the rest.  A choice made by hand is stored and kept whatever later happens to the marker; the Settings window counts a change to the box as a choice only when the box itself changed, not when it followed the marker.  With it off, Review shows a line saying the bypass is turned off and where to change it in place of the bypass card (`ReviewRules.BypassTurnedOff`), and the bypass button checks the setting again before it runs.

**Scan recommends Keep it as it is for a repository with its own way of working** (5).  Scan put Apply the convention first for every convertible repository, so the obvious button was the one that changes a team's way of working.  A convertible repository that an organisation owns, or whose rules already cover the convention's ruleset, now has Keep it as it is recommended (`ScanRules.RecommendsKeep`, from the Organisation and RulesCover findings): its choice moves above Apply the convention and becomes the main button, Apply the convention stays as the secondary one, and the next step names the reason (`ScanReportText.KeepReason`).  The copied report and `--scan` say the same.  Marking such a repository also keeps set-up and Check off it and, through the bypass setting above, merge with bypass.

**A marked repository says so** (5).  Repositories tagged a repository marked Keeps its own conventions Not set up, as if it were waiting for the convention, and a scan of it headed its changes "What set-up would change" although set-up refuses it.  Repositories now tags it Own conventions, in soft gold, and the marker wins over a manifest as it does for the board (`OwnConventions.Tag`); other entries show the convention's version or Not set up as before.  For the Keeps its own conventions verdict the changes are headed "What the convention would change", on screen, in each finding's accessible name, in the copied report and in `--scan` (`ScanReportText.GroupTitle`).

**What the role allows** (5).  Review was shown to admins only, and the rest ignored the role: Status's Set board token and the Settings window's bypass box stayed enabled for anyone, and GitHub refused afterwards.  The signed-in user's role is now read as `Role` (`Roles.Parse`, from gh's `viewerPermission`) and decides what is offered (`Roles`): Review, with approve, request changes and merge, for write, maintain and admin, as GitHub allows; merge with bypass and Set board token for admin only.  Read and triage don't see Review.  Review tells write and maintain that the bypass needs admin, and where the rules would otherwise have offered it, a line says why it isn't there.  The Settings window reads the role for the repository it shows, with one `gh repo view`, and greys out Offer merge with bypass below admin, keeping the stored value.  A role that couldn't be read greys out nothing and says so.  `ScreenCatalogue` takes the role in place of an admin flag.

**The window places itself** (5).  WPF's `CenterScreen` centres the window on the monitor under the mouse without checking that it fits, so on a small monitor it overhung onto the next one.  The startup location is now `Manual`: on `SourceInitialized` the window reads that monitor's work area and DPI through Win32, shrinks to the work area where it has to, and centres itself there (`Core\WindowPlacement`, pure, and `Helpers\MonitorHelper`).  Monitors at different scales need the move in two passes, because WPF converts coordinates with the DPI of the monitor the handle was created on until the window has moved, and Windows only switches the DPI once the window lies mostly on the target.

**The Settings window scrolls** (6).  The repository Settings window sized itself to its content, which had grown with the profile's columns and boxes, and on a laptop screen it ran off the bottom with Save and Cancel.  Its content now sits in a scroll area with Save and Cancel pinned below it, and the window is no taller than the work area of the monitor under the mouse, less a margin, and moved to stay inside it (`Core\DialogFit`, with `Helpers\MonitorHelper`).

**Scan ends with a next step** (5).  Each verdict names what to do next, inside the notice and as a button under it: set up the repository, open Check when it is already set up and current, add it to Repositories, or fix the blockers and scan again.  The text report, and so `--scan`, says the same, naming the switches.

**Scan's targets say what they read** (5).  Mycelia was scanned by name while its clone was on the machine, since nothing said that the folder option reads GitHub as well, and the scan by name offered to clone it again.  The folder option is now "A clone or folder on this machine", with a line saying it reads the clone and its repository on GitHub and is the one to use for a clone; the name option is "A repository on GitHub, with no clone here", reading GitHub only.

**The copied report offers the choice too** (5).  Copy report's next step for a convertible repository still said only to run the set-up wizard or `--setup`, though the screen offered keeping it as it is.  The text report now names both: the wizard or `--setup` to take the convention, and Keep it as it is on Scan to keep its own way of working (`Scan_Next_Convertible_Text`).

**The wizard is called Set up a repository** (5).  "New repository" read as if a scanned repository would be created again, so the navigation entry, the wizard's title and the Repositories button are "Set up a repository", and its third page is "Plan", not "Create".  Scan's buttons are "Apply the convention" and "Work on it in Repo Starter", and the convertible next step says nothing changes until Start.  Each button's accessible name is its label, with the longer description as help text.  Sections 4, 5 and 11 keep the old name.

**Screen loads give way** (5).  A reload asked for while other work runs used to be dropped, so a screen opened during another's load never loaded.  It is now held (`Core\HeldReload`) and runs when the work ends, if its screen is still shown and no load that began after the request has finished; `ScreenBase.LastLoadBegan` records when the last finished load began.  The running reload also records its screen, and asking for another screen's reload stops it (`HeldReload.GivesWay`): the activity panel names the screen stopped, and the new one loads at once instead of waiting behind, for example, Status's Actions usage calls.  A user action is never stopped; a held reload waits for it.

**An existing repository keeps its visibility** (5).  The wizard took an existing folder's visibility from its radio buttons, which default to Private, so a public repository's clone was set up without CodeQL.  `SetupAnswers.PrivateOnGitHub` now decides: a repository the scan found on GitHub, from an existing folder or by owner and name, keeps GitHub's visibility, shown with the buttons disabled; a new repository, or a folder with no repository on GitHub yet, keeps the choice.  Set-up never changes an existing repository's visibility.  Scan's Apply the convention passes its `ScanFacts` to the wizard, so the first page arrives with the visibility, description, language and project; Next still scans again.

**The Closes line** (7).  Submit fills the repository's pull request template.  It counts only a "Closes #" that starts a line and is outside HTML comments, the line GitHub reads and Guard checks, and adds an Issue section at the top when there is none; a template that mentions "Closes #12" only inside a comment, as paulfilkin/mycelia's does, no longer gets the number put into the comment.  The template is read from `.github/`, the root or `docs/`, as GitHub does; templates in a `.github/PULL_REQUEST_TEMPLATE/` folder are not used, since GitHub applies those only when the link names one.

**The app puts pull requests on the board too** (3.3, 7).  Without a board token or Auto-add nothing added pull requests to the board, so its In review column stayed empty and a bot's pull requests, such as Dependabot's, could only be found on Review.  Submit now adds the pull request it opens to the board with the signed-in account, as Start work does for the issues it creates, noting rather than failing when it can't.  Status's board action is now Add missing issues and pull requests (`GitHubService.AddMissingToBoardAsync`): it adds every open issue and pull request that isn't on the board, matched by URL rather than number (`BoardMissing`), since an issue and a pull request can share a number and a board can hold other repositories' items.

**The app is called Designated Driver** (6).  The tool had grown from setting repositories up into running the whole workflow, from issue to release, and "Repo Starter" no longer said that.  The window, the About window and every string say Designated Driver; the project, exe, assembly and namespace are `DesignatedDriver`, in a `DesignatedDriver\` folder; temporary files are named `designated-driver-*`; the release zip is `designated-driver-vX.Y.Z.zip`.  The icon is a DD mark drawn by `Tools\New-Icon.ps1`: a navy rounded square with a silver and a gold D linked like a chain, flatter at 24 px and below.  Settings live in `%AppData%\DesignatedDriver`; on first start, when only `%AppData%\RepoStarter` has a settings file, its files are copied across and the old folder is left as it is (`Settings.CarryOver`).  The board token comes from `DESIGNATED_DRIVER_BOARD_TOKEN`, with `REPO_STARTER_BOARD_TOKEN` read when it is not set.  The old repository is kept private as paulfilkin/repo-starter-archive, with its history, issues, pull requests and releases, because its self-test samples had held another organisation's data in every commit.  paulfilkin/designated-driver is a new public repository, started from the cleaned tree as one commit and set up by the app's own `--setup`.  What the tool writes into repositories (the manifest's name, the hooks' variable, the set-up branch, and the text in the convention files, issues and pull requests that names the tool) kept the old name until issue 8 (67 in the archive), below.  The paragraphs above and the releases table keep the old name.

**GitHub's nulls read as empty** (4).  GitHub returns `"description": null` for a repository made without one, and the null replaced the models' empty defaults, so the set-up's settings check crashed on it.  `GhJson`'s options read a JSON null into any string property as an empty string, for every gh model.

**A renamed repository is a blocker** (5).  GitHub answers a renamed or transferred repository's old name with the repository it now is, until a new repository takes the name; the first set-up of this repository followed that redirect to the archive.  The scan compares the full name GitHub returns with the one it asked for (`GhRepositoryDetails.IsNamed`); a difference is a blocker naming where the name leads, and nothing more is read from that repository.  The set-up's first step stops on it before creating or cloning anything, and the wizard says so instead of "already exists".

**GitHub's default labels are renamed** (3.2).  A repository with `enhancement` and `documentation` and without `feature` and `docs` has them renamed with `gh label edit --name`, keeping their colour and description in line with the convention, so labelled issues keep the label (`ConventionLabels.Plan`).  When both names exist, both are left alone and Scan says so.  Scan also names an issue form in the clone that applies a label to be renamed (`IssueForms.Labels`).

**A repository's own issue forms are kept** (3.1).  A repository with issue forms of its own (any `.yml` or `.yaml` in `.github/ISSUE_TEMPLATE` other than `config.yml` and the convention's two) keeps them, and set-up doesn't add `bug.yml` and `feature.yml` beside them; `config.yml` is still written.  Scan lists the kept forms, and Check treats the convention's forms as tailored: reported unticked when they differ or are missing.

**New issue follows the repository's own forms** (5).  New issue's fields were built in, mirroring the convention's `bug.yml` and `feature.yml`, so a repository with forms of its own got issues without their dropdowns and checkboxes.  Start work now reads every form in `.github/ISSUE_TEMPLATE` on GitHub's default branch, where GitHub's own New issue page reads them, whatever the file is called (`IssueFormsAsync`, `IssueForms.Read`).  It offers one choice per form under the form's name, in file name order, plus Chore and Docs where no form carries their label.  Each field gets the control for its type: a box for textarea and input, a list for a dropdown, tick boxes for a dropdown that allows several choices and for checkboxes, and help text for markdown.  The form's title prefix fills the title box.  The body is written as GitHub writes it: every field's heading in order, `_No response_` for an empty optional field, a dropdown's choices joined with ", ", a `- [X]` or `- [ ]` line per checkbox option, and a textarea with `render` fenced as that language.  Only the form's labels the repository has are passed to `gh issue create`, which would fail on a missing one where GitHub skips it, and the activity panel names the rest.  A repository with no forms, or whose forms can't be read, gets the built-in Bug, Feature, Chore and Docs, and the card says which.  The forms are read with a small YAML reader for the block YAML that issue forms use, so no package is added.

**The release workflow is left off when releases come another way** (4.1).  Scan reads the repository's releases and each workflow's `on:` triggers (`WorkflowTriggers`).  Releases with files attached, or a workflow other than the convention's run on tags or releases, mean the repository already publishes releases (`ReleaseEvidence`): Scan says so with the latest tag and its files, and the wizard starts with Release unticked and the reason under it.  It can still be ticked.

**Another workflow that builds pull requests** (4.1, 7).  A workflow other than the convention's that runs on pull requests, or on pushes to the default branch, would build every change beside `ci.yml`.  Scan warns about each one (`CompetingWorkflows`), and the wizard's Options page offers, unticked, to remove it; a ticked one is removed with `git rm` in the set-up commit and listed in the set-up pull request (`SetupAnswers.RemoveWorkflows`).

**The workflow section in a kept CONTRIBUTING.md** (3.1).  The convention's section on how a change gets in is its own text, `fragments/workflow-section.md`, used as section 2 of its CONTRIBUTING.md.  Set-up adds it at the end of a kept CONTRIBUTING.md that lacks the heading, in the set-up pull request, leaving the rest of the file alone (`WorkflowSection`).  Scan reports the addition, and Check lists a CONTRIBUTING.md without it as an unticked item that adds it.

**What the convention writes is renamed too** (3.1, 3.7, 7).  New set-ups write the manifest as `.github/designated-driver.json`.  A repository set up before the rename has `.github/repo-starter.json`; Scan, Check and the set-up read it when the new file is absent, from a clone or from GitHub, and Check lists a ticked item that writes the new file and removes the old one with `git rm` in the usual pull request (`Manifest.OnlyAtOldPath`, `ScanFacts.ManifestAtOldPath`).  The `pre-push` hook lets a push through with `DESIGNATED_DRIVER_ALLOW_MAIN` and still with `REPO_STARTER_ALLOW_MAIN`.  The convention files' comments, the CONTRIBUTING and Git guide templates, and the set-up and update issues and pull requests name Designated Driver.  The set-up branch is `chore/<n>-designated-driver-v1`; resuming a set-up also finds one with the old suffix.  A repository set up before the rename sees the changed lines as drift in Check, and Apply brings them up to date.

**Clone and work on it as it is** (5).  Working on an existing repository used to mean applying the convention, or cloning by hand and typing every field into Add.  Repositories now has **Clone a repository**: an owner/name and a parent folder, and `Workspace.CloneAndAddAsync` reads the repository from GitHub, clones it with the protocol and SSH host from Settings (the alias found for the signed-in login, since the owner may be an organisation), and adds it as the current repository with GitHub's spelling of the name and its default branch.  A clone with a manifest brings its self-test command and required check, and has `core.hooksPath` pointed at its hooks folder; that is local configuration.  Nothing else runs: no issue, branch, label or setting changes on GitHub.  A name without an owner, a repository already in the list with a folder, one GitHub does not know or answers for under another name, and a folder that already has files in it are refused before the clone.  Settings opens for the new entry afterwards.  Scan shows **Clone and work on it as it is** after a scan by name of a repository with no clone that is not in the list (`ScanRules.CanCloneAsIs`), and the convertible next step says so; it opens the card with the name filled in.

**A repository's own settings are read into its profile** (5, 6).  A repository without a manifest kept only what was typed into Settings, and the defaults (Build and test, no board, no self-test command) were wrong for most.  `RepositoryProfile.From(ScanFacts)` now reads what it already uses from a scan: the default branch; the required check, the first context of a `required_status_checks` rule from `rules/branches/{branch}` (`GhBranchRule.Parameters`), else from classic branch protection's `required_status_checks`, read only when no ruleset names one; the first linked board with a pick-up and an in-progress column; and the self-test command, the first test command (`--self-test`, `dotnet test`, `npm test`, `pytest` and the like) in the job named for the required check, else in a workflow that builds pull requests or the default branch (`WorkflowJobs`, `CiCommands`).  A line using a workflow expression is skipped and `--no-build` and `--no-restore` are dropped, since locally nothing is built first.  The scan keeps each workflow's text for this (`ScanFacts.WorkflowTexts`).  Columns are matched by name without regard to case (`BoardColumns`): Ready ahead of Todo or To do, since a board with both uses Todo as a backlog, then In progress and In review.  `RepositorySettings` gains `ReadyColumn`, `InProgressColumn` and `InReviewColumn`; empty means the convention's names, so existing entries behave as before.  Start work lists the ready column and moves a card to the in-progress one; Submit moves the issue's card to the in-review column once the pull request is open, noting rather than failing when it can't.  Settings has the three columns and **Read from GitHub**, shown only without a manifest.  A clone being added with Add a clone, a clone made by Clone a repository, and a scanned clone added from Scan open Settings with the read already done; nothing is kept until Save.  A later read fills empty boxes and lists any value that differs from GitHub's, replacing them only on Yes (`ProfileComparison`).  Only gh reads run.

**A repository can keep its own conventions** (5).  Without a manifest a repository was always one to convert, and Apply the convention would recolour its labels, add a ruleset beside its own, and write Guard, hooks and a board workflow it doesn't use.  `RepositorySettings.KeepsOwnConventions`, a box in Settings, says it keeps its own.  The scan takes it from the repository's entry (`ScanFacts.KeepsOwnConventions`) and the verdict is `KeepsOwnConventions`, ahead of any other, with the findings still listed for information and no Apply the convention.  `--scan` reads no settings, so it never sees it.  The wizard's Next and `--setup` (exit 2) refuse such a repository, saying how to change it (`OwnConventions.SetupRefusal`); Check and `--check` (exit 0) show a notice and run nothing (`OwnConventions.CheckNotice`); Status says so and hides its link to Check.  The workflow screens already used the entry's required check, self-test command and columns; the board now comes from the entry alone, ignoring a manifest's (`OwnConventions.BoardFor`).  The commit message and branch name checks still apply.

**Scan's next step is a choice** (5).  Scanning a repository with its own releases, build workflow and templates, the notice said to bring it into the convention while a second button, Work on it in Designated Driver, sat beside it with its meaning in screen reader help only, and taking it didn't mark the repository as keeping its own conventions.  A convertible repository's notice now asks whether it takes the convention or keeps its own way of working (`ScanReportText.NextStep`), and every button under it has its line beside it on screen.  **Keep it as it is** takes one of three routes (`ScanRules.AsItIs`): a clone not in the list is added, a repository found by name with no clone opens the clone card, and an entry already in the list is marked without becoming current.  Each marks the entry as keeping its own conventions (`ScanRules.KeepsOwnAsItIs`), except a clone that turns out to have a manifest; Settings opens showing the box, and after an add or a mark Scan scans again, so the verdict reads Keeps its own conventions.  Other verdicts' buttons read **Add to Repositories** and **Clone it** and mark nothing, and nor does Repositories' own Clone a repository (`Workspace.CloneAndAddAsync`'s `keepsOwnConventions`).

**A scan by name finds a clone beside the known ones** (5).  Mycelia's clone sat in the same folder as this repository's, but a scan by name knew nothing of it and offered to clone it again.  A scan by owner and name of a repository not in the list now looks in the folders that hold the known clones, one level deep, reading each subfolder's `.git/config` for an origin on GitHub that names the repository, without regard to case (`NearbyClones`); no command runs, and the activity panel notes a clone found.  One found is a finding worth knowing that names the folder (`ScanFacts.NearbyClone`), Clone isn't offered, and the next step is **Scan the clone**, which scans that folder.  With none found and cloning offered, the next step adds that a clone kept elsewhere can be scanned by its folder (`ScanReportText.NextStepForName`).

**Scan names a board's real columns** (5).  A usable linked board was reported as having Todo, In Progress and Done whatever its columns were called, since Ready stands for Todo and names are matched without regard to case.  The line now lists the board's own columns, in its order and spelling, and names the ones the workflow screens use to pick up work and for work under way (`BoardColumns.Match`, `TextList.And`).  The set-up's board step says the same when it finds such a board linked.

**The board must be fed** (3.3, 5).  paulfilkin/mycelia was set up without a board token, so `board.yml` skipped itself and its pull requests never reached the board; a token stored for Actions would still have missed Dependabot's, because a run Dependabot starts reads only Dependabot's secrets.  The board's own Auto-add to project workflow needs no token, so it is the fix offered first, with the filter `is:issue,pr is:open`; GitHub's API reads a board's workflows but can't turn one on, so the app links to the board's Workflows page.  A board is fed when Auto-add is on or `PROJECT_TOKEN` is stored for Actions and for Dependabot (`BoardFeed.Assess`).  Status's Automation card says which, Scan warns when neither, Check notes it unticked, and set-up's last page says so with the link.  The token is now stored in both places, and `board.yml` writes a warning on the run when it has none, instead of passing silently.

**Picks give way** (5).  Picking a pull request on Review only loads its files, but it ran as a user action, so a second pick within a second was refused with "Something is still running" after the busy strip had cleared.  Work is now a reload, a pick or an action (`BusyRules`): a pick or an action stops a running reload or pick and starts once it has stopped, so the latest pick is the one shown; nothing stops an action, and a pick while one runs is still refused.  Only the latest of several waiting requests starts.

**The busy strip keeps its space** (5).  The strip sat in an auto-height row and was collapsed when idle, so every start of work pushed the page down by its height and every end pulled it back.  On Review that moved the list under a held click: clicking a pull request started loading its files, the strip appeared, and the list selected the row that slid under the pointer, whose pick then stopped the first.  The strip is now hidden rather than collapsed when idle, so its row keeps its height and nothing below it moves.

**Start work shows the picked issue** (5).  Start work's list showed only titles, so a branch could be started for an issue without reading what it asks for.  Picking an issue now shows a card under the list, as Review shows a picked pull request: its number and title, its labels, who opened it and when, and its body, with Open on GitHub.  The list already reads each issue's body, and now its author and creation date too, so the pick runs no command.  The body is laid out by `IssueBodyLayout`, a simple reading of GitHub Markdown, not all of it: headings (the `###` sections issue forms write), paragraphs with their line breaks, bullets and numbered items with their nesting, task lines with their boxes, fenced code, and `_No response_` greyed; HTML comments are left out, and inline only code, bold and links (shown as their text) are read.  A long body scrolls inside the card.

### 12.3 Releases

| Version | Date | What it added |
|---|---|---|
| v0.1.0 | 20 November 2026 | The four phases: set-up, Check, the workflow screens, Status's Dependabot and Actions card, the release workflow, the getting started guide and the icon. |
| v0.1.1 | 20 November 2026 | Check ignores action version pins; the templates' actions bumped. |
| v0.2.0 | 20 November 2026 | Publishing with the `SelfContained` property; the responsive window; a New issue card on Start work that writes from the issue forms' fields; the Repositories filter by text and by GitHub stars; the merge safeguards. |
| v0.2.1 | 21 November 2026 | Guard accepts a merge commit before GitHub has linked it to its pull request; the release build takes its version from the tag; the README filled and this section added. |
| v0.2.2 | 21 November 2026 | The window opens wholly on the monitor under the mouse. |
| v0.2.3 | 21 November 2026 | Scan ends with a next step and a button for it. |
| v0.2.4 | 21 November 2026 | A screen opened while another is loading loads once that work ends. |
| v0.2.5 | 21 November 2026 | The wizard is called Set up a repository and Scan's buttons say what they do; screen readers hear Scan's options and buttons by their text; copyright is multifarious. |
| v0.2.6 | 21 November 2026 | Set-up takes an existing folder's visibility from GitHub, and Scan hands what it found to the wizard; leaving a screen stops its load. |
| v0.3.0 | 22 November 2026 | The app is Designated Driver, with the DD icon, and carries its settings over from Repo Starter; Submit fills only a Closes line outside comments, and reads the template from the root and `docs/` too; the self-test samples hold made-up data.  The first release from paulfilkin/designated-driver; v0.1.0 to v0.2.6 are in the private archive. |
| v0.3.1 | 22 November 2026 | GitHub's nulls read as empty; a renamed repository is a scan blocker; default labels renamed; a repository's own issue forms kept; the release workflow left off when releases come another way; another workflow that builds pull requests warned about and removable; the workflow section added to a kept CONTRIBUTING.md; picks on Review give way; the README for the public repository, with the MIT licence. |
| v0.4.0 | 22 November 2026 | What the convention writes names Designated Driver, and Check moves an old manifest to its new name; Clone a repository, to work on one as it is; a repository's profile read from GitHub (required check, board and its columns, self-test command), with Submit moving the card to In review; Keeps its own conventions; Scan names a board's real columns; the busy strip edged in gold. |
| v0.4.1 | 22 November 2026 | Scan's next step a choice between the convention and Keep it as it is, which marks the repository as keeping its own conventions, with a visible line beside every button; Scan's targets say what they read; a scan by name finds a clone beside the known ones and offers Scan the clone. |
| v0.4.2 | 22 November 2026 | The busy strip keeps its space, so the page no longer moves when work starts and a held click on Review stays on the pull request clicked; Status skips the plan name and billing without the user scope, doesn't count a public repository's Actions minutes, and cuts the runs list to the ids. |
| v0.4.3 | 22 November 2026 | Review asks in a window that names the repository in large type, with the name typed to merge unless the repository's Settings turn that off; Status, Scan, Check and set-up say whether anything puts new issues and pull requests on the board, offering Auto-add, and the board token is stored for Dependabot too; Submit and Status add pull requests to the board; rules already in force stand for the convention's ruleset; an organisation's repository isn't judged by a personal plan; Keep it as it is in the copied scan report; tokens masked in the activity panel; the repository Settings window scrolls. |
| v0.4.4 | 23 November 2026 | Offer merge with bypass on Review, a per-repository setting that is off by default for a repository that keeps its own conventions and kept once chosen by hand; Scan recommends Keep it as it is, with the reason, for a repository an organisation owns or whose rules already cover the convention's ruleset. |
| v0.4.5 | 23 November 2026 | What the signed-in role can't do is hidden or greyed out with the reason: Review needs write access, and merge with bypass and Set board token need admin; a repository that keeps its own conventions is tagged Own conventions, and its scan heads its changes with what the convention would change; the strapline reads "Vibe code all you like: every change still gets home safely.", with a new README screenshot. |
| v0.4.6 | 24 November 2026 | Start work shows the picked issue in a card under the list: its title, labels, who opened it and when, and its description laid out from Markdown, with a link to GitHub; New issue builds its choices and fields from the repository's own issue forms on GitHub, with a list, tick boxes or help text where the form has a dropdown, checkboxes or markdown, and writes the body as GitHub does, `_No response_` included, with the built-in choices kept for a repository with no forms. |

## Sources

- [About GitHub's plans](https://docs.github.com/en/get-started/learning-about-github/githubs-plans): Free versus Pro feature lists, Actions allowance.
- [Available rules for rulesets](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-rulesets/available-rules-for-rulesets): rulesets on public repositories with Free, private with Pro and above.
- [Adding items automatically](https://docs.github.com/en/issues/planning-and-tracking-with-projects/automating-your-project/adding-items-automatically): one auto-add workflow per project on Free.
- [Using the built-in automations](https://docs.github.com/en/issues/planning-and-tracking-with-projects/automating-your-project/using-the-built-in-automations): the two workflows on by default.
- `gh` 2.103.3 help for `repo create`, `repo edit`, `label clone`, `project copy`, `project link`, `ruleset`.
