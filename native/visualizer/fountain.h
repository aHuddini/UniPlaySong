// The Jukebox Fountain: UniPlaySong's own fountain visualizer, drawn natively instead of through projectM. A port of
// the WebGL bench (native/visualizer/bench/fountain-bench.html) - its level engine line for line, and its shaders as
// they are, lifted into the style file by bench/tools/export_t8.mjs with the T8 standard's values. Parity with the bench
// is the rule: change the bench, regenerate the style file, and port engine changes here.
#pragma once
#include <cmath>
#include <cstdio>
#include <cstring>
#include <string>
#include <vector>
#include <algorithm>

namespace fountain {

static const double PI = 3.14159265358979323846;

// ---- the style file (UPS - Jukebox Fountain.vis) ---------------------------------------------------------------
struct Uniform { std::string name; int count; float v[3]; bool isInt; };
struct Style
{
    std::vector<std::string> names;   // the parameters, in the shaders' uP[] order
    std::vector<float> values;
    std::vector<Uniform> uniforms;
    std::string scene, blur, comp;

    bool Load(const char* path)
    {
        FILE* f = std::fopen(path, "rb");
        if (!f) return false;
        std::string text;
        char buf[65536]; size_t n;
        while ((n = std::fread(buf, 1, sizeof(buf), f)) > 0) text.append(buf, n);
        std::fclose(f);
        std::string* shader = nullptr;
        size_t at = 0;
        while (at < text.size())
        {
            size_t end = text.find('\n', at);
            if (end == std::string::npos) end = text.size();
            std::string line = text.substr(at, end - at);
            at = end + 1;
            if (!line.empty() && line.back() == '\r') line.pop_back();
            if (line == "@@SCENE") { shader = &scene; continue; }
            if (line == "@@BLUR") { shader = &blur; continue; }
            if (line == "@@COMP") { shader = &comp; continue; }
            if (shader) { *shader += line; *shader += '\n'; continue; }
            if (line.size() < 3 || line[0] == '#') continue;
            char name[64] = {}; float a = 0, b = 0, c = 0;
            if (line[0] == 'P' && std::sscanf(line.c_str() + 2, "%63s %f", name, &a) == 2) { names.push_back(name); values.push_back(a); }
            else if (line[0] == 'U' || line[0] == 'I')
            {
                int got = std::sscanf(line.c_str() + 2, "%63s %f %f %f", name, &a, &b, &c);
                if (got >= 2) uniforms.push_back({ name, got - 1, { a, b, c }, line[0] == 'I' });
            }
        }
        return !names.empty() && !scene.empty() && !blur.empty() && !comp.empty();
    }
    float Get(const char* name) const
    {
        for (size_t i = 0; i < names.size(); i++) if (names[i] == name) return values[i];
        std::fprintf(stderr, "style: no parameter %s\n", name);
        return 0.0f;
    }
};

// ---- whole-song analysis, as UniPlaySong measured it (empty until a song's analysis arrives) ----------------------
struct Section { float start, end; int label; };
struct Song
{
    // song map: per-frame section and build-up tension, the drops (seconds), the beat grid's estimate
    bool map = false;
    double mapFr = 0, offset = 0, beat = 0.5;
    std::vector<float> section, tension;
    std::vector<double> drops;
    // tracked beats: times, strengths, which of every four is the bar's ONE, the median beat
    bool beats = false;
    std::vector<double> beatT, strength;
    int barPhase = 0;
    double period = 0.5;
    // layers: held chords, melody (Hz, strength), harmony per beat (angle on the circle of fifths, clarity), sections
    bool layers = false;
    float lyFr = 0;
    std::vector<float> pad, melF, melS, harm, clarity;
    std::vector<Section> sections;
};

// ---- the spectrum, as the bench's AnalyserNode gives it -------------------------------------------------------------
// 1024 samples ending now, a Blackman window, an FFT, magnitude / N (getFloatFrequencyData with no smoothing, its dB
// turned back to linear)
struct Analyser
{
    static const int n = 1024;
    float win[n], re[n], im[n];
    Analyser() { for (int i = 0; i < n; i++) win[i] = (float)(0.42 - 0.5 * std::cos(2 * PI * i / n) + 0.08 * std::cos(4 * PI * i / n)); }
    void Spectrum(const float* x, float* out512)
    {
        for (int i = 0; i < n; i++) { re[i] = x[i] * win[i]; im[i] = 0; }
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; j & bit; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) { std::swap(re[i], re[j]); std::swap(im[i], im[j]); }
        }
        for (int len = 2; len <= n; len <<= 1)
        {
            double a = -2 * PI / len;
            for (int i = 0; i < n; i += len)
                for (int k = 0; k < len / 2; k++)
                {
                    float wr = (float)std::cos(a * k), wi = (float)std::sin(a * k);
                    int p = i + k, q = p + len / 2;
                    float tr = re[q] * wr - im[q] * wi, ti = re[q] * wi + im[q] * wr;
                    re[q] = re[p] - tr; im[q] = im[p] - ti; re[p] += tr; im[p] += ti;
                }
        }
        for (int i = 0; i < n / 2; i++) out512[i] = std::sqrt(re[i] * re[i] + im[i] * im[i]) / n;
    }
};

