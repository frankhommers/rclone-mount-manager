#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APP_NAME="Rclone Mount Manager"
BUNDLE_ID="io.github.frankhommers.rclone-mount-manager"
CONFIGURATION="Release"
OUTPUT_DIR="$ROOT_DIR/dist"
RID=""
ENABLE_CODESIGN="false"
APP_VERSION=""

usage() {
  cat <<EOF
Builds a distributable macOS .app bundle for Rclone Mount Manager.

Usage:
  scripts/build-macos-app.sh [options]

Options:
  --rid <rid>              Runtime identifier (default: osx-arm64 on Apple Silicon, osx-x64 on Intel)
  --configuration <conf>   Build configuration (default: Release)
  --output <dir>           Output directory for .app bundle (default: ./dist)
  --codesign               Apply ad-hoc codesign to bundle
  --version <semver>       Version to embed in published binaries (e.g. 1.2.3)
  -h, --help               Show this help
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --rid) RID="$2"; shift 2 ;;
    --configuration) CONFIGURATION="$2"; shift 2 ;;
    --output) OUTPUT_DIR="$2"; shift 2 ;;
    --codesign) ENABLE_CODESIGN="true"; shift ;;
    --version) APP_VERSION="$2"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown option: $1" >&2; usage; exit 1 ;;
  esac
done

if [[ -z "$RID" ]]; then
  ARCH="$(uname -m)"
  if [[ "$ARCH" == "arm64" ]]; then
    RID="osx-arm64"
  else
    RID="osx-x64"
  fi
fi

PUBLISH_DIR="$ROOT_DIR/.artifacts/publish/gui/$RID"
APP_BUNDLE_DIR="$OUTPUT_DIR/$APP_NAME.app"
APP_CONTENTS_DIR="$APP_BUNDLE_DIR/Contents"
APP_MACOS_DIR="$APP_CONTENTS_DIR/MacOS"
APP_RESOURCES_DIR="$APP_CONTENTS_DIR/Resources"

PUBLISH_VERSION_ARG=()
if [[ -n "$APP_VERSION" ]]; then
  PUBLISH_VERSION_ARG=("-p:Version=$APP_VERSION")
fi

echo "==> Publishing GUI ($RID, $CONFIGURATION)"
dotnet publish "$ROOT_DIR/RcloneMountManager.GUI/RcloneMountManager.GUI.csproj" \
  -c "$CONFIGURATION" \
  -r "$RID" \
  --self-contained true \
  "${PUBLISH_VERSION_ARG[@]}" \
  -o "$PUBLISH_DIR"

echo "==> Creating app bundle at $APP_BUNDLE_DIR"
rm -rf "$APP_BUNDLE_DIR"
mkdir -p "$APP_MACOS_DIR" "$APP_RESOURCES_DIR"

cp -R "$PUBLISH_DIR"/* "$APP_MACOS_DIR/"
chmod +x "$APP_MACOS_DIR/RcloneMountManager.GUI"

cat > "$APP_CONTENTS_DIR/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key>
  <string>$APP_NAME</string>
  <key>CFBundleDisplayName</key>
  <string>$APP_NAME</string>
  <key>CFBundleIdentifier</key>
  <string>$BUNDLE_ID</string>
  <key>CFBundleVersion</key>
  <string>${APP_VERSION:-1.0.0}</string>
  <key>CFBundleShortVersionString</key>
  <string>${APP_VERSION:-1.0.0}</string>
  <key>CFBundleExecutable</key>
  <string>RcloneMountManager.GUI</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleIconFile</key>
  <string>AppIcon</string>
  <key>LSMinimumSystemVersion</key>
  <string>11.0</string>
  <key>NSHighResolutionCapable</key>
  <true/>
</dict>
</plist>
EOF

if [[ "$ENABLE_CODESIGN" == "true" ]]; then
  echo "==> Applying ad-hoc codesign"
  codesign --force --deep --sign - "$APP_BUNDLE_DIR"
fi

echo "==> Done"
echo "App bundle: $APP_BUNDLE_DIR"
echo "Run it with: open \"$APP_BUNDLE_DIR\""
