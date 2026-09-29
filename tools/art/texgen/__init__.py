"""Dark Territory texture generator (GDD §25-28).

Modules:
  core      colour spaces, palette, seeded RNG, image I/O helpers
  noise     periodic (tiling) noise, voronoi, height/light helpers
  convert   the PBR -> legacy (diffuse + spec/gloss) conversion and the "hand pass"
  sources   CC0 photo source loading with provenance and a procedural fallback
  mat_*     the texture list, grouped by family
  sheet     contact sheet for review
"""
