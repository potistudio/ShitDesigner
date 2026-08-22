#include "shitdesigner_hap.h"

#include <limits.h>
#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#if defined(_WIN32)
#  include <windows.h>
typedef CRITICAL_SECTION sd_mutex;
static void sd_mutex_init(sd_mutex* m) { InitializeCriticalSection(m); }
static void sd_mutex_lock(sd_mutex* m) { EnterCriticalSection(m); }
static void sd_mutex_unlock(sd_mutex* m) { LeaveCriticalSection(m); }
static void sd_mutex_destroy(sd_mutex* m) { DeleteCriticalSection(m); }
#else
#  include <pthread.h>
typedef pthread_mutex_t sd_mutex;
static void sd_mutex_init(sd_mutex* m) { pthread_mutex_init(m, NULL); }
static void sd_mutex_lock(sd_mutex* m) { pthread_mutex_lock(m); }
static void sd_mutex_unlock(sd_mutex* m) { pthread_mutex_unlock(m); }
static void sd_mutex_destroy(sd_mutex* m) { pthread_mutex_destroy(m); }
#endif

typedef struct sd_atom {
    size_t offset;
    size_t header;
    size_t size;
    char type[5];
} sd_atom;

typedef struct sd_sample {
    uint64_t offset;
    uint32_t size;
    uint64_t ticks;
    uint32_t duration;
} sd_sample;

typedef struct sd_context {
    uint8_t* file;
    size_t file_size;
    int codec;
    uint32_t width;
    uint32_t height;
    uint32_t timescale;
    uint64_t duration_ticks;
    sd_sample* samples;
    size_t sample_count;
    size_t current;
    int playing;
    int loop;
    int demanded;
    double speed;
    uint8_t* rgba;
    size_t rgba_bytes;
    uint8_t* plane_blocks[2];
    size_t plane_bytes[2];
    uint32_t plane_format[2];
    int frame_leased;
    sd_mutex mutex;
} sd_context;

static uint32_t be16(const uint8_t* p) { return ((uint32_t)p[0] << 8) | p[1]; }
static uint32_t be32(const uint8_t* p) { return ((uint32_t)p[0] << 24) | ((uint32_t)p[1] << 16) | ((uint32_t)p[2] << 8) | p[3]; }
static uint64_t be64(const uint8_t* p) { return ((uint64_t)be32(p) << 32) | be32(p + 4); }
static uint32_t le32(const uint8_t* p) { return p[0] | ((uint32_t)p[1] << 8) | ((uint32_t)p[2] << 16) | ((uint32_t)p[3] << 24); }
static uint16_t le16(const uint8_t* p) { return (uint16_t)(p[0] | ((uint16_t)p[1] << 8)); }
static int add_ok(size_t a, size_t b, size_t limit, size_t* out) { if (b > limit || a > limit - b) return 0; *out = a + b; return 1; }

static int read_atom(const uint8_t* bytes, size_t offset, size_t available, sd_atom* out)
{
    uint64_t size;
    size_t header = 8;
    if (!bytes || !out || available < 8) return SD_HAP_INVALID;
    size = be32(bytes + offset);
    memcpy(out->type, bytes + offset + 4, 4);
    out->type[4] = 0;
    if (size == 1) {
        if (available < 16) return SD_HAP_INVALID;
        size = be64(bytes + offset + 8);
        header = 16;
    } else if (size == 0) {
        size = available;
    }
    if (size < header || size > available || size > SIZE_MAX) return SD_HAP_INVALID;
    out->offset = offset;
    out->header = header;
    out->size = (size_t)size;
    return SD_HAP_OK;
}

static int child_atom(const uint8_t* bytes, size_t file_size, const sd_atom* parent, const char* type, size_t ordinal, sd_atom* out)
{
    size_t p, end, next;
    size_t seen = 0;
    if (!parent || parent->header > parent->size || parent->size > file_size - parent->offset) return SD_HAP_INVALID;
    p = parent->offset + parent->header;
    end = parent->offset + parent->size;
    while (p < end) {
        sd_atom atom;
        if (read_atom(bytes, p, end - p, &atom) != SD_HAP_OK) return SD_HAP_INVALID;
        next = p + atom.size;
        if (strcmp(atom.type, type) == 0) {
            if (seen++ == ordinal) { *out = atom; return SD_HAP_OK; }
        }
        p = next;
    }
    return SD_HAP_INVALID;
}

static int root_atom(const uint8_t* bytes, size_t file_size, const char* type, size_t ordinal, sd_atom* out)
{
    size_t p = 0, seen = 0;
    while (p < file_size) {
        sd_atom atom;
        if (read_atom(bytes, p, file_size - p, &atom) != SD_HAP_OK) return SD_HAP_INVALID;
        if (strcmp(atom.type, type) == 0 && seen++ == ordinal) { *out = atom; return SD_HAP_OK; }
        p += atom.size;
    }
    return SD_HAP_INVALID;
}

static int hdlr_is_video(const uint8_t* bytes, const sd_atom* hdlr)
{
    size_t data;
    if (!hdlr || hdlr->size - hdlr->header < 12) return 0;
    data = hdlr->offset + hdlr->header;
    return memcmp(bytes + data + 8, "vide", 4) == 0;
}

static int find_video_track(const uint8_t* bytes, size_t file_size, const sd_atom* moov, sd_atom* out)
{
    size_t p = moov->offset + moov->header, end = moov->offset + moov->size;
    size_t track = 0;
    while (p < end) {
        sd_atom trak, mdia, hdlr;
        if (read_atom(bytes, p, end - p, &trak) != SD_HAP_OK) return SD_HAP_INVALID;
        p += trak.size;
        if (strcmp(trak.type, "trak") != 0) continue;
        if (child_atom(bytes, file_size, &trak, "mdia", 0, &mdia) != SD_HAP_OK ||
            child_atom(bytes, file_size, &mdia, "hdlr", 0, &hdlr) != SD_HAP_OK) continue;
        if (hdlr_is_video(bytes, &hdlr)) { *out = trak; return SD_HAP_OK; }
        if (++track > 1024) return SD_HAP_INVALID;
    }
    return SD_HAP_INVALID;
}

