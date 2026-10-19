// UniPlaySong visualizer renderer (x64): renders one style offscreen and hands each frame to UniPlaySong's
// UPS_MusicVisualControl through shared GPU textures (visualizer_shared.h). A style is a MilkDrop preset (.milk, drawn by
// projectM) or UniPlaySong's own fountain (.vis, fountain.h). The music comes from UniPlaySong itself, through a shared
// ring - nothing is captured from the speakers.
//
//   UniPlaySongVisualizer.exe <style.milk|.vis> <width> <height> <channel> [parentPid]
//       Paced by UniPlaySong: one frame each time it signals <channel>_Tick, and none while it doesn't (minimized,
//       idle, in the background). Exits when <channel>_Frames says stop, or when the parent process exits (so a crashed
//       Playnite never leaves it running).
//
//   UniPlaySongVisualizer.exe <style.milk|.vis> <width> <height> --bench <seconds>
//       For style work: renders unpaced for <seconds>, touches no named objects, prints timings.
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
#include <thread>
#include <mutex>
#include <atomic>
#include <memory>
#include "visualizer_shared.h"
#include "fountain.h"
#include "analysis.h"
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

// ---- the key pass: the frame as UniPlaySong shows it, made on the GPU ---------------------------------------------
// Top row first (GL's rows run bottom-up), premultiplied BGRA with coverage = the brightest channel, so black is
// see-through and the theme shows behind. Done per pixel on the CPU this took ~1.5 ms a frame at 1728x712; here the CPU
// only uploads what it reads back. projectM keeps its GL state across frames, so the pass puts back what it touches.
struct KeyPass
{
    GLuint src = 0, dst = 0, fbo = 0, prog = 0, vao = 0;

    static GLuint Shader(GLenum type, const char* code)
    {
        GLuint sh = glCreateShader(type);
        glShaderSource(sh, 1, &code, nullptr);
        glCompileShader(sh);
        GLint ok = 0; glGetShaderiv(sh, GL_COMPILE_STATUS, &ok);
        if (!ok) { char log[1024] = {}; glGetShaderInfoLog(sh, sizeof(log), nullptr, log); std::fprintf(stderr, "%s\n", log); Fail("key pass shader"); }
        return sh;
    }

    static GLuint Target(int w, int h)
    {
        GLuint t = 0;
        glGenTextures(1, &t);
        glBindTexture(GL_TEXTURE_2D, t);
        glTexImage2D(GL_TEXTURE_2D, 0, GL_RGBA8, w, h, 0, GL_RGBA, GL_UNSIGNED_BYTE, nullptr);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_NEAREST);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_NEAREST);
        return t;
    }

    void Create(int w, int h)
    {
        src = Target(w, h);
        dst = Target(w, h);
        glBindTexture(GL_TEXTURE_2D, 0);
        glGenFramebuffers(1, &fbo);
        glBindFramebuffer(GL_FRAMEBUFFER, fbo);
        glFramebufferTexture2D(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, dst, 0);
        if (glCheckFramebufferStatus(GL_FRAMEBUFFER) != GL_FRAMEBUFFER_COMPLETE) Fail("key pass framebuffer");
        glBindFramebuffer(GL_FRAMEBUFFER, 0);
        // One triangle covering the target; each pixel takes the source pixel from the mirrored row.
        GLuint vs = Shader(GL_VERTEX_SHADER, "#version 330 core\n"
            "void main() { vec2 p = vec2((gl_VertexID << 1) & 2, gl_VertexID & 2); gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0); }");
        GLuint fs = Shader(GL_FRAGMENT_SHADER, "#version 330 core\n"
            "uniform sampler2D src; out vec4 color;\n"
            "void main() { ivec2 p = ivec2(gl_FragCoord.xy);\n"
            "  vec3 c = texelFetch(src, ivec2(p.x, textureSize(src, 0).y - 1 - p.y), 0).rgb;\n"
            "  color = vec4(c, max(c.r, max(c.g, c.b))); }");
        prog = glCreateProgram();
        glAttachShader(prog, vs); glAttachShader(prog, fs);
        glLinkProgram(prog);
        GLint ok = 0; glGetProgramiv(prog, GL_LINK_STATUS, &ok);
        if (!ok) Fail("key pass link");
        glDeleteShader(vs); glDeleteShader(fs);
        glGenVertexArrays(1, &vao);
    }

    // projectM's finished frame (framebuffer 0) -> out, w*h BGRA, top row first.
    void Run(int w, int h, uint8_t* out)
    {
        GLint active, tex, program, array, view[4];
        glGetIntegerv(GL_ACTIVE_TEXTURE, &active);
        glActiveTexture(GL_TEXTURE0);
        glGetIntegerv(GL_TEXTURE_BINDING_2D, &tex);
        glGetIntegerv(GL_CURRENT_PROGRAM, &program);
        glGetIntegerv(GL_VERTEX_ARRAY_BINDING, &array);
        glGetIntegerv(GL_VIEWPORT, view);
        const GLenum caps[] = { GL_BLEND, GL_SCISSOR_TEST, GL_DEPTH_TEST, GL_CULL_FACE };
        GLboolean was[4];
        for (int i = 0; i < 4; i++) { was[i] = glIsEnabled(caps[i]); glDisable(caps[i]); }

        // Presets with a composite shader leave projectM's own framebuffer bound for reading; the finished image is
        // always in framebuffer 0. Reading the bound one returns black.
        glBindFramebuffer(GL_READ_FRAMEBUFFER, 0);
        glBindTexture(GL_TEXTURE_2D, src);
        glCopyTexSubImage2D(GL_TEXTURE_2D, 0, 0, 0, 0, 0, w, h);
        glBindFramebuffer(GL_FRAMEBUFFER, fbo);
        glViewport(0, 0, w, h);
        glUseProgram(prog);
        glBindVertexArray(vao);
        glDrawArrays(GL_TRIANGLES, 0, 3);
        glReadPixels(0, 0, w, h, GL_BGRA, GL_UNSIGNED_BYTE, out);

        glBindFramebuffer(GL_FRAMEBUFFER, 0);
        glBindVertexArray(array);
        glUseProgram(program);
        glBindTexture(GL_TEXTURE_2D, tex);
        glActiveTexture(active);
        glViewport(view[0], view[1], view[2], view[3]);
        for (int i = 0; i < 4; i++) if (was[i]) glEnable(caps[i]);
    }
};

