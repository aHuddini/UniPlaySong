# Generates UPS's own MilkDrop presets. Written for UPS, so they ship under UPS's licence, unlike the community
# preset packs (no licence, never bundled). Run it to regenerate the .milk files here; native/visualizer/build.cmd copies
# the style that ships (Glow Bars, for now) into src/Audio/Native/Visualizer/presets/.
#
# Every style shares one bar engine. Order inside one projectM frame:
#   1. preset per-frame code: turns last frame's spectrum (gmem[1000..1511]) into N log-spaced bar levels -
#      auto-gain, a 30 dB window, time-based rise and fall, a held peak per bar - then packs them into q1..q32
#      for the shaders
#   2. warp shader: last frame (faded, maybe drifted for trails) plus this frame's bars -> the main image
#   3. shapes (LEDs, particles) drawn onto the main image
#   4. wave 0, invisible: copies this frame's 512 spectrum bins into gmem for the next frame
#   5. comp shader: main image + blur of it (bloom) + a soft bass-driven backlight -> screen
# Pure black is see-through once the renderer keys it, so bloom and backlight tint whatever the theme shows behind.
#
# Layout contract with theme pages - the ART SLOT: a disc at the centre of the frame, 0.30 of its height across, kept
# clear for cover art. Bar styles are wings either side of it (bass next to the art, starting 0.19 of the height
# from the centre), growing up and down from the centre line with a dimmer reflection below; Halo Ring circles it.
# A page puts its cover there and every style frames it.
import os
import sys

LO_BIN, HI_BIN = 1.5, 300.0   # ~70 Hz .. ~14 kHz at 47 Hz per bin
RISE, FALL = 14.0, 0.8        # rise: approach rate per second; fall: full heights per second

HEADER = """[preset00]
MILKDROP_PRESET_VERSION=201
PSVERSION=2
PSVERSION_WARP=2
PSVERSION_COMP=2
fRating=5.0
fGammaAdj=1.0
fDecay=1.0
fVideoEchoZoom=1.0
fVideoEchoAlpha=0.0
nVideoEchoOrientation=0
nWaveMode=0
bAdditiveWaves=0
bWaveDots=0
bWaveThick=0
bModWaveAlphaByVolume=0
bMaximizeWaveColor=0
bTexWrap=0
bDarkenCenter=0
bRedBlueStereo=0
bBrighten=0
bDarken=0
bSolarize=0
bInvert=0
fWaveAlpha=0.0
fWaveScale=1.0
fWaveSmoothing=0.0
fWaveParam=0.0
fWarpAnimSpeed=1.0
fWarpScale=1.0
fZoomExponent=1.0
fShader=0.0
zoom=1.0
rot=0.0
cx=0.5
cy=0.5
dx=0.0
dy=0.0
warp=0.0
sx=1.0
sy=1.0
wave_r=0.0
wave_g=0.0
wave_b=0.0
wave_a=0.0
ob_size=0.0
ob_a=0.0
ib_size=0.0
ib_a=0.0
nMotionVectorsX=0.0
nMotionVectorsY=0.0
mv_a=0.0
b1ed=0.0
"""

ANALYSIS_WAVE = """wavecode_0_enabled=1
wavecode_0_samples=512
wavecode_0_sep=0
wavecode_0_bSpectrum=1
wavecode_0_bUseDots=0
wavecode_0_bDrawThick=0
wavecode_0_bAdditive=0
wavecode_0_scaling=1.0
wavecode_0_smoothing=0.0
wavecode_0_r=0
wavecode_0_g=0
wavecode_0_b=0
wavecode_0_a=0
wave_0_per_point1=i=floor(sample*511+0.5);
wave_0_per_point2=gmem[1000+i]=(value1+value2)*0.5;
wave_0_per_point3=a=0;
"""


def engine(n):
    # gmem[0..n) level 0..1, gmem[100..) peak, gmem[300..) peak hold time; reg00 auto-gain; reg01 smoothed loudness;
    # reg02/reg03 the wings' gap and length in width units (for shapes, which get no frame size)
    return f"""dt=1/max(fps, 15); n={n}; lo={LO_BIN}; hi={HI_BIN}; loudest=0; total=0; bi=0;
loop(n,
  f0=lo*pow(hi/lo, bi/n); f1=lo*pow(hi/lo, (bi+1)/n);
  v=0;
  f1-f0 < 1 ? (
    c=(f0+f1)/2; i=floor(c); t=c-i;
    v=gmem[1000+i]*(1-t) + gmem[1001+i]*t;
  ) : (
    i=floor(f0);
    loop(floor(f1)-floor(f0)+1, v=max(v, gmem[1000+i]); i+=1;);
  );
  v*=1+1.5*bi/n;
  gmem[200+bi]=v;
  loudest=max(loudest, v);
  bi+=1;
);
reg00=max(max(loudest, reg00*pow(0.5, dt/4)), 0.02);
bi=0;
loop(n,
  t=min(1, max(0, 1+20*log10(max(gmem[200+bi], 0.00001)/reg00)/30));
  h=gmem[bi];
  h=t > h ? h+(t-h)*min(1, dt*{RISE}) : max(t, h-dt*{FALL});
  gmem[bi]=h;
  total+=h;
  pk=gmem[100+bi];
  pk > h ? (
    gmem[300+bi] > 0 ? (gmem[300+bi]-=dt;) : (pk=max(h, pk-dt*0.35););
  ) : (
    pk=h; gmem[300+bi]=0.4;
  );
  gmem[100+bi]=pk;
  bi+=1;
);
reg01=reg01+(total/n-reg01)*min(1, dt*6);
reg02=0.19/aspecty; reg03=0.44-reg02;"""