static int codec_from_fourcc(const uint8_t* p)
{
    if (memcmp(p, "Hap1", 4) == 0) return 3;
    if (memcmp(p, "Hap5", 4) == 0) return 4;
    if (memcmp(p, "HapY", 4) == 0) return 5;
    if (memcmp(p, "HapM", 4) == 0) return 6;
    return 0;
}

static int parse_stsd(const uint8_t* bytes, size_t file_size, const sd_atom* stsd, int expected_codec, uint32_t* width, uint32_t* height)
{
    size_t data, entry, entry_end;
    uint32_t entry_count, entry_size;
    int found_codec;
    if (!stsd || stsd->size - stsd->header < 8) return SD_HAP_INVALID;
    data = stsd->offset + stsd->header;
    entry_count = be32(bytes + data + 4);
    if (entry_count != 1 || stsd->size - stsd->header < 16) return SD_HAP_INVALID;
    entry = data + 8;
    if (!add_ok(entry, 8, file_size, &entry_end)) return SD_HAP_INVALID;
    entry_size = be32(bytes + entry);
    if (entry_size < 36 || entry_size > stsd->offset + stsd->size - entry) return SD_HAP_INVALID;
    found_codec = codec_from_fourcc(bytes + entry + 4);
    if (found_codec == 0 || found_codec != expected_codec) return SD_HAP_UNSUPPORTED;
    *width = be16(bytes + entry + 32);
    *height = be16(bytes + entry + 34);
    if (*width == 0 || *height == 0 || *width > 16384 || *height > 16384) return SD_HAP_INVALID;
    return SD_HAP_OK;
}

static int parse_stts(const uint8_t* bytes, const sd_atom* stts, uint32_t** counts, uint32_t** deltas, size_t* count)
{
    size_t data, bytes_needed, i;
    uint32_t n;
    if (!stts || stts->size - stts->header < 8) return SD_HAP_INVALID;
    data = stts->offset + stts->header; n = be32(bytes + data + 4);
    if (n == 0 || n > 1000000 || (size_t)n > (stts->size - stts->header - 8) / 8) return SD_HAP_INVALID;
    bytes_needed = (size_t)n * sizeof(uint32_t);
    *counts = (uint32_t*)calloc(n, sizeof(uint32_t)); *deltas = (uint32_t*)calloc(n, sizeof(uint32_t));
    if (!*counts || !*deltas) return SD_HAP_IO;
    for (i = 0; i < n; ++i) { (*counts)[i] = be32(bytes + data + 8 + i * 8); (*deltas)[i] = be32(bytes + data + 12 + i * 8); if ((*counts)[i] == 0 || (*deltas)[i] == 0) return SD_HAP_INVALID; }
    *count = n; (void)bytes_needed; return SD_HAP_OK;
}

static int parse_stsc(const uint8_t* bytes, const sd_atom* stsc, uint32_t** first, uint32_t** per_chunk, size_t* count)
{
    size_t data, i;
    uint32_t n;
    if (!stsc || stsc->size - stsc->header < 8) return SD_HAP_INVALID;
    data = stsc->offset + stsc->header; n = be32(bytes + data + 4);
    if (n == 0 || n > 1000000 || (size_t)n > (stsc->size - stsc->header - 8) / 12) return SD_HAP_INVALID;
    *first = (uint32_t*)calloc(n, sizeof(uint32_t)); *per_chunk = (uint32_t*)calloc(n, sizeof(uint32_t));
    if (!*first || !*per_chunk) return SD_HAP_IO;
    for (i = 0; i < n; ++i) { uint32_t description; (*first)[i] = be32(bytes + data + 8 + i * 12); (*per_chunk)[i] = be32(bytes + data + 12 + i * 12); description = be32(bytes + data + 16 + i * 12); if ((*per_chunk)[i] == 0 || description != 1 || (i == 0 && (*first)[i] != 1) || (i > 0 && (*first)[i] <= (*first)[i - 1])) return SD_HAP_INVALID; }
    *count = n; return SD_HAP_OK;
}

static int parse_stsz(const uint8_t* bytes, const sd_atom* stsz, uint32_t** sizes, size_t* count)
{
    size_t data, i;
    uint32_t fixed, n;
    if (!stsz || stsz->size - stsz->header < 12) return SD_HAP_INVALID;
    data = stsz->offset + stsz->header; fixed = be32(bytes + data + 4); n = be32(bytes + data + 8);
    if (n == 0 || n > 1000000 || (!fixed && (size_t)n > (stsz->size - stsz->header - 12) / 4)) return SD_HAP_INVALID;
    *sizes = (uint32_t*)calloc(n, sizeof(uint32_t)); if (!*sizes) return SD_HAP_IO;
    for (i = 0; i < n; ++i) { (*sizes)[i] = fixed ? fixed : be32(bytes + data + 12 + i * 4); if ((*sizes)[i] == 0) return SD_HAP_INVALID; }
    *count = n; return SD_HAP_OK;
}

