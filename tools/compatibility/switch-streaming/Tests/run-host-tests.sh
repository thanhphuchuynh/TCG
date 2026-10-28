#!/bin/sh
set -eu
here=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
mono_bin=${TCG_MONO_BIN:-/Applications/Unity/Hub/Editor/2021.3.45f2/Unity.app/Contents/MonoBleedingEdge/bin}
test_dir=$(mktemp -d "${TMPDIR:-/tmp}/tcg-art-tests.XXXXXX")
trap 'rm -rf "$test_dir"' EXIT HUP INT TERM
"$mono_bin/mcs" -out:"$test_dir/tests.exe" "$here/HostTests.cs" "$here/../Runtime/ArtCache.cs" "$here/../Runtime/ArtManifest.cs"
"$mono_bin/mono" "$test_dir/tests.exe"
