# Jellyfin Full Crew

Shows **full cast and crew** on Jellyfin Web movie/series detail pages, grouped by department (Cast, Directing, Writing, Production, Camera, Editing, Sound, Art, Costume & Make-Up, Visual Effects, Lighting, Crew).

Jellyfin’s built-in TMDb importer only keeps a thin slice of people. This plugin loads live TMDB credits (using the item’s existing TMDB id) and renders a categorized accordion in the web UI.

## Install from catalog (recommended)

1. Dashboard → Plugins → Repositories → add:

```text
https://raw.githubusercontent.com/trolle6/jellyfin-plugin-fullcrew/master/manifest.json
```

2. Catalog → find **Full Crew** → Install → restart Jellyfin.
3. Hard-refresh the web client (Ctrl+Shift+R).

You already have **JavaScript Injector** — after install/restart the plugin registers itself with it automatically. If the accordion still doesn’t appear, add this injector script once:

```js
(function () {
  var s = document.createElement('script');
  s.src = '/FullCrew/fullcrew.js';
  document.head.appendChild(s);
})();
```

## Requirements

- Jellyfin **10.11+** (net9.0)
- Items identified with TheMovieDb provider ids
- **Jellyfin Web** (browser). Native TV/mobile apps cannot load the injected UI.

No personal TMDB key is required. By default the plugin uses the same shared API key as Jellyfin’s built-in TheMovieDb provider. You can optionally supply your own key in plugin settings.

## Manual install

1. Build:

```bash
dotnet build -c Release
```

2. Copy `Jellyfin.Plugin.FullCrew/bin/Release/net9.0/Jellyfin.Plugin.FullCrew.dll` into your Jellyfin plugins folder, e.g. `plugins/Jellyfin.Plugin.FullCrew/`.

3. Restart Jellyfin.

4. Hard-refresh the web client (Ctrl+Shift+R) and open a movie or series.

5. Optional: Dashboard → Plugins → **Full Crew** to tweak departments/cache or set a personal TMDB API key.

### Script injection

The plugin needs its script loaded in Jellyfin Web (in order):

1. **JavaScript Injector** (auto-registers if installed)
2. **File Transformation** (Docker-safe HTML rewrite)
3. Fallback: patch `index.html`, or add manually:

```html
<script plugin="FullCrew" src="/FullCrew/fullcrew.js"></script>
```

## Configuration

| Setting | Description |
| --- | --- |
| TMDB API Key | Optional; blank uses Jellyfin’s shared TMDB key |
| Cache hours | In-memory cache duration (default 12) |
| Max people per department | Cap per accordion section |
| Departments | Toggle which groups appear |

## API

Authenticated:

- `GET /FullCrew/{itemId}` — categorized cast/crew JSON

Public assets:

- `GET /FullCrew/fullcrew.js`
- `GET /FullCrew/fullcrew.css`

## Notes

- Movies use `/movie/{id}/credits`; series use `/tv/{id}/aggregate_credits`.
- Episodes resolve credits from the parent series TMDB id when needed.
- Credits are not written into Jellyfin’s people library.

## Release

Tag a version to build and publish a catalog zip:

```bash
git tag v1.0.0.0
git push origin v1.0.0.0
```
