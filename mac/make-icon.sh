#!/bin/bash
# The app's icon, in the form a Mac wants (.icns), made from the same
# picture the Windows icon was made from. Needs a Mac (sips, iconutil).
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
source="$here/../assets/id-maker-logo.png"
set_dir="$(mktemp -d)/AppIcon.iconset"
mkdir -p "$set_dir"
for size in 16 32 128 256 512; do
  sips -z "$size" "$size" "$source" --out "$set_dir/icon_${size}x${size}.png" >/dev/null
  double=$((size * 2))
  sips -z "$double" "$double" "$source" --out "$set_dir/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$set_dir" -o "$here/IdMaker.Mac/AppIcon.icns"
echo "Made $here/IdMaker.Mac/AppIcon.icns"
