# tone_irq.psf: the tone, plus a root-counter-2 interrupt every 10 ms whose BIOS event callback
# flips voice 0 between pitch 0x1000 and 0x0800 every 32 ticks (320 ms). Exercises the HLE
# syscalls, IRQ dispatch, the host->guest softcall and the register save/restore around it.
from psfasm import *

a = Asm()
spu_setup(a, 32)

# OpenEvent(RCntCNT2, EvSpINT, EvMdINTR, tick) -> v0; EnableEvent(v0). BIOS calls are jumps to
# absolute 0xb0 with the function number in t1 - here LOADED in the jalr delay slot, so the
# trap must see a load that is still in the delay queue when it fires. A trap that reads the
# stale t1 dispatches the wrong function and the pitch never flips.
a.li('a0', 0xf2000002); a.li('a1', 2); a.li('a2', 0x1000); a.la('a3', 'tick')
a.la('t6', 'fnums')
a.li('t2', 0xb0); a.jalr('t2'); a.lw('t1', 0, 't6')
a.addu('a0', 'v0', 'zero')
a.li('t2', 0xb0); a.jalr('t2'); a.lw('t1', 4, 't6')

# root counter 2: sysclk/8 = 4233600 Hz, target 42336 -> 100 Hz, IRQ + repeat
a.lui('t0', 0x1f80)
a.li('t1', 42336); a.sw('t1', 0x1128, 't0')
a.li('t1', 0x0258); a.sw('t1', 0x1124, 't0')
a.li('t1', 0x40);   a.sw('t1', 0x1074, 't0')      # I_MASK: RC2
a.mfc0('t1', 12); a.nop(); a.ori('t1', 't1', 0x0401); a.mtc0('t1', 12)   # IEc + IM2
a.label('spin'); a.j('spin'); a.nop()

a.label('tick')
a.lui('t0', 0x1f80)
a.la('t5', 'ticks')
a.lw('t6', 0, 't5'); a.nop()
a.addiu('t6', 't6', 1)
a.sw('t6', 0, 't5')
a.andi('t7', 't6', 0x1f)
a.bne('t7', 'zero', 'done'); a.nop()
a.lhu('t8', 0x1c04, 't0'); a.nop()
a.xori('t8', 't8', 0x1800)
a.sh('t8', 0x1c04, 't0')
a.label('done')
a.jr('ra'); a.nop()

a.label('ticks'); a.word(0)
a.label('fnums'); a.word(0x08); a.word(0x0c)
a.label('sample'); a.data(square_blocks())

open('tone_irq.psf', 'wb').write(psf(a.resolve(), {'title': 'tone irq', 'length': 3, 'fade': 0}))
print('tone_irq.psf written, %d words' % len(a.code))
