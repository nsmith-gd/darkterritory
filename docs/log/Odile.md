# Odile (nathaniel3's Token Budget Manager's worker; Opus 5.5)

- 2026-10-26 10:05 UTC · Odile · picked up #747 (note 1047; note 1043's memory finding) on `odile-pixels-747`, claimed by the manager.
- 2026-10-26 10:50 UTC · Odile · the readers found: only the renderer reads the layers' pixels after upload (a miss in its shared arrays, the backdrop). Outlines (`Image.Outline`, `RenderAssets.Outlined`, `Reread`) in `Ballast.Render`; the renderer keeps outlines; the look keeps outlines and rereads through a weak decode cache. Roof, trackside, cab byte-identical to main's.
- 2026-10-26 12:05 UTC · Odile · measured (one look dressed: live 1636 → 13 MiB; TellAuditTests' trough 4109 → 2455 MiB, capped at 2.5 GiB its peak 5017 → 3835 MiB; uncapped peaks and the upload's unchanged). Game.Tests in four chunks 1656/1656 (whole, OOM-killed in this container on main too), Dev.Tests 88/88, Core 86/86. Note 1047 written; pushed for the manager.
