# Tiny MIPS assembler + PS-EXE/PSF packer for synthetic test programs.
import struct, zlib

BASE = 0x80010000
R = {'zero': 0, 'at': 1, 'v0': 2, 'v1': 3, 'a0': 4, 'a1': 5, 'a2': 6, 'a3': 7,
     't0': 8, 't1': 9, 't2': 10, 't3': 11, 't4': 12, 't5': 13, 't6': 14, 't7': 15,
     's0': 16, 't8': 24, 't9': 25, 'sp': 29, 'ra': 31}

class Asm:
    def __init__(self):
        self.code = []
        self.labels = {}
        self.fixups = []            # (index, kind, label)
    def emit(self, w):             self.code.append(w & 0xffffffff)
    def here(self):                return BASE + len(self.code) * 4
    def label(self, name):         self.labels[name] = self.here()
    # I-type
    def _i(self, op, rs, rt, imm): self.emit(op << 26 | R[rs] << 21 | R[rt] << 16 | (imm & 0xffff))
    def lui(self, rt, imm):        self.emit(0x0f << 26 | R[rt] << 16 | (imm & 0xffff))
    def ori(self, rt, rs, imm):    self._i(0x0d, rs, rt, imm)
    def xori(self, rt, rs, imm):   self._i(0x0e, rs, rt, imm)
    def andi(self, rt, rs, imm):   self._i(0x0c, rs, rt, imm)
    def addiu(self, rt, rs, imm):  self._i(0x09, rs, rt, imm)
    def sh(self, rt, off, base):   self._i(0x29, base, rt, off)
    def sw(self, rt, off, base):   self._i(0x2b, base, rt, off)
    def lhu(self, rt, off, base):  self._i(0x25, base, rt, off)
    def lw(self, rt, off, base):   self._i(0x23, base, rt, off)
    def bne(self, rs, rt, label):  self.fixups.append((len(self.code), 'br', label)); self._i(0x05, rs, rt, 0)
    def beq(self, rs, rt, label):  self.fixups.append((len(self.code), 'br', label)); self._i(0x04, rs, rt, 0)
    # R-type
    def addu(self, rd, rs, rt):    self.emit(R[rs] << 21 | R[rt] << 16 | R[rd] << 11 | 0x21)
    def jr(self, rs):              self.emit(R[rs] << 21 | 0x08)
    def jalr(self, rs):            self.emit(R[rs] << 21 | 31 << 11 | 0x09)
    def nop(self):                 self.emit(0)
    def mfc0(self, rt, rd):        self.emit(0x10 << 26 | R[rt] << 16 | rd << 11)
    def mtc0(self, rt, rd):        self.emit(0x10 << 26 | 4 << 21 | R[rt] << 16 | rd << 11)
    def j(self, label):            self.fixups.append((len(self.code), 'j', label)); self.emit(0x02 << 26)
    # pseudo
    def li(self, rt, v):
        if 0 <= v <= 0xffff: self.ori(rt, 'zero', v)
        else: self.lui(rt, v >> 16); self.ori(rt, rt, v & 0xffff)
    def la(self, rt, label):
        self.fixups.append((len(self.code), 'la', label)); self.lui(rt, 0); self.ori(rt, rt, 0)
    def word(self, w):             self.emit(w)
    def data(self, b):
        b += b'\0' * (-len(b) % 4)
        for i in range(0, len(b), 4): self.emit(struct.unpack('<I', b[i:i+4])[0])

    def resolve(self):
        for i, kind, name in self.fixups:
            a = self.labels[name]
            if kind == 'br':   self.code[i] |= ((a - (BASE + (i + 1) * 4)) >> 2) & 0xffff
            elif kind == 'j':  self.code[i] |= (a >> 2) & 0x3ffffff
            elif kind == 'la': self.code[i] |= a >> 16; self.code[i + 1] |= a & 0xffff
        return b''.join(struct.pack('<I', w) for w in self.code)

def psf(text, tags, region=b'North America'):
    text += b'\0' * (-len(text) % 2048)
    hdr = bytearray(2048)
    hdr[0:8] = b'PS-X EXE'
    struct.pack_into('<I', hdr, 0x10, BASE)
    struct.pack_into('<I', hdr, 0x18, BASE)
    struct.pack_into('<I', hdr, 0x1c, len(text))
    struct.pack_into('<I', hdr, 0x30, 0x801ffff0)
    marker = b'Sony Computer Entertainment Inc. for ' + region + b' area'
    hdr[0x4c:0x4c + len(marker)] = marker
    z = zlib.compress(bytes(hdr) + text, 9)
    out = b'PSF\x01' + struct.pack('<III', 0, len(z), zlib.crc32(z)) + z
    out += b'[TAG]' + ''.join('%s=%s\n' % kv for kv in tags.items()).encode()
    return out

# Two ADPCM blocks of a 28-sample square wave, looped: 1575 Hz at pitch 0x1000.
def square_blocks():
    block = lambda flags: bytes([0x00, flags]) + b'\x77' * 7 + b'\x99' * 7
    return block(0x04) + block(0x03)

# Uploads the bytes at label `sample` to SPU RAM 0x1000 by DMA and keys voice 0 on them at
# pitch 0x1000, full volume, ADSR held at maximum.
def spu_setup(a, nbytes):
    a.lui('t0', 0x1f80)
    a.sh('zero', 0x1daa, 't0')
    a.li('t1', 0x3fff); a.sh('t1', 0x1d80, 't0'); a.sh('t1', 0x1d82, 't0')
    a.li('t1', 4);      a.sh('t1', 0x1dac, 't0')
    a.li('t1', 0x200);  a.sh('t1', 0x1da6, 't0')
    a.li('t1', 0xc020); a.sh('t1', 0x1daa, 't0')
    a.la('t2', 'sample'); a.sw('t2', 0x10c0, 't0')
    a.li('t1', 0x00010000 | nbytes // 4); a.sw('t1', 0x10c4, 't0')
    a.li('t1', 0x01000201); a.sw('t1', 0x10c8, 't0')
    a.li('t1', 0xc000); a.sh('t1', 0x1daa, 't0')
    a.li('t1', 0x3fff); a.sh('t1', 0x1c00, 't0'); a.sh('t1', 0x1c02, 't0')
    a.li('t1', 0x1000); a.sh('t1', 0x1c04, 't0')
    a.li('t1', 0x200);  a.sh('t1', 0x1c06, 't0')
    a.li('t1', 0x000f); a.sh('t1', 0x1c08, 't0'); a.sh('zero', 0x1c0a, 't0')
    a.li('t1', 1);      a.sh('t1', 0x1d88, 't0')
