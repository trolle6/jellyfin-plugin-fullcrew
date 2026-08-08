# Full Crew

**Complete TMDB cast & crew on Jellyfin Web — plus Library Stats, studio profiles, and nostalgia break bumpers.**

[![Version](https://img.shields.io/badge/version-1.5.3.0-00a4dc)](meta.json)
[![Jellyfin](https://img.shields.io/badge/Jellyfin-10.11%2B-00a4dc?logo=jellyfin&logoColor=white)](https://jellyfin.org)
[![.NET](https://img.shields.io/badge/.NET-9.0-512BD4)](Jellyfin.Plugin.FullCrew/Jellyfin.Plugin.FullCrew.csproj)
[![Repo](https://img.shields.io/badge/github-trolle6%2Fjellyfin--plugin--fullcrew-181717?logo=github)](https://github.com/trolle6/jellyfin-plugin-fullcrew)

Jellyfin’s built-in TMDb importer keeps a thin slice of people. Full Crew loads live TMDB credits for the item you already identified, groups them by department, and renders a polished accordion on **Movie / Series / Season / Episode** detail pages — not on Person, Genre, Studio, or BoxSet pages.

---

## Screenshots

Screenshots are not in the repo yet. Suggested paths once you capture them:

| Path | Subject |
| --- | --- |
| `docs/screenshots/cast-accordion.png` | Department accordion on a movie or series |
| `docs/screenshots/library-stats.png` | Library Stats overview (`#/fullcrew/stats`) |
| `docs/screenshots/studio-page.png` | Studio profile (`#/fullcrew/studio/...`) |
| `docs/screenshots/bumper-button.png` | Bumper / Trailer detail buttons |

---

## Features

- **Full cast & crew accordion** — Cast, Directing, Writing, Production, Camera, Editing, Sound, Art, Costume & Make-Up, Visual Effects, Lighting, Crew, and Other, with roles stacked per person
- **Media-only injection** — mounts on Movie, Series, Season, and Episode; deliberately skipped on Person and other entity detail shells
- **Library Stats** — Home header tab next to Favourites → `#/fullcrew/stats` with charts, auto insights, and per-category drill-downs (`#/fullcrew/stats/{category}`)
- **Studio pages** — `#/fullcrew/studio/...` profile layout: TMDB company metadata, library titles, studio-scoped stats, name-root clusters from Stats
- **Break bumpers** — detail-page Bumper button; prefers a local “Bumpers” collection/folder, then curated/search YouTube when enabled
- **Trailer companion** — adds a Trailer button when Jellyfin’s native trailer control is missing; uses local/remote trailer metadata first, then YouTube lookup
- **Self-hosted injection** — registers with File Transformation and JavaScript Injector when present, patches `index.html` when writable, and ships an **early-boot** snippet so `#/fullcrew/*` routes don’t flash Jellyfin’s “page not found” chrome
- **Configurable** — optional personal TMDB key, cache TTL, per-department toggles, bumper/YouTube/stats switches

Credits are fetched on demand and **not** written into Jellyfin’s people library.

---

## Requirements

- **Jellyfin 10.11+** (plugin targets `net9.0` / ABI `10.11.0.0`)
- Items identified with **TheMovieDb** provider IDs
- **Jellyfin Web** (browser). Native TV / mobile apps do not load the injected UI.

No personal TMDB API key is required. By default the plugin uses the same shared key as Jellyfin’s built-in TheMovieDb provider. You can set your own key in plugin settings for separate rate limits.

---

## Install

### Catalog (recommended)

1. Dashboard → Plugins → Repositories → add:

```text
https://raw.githubusercontent.com/trolle6/jellyfin-plugin-fullcrew/master/manifest.json
```

2. Catalog → **Full Crew** → Install → restart Jellyfin.
3. Hard-refresh the web client (`Ctrl+Shift+R` / `Cmd+Shift+R`).

The plugin tries to inject itself automatically (File Transformation → JavaScript Injector → `index.html` patch). Prefer **File Transformation** on Docker hosts where the web root is read-only.

If the UI still never appears, add a one-shot injector script:

```js
(function () {
  var s = document.createElement('script');
  s.src = '/FullCrew/fullcrew.js';
  document.head.appendChild(s);
})();
```

Or insert before `</body>` in `index.html`:

```html
<script plugin="FullCrew" src="/FullCrew/fullcrew.js"></script>
```

(Current releases also inject a synchronous **FullCrew-early** boot script ahead of the deferred main bundle for Stats/studio routes.)

### Manual

1. Build (see [Building from source](#building-from-source)).
2. Copy `Jellyfin.Plugin.FullCrew.dll` (and, if present, the extracted `Web/` assets next to it) into your Jellyfin plugins folder, e.g. `plugins/Jellyfin.Plugin.FullCrew/`.
3. Restart Jellyfin and hard-refresh the web client.

---

## Configuration

Dashboard → Plugins → **Full Crew**

| Setting | Default | What it does |
| --- | --- | --- |
| TMDB API Key | *(empty)* | Optional; blank uses Jellyfin’s shared TMDB key |
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

Open a movie, series, season, or episode in Jellyfin Web. Below the usual detail content you’ll get a **Full Cast & Crew** accordion. Expand a department to browse people (avatars when TMDB provides them; TMDB person links when IDs are present).

| Item | TMDB source |
| --- | --- |
| Movie | `/movie/{id}/credits` |
| Series / Episode | `/tv/{id}/aggregate_credits` (episodes resolve via the parent series TMDB id when needed) |
| Season | `/tv/{id}/season/{n}/credits`, falling back to series aggregate credits |

### Library Stats

With Library Stats enabled, a **Stats** tab appears in the Home header next to Favourites.

- Overview: `#/fullcrew/stats` — counts, insights, genres, studios, people-by-role, ratings, decades, tags, languages, collections, resolution / HDR / codecs / audio
- Detail: `#/fullcrew/stats/{category}` — full ranked list for one category (e.g. `actors`, `genres`, `hdr`)
- Rows that map to library entities navigate into Jellyfin (or Full Crew studio pages for studios)

### Studio pages

From Stats (or a direct hash), open `#/fullcrew/studio/{name}` for a person-style profile: logo/overview from TMDB when available, titles in your library, studio-scoped breakdowns, and optional “missing popular” titles not in the library. Name-root clusters from Stats can expand into exact studio credit branches.

### Bumpers & trailers

On media detail pages:

- **Bumper** — short nostalgia clip keyed to the show/movie (local library first, then YouTube if allowed)
- **Trailer** — only when Jellyfin’s native trailer button isn’t visible; plays native/remote trailers when present, otherwise YouTube lookup/search

---

## Building from source

```bash
dotnet build Jellyfin.Plugin.FullCrew/Jellyfin.Plugin.FullCrew.csproj -c Release
```

Output DLL:

```text
Jellyfin.Plugin.FullCrew/bin/Release/net9.0/Jellyfin.Plugin.FullCrew.dll
```

Client assets (`fullcrew.js` / `fullcrew.css`) are embedded and extracted beside the DLL on startup. Asset URLs are version-queried (`?v=…`) for cache busting after upgrades.

### Release packaging

Push a version tag to run the GitHub Actions release workflow:

```bash
git tag v1.5.3.0
git push origin v1.5.3.0
```

---

## Privacy & network

Full Crew is self-hosted, but some features call out of your server:

| Destination | When |
| --- | --- |
| **api.themoviedb.org** / **image.tmdb.org** | Credits, studio company metadata, profile/logo images |
| **YouTube** | Break bumpers and trailer fallback when YouTube lookups are enabled |

Disable **Allow YouTube bumpers** to stop outbound YouTube requests for bumpers and trailers. Library Stats aggregates your local library only (no TMDB round-trip for the overview charts).

---

## API (for integrators)

Authenticated:

| Method | Path | Purpose |
| --- | --- | --- |
| `GET` | `/FullCrew/{itemId}` | Categorized cast/crew |
| `GET` | `/FullCrew/{itemId}/bumper` | Resolve break bumper |
| `GET` | `/FullCrew/{itemId}/trailer` | Resolve trailer companion |
| `GET` | `/FullCrew/stats` | Library stats overview |
| `GET` | `/FullCrew/stats/{category}` | Full ranked category list |
| `GET` | `/FullCrew/studio?name=…` | Studio page (query form preferred by the client) |
| `GET` | `/FullCrew/studio/{name}` | Studio page (path form) |
| `GET` | `/FullCrew/studio/item/{itemId}` | Studio page by Jellyfin studio id |

Public assets:

| Method | Path |
| --- | --- |
| `GET` | `/FullCrew/fullcrew.js` |
| `GET` | `/FullCrew/fullcrew.css` |

---

## Contributing

Issues and pull requests are welcome at [trolle6/jellyfin-plugin-fullcrew](https://github.com/trolle6/jellyfin-plugin-fullcrew). Keep changes concrete and test against Jellyfin Web 10.11+ — especially Stats/studio routing (early-boot anti-blink) and Person-page gating.

## License

No license file is published in this repository yet. Check the [GitHub repo](https://github.com/trolle6/jellyfin-plugin-fullcrew) for updates before redistributing.
