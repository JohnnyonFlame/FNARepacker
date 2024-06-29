#ifdef _MSC_VER
#define DECLSPEC extern "C" __declspec(dllexport)
#else
#define DECLSPEC extern "C"
#endif

#include <cstdio>
#include <thread>
#include <cstdint>
#include <fstream>
#include <vector>
#include <filesystem>
#include <time.h>
#include <errno.h>

#include "astcenc.h"

#define BLOCK_X 4
#define BLOCK_Y 4
#define ASTC_QUALITY ASTCENC_PRE_MEDIUM

static std::filesystem::path out_dir{"out"};
static std::filesystem::path gfx_dir;

static astcenc_context* astc_ctx = NULL;
static astcenc_config astc_cfg = {};

static int NUM_JOBS = 1;

static const astcenc_swizzle swizzle {
    ASTCENC_SWZ_R, ASTCENC_SWZ_G, ASTCENC_SWZ_B, ASTCENC_SWZ_A
};

int astcenc_init(unsigned int blk_w, unsigned int blk_h)
{
    // Check if we're already configured and configured correctly
    if ((astc_ctx != NULL) && (blk_w == astc_cfg.block_x) && (blk_h == astc_cfg.block_y))
        return 1;

    // If ctx exists, we are reconfiguring it.
    if (astc_ctx != NULL) {
        astcenc_context_free(astc_ctx);
        astc_ctx = NULL;
    }

    astcenc_error status;
    NUM_JOBS = std::thread::hardware_concurrency();
    NUM_JOBS = NUM_JOBS ? NUM_JOBS : 1;

    float q, quality = ASTC_QUALITY;
    char *quality_override, *_;
    if (quality_override = getenv("ASTC_QUALITY")) {
        errno = 0;
        q = strtof(quality_override, &_);
        if (errno != 0) {
            fprintf(stderr, "Warning: Failed to parse ASTC_QUALITY.\n");
        } else {
            quality = q;
        }
    }

    std::printf("astcenc_config_init(): blk %dx%d, quality: %f\n", blk_w, blk_h, quality);

    // Recreate astc cfg
    status = astcenc_config_init(ASTCENC_PRF_LDR, blk_w, blk_h, 1, quality, 0, &astc_cfg);
    if (status != ASTCENC_SUCCESS) {
        std::printf("ERROR: astcenc_config_init failed: %s\n", astcenc_get_error_string(status));
        return 0;
    }

    status = astcenc_context_alloc(&astc_cfg, NUM_JOBS, &astc_ctx);
    if (status != ASTCENC_SUCCESS) {
        printf("ERROR: astcenc_context_alloc failed: %s\n", astcenc_get_error_string(status));
        return 0;
    }

    return 1;
}

DECLSPEC int convert_texture(int w, int h, int payload_len, unsigned int blk_w, unsigned int blk_h, uint8_t *in_tex, uint8_t *out_tex)
{
    if (!astcenc_init(blk_w, blk_h)) {
        return 0;
    }

    void *slices[] = { (void*)in_tex };
    astcenc_image image = {};
    image.dim_x = w;
    image.dim_y = h;
    image.dim_z = 1;
    image.data_type = ASTCENC_TYPE_U8;
    image.data = slices;

    // Spawn worker threads and wait...
    std::vector<std::thread> jobs;
    for (int i = 0; i < NUM_JOBS; i++) {
        jobs.emplace_back([&](int job) {
            return astcenc_compress_image(astc_ctx, &image, &swizzle, out_tex, payload_len, job);
        }, i);
    }

    for (int i = 0; i < NUM_JOBS; i++)
        jobs[i].join();

    astcenc_error status = astcenc_compress_reset(astc_ctx);
    if (status != ASTCENC_SUCCESS)
    {
        std::printf("ERROR: Failed to compress texture: %s\n", astcenc_get_error_string(status));		
        return 0;
    }

    return 1;
}