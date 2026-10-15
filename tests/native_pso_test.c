#define _POSIX_C_SOURCE 200809L
#include "dnd_model_internal.h"
#include "dnd_storage.h"
#include <assert.h>
#include <math.h>
#include <stdio.h>
#include <stdlib.h>

static DndManagedHeap heap;
static unsigned checks;
#define CHECK(x) do { assert(x); checks++; } while (0)
static DndString *text(const char *path) { return dnd_string_from_utf8(&heap, path); }
static int load(const char *path, const char *texture, const char *motion, int budget) {
  return dnd_model_begin_pso(text(path), texture ? text(texture) : NULL,
      motion ? text(motion) : NULL, motion ? 30 : 0, 16, budget);
}
static void clean(void) {
  CHECK(!dnd_model_live_handles());
  CHECK(!dnd_fs_open_handles());
  CHECK(!dnd_exception_pending());
}
int main(int argc, char **argv) {
  CHECK(argc == 2);
  CHECK(!setenv("DND_STORAGE_ROOT", argv[1], 1));
  static unsigned char memory[256 * 1024];
  dnd_managed_heap_init(&heap, memory, sizeof(memory));
  CHECK(dnd_storage_mount(2));
  const char *good[] = {"sd:/plain.gj", "sd:/mixed.gj", "sd:/strip.gj", "sd:/fan.gj", "sd:/rotated.gj"};
  for (unsigned i = 0; i < sizeof(good) / sizeof(*good); i++) {
    int model = load(good[i], NULL, NULL, 8 * 1024 * 1024);
    CHECK(model);
    DndModel *m = dnd_model_find(model, false);
    CHECK(m->nodes == 1 && m->meshes == 1 && m->textures == 0);
    const unsigned triangle[] = {0, 2, 1};
    const unsigned strip[] = {0, 2, 1, 2, 3, 1};
    const unsigned fan[] = {0, 2, 1, 0, 3, 2};
    const unsigned *expected = i == 2 ? strip : i == 3 ? fan : triangle;
    unsigned count = i == 2 || i == 3 ? 6 : 3;
    CHECK(m->mesh[0].indices == count);
    for (unsigned n = 0; n < count; n++) CHECK(dnd_model_u32(m->mesh[0].index + n * 4) == expected[n]);
    if (i == 1) CHECK(fabsf(dnd_model_f32(m->mesh[0].data + 80 + 24) - 1) < .001f);
    int instance = dnd_model_instance(model, 4 * 1024 * 1024); CHECK(instance);
    if (i == 4) {
      CHECK(!dnd_model_evaluate(dnd_instance_find(instance)));
      CHECK(fabsf(dnd_instance_find(instance)->global[0]) < .001f);
      CHECK(fabsf(dnd_instance_find(instance)->global[1] - 1) < .001f);
    }
    CHECK(!dnd_model_instance_dispose(instance)); CHECK(!dnd_model_dispose(model)); clean();
  }
  int model = load("sd:/textured.gj", "sd:/preview.gvm", "sd:/move.njm", 8 * 1024 * 1024);
  CHECK(model);
  DndModel *m = dnd_model_find(model, false);
  CHECK(m->textures == 1 && m->clips == 1);
  const unsigned char *pixels = m->texture[0].data;
  CHECK(pixels[0] == 255 && pixels[1] == 230 && pixels[32] == 40 && pixels[33] == 10);
  int instance = dnd_model_instance(model, 4 * 1024 * 1024); CHECK(instance);
  CHECK(!dnd_model_play(instance, 0, 0, 0)); CHECK(!dnd_model_control(instance, 0, .5f));
  CHECK(!dnd_model_evaluate(dnd_instance_find(instance)));
  CHECK(fabsf(dnd_instance_find(instance)->global[12] - 1) < .001f);
  CHECK(!dnd_model_instance_dispose(instance)); CHECK(!dnd_model_dispose(model)); clean();
  model = dnd_model_begin_pso_ep3(text("sd:/preview.bml#preview.gj"), NULL, NULL, 30, 16, 8 * 1024 * 1024, 0);
  CHECK(model); CHECK(dnd_model_find(model, false)->textures == 1);
  CHECK(!dnd_model_dispose(model)); clean();
  CHECK(!load("sd:/textured.gj", NULL, NULL, 8 * 1024 * 1024)); clean();
  CHECK(!load("sd:/textured.gj", "sd:/truncated.gvm", NULL, 8 * 1024 * 1024)); clean();
  CHECK(!load("sd:/plain.gj", NULL, NULL, 1024)); clean();
  const char *bad[] = {"sd:/bad-pointer.gj", "sd:/bad-index.gj", "sd:/nan.gj", "sd:/bad-primitive.gj", "sd:/short-stream.gj", "sd:/cycle.gj"};
  for (unsigned i = 0; i < sizeof(bad) / sizeof(*bad); i++) {
    CHECK(!load(bad[i], NULL, NULL, 8 * 1024 * 1024)); clean();
  }
  for (int i = 0; i < 8; i++) {
    char path[64]; snprintf(path, sizeof(path), "sd:/short-%d.gj", i);
    CHECK(!load(path, NULL, NULL, 8 * 1024 * 1024)); clean();
  }
  dnd_storage_unmount(2); clean();
  printf("%u SDK PSO format/texture/motion/cleanup assertions passed\n", checks);
  return 0;
}
