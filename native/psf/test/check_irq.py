# Passes when tone_irq.wav alternates between 1575 Hz and 787.5 Hz every 320 ms.
import struct, wave, sys
w = wave.open(sys.argv[1] if len(sys.argv) > 1 else 'tone_irq.wav'); n = w.getnframes(); d = w.readframes(n); w.close()
L = struct.unpack('<%dh' % (n * 2), d)[0::2]
seg = int(0.320 * 44100)
freqs = []
for k in range(1, n // seg):                       # skip segment 0: startup lands in it
    s = L[k * seg + 400 : (k + 1) * seg - 400]       # trim the edges where the flip lands
    crossings = sum(1 for i in range(1, len(s)) if (s[i-1] < 0) != (s[i] < 0))
    freqs.append(crossings / 2 / (len(s) / 44100.0))
print('segment frequencies:', ' '.join('%.0f' % f for f in freqs))
hi = [abs(f - 1575) < 40 for f in freqs]
lo = [abs(f - 787.5) < 40 for f in freqs]
assert all(h or l for h, l in zip(hi, lo)), 'a segment is neither pitch'
assert any(hi) and any(lo), 'the pitch never flipped: the interrupt path is dead'
assert all(hi[i] != hi[i+1] for i in range(len(hi) - 1)), 'pitch did not alternate every 320 ms'
print('ok: pitch alternates 1575/787.5 Hz every 320 ms across %d segments' % len(freqs))
