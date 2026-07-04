# 2026 FIFA World Cup Tournament Tracker

A Windows desktop app built with WPF/.NET 10 that tracks the 2026 FIFA World Cup in real time — live knockout bracket, group standings, penalty shootout scores, and a one-click PDF export poster.

![Platform](https://img.shields.io/badge/platform-Windows-0078D4?logo=windows)
![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)
![Language](https://img.shields.io/badge/language-C%23-239120?logo=csharp)

---

## Features

| Feature | Details |
|---|---|
| **Live knockout bracket** | Full 32 → 16 → QF → SF → Final tree, auto-scaled via `Viewbox` to fit any resolution or DPI setting |
| **Penalty shootout scores** | Fetches shootout results from the FIFA detail endpoint for drawn knockout matches |
| **Group stage standings** | All 12 groups with points, GD, top-2 highlighted, best-8 third-place teams marked |
| **Live score badge** | "In Progress" indicator with elapsed time on bracket cards during live matches |
| **Auto-refresh** | Scores refresh automatically every 60 seconds from the FIFA Official API |
| **PDF export** | Single-page poster: bracket tree on top, all 12 group standings below, with team flags |
| **Chinese / English UI** | Toggle in the toolbar; switches labels, team names, and time zones instantly |
| **Timezone display** | English mode shows venue local time (`UTC-4`, `UTC-7`, etc.); Chinese mode shows Beijing time (`UTC+8`) |

---

## Screenshots

> _Add screenshots here after first run._

---

## Data Sources

The app uses a two-layer data pipeline:

1. **Local JSON baseline** (`Data/tournament_matches.json`) — fixture skeleton: match IDs, team codes, venue UTC offsets, and bracket structure. Embedded in the binary at build time.
2. **FIFA Official API** (`api.fifa.com`) — live scores, match status, and penalty shootout data. No API key required.

On each refresh, the FIFA API data is overlaid on the local skeleton. Penalty scores that are absent from the calendar endpoint are fetched individually from `/api/v3/live/football/{idMatch}`.

---

## Tech Stack

| Package | Purpose |
|---|---|
| `.NET 10 / WPF` | UI framework (Windows only) |
| `Microsoft.Extensions.DependencyInjection` | Service container |
| `Microsoft.Extensions.Http` | Typed `HttpClient` for FIFA API |
| `QuestPDF` | PDF poster generation |
| `SkiaSharp` | Raster rendering in the PDF (flags, vector graphics) |
| `CommunityToolkit.Mvvm` | MVVM source generators |
| `LiveChartsCore.SkiaSharpView.WPF` | Chart support (statistics view) |

---

## Requirements

- **OS**: Windows 10 or later (x64)
- **SDK**: [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- **Network**: Internet access to `api.fifa.com` for live scores

---

## Build & Run

```bash
# Clone
git clone https://github.com/<your-username>/WorldCup2026.git
cd WorldCup2026

# Run in debug
dotnet run --project WorldCup2026/WorldCup2026.csproj

# Publish as a self-contained single-file executable
dotnet publish WorldCup2026/WorldCup2026.csproj \
  -c Release \
  -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -o ./publish
```

The published binary in `./publish` runs on any Windows 10+ machine with no .NET runtime required.

---

## Project Structure

```
WorldCup2026/
├── Data/
│   ├── tournament_matches.json   # Fixture skeleton (embedded resource)
│   └── tournament_teams.json     # Team metadata
├── Models/
│   ├── Match.cs                  # Core match model (scores, penalties, status)
│   ├── Group.cs / Team.cs        # Group stage and team models
│   └── TournamentStage.cs        # Enum: GroupStage → RoundOf32 → … → Final
├── Services/
│   ├── FifaApiService.cs         # FIFA Official API client + penalty enrichment
│   ├── LocalDataService.cs       # Reads the embedded JSON baseline
│   ├── DataServiceAggregator.cs  # Merges sources, resolves bracket placeholders
│   ├── LocalizationService.cs    # Chinese / English toggle
│   └── PdfExportService.cs       # QuestPDF + SkiaSharp poster export
├── Views/
│   ├── BracketView.xaml(.cs)     # Knockout bracket — Canvas + Viewbox auto-scale
│   ├── GroupStageView.xaml(.cs)  # Group standings grid
│   └── StatisticsView.xaml       # Statistics (charts)
├── ViewModels/
│   ├── BracketViewModel.cs
│   └── GroupStageViewModel.cs
├── Resources/
│   └── Flags/                    # PNG flag images (embedded)
└── MainWindow.xaml(.cs)          # Shell: toolbar, layout, auto-refresh timer
```

---

## How the Bracket Works

The bracket is drawn entirely in code on a WPF `Canvas`:

1. 32 teams are split into an upper half and a lower half, each progressing left → right (upper) or right → left (lower, mirrored).
2. Positions are computed from the R32 layout; each subsequent round's match slots are centred between their two feeder matches.
3. The Final sits in the middle; the third-place match is placed directly below it.
4. `Winner Match N` / `Loser Match N` placeholders are resolved after each refresh once the referenced match has a confirmed result.
5. The entire `Canvas` is wrapped in a `Viewbox` (`StretchDirection="DownOnly"`) so the bracket scales down on smaller screens or at higher DPI without clipping or a scrollbar.

---

## Penalty Shootout Logic

The FIFA calendar endpoint (`/api/v3/calendar/matches`) does not include penalty scores. For any finished knockout match that ended level (same score in regular + extra time), the app calls the detail endpoint (`/api/v3/live/football/{idMatch}`) to retrieve `HomeTeamPenaltyScore` / `AwayTeamPenaltyScore`. Results are cached in memory so subsequent auto-refreshes do not re-fetch completed shootouts.

---

## License

MIT

---

## Acknowledgements

Match data provided by the [FIFA Official API](https://api.fifa.com). Team flag images sourced from public domain resources.