static int parse_offsets(const uint8_t* bytes, const sd_atom* atom, uint64_t** offsets, size_t* count)
{
    size_t data, i, entry_bytes;
    uint32_t n;
    if (!atom || atom->size - atom->header < 8) return SD_HAP_INVALID;
    data = atom->offset + atom->header; n = be32(bytes + data + 4); entry_bytes = strcmp(atom->type, "co64") == 0 ? 8 : 4;
    if (n == 0 || n > 1000000 || (size_t)n > (atom->size - atom->header - 8) / entry_bytes) return SD_HAP_INVALID;
    *offsets = (uint64_t*)calloc(n, sizeof(uint64_t)); if (!*offsets) return SD_HAP_IO;
    for (i = 0; i < n; ++i) (*offsets)[i] = entry_bytes == 8 ? be64(bytes + data + 8 + i * 8) : be32(bytes + data + 8 + i * 4);
    *count = n; return SD_HAP_OK;
}

static int build_samples(const uint8_t* bytes, size_t file_size, const uint64_t* offsets, size_t offset_count, const uint32_t* sizes, size_t size_count, const uint32_t* first, const uint32_t* per_chunk, size_t stsc_count, const uint32_t* stts_counts, const uint32_t* stts_deltas, size_t stts_count, sd_sample** out_samples)
{
    size_t chunk, sample = 0, stts_i = 0, sample_in_run = 0;
    uint32_t stts_left = stts_count ? stts_counts[0] : 0;
    uint64_t ticks = 0;
    sd_sample* result;
    if (!offsets || !sizes || !first || !per_chunk || !stsc_count || !stts_count || size_count > offset_count * 1000000ULL) return SD_HAP_INVALID;
    result = (sd_sample*)calloc(size_count, sizeof(sd_sample)); if (!result) return SD_HAP_IO;
    for (chunk = 1; chunk <= offset_count && sample < size_count; ++chunk) {
        size_t map = 0, j; uint64_t offset = offsets[chunk - 1]; uint32_t count;
        for (j = 0; j < stsc_count; ++j) { if (first[j] <= chunk) map = j; else break; }
        if (first[map] > chunk || map >= stsc_count) { free(result); return SD_HAP_INVALID; }
        count = per_chunk[map];
        for (sample_in_run = 0; sample_in_run < count && sample < size_count; ++sample_in_run) {
            if (offset > file_size || sizes[sample] > file_size - (size_t)offset || stts_i >= stts_count || stts_left == 0) { free(result); return SD_HAP_INVALID; }
            if (offset > UINT64_MAX - sizes[sample] || ticks > UINT64_MAX - stts_deltas[stts_i]) { free(result); return SD_HAP_INVALID; }
            result[sample].offset = offset; result[sample].size = sizes[sample]; result[sample].ticks = ticks; result[sample].duration = stts_deltas[stts_i]; offset += sizes[sample]; ticks += stts_deltas[stts_i]; ++sample;
            if (--stts_left == 0 && ++stts_i < stts_count) stts_left = stts_counts[stts_i];
        }
    }
    if (sample != size_count || stts_i != stts_count) { free(result); return SD_HAP_INVALID; }
    *out_samples = result; return SD_HAP_OK;
}

typedef struct sd_section { size_t data, length, total; uint8_t type; } sd_section;
static int read_section(const uint8_t* bytes, size_t offset, size_t available, sd_section* section)
{
    uint32_t short_length, length; size_t header;
    if (!bytes || !section || available < 4) return SD_HAP_INVALID;
    short_length = (uint32_t)bytes[offset] | ((uint32_t)bytes[offset + 1] << 8) | ((uint32_t)bytes[offset + 2] << 16);
    header = short_length == 0 ? 8 : 4;
    if (available < header) return SD_HAP_INVALID;
    length = short_length == 0 ? le32(bytes + offset + 4) : short_length;
    if ((size_t)length > available - header) return SD_HAP_INVALID;
    section->data = offset + header; section->length = length; section->total = header + length; section->type = bytes[offset + 3]; return SD_HAP_OK;
}

static int snappy_varint(const uint8_t* src, size_t size, size_t* p, size_t* value)
{
    size_t i, v = 0;
    for (i = 0; i < 10; ++i) { uint8_t b; if (*p >= size) return SD_HAP_INVALID; b = src[(*p)++]; if (i == 9 && b > 1) return SD_HAP_INVALID; v |= (size_t)(b & 0x7F) << (i * 7); if (!(b & 0x80)) { *value = v; return SD_HAP_OK; } }
    return SD_HAP_INVALID;
}

static int snappy_decode(const uint8_t* src, size_t src_size, uint8_t* dst, size_t dst_size)
{
    size_t p = 0, output = 0, expected, i;
    if (snappy_varint(src, src_size, &p, &expected) != SD_HAP_OK || expected != dst_size) return SD_HAP_INVALID;
    while (p < src_size && output < dst_size) {
        uint8_t tag = src[p++]; size_t length, offset;
        switch (tag & 3) {
            case 0: {
                size_t n = (tag >> 2) & 0x3F;
                if (n < 60) length = n + 1;
                else { size_t bytes = n - 59; uint32_t v = 0; if (bytes > 4 || p + bytes > src_size) return SD_HAP_INVALID; for (i = 0; i < bytes; ++i) v |= (uint32_t)src[p++] << (8 * i); length = (size_t)v + 1; }
                if (p > src_size - length || output > dst_size - length) return SD_HAP_INVALID;
                memcpy(dst + output, src + p, length); p += length; output += length; break;
            }
            case 1:
                if (p >= src_size) return SD_HAP_INVALID;
                length = 4 + ((tag >> 2) & 7); offset = ((size_t)(tag & 0xE0) << 3) | src[p++];
                if (offset == 0 || offset > output || output > dst_size - length) return SD_HAP_INVALID;
                for (i = 0; i < length; ++i) { uint8_t value = dst[output - offset]; dst[output++] = value; }
                break;
            case 2:
                if (p + 2 > src_size) return SD_HAP_INVALID;
                length = 1 + ((tag >> 2) & 0x3F); offset = le16(src + p); p += 2;
                if (offset == 0 || offset > output || output > dst_size - length) return SD_HAP_INVALID;
                for (i = 0; i < length; ++i) { uint8_t value = dst[output - offset]; dst[output++] = value; }
                break;
            default:
                if (p + 4 > src_size) return SD_HAP_INVALID;
                length = 1 + ((tag >> 2) & 0x3F); offset = le32(src + p); p += 4;
                if (offset == 0 || offset > output || output > dst_size - length) return SD_HAP_INVALID;
                for (i = 0; i < length; ++i) { uint8_t value = dst[output - offset]; dst[output++] = value; }
                break;
        }
    }
    return output == dst_size && p == src_size ? SD_HAP_OK : SD_HAP_INVALID;
}

