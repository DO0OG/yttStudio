#!/usr/bin/env bash
# publish 결과를 macOS .app 번들로 감싸고 .dmg 디스크 이미지를 만든다.
#
#   build-macos.sh <publish 경로> <버전> <번들 스테이지 경로> <artifacts 경로>
#
# 애플 개발자 계정이 없으므로 ad-hoc 서명만 한다. Apple Silicon 에서는 서명이
# 아예 없으면 실행이 거부되므로 ad-hoc 서명은 선택이 아니라 필수다.
# Gatekeeper 격리 경고는 남는다. 사용자 안내는 README 를 따른다.
set -euo pipefail

publish_dir="${1:?publish 경로가 필요하다}"
version="${2:?버전이 필요하다}"
stage_dir="${3:?번들 스테이지 경로가 필요하다}"
artifacts_dir="${4:?artifacts 경로가 필요하다}"

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
app_name="yttStudio"
executable="YttStudio.App"
app_dir="${stage_dir}/${app_name}.app"

# 재실행 시 이전 번들이 섞이지 않도록 스테이지를 비우고 시작한다.
if [ -d "$stage_dir" ]; then
    chmod -R u+w "$stage_dir"
    find "$stage_dir" -mindepth 1 -delete
fi
mkdir -p "${app_dir}/Contents/MacOS" "${app_dir}/Contents/Resources" "$artifacts_dir"

cp -R "${publish_dir}/." "${app_dir}/Contents/MacOS/"
chmod +x "${app_dir}/Contents/MacOS/${executable}"

sed "s/__VERSION__/${version}/g" "${repo_root}/packaging/macos/Info.plist" \
    > "${app_dir}/Contents/Info.plist"

# 256px 원본에서 업스케일 없이 만들 수 있는 크기만 담는다.
iconset="$(mktemp -d)/app.iconset"
mkdir -p "$iconset"
source_icon="${repo_root}/docs/assets/logo.png"
sips -z 16 16     "$source_icon" --out "${iconset}/icon_16x16.png"      > /dev/null
sips -z 32 32     "$source_icon" --out "${iconset}/icon_16x16@2x.png"   > /dev/null
sips -z 32 32     "$source_icon" --out "${iconset}/icon_32x32.png"      > /dev/null
sips -z 64 64     "$source_icon" --out "${iconset}/icon_32x32@2x.png"   > /dev/null
sips -z 128 128   "$source_icon" --out "${iconset}/icon_128x128.png"    > /dev/null
sips -z 256 256   "$source_icon" --out "${iconset}/icon_128x128@2x.png" > /dev/null
sips -z 256 256   "$source_icon" --out "${iconset}/icon_256x256.png"    > /dev/null
iconutil -c icns "$iconset" -o "${app_dir}/Contents/Resources/app.icns"

# 번들 구조를 바꾼 뒤이므로 기존 서명은 무효다. 다시 ad-hoc 서명한다.
codesign --force --deep --sign - "$app_dir"
codesign --verify --deep --strict "$app_dir"

dmg_root="$(mktemp -d)/dmg"
mkdir -p "$dmg_root"
cp -R "$app_dir" "${dmg_root}/"
ln -s /Applications "${dmg_root}/Applications"

dmg_path="${artifacts_dir}/yttStudio-v${version}-osx-arm64.dmg"
hdiutil create \
    -volname "${app_name} ${version}" \
    -srcfolder "$dmg_root" \
    -fs HFS+ \
    -format UDZO \
    -ov \
    "$dmg_path"

echo "번들: ${app_dir}"
echo "디스크 이미지: ${dmg_path}"
