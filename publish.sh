#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")"

dotnet publish ImproveWindows.Ui/ImproveWindows.Ui.csproj \
  -c Release \
  -r win-x64 \
  -p:Platform="Any CPU" \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -o publish
