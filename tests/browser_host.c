#define _POSIX_C_SOURCE 200809L
#include "dnd_managed.h"
#include "dnd_input.h"
#include "dnd_model.h"
#include "dnd_storage.h"
#include "dnd_draw.h"
#include "dnd_async.h"
#include <assert.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
static unsigned polls, errors;
static bool inspected;
static bool error_mode;
extern intptr_t dnd_value_aot_entry(DndManagedHeap *heap);
void dnd_console_write_line(const char *message) { errors++; fprintf(stderr,"Browser error: %s\n",message); }
void dnd_input_backend_init(void) {}
void dnd_input_backend_shutdown(void) {}
bool dnd_input_backend_rumble(int port,uint32_t time) { (void)port;(void)time;return false; }
void dnd_input_backend_stop_rumble(int port) { (void)port; }
void dnd_input_backend_sample(DndInputSample pads[4]) {
  unsigned step = polls++;
  pads[0].connected = true;
  if (error_mode) {
    if (step == 1 || step == 3) pads[0].held = 0x100;
    if (step == 5) pads[0].held = 0x400; // retry the failed recipe
    if (step == 7) pads[0].held = 0x200; // return to sources
    if (step >= 9) pads[0].held = 0x1000;
    return;
  }
  // Release between each press. First choose extracted files, then asset.psoasset.
  if (step == 1 || step == 3) pads[0].held = 0x100;
  if (step >= 4 && step < 15) {
    assert(dnd_model_live_handles() == 2);
    inspected = true;
    pads[0].stick_x = 32; pads[0].cstick_y = 24; pads[0].trigger_r = 90;
    if (step == 5) pads[0].held = 0x8; // select motion
    if (step == 7) pads[0].held = 0x100; // pause
    if (step == 9) pads[0].held = 0x2; // seek right
    if (step == 11) pads[0].held = 0x400; // speed
    if (step == 13) pads[0].held = 0x800; // reset framing
  }
  if (step == 15) pads[0].held = 0x200; // back to files
  if (step >= 17) pads[0].held = 0x1000; // exit
}
int main(int argc,char **argv) {
  assert(argc >= 2 && !setenv("DND_STORAGE_ROOT",argv[1],1));
  error_mode = argc > 2 && !strcmp(argv[2],"--error");
  static unsigned char memory[256*1024]; DndManagedHeap heap;
  dnd_managed_heap_init(&heap,memory,sizeof(memory)); dnd_gc_set_stress(argc > 2);
  intptr_t result = dnd_value_aot_entry(&heap);
  if (result || dnd_exception_pending()) {
    fprintf(stderr,"Browser result=%ld error=%s\n",(long)result,dnd_exception_message());
    return 1;
  }
  if (error_mode) assert(!inspected && errors == 2 && polls == 10);
  else assert(inspected && !errors && polls == 18);
  assert(!dnd_model_live_handles() && !dnd_fs_open_handles() && !dnd_draw_live());
  assert(!dnd_storage_is_mounted(6) && !dnd_async_running());
  printf("Browser AOT %s passed; collections=%zu\n",error_mode?"persistent error/retry/back/exit":"source/list/load/orbit/playback/back/exit",heap.collections);
  return 0;
}
