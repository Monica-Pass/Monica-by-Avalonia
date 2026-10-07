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

# Avalonia's ICO is enough for the Windows executable, but Finder requires an
# ICNS resource for a real application icon. Build the standard iconset from
# the shipped logo when Apple's image tools are available on the packaging host.
icon_source="$macos_dir/Assets/Logo.png"
iconset_dir="$work_dir/AppIcon.iconset"
icon_file="$resources_dir/AppIcon.icns"
if [[ -f "$icon_source" ]] && command -v sips >/dev/null 2>&1 && command -v iconutil >/dev/null 2>&1; then
  mkdir -p "$iconset_dir"
  for size in 16 32 128 256 512; do
    sips -z "$size" "$size" "$icon_source" --out "$iconset_dir/icon_${size}x${size}.png" >/dev/null
    double_size=$((size * 2))
    sips -z "$double_size" "$double_size" "$icon_source" --out "$iconset_dir/icon_${size}x${size}@2x.png" >/dev/null
  done
  iconutil -c icns "$iconset_dir" -o "$icon_file"
  rm -rf "$iconset_dir"
fi

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
  <key>CFBundleIconFile</key>
  <string>AppIcon</string>
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
