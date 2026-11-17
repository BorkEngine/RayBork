#version 450
#define SSAO_PASS_DENOISE
#define DENOISE_SAMPLE_COUNT 8
#include "SsaoDenoise8Kernel.glslinc"
#include "SsaoDenoise.glslinc"