def pack_pairs(n):
    # q(k+1) = bar 2k in the integer part (x999), bar 2k+1 in the fraction
    return "\n".join(f"q{k + 1}=floor(gmem[{2 * k}]*999)+min(gmem[{2 * k + 1}], 0.999);" for k in range(n // 2))


def pack_peaks(n):
    # q(k+1) = level of bar k in the integer part, its peak in the fraction
    return "\n".join(f"q{k + 1}=floor(gmem[{k}]*999)+min(gmem[{100 + k}], 0.999);" for k in range(n))


# HLSL helpers placed before shader_body. Q(k) picks q(k+1); BAR(i) decodes bar i of a pair-packed set.
def q_select():
    # A sum of step windows: a 32-deep ?: chain gets regrouped by the HLSL translator and always lands on q32.
    terms = " + ".join(f"q{k + 1} * (step({k - 0.5}, k) - step({k + 0.5}, k))" for k in range(32))
    return f"float Q(float k) {{ return {terms}; }}"

PAIR_HELPERS = q_select() + """
float BAR(float i) { float q = Q(floor(i * 0.5)); return frac(i * 0.5) < 0.25 ? floor(q) / 999.0 : frac(q); }"""

PEAK_HELPERS = q_select() + """
float LVL(float i) { return floor(Q(i)) / 999.0; }
float PEAK(float i) { return frac(Q(i)); }"""


def shader(prefix, helpers, body):
    lines_ = (helpers.splitlines() if helpers else []) + ["shader_body", "{"] + body.strip("\n").splitlines() + ["}"]
    return "".join(f"{prefix}{i}=`{line}\n" for i, line in enumerate(lines_, 1))


def lines(prefix, code):
    return "".join(f"{prefix}{i}={line}\n" for i, line in enumerate(code.splitlines(), 1))


# Bloom + backlight, shared by every style. Backlight: a soft band of the style's colour along the centre line,
# swelling with bass - the part that makes the page around the visualizer react too. Everything fades out before the
# frame's edge, and faint haze is crushed to black (= see-through): any light stopping at the boundary, or a faint tint
# filling the frame, would show the rectangle.
# The art slot's shadow on the visualizer's own light: what passes under the cover dims toward its rim, over 0.06 of the
# height. Drawn here, not by the page - the visualizer can't darken the theme behind it (black is see-through), and pages
# should not need a WPF effect over a surface that changes every frame.
SHADOW = " * lerp(0.25, 1.0, smoothstep(0.15, 0.21, length((uv - 0.5) * float2(texsize.x * texsize.w, 1.0))))"


def comp(backlight, bloom=(0.9, 1.3), spread="0.8, 3.0", fringe=0.0, fade_x="0.10", shadow=False):
    # fringe: a lens's chromatic aberration - red pushed right, blue left, by `fringe` of the width at the frame's edge,
    # none at its centre
    # sampler_pc_main (point, clamped), not sampler_main: projectM's filtered read lands between pixels, so every frame
    # came out 2x2-blurred - a 1-pixel checkerboard read back as flat grey (0% exact pixels; point-sampled: 100%).
    sample = "float3 c = tex2D(sampler_pc_main, uv).xyz;" if not fringe else (
        f"float2 o = float2({fringe} * smoothstep(0.2, 1.0, length(uv - 0.5) * 2.0), 0.0);\n"
        "  float3 c = float3(tex2D(sampler_pc_main, uv + o).x, tex2D(sampler_pc_main, uv).y, tex2D(sampler_pc_main, uv - o).z);")
    return shader("comp_", None, f"""
  {sample}
  float3 glow = GetBlur1(uv) * {bloom[0]} + GetBlur2(uv) * {bloom[1]};
  float2 d = (uv - float2(0.5, 0.5)) * float2({spread});
  float pulse = saturate((bass_att - 0.6) * 0.7);
  float3 back = float3{backlight} * exp(-length(d) * 4.0) * (0.10 + 0.35 * pulse);
  float2 e = min(uv, 1.0 - uv);
  float fade = smoothstep(0.0, {fade_x}, e.x) * smoothstep(0.0, 0.14, e.y);
  ret = saturate((c + glow + back) * fade{SHADOW if shadow else ""} - 0.02) / 0.98;""")


# Last frame, faded and drifted away from the centre line by `drift` per 60 fps frame (up in the top half, down in the
# reflection), part-blurred so trails read as haze, not copies.
def trail(drift, fade, haze, sway=0.0):
    return f"""
  float k = 60.0 / max(fps, 15.0);
  float sgn = 1.0 - 2.0 * step(0.5, uv.y);
  float2 tuv = uv + float2(sin(uv.y * 9.0 + time * 0.7) * {sway}, {drift} * sgn) * k;
  float3 prev = lerp(tex2D(sampler_main, tuv).xyz, GetBlur1(tuv), {haze}) * pow({fade}, k);"""


# Wings: bars either side of the art slot, bass next to the art, highs towards the edges; y is the distance from the
# centre line (bars grow both ways), `down` marks the reflection half.
def wing(n, gap=0.19, end=0.44):
    # gap: where the wings start, from the centre in frame heights; end: where they stop, from the centre in widths
    return f"""
  float2 p = uv_orig;
  float gap = {gap} * texsize.y * texsize.z;
  float wing = {end} - gap;
  float fx = (abs(p.x - 0.5) - gap) / wing * {n}.0;
  float inWing = step(0.0, fx) * step(fx, {n}.0);
  float i = clamp(floor(fx), 0.0, {n - 1}.0);
  float f = frac(fx) - 0.5;
  float px = texsize.z * {n}.0 / wing;
  float y = abs(p.y - 0.5);
  float down = step(0.5, p.y);"""


STYLES = {}

# Square LEDs, 8 rows each way, peak markers and a faint unlit grid; light bloom, no trails. Drawn per pixel, so LEDs
# stay square and sharp at any size (as shapes, 1152 LEDs cost ~11 ms a frame on the CPU).
STYLES["LED Bars"] = dict(n=32, pack=pack_peaks, comp=comp("(0.2, 0.9, 0.35)", bloom=(0.5, 0.3)),
    warp=shader("warp_", PEAK_HELPERS, wing(32) + """
  float fy = y / 0.30 * 8.0;
  float s = floor(fy);
  float g = frac(fy) - 0.5;
  float colpx = wing * texsize.x / 32.0;
  float rowpx = 0.30 * texsize.y / 8.0;
  float side = 0.72 * min(colpx, rowpx);
  float hx = side * 0.5 / colpx;
  float hy = side * 0.5 / rowpx;
  float inLed = saturate((hx - abs(f)) * colpx + 0.5) * saturate((hy - abs(g)) * rowpx + 0.5) * inWing * step(fy, 8.0);
  float lit = step(s + 0.001, LVL(i) * 8.0);
  float pk = floor(PEAK(i) * 8.0 - 0.001);
  float peak = (1.0 - step(0.5, abs(s - pk))) * step(0.5, pk);
  float t = s / 7.0;
  float3 col = t < 0.6 ? float3(0.15 + 0.5 * t, 0.95, 0.25) : (t < 0.82 ? float3(1.0, 0.7, 0.08) : float3(1.0, 0.18, 0.08));
  float dome = 1.0 - 0.25 * length(float2(f / hx, g / hy));
  float a = max(max(lit, peak * 0.95), 0.06);
  ret = col * a * dome * inLed * (1.0 - 0.5 * down);"""))

# Slim bars with a cool gradient and a bright tip; haze drifting off them; bloom.
STYLES["Glow Bars"] = dict(n=32, pack=pack_pairs, comp=comp("(0.2, 0.35, 1.0)", bloom=(0.6, 0.7)),
    warp=shader("warp_", PAIR_HELPERS, trail(0.0018, 0.80, 0.6) + wing(32) + """
  float h = BAR(i) * 0.30 + 0.004;
  float inX = saturate((0.30 - abs(f)) / px + 0.5) * inWing;
  float inY = saturate((h - y) / texsize.w + 0.5);
  float t = saturate(y / 0.30);
  float3 col = lerp(float3(0.05, 0.55, 1.0), float3(0.85, 0.35, 1.0), t);
  col = lerp(col, float3(1.0, 1.0, 1.0), saturate((y - h + 0.008) / 0.008) * 0.25);
  ret = max(prev, col * inX * inY * (1.0 - 0.55 * down));"""))

# Ember bars with glowing peak caps, sparks thrown upwards off the loud ones.
SPARKS = 240
STYLES["Spark Bars"] = dict(n=32, pack=pack_peaks, comp=comp("(1.0, 0.45, 0.1)", bloom=(0.8, 0.5)),
    warp=shader("warp_", PEAK_HELPERS, trail(0.0012, 0.78, 0.5) + wing(32) + """
  float h = LVL(i) * 0.30 + 0.004;
  float pk = PEAK(i) * 0.30 + 0.004;
  float inX = saturate((0.36 - abs(f)) / px + 0.5) * inWing;
  float inY = saturate((h - y) / texsize.w + 0.5);
  float t = saturate(y / 0.30);
  float3 col = lerp(float3(0.55, 0.08, 0.02), float3(1.0, 0.6, 0.15), t);
  float cap = inX * saturate(1.0 - abs(y - pk - 0.006) / 0.003);
  ret = max(prev, (col * inX * inY + float3(1.0, 0.85, 0.55) * cap) * (1.0 - 0.55 * down));"""),
    shapes=f"""shapecode_0_enabled=1
shapecode_0_sides=8
shapecode_0_additive=1
shapecode_0_textured=0
shapecode_0_num_inst={SPARKS}
shapecode_0_rad=0.005
shapecode_0_a=1
shapecode_0_border_a=0
""" + lines("shape_0_per_frame", """dt=1/max(fps, 15); o=2000+instance*6; life=gmem[o+4];
life <= 0 ? (
  bi=floor(rand(32)); h=gmem[bi];
  rand(1) < h*h*dt*8 ? (
    sd=rand(1) < 0.5 ? -1 : 1;
    gmem[o]=0.5+sd*(reg02+(bi+0.2+rand(0.6))*reg03/32); gmem[o+1]=0.5+h*0.30;
    gmem[o+2]=(rand(1)-0.5)*0.04; gmem[o+3]=0.05+0.20*h+rand(0.08);
    gmem[o+4]=0.6+rand(0.6); gmem[o+5]=rand(1);
  ) : 0;
) : 0;
life=gmem[o+4];
life > 0 ? (
  gmem[o]+=gmem[o+2]*dt + sin(time*3+instance)*0.01*dt;
  gmem[o+1]+=gmem[o+3]*dt; gmem[o+3]*=pow(0.6, dt); gmem[o+4]=life-dt*0.7;
) : 0;
x=gmem[o]; y=gmem[o+1]; lf=max(0, gmem[o+4]);
sides=10; additive=1; rad=0.005+0.007*gmem[o+5];
r=1; g=0.55+0.4*lf; b=0.15+0.5*lf*lf; a=min(1, lf*1.5)*(gmem[o+4] > 0);
r2=r; g2=g; b2=b; a2=0; border_a=0;"""))

# A smooth spectrum line either side of the art, mirrored below; its afterglow drifts outwards and sways like aurora.
STYLES["Aurora"] = dict(n=64, pack=pack_pairs, comp=comp("(0.1, 0.8, 0.5)", bloom=(1.0, 0.9)),
    warp=shader("warp_", PAIR_HELPERS, trail(0.0030, 0.93, 0.6, sway=0.0006) + wing(63) + """
  float ends = smoothstep(-0.5, 3.0, fx) * smoothstep(63.0, 59.0, fx);
  float fc = clamp(fx, 0.0, 63.0);
  float j = floor(fc);
  float u = frac(fc);
  u = u * u * (3.0 - 2.0 * u);
  float h = lerp(BAR(j), BAR(min(j + 1.0, 63.0)), u);
  float dy = y - (h * 0.28 + 0.01);
  float core = exp(-abs(dy) / (texsize.w * 1.5));
  float halo = exp(-abs(dy) * 60.0) * 0.35;
  float fill = step(dy, 0.0) * exp(dy * 18.0) * 0.05;
  float hue = fc / 63.0 * 0.7 + time * 0.05;
  float3 col = 0.5 + 0.5 * cos(6.2832 * (hue + float3(0.0, 0.33, 0.67)));
  col = lerp(col, float3(0.2, 1.0, 0.6), 0.35);
  ret = max(prev, col * (core + halo + fill) * ends * (1.0 - 0.5 * down));"""))

# Spectrum spokes around the art, a ring that breathes with the bass; echoes expand outwards.
STYLES["Halo Ring"] = dict(n=64, pack=pack_pairs, comp=comp("(0.45, 0.4, 1.0)", bloom=(0.8, 0.35), spread="1.7, 1.7"),
    warp=shader("warp_", PAIR_HELPERS, """
  float k = 60.0 / max(fps, 15.0);
  float2 c = float2(0.5, 0.5);
  float2 tuv = c + (uv - c) * (1.0 - 0.012 * k);
  float edge = saturate(min(min(tuv.x, 1.0 - tuv.x), min(tuv.y, 1.0 - tuv.y)) * 12.0);
  float3 prev = lerp(tex2D(sampler_main, tuv).xyz, GetBlur1(tuv), 0.25) * pow(0.80, k) * edge;
  float2 p = (uv_orig - c) * float2(texsize.x * texsize.w, 1.0);
  float r = length(p);
  float t = abs(atan2(p.x, p.y)) / 3.14159;
  float fx = t * 64.0;
  float i = clamp(floor(fx), 0.0, 63.0);
  float f = frac(fx) - 0.5;
  float pulse = saturate((bass_att - 0.6) * 0.7);
  float R0 = 0.165 + 0.02 * pulse;
  float L = BAR(i) * 0.16 + 0.004;
  float pxa = texsize.w / (max(r, 0.01) * 3.14159 / 64.0);
  float inA = saturate((0.28 - abs(f)) / pxa + 0.5);
  float inR = saturate((r - R0) / texsize.w + 0.5) * saturate((R0 + L - r) / texsize.w + 0.5);
  float ring = exp(-abs(r - R0 + 0.008) / (texsize.w * 1.2)) * 0.7;
  float hue = t * 0.5 + time * 0.04;
  float3 col = 0.55 + 0.45 * cos(6.2832 * (hue + float3(0.0, 0.33, 0.67)));
  ret = max(prev, col * inA * inR + float3(0.6, 0.8, 1.0) * ring * (0.5 + pulse));"""))


# Fountain: TEKKEN 8's jukebox visualiser, on the particle model read out of the game's own GPU shader (Lucid's
# docs/tekken8-visualizer-spec.md). One line of 96 columns, low to high across the frame - lows at the far left, highs at
# the far right, split around the art slot. Each column is a cloud of small glowing dots:
#   - every dot sits at a FIXED random fraction k = u*p of its column's level (two signed uniforms, |k| <= 0.5): no
#     launch, no flight. The product of two uniforms peaks at 0, so a column is dense on the line and thins to its tips,
#     mirrored above and below;
#   - a dot's level eases from the level it was born with to the live level along the game's curve B over its life, so
#     older dots follow a hit at once and young ones lag - the "lava-lamp pulse";
#   - lifetimes are 1-5 s (weighted as the live population is: L = sqrt(1 + 24h)), opacity follows the game's curve A
#     (peak 0.6 at mid-life), colour is a random blend between magenta (6,1,2) and violet (6.5,4,10) per dot;
#   - a dot whose level is changing fast is stretched vertically, up to 5x: hits flash as streaks, steady tones are
#     round dots;
#   - no horizontal jitter: dots sit on the column's axis, 0.5-0.8 of the column spacing across.
# The game's softness is its lens: the line is far behind the focus, so every dot is a big bokeh disc, under 10x bloom
# tinted violet, with a red/blue fringe. Here a defocus mix in the comp shader (FOUNTAIN_DEFOCUS) blends the sharp
# frame toward projectM's blur.
#
# Levels are the game's kind, not Lucid's: LINEAR magnitude, which is what makes the game dynamic - a hit towers over
# what is around it. Each column is scaled to its own recent peak (decaying, half-life 3 s; never below 0.03 of the
# loudest column, so near-silent columns stay low instead of flickering at full height), on a 0.7 curve, point-sampled
# 50 Hz .. 16 kHz on a pow 1.5 curve and tilted up 4x toward the top (music is quieter there; without the tilt the high
# half of the line lay flat). Then the game's per-tick smoothing (rise 80%, fall 50%, a peak held two ticks) and an ease
# of 0.10 s up / 0.30 s down. Why not Lucid's decibels from 200 Hz against a per-column average: on identical kicks over
# changing music the kick's body sat below 200 Hz (kick columns rose 3%), and the average - raised by the kicks - made
# the same kick land anywhere from small to full (+-47%). Peak-scaled linear: kicks 3x higher, +-18%, none missed.
#
# Dots are stateless - a pure function of the clock, a per-dot hash and the level history - so the warp shader draws
# them per pixel. With no horizontal jitter and small dots, a pixel only ever meets its own column's dots.
FOUNTAIN_COLS, FOUNTAIN_DOTS, FOUNTAIN_REACH = 96, 30, 18.0  # dots per column (the game: 10/s x mean life 3 s);
                                                             # a full level at |k| = 0.5 reaches 18 column widths
FOUNTAIN_HIST = 128      # history rows, one per 1/60 s: a dot's birth level is still read until ~0.4 of a 5 s life
FOUNTAIN_DEFOCUS = 1.0   # 0 = sharp dots; 1 = the game's look, each dot carried mostly by projectM's blur (bokeh)


def fountain_engine(n):
    # gmem[0..n) eased level 0..1 (packed for the shader), [200..) magnitude, [400..) each column's recent peak, [500..)
    # the game's smoothed value; reg00 the loudest column, decaying
    return f"""dt=1/max(fps, 15); n={n}; b0=1.07; b1=340; loudest=0; bi=0;
loop(n,
  c=b0+pow(bi/n, 1.5)*(b1-b0); i=floor(c); t=c-i;
  v=(gmem[1000+i]*(1-t) + gmem[1001+i]*t)*(1+3*bi/n);
  gmem[200+bi]=v;
  loudest=max(loudest, v);
  bi+=1;
);
reg00=max(max(loudest, reg00*pow(0.5, dt/4)), 0.02);
pd=pow(0.5, dt/3); up=1-pow(0.2, dt*60); dn=1-pow(0.5, dt*60); ea=1-exp(-dt/0.10); er=1-exp(-dt/0.30);
total=0; bi=0;
loop(n,
  v=gmem[200+bi];
  pk=max(max(gmem[400+bi]*pd, v), 0.03*reg00); gmem[400+bi]=pk;
  tg=max(pow(min(1, max(0, (v/pk-0.08)/0.92)), 0.7), 0.0034);
  pv=gmem[500+bi]; nx=pv+(tg-pv)*(tg > pv ? up : dn); gmem[500+bi]=nx;
  o=max(nx, pv);
  ez=gmem[bi]; ez+=(o-ez)*(o > ez ? ea : er); gmem[bi]=ez;
  total+=ez;
  bi+=1;
);
reg01=reg01+(total/n-reg01)*min(1, dt*6);
reg02=0.19/aspecty; reg03=0.44-reg02;"""


def pack_triples(n):
    # q(k+1) = levels 3k, 3k+1, 3k+2 at 8 bits each - an integer under 2^24, which a float holds exactly
    return "\n".join(f"q{k + 1}=floor(gmem[{3 * k}]*255)*65536+floor(gmem[{3 * k + 1}]*255)*256+floor(gmem[{3 * k + 2}]*255);"
                     for k in range(n // 3))


TRIPLE_HELPERS = q_select() + """
float LV(float g) { float k = floor(g / 3.0); float q = Q(k); float r = g - 3.0 * k; float a = floor(q / 65536.0); float m = q - a * 65536.0; float b = floor(m / 256.0); float c = m - b * 256.0; return (a * (1.0 - step(0.5, r)) + b * (step(0.5, r) - step(1.5, r)) + c * step(1.5, r)) / 255.0; }
float2 H2(float c, float s) { return frac(sin(float2(dot(float2(c, s), float2(12.9898, 78.233)), dot(float2(c, s), float2(39.3468, 11.135)))) * 43758.5453); }
float3 ACES(float3 x) { return saturate(x * (2.51 * x + 0.03) / (x * (2.43 * x + 0.59) + 0.14)); }
float CurveB(float x) { float s = saturate(x / 0.205); return x < 0.205 ? 0.745 * s * s * (3.0 - 2.0 * s) : 1.0 - 0.255 * exp(-5.3 * (x - 0.205)); }
float CurveBd(float x) { float s = saturate(x / 0.205); return x < 0.205 ? 0.745 * 6.0 * s * (1.0 - s) / 0.205 : 0.255 * 5.3 * exp(-5.3 * (x - 0.205)); }
float CurveA(float x) { float s = saturate(x / 0.49); return x < 0.49 ? 0.6 * s * s * (3.0 - 2.0 * s) : 0.6 * (1.0 - x) / 0.51; }
float Hist(float g, float row) { return tex2D(sampler_pc_main, (float2(g, row) + 0.5) * texsize.zw).x; }"""

# The data corner of the main texture: the warp shader's output is the next frame's sampler_main, so pixels written
# there persist (MilkDrop has no data textures). Columns 0..95 by row: rows 0..HIST-1 the level history (newest in row 0,
# shifted one row every 1/60 s), row HIST the mean level (the scene light), row HIST+1 each column's loudest in the
# history (to cull pixels no dot can reach). The comp shader never shows the corner, nor lets it bloom.
DATA_W, DATA_H = FOUNTAIN_COLS, FOUNTAIN_HIST + 4   # + 2 rows the Lessons variants use


def fountain_comp():
    return shader("comp_", None, f"""
  float2 o = float2(0.0015 * smoothstep(0.2, 1.0, length(uv - 0.5) * 2.0), 0.0);
  float3 c = float3(tex2D(sampler_pc_main, uv + o).x, tex2D(sampler_pc_main, uv).y, tex2D(sampler_pc_main, uv - o).z);
  // The game's lens: the line is far behind the focus, so each dot is a soft bokeh disc - carried by the blur, with a
  // little of the sharp dot left in its core.
  float3 base = lerp(c, GetBlur1(uv) * 1.2 + GetBlur2(uv) * 0.6 + c * 0.25, {FOUNTAIN_DEFOCUS});
  // The game's bloom: everything blooms (threshold -1), wide, tinted violet (0.80, 0.27, 0.96).
  float3 glow = GetBlur2(uv) * 0.9 + GetBlur3(uv) * 1.3 * float3(0.80, 0.27, 0.96);
  float2 P = uv * texsize.xy;
  float inData = step(P.x, {DATA_W + 1}.0) * step(P.y, {DATA_H + 1}.0);
  base *= 1.0 - inData;
  glow *= 1.0 - (1.0 - smoothstep({DATA_W + 1}.0, 420.0, P.x)) * (1.0 - smoothstep({DATA_H + 1}.0, 420.0, P.y));
  // The game's level light (magenta-red, the sum of every column): a soft glow behind the line.
  float level = tex2D(sampler_pc_main, float2(0.5, {FOUNTAIN_HIST}.5) * texsize.zw).x;
  float2 d = (uv - float2(0.5, 0.5)) * float2(0.8, 3.0);
  float3 back = float3(1.0, 0.112, 0.248) * exp(-length(d) * 4.0) * (0.06 + 0.4 * saturate(level * 2.5));
  float2 e = min(uv, 1.0 - uv);
  float fade = smoothstep(0.0, 0.05, e.x) * smoothstep(0.0, 0.14, e.y);
  ret = saturate((base + glow + back) * fade{SHADOW} - 0.02) / 0.98;""")


HALF = FOUNTAIN_COLS // 2
FOUNTAIN_WARP = f"""
  float2 P = uv_orig * texsize.xy;
  if (P.x < {DATA_W}.0 && P.y < {DATA_H}.0)
  {{
    float gx = floor(P.x);
    float r = floor(P.y);
    float shift = step(0.5, floor(time * 60.0) - floor((time - 1.0 / max(fps, 15.0)) * 60.0));
    float v = 0.0;
    if (r < 0.5) v = LV(gx);
    else if (r < {FOUNTAIN_HIST}.0) v = Hist(gx, r - shift);
    else if (r < {FOUNTAIN_HIST + 1}.0)
    {{
      for (float j = 0.0; j < {FOUNTAIN_COLS}.0; j += 1.0) v += LV(j);
      v /= {FOUNTAIN_COLS}.0;
    }}
    else
    {{
      for (float j = 0.0; j < {FOUNTAIN_HIST}.0; j += 1.0) v = max(v, Hist(gx, j));
    }}
    ret = float3(v, 0.0, 0.0);
  }}
  else
  {{
    float colpx = wing * texsize.x / {HALF}.0;
    float zy = (0.5 - p.y) * texsize.y / colpx;       // signed height above the line, in column widths
    float dx = fx - i - 0.5;                          // from the column's axis
    float g = step(p.x, 0.5) > 0.5 ? {HALF - 1}.0 - i : {HALF}.0 + i;
    float3 acc = float3(0.0, 0.0, 0.0);
    if (inWing > 0.5 && abs(dx) < 0.42)
    {{
      float live = LV(g);
      float prev = Hist(g, 1.0);
      if (abs(zy) < max(live, Hist(g, {FOUNTAIN_HIST + 1}.0)) * {FOUNTAIN_REACH} + 2.0)
      for (float s = 0.0; s < {FOUNTAIN_DOTS}.0; s += 1.0)
      {{
        float2 h = H2(g, s);
        float2 u = H2(g + 97.0, s + 13.0);
        float life = sqrt(1.0 + 24.0 * h.x);          // 1-5 s, weighted as the game's live population is
        float na = frac(time / life + h.y);           // normalized age
        float born = Hist(g, min(floor(na * life * 60.0), {FOUNTAIN_HIST - 1}.0));
        float b = CurveB(na);
        float level = lerp(born, live, b);            // the game's PolarisTmp: birth level -> live level
        float k = (1.0 - 2.0 * u.x) * (u.y - 0.5);    // fixed fraction of the level, |k| <= 0.5, dense near 0
        float z = level * k * {2 * FOUNTAIN_REACH};
        // Stretched vertically by how fast its level is changing (the game: up to 5x).
        float dl = abs(b * (live - prev) + (live - born) * CurveBd(na) / (life * 60.0));
        float stretch = 1.0 + 4.0 * saturate(dl * 1.6);
        // 0.5-0.8 of the column spacing across. (Not "rad": MilkDrop shaders already have rad and ang.)
        float size = 0.25 + 0.15 * frac(h.x * 7.31 + u.x * 3.17);
        float2 d = float2(dx, (zy - z) / stretch) / size;
        float disc = 1.0 - smoothstep(0.0, 1.0, length(d));
        float3 col = lerp(float3(6.0, 1.0, 2.0), float3(6.5, 4.0, 10.0), frac(h.y * 5.17 + u.y * 2.31)) * 0.12;
        acc += col * disc * disc * CurveA(na) * 6.0;
      }}
    }}
    // The game's intro: the line comes on over half a second, not at once.
    acc *= smoothstep(0.1, 0.5, time);
    // Exposure 0.6: at 1 the white-hot cores and bloom were blinding.
    ret = ACES(acc * 0.6);
  }}"""

STYLES["Fountain"] = dict(n=FOUNTAIN_COLS, pack=pack_triples, engine=fountain_engine, comp=fountain_comp(),
    # The wings start under the art slot's rim (0.11 of the height out, the slot's edge is 0.15), so the line runs on
    # behind the cover, and reach nearer the frame's sides: bigger columns, and the cover sits in front of the fountain.
    warp=shader("warp_", TRIPLE_HELPERS, wing(HALF, gap=0.11, end=0.46) + FOUNTAIN_WARP))


# ---- Fountain "Lessons" (test variants, not shipped) ---------------------------------------------------------------
# The game's particles and look, with what testing taught:
#   - exact history steps: the history moves by however many 1/60 s steps really passed since the last frame (the step
#     index is kept in the data corner). Shifting once per frame on a clock boundary skipped and doubled steps whenever
#     frames didn't land on them, nudging young dots' birth levels;
#   - smoothed streaks: a dot's stretch comes from a per-column envelope of the level's speed of change that relaxes over
#     ~0.12 s. Measured against the last history step, the stretch flicked on and off between frames on a kick;
#   - a faster lock (optional): the level eases in over 0.035 s, not 0.10 s, so a kick lands within the spec's 3-5
#     frames; it still eases out over 0.28 s.
_LESSONS_DATA_OLD = f"""    float shift = step(0.5, floor(time * 60.0) - floor((time - 1.0 / max(fps, 15.0)) * 60.0));
    float v = 0.0;
    if (r < 0.5) v = LV(gx);
    else if (r < {FOUNTAIN_HIST}.0) v = Hist(gx, r - shift);
    else if (r < {FOUNTAIN_HIST + 1}.0)
    {{
      for (float j = 0.0; j < {FOUNTAIN_COLS}.0; j += 1.0) v += LV(j);
      v /= {FOUNTAIN_COLS}.0;
    }}
    else
    {{
      for (float j = 0.0; j < {FOUNTAIN_HIST}.0; j += 1.0) v = max(v, Hist(gx, j));
    }}
    ret = float3(v, 0.0, 0.0);"""
_LESSONS_DATA_NEW = f"""    // How many 1/60 s steps really passed since the last frame, from the step index kept here last frame.
    float tick = fmod(floor(time * 60.0), 256.0);
    float last = floor(tex2D(sampler_pc_main, float2(0.5, {FOUNTAIN_HIST + 3}.5) * texsize.zw).x * 255.0 + 0.5);
    float n = min(fmod(tick - last + 256.0, 256.0), 4.0);
    float3 o = float3(0.0, 0.0, 0.0);
    if (r < {FOUNTAIN_HIST}.0) o.x = r < max(n, 1.0) ? LV(gx) : Hist(gx, r - n);
    else if (r < {FOUNTAIN_HIST + 1}.0)
    {{
      for (float j = 0.0; j < {FOUNTAIN_COLS}.0; j += 1.0) o.x += LV(j);
      o.x /= {FOUNTAIN_COLS}.0;
    }}
    else if (r < {FOUNTAIN_HIST + 2}.0)
    {{
      for (float j = 0.0; j < {FOUNTAIN_HIST}.0; j += 1.0) o.x = max(o.x, Hist(gx, j));
    }}
    else if (r < {FOUNTAIN_HIST + 3}.0)
    {{
      // The column's last level, and how fast it has been changing: a peak that relaxes over ~0.12 s.
      float live = LV(gx);
      float2 was = tex2D(sampler_pc_main, (float2(gx, r) + 0.5) * texsize.zw).xy;
      float fr = max(fps, 15.0);
      float rate = abs(live - was.x) * fr / 60.0;
      o = float3(live, max(saturate(rate * 3.0), was.y * exp(-1.0 / (fr * 0.12))), 0.0);
    }}
    else o.x = gx < 0.5 ? tick / 255.0 : 0.0;
    ret = o;"""
_LESSONS_PAIRS = [
    (_LESSONS_DATA_OLD, _LESSONS_DATA_NEW),
    ("      float prev = Hist(g, 1.0);\n",
     f"      float env = tex2D(sampler_pc_main, (float2(g, {FOUNTAIN_HIST + 2}.0) + 0.5) * texsize.zw).y / 3.0;\n"),
    ("        float dl = abs(b * (live - prev) + (live - born) * CurveBd(na) / (life * 60.0));\n",
     "        float dl = b * env + abs(live - born) * CurveBd(na) / (life * 60.0);\n"),
]


# Calm core: the stretch and the kick are carried by the outer dots, not the whole column. A dot's stretch scales with
# how far from the line it sits (|k|), as the earlier fountain's did with a bead's own speed - so the white core stays
# round and steady - and on a kick the outer dots briefly reach further out (the column's change envelope, squared
# |k|) and settle back as it fades. The level light pulses a third as much: it lit the whole frame on every kick.
_CALM_PAIRS = [
    ("        float dl = b * env + abs(live - born) * CurveBd(na) / (life * 60.0);\n",
     "        float dl = (b * env + abs(live - born) * CurveBd(na) / (life * 60.0)) * abs(k) * 2.8;\n"),
    (f"        float z = level * k * {2 * FOUNTAIN_REACH};\n",
     f"        float z = level * k * {2 * FOUNTAIN_REACH} * (1.0 + 0.6 * saturate(env * 3.0) * k * k * 4.0);\n"),
]


def fountain_lessons(ease_in, ease_out, calm_core=False):
    w = FOUNTAIN_WARP
    for old, new in _LESSONS_PAIRS + (_CALM_PAIRS if calm_core else []):
        assert w.count(old) == 1, old[:60]
        w = w.replace(old, new)
    comp_text = STYLES["Fountain"]["comp"]
    if calm_core:
        old = "(0.06 + 0.4 * saturate(level * 2.5))"
        assert comp_text.count(old) == 1
        comp_text = comp_text.replace(old, "(0.06 + 0.13 * saturate(level * 2.5))")

    def engine(n):
        e = fountain_engine(n)
        old = "ea=1-exp(-dt/0.10); er=1-exp(-dt/0.30);"
        assert e.count(old) == 1
        return e.replace(old, f"ea=1-exp(-dt/{ease_in}); er=1-exp(-dt/{ease_out});")
    return dict(STYLES["Fountain"], engine=engine, comp=comp_text,
                warp=shader("warp_", TRIPLE_HELPERS, wing(HALF, gap=0.11, end=0.46) + w))


EXPERIMENTS = {
    "Fountain Lessons": fountain_lessons(0.035, 0.28),
    "Fountain Lessons (default ease)": fountain_lessons(0.10, 0.30),
    "Fountain Calm Core": fountain_lessons(0.10, 0.30, calm_core=True),
    "Fountain Calm Core (fast lock)": fountain_lessons(0.035, 0.28, calm_core=True),
}

# ---- Fountain "Tunable" (test variant, not shipped) ----------------------------------------------------------------
# The spec look with 07/08's beat: every dot follows the live level (no birth lag - 07's beads did), stretch grows with
# a dot's distance from the line so the core stays round while the tips streak, kicks drive the stretch through the
# smoothed kick envelope, a slight outward fling of the tips on a kick, and a calm scene light. Every knob is an
# @TOKEN@ with a default in TUNABLE, written as a .tune sidecar: the test build fills them live (F6/F7, [ ]).
TUNABLE = [
    # label, token, default, step, min, max
    ("Follow the live level (1 = like 07, 0 = the game's birth lag)", "LIVE", 1.0, 0.1, 0.0, 1.0),
    ("Stretch strength", "STRETCH_GAIN", 1.4, 0.2, 0.0, 6.0),
    ("Stretch max (x)", "STRETCH_MAX", 4.0, 0.5, 0.0, 9.0),
    ("Stretch tips-only (higher = only the outermost dots)", "STRETCH_TIP", 1.0, 0.25, 0.0, 4.0),
    ("Kick relax time (s)", "KICK_DECAY", 0.12, 0.02, 0.02, 0.6),
    ("Tip fling on a kick", "FLING", 0.3, 0.1, 0.0, 2.0),
    ("Core density (higher = more dots hug the line)", "CORE", 1.0, 0.1, 0.4, 3.0),
    ("Core hold (1 = the core ignores kicks)", "CORE_HOLD", 0.85, 0.05, 0.0, 1.0),
    ("Core size (how far from the line counts as core)", "CORE_SIZE", 0.35, 0.05, 0.05, 1.0),
    ("Core smoothing (s)", "CORE_SLOW", 1.0, 0.1, 0.1, 4.0),
    ("Reach (column widths)", "REACH", 18.0, 1.0, 4.0, 30.0),
    ("Life spread (longest life = sqrt(1 + this) s)", "LIFE_SPREAD", 24.0, 2.0, 0.0, 60.0),
    ("Dot brightness", "BRIGHT", 6.0, 0.5, 1.0, 15.0),
    ("Exposure", "EXPOSURE", 0.6, 0.05, 0.2, 1.5),
    ("Defocus (0 = sharp dots, 1 = the game's bokeh)", "DEFOCUS", 1.0, 0.05, 0.0, 1.0),
    ("Bloom, wide", "BLOOM_WIDE", 1.3, 0.1, 0.0, 3.0),
    ("Scene glow pulse", "GLOW_PULSE", 0.13, 0.03, 0.0, 1.0),
    ("Level ease in (s)", "EASE_IN", 0.10, 0.01, 0.01, 0.5),
    ("Level ease out (s)", "EASE_OUT", 0.30, 0.02, 0.04, 1.2),
]


def fountain_tunable():
    w = FOUNTAIN_WARP
    for old, new in _LESSONS_PAIRS + _CALM_PAIRS:
        assert w.count(old) == 1, old[:60]
        w = w.replace(old, new)
    warp_tokens = [
        ("        float dl = (b * env + abs(live - born) * CurveBd(na) / (life * 60.0)) * abs(k) * 2.8;\n",
         "        float dl = (b * env + abs(live - born) * CurveBd(na) / (life * 60.0)) * pow(abs(k) * 2.0, @STRETCH_TIP@) * @STRETCH_GAIN@;\n"),
        ("float stretch = 1.0 + 4.0 * saturate(dl * 1.6);", "float stretch = 1.0 + @STRETCH_MAX@ * saturate(dl * 1.6);"),
        (f"        float z = level * k * {2 * FOUNTAIN_REACH} * (1.0 + 0.6 * saturate(env * 3.0) * k * k * 4.0);\n",
         "        float z = level * k * 2.0 * @REACH@ * (1.0 + @FLING@ * saturate(env * 3.0) * k * k * 4.0);\n"),
        ("        float b = CurveB(na);\n", "        float b = lerp(CurveB(na), 1.0, @LIVE@);\n"),
        ("float k = (1.0 - 2.0 * u.x) * (u.y - 0.5);",
         "float k = (1.0 - 2.0 * u.x) * (u.y - 0.5); k = sign(k) * pow(abs(k) * 2.0, @CORE@) * 0.5;\n"
         "        // Core hold: dots near the line follow the column's slow level, so the glowing core keeps its shape\n"
         "        // through a kick and the outer dots carry it.\n"
         "        level = lerp(level, slowL, @CORE_HOLD@ * (1.0 - smoothstep(0.0, @CORE_SIZE@, abs(k) * 2.0)));"),
        (f"      float env = tex2D(sampler_pc_main, (float2(g, {FOUNTAIN_HIST + 2}.0) + 0.5) * texsize.zw).y / 3.0;\n",
         f"      float3 kickRow = tex2D(sampler_pc_main, (float2(g, {FOUNTAIN_HIST + 2}.0) + 0.5) * texsize.zw).xyz;\n"
         "      float env = kickRow.y / 3.0;\n"
         "      float slowL = kickRow.z;\n"),
        ("      float2 was = tex2D(sampler_pc_main, (float2(gx, r) + 0.5) * texsize.zw).xy;\n",
         "      float3 was = tex2D(sampler_pc_main, (float2(gx, r) + 0.5) * texsize.zw).xyz;\n"),
        ("sqrt(1.0 + 24.0 * h.x)", "sqrt(1.0 + @LIFE_SPREAD@ * h.x)"),
        ("      o = float3(live, max(saturate(rate * 3.0), was.y * exp(-1.0 / (fr * 0.12))), 0.0);\n",
         "      // z: the column's slow level, for the core hold\n"
         "      o = float3(live, max(saturate(rate * 3.0), was.y * exp(-1.0 / (fr * @KICK_DECAY@))),\n"
         "                 lerp(was.z, live, 1.0 - exp(-1.0 / (fr * @CORE_SLOW@))));\n"),
        ("CurveA(na) * 6.0;", "CurveA(na) * @BRIGHT@;"),
        ("ret = ACES(acc * 0.6);", "ret = ACES(acc * @EXPOSURE@);"),
        (f"* {FOUNTAIN_REACH} + 2.0)", "* @REACH@ * 1.6 + 6.0)"),
    ]
    for old, new in warp_tokens:
        assert w.count(old) == 1, old[:60]
        w = w.replace(old, new)
    c = STYLES["Fountain"]["comp"]
    for old, new in [("(0.06 + 0.4 * saturate(level * 2.5))", "(0.06 + @GLOW_PULSE@ * saturate(level * 2.5))"),
                     ("GetBlur2(uv) * 0.6 + c * 0.25, 1.0);", "GetBlur2(uv) * 0.6 + c * 0.25, @DEFOCUS@);"),
                     ("GetBlur3(uv) * 1.3", "GetBlur3(uv) * @BLOOM_WIDE@")]:
        assert c.count(old) == 1, old
        c = c.replace(old, new)

    def engine(n):
        e = fountain_engine(n)
        old = "ea=1-exp(-dt/0.10); er=1-exp(-dt/0.30);"
        assert e.count(old) == 1
        return e.replace(old, "ea=1-exp(-dt/@EASE_IN@); er=1-exp(-dt/@EASE_OUT@);")
    return dict(STYLES["Fountain"], engine=engine, comp=c,
                warp=shader("warp_", TRIPLE_HELPERS, wing(HALF, gap=0.11, end=0.46) + w))


EXPERIMENTS["Fountain Tunable"] = fountain_tunable()
TUNE_FILES = {"Fountain Tunable": TUNABLE}

def write(name, s):
    n = s["n"]
    text = (HEADER + ANALYSIS_WAVE + s.get("shapes", "")
            + lines("per_frame_", s.get("engine", engine)(n) + "\n" + s["pack"](n))
            + s["warp"] + s["comp"])
    with open(os.path.join(OUT, f"UPS - {name}.milk"), "w", newline="\r\n") as f:
        f.write(text)
    print("wrote", name)


HERE = os.path.dirname(os.path.abspath(__file__))
OUT = sys.argv[1] if len(sys.argv) > 1 else HERE  # optional output folder, for trying styles out
if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    for old in os.listdir(OUT):
        if old.startswith("UPS - ") and old.endswith(".milk"):
            os.remove(os.path.join(OUT, old))
    for name, s in STYLES.items():
        write(name, s)
    for name, s in EXPERIMENTS.items():
        write(name, s)
    for name, params in TUNE_FILES.items():
        with open(os.path.join(OUT, f"UPS - {name}.tune"), "w", newline="\r\n") as f:
            for label, token, default, step, lo, hi in params:
                f.write(f"{label}|{token}|{default}|{step}|{lo}|{hi}\n")
