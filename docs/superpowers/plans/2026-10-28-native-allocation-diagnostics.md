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

First 4 GiB replay: title passed, load failed33% with ENOMEM (24.0625 MiB
backing request) despite120 MiB total free. JIT stayed112 MiB arenas/48 MiB
buffers, not the unexplained bulk. Anonymous requests1495.6 MiB atfailure.
Refine observation using existing tested native_heap statistics, including largest
aligned/newlib holes and shared gap, before changing allocator semantics. These
hole figures are snapshots, not guarantees that a particular alignment fits.

Refined replay: after one separately preserved startup wait stall, unchanged
repeat reached title then39% load/38% Player and ENOMEM. Post-failure aligned
heap used2991.5 MiB, free91.125 MiB/largest16.125 MiB; newlibfree18.601 MiB/
largest2.744 MiB, gap0.688 MiB. These snapshots support fragmentation/pressure,
but are not an instantaneous failing-allocation trace. No allocator change yet.
Next separate physical-heap source-page attributes using bounded svcQueryMemory
scan. DeviceMapped/Borrowed/IPC fields overlap; exclude virtual aliases outside
physical heap and non-Heap types, mark partial queries explicitly. This measures
registered source pages rather than live texture bytes or a complete RAM total.
The device scan's complete flag means traversal reached the heap end; concurrent
mapping changes mean this is not an atomic snapshot of all attributes.

All three native diagnostic revisions passed the SDK, sanitizer observer/bridge,
and ARM64 dispatcher jobs. The final device metadata observer reaches title, but
Ryujinx's NvMap allocation implementation does not set the DeviceMapped kernel
attribute. Its zero is therefore not usable as a GPU allocation measurement here.
Existing emulator ServiceNv logs at title record219 creates/219 successful allocs,
one deleted map and479.5 MiB of live map requests. These are logical requests,
not a union of physical source pages. Gameplay-side NvMap measurements remain
pending because macOS locked and blocks desktop input. Temporary emulator debug
logging was restored. 4 GiB gameplay acceptance remains unmet; no fullgame release.
