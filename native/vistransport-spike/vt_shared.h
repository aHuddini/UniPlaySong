// SPIKE A: frame transport from a 64-bit renderer to a 32-bit WPF element.
// The layout both processes map. Fixed-width fields only, so x64 and x86 agree byte for byte.
#pragma once
#include <stdint.h>

#define VT_MAPPING_NAME L"Local\\UPS_VisualTransport_Spike"
#define VT_MAGIC 0x31585456u /* "VTX1" */
#define VT_BUFFERS 3

#pragma pack(push, 8)
typedef struct VtHeader
{
    uint32_t magic;
    uint32_t width;
    uint32_t height;
    uint32_t buffers;
    // DXGI legacy shared handles of the three GPU textures. Global kernel names, not process handles,
    // so a 32-bit process can open what a 64-bit one shared; the value fits 32 bits either way.
    uint64_t gpuHandles[VT_BUFFERS];
    // Written after a buffer is complete: the index, then the frame counter (the consumer reads the
    // counter first, then the index).
    volatile int32_t latest;
    int32_t pad0;
    volatile int64_t frame;
    // QueryPerformanceCounter when each buffer was finished; QPC is system-wide, so the consumer can
    // measure producer-to-screen delay directly.
    volatile int64_t producedQpc[VT_BUFFERS];
    volatile int32_t stop; // consumer sets 1 to end the producer
    int32_t pad1;
    // CPU path: VT_BUFFERS premultiplied BGRA frames follow the header at VT_CPU_OFFSET.
} VtHeader;
#pragma pack(pop)

#define VT_CPU_OFFSET 4096