static size_t block_bytes(uint8_t format, uint32_t width, uint32_t height)
{
    size_t blocks = ((size_t)width + 3) / 4 * (((size_t)height + 3) / 4);
    if (format == 0x0B || format == 0x01) return blocks * 8;
    if (format == 0x0E || format == 0x0F) return blocks * 16;
    return 0;
}

static int decompress_complex(const uint8_t* data, size_t length, size_t expected, uint8_t* output)
{
    sd_section instruction, table; const uint8_t *compressors = NULL, *sizes = NULL, *offsets = NULL;
    size_t p, end, count = 0, i, frame_data, running = 0, running_out = 0;
    if (read_section(data, 0, length, &instruction) != SD_HAP_OK || instruction.type != 1) return SD_HAP_INVALID;
    p = instruction.data; end = p + instruction.length;
    while (p < end) {
        if (read_section(data, p, end - p, &table) != SD_HAP_OK) return SD_HAP_INVALID;
        if (table.type == 2) { compressors = data + table.data; if (count && count != table.length) return SD_HAP_INVALID; count = table.length; }
        else if (table.type == 3) { sizes = data + table.data; if (table.length % 4) return SD_HAP_INVALID; if (count && count != table.length / 4) return SD_HAP_INVALID; count = table.length / 4; }
        else if (table.type == 4) { offsets = data + table.data; if (table.length % 4) return SD_HAP_INVALID; if (count && count != table.length / 4) return SD_HAP_INVALID; count = table.length / 4; }
        p += table.total;
    }
    if (!compressors || !sizes || count == 0) return SD_HAP_INVALID;
    frame_data = instruction.total;
    for (i = 0; i < count; ++i) {
        uint32_t compressed_size = le32(sizes + i * 4); uint32_t relative = offsets ? le32(offsets + i * 4) : (uint32_t)running;
        uint8_t* chunk_output; size_t unpacked;
        if ((size_t)relative > length || (size_t)compressed_size > length - relative || frame_data > length - relative - compressed_size) return SD_HAP_INVALID;
        chunk_output = output + running_out;
        if (compressors[i] == 0x0A) { unpacked = compressed_size; if (running_out > expected - (unpacked > expected ? expected : unpacked)) return SD_HAP_INVALID; memcpy(chunk_output, data + frame_data + relative, unpacked); }
        else if (compressors[i] == 0x0B) { size_t declared = 0, q = 0; if (snappy_varint(data + frame_data + relative, compressed_size, &q, &declared) != SD_HAP_OK || declared > expected - running_out) return SD_HAP_INVALID; unpacked = declared; if (snappy_decode(data + frame_data + relative, compressed_size, chunk_output, unpacked) != SD_HAP_OK) return SD_HAP_INVALID; }
        else return SD_HAP_UNSUPPORTED;
        running_out += unpacked; running = (size_t)relative + compressed_size;
    }
    return running_out == expected ? SD_HAP_OK : SD_HAP_INVALID;
}

static int decode_payload(const uint8_t* data, size_t length, uint8_t type, size_t expected, uint8_t* output)
{
    uint8_t compressor = type >> 4;
    if ((type & 0x0F) != 0x0B && (type & 0x0F) != 0x0E && (type & 0x0F) != 0x0F && (type & 0x0F) != 0x01) return SD_HAP_UNSUPPORTED;
    if (compressor == 0x0A) { if (length != expected) return SD_HAP_INVALID; memcpy(output, data, expected); return SD_HAP_OK; }
    if (compressor == 0x0B) return snappy_decode(data, length, output, expected);
    if (compressor == 0x0C) return decompress_complex(data, length, expected, output);
    return SD_HAP_UNSUPPORTED;
}

static void rgb565(uint16_t value, uint8_t* c)
{
    c[0] = (uint8_t)(((value >> 11) & 31) * 255 / 31);
    c[1] = (uint8_t)(((value >> 5) & 63) * 255 / 63);
    c[2] = (uint8_t)((value & 31) * 255 / 31);
}
static uint8_t lerp8(uint8_t a, uint8_t b, int aw, int bw) { return (uint8_t)(((int)a * aw + (int)b * bw) / (aw + bw)); }

static void decode_alpha(const uint8_t* b, size_t o, int x, int y, uint8_t* out)
{
    uint64_t bits = 0; int i, index; uint8_t a0 = b[o], a1 = b[o + 1];
    for (i = 0; i < 6; ++i) bits |= (uint64_t)b[o + 2 + i] << (8 * i);
    index = (int)((bits >> (3 * (y * 4 + x))) & 7);
    if (index == 0) *out = a0; else if (index == 1) *out = a1;
    else if (a0 > a1) *out = (uint8_t)(((8 - index) * a0 + (index - 1) * a1) / 7);
    else if (index <= 5) *out = (uint8_t)(((6 - index) * a0 + (index - 1) * a1) / 5);
    else *out = index == 6 ? 0 : 255;
}