// ---- the level engine: engine(dt, t) from the bench ---------------------------------------------------------------
struct Engine
{
    static const int N = 96, HIST = 128, WAVE_ROWS = 120;
    // the parameters the engine reads
    struct { float STEREO, TILT, PEAK_HOLD, EASE_IN, EASE_OUT, KICK_DECAY, CORE_SLOW, FLING_FREQ, FLING_DAMP, SECTION_SENS,
                   ANALYSIS, BEAT_DRIVE, BEAT_SPRING, BEAT_SNAP, BEAT_LEAD, BEAT_CONTRAST, BEAT_ACCENT, BEAT_OTHERS, BEAT_FLOOR,
                   PINGPONG, DYNAMICS, DROP_DECAY, WAVE_SPEED, GAMMA, KICK_THRESHOLD, KICK_GAIN, BEAT_EVEN, LIQUID,
                   BEAT_IN_PLACE, WAVE, DROP_FLING, GLOW_SMOOTH, COLOR_EASE, COLOR_CURVE, COLOR_LEVEL,
                   RAIN, FLOOR, REACH, BREATH, TENSION, COOL_AIR, PAD_SWELL, MELODY, SIZE, STRETCH_MAX, GRAVITY, SPLASH,
                   QUANTIZE, FLING; } P;

    float specL[512] = {}, specR[512] = {};
    float pk[N], pv[N] = {}, ez[N] = {}, lastLive[N] = {}, env[N] = {}, slow[N] = {}, rate[N] = {}, fl[N] = {}, flv[N] = {};
    float hist[N * HIST] = {}, colA[N * 4] = {}, colB[N * 4] = {}, colC[N * 4] = {};
    float bandPos[N] = {}, melC[N] = {}, kv[N] = {}, kc[N] = {}, heatC[N] = {}, settleC[N] = {}, targ[N] = {}, liqTmp[N] = {};
    float waveRing[WAVE_ROWS] = {};
    double reg00 = 0.02, acc60 = 0, meanLevel = 0;
    double tension = 0, beatIdx = 0, beatFrac = 0, phrase = 0, lastSongT = -1;
    int dropColL = 24, dropColR = 71;
    double beatEnv = 0, beatAt = -1, padE = 0, harmX = 0, harmY = 0, harmE = 0;
    double dropC = 0, sectionC = 1, tensionC = 0, phraseC = 0, actC = 0, secHueC = 0, melPos = 0.3, mT = 0;
    double eFast = 0, eSlow = 0, section = 1, quietFor = 0, quietAgo = 99, lastQuiet = 0, dropPulse = 0, bassKick = 0;
    int waveAt = 0;
    double waveAcc = 0;
    double peakKey[3] = { -1, -1, -1 }, peakAt = 0;   // springPeak's last answer (the bench caches by value)
    Analyser analyser;

    void Init(const Style& s)
    {
#define FP(n) P.n = s.Get(#n)
        FP(STEREO); FP(TILT); FP(PEAK_HOLD); FP(EASE_IN); FP(EASE_OUT); FP(KICK_DECAY); FP(CORE_SLOW); FP(FLING_FREQ);
        FP(FLING_DAMP); FP(SECTION_SENS); FP(ANALYSIS); FP(BEAT_DRIVE); FP(BEAT_SPRING); FP(BEAT_SNAP); FP(BEAT_LEAD);
        FP(BEAT_CONTRAST); FP(BEAT_ACCENT); FP(BEAT_OTHERS); FP(BEAT_FLOOR); FP(PINGPONG); FP(DYNAMICS); FP(DROP_DECAY);
        FP(WAVE_SPEED); FP(GAMMA); FP(KICK_THRESHOLD); FP(KICK_GAIN); FP(BEAT_EVEN); FP(LIQUID); FP(BEAT_IN_PLACE); FP(WAVE);
        FP(DROP_FLING); FP(GLOW_SMOOTH); FP(COLOR_EASE); FP(COLOR_CURVE); FP(COLOR_LEVEL);
        FP(RAIN); FP(FLOOR); FP(REACH); FP(BREATH); FP(TENSION); FP(COOL_AIR); FP(PAD_SWELL); FP(MELODY); FP(SIZE);
        FP(STRETCH_MAX); FP(GRAVITY); FP(SPLASH); FP(QUANTIZE); FP(FLING);
#undef FP
        for (int c = 0; c < N; c++) pk[c] = 1e-3f;
    }

