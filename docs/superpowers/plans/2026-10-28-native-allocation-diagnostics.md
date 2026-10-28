# Native allocation diagnostics plan

Continuation of explicitly approved runtime investigation; observation only.
Game/quality/GC/context/asset semantics remain unchanged. FEX upstream source and
its patches are excluded by its AGENTS.md. Only own Autorun host bridge/mapping
instrumentation is allowed. No fullgame release or physicalSwitchclaim.

Evidence: verified dee83bc streamed229finish+381art+252Mesh 4GB stillfails37-39%.
Demandfilemappingexistingoption6views33chunksdidnotfix; restoreddefaults.
Opt-inUnitycounterprobe assetsemanticsidentical, records atfailure allocated795MB,
reserved934MB, Texturecurrent677MB, Monoheap47MB, graphicsdriver0(unavailable).
Nativeheap~3GB. Theseare differentcounters andcannot simplybe subtracted oradded.
Needattribute nativebackings/JIT arenas before semanticfix oranothercatalogue.

1 RED/GREEN own fex_jit.c/h host diagnostic snapshot: arena capacities/requested
   mapbytes/buffer count, thread-safe nonblocking read, no allocation/cachechanges.
   Test multiplealiases countedonce andclose/release/concurrentlifetime.
2 RED/GREEN own generic backing summary withdedup/anon/file bytes/reservations,
   useexistingmappinglock/trylock, no allocations inreport/no semanticchanges.
   Snapshot physicalbackingbytes vslogicalmappingbytes separately. Report pool
   spare separately, not an additiveapplicationRAMclaim.
3 Wirebounded diagnostic output intonativeprogress, preserveallABIoffsets and
   existingruntime/PE binaries; source-onlyincrementalpatch after existingpatches.
   Runhosttests +fullSDKCI/ARM64dispatcher. Installonlyprovenbinary, preserveold.
4 Replayexact4GBdiagnosticplayer withsamegraphics; captureNativecounterattribution.
   No successclaimbeforemain shop/pack/album/save-load/headroom gates.

Counter contract: anon/file/section requested bytes describe outstanding backing
requests, not exact malloc capacity. Dedicated allocations may round to 2 MiB;
allocator overhead and uninstrumented allocations remain unclassified. Section
requests include descriptor-only memory; alias lengths and VA reservations are
separate and must not be summed into RAM. JIT arena capacity counts shared RX/RW
storage once; JIT buffer capacity is a subset, not emitted code size.
