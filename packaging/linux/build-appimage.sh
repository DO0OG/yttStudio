#!/usr/bin/env bash
# publish 결과를 단일 실행 파일 AppImage 로 묶는다.
#
#   build-appimage.sh <publish 경로> <버전> <artifacts 경로>
#
# GitHub 러너에는 FUSE 가 없으므로 appimagetool 을 --appimage-extract-and-run
# 으로 돌린다. 이 옵션이 없으면 "Cannot mount AppImage" 로 실패한다.
set -euo pipefail

publish_dir="${1:?publish 경로가 필요하다}"
version="${2:?버전이 필요하다}"
artifacts_dir="${3:?artifacts 경로가 필요하다}"

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
executable="YttStudio.App"
work_dir="$(mktemp -d)"
app_dir="${work_dir}/AppDir"

mkdir -p "${app_dir}/usr/bin" \
         "${app_dir}/usr/share/applications" \
         "${app_dir}/usr/share/icons/hicolor/256x256/apps" \
         "$artifacts_dir"

cp -R "${publish_dir}/." "${app_dir}/usr/bin/"
chmod +x "${app_dir}/usr/bin/${executable}"

# appimagetool 은 AppDir 최상단의 AppRun · .desktop · 아이콘을 요구한다.
install -m 755 "${repo_root}/packaging/linux/AppRun" "${app_dir}/AppRun"
install -m 644 "${repo_root}/packaging/linux/yttStudio.desktop" "${app_dir}/yttStudio.desktop"
install -m 644 "${repo_root}/packaging/linux/yttStudio.desktop" \
    "${app_dir}/usr/share/applications/yttStudio.desktop"
install -m 644 "${repo_root}/docs/assets/logo.png" "${app_dir}/yttstudio.png"
install -m 644 "${repo_root}/docs/assets/logo.png" \
    "${app_dir}/usr/share/icons/hicolor/256x256/apps/yttstudio.png"

tool="${work_dir}/appimagetool"
curl -fsSL -o "$tool" \
    "https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage"
chmod +x "$tool"

output="${artifacts_dir}/yttStudio-v${version}-linux-x86_64.AppImage"
ARCH=x86_64 "$tool" --appimage-extract-and-run "$app_dir" "$output"
chmod +x "$output"

echo "AppImage: ${output}"