static void decode_bc1(const uint8_t* b, size_t o, int x, int y, uint8_t* out)
{
    uint8_t colors[4][3]; uint16_t c0 = le16(b + o), c1 = le16(b + o + 2); uint32_t bits = le32(b + o + 4); int index = (bits >> (2 * (y * 4 + x))) & 3;
    rgb565(c0, colors[0]); rgb565(c1, colors[1]);
    colors[2][0] = lerp8(colors[0][0], colors[1][0], 2, 1); colors[2][1] = lerp8(colors[0][1], colors[1][1], 2, 1); colors[2][2] = lerp8(colors[0][2], colors[1][2], 2, 1);
    colors[3][0] = lerp8(colors[0][0], colors[1][0], 1, 2); colors[3][1] = lerp8(colors[0][1], colors[1][1], 1, 2); colors[3][2] = lerp8(colors[0][2], colors[1][2], 1, 2);
    if (c0 <= c1) { colors[2][0] = lerp8(colors[0][0], colors[1][0], 1, 1); colors[2][1] = lerp8(colors[0][1], colors[1][1], 1, 1); colors[2][2] = lerp8(colors[0][2], colors[1][2], 1, 1); colors[3][0] = colors[3][1] = colors[3][2] = 0; }
    out[0] = colors[index][0]; out[1] = colors[index][1]; out[2] = colors[index][2]; out[3] = (index == 3 && c0 <= c1) ? 0 : 255;
}

static int decode_plane(const uint8_t* frame, size_t frame_size, const sd_section* section, uint32_t width, uint32_t height, uint8_t* rgba, uint8_t* blocks_out, uint8_t* format_out)
{
    size_t expected = block_bytes(section->type & 0x0F, width, height), x, y, block_width = ((size_t)width + 3) / 4;
    size_t blocks_size;
    if (!expected) return SD_HAP_UNSUPPORTED;
    if (section->data > frame_size || section->length > frame_size - section->data) return SD_HAP_INVALID;
    if (section->type >> 4 == 0x0C) { if (decompress_complex(frame + section->data, section->length, expected, blocks_out) != SD_HAP_OK) return SD_HAP_INVALID; }
    else if (decode_payload(frame + section->data, section->length, section->type, expected, blocks_out) != SD_HAP_OK) return SD_HAP_INVALID;
    *format_out = (section->type & 0x0F) == 0x0B ? 1 : ((section->type & 0x0F) == 0x01 ? 3 : 2);
    if (!rgba) return SD_HAP_OK;
    blocks_size = (*format_out == 1 || *format_out == 3) ? 8 : 16;
    for (y = 0; y < height; ++y) for (x = 0; x < width; ++x) {
        size_t block = ((y / 4) * block_width + x / 4) * blocks_size, pixel = (y * width + x) * 4; uint8_t value;
        if (*format_out == 1) decode_bc1(blocks_out, block, (int)(x & 3), (int)(y & 3), rgba + pixel);
        else if (*format_out == 2) { decode_bc1(blocks_out, block + 8, (int)(x & 3), (int)(y & 3), rgba + pixel); decode_alpha(blocks_out, block, (int)(x & 3), (int)(y & 3), &value); rgba[pixel + 3] = value; }
        else { decode_alpha(blocks_out, block, (int)(x & 3), (int)(y & 3), &value); rgba[pixel] = rgba[pixel + 1] = rgba[pixel + 2] = value; rgba[pixel + 3] = 255; }
    }
    return SD_HAP_OK;
}

static void convert_ycocg(uint8_t* rgba, uint32_t width, uint32_t height)
{
    size_t i; for (i = 0; i < (size_t)width * height; ++i) {
        float co = rgba[i * 4] / 255.0f - (0.5f * 256.0f / 255.0f);
        float cg = rgba[i * 4 + 1] / 255.0f - (0.5f * 256.0f / 255.0f);
        float scale = rgba[i * 4 + 2] * (255.0f / 8.0f) / 255.0f + 1.0f;
        float y = rgba[i * 4 + 3] / 255.0f;
        float r = y + co / scale - cg / scale, g = y + cg / scale, b = y - co / scale - cg / scale;
        rgba[i * 4] = (uint8_t)(fmaxf(0.0f, fminf(1.0f, r)) * 255.0f + 0.5f);
        rgba[i * 4 + 1] = (uint8_t)(fmaxf(0.0f, fminf(1.0f, g)) * 255.0f + 0.5f);
        rgba[i * 4 + 2] = (uint8_t)(fmaxf(0.0f, fminf(1.0f, b)) * 255.0f + 0.5f);
    }
}

