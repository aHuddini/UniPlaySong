// SPIKE B renderer (x64): projectM MilkDrop presets rendered offscreen, handed to the WPF side through the
// Spike A transport (vt_shared.h: three shared D3D11 textures, one frame per consumer signal).
//
//   vt_milkdrop.exe <presetDir> [width] [height] [opaque|luma|additive] [presetSeconds]
//
// projectM 4.1 always draws its final image to the default framebuffer (ProjectM.cpp: "ToDo: Allow external
// apps to provide a custom target framebuffer"), so it renders into a WGL pbuffer: an offscreen surface whose
// default framebuffer we own outright, unlike a hidden window's, whose pixels a driver may discard. projectM
// stays unmodified.
//
// Audio: WASAPI loopback of the default output, polled on the render thread (spike only; the feature will take
// UPS's own PCM instead of everything the PC plays).
#define NOMINMAX
#include <windows.h>
#include <d3d11.h>
#include <dxgi.h>
#include <mmdeviceapi.h>
#include <audioclient.h>
#include <GL/glew.h>
#include <GL/wglew.h>
#include <projectM-4/projectM.h>
#include <projectM-4/playlist.h>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <climits>
#include <vector>
#include <algorithm>
#include "vt_shared.h"
#pragma comment(lib, "d3d11.lib")
#pragma comment(lib, "dxgi.lib")
#pragma comment(lib, "opengl32.lib")
#pragma comment(lib, "glew32.lib")
#pragma comment(lib, "projectM-4.lib")
#pragma comment(lib, "projectM-4-playlist.lib")
#pragma comment(lib, "ole32.lib")
#pragma comment(lib, "user32.lib")
#pragma comment(lib, "gdi32.lib")

static void Fail(const char* what, long code = 0) { std::fprintf(stderr, "%s failed (0x%08lx)\n", what, (unsigned long)code); std::exit(1); }

// ---- OpenGL 3.3 core in an offscreen pbuffer -------------------------------------------------------------
static void CreateGl(int w, int h)
{
    WNDCLASSW wc = {}; wc.lpfnWndProc = DefWindowProcW; wc.hInstance = GetModuleHandleW(nullptr); wc.lpszClassName = L"vt_milkdrop";
    RegisterClassW(&wc);
    HWND hwnd = CreateWindowW(L"vt_milkdrop", L"", WS_POPUP, 0, 0, 1, 1, nullptr, nullptr, wc.hInstance, nullptr); // never shown
    HDC dc = GetDC(hwnd);
    PIXELFORMATDESCRIPTOR pfd = { sizeof(pfd), 1, PFD_DRAW_TO_WINDOW | PFD_SUPPORT_OPENGL, PFD_TYPE_RGBA, 32 };
    SetPixelFormat(dc, ChoosePixelFormat(dc, &pfd), &pfd);
    HGLRC bootstrap = wglCreateContext(dc);
    wglMakeCurrent(dc, bootstrap);
    if (glewInit() != GLEW_OK) Fail("glewInit (bootstrap)");
    if (!WGLEW_ARB_pbuffer || !WGLEW_ARB_pixel_format || !WGLEW_ARB_create_context) Fail("WGL pbuffer/create_context extensions");

    const int fmtAttribs[] = { WGL_DRAW_TO_PBUFFER_ARB, 1, WGL_SUPPORT_OPENGL_ARB, 1, WGL_PIXEL_TYPE_ARB, WGL_TYPE_RGBA_ARB,
                               WGL_COLOR_BITS_ARB, 32, WGL_ALPHA_BITS_ARB, 8, WGL_DEPTH_BITS_ARB, 0, 0 };
    int fmt = 0; UINT count = 0;
    if (!wglChoosePixelFormatARB(dc, fmtAttribs, nullptr, 1, &fmt, &count) || count == 0) Fail("wglChoosePixelFormatARB");
    const int pbAttribs[] = { 0 };
    HPBUFFERARB pb = wglCreatePbufferARB(dc, fmt, w, h, pbAttribs);
    if (!pb) Fail("wglCreatePbufferARB", GetLastError());
    HDC pbDc = wglGetPbufferDCARB(pb);
    const int ctxAttribs[] = { WGL_CONTEXT_MAJOR_VERSION_ARB, 3, WGL_CONTEXT_MINOR_VERSION_ARB, 3,
                               WGL_CONTEXT_PROFILE_MASK_ARB, WGL_CONTEXT_CORE_PROFILE_BIT_ARB, 0 };
    HGLRC ctx = wglCreateContextAttribsARB(pbDc, nullptr, ctxAttribs);
    if (!ctx) Fail("wglCreateContextAttribsARB", GetLastError());
    wglMakeCurrent(pbDc, ctx);
    wglDeleteContext(bootstrap);
    glewExperimental = GL_TRUE;
    if (glewInit() != GLEW_OK) Fail("glewInit (core)");
    glGetError(); // glewInit on a core profile leaves a harmless GL_INVALID_ENUM
    std::printf("GL: %s | %s\n", (const char*)glGetString(GL_RENDERER), (const char*)glGetString(GL_VERSION));
}

