# Contributing

Thank you for helping improve Sound System. Issues are for reproducible bugs and actionable feature requests; use pull requests to propose changes.

## Fork-first workflow

1. Fork [the repository](https://github.com/phillipeaam/unity-sound-system).
2. Create a focused branch from `main` in your fork, for example `fix/sample-import` or `feature/playback-option`.
3. Commit and push the branch to your fork. Contributors cannot create or push branches in the upstream repository.
4. Open a pull request from your fork to the upstream `main` branch and complete the pull request template.
5. Address review feedback and wait for maintainer approval before the change is merged. Upstream `main` is protected and requires an approving review.

## Validation

- Check `package.json` for the minimum supported Unity version and test in that version when possible.
- Run the relevant EditMode and PlayMode tests in a Unity project that includes the package and Unity Test Framework.
- Import and exercise **Basic Audio Setup** when changing the sample, package integration, or serialized assets.
- In the pull request, report the Unity Editor version, platform, checks performed, and any checks you could not run. GitHub Actions tests are not currently configured.

Keep changes focused, preserve Unity `.meta` GUIDs and serialized asset references, and update user documentation or the changelog when public behavior changes. Avoid adding runtime dependencies unless the change requires them.
