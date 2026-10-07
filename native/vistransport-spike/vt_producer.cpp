// SPIKE A producer (x64): stands in for projectM. Renders an animated, partly transparent pattern into
// three shared D3D11 textures (GPU path) and into a shared-memory BGRA ring (CPU path), round-robin,
// at a fixed rate, and stamps each finished buffer with QPC.
//
//   vt_producer.exe [width] [height] [fps] [seconds] [timer|ondemand]
// ondemand: render one frame each time the consumer signals a display frame (falls back to the timer rate if
// no signal arrives within 50 ms), so frames line up with the screen instead of drifting against it.
#include <windows.h>
#include <d3d11.h>
#include <dxgi.h>
#include <cstdio>
#include <cmath>
#include <cstdlib>
#include <cstring>
#include <climits>
#include <vector>
#include "vt_shared.h"
#pragma comment(lib, "d3d11.lib")
#pragma comment(lib, "dxgi.lib")
#pragma comment(lib, "winmm.lib")

static void Fail(const char* what, HRESULT hr) { std::fprintf(stderr, "%s failed: 0x%08lx\n", what, (unsigned long)hr); std::exit(1); }

// Premultiplied BGRA: moving colour bands, a soft-edged disc of opacity that orbits the frame, fully
// transparent outside it. Integer-only so the producer itself is never the bottleneck.
static void Paint(uint32_t* px, int w, int h, int t)
{
    int cx = w / 2 + (int)(w / 4 * std::sin(t * 0.03)), cy = h / 2 + (int)(h / 4 * std::cos(t * 0.021));
    int r = h / 3, r2 = r * r;
    for (int y = 0; y < h; y++)
    {
        int dy = y - cy;
        uint32_t* row = px + (size_t)y * w;
        for (int x = 0; x < w; x++)
        {
            int dx = x - cx, d2 = dx * dx + dy * dy;
            uint32_t a = d2 >= r2 ? 0u : (uint32_t)(255 - (int64_t)255 * d2 / r2);
            uint32_t rr = ((x + t * 3) & 255), gg = ((y + t * 2) & 255), bb = (((x ^ y) + t) & 255);
            rr = rr * a / 255; gg = gg * a / 255; bb = bb * a / 255; // premultiply
            row[x] = (a << 24) | (rr << 16) | (gg << 8) | bb;
        }
    }
}

int main(int argc, char** argv)
{
    int w = argc > 1 ? std::atoi(argv[1]) : 1280;
    int h = argc > 2 ? std::atoi(argv[2]) : 720;
    int fps = argc > 3 ? std::atoi(argv[3]) : 60;
    int seconds = argc > 4 ? std::atoi(argv[4]) : 30;
    bool onDemand = argc > 5 && std::strcmp(argv[5], "ondemand") == 0;
    HANDLE tick = CreateEventW(nullptr, FALSE, FALSE, L"Local\\UPS_VisualTransport_Spike_Tick");

    ID3D11Device* dev = nullptr; ID3D11DeviceContext* ctx = nullptr;
    HRESULT hr = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, D3D11_CREATE_DEVICE_BGRA_SUPPORT,
                                   nullptr, 0, D3D11_SDK_VERSION, &dev, nullptr, &ctx);
    if (FAILED(hr)) Fail("D3D11CreateDevice", hr);

    size_t frameBytes = (size_t)w * h * 4;
    HANDLE map = CreateFileMappingW(INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE, 0,
                                    (DWORD)(VT_CPU_OFFSET + frameBytes * VT_BUFFERS), VT_MAPPING_NAME);
    if (!map) Fail("CreateFileMapping", HRESULT_FROM_WIN32(GetLastError()));
    auto* base = (uint8_t*)MapViewOfFile(map, FILE_MAP_ALL_ACCESS, 0, 0, 0);
    auto* hdr = (VtHeader*)base;
    ZeroMemory(hdr, sizeof(VtHeader));

    ID3D11Texture2D* tex[VT_BUFFERS];
    for (int i = 0; i < VT_BUFFERS; i++)
    {
        D3D11_TEXTURE2D_DESC d = {};
        d.Width = w; d.Height = h; d.MipLevels = 1; d.ArraySize = 1;
        d.Format = DXGI_FORMAT_B8G8R8A8_UNORM; d.SampleDesc.Count = 1; d.Usage = D3D11_USAGE_DEFAULT;
        d.BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE;
        d.MiscFlags = D3D11_RESOURCE_MISC_SHARED; // legacy handle: what D3D9Ex (WPF's D3DImage) can open
        hr = dev->CreateTexture2D(&d, nullptr, &tex[i]);
        if (FAILED(hr)) Fail("CreateTexture2D", hr);
        IDXGIResource* res = nullptr;
        tex[i]->QueryInterface(__uuidof(IDXGIResource), (void**)&res);
        HANDLE sh = nullptr;
        hr = res->GetSharedHandle(&sh);
        if (FAILED(hr)) Fail("GetSharedHandle", hr);
        hdr->gpuHandles[i] = (uint64_t)(uintptr_t)sh;
        res->Release();
    }
    hdr->width = w; hdr->height = h; hdr->buffers = VT_BUFFERS; hdr->latest = -1;
    MemoryBarrier();
    hdr->magic = VT_MAGIC;
    std::printf("producer: %dx%d @ %d fps, handles %llx %llx %llx\n", w, h, fps,
                (unsigned long long)hdr->gpuHandles[0], (unsigned long long)hdr->gpuHandles[1], (unsigned long long)hdr->gpuHandles[2]);
    std::fflush(stdout);

    std::vector<uint32_t> px((size_t)w * h);
    LARGE_INTEGER freq, start, now; QueryPerformanceFrequency(&freq); QueryPerformanceCounter(&start);
    timeBeginPeriod(1);
    int64_t frame = 0, total = onDemand ? LLONG_MAX : (int64_t)fps * seconds; // on demand: until the consumer sets stop
    for (; frame < total && !hdr->stop; frame++)
    {
        int i = (int)(frame % VT_BUFFERS);
        Paint(px.data(), w, h, (int)frame);
        ctx->UpdateSubresource(tex[i], 0, nullptr, px.data(), w * 4, 0);
        ctx->Flush();
        memcpy(base + VT_CPU_OFFSET + frameBytes * i, px.data(), frameBytes);
        QueryPerformanceCounter(&now);
        hdr->producedQpc[i] = now.QuadPart;
        hdr->latest = i;
        MemoryBarrier();
        hdr->frame = frame + 1;

        if (onDemand)
        {
            WaitForSingleObject(tick, 50);
            continue;
        }
        // Pace to the target rate from the start time, so drift doesn't accumulate.
        int64_t due = start.QuadPart + (frame + 1) * freq.QuadPart / fps;
        for (;;)
        {
            QueryPerformanceCounter(&now);
            int64_t left = due - now.QuadPart;
            if (left <= 0) break;
            DWORD ms = (DWORD)(left * 1000 / freq.QuadPart);
            if (ms > 1) Sleep(ms - 1); else YieldProcessor();
        }
    }
    timeEndPeriod(1);
    QueryPerformanceCounter(&now);
    std::printf("producer: %lld frames in %.2f s\n", (long long)frame, (double)(now.QuadPart - start.QuadPart) / freq.QuadPart);
    hdr->magic = 0;
    for (auto t : tex) t->Release();
    ctx->Release(); dev->Release();
    UnmapViewOfFile(base); CloseHandle(map);
    return 0;
}
