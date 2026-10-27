#include "bootstrap/source/tcg_prepare.h"
#include <errno.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
int main(int argc, char **argv) {
    TcgPrepareOptions options = {0};
    if (argc != 5 && argc != 7) {
        fprintf(stderr, "usage: host_prepare PAYLOAD MANIFEST STAGE DEST [--fail-after BYTES]\n");
        return 2;
    }
    if (argc == 7) {
        char *end;
        errno = 0;
        options.fail_after_bytes = strtoull(argv[6], &end, 10);
        if (strcmp(argv[5], "--fail-after") || errno || !*argv[6] || *end || argv[6][0] == '-') return 2;
    }
    if (tcg_prepare(argv[1], argv[2], argv[3], argv[4], &options)) {
        fprintf(stderr, "%s\n", tcg_prepare_error());
        return 1;
    }
    puts("ready");
    return 0;
}
