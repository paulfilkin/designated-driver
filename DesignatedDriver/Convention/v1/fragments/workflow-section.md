How a change gets into {{defaultBranch}}

Nobody pushes to `{{defaultBranch}}`.  Every change arrives through a pull request, passes the automatic checks and is approved.  On a public repository GitHub enforces this; on a private one the local hooks in `.githooks\` refuse the push, and the Guard workflow reports anything that slips through.

Every change starts from an issue.  Open one with the **Feature** or **Bug** form on the repository's Issues tab.  The form adds the type label (`feature`, `bug`); add `docs` or `chore` where they fit, and `blocked` while an issue is waiting on something else.  An issue is ready to work on once it has acceptance criteria and isn't labelled `blocked`.

The [project board]({{boardUrl}}) shows every issue in one of GitHub's three columns: Todo, In Progress and Done.  The columns are left as GitHub made them, so its built-in automations do the moving.

- A new issue is added to the board automatically, in Todo.  One that is not ready to pick up is labelled `blocked`.
- Opening a pull request with `Closes #42` links it to the issue.
- You move it to In Progress when you assign it to yourself; Designated Driver's Start work does both.
- Merging the pull request, or closing the issue, moves it to Done.

1. Pick a ready issue and assign it to yourself, so nobody else starts the same work.
2. Start from the latest `{{defaultBranch}}`: `git switch {{defaultBranch}}`, then `git pull`.
3. Create a branch named for the issue: `feature/`, `fix/`, `docs/` or `chore/` followed by the issue number and a short name, for example `feature/42-feedback-export`.
4. Make the change in small commits.  Push the branch whenever you stop work, so nothing lives only on your machine.
5. Open a pull request into `{{defaultBranch}}` and fill in the template, with `Closes #42` (the issue number) in the description.  The merge then closes the issue and links the two.  Keep a pull request to one issue; several small ones are reviewed faster than one large one.
6. The automatic checks run.  Fix anything they report.
7. The reviewer reviews.  Answer every comment, either with a change or a reply, and resolve the conversation.
8. The pull request is merged with **Squash and merge**, which makes it one commit on `{{defaultBranch}}`.  The branch is then deleted and the issue closed.

`Docs\Working with Git.md` gives the commands for each of these steps.
