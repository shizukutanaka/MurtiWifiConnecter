# Pending CI workflows

These workflow definitions are staged here because the automation token used
to push this branch lacks the GitHub `workflows` permission, so files cannot be
written under `.github/workflows/` directly (the API returns
`403 Resource not accessible by integration`).

**A maintainer with `workflows` write access must move these into place:**

```sh
mkdir -p .github/workflows
git mv docs/ci/ci.yml      .github/workflows/ci.yml
git mv docs/ci/codeql.yml  .github/workflows/codeql.yml
git commit -m "ci: activate CI and CodeQL workflows"
git push
```

## Contents

| File          | Purpose                                                        |
|---------------|---------------------------------------------------------------|
| `ci.yml`      | Windows full build + test; Ubuntu `MWC.Core` build (no tests) |
| `codeql.yml`  | Weekly C# SAST (CodeQL), manual build mode on `windows-latest` |

Both rely on the solution filters committed at the repo root:
`MWC.ci-win.slnf` (Windows-buildable projects incl. tests) and
`MWC.ci-linux.slnf` (Core + cross-platform projects, no WPF/tests).
