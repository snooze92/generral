# Copyright

Copyright (C) 2026 The Generral contributors

_Please refer to the git commit log for details on all contributors and their respective
contributions._

This program is free software: you can redistribute it and/or modify it under the terms of the GNU
General Public License as published by the Free Software Foundation, either version 3 of the
License, or (at your option) any later version.

This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without
even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
General Public License for more details.

You can find a copy of the GNU General Public License in the [LICENSE](LICENSE) file and at
<https://www.gnu.org/licenses/>.

## Third-party code included in Generral

Generral reuses code from the projects below. Their copyright notices are retained as the GNU
General Public License requires. Machine-readable, per-file information is in
[`REUSE.toml`](REUSE.toml) and in the `SPDX-FileCopyrightText` headers of ported files.

| Project | Copyright | Licence | What Generral uses | Upstream revision |
|---------|-----------|---------|--------------------|-------------------|
| [Sonarr](https://github.com/Sonarr/Sonarr) | 2010-2026 Sonarr Team and contributors (Mark McDowall, Keivan Beigi, Taloth Saldono and contributors) | GPL-3.0-or-later | Backend: authentication (API key handler, password hashing, local-address bypass, initial user), log secret cleansing, IP address helpers. Frontend: generic UI components, hooks, utilities and themes (360 files vendored by `frontend/scripts/vendor-sonarr.mjs`), and the page shell, settings and status pages adapted from it | `51a2412af235197ce15d7d4a17c053a8e2c26609` |

Bundled fonts (`frontend/public/Content/Fonts`) are Inter, JetBrains Mono and Fraunces from
[Fontsource](https://fontsource.org), under the SIL Open Font License 1.1
([`LICENSES/OFL-1.1.txt`](LICENSES/OFL-1.1.txt)). Noto Sans Bold (© 2022 The Noto Project Authors,
OFL-1.1) is embedded in the backend to draw generated posters, with SkiaSharp (MIT). The licences of the npm packages bundled into the
web UI are listed in `.vite/license.md` inside the build output.

Vendored frontend files are byte-identical to upstream apart from the mechanical transforms listed
in `frontend/scripts/vendor-sonarr.mjs` (CI checks this). Adapted frontend files start with an
`Adapted from Sonarr (GPL-3.0-or-later): <path>` comment.

Ported backend files keep a header naming the upstream file and revision, for example:

```csharp
// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Core/Authentication/UserService.cs
```

### Licence compatibility notes

- **Sonarr** and **Sportarr** state "version 3 of the License, or (at your option) any later version",
  which is compatible with Generral's GPL-3.0-or-later.
- **Prowlarr** only states "GNU GPL v3" (no "or later"). Before porting Prowlarr code (e.g. the
  Cardigann engine planned for the `http-html` source in M1), check the provenance of the specific
  files: code that is GPL-3.0-only would make the combined work GPL-3.0-only. Prefer Sonarr's
  equivalent where one exists, or a clean-room implementation of the YAML selector/filter format.

## Trademarks

Sonarr, Prowlarr, Plex and other names are trademarks of their respective owners. Generral is not
affiliated with or endorsed by them, and does not use their logos as its branding.
