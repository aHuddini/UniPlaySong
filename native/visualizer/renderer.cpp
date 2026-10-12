// UniPlaySong visualizer renderer (x64): renders one MilkDrop preset with projectM offscreen and hands each frame to
// UniPlaySong's UPS_MusicVisualControl through shared GPU textures (visualizer_shared.h). The music comes from
// UniPlaySong itself, through a shared ring - nothing is captured from the speakers.
//
//   UniPlaySongVisualizer.exe <preset.milk> <width> <height> <channel> [parentPid]
//       Paced by UniPlaySong: one frame each time it signals <channel>_Tick. Exits when <channel>_Frames says stop,
//       or when the parent process exits (so a crashed Playnite never leaves it running).
//
//   UniPlaySongVisualizer.exe <preset.milk> <width> <height> --bench <seconds>
//       For preset work: renders unpaced for <seconds>, touches no named objects, prints timings.
//       VIS_PCM=<file> feeds raw 48 kHz stereo float (800 frames per rendered frame); VIS_DUMP=<file.bmp> saves the
//       last frame composited over grey, as a theme would show it.
//
// projectM 4.1 always draws its final image to the default framebuffer, so it renders into a WGL pbuffer: an
// offscreen surface whose default framebuffer we own outright. projectM stays unmodified.
#define NOMINMAX
#define _CRT_SECURE_NO_WARNINGS
#include <windows.h>
#include <d3d11.h>
#include <dxgi.h>
#include <GL/glew.h>
#include <GL/wglew.h>
#include <projectM-4/projectM.h>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <vector>
#include <algorithm>
#include <string>
#include "visualizer_shared.h"
#pragma comment(lib, "d3d11.lib")
#pragma comment(lib, "dxgi.lib")
#pragma comment(lib, "opengl32.lib")
#pragma comment(lib, "glew32.lib")
#pragma comment(lib, "projectM-4.lib")
#pragma comment(lib, "user32.lib")
#pragma comment(lib, "gdi32.lib")

static void Fail(const char* what, long code = 0) { std::fprintf(stderr, "%s failed (0x%08lx)\n", what, (unsigned long)code); std::exit(1); }
static double Ms(LARGE_INTEGER a, LARGE_INTEGER b, LARGE_INTEGER f) { return (b.QuadPart - a.QuadPart) * 1000.0 / f.QuadPart; }

