#!/bin/bash
# Builds the Mac app for one kind of Mac and puts it in a disk image the
# way Mac apps are handed out: open it, drag the app to Applications.
#
#   mac/package.sh osx-arm64 AppleSilicon [--needs-register]
#   mac/package.sh osx-x64   Intel        [--needs-register]
#
# The app is not signed with an Apple developer certificate (there is
# none), only ad hoc, which Apple Silicon requires of anything it runs. So
# the first time, macOS says it cannot check the app: open it once from
# System Settings, Privacy & Security, Open Anyway.
#
# The image is then opened again and the app inside it is run: its own
# checks (--selftest) and a real start (--starttest). With --needs-register
# the checks fail unless the build was given the register's project.
set -euo pipefail
rid="$1"; name="$2"; needs="${3:-}"
here="$(cd "$(dirname "$0")" && pwd)"
repo="$(cd "$here/.." && pwd)"
cd "$here"

./make-icon.sh
dotnet build IdMaker.Mac -c Release -r "$rid"
app="$(find "IdMaker.Mac/bin/Release" -maxdepth 4 -path "*$rid*" -name '*.app' | head -1)"
[ -n "$app" ] || { echo "The build made no app for $rid."; exit 1; }
[ -f "$app/Contents/Resources/AppIcon.icns" ] || { echo "The app has no icon."; exit 1; }
codesign --force --deep --sign - "$app"
codesign --verify --deep --strict "$app"

mkdir -p dist
stage="$(mktemp -d)"
cp -R "$app" "$stage/"
ln -s /Applications "$stage/Applications"
image="dist/AlphaPremierIdMaker-$name.dmg"
rm -f "$image"
hdiutil create -volname "Alpha Premier ID Maker" -srcfolder "$stage" -ov -format UDZO "$image" >/dev/null
ls -l "$image"

# what a person gets: the image opened, the app inside it run
mount="$(mktemp -d)"
hdiutil attach -nobrowse -readonly -mountpoint "$mount" "$image" >/dev/null
trap 'hdiutil detach "$mount" -force >/dev/null || true' EXIT
inside="$(find "$mount" -maxdepth 1 -name '*.app' | head -1)"
binary="$inside/Contents/MacOS/AlphaPremierIdMaker"
file "$binary"
run=()
if [ "$rid" = "osx-x64" ] && [ "$(uname -m)" = "arm64" ]; then run=(arch -x86_64); fi
# (written this way because the bash a Mac ships with treats an empty list as unset)
${run[@]+"${run[@]}"} "$binary" --selftest --repo "$repo" $needs
${run[@]+"${run[@]}"} "$binary" --starttest
echo "Packaged and started: $image"
