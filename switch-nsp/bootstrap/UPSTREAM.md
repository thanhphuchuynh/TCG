Based on switchbrew/nx-hbloader v2.4.5 (MIT), https://github.com/switchbrew/nx-hbloader/tree/v2.4.5 . The original MIT license is preserved in LICENSE.md.

Changes: a checksummed staged first-run payload preparation step runs before the loader's main heap is assigned; loader-private libc heap is 16 MiB for preparation UI/filesystem work; the initial NRO is Autorun; returning without a next-load request exits the title. The original loader's NRO mapping/trampoline/homebrew ABI remains intact. NPDM requests the normal 39-bit homebrew application address space for this private title.

Only this source is compiled by GitHub Actions. Game files and console keys are never uploaded to CI.