// ---- the music, from UniPlaySong's ring ----------------------------------------------------------------------
struct AudioRing
{
    const VisAudio* hdr = nullptr;
    const float* data = nullptr;
    const uint8_t* base = nullptr;
    size_t size = 0;      // the mapping's size, for what follows the audio (the song link)
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
        this->base = base;
        MEMORY_BASIC_INFORMATION mi = {};
        if (VirtualQuery(base, &mi, sizeof(mi))) size = mi.RegionSize;
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

    // The last n frames heard, each channel on its own (the newest last); silence where there's no music.
    void Window(int n, float* L, float* R) const
    {
        std::fill(L, L + n, 0.0f); std::fill(R, R + n, 0.0f);
        if (!hdr || hdr->magic != VIS_AUDIO_MAGIC) return;
        uint32_t played = hdr->played, written = hdr->written, cap = hdr->capacity;
        if (played - written < 0x80000000u && played != written) played = written; // clock ahead of the data: the newest
        for (int i = 0; i < n; i++)
        {
            uint32_t frame = played - (uint32_t)(n - i), age = written - frame;
            if (age == 0 || age > cap || age >= 0x80000000u) continue; // not written yet, or already overwritten
            uint32_t at = frame & (cap - 1);
            L[i] = data[2 * (size_t)at]; R[i] = data[2 * (size_t)at + 1];
        }
    }
    double SampleRate() const { return hdr && hdr->magic == VIS_AUDIO_MAGIC && hdr->sampleRate ? hdr->sampleRate : 44100.0; }
};

// ---- the song, for the Jukebox Fountain: where it is now (the stamps) and its analysis (analysis.h) ----------------
struct SongLink
{
    const VisSong* vs = nullptr;
    uint32_t seenSeq = 0;
    std::mutex lock;
    std::shared_ptr<fountain::Song> ready;   // the newest finished analysis (worker threads)...
    uint32_t readyFor = 0;                   // ...and its song
    std::atomic<uint32_t> wanted{ 0 };      // the song the latest request is for
    std::atomic<uint32_t> failedFor{ 0 };   // a song whose analysis could not be made or read
    std::shared_ptr<fountain::Song> song;    // the analysis in use (the drawing thread's)
    uint32_t songFor = 0;
    std::vector<std::thread> workers;

    void Open(const AudioRing& ring)
    {
        if (ring.base && ring.size >= VIS_AUDIO_MAPPING) vs = (const VisSong*)(ring.base + VIS_SONG_OFFSET);
    }
    static std::wstring Path(const uint16_t* p)
    {
        std::wstring s;
        for (int i = 0; i < VIS_PATH_CHARS && p[i]; i++) s += (wchar_t)p[i];
        return s;
    }

