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
def comp(backlight, bloom=(0.9, 1.3), spread="0.8, 3.0"):
    return shader("comp_", None, f"""
  float3 c = tex2D(sampler_main, uv).xyz;
  float3 glow = GetBlur1(uv) * {bloom[0]} + GetBlur2(uv) * {bloom[1]};
  float2 d = (uv - float2(0.5, 0.5)) * float2({spread});
  float pulse = saturate((bass_att - 0.6) * 0.7);
  float3 back = float3{backlight} * exp(-length(d) * 4.0) * (0.10 + 0.35 * pulse);
  float2 e = min(uv, 1.0 - uv);
  float fade = smoothstep(0.0, 0.10, e.x) * smoothstep(0.0, 0.14, e.y);
  ret = saturate((c + glow + back) * fade - 0.02) / 0.98;""")


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
def wing(n):
    return f"""
  float2 p = uv_orig;
  float gap = 0.19 * texsize.y * texsize.z;
  float wing = 0.44 - gap;
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


def write(name, s):
    n = s["n"]
    text = (HEADER + ANALYSIS_WAVE + s.get("shapes", "")
            + lines("per_frame_", engine(n) + "\n" + s["pack"](n))
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
