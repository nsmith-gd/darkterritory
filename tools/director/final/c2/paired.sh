#!/bin/bash
# #663's gentler surge, paired by seed with a same-commit control (main 67ff258b + n1-c2-663b): resumable, one cell × 2 seeds a file per arm, both arms at once.
S=${S:-out/director}  # scratch dir: chunks land in $S/out-<arm>
W=${W:-.}  # the checkout to run, built
mkdir -p $S/out-g-control $S/out-g-gentle
run() { # arm edition cell seeds
  out=$S/out-g-$1/$3-$4.json
  [ -s $out ] && return
  ed=""; [ -n "$2" ] && ed="--edition $2"
  (cd $W && dotnet run --no-build --project src/DarkTerritory.Cli -- $ed director sweep --routes frontier --cells $3 --seeds $4 --seconds 2700 --parallel 2 --out $out.tmp > /dev/null 2>> $S/out-g-$1.log) && mv $out.tmp $out
}
for b in 1-2 3-4 5-6 7-8 9-10; do
  for cell in 1x3 2x5 4x8 8x12; do
    run control "" $cell $b & run gentle c2-gentle $cell $b & wait
  done
  echo "seeds $b done $(date -u +%H:%M)"
done