    // Once a frame: a new request is analysed (or its file read) on a worker thread; a finished analysis is taken up.
    void Poll()
    {
        if (!vs || vs->magic != VIS_SONG_MAGIC) return;
        uint32_t seq = vs->seq;
        if (seq != seenSeq && !(seq & 1))
        {
            MemoryBarrier();
            uint32_t req = vs->song; int64_t bytes = vs->sourceBytes, time = vs->sourceTime;
            std::wstring file = Path(vs->analysis), pcm = Path(vs->pcm);
            MemoryBarrier();
            if (vs->seq == seq)
            {
                seenSeq = seq; wanted = req;
                if (req && file.empty()) failedFor = req;
                else if (req) workers.emplace_back([this, req, file, pcm, bytes, time] { Work(req, file, pcm, bytes, time); });
            }
        }
        std::lock_guard<std::mutex> g(lock);
        if (ready && readyFor != songFor) { song = ready; songFor = readyFor; }
    }

    // Analyses the song into its file when UniPlaySong handed its samples over, then reads the file.
    void Work(uint32_t req, std::wstring file, std::wstring pcm, int64_t bytes, int64_t time)
    {
        if (!pcm.empty())
        {
            std::vector<float> x;
            if (FILE* f = _wfopen(pcm.c_str(), L"rb"))
            {
                std::fseek(f, 0, SEEK_END); x.resize(std::ftell(f) / sizeof(float)); std::fseek(f, 0, SEEK_SET);
                x.resize(std::fread(x.data(), sizeof(float), x.size(), f)); std::fclose(f);
            }
            DeleteFileW(pcm.c_str());
            if (x.empty()) { failedFor = req; return; }
            if (wanted != req) return;
            analysis::SongMap map = analysis::BuildMap(x.data(), x.size(), 48000);
            analysis::Beats beats = analysis::TrackBeats(x.data(), x.size(), 48000);
            analysis::BeatStrengths(beats);
            if (wanted != req) return;
            analysis::Layers layers = analysis::AnalyseLayers(x.data(), x.size(), 48000, beats.beats, beats.barPhase);
            if (!analysis::Save(file, map, beats, layers, bytes, time)) std::printf("song analysis: could not write the analysis file\n");
            std::printf("song analysis: %zu beats at %.0f BPM, %zu drops, %zu sections\n", beats.beats.size(), beats.bpm, map.drops.size(), layers.sections.size()); std::fflush(stdout);
        }
        fountain::Song s;
        if (!analysis::Load(file, bytes, time, s)) { failedFor = req; return; }
        if (wanted != req) return;
        std::lock_guard<std::mutex> g(lock);
        ready = std::make_shared<fountain::Song>(std::move(s)); readyFor = req;
    }

    // Where the song is at the frame being heard: from the newest stamp at or before it, moved on by the frames since
    // (or, between two stamps of the same song, along their slope - right under the slowed-down effect too). false when
    // it isn't known (no stamps yet, or not a song file).
    bool Time(const AudioRing& ring, uint32_t* songId, double* t) const
    {
        if (!vs || vs->magic != VIS_SONG_MAGIC || !ring.hdr) return false;
        uint32_t n = vs->stamps, played = ring.hdr->played;
        if (!n) return false;
        VisStamp a = {}, b = {}; bool haveA = false, haveB = false;
        for (uint32_t k = 0; k < std::min<uint32_t>(n, VIS_STAMPS - 1); k++)   // newest first
        {
            VisStamp st = vs->stamp[(n - 1 - k) % VIS_STAMPS];
            uint32_t ahead = st.frame - played;
            if (ahead == 0 || ahead >= 0x80000000u) { a = st; haveA = true; break; }
            b = st; haveB = true;
        }
        if (!haveA || !a.song || a.seconds < 0) return false;
        const double sr = ring.SampleRate();
        double tt = a.seconds + (double)(played - a.frame) / sr;
        if (haveB && b.song == a.song)
        {
            double span = (double)(b.frame - a.frame), d = b.seconds - a.seconds;
            if (span > 0 && d >= 0 && d <= 2 * span / sr) tt = a.seconds + d * (double)(played - a.frame) / span;
        }
        *songId = a.song; *t = tt;
        return true;
    }

