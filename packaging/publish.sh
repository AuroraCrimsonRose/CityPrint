#!/usr/bin/env bash
# Builds self-contained release archives for Windows, Linux and macOS into artifacts/.
# Usage: packaging/publish.sh   (from Git Bash on Windows; needs the .NET 10 SDK)
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
project="$root/src/CityPrint.App/CityPrint.App.csproj"
out="$root/artifacts"
version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$project")"

rm -rf "$out"
mkdir -p "$out"

publish() {
  dotnet publish "$project" -c Release -r "$1" --self-contained true \
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:DebugType=none -p:DebugSymbols=false -o "$2" -v q -nologo
  # Native dependencies ship their own symbol files; users don't need them.
  find "$2" -name "*.pdb" -delete
}

# GNU tar can mark files executable even when building on Windows, where the file system has no exec bit.
tarball() { tar --owner=0 --group=0 --mode='u+rwX,go+rX,a+x' -czf "$1" -C "$2" "$3"; }

for rid in win-x64 linux-x64 osx-x64 osx-arm64; do
  echo "== $rid"
  stage="$out/stage/$rid"
  case "$rid" in
    win-*)
      publish "$rid" "$stage/CityPrint"
      cp "$root/LICENSE" "$root/NOTICE" "$stage/CityPrint/"
      (cd "$stage" && /c/Windows/System32/tar.exe -a -cf "$(cygpath -w "$out/CityPrint-$rid.zip")" CityPrint)
      ;;
    linux-*)
      publish "$rid" "$stage/CityPrint"
      cp "$root/LICENSE" "$root/NOTICE" "$stage/CityPrint/"
      tarball "$out/CityPrint-$rid.tar.gz" "$stage" CityPrint
      ;;
    osx-*)
      app="$stage/City Print.app/Contents"
      publish "$rid" "$app/MacOS"
      mkdir -p "$app/Resources"
      cp "$root/packaging/macos/CityPrint.icns" "$app/Resources/"
      cp "$root/LICENSE" "$root/NOTICE" "$app/Resources/"
      sed "s/@VERSION@/$version/g" "$root/packaging/macos/Info.plist" > "$app/Info.plist"
      tarball "$out/CityPrint-$rid.tar.gz" "$stage" "City Print.app"
      ;;
  esac
done

rm -rf "$out/stage"
(cd "$out" && sha256sum CityPrint-* > SHA256SUMS.txt)
ls -la "$out"
