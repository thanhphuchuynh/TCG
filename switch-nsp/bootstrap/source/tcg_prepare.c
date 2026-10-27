#define _POSIX_C_SOURCE 200809L
#include "tcg_prepare.h"
#include <errno.h>
#include <fcntl.h>
#include <inttypes.h>
#include <stdarg.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/stat.h>
#include <unistd.h>
#ifdef TCG_HOST
#include <CommonCrypto/CommonDigest.h>
typedef CC_SHA256_CTX Hash;
#define hash_init(h) CC_SHA256_Init(h)
#define hash_update(h,p,n) CC_SHA256_Update(h,p,(CC_LONG)(n))
#define hash_final(h,p) CC_SHA256_Final(p,h)
#else
#include <switch.h>
typedef Sha256Context Hash;
#define hash_init(h) sha256ContextCreate(h)
#define hash_update(h,p,n) sha256ContextUpdate(h,p,n)
#define hash_final(h,p) sha256ContextGetHash(h,p)
#endif

#define PATH_CAP 4096
#define LINE_CAP (PATH_CAP + 128)
#define OWNER ".tcg-stage-owner"
#define READY ".tcg-compat-installed"
static char error_text[512];
static unsigned char buffer[256 * 1024];
typedef struct { char hash[65], relative[PATH_CAP]; uint64_t size; } Entry;
typedef struct { char id[65]; uint64_t bytes, count; } Manifest;

const char *tcg_prepare_error(void) { return error_text; }
static int fail(const char *format, ...) {
    va_list ap;
    va_start(ap, format); vsnprintf(error_text, sizeof(error_text), format, ap); va_end(ap);
    return -1;
}
static int hex64(const char *s) {
    if (strlen(s) != 64) return 0;
    for (int i = 0; i < 64; i++) if (!((s[i] >= '0' && s[i] <= '9') || (s[i] >= 'a' && s[i] <= 'f'))) return 0;
    return 1;
}
static int number(const char *s, uint64_t *out) {
    if (!*s) return -1;
    uint64_t n = 0;
    for (; *s; s++) {
        if (*s < '0' || *s > '9' || n > (UINT64_MAX - (unsigned)(*s - '0')) / 10) return -1;
        n = n * 10 + (unsigned)(*s - '0');
    }
    *out = n; return 0;
}
static int relative_ok(const char *s) {
    if (!*s || *s == '/' || strchr(s, '\\') || strchr(s, ':') || strchr(s, '\r')) return 0;
    while (*s) {
        const char *end = strchr(s, '/');
        size_t n = end ? (size_t)(end - s) : strlen(s);
        if (!n || (n == 1 && *s == '.') || (n == 2 && !strncmp(s, "..", 2)) ||
            (n >= 5 && !strncmp(s, ".tcg-", 5))) return 0;
        if (!end) break;
        s = end + 1;
        if (!*s) return 0;
    }
    return 1;
}
static int join(char *out, const char *root, const char *rel) {
    int n = snprintf(out, PATH_CAP, "%s/%s", root, rel);
    return n < 0 || n >= PATH_CAP ? fail("Path too long") : 0;
}
/* Check every component below the selected root, refusing symlinks. Missing
 * directories may be created only within an already owned staging root. */