static int decode_frame(const uint8_t* frame, size_t frame_size, int codec, uint32_t width, uint32_t height, uint8_t* rgba, uint8_t** plane0, size_t* plane0_bytes, uint32_t* plane0_format, uint8_t** plane1, size_t* plane1_bytes, uint32_t* plane1_format)
{
    sd_section root, a, b; uint8_t* blocks_a; uint8_t* blocks_b; uint8_t format_a, format_b;
    size_t expected_a = 0, expected_b = 0, end;
    *plane0 = NULL; *plane1 = NULL; *plane0_bytes = 0; *plane1_bytes = 0; *plane0_format = 0; *plane1_format = 0;
    if (read_section(frame, 0, frame_size, &root) != SD_HAP_OK || root.total > frame_size) return SD_HAP_INVALID;
    blocks_a = (uint8_t*)malloc(block_bytes(0x0F, width, height)); blocks_b = (uint8_t*)malloc(block_bytes(0x01, width, height));
    if (!blocks_a || !blocks_b) { free(blocks_a); free(blocks_b); return SD_HAP_IO; }
    if (root.type == 0x0D) {
        size_t p = root.data; uint8_t* plane_a = NULL; uint8_t* plane_b = NULL; size_t pixels = (size_t)width * height;
        if (codec != 6 || p >= root.data + root.length || read_section(frame, p, root.length, &a) != SD_HAP_OK) { free(blocks_a); free(blocks_b); return SD_HAP_INVALID; }
        p += a.total; if (p >= root.data + root.length || read_section(frame, p, root.data + root.length - p, &b) != SD_HAP_OK || p + b.total != root.data + root.length) { free(blocks_a); free(blocks_b); return SD_HAP_INVALID; }
        expected_a = block_bytes(a.type & 0x0F, width, height); expected_b = block_bytes(b.type & 0x0F, width, height);
        if (!expected_a || !expected_b || expected_a > block_bytes(0x0F, width, height) || expected_b > block_bytes(0x0F, width, height)) { free(blocks_a); free(blocks_b); return SD_HAP_INVALID; }
        if (rgba) {
            plane_a = (uint8_t*)malloc(pixels * 4); plane_b = (uint8_t*)malloc(pixels * 4);
            if (!plane_a || !plane_b) { free(plane_a); free(plane_b); free(blocks_a); free(blocks_b); return SD_HAP_IO; }
        }
        if (decode_plane(frame, frame_size, &a, width, height, plane_a, blocks_a, &format_a) != SD_HAP_OK || decode_plane(frame, frame_size, &b, width, height, plane_b, blocks_b, &format_b) != SD_HAP_OK || !((format_a == 2 && format_b == 3) || (format_a == 3 && format_b == 2))) { free(plane_a); free(plane_b); free(blocks_a); free(blocks_b); return SD_HAP_INVALID; }
        if (rgba) { uint8_t* color = format_a == 2 ? plane_a : plane_b; uint8_t* alpha = format_a == 3 ? plane_a : plane_b; size_t i; convert_ycocg(color, width, height); for (i = 0; i < pixels; ++i) { rgba[i * 4] = color[i * 4]; rgba[i * 4 + 1] = color[i * 4 + 1]; rgba[i * 4 + 2] = color[i * 4 + 2]; rgba[i * 4 + 3] = alpha[i * 4]; } }
        if (format_a == 2) { *plane0 = blocks_a; *plane0_bytes = expected_a; *plane0_format = format_a; *plane1 = blocks_b; *plane1_bytes = expected_b; *plane1_format = format_b; }
        else { *plane0 = blocks_b; *plane0_bytes = expected_b; *plane0_format = format_b; *plane1 = blocks_a; *plane1_bytes = expected_a; *plane1_format = format_a; }
        free(plane_a); free(plane_b);
    } else {
        if (codec == 3 && (root.type & 0x0F) != 0x0B || codec == 4 && (root.type & 0x0F) != 0x0E || codec == 5 && (root.type & 0x0F) != 0x0F || codec == 6) { free(blocks_a); free(blocks_b); return SD_HAP_INVALID; }
        if (decode_plane(frame, frame_size, &root, width, height, rgba, blocks_a, &format_a) != SD_HAP_OK) { free(blocks_a); free(blocks_b); return SD_HAP_INVALID; }
        *plane0 = blocks_a; *plane0_bytes = block_bytes(root.type & 0x0F, width, height); *plane0_format = format_a; free(blocks_b); blocks_b = NULL;
        if (rgba && codec == 5) convert_ycocg(rgba, width, height);
        if (rgba && codec == 3) { size_t i; for (i = 0; i < (size_t)width * height; ++i) rgba[i * 4 + 3] = 255; }
    }
    (void)end; return SD_HAP_OK;
}

static int parse_movie(sd_context* ctx, const char* path)
{
    FILE* file; sd_atom moov, trak, mdia, mdhd, hdlr, minf, stbl, stsd, stts, stsc, stsz, offsets_atom; uint32_t* stts_counts = NULL; uint32_t* stts_deltas = NULL; uint32_t* stsc_first = NULL; uint32_t* stsc_per = NULL; uint32_t* sizes = NULL; uint64_t* offsets = NULL; size_t stts_count = 0, stsc_count = 0, size_count = 0, offset_count = 0; int result = SD_HAP_INVALID; uint8_t* bytes;
    file = fopen(path, "rb"); if (!file) return SD_HAP_IO; if (fseek(file, 0, SEEK_END) != 0) { fclose(file); return SD_HAP_IO; } { long length = ftell(file); if (length < 16 || (uint64_t)length > SIZE_MAX) { fclose(file); return SD_HAP_INVALID; } ctx->file_size = (size_t)length; } if (fseek(file, 0, SEEK_SET) != 0) { fclose(file); return SD_HAP_IO; }
    bytes = (uint8_t*)malloc(ctx->file_size); if (!bytes) { fclose(file); return SD_HAP_IO; } if (fread(bytes, 1, ctx->file_size, file) != ctx->file_size) { free(bytes); fclose(file); return SD_HAP_IO; } fclose(file); ctx->file = bytes;
    if (root_atom(bytes, ctx->file_size, "moov", 0, &moov) != SD_HAP_OK || find_video_track(bytes, ctx->file_size, &moov, &trak) != SD_HAP_OK || child_atom(bytes, ctx->file_size, &trak, "mdia", 0, &mdia) != SD_HAP_OK || child_atom(bytes, ctx->file_size, &mdia, "mdhd", 0, &mdhd) != SD_HAP_OK || child_atom(bytes, ctx->file_size, &mdia, "minf", 0, &minf) != SD_HAP_OK || child_atom(bytes, ctx->file_size, &minf, "stbl", 0, &stbl) != SD_HAP_OK) goto done;
    { size_t d = mdhd.offset + mdhd.header; uint8_t version = bytes[d]; size_t ts_offset = version == 1 ? 20 : 12; if (mdhd.size - mdhd.header < ts_offset + 4) goto done; ctx->timescale = be32(bytes + d + ts_offset); if (!ctx->timescale) goto done; }
    if (child_atom(bytes, ctx->file_size, &stbl, "stsd", 0, &stsd) != SD_HAP_OK || parse_stsd(bytes, ctx->file_size, &stsd, ctx->codec, &ctx->width, &ctx->height) != SD_HAP_OK || child_atom(bytes, ctx->file_size, &stbl, "stts", 0, &stts) != SD_HAP_OK || child_atom(bytes, ctx->file_size, &stbl, "stsc", 0, &stsc) != SD_HAP_OK || child_atom(bytes, ctx->file_size, &stbl, "stsz", 0, &stsz) != SD_HAP_OK || parse_stts(bytes, &stts, &stts_counts, &stts_deltas, &stts_count) != SD_HAP_OK || parse_stsc(bytes, &stsc, &stsc_first, &stsc_per, &stsc_count) != SD_HAP_OK || parse_stsz(bytes, &stsz, &sizes, &size_count) != SD_HAP_OK) goto done;
    if (child_atom(bytes, ctx->file_size, &stbl, "co64", 0, &offsets_atom) != SD_HAP_OK && child_atom(bytes, ctx->file_size, &stbl, "stco", 0, &offsets_atom) != SD_HAP_OK) goto done;
    if (parse_offsets(bytes, &offsets_atom, &offsets, &offset_count) != SD_HAP_OK || build_samples(bytes, ctx->file_size, offsets, offset_count, sizes, size_count, stsc_first, stsc_per, stsc_count, stts_counts, stts_deltas, stts_count, &ctx->samples) != SD_HAP_OK) goto done;
    ctx->sample_count = size_count; for (size_t i = 0; i < size_count; ++i) { if (ctx->duration_ticks > UINT64_MAX - ctx->samples[i].duration) { result = SD_HAP_INVALID; goto done; } ctx->duration_ticks += ctx->samples[i].duration; } result = SD_HAP_OK;
done:
    free(stts_counts); free(stts_deltas); free(stsc_first); free(stsc_per); free(sizes); free(offsets); if (result != SD_HAP_OK) { free(ctx->file); ctx->file = NULL; free(ctx->samples); ctx->samples = NULL; } return result;
}

