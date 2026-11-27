# OSU SR

OSU SR is a fork of osu!lazer (https://github.com/ppy/osu) for **continuous play and skillset training**.
Instead of playing whole maps, the player plays a chain of map *segments* (mostly the hardest parts) that
match a chosen skillset (aim, speed, stamina, tech…), with natural transitions between segments.

Scope for now: **osu!standard only**, using the player's **locally installed beatmaps**.

## Plan

1. Minimal lazer base: only "pick a map, play it, see results". (done in the first PR)
2. Skillset segment detection from lazer's per-object difficulty strains: `osu.Game.Rulesets.Osu/OsuSR/SkillsetSegmentFinder.cs` (Aim, Speed, Reading).
3. Play a segment: start/end time inside a map, score only that part.
4. Continuous session: chain segments with audio crossfade, close BPMs where possible, short countdown, no menu in between.

## Conventions

- Upstream base: snapshot of ppy/osu tag `2026.1117.0-lazer` (no upstream history; see the import commit).
  To update, diff two upstream tags and apply the patch, rather than merging.
- **Hide, don't delete** upstream features, so upstream updates stay easy to apply.
  Switches live in `osu.Game/OsuSR/OsuSRFeatures.cs`; mark each OSU SR change in upstream files with an `// OSU SR:` comment.
- New OSU SR code goes under `osu.Game/OsuSR/` (or `osu.Game.Rulesets.Osu/OsuSR/` for ruleset-specific code).
- Branding: the code is MIT, but the "osu!" name, logo and art assets are not licensed for reuse.
  Anything published must use OSU SR's own name and visuals.

## Build & test

- Requires the .NET 10 SDK (`global.json`).
- Run the game: `dotnet run --project osu.Desktop`
- Build: `dotnet build osu.Desktop`
- Tests: `dotnet test osu.Game.Tests --filter "FullyQualifiedName~<TestScene>"` (run targeted scenes; the full suite is long).
  Tests that hit osu! web services fail in sandboxes without network access.
