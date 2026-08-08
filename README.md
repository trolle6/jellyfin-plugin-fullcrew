# Full Crew

**Complete TMDB cast & crew on Jellyfin Web — Library Stats, studio profiles, break bumpers, and a local scene index you grow while watching.**

[![Version](https://img.shields.io/badge/version-1.7.0.0-00a4dc)](meta.json)
[![Jellyfin](https://img.shields.io/badge/Jellyfin-10.11%2B-00a4dc?logo=jellyfin&logoColor=white)](https://jellyfin.org)
[![.NET](https://img.shields.io/badge/.NET-9.0-512BD4)](Jellyfin.Plugin.FullCrew/Jellyfin.Plugin.FullCrew.csproj)
[![Repo](https://img.shields.io/badge/github-trolle6%2Fjellyfin--plugin--fullcrew-181717?logo=github)](https://github.com/trolle6/jellyfin-plugin-fullcrew)

Jellyfin’s built-in TMDb importer keeps a thin slice of people. Full Crew loads live TMDB credits for the item you already identified, groups them by department, and renders a polished accordion on **Movie / Series / Season / Episode** detail pages — not on Person, Genre, Studio, or BoxSet pages.

During playback, **Y** opens scene info: title cast anytime, plus indexed “this moment” hits you build over time. Optional OpenAI Vision scans are opt-in and saved locally so revisits do not call the API again.

---

## Screenshots

Screenshots are not in the repo yet. Suggested paths once you capture them:

| Path | Subject |
| --- | --- |
| `docs/screenshots/cast-accordion.png` | Department accordion on a movie or series |
| `docs/screenshots/library-stats.png` | Library Stats overview (`#/fullcrew/stats`) |
| `docs/screenshots/studio-page.png` | Studio profile (`#/fullcrew/studio/...`) |
| `docs/screenshots/scene-info.png` | Playback scene info panel (Y) |
| `docs/screenshots/bumper-button.png` | Bumper / Trailer detail buttons |

---

## Features

- **Full cast & crew accordion** — Cast, Directing, Writing, Production, Camera, Editing, Sound, Art, Costume & Make-Up, Visual Effects, Lighting, Crew, and Other, with roles collapsed per person (unique names, “+N more”, tooltip)
- **Media-only injection** — mounts on Movie, Series, Season, and Episode; deliberately skipped on Person and other entity detail shells
- **Library Stats** — Home header tab next to Favourites → `#/fullcrew/stats` with charts, auto insights, per-category drill-downs, and bucket item lists (e.g. every title that is Stereo / HEVC)
- **Studio pages** — `#/fullcrew/studio/...` profile layout: TMDB company metadata, library titles, studio-scoped stats, name-root clusters from Stats
- **Break bumpers** — detail-page Bumper button; prefers a local “Bumpers” collection/folder, then curated/search YouTube when enabled
- **Trailer companion** — adds a Trailer button when Jellyfin’s native trailer control is missing
- **Scene info + local scene index** — press **Y** (or the OSD face button) for title cast and nearby indexed moments; opt-in Vision scans persist in 10s buckets under the plugin data folder
- **Self-hosted injection** — File Transformation / JavaScript Injector / `index.html` patch, plus **early-boot** so `#/fullcrew/*` routes do not flash “page not found”
- **Configurable** — optional TMDB key, OpenAI key for scene identify, cache TTL, department toggles, bumper/YouTube/stats switches

Credits are fetched on demand and **not** written into Jellyfin’s people library.

---

## Requirements

- **Jellyfin 10.11+** (`net9.0` / ABI `10.11.0.0`)
- Items identified with **TheMovieDb** provider IDs
- **Jellyfin Web** (browser). Native TV / mobile apps do not load the injected UI.

No personal TMDB API key is required by default (shared key, same idea as Jellyfin’s TheMovieDb provider). Scene identify needs your own OpenAI key when enabled.

---

## Install

### Catalog (recommended)

1. Dashboard → Plugins → Repositories → add:

```text
https://raw.githubusercontent.com/trolle6/jellyfin-plugin-fullcrew/master/manifest.json
```

2. Catalog → **Full Crew** → Install → restart Jellyfin.
3. Hard-refresh the web client (`Ctrl+Shift+R` / `Cmd+Shift+R`).

Prefer **File Transformation** on Docker hosts where the web root is read-only.

If the UI never appears, inject once:

```js
(function () {
  var s = document.createElement('script');
  s.src = '/FullCrew/fullcrew.js';
  document.head.appendChild(s);
})();
```

Or before `</body>` in `index.html`:

```html
<script plugin="FullCrew" src="/FullCrew/fullcrew.js"></script>
```

### Manual

1. Build (see [Building from source](#building-from-source)).
2. Copy `Jellyfin.Plugin.FullCrew.dll` and `Web/` assets into your Jellyfin plugins folder.
3. **Stop → Start** Jellyfin, then hard-refresh the web client.

---

## Configuration

Dashboard → Plugins → **Full Crew**

| Setting | Default | What it does |
| --- | --- | --- |
| TMDB API Key | *(empty)* | Optional; blank uses Jellyfin’s shared TMDB key |
| Enable scene identify | Off | Opt-in Vision scans when you choose Scan / first visit a moment |
| OpenAI API Key | *(empty)* | Required when scene identify is enabled |
| OpenAI vision model | `gpt-4o-mini` | Vision-capable chat model |
| Cache hours | `12` | In-memory credits cache (1–168) |
| Max people per department | `100` | Cap per accordion section |
| Enable Library Stats | on | Stats tab + `/FullCrew/stats` API |
| Enable bumpers | on | Bumper button on media detail pages |
| Allow YouTube bumpers | on | YouTube curated/search when no local bumper; also gates trailer YouTube lookup |
| Bumpers collection name | `Bumpers` | Preferred local collection/folder for bumpers |
| Departments | all listed | Toggle which accordion groups appear |

---

## Usage

### Cast & crew

Open a movie, series, season, or episode in Jellyfin Web. Below the usual detail content you’ll get a **Full Cast & Crew** accordion.

| Item | TMDB source |
| --- | --- |
| Movie | `/movie/{id}/credits` |
| Series / Episode | `/tv/{id}/aggregate_credits` |
| Season | `/tv/{id}/season/{n}/credits`, falling back to series aggregate |

### Library Stats

- Overview: `#/fullcrew/stats`
- Detail: `#/fullcrew/stats/{category}`
- Bucket titles: `#/fullcrew/stats/{category}/items/{bucket}` (every contributing Movie/Series)

### Studio pages

`#/fullcrew/studio/{name}` — person-style profile, library grid, optional missing popular titles.

### Scene info (playback)

During **Jellyfin Web** playback:

1. Press **Y**, or the face icon on the player OSD
2. Side sheet shows **Title cast** (TMDB, no OpenAI) and **This moment** if indexed
3. With scene identify enabled, unscanned moments can auto-scan once; results are stored in the local **scene index**
4. Revisit the same timestamp (±10s) → index hit, **no new OpenAI call**
5. Use **Rescan this frame** to force a fresh Vision pass

Not Amazon X-Ray. Best on clear close-ups of billed actors.

### Bumpers & trailers

On media detail pages: **Bumper** (local first, then YouTube if allowed) and **Trailer** when Jellyfin’s native control is missing.

---

## Building from source

```bash
dotnet build Jellyfin.Plugin.FullCrew/Jellyfin.Plugin.FullCrew.csproj -c Release
dotnet test -c Release
```

Output DLL:

```text
Jellyfin.Plugin.FullCrew/bin/Release/net9.0/Jellyfin.Plugin.FullCrew.dll
```

Client assets (`fullcrew.js` / `fullcrew.css`) are embedded; URLs are version-queried (`?v=…`) for cache busting.

### Release packaging

```bash
git tag v1.7.0.0
git push origin v1.7.0.0
```

---

## Privacy & network

| Destination | When |
| --- | --- |
| **api.themoviedb.org** / **image.tmdb.org** | Credits, studio metadata, profile/logo images |
| **YouTube** | Bumpers/trailers when YouTube lookups are enabled |
| **api.openai.com** | Scene identify only — when enabled and you scan a frame |

Scene index JSON is stored on your server under the plugin data folder (`scene-index/`). Leave **Enable scene identify** off to never send frames to OpenAI. **Y** still shows title cast and any already-indexed moments.

---

## API (for integrators)

| Method | Path | Purpose |
| --- | --- | --- |
| `GET` | `/FullCrew/{itemId}` | Categorized cast/crew |
| `GET` | `/FullCrew/{itemId}/bumper` | Resolve break bumper |
| `GET` | `/FullCrew/{itemId}/trailer` | Resolve trailer companion |
| `GET` | `/FullCrew/scene-identify/status` | Vision enabled? (no secrets) |
| `GET` | `/FullCrew/{itemId}/playback-scene` | Title cast + nearby scene-index hit |
| `POST` | `/FullCrew/{itemId}/identify-frame` | Vision match; saves to scene index |
| `GET` | `/FullCrew/stats` | Library stats overview |
| `GET` | `/FullCrew/stats/{category}` | Full ranked category list |
| `GET` | `/FullCrew/stats/{category}/items` | Titles in one bucket |
| `GET` | `/FullCrew/studio?name=…` | Studio page (preferred) |
| `GET` | `/FullCrew/studio/{name}` | Studio page (path) |
| `GET` | `/FullCrew/studio/item/{itemId}` | Studio page by Jellyfin studio id |
| `GET` | `/FullCrew/fullcrew.js` | Client script (public) |
| `GET` | `/FullCrew/fullcrew.css` | Client styles (public) |

---

## Contributing

Issues and PRs welcome at [trolle6/jellyfin-plugin-fullcrew](https://github.com/trolle6/jellyfin-plugin-fullcrew). Test against Jellyfin Web 10.11+ — especially Stats/studio routing, Person-page gating, and playback **Y**.

## Built with AI

This project was developed **with substantial help from AI coding assistants** (Cursor / similar tools): design discussion, implementation, refactors, and docs. Human direction, review, and self-hosted testing still steer what ships. If that matters to you as a user or contributor, now you know.

## License

No license file is published in this repository yet. Check the [GitHub repo](https://github.com/trolle6/jellyfin-plugin-fullcrew) before redistributing.
