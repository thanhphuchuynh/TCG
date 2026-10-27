#ifndef TCG_PREPARE_H
#define TCG_PREPARE_H
#include <stdint.h>
typedef struct {
    void (*progress)(const char *relative, uint64_t complete, uint64_t total, void *opaque);
    void *opaque;
    uint64_t fail_after_bytes;
} TcgPrepareOptions;
int tcg_prepare(const char *payload_root, const char *manifest_path,
                const char *stage_root, const char *destination_root,
                const TcgPrepareOptions *options);
const char *tcg_prepare_error(void);
#endif
