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

arch="amd64"
case "$rid" in
  linux-x64) arch="amd64" ;;
  linux-arm64) arch="arm64" ;;
  *) echo "Unsupported Linux runtime identifier '$rid'." >&2; exit 1 ;;
esac

package_name="monica"
package_root="artifacts/package-root/${rid}/${mode}"
install_dir="$package_root/usr/lib/monica"
bin_dir="$package_root/usr/bin"
desktop_dir="$package_root/usr/share/applications"
icon_dir="$package_root/usr/share/icons/hicolor/256x256/apps"
control_dir="$package_root/DEBIAN"

rm -rf "$package_root"
mkdir -p "$install_dir" "$bin_dir" "$desktop_dir" "$icon_dir" "$control_dir" "$output_dir"
cp -a "$publish_dir/." "$install_dir/"

chmod +x "$install_dir/Monica.App" || true

cat > "$bin_dir/monica" <<'EOF'
#!/usr/bin/env sh
exec /usr/lib/monica/Monica.App "$@"
EOF
chmod 0755 "$bin_dir/monica"

if [[ -f "$install_dir/Assets/Logo.png" ]]; then
  cp "$install_dir/Assets/Logo.png" "$icon_dir/monica.png"
fi

cat > "$desktop_dir/monica.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=Monica
Comment=Monica password and secure vault manager
Exec=monica
Icon=monica
Terminal=false
Categories=Utility;Security;
EOF

installed_size="$(du -sk "$package_root/usr" | cut -f1)"
cat > "$control_dir/control" <<EOF
Package: $package_name
Version: $version
Section: utils
Priority: optional
Architecture: $arch
Maintainer: Monica Maintainers <maintainers@example.com>
Installed-Size: $installed_size
Depends: libc6, libx11-6, libice6, libsm6, libfontconfig1
Description: Monica password and secure vault manager
 Monica by Avalonia desktop package built in $mode mode from $rid.
EOF

deb_path="$output_dir/Monica-${version}-${rid}-${mode}.deb"
fakeroot dpkg-deb --build "$package_root" "$deb_path"
echo "Created $deb_path"