static size_t sample_for_time(const sd_context* ctx, double seconds)
{
    uint64_t target; size_t i;
    if (!ctx || !ctx->sample_count || seconds <= 0) return 0;
    if (!isfinite(seconds)) return ctx->sample_count - 1;
    if (seconds > (double)UINT64_MAX / (double)ctx->timescale) return ctx->loop ? 0 : ctx->sample_count - 1;
    target = (uint64_t)(seconds * ctx->timescale);
    if (ctx->duration_ticks && target >= ctx->duration_ticks) { if (ctx->loop) target %= ctx->duration_ticks; else return ctx->sample_count - 1; }
    for (i = 0; i + 1 < ctx->sample_count; ++i) if (target < ctx->samples[i].ticks + ctx->samples[i].duration) break;
    return i;
}

SD_HAP_API uint32_t sd_hap_abi_version(void) { return 1; }
SD_HAP_API uint32_t sd_hap_capabilities(void) { return SD_HAP_CAP_BC1 | SD_HAP_CAP_BC3 | SD_HAP_CAP_BC4 | SD_HAP_CAP_CPU_RGBA; }

SD_HAP_API int sd_hap_open(const char* path, int codec, void** out_handle)
{
    sd_context* ctx;
    if (!path || !out_handle || (codec < 3 || codec > 6)) return SD_HAP_INVALID;
    *out_handle = NULL; ctx = (sd_context*)calloc(1, sizeof(sd_context)); if (!ctx) return SD_HAP_IO; ctx->codec = codec; ctx->speed = 1.0; ctx->loop = 1; sd_mutex_init(&ctx->mutex);
    if (parse_movie(ctx, path) != SD_HAP_OK) { sd_mutex_destroy(&ctx->mutex); free(ctx); return SD_HAP_INVALID; }
    *out_handle = ctx; return SD_HAP_OK;
}

SD_HAP_API int sd_hap_prepare(void* handle)
{
    sd_context* ctx = (sd_context*)handle; if (!ctx) return SD_HAP_INVALID; return sd_hap_acquire_frame(ctx, 0, NULL);
}