// ---- OpenGL 3.3 core in an offscreen pbuffer -----------------------------------------------------------------
static void CreateGl(int w, int h)
{
    WNDCLASSW wc = {}; wc.lpfnWndProc = DefWindowProcW; wc.hInstance = GetModuleHandleW(nullptr); wc.lpszClassName = L"UniPlaySongVisualizer";
    RegisterClassW(&wc);
    HWND hwnd = CreateWindowW(wc.lpszClassName, L"", WS_POPUP, 0, 0, 1, 1, nullptr, nullptr, wc.hInstance, nullptr); // never shown
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

// ---- the music, from UniPlaySong's ring ----------------------------------------------------------------------
struct AudioRing
{
    const VisAudio* hdr = nullptr;
    const float* data = nullptr;
    uint32_t fed = 0;      // the frame fed up to
    bool started = false;
    std::vector<float> chunk;

    void Open(const std::wstring& name)
    {
        HANDLE map = OpenFileMappingW(FILE_MAP_READ, FALSE, name.c_str());
        if (!map) { std::printf("audio: no ring at the channel, rendering without music\n"); return; }
        auto* base = (const uint8_t*)MapViewOfFile(map, FILE_MAP_READ, 0, 0, 0);
        if (!base) return;
        hdr = (const VisAudio*)base;
        data = (const float*)(base + VIS_AUDIO_DATA_OFFSET);
    }

    // Everything from the last feed up to the frame being heard. Unsigned differences survive the counters wrapping.
    void Feed(projectm_handle pm)
    {
        if (!hdr || hdr->magic != VIS_AUDIO_MAGIC) return;
        uint32_t played = hdr->played, written = hdr->written, cap = hdr->capacity;
        if (played - written < 0x80000000u && played != written) played = written; // clock ahead of the data: hold at the newest
        uint32_t span = played - fed;
        // First feed, a long pause, a player switch or a seek: start fresh a short window back instead of replaying it.
        if (!started || span > 8192 || span >= 0x80000000u) { fed = played - 1024; started = true; span = 1024; }
        if (written - fed > cap) { fed = written - cap + 1; span = played - fed; } // the oldest frames were overwritten
        if (span == 0) return;
        chunk.resize((size_t)span * 2);
        for (uint32_t i = 0; i < span; i++)
        {
            uint32_t at = (fed + i) & (cap - 1);
            chunk[2 * (size_t)i] = data[2 * (size_t)at];
            chunk[2 * (size_t)i + 1] = data[2 * (size_t)at + 1];
        }
        projectm_pcm_add_float(pm, chunk.data(), span, PROJECTM_STEREO);
        fed = played;
    }
};

static void WriteBmp(const char* path, const std::vector<uint8_t>& bgra, int w, int h)
{
    FILE* f = std::fopen(path, "wb");
    if (!f) return;
    int rowBytes = (w * 3 + 3) & ~3;
    BITMAPFILEHEADER fh = {}; BITMAPINFOHEADER ih = {};
    fh.bfType = 0x4D42; fh.bfOffBits = sizeof(fh) + sizeof(ih); fh.bfSize = fh.bfOffBits + rowBytes * h;
    ih.biSize = sizeof(ih); ih.biWidth = w; ih.biHeight = -h; ih.biPlanes = 1; ih.biBitCount = 24;
    std::fwrite(&fh, sizeof(fh), 1, f); std::fwrite(&ih, sizeof(ih), 1, f);
    std::vector<uint8_t> row(rowBytes);
    for (int y = 0; y < h; y++)
    {
        const uint8_t* s = bgra.data() + (size_t)y * w * 4;
        for (int x = 0; x < w; x++, s += 4)
            for (int c = 0; c < 3; c++) row[x * 3 + c] = (uint8_t)std::min(255, s[c] + 96 * (255 - s[3]) / 255);
        std::fwrite(row.data(), rowBytes, 1, f);
    }
    std::fclose(f);
}

int main(int argc, char** argv)
{
    if (argc < 5)
    {
        std::fprintf(stderr, "usage: UniPlaySongVisualizer <preset.milk> <width> <height> <channel> [parentPid]\n"
                             "       UniPlaySongVisualizer <preset.milk> <width> <height> --bench <seconds>\n");
        return 2;
    }
    const char* preset = argv[1];
    int w = std::atoi(argv[2]), h = std::atoi(argv[3]);
    if (w < 16 || h < 16 || w > 4096 || h > 4096) { std::fprintf(stderr, "bad size %dx%d\n", w, h); return 2; }
    bool bench = !std::strcmp(argv[4], "--bench");
    int benchSeconds = bench ? (argc > 5 ? std::atoi(argv[5]) : 6) : 0;
    std::wstring channel;
    if (!bench) { std::string c = argv[4]; channel.assign(c.begin(), c.end()); }
    HANDLE parent = !bench && argc > 5 ? OpenProcess(SYNCHRONIZE, FALSE, (DWORD)std::atoi(argv[5])) : nullptr;

    // ---- frames: three shared D3D11 textures + the header UniPlaySong reads
    ID3D11Device* dev = nullptr; ID3D11DeviceContext* d3d = nullptr;
    if (FAILED(D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, D3D11_CREATE_DEVICE_BGRA_SUPPORT, nullptr, 0, D3D11_SDK_VERSION, &dev, nullptr, &d3d)))
        Fail("D3D11CreateDevice");
    size_t frameBytes = (size_t)w * h * 4;
    HANDLE map = CreateFileMappingW(INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE, 0, (DWORD)(VIS_CPU_OFFSET + frameBytes * VIS_BUFFERS),
                                    bench ? nullptr : (channel + L"_Frames").c_str()); // a bench run has no viewer: unnamed
    if (!map) Fail("CreateFileMapping", GetLastError());
    auto* hdr = (VisFrames*)MapViewOfFile(map, FILE_MAP_ALL_ACCESS, 0, 0, 0);
    ZeroMemory(hdr, sizeof(VisFrames));
    ID3D11Texture2D* tex[VIS_BUFFERS];
    for (int i = 0; i < VIS_BUFFERS; i++)
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
    hdr->width = w; hdr->height = h; hdr->buffers = VIS_BUFFERS; hdr->latest = -1;
    MemoryBarrier();
    hdr->magic = VIS_FRAMES_MAGIC;
    HANDLE tick = CreateEventW(nullptr, FALSE, FALSE, bench ? nullptr : (channel + L"_Tick").c_str());

    // ---- projectM, one preset, no playlist
    CreateGl(w, h);
    projectm_handle pm = projectm_create();
    if (!pm) Fail("projectm_create");
    projectm_set_window_size(pm, w, h);
    projectm_set_fps(pm, 60);
    projectm_set_mesh_size(pm, 48, 32);
    projectm_set_preset_switch_failed_event_callback(pm, [](const char* file, const char* message, void*) {
        std::printf("preset failed: %s\n  %s\n", file, message); std::fflush(stdout); }, nullptr);
    projectm_load_preset_file(pm, preset, false);
    std::printf("projectM %dx%d, preset %s\n", w, h, preset);
    std::fflush(stdout);

    AudioRing ring;
    std::vector<float> pcm;
    size_t pcmPos = 0;
    if (!bench) ring.Open(channel + L"_Audio");
    else if (const char* pcmPath = std::getenv("VIS_PCM"))
        if (FILE* f = std::fopen(pcmPath, "rb"))
        {
            std::fseek(f, 0, SEEK_END); pcm.resize(std::ftell(f) / sizeof(float)); std::fseek(f, 0, SEEK_SET);
            pcm.resize(std::fread(pcm.data(), sizeof(float), pcm.size(), f));
            std::fclose(f);
        }

    std::vector<uint8_t> gl(frameBytes), out(frameBytes);
    LARGE_INTEGER freq, start, t0, t1, t2, t3; QueryPerformanceFrequency(&freq); QueryPerformanceCounter(&start);
    double sumRender = 0, sumRead = 0, sumConvert = 0;
    std::vector<double> totals;
    int64_t frame = 0;
    while (!hdr->stop)
    {
        if (bench)
        {
            LARGE_INTEGER now; QueryPerformanceCounter(&now);
            if (Ms(start, now, freq) > benchSeconds * 1000.0) break;
        }
        else
            WaitForSingleObject(tick, 50); // one frame per displayed frame on UniPlaySong's side
        if (parent && WaitForSingleObject(parent, 0) == WAIT_OBJECT_0) break;
        if (hdr->stop) break;
        int i = (int)(frame % VIS_BUFFERS);

        QueryPerformanceCounter(&t0);
        ring.Feed(pm);
        if (pcm.size() >= 1600)
        {
            if (pcmPos + 1600 > pcm.size()) pcmPos = 0;
            projectm_pcm_add_float(pm, pcm.data() + pcmPos, 800, PROJECTM_STEREO);
            pcmPos += 1600;
        }
        projectm_opengl_render_frame(pm);
        glFinish();
        QueryPerformanceCounter(&t1);
        // Presets with a composite shader leave projectM's own framebuffer bound for reading; the finished image is
        // always in framebuffer 0. Reading the bound one returns black.
        glBindFramebuffer(GL_READ_FRAMEBUFFER, 0);
        glReadPixels(0, 0, w, h, GL_BGRA, GL_UNSIGNED_BYTE, gl.data());
        QueryPerformanceCounter(&t2);

        // GL rows run bottom-up: flip, and key out the dark. Premultiplied BGRA with coverage = the brightest channel,
        // so black is see-through and the theme shows behind; the presets crush faint haze to black for this.
        for (int y = 0; y < h; y++)
        {
            const uint8_t* s = gl.data() + (size_t)(h - 1 - y) * w * 4;
            uint8_t* d = out.data() + (size_t)y * w * 4;
            for (int x = 0; x < w; x++, s += 4, d += 4)
            {
                d[0] = s[0]; d[1] = s[1]; d[2] = s[2];
                d[3] = std::max(s[0], std::max(s[1], s[2]));
            }
        }
        d3d->UpdateSubresource(tex[i], 0, nullptr, out.data(), w * 4, 0);
        d3d->Flush();
        QueryPerformanceCounter(&t3);

        hdr->producedQpc[i] = t3.QuadPart;
        hdr->latest = i;
        MemoryBarrier();
        hdr->frame = ++frame;
        sumRender += Ms(t0, t1, freq); sumRead += Ms(t1, t2, freq); sumConvert += Ms(t2, t3, freq);
        if (bench) totals.push_back(Ms(t0, t3, freq));
    }

    if (bench)
    {
        if (const char* dump = std::getenv("VIS_DUMP")) WriteBmp(dump, out, w, h);
        QueryPerformanceCounter(&t3);
        double secs = Ms(start, t3, freq) / 1000.0;
        if (frame > 0)
        {
            std::sort(totals.begin(), totals.end());
            std::printf("frames %lld in %.1f s (%.1f fps); per frame: render %.2f ms, readback %.2f ms, convert+upload %.2f ms; p95 total %.2f ms\n",
                        (long long)frame, secs, frame / secs, sumRender / frame, sumRead / frame, sumConvert / frame,
                        totals[totals.size() * 95 / 100]);
        }
    }
    projectm_destroy(pm);
    hdr->magic = 0;
    return 0;
}
