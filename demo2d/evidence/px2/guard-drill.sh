#!/bin/zsh
# PX2's vendor guard drill: in a scratch clone, force-stage vendor, derived and baked files one
# case at a time and run the guard on the index. Every case must fail; the clean index must pass.
set -u
WT=$1; OUT=$2; C=$(mktemp -d)/clone
git clone -q "$WT" "$C" && cd "$C" || exit 1
V=$WT/demo2d/Assets/Vendor
guard() { uv run -q --project demo3d/tools demo2d guard --index > /tmp/px2-drill-guard.txt 2>&1; echo $?; }
run() { # name, then: dest src pairs
  local name=$1; shift
  while (( $# )); do mkdir -p "$(dirname "$1")"; cp "$2" "$1"; git add -f -- "$1"; shift 2; done
  local code=$(guard)
  local why=$(grep -v '^vendor guard' /tmp/px2-drill-guard.txt | head -1 | cut -c1-150)
  printf '%-4s %s: %s\n' "$([ $code = 0 ] && echo PASS || echo FAIL)" "$name" "$why" >> "$OUT"
  git reset -q --hard HEAD; git clean -qfdx demo2d/Assets demo2d/evidence docs 2>/dev/null
}
echo "PX2 vendor guard drill at $(git rev-parse --short HEAD), $(date -u +%FT%TZ)" > "$OUT"
printf '%-4s %s\n' "$([ $(guard) = 0 ] && echo PASS || echo FAIL)" "the clean index (control)" >> "$OUT"
for pack in knight_male Skeleton Ghoul "Zombie Warrior" "Flame Imp" "Ice Skull" Mask "Skeleton Mage" "Blood Mage" Ogre Cyclops Werewolf "Lava Beast" "Boss Devil Man"; do
  f=$(ls "$V/$pack"/*_png/*.png | head -1); a=$(ls "$V/$pack"/*/*.anim | head -1); b=$(basename "$f")
  run "$pack: a frame force-staged in Vendor/" "demo2d/Assets/Vendor/${f#$V/}" "$f"
  run "$pack: the frame copied under Demo2D/Art/" "demo2d/Assets/Demo2D/Art/$b" "$f"
  run "$pack: the frame's .meta alone under Demo2D/Art/" "demo2d/Assets/Demo2D/Art/$b.meta" "$f.meta"
  run "$pack: a clip (.anim as Unity rewrote it, YAML) under Demo2D/Art/" "demo2d/Assets/Demo2D/Art/$(basename "$a")" "$a"
  run "$pack: the source clip (.anim, binary) under docs/" "docs/$(basename "$a")" "$HOME/unity_assets/Assets/${a#$V/}"
done
F=$V/RPGW_AncientForest
run "AncientForest: MainLev.png force-staged in Vendor/" "demo2d/Assets/Vendor/RPGW_AncientForest/Sliced/MainLev.png" "$F/Sliced/MainLev.png"
run "AncientForest: MainLev.png renamed under docs/" "docs/tech-demo-2d/tiles.png" "$F/Sliced/MainLev.png"
run "AncientForest: MainLev.png.meta alone under Demo2D/Data/" "demo2d/Assets/Demo2D/Data/MainLev.png.meta" "$F/Sliced/MainLev.png.meta"
run "AncientForest: a tree clip (.anim, YAML) under Demo2D/Art/" "demo2d/Assets/Demo2D/Art/tree1.anim" "$F/Animated/tree1.anim"
run "AncientForest: a tree controller under unity-packages/" "unity-packages/com.playlistsurvivors.choreography/Runtime/tree1_0.controller" "$F/Animated/tree1_0.controller"
X="$V/JMO Assets/Cartoon FX Remaster"
run "CFXR: a prefab under Demo2D/Art/" "demo2d/Assets/Demo2D/Art/slash.prefab" "$X/CFXR Prefabs/Impacts/CFXR Slash (Blue).prefab"
run "CFXR: a prefab's .meta alone under Demo2D/Art/" "demo2d/Assets/Demo2D/Art/slash.prefab.meta" "$X/CFXR Prefabs/Impacts/CFXR Slash (Blue).prefab.meta"
run "CFXR: the ubershader (text) under Demo2D/Runtime/" "demo2d/Assets/Demo2D/Runtime/Uber.cfxrshader" "$X/CFXR Assets/Shaders/CFXR Particle Ubershader.cfxrshader"
run "CFXR: a script (CFXR_Effect.cs) under Demo2D/Runtime/" "demo2d/Assets/Demo2D/Runtime/CFXR_Effect.cs" "$X/CFXR Assets/Scripts/CFXR_Effect.cs"
run "CFXR: a texture force-staged in Vendor/" "demo2d/Assets/Vendor/JMO Assets/Cartoon FX Remaster/CFXR Assets/Graphics/cfxr star.png" "$X/CFXR Assets/Graphics/cfxr star.png"
for pack in "Magic Spells SFX Bundle" "RPG Magic Sound Effects Pack 3 [ELEMENTAL]" "Universal Sound FX"; do
  s=$(find "$V/$pack" -type f ! -name '*.meta' | head -1)
  run "$pack: a sound under Demo2D/Audio/" "demo2d/Assets/Demo2D/Audio/$(basename "$s")" "$s"
  run "$pack: its .meta alone under Demo2D/Audio/" "demo2d/Assets/Demo2D/Audio/$(basename "$s").meta" "$s.meta"
done
# A baked atlas: our own generated PNG (no vendor bytes), in Baked/, and a derived one elsewhere.
python3 -c "from PIL import Image; Image.new('RGBA',(64,64),(200,40,40,255)).save('/tmp/px2-drill-atlas.png')"
printf 'fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef\n' > /tmp/px2-drill-atlas.png.meta
run "a baked atlas force-staged in Baked/" "demo2d/Assets/Baked/Atlases/skeleton.png" /tmp/px2-drill-atlas.png "demo2d/Assets/Baked/Atlases/skeleton.png.meta" /tmp/px2-drill-atlas.png.meta
run "a baked atlas (untagged PNG) under Demo2D/Art/" "demo2d/Assets/Demo2D/Art/skeleton-atlas.png" /tmp/px2-drill-atlas.png
run "a baked atlas (untagged PNG) under evidence/px2/" "demo2d/evidence/px2/skeleton-atlas.png" /tmp/px2-drill-atlas.png
cp "$WT/demo2d/evidence/px2/trees.jpg" /tmp/px2-drill-tagged.jpg
run "a tagged still under evidence/ (control: allowed)" "demo2d/evidence/px2/trees-copy.jpg" /tmp/px2-drill-tagged.jpg
rm -rf "$(dirname "$C")"
