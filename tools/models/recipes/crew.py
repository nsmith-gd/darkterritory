"""CREW (GDD §29): the rail worker of tools/blender/crew.py, taken to the fidelity target (ARCHITECTURE §8 note 58).

tools/blender/crew.py stays the crew's source: its skeleton, its weights, its clips and its game mesh (the kit's lofts
and tubes, 1.8 m, SK_Human, variants 0 cap, 1 helmet, 2 cap + scarf, 3 helmet + scarf). tools/models/crewfigure runs it,
then:
  * swaps the egg of a head for Lee Perry-Smith's scanned head (CC BY 3.0), decimated, on the head bone, the hats
    over it as before;
  * models a high-resolution copy of every part over the game mesh: the coat's weight hanging in folds below the
    belt, seams down the back and sides, buttons down the front, the sleeves bunched at the elbow and the cuff,
    trousers creased at the knee and gathered over the boots, laced boots with a welt and a cleated sole, gloves with
    their fingers parted and a seam over the knuckles, a cap of stitched panels, a helmet with a rolled rim;
  * dresses the high copy in the texture library's cloth, oilskin, leather and wool at the library's own scale, and
    the scan's own skin;
  * bakes it down (normal, occlusion, colour) into one 1024 atlas on the game mesh, part by part so a cap never
    shadows a helmet it's never worn with, and sooted: soot in the creases, grime rising from the boots.
The chest lamp keeps crew_atlas's lit glass (a pure light, not baked); the shovel keeps the library's own layers.

    python3 tools/models/fetch.py threejs-leeperrysmith && tools/models/build.sh crew
    CREW_PREVIEW=1 tools/models/build.sh crew   # renders the high figure to out/review/crew-high-*.png, no bake
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import crewfigure  # noqa: E402

crewfigure.build("crew", crewfigure.Style())
