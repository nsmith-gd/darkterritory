#!/bin/bash
# #613's final sweep, two editions (n1-B1's n1-b1-613-final, note 930), in resumable chunks: one cell × 4 seeds a file.
S=${S:-out/director}  # scratch dir: chunks land in $S/out-<arm>
W=${W:-.}  # the checkout to run, built
ed=$1
mkdir -p $S/out-$ed
for cell in 1x3 2x5 4x8 8x12; do
  for b in 1-4 5-8 9-10; do
    out=$S/out-$ed/$cell-$b.json
    [ -s $out ] && continue
    (cd $W && dotnet run --no-build --project src/DarkTerritory.Cli -- --edition $ed director sweep --routes frontier --cells $cell --seeds $b --seconds 2700 --parallel 4 --out $out.tmp > /dev/null 2>> $S/out-$ed.log) && mv $out.tmp $out
  done
done
echo "$ed done"
