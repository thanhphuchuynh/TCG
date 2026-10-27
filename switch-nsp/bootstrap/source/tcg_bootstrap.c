/* First-run preparation for the private TCG compatibility package. */
#include <switch.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <errno.h>
#include <sys/stat.h>
#include "tcg_prepare.h"

/* Release AM sessions instead of scheduling NSO process exit before NRO handoff. */
u32 __nx_applet_exit_mode = 2;
/* The custom hbloader __appInit bypasses libnx default display initialization. */
extern void __nx_win_init(void);
extern void __nx_win_exit(void);
static unsigned long long last_display;
static void show_progress(const char *path, uint64_t complete, uint64_t total, void *opaque) {
    (void)path; (void)opaque;
    if (complete < last_display + 16*1024*1024 && complete != total) return;
    last_display=complete;
    uint64_t bounded = total && complete > total ? total : complete;
    unsigned percent = total ? (unsigned)(((__uint128_t)bounded * 100) / total) : 0;
    char bar[41];
    unsigned filled = percent * 40 / 100;
    memset(bar, '#', filled);
    memset(bar + filled, '-', 40 - filled);
    bar[40] = '\0';
    printf("\x1b[7;1H[%s] %3u%%\x1b[K\x1b[8;1H%llu / %llu MiB copied\x1b[K",
           bar, percent,
           (unsigned long long)(complete/(1024*1024)),
           (unsigned long long)(total/(1024*1024)));
    consoleUpdate(NULL);
}
static void stop_with_message(const char *message) {
    printf("\x1b[10;1HPreparation stopped:\n%s\n\nExisting game files and saves were preserved.\nPress B to close.\n",message);
    Result hid_rc = hidInitialize();
    if (R_FAILED(hid_rc)) {
        printf("Controller unavailable. Use HOME to close.\n");
        while (appletMainLoop()) consoleUpdate(NULL);
        svcExitProcess();
    }
    PadState pad;
    padConfigureInput(1,HidNpadStyleSet_NpadStandard);
    padInitializeDefault(&pad);
    while (appletMainLoop()) {
        padUpdate(&pad);
        if (padGetButtonsDown(&pad)&HidNpadButton_B) break;
        consoleUpdate(NULL);
    }
    hidExit(); consoleExit(NULL); __nx_win_exit(); appletExit(); svcExitProcess();
}
void tcg_bootstrap_prepare(void) {
    Result rc=fsdevMountSdmc();
    if (R_FAILED(rc)) diagAbortWithResult(rc);
    rc=appletInitialize();
    if (R_FAILED(rc)) diagAbortWithResult(rc);
    __nx_win_init();
    PrintConsole *screen = consoleInit(NULL);
    if (!screen || !screen->consoleInitialised)
        diagAbortWithResult(MAKERESULT(Module_HomebrewLoader, 42));
    printf("\x1b[2;2HTCG Card Shop Simulator\n\nPreparing game files for the first launch.\nThis can take a few minutes. Keep the SD card inserted.\n\x1b[7;1HChecking installation...\x1b[K");
    consoleUpdate(NULL);
    rc=romfsInit();
    if (R_FAILED(rc)) stop_with_message("Cannot open the installed game's bundled files.");
    if (mkdir("sdmc:/switch",0777)!=0 && errno!=EEXIST)
        stop_with_message("Cannot create the switch folder on the SD card.");
    TcgPrepareOptions options={.progress=show_progress,.opaque=NULL,.fail_after_bytes=0};
    if (tcg_prepare("romfs:/payload","romfs:/manifest.tsv", "sdmc:/switch/.tcg-compat-staging", "sdmc:/switch/wine",&options)!=0)
        stop_with_message(tcg_prepare_error());
    /* A one-shot handoff selects this game without changing global Autorun preferences. */
    FILE *handoff=fopen("sdmc:/switch/wine/run-next.txt","wb");
    const char *target="sdmc:/switch/wine/drive_c/TCG_Autorun_Test/TCG Autorun Test.exe\n";
    if (!handoff) stop_with_message("Cannot write the game launch request.");
    int ok=fwrite(target,1,strlen(target),handoff)==strlen(target);
    if (fclose(handoff)!=0 || !ok) stop_with_message("Cannot finish the game launch request.");
    printf("\x1b[9;1HStarting the compatibility runtime...\n");
    consoleUpdate(NULL);
    romfsExit(); consoleExit(NULL); __nx_win_exit(); appletExit(); fsdevUnmountAll();
}
