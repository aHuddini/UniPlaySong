// The Jukebox Fountain's whole-song analysis: the bench's own (native/visualizer/bench/fountain-bench.html) ported line
// for line - the song map (buildMap: sections, build-up tension, drops, a beat grid), the beat tracker (trackBeats, a
// port of librosa's beat_track) and each beat's strength (beatStrengths). The bench stores its arrays as 32-bit floats
// and does its arithmetic in doubles; so does this, step for step, so the two agree to the last bit on the same samples
// (bench/tools/analysis_parity.mjs checks it). Input: the song as mono 48 kHz float, as the bench analyses it.
#pragma once
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <string>
#include <vector>
#include <algorithm>
#include <limits>

namespace analysis {

static const double PI = 3.14159265358979323846;
static double JsRound(double x) { return std::floor(x + 0.5); }   // Math.round
static double Or1(double x) { return x == 0 || std::isnan(x) ? 1 : x; }   // `x || 1`

// makeFFT(n): a radix-2 FFT with its tables in 32-bit floats, each butterfly worked in doubles and stored as floats
struct Fft
{
    int n = 0;
    std::vector<uint32_t> rev;
    std::vector<float> cs, sn;
    explicit Fft(int size) : n(size), rev(size), cs(size / 2), sn(size / 2)
    {
        int bits = 0; while ((1 << bits) < n) bits++;
        for (int i = 0; i < n; i++) { uint32_t r = 0; for (int b = 0; b < bits; b++) r |= ((i >> b) & 1u) << (bits - 1 - b); rev[i] = r; }
        for (int i = 0; i < n / 2; i++) { cs[i] = (float)std::cos(2 * PI * i / n); sn[i] = (float)std::sin(2 * PI * i / n); }
    }
    void operator()(float* re, float* im) const
    {
        for (int i = 0; i < n; i++) { uint32_t j = rev[i]; if ((int)j > i) { std::swap(re[i], re[j]); std::swap(im[i], im[j]); } }
        for (int size = 2; size <= n; size *= 2)
        {
            int half = size / 2, step = n / size;
            for (int i = 0; i < n; i += size)
                for (int j = 0, k = 0; j < half; j++, k += step)
                {
                    int l = i + j + half;
                    double tr = (double)re[l] * cs[k] + (double)im[l] * sn[k], ti = -(double)re[l] * sn[k] + (double)im[l] * cs[k];
                    re[l] = (float)(re[i + j] - tr); im[l] = (float)(im[i + j] - ti);
                    re[i + j] = (float)(re[i + j] + tr); im[i + j] = (float)(im[i + j] + ti);
                }
        }
    }
};

// smooth(a, w): a running mean over w (a float array, as the bench's)
static std::vector<float> Smooth(const std::vector<float>& a, double w)
{
    std::vector<float> o(a.size());
    int h = std::max(1, (int)std::floor(w / 2)), len = (int)a.size();
    for (int i = 0; i < len; i++)
    {
        double sum = 0; int n = 0;
        for (int j = std::max(0, i - h); j <= std::min(len - 1, i + h); j++) { sum += a[j]; n++; }
        o[i] = (float)(sum / n);
    }
    return o;
}
// pct(a, p): the value p of the way up the sorted list
static double Pct(std::vector<float> s, double p)
{
    if (s.empty()) return std::numeric_limits<double>::quiet_NaN();
    std::sort(s.begin(), s.end());
    return s[std::min(s.size() - 1, (size_t)std::floor(p * s.size()))];
}

// ---- the song map ------------------------------------------------------------------------------------------------
struct SongMap
{
    int frames = 0;
    double fr = 0, beat = 0.5, offset = 0, bpm = 120;
    std::vector<float> section, tension;
    std::vector<double> drops;
};

// mapBuffer + buildMap: 1024-sample frames, hop 1024, a Hann window over n - 1
static SongMap BuildMap(const float* x, size_t len, double sr)
{
    const int n = 1024, hop = 1024;
    SongMap m;
    const int frames = (int)std::floor(((double)len - n) / hop);
    m.frames = std::max(0, frames);
    const double fr = sr / hop, binHz = sr / n;
    m.fr = fr;
    if (frames <= 0) return m;
    Fft fft(n);
    std::vector<float> re(n), im(n), mag(n / 2), win(n);
    for (int i = 0; i < n; i++) win[i] = (float)(0.5 - 0.5 * std::cos(2 * PI * i / (n - 1)));
    std::vector<float> E(frames), B(frames), On(frames);
    const int bassTo = std::max(2, (int)JsRound(150 / binHz));
    double prevB = 0;
    for (int f = 0; f < frames; f++)
    {
        size_t o = (size_t)f * hop;
        // the bench mixes its two channels here, (a + b) * 0.5; the mono input is that mix already
        for (int i = 0; i < n; i++) { re[i] = (float)((double)x[o + i] * win[i]); im[i] = 0; }
        fft(re.data(), im.data());
        for (int i = 0; i < n / 2; i++) mag[i] = (float)std::hypot((double)re[i], (double)im[i]);
        double e = 0, b = 0;
        for (int i = 1; i < n / 2; i++) { e += mag[i]; if (i <= bassTo) b += mag[i]; }
        E[f] = (float)std::log10(e + 1e-6); B[f] = (float)b; On[f] = (float)std::max(0.0, b - prevB); prevB = b;
    }
    // sections
    std::vector<float> Es = Smooth(E, JsRound(2 * fr));
    const double lo = Pct(Es, 0.1), hi = Pct(Es, 0.9);
    std::vector<float> norm(frames);
    for (int i = 0; i < frames; i++) norm[i] = (float)std::min(1.0, std::max(0.0, (Es[i] - lo) / std::max(1e-6, hi - lo)));
    std::vector<float> section = Smooth(norm, JsRound(fr));
    // tempo and beat phase, from the onset envelope less its local average
    std::vector<float> Om = Smooth(On, JsRound(fr)), O(frames);
    for (int i = 0; i < frames; i++) O[i] = (float)std::max(0.0, (double)On[i] - Om[i]);
    double best = 0; int bestLag = (int)JsRound(fr / 2);
    for (int lag = (int)std::floor(60 * fr / 180); lag <= (int)std::ceil(60 * fr / 70); lag++)
    {
        double sc = 0; for (int i = 0; i + lag < frames; i++) sc += (double)O[i] * O[i + lag];
        const double bpm = 60 * fr / lag; sc *= std::exp(-0.5 * std::pow(std::log2(bpm / 120), 2));
        if (sc > best) { best = sc; bestLag = lag; }
    }
    const double beat = bestLag / fr;
    int bestOff = 0; double bestSc = -1;
    for (int ph = 0; ph < bestLag; ph++) { double sc = 0; for (int i = ph; i < frames; i += bestLag) sc += O[i]; if (sc > bestSc) { bestSc = sc; bestOff = ph; } }
    const double offset = bestOff / fr;
    // drops
    std::vector<double> drops;
    const int minWin = (int)JsRound(8 * fr);
    std::vector<float> pos; for (float v : O) if (v > 0) pos.push_back(v);
    double onHi = Pct(pos, 0.9); if (!(onHi > 0)) onHi = 0;   // `|| 0`
    struct Cand { double t; int f; double strength; };
    std::vector<Cand> cands;
    for (int f = 1; f < frames; f++)
    {
        if (f < 3 * fr || section[f] <= section[f - 1]) continue;   // the music starting isn't a drop
        double mn = 1; for (int j = std::max(0, f - minWin); j < f; j++) mn = std::min(mn, (double)section[j]);
        const bool crossed = section[f - 1] < 0.6 && section[f] >= 0.6 && mn < 0.3;
        const bool rose = mn < 0.15 && section[f - 1] - mn < 0.3 && section[f] - mn >= 0.3;
        if (!crossed && !rose) continue;
        int at = -1;
        for (int j = std::max(0, f - (int)JsRound(fr)); j < std::min(frames, f + (int)JsRound(6 * fr)); j++) if (O[j] >= onHi * 0.5) { at = j; break; }
        if (at < 0) continue;
        double t = at / fr; t = offset + JsRound((t - offset) / beat) * beat;
        double after = 0; for (int j = f; j < std::min(frames, f + (int)JsRound(3 * fr)); j++) after += section[j];
        cands.push_back({ t, f, after / JsRound(3 * fr) - mn });
    }
    std::stable_sort(cands.begin(), cands.end(), [](const Cand& x, const Cand& y) { return y.strength < x.strength; });
    for (const Cand& c : cands) { bool clear = true; for (double d : drops) if (std::fabs(d - c.t) < 10) { clear = false; break; } if (clear) drops.push_back(c.t); }
    std::sort(drops.begin(), drops.end());
    // build-up tension before each drop
    std::vector<float> tension(frames);
    for (double d : drops)
    {
        const int df = (int)JsRound(d * fr);
        int st = df;
        while (st > 0 && df - st < 10 * fr && st < frames && section[st] >= 0.3) st--;
        st = std::max(st, df - (int)JsRound(10 * fr));
        for (int f = st; f < std::min(df, frames); f++)
            if (f >= 0) tension[f] = (float)std::max((double)tension[f], std::pow((double)(f - st) / std::max(1, df - st), 1.5));
    }
    m.section = section; m.tension = tension; m.drops = drops; m.beat = beat; m.offset = offset; m.bpm = 60 / beat;
    return m;
}

// ---- the beat tracker -----------------------------------------------------------------------------------------------
struct Beats
{
    double bpm = 120, fr = 0, period = 0.5;
    std::vector<double> beats, strength;
    std::vector<float> env;
    int barPhase = 0;
};

static Beats TrackBeats(const float* x, size_t len, double sr)
{
    const int n = 2048, hop = 512, nM = 128, half = n / 2;
    Beats r;
    const double fr = sr / hop; r.fr = fr;
    Fft fft(n);
    // Slaney mel filters (librosa's default), kept sparse
    auto hz2mel = [](double f) { return f < 1000 ? f * 3 / 200 : 15 + std::log(f / 1000) / (std::log(6.4) / 27); };
    auto mel2hz = [](double m) { return m < 15 ? m * 200 / 3 : 1000 * std::exp(std::log(6.4) / 27 * (m - 15)); };
    const double top = hz2mel(sr / 2);
    std::vector<double> pts; for (int i = 0; i < nM + 2; i++) pts.push_back(mel2hz(top * i / (nM + 1)));
    std::vector<int> fk0(nM); std::vector<std::vector<float>> fw(nM);
    for (int m = 0; m < nM; m++)
    {
        const double lo = pts[m], c = pts[m + 1], hi = pts[m + 2], norm = 2 / (hi - lo);
        int k0 = -1;
        for (int k = 0; k <= half; k++)
        {
            const double f = (double)k * sr / n, v = std::max(0.0, std::min((f - lo) / (c - lo), (hi - f) / (hi - c)));
            if (v > 0) { if (k0 < 0) k0 = k; fw[m].push_back((float)(v * norm)); }
            else if (k0 >= 0) break;
        }
        fk0[m] = std::max(0, k0);
    }
    const int T = 1 + (int)std::floor((double)len / hop);
    std::vector<float> win(n); for (int i = 0; i < n; i++) win[i] = (float)(0.5 - 0.5 * std::cos(2 * PI * i / n));
    std::vector<float> re(n), im(n), pw(half + 1), mel((size_t)T * nM);
    double peak = -std::numeric_limits<double>::infinity();
    for (int t = 0; t < T; t++)
    {
        const long long c = (long long)t * hop - half;   // centred frames, zero-padded at the ends
        for (int i = 0; i < n; i++) { long long j = c + i; re[i] = (float)((j >= 0 && j < (long long)len ? (double)x[j] : 0.0) * win[i]); im[i] = 0; }
        fft(re.data(), im.data());
        for (int i = 0; i <= half; i++) pw[i] = (float)((double)re[i] * re[i] + (double)im[i] * im[i]);
        for (int m = 0; m < nM; m++)
        {
            const auto& w = fw[m]; double s = 0;
            for (size_t k = 0; k < w.size(); k++) s += (double)w[k] * pw[fk0[m] + k];
            const double db = 10 * std::log10(std::max(1e-10, s));
            mel[(size_t)t * nM + m] = (float)db; if (db > peak) peak = db;
        }
    }
    for (auto& v : mel) v = (float)std::max((double)v, peak - 80);
    // onset strength: the median rise across the mel bands, delayed two frames
    std::vector<float> env(T), rises(nM);
    for (int t = 3; t < T; t++)
    {
        for (int m = 0; m < nM; m++) rises[m] = (float)std::max(0.0, (double)mel[(size_t)(t - 2) * nM + m] - mel[(size_t)(t - 3) * nM + m]);
        std::sort(rises.begin(), rises.end());
        env[t] = (float)(0.5 * ((double)rises[nM / 2 - 1] + rises[nM / 2]));
    }
    // tempo: the autocorrelation of 8 s windows of it (every 4th frame, normalised), averaged, leaning toward 120 BPM
    const int W = (int)std::floor(8 * fr), A = 1 << (int)std::ceil(std::log2(2.0 * W));
    std::vector<float> ar(A), ai(A), hw(W);
    Fft fa(A);
    for (int i = 0; i < W; i++) hw[i] = (float)(0.5 - 0.5 * std::cos(2 * PI * i / W));
    std::vector<double> tg(W);
    int cols = 0;
    const int STEP = 4, lagLo = std::max(1, (int)std::floor(60 * fr / 240)), lagHi = std::min(W - 1, (int)std::ceil(60 * fr / 40)), nL = lagHi - lagLo + 1;
    std::vector<std::vector<float>> obs;
    for (int t = 0; t < T; t += STEP)
    {
        std::fill(ar.begin(), ar.end(), 0.0f); std::fill(ai.begin(), ai.end(), 0.0f);
        for (int i = 0; i < W; i++) { int j = t - (W >> 1) + i; ar[i] = (float)((j >= 0 && j < T ? (double)env[j] : 0.0) * hw[i]); }
        fa(ar.data(), ai.data());
        for (int i = 0; i < A; i++) { ar[i] = (float)((double)ar[i] * ar[i] + (double)ai[i] * ai[i]); ai[i] = 0; }
        fa(ar.data(), ai.data());
        std::vector<float> o(nL);
        if (ar[0] > 0)
        {
            for (int l = 0; l < W; l++) tg[l] += (double)ar[l] / ar[0];
            cols++;
            for (int k = 0; k < nL; k++) o[k] = (float)std::log1p(1e6 * std::max(0.0, (double)ar[lagLo + k] / ar[0]));
        }
        else std::fill(o.begin(), o.end(), -1e3f);
        obs.push_back(std::move(o));
    }
    int bestLag = (int)JsRound(fr / 2); double best = -std::numeric_limits<double>::infinity();
    for (int l = 1; l < W; l++)
    {
        const double bpm = 60 * fr / l; if (bpm > 320) continue;
        const double sc = std::log1p(1e6 * tg[l] / std::max(1, cols)) - 0.5 * std::pow(std::log2(bpm / 120), 2);
        if (sc > best) { best = sc; bestLag = l; }
    }
    const double bpm = 60 * fr / bestLag; const int period = (int)JsRound(fr * 60 / bpm);
    // the tempo over time: Viterbi through the windows' scores, each leaning toward the song's overall tempo
    const int gLag = bestLag; const double LAM = 50;
    for (auto& o : obs) for (int k = 0; k < nL; k++) o[k] = (float)(o[k] + -0.5 * std::pow(std::log2(60 * fr / (lagLo + k) / 120), 2));
    std::vector<float> cost((size_t)nL * nL);
    for (int a = 0; a < nL; a++) for (int b = 0; b < nL; b++) cost[(size_t)a * nL + b] = (float)(LAM * std::pow(std::log2((double)(lagLo + a) / (lagLo + b)), 2));
    std::vector<float> acc = obs[0];
    std::vector<std::vector<int16_t>> from;
    for (size_t c = 1; c < obs.size(); c++)
    {
        std::vector<float> nx(nL); std::vector<int16_t> bk(nL);
        for (int b = 0; b < nL; b++)
        {
            double bs = -std::numeric_limits<double>::infinity(); int ba = 0;
            for (int a = 0; a < nL; a++) { const double v = (double)acc[a] - cost[(size_t)a * nL + b]; if (v > bs) { bs = v; ba = a; } }
            nx[b] = (float)(bs + obs[c][b]); bk[b] = (int16_t)ba;
        }
        acc = nx; from.push_back(std::move(bk));
    }
    std::vector<int> path(obs.size());
    int end = 0; for (int k = 1; k < nL; k++) if (acc[k] > acc[end]) end = k;
    path[obs.size() - 1] = end;
    for (size_t c = obs.size() - 1; c > 0; c--) path[c - 1] = from[c - 1][path[c]];
    // the path counts only where it holds a different tempo (not double or half) for 10 s or more, and fits the hits
    auto related = [](double q) { for (double s : { 0.5, 1.0, 2.0 }) if (std::fabs(std::log2(q / s)) < 0.085) return true; return false; };
    auto comb = [&](int p, int a, int b)
    {
        double bestC = 0;
        for (int ph = 0; ph < p; ph++) { double sum = 0; int cnt = 0; for (int f = a + ph; f < b; f += p) { sum += env[f]; cnt++; } if (cnt) bestC = std::max(bestC, sum / cnt); }
        return bestC;
    };
    std::vector<uint8_t> use(obs.size());
    const int minRun = (int)JsRound(10 * fr / STEP);
    for (int c = 0; c < (int)obs.size();)
    {
        int e = c; const double q = (double)(lagLo + path[c]) / gLag;
        if (related(q)) { c++; continue; }
        while (e < (int)obs.size() && std::fabs(std::log2((double)(lagLo + path[e]) / (lagLo + path[c]))) < 0.15) e++;
        if (e - c >= minRun)
        {
            const int a0 = c * STEP, b0 = std::min(T, e * STEP);
            if (comb(lagLo + path[c], a0, b0) > 1.1 * comb(gLag, a0, b0)) std::fill(use.begin() + c, use.begin() + e, (uint8_t)1);
        }
        c = std::max(e, c + 1);
    }
    std::vector<float> perAt(T);
    for (int i = 0; i < T; i++) { int c = std::min((int)obs.size() - 1, (int)JsRound((double)i / STEP)); perAt[i] = (float)(use[c] ? lagLo + path[c] : gLag); }
    // local score: the onsets normalised and smoothed by a Gaussian a beat wide
    double mu = 0; for (float v : env) mu += v; mu /= T;
    double sd = 0; for (float v : env) sd += (v - mu) * (v - mu); sd = std::sqrt(sd / std::max(1, T - 1)); if (sd == 0 || std::isnan(sd)) sd = 1;   // `|| 1`
    std::vector<double> g; for (int i = -period; i <= period; i++) g.push_back(std::exp(-0.5 * std::pow((double)i * 32 / period, 2)));
    std::vector<float> local(T);
    for (int t = 0; t < T; t++)
    {
        double s = 0;
        for (int i = 0; i < (int)g.size(); i++) { int j = t + i - period; if (j >= 0 && j < T) s += env[j] / sd * g[g.size() - 1 - i]; }
        local[t] = (float)s;
    }
    // dynamic programming: each frame's best previous beat 0.5-2 periods back, penalising tempo drift (tightness 100)
    std::vector<int> back(T); std::vector<float> cum(T);
    double lmax = 0; for (float v : local) lmax = std::max(lmax, (double)v);
    bool first = true;
    for (int i = 0; i < T; i++)
    {
        double bs = -std::numeric_limits<double>::infinity(); int bl = -1;
        for (int loc = i - (int)JsRound(perAt[i] / 2.0); loc >= i - 2 * perAt[i]; loc--)
        {
            if (loc < 0) break;
            const double sc = cum[loc] - 100 * std::pow(std::log((double)(i - loc)) - std::log((double)perAt[i]), 2);
            if (sc > bs) { bs = sc; bl = loc; }
        }
        cum[i] = (float)(local[i] + (bl >= 0 ? bs : 0));
        if (first && local[i] < 0.01 * lmax) back[i] = -1; else { back[i] = bl; first = false; }
    }
    // the last beat: the latest local max of the cumulative score above half the median of them; then trace back
    std::vector<int> maxes; for (int i = 1; i < T - 1; i++) if (cum[i] > cum[i - 1] && cum[i] >= cum[i + 1]) maxes.push_back(i);
    std::vector<float> mv; for (int i : maxes) mv.push_back(cum[i]);
    std::sort(mv.begin(), mv.end());
    const double med = mv.empty() ? 0 : mv[mv.size() >> 1];
    int tail = -1; for (int i : maxes) if (cum[i] >= 0.5 * med) tail = i;
    std::vector<int> beats; for (int i = tail; i >= 0; i = back[i]) beats.push_back(i);
    std::reverse(beats.begin(), beats.end());
    // trim weak beats at the ends (fade-ins and outs), as librosa's trim
    std::vector<double> sm(beats.size());
    for (int k = 0; k < (int)beats.size(); k++)
    {
        double s = 0;
        for (int d = -2; d <= 2; d++) { int q = k + d; if (q >= 0 && q < (int)beats.size()) s += local[beats[q]] * (0.5 - 0.5 * std::cos(2 * PI * (d + 3) / 6)); }
        sm[k] = s;
    }
    double ss = 0; for (double v : sm) ss += v * v;
    const double thr = 0.5 * std::sqrt(ss / std::max<size_t>(1, sm.size()));
    int a = 0, b = (int)beats.size() - 1; while (a <= b && sm[a] <= thr) a++; while (b >= a && sm[b] <= thr) b--;
    for (int k = a; k <= b; k++) r.beats.push_back((double)beats[k] * hop / sr);
    r.bpm = bpm; r.env = std::move(env);
    return r;
}

// beatStrengths: each beat's onset strength against the song's strong beats (the 90th percentile), which of the four
// beat positions is the bar's first (the strongest on average), and the median beat gap
static void BeatStrengths(Beats& r)
{
    std::vector<double> st;
    for (double t : r.beats)
    {
        int f = (int)JsRound(t * r.fr); double m = 0;
        for (int j = std::max(0, f - 2); j <= std::min((int)r.env.size() - 1, f + 2); j++) m = std::max(m, (double)r.env[j]);
        st.push_back(m);
    }
    std::vector<double> sorted = st; std::sort(sorted.begin(), sorted.end());
    double strong = sorted.empty() ? 0 : sorted[(size_t)std::floor(st.size() * 0.9)];
    if (strong == 0 || std::isnan(strong)) strong = 1;   // `|| 1`
    r.strength.clear(); for (double v : st) r.strength.push_back(std::min(1.2, v / strong));
    double sum[4] = {};
    for (size_t i = 0; i < r.strength.size(); i++) sum[i % 4] += r.strength[i];
    r.barPhase = 0; for (int i = 1; i < 4; i++) if (sum[i] > sum[r.barPhase]) r.barPhase = i;
    std::vector<double> gaps; for (size_t i = 1; i < r.beats.size(); i++) gaps.push_back(r.beats[i] - r.beats[i - 1]);
    std::sort(gaps.begin(), gaps.end());
    r.period = gaps.empty() || !(gaps[gaps.size() >> 1] != 0) ? 0.5 : gaps[gaps.size() >> 1];
}

// ---- the layers ---------------------------------------------------------------------------------------------------
// analyseLayers: the song split into what hits and what holds (median-filter HPSS), and from the held part the pads, the
// harmony per beat on the circle of fifths, the lead melody; and the song's structure, per bar, as lettered sections.
// (The bench also returns each beat's chord change; nothing draws it, so it isn't kept.)
struct LayerSection { double start, end; int label; };
struct Layers
{
    double fr = 0;
    std::vector<float> pad, melF, melS;
    std::vector<double> harm, clarity;
    std::vector<LayerSection> sections;
};

static Layers AnalyseLayers(const float* x, size_t len, double sr, const std::vector<double>& beats, int barPhase)
{
    const int n = 4096, hop = 2048, half = n / 2;
    Fft fft(n);
    const double binHz = sr / n, fr = sr / hop;
    const int lo = std::max(1, (int)JsRound(60 / binHz)), hi = std::min(half - 1, (int)JsRound(5000 / binHz)), nb = hi - lo + 1;
    const int T = std::max(1, (int)std::floor(((double)len - n) / hop) + 1);
    std::vector<float> win(n); for (int i = 0; i < n; i++) win[i] = (float)(0.5 - 0.5 * std::cos(2 * PI * i / n));
    std::vector<float> re(n), im(n), S((size_t)T * nb);
    for (int t = 0; t < T; t++)
    {
        for (int i = 0; i < n; i++) { size_t j = (size_t)t * hop + i; re[i] = (float)((j < len ? (double)x[j] : 0.0) * win[i]); im[i] = 0; }
        fft(re.data(), im.data());
        for (int k = 0; k < nb; k++) { const int b = lo + k; S[(size_t)t * nb + k] = (float)((double)re[b] * re[b] + (double)im[b] * im[b]); }
    }
    // medians: 9 frames (~0.4 s) along time, 17 bins (~200 Hz) along frequency; a soft mask keeps the held share
    auto med = [](float* buf, int m) { std::nth_element(buf, buf + (m >> 1), buf + m); return (double)buf[m >> 1]; };
    float bt[9], bf[17];
    std::vector<float> Hp((size_t)T * nb);
    for (int t = 0; t < T; t++)
        for (int k = 0; k < nb; k++)
        {
            int m = 0; for (int d = -4; d <= 4; d++) bt[m++] = S[(size_t)std::min(T - 1, std::max(0, t + d)) * nb + k];
            const double h = med(bt, 9);
            m = 0; for (int d = -8; d <= 8; d++) bf[m++] = S[(size_t)t * nb + std::min(nb - 1, std::max(0, k + d))];
            const double p = med(bf, 17);
            Hp[(size_t)t * nb + k] = (float)((double)S[(size_t)t * nb + k] * h * h / (h * h + p * p + 1e-20));
        }
    auto H = [&](int t, int k) { return (double)Hp[(size_t)t * nb + k]; };
    Layers r; r.fr = fr;
    // pads: held energy 200 Hz - 5 kHz, as amplitude, averaged over ~1 s, against the song's 95th percentile
    const int k200 = std::max(0, (int)JsRound(200 / binHz) - lo);
    std::vector<float> raw(T);
    for (int t = 0; t < T; t++) { double e = 0; for (int k = k200; k < nb; k++) e += H(t, k); raw[t] = (float)std::sqrt(e); }
    r.pad.resize(T);
    const int w = (int)JsRound(fr / 2);
    for (int t = 0; t < T; t++) { double s = 0; int c = 0; for (int j = std::max(0, t - w); j <= std::min(T - 1, t + w); j++) { s += raw[j]; c++; } r.pad[t] = (float)(s / c); }
    const double p95 = Or1(Pct(r.pad, 0.95));
    for (int t = 0; t < T; t++) r.pad[t] = (float)std::min(1.2, r.pad[t] / p95);
    // the song's tuning (as librosa's estimate_tuning): how far its notes sit off A = 440, from the held part's strong
    // peaks - a song tuned a quarter-tone off would otherwise land every note between two pitch classes
    double tx = 0, ty = 0;
    for (int t = 0; t < T; t += 2)
        for (int k = 1; k < nb - 1; k++)
        {
            const double v = H(t, k); if (v <= H(t, k - 1) || v < H(t, k + 1)) continue;
            // the peak's true frequency, from the bins either side (parabolic), then its offset from the nearest semitone
            const double l = std::sqrt(H(t, k - 1)), c = std::sqrt(v), rr = std::sqrt(H(t, k + 1));
            const double dlt = 0.5 * (l - rr) / Or1(l - 2 * c + rr);
            const double f = ((double)(lo + k) + dlt) * binHz; if (f < 200) continue;
            const double st = 12 * std::log2(f / 440), a = 2 * PI * (st - JsRound(st));
            tx += c * std::cos(a); ty += c * std::sin(a);
        }
    const double tuning = std::atan2(ty, tx) / (2 * PI);
    // chroma of the held part (bins from 60 Hz), as amplitude, each frame scaled to its loudest note
    std::vector<int8_t> pc(nb);
    for (int k = 0; k < nb; k++)
    {
        const double f = (double)(lo + k) * binHz;
        pc[k] = f < 60 ? -1 : (int8_t)((((int)JsRound(12 * std::log2(f / 440) - tuning + 69) % 12) + 12) % 12);
    }
    std::vector<float> chroma((size_t)T * 12);
    for (int t = 0; t < T; t++)
    {
        float* ch = &chroma[(size_t)t * 12];
        for (int k = 0; k < nb; k++) if (pc[k] >= 0) ch[pc[k]] = (float)(ch[pc[k]] + std::sqrt(H(t, k)));
        double mx = 0; for (int q = 0; q < 12; q++) mx = std::max(mx, (double)ch[q]);
        if (mx > 0) for (int q = 0; q < 12; q++) ch[q] = (float)(ch[q] / mx);
    }
    // the frames a beat covers: to the next beat, or half a second for the last
    const int nBeats = (int)beats.size();
    auto span = [&](int i, int* a, int* b)
    {
        *a = std::max(0, (int)std::floor(beats[i] * fr));
        *b = std::min(T, std::max(*a + 1, (int)std::floor((i + 1 < nBeats ? beats[i + 1] : beats[i] + 0.5) * fr)));
    };
    // per beat: the chroma over the beat (the median per note - a passing note doesn't count) on the circle of fifths:
    // its angle, and how clearly it points (0..1)
    std::vector<std::vector<float>> beatChroma;
    std::vector<double> col;
    for (int i = 0; i < nBeats; i++)
    {
        int a, b; span(i, &a, &b);
        float c[12];
        for (int q = 0; q < 12; q++)
        {
            col.clear(); for (int t = a; t < b; t++) col.push_back(chroma[(size_t)t * 12 + q]);
            std::sort(col.begin(), col.end()); c[q] = col.empty() ? 0 : (float)col[col.size() >> 1];
        }
        double vx = 0, vy = 0, sum = 0, nn = 0;
        for (int q = 0; q < 12; q++) { const double ang = 2 * PI * ((q * 7) % 12) / 12; vx += c[q] * std::cos(ang); vy += c[q] * std::sin(ang); sum += c[q]; nn += (double)c[q] * c[q]; }
        r.harm.push_back(std::atan2(vy, vx)); r.clarity.push_back(sum > 0 ? std::min(1.0, 3 * std::hypot(vx, vy) / sum) : 0);
        const double norm = Or1(std::sqrt(nn));
        std::vector<float> u(12); for (int q = 0; q < 12; q++) u[q] = (float)(c[q] / norm);
        beatChroma.push_back(std::move(u));
    }
    // lead melody: per frame, the pitch from 196 Hz to 1.3 kHz whose harmonics (x1..x5, weighted 0.8^n) carry the most
    // held energy; kept where it stands clearly above the other candidates and the held part is loud enough
    std::vector<float> melF(T), melS(T), melE(T);
    for (int t = 0; t < T; t++)
    {
        auto amp = [&](int k) { return k >= 0 && k < nb ? std::sqrt(H(t, k)) : 0.0; };
        double best = 0, bestF = 0, sum = 0; int cnt = 0;
        for (int m = 55; m <= 88; m++)
        {
            const double f0 = 440 * std::pow(2.0, (m + tuning - 69) / 12); double sal = 0;
            for (int hh = 1; hh <= 5; hh++) sal += std::pow(0.8, hh - 1) * amp((int)JsRound(hh * f0 / binHz) - lo);
            sum += sal; cnt++; if (sal > best) { best = sal; bestF = f0; }
        }
        melF[t] = (float)bestF; melE[t] = (float)best; melS[t] = (float)(best / (sum / cnt + 1e-12));   // salience against the average
    }
    const double e90 = Or1(Pct(melE, 0.9));
    for (int t = 0; t < T; t++) melS[t] = (float)(std::min(1.0, std::max(0.0, (melS[t] - 2.2) / 1.5)) * std::min(1.0, melE[t] / e90));
    // contrast: only clearly melodic moments (roughly the top quarter to third of a song) light the highlight
    for (int t = 0; t < T; t++) { const double u = std::min(1.0, std::max(0.0, (melS[t] - 0.45) / 0.45)); melS[t] = (float)(u * u * (3 - 2 * u)); }
    // a one-frame blip isn't a note: keep a pitch only where its neighbours agree (median of three)
    r.melF = melF;
    for (int t = 1; t < T - 1; t++) { float a3[3] = { melF[t - 1], melF[t], melF[t + 1] }; std::sort(a3, a3 + 3); r.melF[t] = a3[1]; }
    r.melS = std::move(melS);
    // song structure: per bar, harmony (chroma) and timbre (8 log bands of the whole spectrum); boundaries where a
    // checkerboard kernel (Foote) finds the sound changing; segments that sound alike share a letter
    const int nbands = 8;
    int edges[nbands + 1];
    for (int q = 0; q <= nbands; q++)
        edges[q] = (int)JsRound(lo + (double)(hi - lo) * (std::pow(2.0, (double)q / nbands * std::log2(5000.0 / 60)) - 1) / (5000.0 / 60 - 1)) - lo;
    std::vector<std::vector<float>> beatTimbre(nBeats, std::vector<float>(nbands));
    for (int i = 0; i < nBeats; i++)
    {
        int a, b; span(i, &a, &b);
        for (int q = 0; q < nbands; q++)
        {
            double e = 0; int c = 0;
            for (int t = a; t < b; t++) for (int k = edges[q]; k < std::max(edges[q] + 1, edges[q + 1]); k++) { e += S[(size_t)t * nb + k]; c++; }
            beatTimbre[i][q] = (float)std::log(1 + e / std::max(1, c));
        }
    }
    // timbre standardised over the song, so it's the change that counts, not the level
    float mu[nbands] = {}, sd[nbands] = {};
    for (const auto& v : beatTimbre) for (int q = 0; q < nbands; q++) mu[q] = (float)(mu[q] + (double)v[q] / nBeats);
    for (const auto& v : beatTimbre) for (int q = 0; q < nbands; q++) sd[q] = (float)(sd[q] + ((double)v[q] - mu[q]) * ((double)v[q] - mu[q]) / nBeats);
    const int nf = 12 + nbands;
    std::vector<std::vector<double>> feat(nBeats, std::vector<double>(nf));
    for (int i = 0; i < nBeats; i++)
    {
        for (int q = 0; q < 12; q++) feat[i][q] = beatChroma[i][q];
        for (int q = 0; q < nbands; q++) { feat[i][12 + q] = 0.7 * ((double)beatTimbre[i][q] - mu[q]) / Or1(std::sqrt((double)sd[q])) / std::sqrt((double)nbands); }
    }
    auto unit = [](const std::vector<float>& f)
    {
        double n2 = 0; for (float v : f) n2 += (double)v * v; n2 = Or1(std::sqrt(n2));
        std::vector<float> o(f.size()); for (size_t q = 0; q < f.size(); q++) o[q] = (float)(f[q] / n2); return o;
    };
    auto sim = [](const std::vector<float>& p, const std::vector<float>& q) { double d = 0; for (size_t k = 0; k < p.size(); k++) d += (double)p[k] * q[k]; return d; };
    std::vector<int> barStart; std::vector<std::vector<float>> bu;
    for (int i = ((barPhase % 4) + 4) % 4; i + 4 <= nBeats; i += 4)
    {
        std::vector<float> f(nf);
        for (int k = i; k < i + 4; k++) for (int q = 0; q < nf; q++) f[q] = (float)(f[q] + feat[k][q] / 4);
        barStart.push_back(i); bu.push_back(unit(f));
    }
    const int nB = (int)bu.size(), K = 4;
    std::vector<float> nov(nB);
    for (int b = K; b < nB - K; b++)
    {
        double s1 = 0;
        for (int i = -K; i < K; i++) for (int j = -K; j < K; j++)
        {
            const double wk = std::exp(-((i + 0.5) * (i + 0.5) + (j + 0.5) * (j + 0.5)) / (2 * std::pow(K / 2.0, 2)));
            s1 += ((i < 0) == (j < 0) ? 1 : -1) * wk * sim(bu[b + i], bu[b + j]);
        }
        nov[b] = (float)std::max(0.0, s1);
    }
    double nm = 0; for (float v : nov) nm += v; nm /= std::max(1, nB);
    double nsd = 0; for (float v : nov) nsd += (v - nm) * (v - nm); nsd = std::sqrt(nsd / std::max(1, nB));
    auto novAt = [&](int b) { return b >= 0 && b < nB ? (double)nov[b] : 0.0; };   // `?? 0`
    std::vector<int> cuts{ 0 };
    for (int b = 1; b < nB - 1; b++)
        if (nov[b] > nm + 0.5 * nsd && nov[b] >= nov[b - 1] && nov[b] >= nov[b + 1] && nov[b] >= novAt(b - 2) && nov[b] >= novAt(b + 2) && b - cuts.back() >= 4)
            cuts.push_back(b);
    cuts.push_back(nB);
    std::vector<std::vector<float>> cents;
    for (size_t k = 0; k + 1 < cuts.size(); k++)
    {
        std::vector<float> f(nB ? nf : 1);
        for (int b = cuts[k]; b < cuts[k + 1]; b++) for (int q = 0; q < nf; q++) f[q] = (float)(f[q] + bu[b][q]);
        const std::vector<float> cf = unit(f);
        int lab = -1; double bs = 0.92;
        for (size_t li = 0; li < cents.size(); li++) { const double v = sim(cents[li], cf); if (v > bs) { bs = v; lab = (int)li; } }
        if (lab < 0) { lab = std::min(5, (int)cents.size()); if (cents.size() < 6) cents.push_back(cf); }
        const int startBeat = cuts[k] < nB ? barStart[cuts[k]] : 0;
        const bool last = k + 2 == cuts.size();
        r.sections.push_back({ k == 0 ? 0.0 : beats[startBeat], last ? 1e9 : beats[std::min(nBeats - 1, barStart[cuts[k + 1] - 1] + 4)], lab });
    }
    return r;
}

// ---- the analysis file (a song's .vab, in a VisualAnalysis folder) -------------------------------------------------
// A header (magic, VIS_ANALYSIS_VERSION, the song file's size and last write - a mismatch on any means analyse again),
// then tagged chunks, so a later version can add its own without breaking the reader:
//   "MAP " fr, beat, offset, bpm (f64); n, section[n], tension[n] (f32); count, drops (f64)
//   "BEAT" bpm, period (f64), barPhase (i32); n, beats[n], strength[n] (f64)
//   "LAYR" fr (f64); n, pad[n], melF[n], melS[n] (f32); n, harm[n], clarity[n] (f32); n, n x { start, end (f64), label (i32) }
static const uint32_t FileMagic = 0x31415655u;   // "UVA1"

struct Writer
{
    std::vector<uint8_t> buf;
    template <class T> void Put(const T& v) { const uint8_t* p = (const uint8_t*)&v; buf.insert(buf.end(), p, p + sizeof(T)); }
    template <class T> void Many(const std::vector<T>& v) { Put((uint32_t)v.size()); Raw(v); }
    template <class T> void Raw(const std::vector<T>& v) { const uint8_t* p = (const uint8_t*)v.data(); buf.insert(buf.end(), p, p + v.size() * sizeof(T)); }
};

// Writes the file whole, through a temporary name, so a reader never sees half of one.
static bool Save(const std::wstring& path, const SongMap& map, const Beats& beats, const Layers& layers, int64_t sourceBytes, int64_t sourceTime)
{
    Writer w;
    w.Put(FileMagic); w.Put((uint32_t)VIS_ANALYSIS_VERSION); w.Put(sourceBytes); w.Put(sourceTime);
    Writer m;
    m.Put(map.fr); m.Put(map.beat); m.Put(map.offset); m.Put(map.bpm); m.Many(map.section);
    { std::vector<float> t = map.tension; t.resize(map.section.size()); m.buf.insert(m.buf.end(), (const uint8_t*)t.data(), (const uint8_t*)(t.data() + t.size())); }
    m.Many(map.drops);
    w.buf.insert(w.buf.end(), { 'M', 'A', 'P', ' ' }); w.Put((uint32_t)m.buf.size()); w.buf.insert(w.buf.end(), m.buf.begin(), m.buf.end());
    Writer b;
    b.Put(beats.bpm); b.Put(beats.period); b.Put((int32_t)beats.barPhase); b.Many(beats.beats);
    { std::vector<double> s = beats.strength; s.resize(beats.beats.size()); b.buf.insert(b.buf.end(), (const uint8_t*)s.data(), (const uint8_t*)(s.data() + s.size())); }
    w.buf.insert(w.buf.end(), { 'B', 'E', 'A', 'T' }); w.Put((uint32_t)b.buf.size()); w.buf.insert(w.buf.end(), b.buf.begin(), b.buf.end());
    Writer l;
    l.Put(layers.fr); l.Many(layers.pad); l.Raw(layers.melF); l.Raw(layers.melS);
    auto f32 = [](const std::vector<double>& v) { std::vector<float> o(v.size()); for (size_t i = 0; i < v.size(); i++) o[i] = (float)v[i]; return o; };
    l.Many(f32(layers.harm)); l.Raw(f32(layers.clarity));
    l.Put((uint32_t)layers.sections.size()); for (const LayerSection& sc : layers.sections) { l.Put(sc.start); l.Put(sc.end); l.Put((int32_t)sc.label); }
    w.buf.insert(w.buf.end(), { 'L', 'A', 'Y', 'R' }); w.Put((uint32_t)l.buf.size()); w.buf.insert(w.buf.end(), l.buf.begin(), l.buf.end());

    size_t slash = path.find_last_of(L"\\/");
    if (slash != std::wstring::npos) CreateDirectoryW(path.substr(0, slash).c_str(), nullptr);
    std::wstring tmp = path + L".tmp";
    FILE* f = _wfopen(tmp.c_str(), L"wb");
    if (!f) return false;
    bool ok = std::fwrite(w.buf.data(), 1, w.buf.size(), f) == w.buf.size();
    ok = std::fclose(f) == 0 && ok;
    if (!ok || !MoveFileExW(tmp.c_str(), path.c_str(), MOVEFILE_REPLACE_EXISTING)) { DeleteFileW(tmp.c_str()); return false; }
    return true;
}

// Reads a song's analysis into the fountain's form; false when the file is missing, from another version, or for
// another state of the song file.
static bool Load(const std::wstring& path, int64_t sourceBytes, int64_t sourceTime, fountain::Song& song)
{
    FILE* f = _wfopen(path.c_str(), L"rb");
    if (!f) return false;
    std::vector<uint8_t> d;
    uint8_t chunk[65536]; size_t got;
    while ((got = std::fread(chunk, 1, sizeof(chunk), f)) > 0) d.insert(d.end(), chunk, chunk + got);
    std::fclose(f);
    size_t at = 0;
    auto take = [&](void* dst, size_t n) { if (at + n > d.size()) return false; std::memcpy(dst, d.data() + at, n); at += n; return true; };
    uint32_t magic = 0, version = 0; int64_t bytes = 0, time = 0;
    if (!take(&magic, 4) || !take(&version, 4) || !take(&bytes, 8) || !take(&time, 8)) return false;
    if (magic != FileMagic || version != VIS_ANALYSIS_VERSION || bytes != sourceBytes || time != sourceTime) return false;
    fountain::Song s;
    while (at + 8 <= d.size())
    {
        char tag[4]; uint32_t len = 0;
        take(tag, 4); take(&len, 4);
        size_t end = at + len;
        if (end > d.size()) return false;
        uint32_t n = 0;
        if (!std::memcmp(tag, "MAP ", 4))
        {
            double bpm;
            take(&s.mapFr, 8); take(&s.beat, 8); take(&s.offset, 8); take(&bpm, 8);
            take(&n, 4); s.section.resize(n); s.tension.resize(n);
            take(s.section.data(), n * 4); take(s.tension.data(), n * 4);
            take(&n, 4); s.drops.resize(n); take(s.drops.data(), n * 8);
            s.map = !s.section.empty();
        }
        else if (!std::memcmp(tag, "BEAT", 4))
        {
            double bpm; int32_t phase = 0;
            take(&bpm, 8); take(&s.period, 8); take(&phase, 4); s.barPhase = phase;
            take(&n, 4); s.beatT.resize(n); s.strength.resize(n);
            take(s.beatT.data(), n * 8); take(s.strength.data(), n * 8);
            s.beats = !s.beatT.empty();
        }
        else if (!std::memcmp(tag, "LAYR", 4))
        {
            double fr = 0; take(&fr, 8); s.lyFr = (float)fr;
            take(&n, 4); s.pad.resize(n); s.melF.resize(n); s.melS.resize(n);
            take(s.pad.data(), n * 4); take(s.melF.data(), n * 4); take(s.melS.data(), n * 4);
            take(&n, 4); s.harm.resize(n); s.clarity.resize(n); take(s.harm.data(), n * 4); take(s.clarity.data(), n * 4);
            take(&n, 4); s.sections.resize(n);
            for (auto& sc : s.sections) { double a = 0, b = 0; int32_t label = 0; take(&a, 8); take(&b, 8); take(&label, 4); sc = { (float)a, (float)b, label }; }
            s.layers = !s.pad.empty();
        }
        at = end;   // past anything this version doesn't read
    }
    song = std::move(s);
    return true;
}

} // namespace analysis