    void Join() { for (auto& w : workers) if (w.joinable()) w.join(); }
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

// --analyze <mono48k.f32> <out.json>: the Jukebox Fountain's song analysis on one song, as JSON - for
// bench/tools/analysis_parity.mjs, which runs the bench's own analysis on the same samples and compares.
static int AnalyzeToJson(const char* pcmPath, const char* outPath)
{
    FILE* f = std::fopen(pcmPath, "rb");
    if (!f) { std::fprintf(stderr, "no %s\n", pcmPath); return 1; }
    std::fseek(f, 0, SEEK_END); std::vector<float> x(std::ftell(f) / sizeof(float)); std::fseek(f, 0, SEEK_SET);
    x.resize(std::fread(x.data(), sizeof(float), x.size(), f)); std::fclose(f);
    LARGE_INTEGER q0, q1, q2, fq; QueryPerformanceFrequency(&fq); QueryPerformanceCounter(&q0);
    analysis::SongMap map = analysis::BuildMap(x.data(), x.size(), 48000);
    QueryPerformanceCounter(&q1);
    analysis::Beats beats = analysis::TrackBeats(x.data(), x.size(), 48000);
    analysis::BeatStrengths(beats);
    QueryPerformanceCounter(&q2);
    analysis::Layers layers = analysis::AnalyseLayers(x.data(), x.size(), 48000, beats.beats, beats.barPhase);
    LARGE_INTEGER q3; QueryPerformanceCounter(&q3);
    FILE* o = std::fopen(outPath, "wb");
    if (!o) return 1;
    auto list = [&](const char* name, auto const& v, bool last) {
        std::fprintf(o, "\"%s\":[", name);
        for (size_t i = 0; i < v.size(); i++) std::fprintf(o, i ? ",%.17g" : "%.17g", (double)v[i]);
        std::fprintf(o, "]%s\n", last ? "" : ","); };
    std::fprintf(o, "{\"map\":{\"frames\":%d,\"fr\":%.17g,\"beat\":%.17g,\"offset\":%.17g,\"bpm\":%.17g,\n", map.frames, map.fr, map.beat, map.offset, map.bpm);
    list("section", map.section, false); list("tension", map.tension, false); list("drops", map.drops, true);
    std::fprintf(o, "},\"beats\":{\"bpm\":%.17g,\"fr\":%.17g,\"period\":%.17g,\"barPhase\":%d,\n", beats.bpm, beats.fr, beats.period, beats.barPhase);
    list("beats", beats.beats, false); list("strength", beats.strength, false); list("env", beats.env, true);
    std::fprintf(o, "},\"layers\":{\"fr\":%.17g,\n", layers.fr);
    std::vector<double> secStart, secEnd, secLabel;
    for (const auto& sc : layers.sections) { secStart.push_back(sc.start); secEnd.push_back(sc.end); secLabel.push_back(sc.label); }
    list("pad", layers.pad, false); list("melF", layers.melF, false); list("melS", layers.melS, false);
    list("harm", layers.harm, false); list("clarity", layers.clarity, false);
    list("secStart", secStart, false); list("secEnd", secEnd, false); list("secLabel", secLabel, true);
    std::fprintf(o, "}}\n");
    std::fclose(o);
    std::printf("analysed %.1f s of song: map %.0f ms, beats %.0f ms, layers %.0f ms - %zu beats at %.1f BPM, %zu drops, %zu sections\n",
                x.size() / 48000.0, Ms(q0, q1, fq), Ms(q1, q2, fq), Ms(q2, q3, fq), beats.beats.size(), beats.bpm, map.drops.size(), layers.sections.size());
    return 0;
}

int main(int argc, char** argv)
{
    if (argc == 4 && !std::strcmp(argv[1], "--analyze")) return AnalyzeToJson(argv[2], argv[3]);
    if (argc < 5)
    {
        std::fprintf(stderr, "usage: UniPlaySongVisualizer <style.milk|.vis> <width> <height> <channel> [parentPid]\n"
                             "       UniPlaySongVisualizer <style.milk|.vis> <width> <height> --bench <seconds>\n");
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

    // ---- the style: projectM with one MilkDrop preset (no playlist), or the native fountain
    CreateGl(w, h);
    size_t presetLen = std::strlen(preset);
    const bool native = presetLen > 4 && !_stricmp(preset + presetLen - 4, ".vis");
    projectm_handle pm = nullptr;
    fountain::Style style;
    fountain::Engine engine;
    fountain::Renderer fountainGl;
    if (native)
    {
        if (!style.Load(preset)) Fail("style file");
        engine.Init(style);
        fountainGl.profile = bench && std::getenv("VIS_PROFILE");
        if (!fountainGl.Create(w, h, style)) Fail("fountain shaders");
        std::printf("fountain %dx%d, style %s (%zu params)\n", w, h, preset, style.values.size());
    }
    else
    {
        pm = projectm_create();
        if (!pm) Fail("projectm_create");
        projectm_set_window_size(pm, w, h);
        projectm_set_fps(pm, 60);
        projectm_set_mesh_size(pm, 48, 32);
        projectm_set_preset_switch_failed_event_callback(pm, [](const char* file, const char* message, void*) {
            std::printf("preset failed: %s\n  %s\n", file, message); std::fflush(stdout); }, nullptr);
        projectm_load_preset_file(pm, preset, false);
        std::printf("projectM %dx%d, preset %s\n", w, h, preset);
    }
    KeyPass key;
    key.Create(w, h);
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

    SongLink songLink;
    if (!bench) songLink.Open(ring);
    // bench only: VIS_ANALYZE=1 analyses the PCM (as mono) and runs the engine with its beats and map, as a song would
    fountain::Song benchSong;
    const bool benchAnalysed = bench && native && std::getenv("VIS_ANALYZE") && pcm.size() >= 2;
    if (benchAnalysed)
    {
        std::vector<float> mono(pcm.size() / 2);
        for (size_t i = 0; i < mono.size(); i++) mono[i] = (float)((pcm[2 * i] + (double)pcm[2 * i + 1]) * 0.5);
        analysis::SongMap map = analysis::BuildMap(mono.data(), mono.size(), 48000);
        analysis::Beats beats = analysis::TrackBeats(mono.data(), mono.size(), 48000);
        analysis::BeatStrengths(beats);
        analysis::Layers layers = analysis::AnalyseLayers(mono.data(), mono.size(), 48000, beats.beats, beats.barPhase);
        // through an analysis file and back, as a song's analysis reaches the engine
        wchar_t tmpDir[MAX_PATH]; GetTempPathW(MAX_PATH, tmpDir);
        std::wstring file = std::wstring(tmpDir) + L"ups_bench.vab";
        if (!analysis::Save(file, map, beats, layers, 1, 2) || !analysis::Load(file, 1, 2, benchSong)) Fail("bench analysis file");
        DeleteFileW(file.c_str());
        std::printf("bench song analysis: %zu beats at %.0f BPM, %zu drops (through an analysis file)\n", beats.beats.size(), beats.bpm, map.drops.size());
    }
    std::vector<uint8_t> out(frameBytes);
    std::vector<float> winL(1024), winR(1024);   // the fountain's listening window, each channel
    double simT = 0;                              // the fountain's running time
    // bench only: the fountain engine's packed columns (colA, colB, colC) each frame, for bench/tools/engine_parity.mjs
    FILE* engineDump = bench && native && std::getenv("VIS_ENGINE_DUMP") ? std::fopen(std::getenv("VIS_ENGINE_DUMP"), "wb") : nullptr;
    LARGE_INTEGER freq, start, t0, t1, t2, t3, lastTick; QueryPerformanceFrequency(&freq); QueryPerformanceCounter(&start);
    lastTick = start;
    double sumRender = 0, sumRead = 0, sumUpload = 0;
    std::vector<double> totals;
    int64_t frame = 0;
    while (!hdr->stop)
    {
        if (bench)
        {
            LARGE_INTEGER now; QueryPerformanceCounter(&now);
            if (Ms(start, now, freq) > benchSeconds * 1000.0) break;
            if (const char* nf = std::getenv("VIS_FRAMES")) if (frame >= std::atoll(nf)) break;   // exactly this many frames
        }
        else if (WaitForSingleObject(tick, 50) != WAIT_OBJECT_0)
        {
            // No frame asked for: UniPlaySong isn't showing one (minimized, idle, in the background). Don't draw for
            // nobody - just make sure Playnite is still there. (Drawing anyway cost a third of a core while minimized.)
            if (parent && WaitForSingleObject(parent, 0) == WAIT_OBJECT_0) break;
            continue;
        }
        if (parent && WaitForSingleObject(parent, 0) == WAIT_OBJECT_0) break;
        if (hdr->stop) break;
        int i = (int)(frame % VIS_BUFFERS);

        QueryPerformanceCounter(&t0);
        if (native)
        {
            // a bench run steps a fixed 1/60 s over 800 frames of its 48 kHz PCM; a live one, the real time since the
            // last frame (at most 0.1 s, as the bench's page does, so a frame after a long idle doesn't leap)
            double dt, sr;
            if (bench)
            {
                dt = 1.0 / 60; sr = 48000;
                if (pcm.size() >= 1600)
                {
                    if (pcmPos + 1600 > pcm.size()) pcmPos = 0;
                    pcmPos += 1600;
                    for (int k = 0; k < 1024; k++)
                    {
                        long long f = (long long)(pcmPos / 2) - 1024 + k;
                        winL[k] = f >= 0 ? pcm[2 * (size_t)f] : 0.0f; winR[k] = f >= 0 ? pcm[2 * (size_t)f + 1] : 0.0f;
                    }
                }
            }
            else
            {
                dt = std::min(0.1, std::max(1e-3, Ms(lastTick, t0, freq) / 1000.0)); sr = ring.SampleRate();
                ring.Window(1024, winL.data(), winR.data());
            }
            lastTick = t0;
            simT += dt;
            // the song's analysis, when there is one for the song being heard
            const fountain::Song* song = nullptr; double songT = -1;
            if (benchAnalysed) { song = &benchSong; songT = (pcmPos / 2) / 48000.0; hdr->songState = 2; }
            else if (!bench)
            {
                songLink.Poll();
                uint32_t heard = 0; double at = 0;
                bool known = songLink.Time(ring, &heard, &at);
                if (songLink.song && known && heard == songLink.songFor) { song = songLink.song.get(); songT = at; }
                hdr->songState = song ? 2 : (heard && heard == songLink.wanted && heard != songLink.failedFor ? 1 : 0);
                // VIS_DEBUG: once a second, where the song is and whether its analysis is driving the fountain
                static const bool debugSong = std::getenv("VIS_DEBUG") != nullptr;
                static double nextDebug = 0;
                if (debugSong && simT >= nextDebug)
                {
                    nextDebug = simT + 1;
                    std::printf("song %u at %.2f s%s; analysis %s\n", heard, at, known ? "" : " (unknown)",
                                song ? "in use" : (songLink.song ? "for another song" : "not ready"));
                    std::fflush(stdout);
                }
            }
            engine.Step(dt, simT, winL.data(), winR.data(), sr, song, songT);
            if (engineDump)
            {
                std::fwrite(engine.colA, sizeof(engine.colA), 1, engineDump); std::fwrite(engine.colB, sizeof(engine.colB), 1, engineDump);
                std::fwrite(engine.colC, sizeof(engine.colC), 1, engineDump);
            }
            fountainGl.Draw(engine, simT);
        }
        else
        {
            ring.Feed(pm);
            if (pcm.size() >= 1600)
            {
                if (pcmPos + 1600 > pcm.size()) pcmPos = 0;
                projectm_pcm_add_float(pm, pcm.data() + pcmPos, 800, PROJECTM_STEREO);
                pcmPos += 1600;
            }
            projectm_opengl_render_frame(pm);
        }
        glFinish();
        if (native) fountainGl.Collect();
        QueryPerformanceCounter(&t1);
        key.Run(w, h, out.data()); // the presets crush faint haze to black, for the keying
        QueryPerformanceCounter(&t2);
        d3d->UpdateSubresource(tex[i], 0, nullptr, out.data(), w * 4, 0);
        d3d->Flush();
        QueryPerformanceCounter(&t3);

        hdr->producedQpc[i] = t3.QuadPart;
        hdr->latest = i;
        MemoryBarrier();
        hdr->frame = ++frame;
        sumRender += Ms(t0, t1, freq); sumRead += Ms(t1, t2, freq); sumUpload += Ms(t2, t3, freq);
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
            std::printf("frames %lld in %.1f s (%.1f fps); per frame: render %.2f ms, key+readback %.2f ms, upload %.2f ms; p95 total %.2f ms\n",
                        (long long)frame, secs, frame / secs, sumRender / frame, sumRead / frame, sumUpload / frame,
                        totals[totals.size() * 95 / 100]);
        }
    }
    if (engineDump) std::fclose(engineDump);
    songLink.Join();
    if (fountainGl.profiled)
        std::printf("fountain GPU per frame: scene %.2f ms, blur pyramid %.2f ms, composite %.2f ms\n", fountainGl.passMs[0] / fountainGl.profiled,
                    fountainGl.passMs[1] / fountainGl.profiled, fountainGl.passMs[2] / fountainGl.profiled);
    if (pm) projectm_destroy(pm);
    hdr->magic = 0;
    return 0;
}
