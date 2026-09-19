# tone.psf / tone_dma.psf: a PS-EXE that uploads a 2-block ADPCM square wave and keys voice 0 at
# pitch 1:1. Expected: 1575 Hz square (44100/28), near full scale. The default variant uploads
# through the manual FIFO (SPUDATA); `dma` uploads through DMA channel 4, as real drivers do.
import sys
from psfasm import *

DMA = len(sys.argv) > 1 and sys.argv[1] == 'dma'
a = Asm()

if DMA:
    spu_setup(a, 32)
else:
    a.lui('t0', 0x1f80)
    a.sh('zero', 0x1daa, 't0')
    a.li('t1', 0x3fff); a.sh('t1', 0x1d80, 't0'); a.sh('t1', 0x1d82, 't0')
    a.li('t1', 4);      a.sh('t1', 0x1dac, 't0')
    a.li('t1', 0x200);  a.sh('t1', 0x1da6, 't0')
    a.la('t2', 'sample'); a.li('t3', 16)
    a.label('loop')
    # addiu fills the load delay slot: the R3000 stores the OLD t4 if sh follows lhu directly
    a.lhu('t4', 0, 't2'); a.addiu('t2', 't2', 2); a.sh('t4', 0x1da8, 't0'); a.addiu('t3', 't3', -1)
    a.bne('t3', 'zero', 'loop'); a.nop()
    a.li('t1', 0xc010); a.sh('t1', 0x1daa, 't0')   # enable+unmute, manual write: flushes FIFO to RAM
    a.li('t1', 0xc000); a.sh('t1', 0x1daa, 't0')
    a.li('t1', 0x3fff); a.sh('t1', 0x1c00, 't0'); a.sh('t1', 0x1c02, 't0')
    a.li('t1', 0x1000); a.sh('t1', 0x1c04, 't0')
    a.li('t1', 0x200);  a.sh('t1', 0x1c06, 't0')
    a.li('t1', 0x000f); a.sh('t1', 0x1c08, 't0'); a.sh('zero', 0x1c0a, 't0')
    a.li('t1', 1);      a.sh('t1', 0x1d88, 't0')

a.label('spin'); a.j('spin'); a.nop()
a.label('sample'); a.data(square_blocks())

name = 'tone_dma.psf' if DMA else 'tone.psf'
open(name, 'wb').write(psf(a.resolve(), {'title': 'tone', 'length': 3, 'fade': 0}))
print(name, 'written, %d words' % len(a.code))