SD_HAP_API int sd_hap_acquire_frame(void* handle, int64_t index, sd_hap_frame* out_frame)
{
    sd_context* ctx = (sd_context*)handle; sd_sample sample; uint8_t* rgba = NULL; uint8_t* plane0 = NULL; uint8_t* plane1 = NULL; size_t plane0_bytes = 0, plane1_bytes = 0, bytes; uint32_t plane0_format = 0, plane1_format = 0; int compressed_only = out_frame && out_frame->decode_path == 1;
    if (!ctx) return SD_HAP_INVALID; sd_mutex_lock(&ctx->mutex);
    if (ctx->frame_leased || (index >= 0 && (uint64_t)index >= ctx->sample_count)) { sd_mutex_unlock(&ctx->mutex); return SD_HAP_INVALID; }
    if (index < 0) index = (int64_t)ctx->current;
    sample = ctx->samples[(size_t)index]; bytes = (size_t)ctx->width * ctx->height * 4;
    if (!compressed_only) { rgba = (uint8_t*)malloc(bytes); if (!rgba) { sd_mutex_unlock(&ctx->mutex); return SD_HAP_IO; } }
    if (decode_frame(ctx->file + sample.offset, sample.size, ctx->codec, ctx->width, ctx->height, rgba, &plane0, &plane0_bytes, &plane0_format, &plane1, &plane1_bytes, &plane1_format) != SD_HAP_OK) { free(rgba); sd_mutex_unlock(&ctx->mutex); return SD_HAP_INVALID; }
    free(ctx->rgba); free(ctx->plane_blocks[0]); free(ctx->plane_blocks[1]); ctx->rgba = rgba; ctx->rgba_bytes = compressed_only ? 0 : bytes; ctx->plane_blocks[0] = plane0; ctx->plane_blocks[1] = plane1; ctx->plane_bytes[0] = plane0_bytes; ctx->plane_bytes[1] = plane1_bytes; ctx->plane_format[0] = plane0_format; ctx->plane_format[1] = plane1_format; ctx->current = (size_t)index;
    if (out_frame) {
        memset(out_frame, 0, sizeof(*out_frame));
        out_frame->struct_size = sizeof(*out_frame);
        out_frame->width = ctx->width;
        out_frame->height = ctx->height;
        /* TextureFormat is a codec-plane marker, not a host GPU capability:
           0x0B=BC1/Hap1, 0x0E=BC3/Hap5, 0x0F=BC3+BC4 YCoCg/Q. */
        out_frame->texture_format = ctx->codec == 3 ? 0x0Bu : (ctx->codec == 4 ? 0x0Eu : 0x0Fu);
        out_frame->frame_index = (uint64_t)index;
        out_frame->presentation_ticks = sample.ticks;
        out_frame->rgba_bytes = compressed_only ? 0u : (uint32_t)bytes;
        out_frame->rgba = ctx->rgba;
        out_frame->plane_count = ctx->plane_blocks[1] ? 2u : 1u;
        out_frame->planes[0].format = ctx->plane_format[0];
        out_frame->planes[0].bytes = (uint32_t)ctx->plane_bytes[0];
        out_frame->planes[0].data = ctx->plane_blocks[0];
        if (out_frame->plane_count == 2) {
            out_frame->planes[1].format = ctx->plane_format[1];
            out_frame->planes[1].bytes = (uint32_t)ctx->plane_bytes[1];
            out_frame->planes[1].data = ctx->plane_blocks[1];
        }
        out_frame->decode_path = compressed_only ? 1u : 3u;
        ctx->frame_leased = 1;
    }
    sd_mutex_unlock(&ctx->mutex); return SD_HAP_OK;
}

SD_HAP_API void sd_hap_release_frame(void* handle, const sd_hap_frame* frame) { sd_context* ctx = (sd_context*)handle; if (!ctx) return; sd_mutex_lock(&ctx->mutex); if (ctx->frame_leased && (!frame || frame->rgba == ctx->rgba)) { ctx->frame_leased = 0; free(ctx->rgba); free(ctx->plane_blocks[0]); free(ctx->plane_blocks[1]); ctx->rgba = NULL; ctx->plane_blocks[0] = NULL; ctx->plane_blocks[1] = NULL; ctx->rgba_bytes = 0; ctx->plane_bytes[0] = ctx->plane_bytes[1] = 0; } sd_mutex_unlock(&ctx->mutex); }
SD_HAP_API void sd_hap_close(void* handle)
{
    sd_context* ctx = (sd_context*)handle; if (!ctx) return; sd_mutex_lock(&ctx->mutex); free(ctx->rgba); free(ctx->plane_blocks[0]); free(ctx->plane_blocks[1]); free(ctx->samples); free(ctx->file); ctx->rgba = NULL; ctx->samples = NULL; ctx->file = NULL; sd_mutex_unlock(&ctx->mutex); sd_mutex_destroy(&ctx->mutex); free(ctx);
}
SD_HAP_API int sd_hap_play(void* handle) { sd_context* ctx = (sd_context*)handle; if (!ctx) return SD_HAP_INVALID; sd_mutex_lock(&ctx->mutex); ctx->playing = 1; sd_mutex_unlock(&ctx->mutex); return SD_HAP_OK; }
SD_HAP_API int sd_hap_pause(void* handle) { sd_context* ctx = (sd_context*)handle; if (!ctx) return SD_HAP_INVALID; sd_mutex_lock(&ctx->mutex); ctx->playing = 0; sd_mutex_unlock(&ctx->mutex); return SD_HAP_OK; }
SD_HAP_API int sd_hap_stop(void* handle) { sd_context* ctx = (sd_context*)handle; if (!ctx) return SD_HAP_INVALID; sd_mutex_lock(&ctx->mutex); ctx->playing = 0; ctx->current = 0; sd_mutex_unlock(&ctx->mutex); return SD_HAP_OK; }
SD_HAP_API int sd_hap_set_speed(void* handle, double speed) { sd_context* ctx = (sd_context*)handle; if (!ctx || !isfinite(speed) || speed < 0 || speed > 4) return SD_HAP_INVALID; sd_mutex_lock(&ctx->mutex); ctx->speed = speed; sd_mutex_unlock(&ctx->mutex); return SD_HAP_OK; }
SD_HAP_API int sd_hap_set_loop(void* handle, int loop) { sd_context* ctx = (sd_context*)handle; if (!ctx) return SD_HAP_INVALID; sd_mutex_lock(&ctx->mutex); ctx->loop = loop != 0; sd_mutex_unlock(&ctx->mutex); return SD_HAP_OK; }
SD_HAP_API int sd_hap_seek(void* handle, double seconds) { sd_context* ctx = (sd_context*)handle; if (!ctx || !isfinite(seconds) || seconds < 0) return SD_HAP_INVALID; sd_mutex_lock(&ctx->mutex); ctx->current = sample_for_time(ctx, seconds); sd_mutex_unlock(&ctx->mutex); return SD_HAP_OK; }
SD_HAP_API int sd_hap_sync(void* handle, double logical_seconds, int demanded) { sd_context* ctx = (sd_context*)handle; if (!ctx || !isfinite(logical_seconds) || logical_seconds < 0) return SD_HAP_INVALID; sd_mutex_lock(&ctx->mutex); ctx->demanded = demanded != 0; if (ctx->demanded) ctx->current = sample_for_time(ctx, logical_seconds); if (!ctx->demanded) ctx->playing = 0; sd_mutex_unlock(&ctx->mutex); return SD_HAP_OK; }
