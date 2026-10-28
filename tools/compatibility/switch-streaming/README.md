# Local artwork streaming experiment

These are source-only tools for an isolated, user-supplied Unity project. They
contain no game or mod files. The target is the existing Windows x86 player under
Autorun, not a native Switch export. A successful build does not establish a
playable Switch port.

`prepare_project.py PROJECT` backs up and applies guarded ownership hooks in the
private recovered project. It computes every replacement before writing sources
and is safe to repeat. Runtime and Editor sources are copied into that project.

The Unity batch entry point `TCG.SwitchStreaming.Editor.StreamingBuild.Run` is
restricted to the prepared `switch-on-demand/macos-port-work` clone. Start Unity
2021.3.45f2 with `-buildTarget Win`. It audits direct references and Resources
roots, builds one LZ4 bundle per eligible texture using original Sprite assets,
strips only verified catalogue/default-card references, and builds direct
Title/Start scenes. A retry restores only its own backed-up serialized assets.
Current resolution, compression, sampler, authored geometry and Ultra settings
stay unchanged. Reports and game outputs belong outside this public repository.

The cache deduplicates shared artwork and pins active/pending consumers. Inactive
cards release their image references before the final lease. Pack preloads and
album/deck gates wait for complete bindings; errors stay local. A resident copy
of the identifier-only manifest detects missing or altered external metadata.

## Verification

Run `sh Tests/run-host-tests.sh` and
`python3 -m unittest discover -s Tests -p 'test_*.py' -v` from this directory.
The cache runner uses the installed Unity Mono compiler without package restores.

For real Unity tests, use a separate empty 2021.3.45f2 project. Copy `Runtime/*.cs`
and `Tests/Unity/{StreamingProbe,CardUI}.cs` under Assets, and `Editor/*.cs` plus
the other `Tests/Unity/*.cs` into Assets/Editor. Provide the standard Unity UI
assembly from the local Unity installation/player. Run batch mode with
`-executeMethod StreamingSuite.Run`; the harness exits and writes
`unity-test-result.txt`. These fixtures create their own tiny test images only.
Do not copy the test sources into the real game project.

`validate_artwork.py --baseline DATA --player DATA --report REPORT --output JSON`
uses UnityPy to compare compressed texture payloads and authored sprite geometry,
check resident leakage, mesh duplication, quality and FAT32 limits. It fails for
missing or ambiguous comparisons. It is read-only; its private reports must not
be uploaded with the game assets.
