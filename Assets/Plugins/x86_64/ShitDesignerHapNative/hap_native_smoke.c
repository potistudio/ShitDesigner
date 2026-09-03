#include "shitdesigner_hap.h"
#include <stdio.h>
#include <string.h>

static unsigned hash(const unsigned char* data, unsigned size) { unsigned h = 2166136261u; for (unsigned i = 0; i < size; ++i) h = (h ^ data[i]) * 16777619u; return h; }

static int check(const char* path, int codec)
{
    void* handle = NULL; sd_hap_frame frame;
    if (sd_hap_open(path, codec, &handle) != SD_HAP_OK || !handle) return 1;
    if (sd_hap_prepare(handle) != SD_HAP_OK) { sd_hap_close(handle); return 2; }
    memset(&frame, 0, sizeof(frame));
    if (sd_hap_acquire_frame(handle, 0, &frame) != SD_HAP_OK || frame.width != 4 || frame.height != 4 || frame.rgba_bytes != 64 || !frame.rgba) { sd_hap_close(handle); return 3; }
    { const unsigned char expected[4] = { codec == 3 ? 255 : (codec == 4 ? 255 : 255), codec == 3 || codec == 4 ? 0 : 143, codec == 3 || codec == 4 ? 0 : 255, 255 }; if (memcmp(frame.rgba, expected, 4) != 0) { sd_hap_release_frame(handle, &frame); sd_hap_close(handle); return 6; } }
    if (frame.planes[0].format != (codec == 3 ? 1u : 2u) || frame.planes[0].bytes != (codec == 3 ? 8u : 16u) || frame.plane_count != (codec == 6 ? 2u : 1u) || (codec == 6 && (frame.planes[1].format != 3u || frame.planes[1].bytes != 8u))) { sd_hap_release_frame(handle, &frame); sd_hap_close(handle); return 7; }
    { unsigned expected_hash = codec == 3 || codec == 4 ? 2624936357u : 693279365u; if (hash(frame.rgba, frame.rgba_bytes) != expected_hash) { sd_hap_release_frame(handle, &frame); sd_hap_close(handle); return 8; } }
    if (frame.frame_index != 0 || frame.plane_count == 0) { sd_hap_release_frame(handle, &frame); sd_hap_close(handle); return 4; }
    sd_hap_release_frame(handle, &frame);
    memset(&frame, 0, sizeof(frame)); frame.struct_size = sizeof(frame); frame.decode_path = 1;
    if (sd_hap_acquire_frame(handle, 0, &frame) != SD_HAP_OK || frame.frame_index != 0 || frame.decode_path != 1 || frame.rgba || frame.rgba_bytes != 0 || frame.plane_count == 0 || !frame.planes[0].data) { sd_hap_close(handle); return 11; }
    sd_hap_release_frame(handle, &frame);
    memset(&frame, 0, sizeof(frame)); frame.struct_size = sizeof(frame);
    if (sd_hap_acquire_frame(handle, 1, &frame) != SD_HAP_OK || frame.frame_index != 1) { sd_hap_close(handle); return 5; }
    { const unsigned char expected[4] = { codec == 3 || codec == 4 ? 0 : 255, codec == 3 || codec == 4 ? 0 : 151, 255, codec == 4 || codec == 6 ? 128 : 255 }; if (memcmp(frame.rgba, expected, 4) != 0 || frame.presentation_ticks != 1) { sd_hap_release_frame(handle, &frame); sd_hap_close(handle); return 9; } }
    { unsigned expected_hash = codec == 3 ? 3479389989u : codec == 4 ? 2273442885u : codec == 5 ? 676316805u : codec == 6 ? 3592397253u : 3479389989u; if (hash(frame.rgba, frame.rgba_bytes) != expected_hash) { sd_hap_release_frame(handle, &frame); sd_hap_close(handle); return 10; } }
    sd_hap_release_frame(handle, &frame);
    if (sd_hap_set_speed(handle, 1.5) != SD_HAP_OK || sd_hap_set_loop(handle, 0) != SD_HAP_OK || sd_hap_seek(handle, 1.0 / 60.0) != SD_HAP_OK || sd_hap_sync(handle, 0.0, 1) != SD_HAP_OK || sd_hap_pause(handle) != SD_HAP_OK || sd_hap_play(handle) != SD_HAP_OK || sd_hap_sync(handle, 1.0 / 60.0, 0) != SD_HAP_OK || sd_hap_sync(handle, 1.0 / 60.0, 1) != SD_HAP_OK) { sd_hap_close(handle); return 11; }
    sd_hap_close(handle); return 0;
}

int main(int argc, char** argv)
{
    void* malformed = NULL;
    if (argc != 8) return 10;
    for (int i = 1; i <= 5; ++i) { int codec = i == 1 ? 3 : i == 2 ? 4 : i == 3 ? 5 : i == 4 ? 6 : 3; if (check(argv[i], codec) != 0) return 20 + i; }
    if (sd_hap_open(argv[6], 3, &malformed) == SD_HAP_OK) { sd_hap_close(malformed); return 31; }
    if (sd_hap_open(argv[7], 3, &malformed) == SD_HAP_OK) { sd_hap_close(malformed); return 32; }
    if (sd_hap_open(argv[1], 7, &malformed) == SD_HAP_OK) { sd_hap_close(malformed); return 33; }
    return 0;
}
