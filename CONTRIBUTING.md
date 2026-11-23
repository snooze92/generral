# Contributing

Thanks for your interest! Bug reports, ideas and pull requests are welcome.

- **Bugs:** open an issue with the Generral version (*System → Status*), how you run it (Docker
  image tag, reverse proxy, …), what you did, what you expected and what happened. Logs from
  *System → Log Files* help, with secrets removed (Generral already redacts API keys in its logs).
- **Features:** open an issue first for anything larger than a small fix, so we can agree on the
  behaviour. The [specs](docs/specs/README.md) describe how Generral is meant to work and the
  [roadmap](docs/specs/13-roadmap.md) what is planned.
- **Security problems:** report them privately; see [SECURITY.md](SECURITY.md).
- **Presets / sources:** Generral ships no presets or source content. Please don't submit any that
  point at content you don't have the rights to share.

## Pull requests

See *Development* in the [README](README.md#development) for building and testing. Before pushing:

- `dotnet format Generral.slnx` and `dotnet test Generral.slnx`;
- in `frontend/`: `pnpm typecheck && pnpm lint && pnpm test && pnpm check:translations`;
- `reuse lint` if you have it ([REUSE](https://reuse.software));
- after changing entities, add an EF migration (see the README);
- update the spec in the same change when behaviour changes (the specs are kept in sync with
  the code);
- don't edit the vendored Sonarr files listed in `frontend/scripts/sonarr-files.txt`; see
  [10 – UI § Implementation approach](docs/specs/10-ui.md#implementation-approach).

CI runs all of these checks on every pull request.

Generral is licensed under the GPL-3.0-or-later ([LICENSE](LICENSE)). By contributing you agree that
your contributions are licensed under the same licence.