static int checked_path(char *out, const char *root, const char *rel, int create) {
    if (join(out, root, rel)) return -1;
    struct stat st;
    if (lstat(root, &st) || !S_ISDIR(st.st_mode)) return fail("Unsafe or missing root: %s", root);
    for (char *p = out + strlen(root) + 1; ; p++) {
        if (*p != '/' && *p) continue;
        char saved = *p; *p = 0;
        int rc = lstat(out, &st);
        if (rc && errno != ENOENT) { *p = saved; return fail("Cannot inspect path: %s", out); }
        if (rc && saved && create) {
            if (mkdir(out, 0777) || lstat(out, &st)) { *p = saved; return fail("Cannot create directory: %s", out); }
            rc = 0;
        }
        if (!rc && (S_ISLNK(st.st_mode) || (saved && !S_ISDIR(st.st_mode)) || (!saved && !S_ISREG(st.st_mode)))) {
            *p = saved; return fail("Unsafe path: %s", out);
        }
        *p = saved;
        if (!saved) return 0;
    }
}
static int sync_fd(int fd) {
    if (fsync(fd) && errno != ENOSYS && errno != EINVAL) return fail("Cannot sync data: %s", strerror(errno));
    return 0;
}
static int sync_dir(const char *path) {
#ifdef TCG_HOST
    int fd = open(path, O_RDONLY);
    if (fd < 0) return fail("Cannot open directory for sync: %s", path);
    int rc = sync_fd(fd);
    if (close(fd) && !rc) rc = fail("Cannot close directory: %s", path);
    return rc;
#else
    /* libnx fsdev open() opens FsFile handles, never directories. Each file
     * is flushed with fsync (fsFileFlush) before its checked close/rename.
     * SD metadata operations use the FS server; there is no directory fsync. */
    (void)path;
    return 0;
#endif
}
static int marker_matches(const char *root, const char *name, const char *id) {
    char path[PATH_CAP], data[67];
    if (checked_path(path, root, name, 0)) return 0;
    FILE *f = fopen(path, "rb");
    if (!f) return 0;
    size_t n = fread(data, 1, sizeof(data), f);
    int bad = ferror(f); fclose(f);
    return !bad && (n == 64 || (n == 65 && data[64] == '\n')) && !memcmp(data, id, 64);
}
static int write_marker(const char *root, const char *name, const char *id) {
    char path[PATH_CAP];
    if (checked_path(path, root, name, 0)) return -1;
    int fd = open(path, O_WRONLY | O_CREAT | O_EXCL, 0666);
    if (fd < 0) return fail("Cannot create marker: %s", path);
    char data[66]; snprintf(data, sizeof(data), "%s\n", id);
    int rc = write(fd, data, 65) == 65 ? sync_fd(fd) : fail("Cannot write marker: %s", path);
    if (close(fd) && !rc) rc = fail("Cannot close marker");
    return rc ? rc : sync_dir(root);
}
static int line(FILE *f, char *out) {
    if (!fgets(out, LINE_CAP, f)) return ferror(f) ? fail("Cannot read manifest") : 0;
    size_t n = strlen(out);
    if (!n || out[n - 1] != '\n') return fail("Manifest line too long or unterminated");
    out[n - 1] = 0; return 1;
}
static char *field(char *s) {
    char *p = strchr(s, '\t');
    if (p) *p++ = 0;
    return p;
}
static int header(FILE *f, Manifest *m) {
    char row[LINE_CAP];
    if (line(f, row) != 1) return fail("Missing manifest header");
    char *id = field(row), *bytes = id ? field(id) : NULL, *count = bytes ? field(bytes) : NULL;
    if (strcmp(row, "TCG-COMPAT-1") || !id || !bytes || !count || !hex64(id) ||
        number(bytes, &m->bytes) || number(count, &m->count)) return fail("Invalid manifest header");
    memcpy(m->id, id, 65); return 0;
}
static int entry(FILE *f, Entry *e) {
    char row[LINE_CAP]; int rc = line(f, row);
    if (rc != 1) return rc;
    char *size = field(row), *rel = size ? field(size) : NULL;
    if (!size || !rel || strchr(rel, '\t') || !hex64(row) || number(size, &e->size) ||
        e->size > UINT32_MAX || !relative_ok(rel) || strlen(rel) >= sizeof(e->relative)) return fail("Invalid manifest entry");
    memcpy(e->hash, row, 65); strcpy(e->relative, rel); return 1;
}
static int validate(FILE *f, Manifest *m) {
    Entry e; uint64_t bytes = 0, count = 0; int rc;
    if (header(f, m)) return -1;
    while ((rc = entry(f, &e)) == 1) {
        if (UINT64_MAX - bytes < e.size || count == UINT64_MAX) return fail("Manifest totals overflow");
        bytes += e.size; count++;
    }
    if (rc < 0) return -1;
    if (bytes != m->bytes || count != m->count) return fail("Manifest count or byte total mismatch");
    rewind(f); return header(f, m);
}
static int matches(const char *path, const Entry *e, int hash) {
    struct stat st;
    if (lstat(path, &st) || !S_ISREG(st.st_mode) || (uint64_t)st.st_size != e->size) return 0;
    if (!hash) return 1;
    FILE *f = fopen(path, "rb"); if (!f) return 0;
    Hash h; hash_init(&h); size_t n;
    while ((n = fread(buffer, 1, sizeof(buffer), f))) hash_update(&h, buffer, n);
    int bad = ferror(f); fclose(f);
    unsigned char digest[32]; char hex[65]; hash_final(&h, digest);
    for (int i = 0; i < 32; i++) snprintf(hex + i * 2, 3, "%02x", digest[i]);
    return !bad && !strcmp(hex, e->hash);
}
static int suffix(const char *s, const char *ending) {
    size_t n = strlen(s), m = strlen(ending);
    return n >= m && !strcmp(s + n - m, ending);
}
static int immutable(const char *rel) {
    int game = !strncmp(rel, "drive_c/TCG_Autorun_Test/", 25);
    if (game && strstr(rel + 25, "_Data/")) return 1;
    if (strstr(rel, "/users/") || !strncmp(rel, "users/", 6)) return 0;
    const char *mutable[] = {".ini", ".cfg", ".conf", ".reg", ".log",
                              ".args.txt", ".keys.txt", ".wine-nx.json"};
    for (size_t i = 0; i < sizeof(mutable) / sizeof(*mutable); i++)
        if (suffix(rel, mutable[i])) return 0;
    return game || suffix(rel, ".nro") || suffix(rel, ".dll") || suffix(rel, ".exe");
}
static int copy_entry(const char *payload, const char *stage, const Entry *e,
                      const TcgPrepareOptions *o, uint64_t *done, uint64_t *written, uint64_t total) {
    char source[PATH_CAP], dest[PATH_CAP], tmp[PATH_CAP], reltmp[PATH_CAP];
    if (checked_path(source, payload, e->relative, 0) || checked_path(dest, stage, e->relative, 1)) return -1;
    if (matches(dest, e, 1)) {
        *done += e->size;
        if (o && o->progress) o->progress(e->relative, *done, total, o->opaque);
        return 0;
    }
    strcpy(reltmp, e->relative);
    char *base = strrchr(reltmp, '/');
    size_t prefix = base ? (size_t)(base + 1 - reltmp) : 0;
    if (prefix + sizeof(".tcg-copy.tmp") > sizeof(reltmp)) return fail("Temporary path too long");
    strcpy(reltmp + prefix, ".tcg-copy.tmp");
    if (checked_path(tmp, stage, reltmp, 1)) return fail("Unsafe temporary path");
    FILE *in = fopen(source, "rb"), *out = NULL;
    if (!in) return fail("Cannot read payload: %s", e->relative);
    out = fopen(tmp, "wb");
    if (!out) { fclose(in); return fail("Cannot create staged file: %s", e->relative); }
    Hash h; hash_init(&h); uint64_t size = 0; int rc = 0; size_t n;
    while ((n = fread(buffer, 1, sizeof(buffer), in))) {
        if (o && o->fail_after_bytes && n > o->fail_after_bytes - *written) n = (size_t)(o->fail_after_bytes - *written);
        if (n && fwrite(buffer, 1, n, out) != n) { rc = fail("Cannot write staged file: %s", e->relative); break; }
        hash_update(&h, buffer, n); size += n; *written += n; *done += n;
        if (o && o->progress) o->progress(e->relative, *done, total, o->opaque);
        if (o && o->fail_after_bytes && *written >= o->fail_after_bytes) { rc = fail("Simulated interruption after copied bytes"); break; }
        if (size > e->size) { rc = fail("Payload size mismatch: %s", e->relative); break; }
    }
    if (ferror(in) && !rc) rc = fail("Cannot read payload: %s", e->relative);
    if (fclose(in) && !rc) rc = fail("Cannot close payload");
    unsigned char digest[32]; char hex[65]; hash_final(&h, digest);
    for (int i = 0; i < 32; i++) snprintf(hex + i * 2, 3, "%02x", digest[i]);
    if (!rc && (size != e->size || strcmp(hex, e->hash))) rc = fail("Payload hash or size mismatch: %s", e->relative);
    if (!rc && (fflush(out) || sync_fd(fileno(out)))) rc = fail("Cannot sync staged file: %s", e->relative);
    if (fclose(out) && !rc) rc = fail("Cannot close staged file");
    /* FS rename does not replace files on Switch. Remove only this invalid
     * manifest entry inside our owned stage, after its replacement is durable.
     * An interruption here leaves the stage resumable; the destination is absent. */
    if (!rc && checked_path(dest, stage, e->relative, 0)) rc = -1;
    if (!rc) {
        struct stat st;
        if (!lstat(dest, &st)) {
            if (!S_ISREG(st.st_mode) || matches(dest, e, 1)) rc = fail("Staged path changed during preparation: %s", e->relative);
            else if (unlink(dest)) rc = fail("Cannot replace invalid staged file: %s", e->relative);
        } else if (errno != ENOENT) rc = fail("Cannot inspect staged replacement: %s", e->relative);
    }
    if (!rc && rename(tmp, dest)) rc = fail("Cannot finish staged file: %s", e->relative);
    if (!rc) {
        char *slash = strrchr(dest, '/');
        if (slash) { *slash = 0; rc = sync_dir(dest); }
    }
    return rc;
}
int tcg_prepare(const char *payload_root, const char *manifest_path,
                const char *stage_root, const char *destination_root, const TcgPrepareOptions *options) {
    error_text[0] = 0;
    FILE *f = fopen(manifest_path, "rb");
    if (!f) return fail("Cannot open compatibility manifest");
    Manifest m; Entry e; struct stat st; int rc = -1, state;
    if (validate(f, &m)) goto end;
    state = lstat(destination_root, &st);
    if (!state) {
        if (!S_ISDIR(st.st_mode) || !marker_matches(destination_root, READY, m.id)) { fail("Existing destination is not owned by this payload"); goto end; }
        while ((state = entry(f, &e)) == 1) {
            char path[PATH_CAP];
            if (immutable(e.relative) && (checked_path(path, destination_root, e.relative, 0) || !matches(path, &e, 0))) {
                fail("Installed immutable file missing or wrong size: %s", e.relative); goto end;
            }
        }
        rc = state < 0 ? -1 : 0; goto end;
    }
    if (errno != ENOENT) { fail("Cannot inspect destination"); goto end; }
    state = lstat(stage_root, &st);
    if (!state) {
        if (!S_ISDIR(st.st_mode) || !marker_matches(stage_root, OWNER, m.id)) { fail("Existing stage is not owned by this payload"); goto end; }
    } else if (errno != ENOENT || mkdir(stage_root, 0777) || write_marker(stage_root, OWNER, m.id)) {
        if (!error_text[0]) fail("Cannot create owned staging directory"); goto end;
    }
    uint64_t done = 0, written = 0;
    while ((state = entry(f, &e)) == 1) if (copy_entry(payload_root, stage_root, &e, options, &done, &written, m.bytes)) goto end;
    if (state < 0) goto end;
    rewind(f);
    if (validate(f, &m)) goto end;
    while ((state = entry(f, &e)) == 1) {
        char path[PATH_CAP];
        if (checked_path(path, stage_root, e.relative, 0) || !matches(path, &e, 1)) { fail("Staged file failed final verification: %s", e.relative); goto end; }
    }
    if (state < 0) goto end;
    if (!marker_matches(stage_root, READY, m.id) && write_marker(stage_root, READY, m.id)) goto end;
    /* A second absence check avoids replacing an existing empty wine folder. */
    if (!lstat(destination_root, &st) || errno != ENOENT) { fail("Destination appeared during preparation"); goto end; }
    if (rename(stage_root, destination_root)) { fail("Cannot activate prepared payload: %s", strerror(errno)); goto end; }
    char parent[PATH_CAP];
    if (strlen(destination_root) >= sizeof(parent)) { fail("Destination path too long"); goto end; }
    strcpy(parent, destination_root);
    char *slash = strrchr(parent, '/');
    if (slash) { if (slash == parent) slash[1] = 0; else *slash = 0; } else strcpy(parent, ".");
    rc = sync_dir(parent);
end:
    if (fclose(f) && !rc) rc = fail("Cannot close manifest");
    return rc;
}