// ---- WASAPI loopback, polled ------------------------------------------------------------------------------
struct Loopback
{
    IAudioClient* client = nullptr; IAudioCaptureClient* capture = nullptr; WAVEFORMATEX* fmt = nullptr; bool isFloat = false;
    std::vector<float> stereo;

    void Open()
    {
        CoInitializeEx(nullptr, COINIT_MULTITHREADED);
        IMMDeviceEnumerator* en = nullptr; IMMDevice* dev = nullptr;
        if (FAILED(CoCreateInstance(__uuidof(MMDeviceEnumerator), nullptr, CLSCTX_ALL, __uuidof(IMMDeviceEnumerator), (void**)&en))) Fail("MMDeviceEnumerator");
        if (FAILED(en->GetDefaultAudioEndpoint(eRender, eConsole, &dev))) Fail("GetDefaultAudioEndpoint");
        if (FAILED(dev->Activate(__uuidof(IAudioClient), CLSCTX_ALL, nullptr, (void**)&client))) Fail("Activate IAudioClient");
        client->GetMixFormat(&fmt);
        isFloat = fmt->wFormatTag == WAVE_FORMAT_IEEE_FLOAT ||
                  (fmt->wFormatTag == WAVE_FORMAT_EXTENSIBLE && ((WAVEFORMATEXTENSIBLE*)fmt)->SubFormat == KSDATAFORMAT_SUBTYPE_IEEE_FLOAT);
        if (FAILED(client->Initialize(AUDCLNT_SHAREMODE_SHARED, AUDCLNT_STREAMFLAGS_LOOPBACK, 10000000, 0, fmt, nullptr))) Fail("IAudioClient::Initialize");
        client->GetService(__uuidof(IAudioCaptureClient), (void**)&capture);
        client->Start();
        std::printf("audio: loopback %lu Hz, %u ch, %s\n", fmt->nSamplesPerSec, fmt->nChannels, isFloat ? "float" : "non-float (ignored)");
    }

    // Everything captured since the last call goes to projectM as interleaved stereo float.
    void Drain(projectm_handle pm)
    {
        UINT32 packet = 0;
        while (SUCCEEDED(capture->GetNextPacketSize(&packet)) && packet > 0)
        {
            BYTE* data; UINT32 frames; DWORD flags;
            if (FAILED(capture->GetBuffer(&data, &frames, &flags, nullptr, nullptr))) break;
            if (isFloat && frames > 0)
            {
                int ch = fmt->nChannels;
                stereo.resize((size_t)frames * 2);
                const float* src = (const float*)data;
                for (UINT32 i = 0; i < frames; i++)
                {
                    bool silent = (flags & AUDCLNT_BUFFERFLAGS_SILENT) != 0;
                    stereo[2 * i] = silent ? 0.f : src[(size_t)i * ch];
                    stereo[2 * i + 1] = silent ? 0.f : src[(size_t)i * ch + (ch > 1 ? 1 : 0)];
                }
                projectm_pcm_add_float(pm, stereo.data(), frames, PROJECTM_STEREO);
            }
            capture->ReleaseBuffer(frames);
        }
    }
};

