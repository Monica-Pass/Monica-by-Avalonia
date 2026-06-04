#!/usr/bin/env bash
set -euo pipefail

publish_dir="${1:?publish directory is required}"
output_dir="${2:?output directory is required}"
version="${3:?version is required}"
rid="${4:?runtime identifier is required}"
mode="${5:?mode is required}"

if [[ ! -d "$publish_dir" ]]; then
  echo "Publish directory '$publish_dir' was not found." >&2
  exit 1
fi

mkdir -p "$output_dir"

work_dir="artifacts/dmg/${rid}/${mode}"
app_dir="$work_dir/Monica.app"
contents_dir="$app_dir/Contents"
macos_dir="$contents_dir/MacOS"
resources_dir="$contents_dir/Resources"

rm -rf "$work_dir"
mkdir -p "$macos_dir" "$resources_dir"
cp -a "$publish_dir/." "$macos_dir/"
chmod +x "$macos_dir/Monica.App" || true

cat > "$contents_dir/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleExecutable</key>
  <string>Monica.App</string>
  <key>CFBundleIdentifier</key>
  <string>io.github.joyinjoester.monica</string>
  <key>CFBundleName</key>
  <string>Monica</string>
  <key>CFBundleDisplayName</key>
  <string>Monica</string>
  <key>CFBundleVersion</key>
  <string>$version</string>
  <key>CFBundleShortVersionString</key>
  <string>$version</string>
  <key>LSMinimumSystemVersion</key>
  <string>11.0</string>
  <key>NSHighResolutionCapable</key>
  <true/>
</dict>
</plist>
EOF

ln -s /Applications "$work_dir/Applications"

dmg_path="$output_dir/Monica-${version}-${rid}-${mode}.dmg"
rm -f "$dmg_path"
hdiutil create -volname "Monica $version" -srcfolder "$work_dir" -ov -format UDZO "$dmg_path"
echo "Created $dmg_path"
