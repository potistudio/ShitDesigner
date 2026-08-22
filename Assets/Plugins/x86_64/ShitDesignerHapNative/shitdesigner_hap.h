#pragma once

#include <stdint.h>

#if defined(_WIN32)
#  define SD_HAP_API __declspec(dllexport)
#else
#  define SD_HAP_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

enum { SD_HAP_OK = 0, SD_HAP_INVALID = -1, SD_HAP_IO = -2, SD_HAP_UNSUPPORTED = -100 };
enum { SD_HAP_CAP_BC1 = 1u << 0, SD_HAP_CAP_BC3 = 1u << 1, SD_HAP_CAP_BC4 = 1u << 2, SD_HAP_CAP_CPU_RGBA = 1u << 3 };

typedef struct sd_hap_plane {
    uint32_t format; /* 1=BC1, 2=BC3, 3=BC4 */
    uint32_t bytes;
    const uint8_t* data;
} sd_hap_plane;

typedef struct sd_hap_frame {
    uint32_t struct_size;
    uint32_t width;
    uint32_t height;
    uint32_t texture_format; /* 0x0B=Hap1/BC1, 0x0E=Hap5/BC3, 0x0F=HapY/HapM YCoCg */
    uint64_t frame_index;
    uint64_t presentation_ticks;
    uint32_t rgba_bytes;
    const uint8_t* rgba; /* straight sRGB RGBA8; managed bridge linearizes and premultiplies */
    uint32_t plane_count;
    sd_hap_plane planes[2];
    /* Set to 1 before acquire to request compressed planes without CPU RGBA
       expansion. The returned value is 1 for that path or 3 for CPU RGBA. */
    uint32_t decode_path;
} sd_hap_frame;

SD_HAP_API uint32_t sd_hap_abi_version(void);
SD_HAP_API uint32_t sd_hap_capabilities(void);
SD_HAP_API int sd_hap_open(const char* path, int codec, void** out_handle);
SD_HAP_API int sd_hap_prepare(void* handle);
SD_HAP_API int sd_hap_acquire_frame(void* handle, int64_t index, sd_hap_frame* out_frame);
SD_HAP_API void sd_hap_release_frame(void* handle, const sd_hap_frame* frame);
SD_HAP_API void sd_hap_close(void* handle);
SD_HAP_API int sd_hap_play(void* handle);
SD_HAP_API int sd_hap_pause(void* handle);
SD_HAP_API int sd_hap_stop(void* handle);
SD_HAP_API int sd_hap_set_speed(void* handle, double speed);
SD_HAP_API int sd_hap_set_loop(void* handle, int loop);
SD_HAP_API int sd_hap_seek(void* handle, double seconds);
SD_HAP_API int sd_hap_sync(void* handle, double logical_seconds, int demanded);

#ifdef __cplusplus
}
#endif
