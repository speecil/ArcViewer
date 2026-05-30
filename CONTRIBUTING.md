# Contributing

Watch is split into a core [AllPoland/ArcViewer](https://github.com/AllPoland/ArcViewer/) style branch and the ScoreSaber branch

For local setup, start with [SETUP.md](SETUP.md)

## Branches

- `core/scoresaber-replays` is for replay support that could reasonably be PR'd upstream
- `main` is for ScoreSaber branding, hosting, Docker, scripts and deployment

Make core replay changes on `core/scoresaber-replays` first, then bring them into `main`

## C# Style

- PascalCase for C# filenames and types
- Follow `.editorconfig`
- Keep ScoreSaber URLs behind `ApiConfig` and runtime env where possible
- Keep existing replay loading working unless you're intentionally changing that behavior

## Builds

Use editor compilation for normal C# work. For WebGL, deployment or player only behavior:

```sh
./arc.cmd build webgl release
```

## Commits

Our commit style is `{feature}: {change_summary} (#{issue_number})` <sub>(sometimes maintainers are naughty and bypass the need for an issue number, do not be like the maintainers)</sub>

Example:

```text
replays: fix arc scoring event matching
webgl: write scoresaber runtime defaults
```
