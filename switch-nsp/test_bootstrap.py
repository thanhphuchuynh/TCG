"""Exercise the real wrapper with a minimal libnx service model on the host."""
from pathlib import Path
import subprocess,tempfile,unittest
ROOT=Path(__file__).resolve().parent
HEADER=r'''
#pragma once
#include <stdint.h>
#include <stdbool.h>
typedef uint32_t u32; typedef uint64_t u64; typedef uint64_t Handle; typedef uint32_t Result;
#define R_FAILED(x) ((x)!=0)
#define MAKERESULT(m,d) ((m)|((d)<<9))
#define Module_HomebrewLoader 347
#define HidNpadStyleSet_NpadStandard 1
#define HidNpadButton_B 1
typedef struct { int dummy; } PadState;
typedef struct { bool consoleInitialised; } PrintConsole;
Result fsdevMountSdmc(void); Result appletInitialize(void); Result hidInitialize(void); Result romfsInit(void);
void diagAbortWithResult(Result); void svcExitProcess(void); void consoleUpdate(PrintConsole*); PrintConsole *consoleInit(PrintConsole*);
void consoleExit(PrintConsole*); void appletExit(void); void hidExit(void); void romfsExit(void); void fsdevUnmountAll(void);
void padConfigureInput(int,int); void padInitializeDefault(PadState*); void padUpdate(PadState*); u64 padGetButtonsDown(PadState*); bool appletMainLoop(void);
'''
DRIVER=r'''
#include <switch.h>
#include <stdio.h>
#include <stdlib.h>
#include <sys/stat.h>
#include "tcg_prepare.h"
static int window, console, bad_console;
static PrintConsole screen;
static void require(int ok) { if(!ok) exit(77); }
Result fsdevMountSdmc(void){return 0;} Result appletInitialize(void){return 0;}
Result hidInitialize(void){return 0;} Result romfsInit(void){return 0;}
void __nx_win_init(void){require(!window);window=1;}
void __nx_win_exit(void){require(window&&!console);window=0;}
PrintConsole *consoleInit(PrintConsole*p){(void)p;screen.consoleInitialised=window&&!bad_console;console=screen.consoleInitialised;return &screen;}
int tcg_test_printf(const char *s,...){(void)s;require(window&&console);return 0;}
void consoleUpdate(PrintConsole*p){(void)p;require(window&&console);}
void consoleExit(PrintConsole*p){(void)p;require(window&&console);console=0;}
void appletExit(void){require(!window&&!console);} void hidExit(void){} void romfsExit(void){}
void fsdevUnmountAll(void){require(!window&&!console);}
void diagAbortWithResult(Result r){(void)r;exit(42);} void svcExitProcess(void){exit(43);}
void padConfigureInput(int a,int b){(void)a;(void)b;} void padInitializeDefault(PadState*p){(void)p;}
void padUpdate(PadState*p){(void)p;} u64 padGetButtonsDown(PadState*p){(void)p;return 1;} bool appletMainLoop(void){return true;}
int tcg_prepare(const char*a,const char*b,const char*c,const char*d,const TcgPrepareOptions*o){(void)a;(void)b;(void)c;(void)d;(void)o;require(window&&console);return 0;}
const char *tcg_prepare_error(void){return "test error";}
void tcg_bootstrap_prepare(void);
int main(int argc,char **argv){(void)argv;bad_console=argc>1;mkdir("sdmc:",0777);mkdir("sdmc:/switch",0777);mkdir("sdmc:/switch/wine",0777);tcg_bootstrap_prepare();require(!window&&!console);return 0;}
'''
class BootstrapTests(unittest.TestCase):
 @classmethod
 def setUpClass(cls):
  cls.tmp=tempfile.TemporaryDirectory();cls.root=Path(cls.tmp.name)
  (cls.root/'switch.h').write_text(HEADER);(cls.root/'driver.c').write_text(DRIVER)
  subprocess.run(['clang','-std=c11','-Wall','-Wextra','-Werror','-Dprintf=tcg_test_printf','-I'+str(cls.root),'-I'+str(ROOT/'bootstrap/source'),str(ROOT/'bootstrap/source/tcg_bootstrap.c'),str(cls.root/'driver.c'),'-o',str(cls.root/'bootstrap')],check=True)
 @classmethod
 def tearDownClass(cls):cls.tmp.cleanup()
 def test_initializes_display_before_print_and_releases_it_before_handoff(self):
  p=subprocess.run([str(self.root/'bootstrap')],cwd=self.root)
  self.assertEqual(p.returncode,0,'Printed without an initialized display or failed cleanup ordering')
 def test_failed_console_does_not_print_into_null_framebuffer(self):
  p=subprocess.run([str(self.root/'bootstrap'),'console-fails'],cwd=self.root)
  self.assertEqual(p.returncode,42,'Expected controlled diagnostic abort instead of null-framebuffer printing')
if __name__=='__main__':unittest.main()
