# designated-driver

Sets up GitHub repositories for a personal account to one convention, keeps them to it, and walks the day-to-day Git workflow.

Designated Driver is a Windows desktop tool.  It runs `git` and `gh` and nothing else, and shows every command it runs.  `Docs\Getting started.md` is the first hour.

## Requirements

- Windows 10 or 11.
- The .NET 10 desktop runtime to run the release, or the .NET 10 SDK to build it.
- Git and the GitHub CLI, signed in with `gh auth login`.
- A GitHub account.  Everything works on the Free plan; public repositories get a few features more, and the tool uses them where they exist.

## Running it

Download the zip from the [Releases](https://github.com/paulfilkin/designated-driver/releases) page, unzip it and run `DesignatedDriver.exe`.  It needs no installing.

From source:

```powershell
dotnet run --project DesignatedDriver\DesignatedDriver.csproj
```

Four switches run without the window.  `Docs\Getting started.md` section 6 has the detail.

| Switch | What it does |
|---|---|
| `--scan <folder or owner/name>` | Reports what setting the repository up would involve |
| `--setup <answers.json>` | Runs the set-up plan from an answers file |
| `--check <folder> [--apply [--all]]` | Reports what has drifted from the convention, and applies it |
| `--self-test` | Runs the built-in checks and prints PASS or FAIL per check |

## Layout

- `DesignatedDriver\`: the WPF project, `DesignatedDriver.csproj`.
- `Docs\`: the design, the getting started guide and the Git guide.
- `.github\`: the convention's own workflows, issue forms, pull request template and manifest, as the tool wrote them.
- `.githooks\`: the local hooks, pointed to by `core.hooksPath`.

Inside the project:

- `Core\`: pure logic with no process or file access: the convention, the manifest, plans, drift, branch names, commit checks, review rules.
- `Convention\`: the convention's files as embedded resources, one folder per version.
- `Models\`: the JSON models for `gh` output.
- `Services\`: the command runner, Git and GitHub services, set-up, scan and check.
- `Screens\`: one user control per screen.
- `SelfTest\`: the checks and the sample `gh` output they run against.
- `Tools\`: `Update-Strings.ps1` regenerates `Strings.Designer.cs`; `New-Icon.ps1` draws the icon.
- `Windows\`: Settings, About, the text viewer and the token prompt.

## Documentation

- `Docs\Getting started.md`: what you need, signing in, the first repository, the screens, the board, the switches.
- `Docs\Designated Driver TDD.md`: the design, and what was built from it.
- `Docs\Working with Git.md`: the commands for each step of a change, for someone new to Git.

## Working on it

How changes get in, and the commands for each step: `CONTRIBUTING.md` and `Docs\Working with Git.md`.