    // How long the spring takes to reach its peak after a beat's throw starts (a pulse decaying over len s), found by
    // simulating it - the throw is started that much early so the peak lands on the beat.
    double SpringPeak(double fq, double z, double len)
    {
        if (fq == peakKey[0] && z == peakKey[1] && len == peakKey[2]) return peakAt;
        double w = 2 * PI * fq, h = 1.0 / 960, x = 0, v = 0, best = 0, at = 0;
        for (double t = 0; t < 2; t += h) { double a = w * w * (std::exp(-t / len) - x) - 2 * z * w * v; v += a * h; x += v * h; if (x > best) { best = x; at = t; } }
        peakKey[0] = fq; peakKey[1] = z; peakKey[2] = len; peakAt = at;
        return at;
    }
    static int BeatIndexAt(const Song* s, double t)
    {
        if (!s || !s->beats) return -1;
        const auto& bs = s->beatT; int lo = 0, hi = (int)bs.size();
        while (lo < hi) { int m = (lo + hi) >> 1; if (bs[m] <= t) lo = m + 1; else hi = m; }
        return lo - 1;
    }

    // dt: seconds since the last step; t: the running time; L, R: the last 1024 samples of each channel (the newest
    // last); sr: their rate; song: the current song's analysis, or null; songT: where the song is (seconds), < 0 = unknown
    void Step(double dt, double t, const float* L, const float* R, double sr, const Song* song, double songT)
    {
        const double binHz = sr / 1024;
        const bool stereo = P.STEREO >= 0.5;
        for (int c = 0; c < N; c++) bandPos[c] = stereo ? (c < N / 2 ? (N / 2 - 1 - c) : (c - N / 2)) / (float)(N / 2) : c / (float)N;
        // the live spectrum: each wing its channel (a mono song's two channels are the same); one spectrum of the mix
        // when the wings aren't split
        if (stereo) { analyser.Spectrum(L, specL); analyser.Spectrum(R, specR); }
        else { float mix[1024]; for (int i = 0; i < 1024; i++) mix[i] = 0.5f * (L[i] + R[i]); analyser.Spectrum(mix, specL); std::memcpy(specR, specL, sizeof(specL)); }
        const double b0 = 50 / binHz, b1 = std::min(510.0, 16000 / binHz);
        float v[N]; double loudest = 0;
        for (int c = 0; c < N; c++)
        {
            const float* src = stereo && c < N / 2 ? specL : specR;
            double p = b0 + std::pow((double)bandPos[c], 1.5) * (b1 - b0); int i = (int)std::floor(p); double f = p - i;
            v[c] = (float)((src[i] * (1 - f) + src[i + 1] * f) * (1 + P.TILT * bandPos[c]));
            loudest = std::max(loudest, (double)v[c]);
        }
        reg00 = std::max(std::max(loudest, reg00 * std::pow(0.5, dt / 4)), 1e-4);
        const double pd = std::pow(0.5, dt / P.PEAK_HOLD), up = 1 - std::pow(0.2, dt * 60), dn = 1 - std::pow(0.5, dt * 60);
        const double ea = 1 - std::exp(-dt / P.EASE_IN), er = 1 - std::exp(-dt / P.EASE_OUT);
        const double kd = std::exp(-dt / P.KICK_DECAY), sl = 1 - std::exp(-dt / P.CORE_SLOW);
        double w = 2 * PI * P.FLING_FREQ; const double z = P.FLING_DAMP;
        double total = 0;
        // song dynamics, from the absolute loudness
        double raw = 0; for (int c = 0; c < N; c++) raw += v[c];
        raw /= N;
        eFast += (raw - eFast) * (1 - std::exp(-dt / 0.4));
        eSlow += (raw - eSlow) * (1 - std::exp(-dt / 6.0));
        const double ratio = eFast / std::max(eSlow, 1e-4);
        const double st = std::min(1.0, std::max(0.0, (ratio - 0.75) / std::max(0.01, P.SECTION_SENS - 0.75)));
        const bool known = songT >= 0;
        const bool useMap = P.ANALYSIS >= 0.5 && song && song->map && known;
        if (!useMap) section += (st - section) * (1 - std::exp(-dt / 1.5));
        if (useMap)
        {
            int f = std::min((int)song->section.size() - 1, std::max(0, (int)std::floor(songT * song->mapFr)));
            section += (song->section[f] - section) * (1 - std::exp(-dt / 0.4));
            tension = song->tension[f];
            if (songT < lastSongT) lastSongT = -1;
            for (double d : song->drops) if (lastSongT < d && songT >= d) dropPulse = 1;
            double bp = (songT - song->offset) / song->beat;
            beatIdx = std::floor(bp); beatFrac = bp - beatIdx; phrase = std::floor(bp / 32);
        }
        else tension = 0;
        if (known) lastSongT = songT;
        // layers at this point of the song, eased: section tint, melody, held chords, harmony
        const bool lyOn = song && song->layers && known;
        double secT = 0;
        if (lyOn)
            for (const Section& sc : song->sections)
                if (songT >= sc.start && songT < sc.end) { static const double turn[6] = { 0, 0.35, -0.35, 0.6, -0.6, 0.85 }; secT = turn[std::min(5, sc.label)]; break; }
        secHueC += (secT - secHueC) * (1 - std::exp(-dt / 2.0));
        mT = 0;
        if (lyOn && !song->melF.empty())
        {
            int f = std::min((int)song->melF.size() - 1, (int)std::floor(songT * song->lyFr));
            if (song->melS[f] > 0)
            {
                mT = song->melS[f];
                double pos = std::pow(std::max(0.0, (song->melF[f] - 50) / 15950.0), 2.0 / 3.0);
                melPos += (pos - melPos) * (1 - std::exp(-dt / 0.12));
            }
        }
        double padT = lyOn && !song->pad.empty() ? song->pad[std::min((int)song->pad.size() - 1, (int)std::floor(songT * song->lyFr))] : 0;
        padE += (padT - padE) * (1 - std::exp(-dt / 0.25));
        double hx = 0, hy = 0;
        if (lyOn) { int bi = BeatIndexAt(song, songT); if (bi >= 0 && bi < (int)song->harm.size()) { hx = std::cos(song->harm[bi]) * song->clarity[bi]; hy = std::sin(song->harm[bi]) * song->clarity[bi]; } }
        const double hk = 1 - std::exp(-dt / 1.0); harmX += (hx - harmX) * hk; harmY += (hy - harmY) * hk;
        harmE = harmY;
        if (useMap) { /* drops come from the map */ }
        else if (section < 0.35) { quietFor += dt; quietAgo = 0; }
        else { if (quietFor > 0) { lastQuiet = quietFor; quietFor = 0; } quietAgo += dt; }
        // the tracked beat: each beat fired a little early so the throw peaks on it; tempo-locked spring and length
        const bool beatOn = song && song->beats && known && P.BEAT_DRIVE > 0;
        const double fq = beatOn && P.BEAT_SPRING > 0 ? P.BEAT_SPRING / song->period : P.FLING_FREQ;
        const double bLen = beatOn && P.BEAT_SNAP > 0 ? P.BEAT_SNAP * song->period : P.KICK_DECAY;
        if (beatOn) w = 2 * PI * fq;
        beatEnv *= std::exp(-dt / bLen);
        if (beatOn)
        {
            const double tt = songT + P.BEAT_LEAD * SpringPeak(fq, z, bLen);
            const auto& bs = song->beatT;
            if (tt < beatAt) beatAt = -1;   // looped
            for (int i = std::max(0, BeatIndexAt(song, beatAt) + 1); i < (int)bs.size() && bs[i] <= tt; i++)
                if (bs[i] > beatAt)
                {
                    const double hit = std::pow(song->strength[i], P.BEAT_CONTRAST) * (i % 4 == song->barPhase ? 1 + P.BEAT_ACCENT : P.BEAT_OTHERS);
                    beatEnv = std::max(beatEnv, P.BEAT_FLOOR + (1 - P.BEAT_FLOOR) * hit);
                    int bl = -1, br = -1;
                    for (int c = 0; c < N; c++) { if (c < N / 2) { if (bl < 0 || ez[c] > ez[bl]) bl = c; } else if (br < 0 || ez[c] > ez[br]) br = c; }
                    dropColL = bl; dropColR = br;
                }
            beatAt = tt;
            int lo = 0, hi = (int)bs.size(); while (lo < hi) { int m = (lo + hi) >> 1; if (bs[m] <= songT) lo = m + 1; else hi = m; }
            if (lo > 0 && lo < (int)bs.size()) { beatIdx = lo - 1; beatFrac = (songT - bs[lo - 1]) / std::max(1e-3, bs[lo] - bs[lo - 1]); phrase = std::floor(beatIdx / 32); }
        }
        const double bd = beatOn ? P.BEAT_DRIVE : 0;
        double bk = 0; int nbk = 0; for (int c = 0; c < N; c++) if (bandPos[c] < 0.084) { bk += env[c]; nbk++; }
        bk /= std::max(1, nbk); bassKick = bk + (beatEnv - bk) * bd;
        if (!useMap && lastQuiet > 2 && quietAgo < 10 && bassKick > 0.25 && ratio > 1.1) { dropPulse = 1; lastQuiet = 0; }
        const double ppAmt = useMap ? P.PINGPONG * P.DYNAMICS * section * std::exp(-beatFrac * 4) : 0;
        dropPulse *= std::exp(-dt / P.DROP_DECAY);
        waveAcc += dt;
        while (waveAcc >= 1.0 / 60) { waveAt = (waveAt + 1) % WAVE_ROWS; waveRing[waveAt] = (float)bassKick; waveAcc -= 1.0 / 60; }
        const double dyn = P.DYNAMICS;
        for (int c = 0; c < N; c++)
        {
            pk[c] = (float)std::max(std::max((double)pk[c] * pd, (double)v[c]), 0.03 * reg00);
            const double tg = std::max(std::pow(std::min(1.0, std::max(0.0, ((double)v[c] / pk[c] - 0.08) / 0.92)), (double)P.GAMMA), 0.0034);
            const double nx = pv[c] + (tg - pv[c]) * (tg > pv[c] ? up : dn);
            const double o = std::max(nx, (double)pv[c]); pv[c] = (float)nx;
            ez[c] = (float)(ez[c] + (o - ez[c]) * (o > ez[c] ? ea : er));
            const double live = ez[c];
            rate[c] = (float)((live - lastLive[c]) / std::max(dt, 1e-3) / 60);
            lastLive[c] = (float)live;
            env[c] = (float)std::max(std::min(1.0, std::max(0.0, rate[c] - (double)P.KICK_THRESHOLD) * P.KICK_GAIN), env[c] * kd);
            slow[c] = (float)(slow[c] + (live - slow[c]) * sl);
            const int back = std::min(WAVE_ROWS - 1, (int)std::floor(std::fabs(c - 47.5) / P.WAVE_SPEED * 60 + 0.5));
            const double wave = waveRing[(waveAt - back + WAVE_ROWS) % WAVE_ROWS];
            const int side = c < 48 ? 0 : 1;
            const double pp = ppAmt * (((long long)std::fabs(beatIdx) % 2) == side ? 1 : -0.5);
            const double sh = std::min(1.0, live * 1.4);
            double shape = sh + (1 - sh) * P.BEAT_EVEN;
            if (P.LIQUID > 0) { int d = c < N / 2 ? c - dropColL : c - dropColR; shape *= 1 - P.LIQUID + P.LIQUID * 1.6 * std::exp(-(double)(d * d) / 25); }
            kv[c] = (float)(env[c] + (beatEnv * shape - env[c]) * bd);
            targ[c] = (float)(std::max({ kv[c] * (beatOn ? P.BEAT_IN_PLACE : 1.0) * (1 + pp), dyn * P.WAVE * wave * (1 + pp), dyn * P.DROP_FLING * dropPulse }) + std::max(0.0, pp) * 0.35);
            total += live;
        }
        // the fling springs, stepped together (with Liquid, coupled to their neighbours); symplectic Euler substeps
        {
            const double k = 2500 * P.LIQUID, wl = w * w * (1 - 0.85 * P.LIQUID);
            const int steps = std::max(4, (int)std::ceil(dt * 240)); const double h = dt / steps;
            for (int s = 0; s < steps; s++)
            {
                if (k > 0) std::memcpy(liqTmp, fl, sizeof(fl));
                for (int c = 0; c < N; c++)
                {
                    double a = wl * (targ[c] - fl[c]) - 2 * z * w * flv[c];
                    if (k > 0) a += k * (liqTmp[c > 0 ? c - 1 : 1] + liqTmp[c < N - 1 ? c + 1 : N - 2] - 2 * liqTmp[c]);
                    flv[c] = (float)(flv[c] + a * h); fl[c] = (float)(fl[c] + flv[c] * h);
                }
            }
        }
        meanLevel += (total / N - meanLevel) * (1 - std::exp(-dt / P.GLOW_SMOOTH));
        const double ca = P.COLOR_EASE > 0 ? 1 - std::exp(-dt / P.COLOR_EASE) : 1, cr = P.COLOR_EASE > 0 ? 1 - std::exp(-dt / (3 * P.COLOR_EASE)) : 1;
        for (int c = 0; c < N; c++) { const double k1 = std::min(1.0, (double)kv[c]); kc[c] = (float)(kc[c] + (k1 - kc[c]) * (k1 > kc[c] ? ca : cr)); }
        dropC += (dropPulse - dropC) * (dropPulse > dropC ? ca : cr);
        sectionC += (section - sectionC) * (1 - std::exp(-dt / 1.2));
        // how strong the beat is around now: a strong beat isn't a breakdown
        double act = 0;
        if (song && song->beats && known)
        {
            const auto& bs = song->beatT; double sum = 0; int cnt = 0;
            for (int i = std::max(0, BeatIndexAt(song, songT - 2)); i < (int)bs.size() && bs[i] < songT + 2; i++) if (bs[i] >= songT - 2) { sum += song->strength[i]; cnt++; }
            if (cnt) act = std::min(1.0, std::max(0.0, (sum / cnt - 0.3) / 0.25));
        }
        actC += (act - actC) * (1 - std::exp(-dt / 1.2));
        for (int c = 0; c < N; c++) { const double h1 = std::min(1.0, (double)ez[c]); heatC[c] = (float)(heatC[c] + (h1 - heatC[c]) * (h1 > heatC[c] ? ca : cr)); }
        for (int c = 0; c < N; c++) { const double m1 = mT * std::exp(-std::pow((bandPos[c] - melPos) / 0.06, 2)); melC[c] = (float)(melC[c] + (m1 - melC[c]) * (m1 > melC[c] ? ca : cr)); }
        tensionC += (tension - tensionC) * (tension > tensionC ? ca : cr);
        const double sIn = 1 - std::exp(-dt / 0.3), sOut = 1 - std::exp(-dt / 0.8);
        for (int c = 0; c < N; c++) { const double s = fl[c] > 0.02 ? std::min(1.0, std::max(0.0, -flv[c] * 0.35)) : 0; settleC[c] = (float)(settleC[c] + (s - settleC[c]) * (s > settleC[c] ? sIn : sOut)); }
        phraseC += ((P.ANALYSIS >= 0.5 ? phrase : 0) - phraseC) * (1 - std::exp(-dt / 1.5));
        acc60 += dt;
        while (acc60 >= 1.0 / 60) { std::memmove(hist + N, hist, sizeof(float) * N * (HIST - 1)); acc60 -= 1.0 / 60; }
        // each column's reach this frame (column widths from the line): its highest level through every multiplier at its
        // ceiling, plus the largest stretched dot - the scene skips the column's dots beyond it (as the bench does)
        const double travelMax = std::max(1.0, P.RAIN > 0 ? (double)P.FLOOR : 1.0);
        const double reachMul = (double)P.REACH * (1 + P.BREATH * P.DYNAMICS) * (1 + 0.35 * P.TENSION) * (1 + 0.18 * P.COOL_AIR) * (1 + 1.2 * P.PAD_SWELL) * (1 + 0.45 * 1.2 * P.MELODY) * travelMax;
        const double reachAdd = 0.4 * P.SIZE * (1 + P.STRETCH_MAX) + 0.6 * P.GRAVITY * P.SPLASH + (P.QUANTIZE > 0 ? 1.5 : 0) + 0.5;
        for (int c = 0; c < N; c++)
        {
            hist[c] = ez[c];
            float lm = std::max(ez[c], slow[c]); for (int r = 1; r < HIST; r++) lm = std::max(lm, hist[r * N + c]);
            float* a = colA + c * 4; a[0] = ez[c]; a[1] = kc[c]; a[2] = slow[c]; a[3] = rate[c];
            float* b = colB + c * 4; b[0] = fl[c]; b[1] = flv[c]; b[2] = heatC[c]; b[3] = settleC[c];
            float* m = colC + c * 4; m[0] = melC[c]; m[1] = bandPos[c]; m[2] = (float)(lm * reachMul * (1 + P.FLING * std::fabs(fl[c])) + reachAdd); m[3] = 0;
        }
    }

