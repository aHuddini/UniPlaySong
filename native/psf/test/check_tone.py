# Passes when tone.wav is the square wave tone.psf asks for: 28-sample period (1575 Hz at
# 44100), near full scale, identical channels, onset within the first few samples.
import struct, wave, sys
w = wave.open(sys.argv[1] if len(sys.argv) > 1 else 'tone.wav'); n = w.getnframes(); d = w.readframes(n); w.close()
s = struct.unpack('<%dh' % (n * 2), d); L = s[0::2]; R = s[1::2]
assert L == R, 'channels differ'
assert n >= 44100, 'too short'
seg = L[44100:44100 + 28 * 100]
crossings = sum(1 for i in range(1, len(seg)) if (seg[i-1] < 0) != (seg[i] < 0))
assert crossings == 200, 'expected 200 zero crossings in 100 periods, got %d' % crossings
assert 27000 < max(seg) < 29000 and -29000 < min(seg) < -27000, 'amplitude off: %d..%d' % (min(seg), max(seg))
assert next(i for i, v in enumerate(L) if v) < 8, 'late onset'
print('ok: 1575 Hz square, peak %d, %d frames' % (max(seg), n))