static double Ms(LARGE_INTEGER a, LARGE_INTEGER b, LARGE_INTEGER f) { return (b.QuadPart - a.QuadPart) * 1000.0 / f.QuadPart; }

int main(int argc, char** argv)
{
    if (argc < 2) { std::fprintf(stderr, "usage: vt_milkdrop <presetDir> [w] [h] [opaque|luma|additive] [presetSeconds]\n"); return 2; }
    const char* presetDir = argv[1];
    int w = argc > 2 ? std::atoi(argv[2]) : 1280, h = argc > 3 ? std::atoi(argv[3]) : 720;
    const char* mode = argc > 4 ? argv[4] : "luma";
    int presetSeconds = argc > 5 ? std::atoi(argv[5]) : 10;
    int alphaMode = !std::strcmp(mode, "opaque") ? 0 : !std::strcmp(mode, "additive") ? 2 : 1;
    // "free": no consumer pacing, render as fast as possible for N seconds (raw throughput measurement).
    // Isolation switches via the environment: VT_NORENDER skips projectM, VT_NOUPLOAD skips readback + upload.
    bool noRender = std::getenv("VT_NORENDER") != nullptr, noUpload = std::getenv("VT_NOUPLOAD") != nullptr;
    int freeSeconds = argc > 6 && !std::strcmp(argv[6], "free") ? (argc > 7 ? std::atoi(argv[7]) : 10) : 0;

    // D3D11 side: identical to the Spike A producer, so the consumer and Playnite harness need no changes.
    ID3D11Device* dev = nullptr; ID3D11DeviceContext* d3d = nullptr;
    if (FAILED(D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, D3D11_CREATE_DEVICE_BGRA_SUPPORT, nullptr, 0, D3D11_SDK_VERSION, &dev, nullptr, &d3d)))
        Fail("D3D11CreateDevice");
    size_t frameBytes = (size_t)w * h * 4;
    HANDLE map = CreateFileMappingW(INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE, 0, (DWORD)(VT_CPU_OFFSET + frameBytes * VT_BUFFERS), VT_MAPPING_NAME);
    auto* base = (uint8_t*)MapViewOfFile(map, FILE_MAP_ALL_ACCESS, 0, 0, 0);
    auto* hdr = (VtHeader*)base;
    ZeroMemory(hdr, sizeof(VtHeader));
    ID3D11Texture2D* tex[VT_BUFFERS];
    for (int i = 0; i < VT_BUFFERS; i++)
    {
        D3D11_TEXTURE2D_DESC d = {};
        d.Width = w; d.Height = h; d.MipLevels = 1; d.ArraySize = 1; d.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
        d.SampleDesc.Count = 1; d.Usage = D3D11_USAGE_DEFAULT; d.BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE;
        d.MiscFlags = D3D11_RESOURCE_MISC_SHARED;
        if (FAILED(dev->CreateTexture2D(&d, nullptr, &tex[i]))) Fail("CreateTexture2D");
        IDXGIResource* res = nullptr; tex[i]->QueryInterface(__uuidof(IDXGIResource), (void**)&res);
        HANDLE sh = nullptr; res->GetSharedHandle(&sh); res->Release();
        hdr->gpuHandles[i] = (uint64_t)(uintptr_t)sh;
    }
    hdr->width = w; hdr->height = h; hdr->buffers = VT_BUFFERS; hdr->latest = -1;
    MemoryBarrier();
    hdr->magic = VT_MAGIC;
    HANDLE tick = CreateEventW(nullptr, FALSE, FALSE, L"Local\\UPS_VisualTransport_Spike_Tick");

    CreateGl(w, h);
    projectm_handle pm = projectm_create();
    if (!pm) Fail("projectm_create");
    projectm_set_window_size(pm, w, h);
    projectm_set_fps(pm, 60);
    projectm_set_preset_duration(pm, presetSeconds);
    projectm_set_soft_cut_duration(pm, 3);
    projectm_set_mesh_size(pm, 48, 32);
    projectm_playlist_handle pl = projectm_playlist_create(pm);
    uint32_t added = projectm_playlist_add_path(pl, presetDir, true, false);
    projectm_playlist_set_shuffle(pl, true);
    projectm_playlist_play_next(pl, true);
    std::printf("projectM %dx%d, %u presets from %s, alpha mode %s\n", w, h, added, presetDir, mode);
    std::fflush(stdout);

    Loopback audio; audio.Open();
    std::vector<uint8_t> gl(frameBytes), out(frameBytes);
    LARGE_INTEGER freq, start, t0, t1, t2, t3; QueryPerformanceFrequency(&freq); QueryPerformanceCounter(&start);
    double sumRender = 0, sumRead = 0, sumConvert = 0, maxTotal = 0;
    int64_t frame = 0;
    while (!hdr->stop)
    {
        if (freeSeconds > 0)
        {
            LARGE_INTEGER now; QueryPerformanceCounter(&now);
            if (Ms(start, now, freq) > freeSeconds * 1000.0) break;
        }
        else
            WaitForSingleObject(tick, 50); // one frame per display frame on the consumer side
        if (hdr->stop) break;
        int i = (int)(frame % VT_BUFFERS);

        QueryPerformanceCounter(&t0);
        audio.Drain(pm);
        if (!noRender) projectm_opengl_render_frame(pm);
        glFinish();
        QueryPerformanceCounter(&t1);
        if (!noUpload) glReadPixels(0, 0, w, h, GL_BGRA, GL_UNSIGNED_BYTE, gl.data());
        QueryPerformanceCounter(&t2);

        // GL rows run bottom-up; flip, and derive alpha. Output is premultiplied BGRA: luma keeps the colour and uses
        // the brightest channel as coverage (dark = see-through); additive uses alpha 0, which premultiplied blending
        // turns into pure addition onto the theme; opaque is alpha 255.
        for (int y = 0; y < h; y++)
        {
            const uint8_t* s = gl.data() + (size_t)(h - 1 - y) * w * 4;
            uint8_t* d = out.data() + (size_t)y * w * 4;
            for (int x = 0; x < w; x++, s += 4, d += 4)
            {
                d[0] = s[0]; d[1] = s[1]; d[2] = s[2];
                d[3] = alphaMode == 0 ? 255 : alphaMode == 2 ? 0 : std::max(s[0], std::max(s[1], s[2]));
            }
        }
        if (!noUpload) { d3d->UpdateSubresource(tex[i], 0, nullptr, out.data(), w * 4, 0); d3d->Flush(); }
        QueryPerformanceCounter(&t3);

        hdr->producedQpc[i] = t3.QuadPart;
        hdr->latest = i;
        MemoryBarrier();
        hdr->frame = ++frame;
        double render = Ms(t0, t1, freq), read = Ms(t1, t2, freq), conv = Ms(t2, t3, freq);
        sumRender += render; sumRead += read; sumConvert += conv; maxTotal = std::max(maxTotal, render + read + conv);
    }
    QueryPerformanceCounter(&t3);
    double secs = Ms(start, t3, freq) / 1000.0;
    if (frame > 0)
        std::printf("frames %lld in %.1f s (%.1f fps); per frame: render %.2f ms, readback %.2f ms, convert+upload %.2f ms; worst total %.1f ms\n",
                    (long long)frame, secs, frame / secs, sumRender / frame, sumRead / frame, sumConvert / frame, maxTotal);
    projectm_playlist_destroy(pl);
    projectm_destroy(pm);
    hdr->magic = 0;
    return 0;
}