    // the glow's warmth: the columns' mean (faded) loudness through the dots' temperature curve
    double Warm() const
    {
        double m = 0; for (int c = 0; c < N; c++) m += heatC[c]; m /= N;
        return std::min(1.0, std::pow(std::min(1.0, m * 1.5), (double)P.COLOR_CURVE) * P.COLOR_LEVEL);
    }
};

// ---- the passes: scene, blur pyramid, composite (into framebuffer 0) ---------------------------------------------
struct Target { GLuint tex = 0, fbo = 0; int w = 0, h = 0; };
struct Renderer
{
    GLuint scene = 0, blur = 0, comp = 0, vao = 0, texHist = 0, texA = 0, texB = 0, texC = 0;
    Target sceneT, b1, b1t, b2, b2t, b3, b3t;
    int W = 0, H = 0;
    const Style* style = nullptr;
    // bench only: GPU time per pass (scene, blur pyramid, composite), summed - Collect() after the frame has finished
    bool profile = false;
    GLuint query[4] = {};
    double passMs[3] = {};
    int profiled = 0;
    void Mark(int i) { if (profile) glQueryCounter(query[i], GL_TIMESTAMP); }
    void Collect()
    {
        if (!profile) return;
        GLuint64 t[4];
        for (int i = 0; i < 4; i++) glGetQueryObjectui64v(query[i], GL_QUERY_RESULT, &t[i]);
        for (int i = 0; i < 3; i++) passMs[i] += (t[i + 1] - t[i]) / 1e6;
        profiled++;
    }

    static GLuint Compile(GLenum type, const std::string& src, const char* what)
    {
        GLuint sh = glCreateShader(type);
        const char* code = src.c_str();
        glShaderSource(sh, 1, &code, nullptr);
        glCompileShader(sh);
        GLint ok = 0; glGetShaderiv(sh, GL_COMPILE_STATUS, &ok);
        if (!ok) { std::vector<char> log(8192); glGetShaderInfoLog(sh, (GLsizei)log.size(), nullptr, log.data()); std::fprintf(stderr, "%s shader:\n%s\n", what, log.data()); return 0; }
        return sh;
    }
    static GLuint Program(const std::string& fs, const char* what)
    {
        static const std::string vs = "#version 330 core\n"
            "void main() { vec2 p = vec2((gl_VertexID << 1) & 2, gl_VertexID & 2); gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0); }\n";
        GLuint v = Compile(GL_VERTEX_SHADER, vs, what), f = Compile(GL_FRAGMENT_SHADER, fs, what);
        if (!v || !f) return 0;
        GLuint p = glCreateProgram();
        glAttachShader(p, v); glAttachShader(p, f); glLinkProgram(p);
        glDeleteShader(v); glDeleteShader(f);
        GLint ok = 0; glGetProgramiv(p, GL_LINK_STATUS, &ok);
        if (!ok) { std::vector<char> log(8192); glGetProgramInfoLog(p, (GLsizei)log.size(), nullptr, log.data()); std::fprintf(stderr, "%s link:\n%s\n", what, log.data()); return 0; }
        return p;
    }
    static GLuint DataTex(GLint fmt, GLenum layout, int w, int h)
    {
        GLuint t = 0; glGenTextures(1, &t); glBindTexture(GL_TEXTURE_2D, t);
        glTexImage2D(GL_TEXTURE_2D, 0, fmt, w, h, 0, layout, GL_FLOAT, nullptr);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_NEAREST); glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_NEAREST);
        return t;
    }
    static Target MakeTarget(int w, int h)
    {
        Target t; t.w = w; t.h = h;
        glGenTextures(1, &t.tex); glBindTexture(GL_TEXTURE_2D, t.tex);
        glTexImage2D(GL_TEXTURE_2D, 0, GL_RGBA8, w, h, 0, GL_RGBA, GL_UNSIGNED_BYTE, nullptr);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR); glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_EDGE); glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_EDGE);
        glGenFramebuffers(1, &t.fbo); glBindFramebuffer(GL_FRAMEBUFFER, t.fbo);
        glFramebufferTexture2D(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, t.tex, 0);
        return t;
    }

    bool Create(int w, int h, const Style& s)
    {
        W = w; H = h; style = &s;
        scene = Program(s.scene, "scene"); blur = Program(s.blur, "blur"); comp = Program(s.comp, "composite");
        if (!scene || !blur || !comp) return false;
        glGenVertexArrays(1, &vao);
        if (profile) glGenQueries(4, query);
        texHist = DataTex(GL_R32F, GL_RED, Engine::N, Engine::HIST);
        texA = DataTex(GL_RGBA32F, GL_RGBA, Engine::N, 1); texB = DataTex(GL_RGBA32F, GL_RGBA, Engine::N, 1); texC = DataTex(GL_RGBA32F, GL_RGBA, Engine::N, 1);
        sceneT = MakeTarget(w, h);
        auto lv = [&](int sc, Target& a, Target& b) { int lw = std::max(1, (int)std::lround(w / (double)sc)), lh = std::max(1, (int)std::lround(h / (double)sc)); a = MakeTarget(lw, lh); b = MakeTarget(lw, lh); };
        lv(2, b1, b1t); lv(4, b2, b2t); lv(8, b3, b3t);
        glBindFramebuffer(GL_FRAMEBUFFER, 0);
        // the style's fixed uniforms, once per program
        for (GLuint p : { scene, comp })
        {
            glUseProgram(p);
            glUniform1fv(glGetUniformLocation(p, "uP"), (GLsizei)s.values.size(), s.values.data());
            for (const Uniform& u : s.uniforms)
            {
                GLint loc = glGetUniformLocation(p, u.name.c_str());
                if (loc < 0) continue;
                if (u.isInt) glUniform1i(loc, (int)u.v[0]);
                else if (u.count == 1) glUniform1f(loc, u.v[0]);
                else if (u.count == 3) glUniform3f(loc, u.v[0], u.v[1], u.v[2]);
            }
        }
        glUseProgram(0);
        return true;
    }
    static void Tex(GLuint prog, const char* name, int unit, GLuint tex)
    {
        glActiveTexture(GL_TEXTURE0 + unit); glBindTexture(GL_TEXTURE_2D, tex);
        glUniform1i(glGetUniformLocation(prog, name), unit);
    }
    static void F1(GLuint p, const char* n, double v) { glUniform1f(glGetUniformLocation(p, n), (float)v); }
    void Blur(const Target& src, const Target& dst, const Target& tmp)
    {
        glUseProgram(blur);
        const Target* from[2] = { &src, &tmp }; const Target* to[2] = { &tmp, &dst };
        const float dir[2][2] = { { 1.0f / tmp.w, 0 }, { 0, 1.0f / dst.h } };
        for (int i = 0; i < 2; i++)
        {
            glBindFramebuffer(GL_FRAMEBUFFER, to[i]->fbo); glViewport(0, 0, to[i]->w, to[i]->h);
            Tex(blur, "uSrc", 0, from[i]->tex);
            glUniform2f(glGetUniformLocation(blur, "uDir"), dir[i][0], dir[i][1]);
            glUniform2f(glGetUniformLocation(blur, "uDst"), (float)to[i]->w, (float)to[i]->h);
            glDrawArrays(GL_TRIANGLES, 0, 3);
        }
    }

    // One frame from the engine's state, finished in framebuffer 0. t: the running time
    void Draw(const Engine& e, double t)
    {
        glDisable(GL_BLEND); glDisable(GL_DEPTH_TEST); glDisable(GL_SCISSOR_TEST); glDisable(GL_CULL_FACE);
        glBindVertexArray(vao);
        glBindTexture(GL_TEXTURE_2D, texHist); glTexSubImage2D(GL_TEXTURE_2D, 0, 0, 0, Engine::N, Engine::HIST, GL_RED, GL_FLOAT, e.hist);
        glBindTexture(GL_TEXTURE_2D, texA); glTexSubImage2D(GL_TEXTURE_2D, 0, 0, 0, Engine::N, 1, GL_RGBA, GL_FLOAT, e.colA);
        glBindTexture(GL_TEXTURE_2D, texB); glTexSubImage2D(GL_TEXTURE_2D, 0, 0, 0, Engine::N, 1, GL_RGBA, GL_FLOAT, e.colB);
        glBindTexture(GL_TEXTURE_2D, texC); glTexSubImage2D(GL_TEXTURE_2D, 0, 0, 0, Engine::N, 1, GL_RGBA, GL_FLOAT, e.colC);
        const double full = std::max(e.sectionC, e.actC);   // full if loud OR drumming
        // scene
        Mark(0);
        glUseProgram(scene);
        glBindFramebuffer(GL_FRAMEBUFFER, sceneT.fbo); glViewport(0, 0, W, H);
        glUniform2f(glGetUniformLocation(scene, "uRes"), (float)W, (float)H);
        F1(scene, "uTime", t); F1(scene, "uSection", full); F1(scene, "uDrop", e.dropC); F1(scene, "uTension", e.tensionC);
        F1(scene, "uPad", e.padE); F1(scene, "uHarm", e.harmE); F1(scene, "uSecHue", e.secHueC); F1(scene, "uPhrase", e.phraseC);
        F1(scene, "uLevelL", e.meanLevel);
        Tex(scene, "uHist", 0, texHist); Tex(scene, "uColA", 1, texA); Tex(scene, "uColB", 2, texB); Tex(scene, "uColC", 3, texC);
        glDrawArrays(GL_TRIANGLES, 0, 3);
        // blur pyramid
        Mark(1);
        Blur(sceneT, b1, b1t); Blur(b1, b2, b2t); Blur(b2, b3, b3t);
        Mark(2);
        // composite, into framebuffer 0 (the key pass reads it from there)
        glUseProgram(comp);
        glBindFramebuffer(GL_FRAMEBUFFER, 0); glViewport(0, 0, W, H);
        glUniform2f(glGetUniformLocation(comp, "uRes"), (float)W, (float)H);
        F1(comp, "uLevel", e.meanLevel); F1(comp, "uTime", t); F1(comp, "uFull", full); F1(comp, "uPad", e.padE);
        F1(comp, "uWarm", e.Warm()); F1(comp, "uRingT", 99);
        Tex(comp, "uScene", 0, sceneT.tex); Tex(comp, "uB1", 1, b1.tex); Tex(comp, "uB2", 2, b2.tex); Tex(comp, "uB3", 3, b3.tex);
        glDrawArrays(GL_TRIANGLES, 0, 3);
        Mark(3);
        glActiveTexture(GL_TEXTURE0);
    }
};

} // namespace fountain
