@echo off
rem Engine checks: three synthetic PSFs (manual FIFO upload, DMA upload, IRQ-driven driver)
rem rendered by obj\harness.exe and asserted by the check scripts. Needs python 3.
setlocal
cd /d "%~dp0"
python make_tone.py >nul || exit /b 1
python make_tone.py dma >nul || exit /b 1
python make_irq.py >nul || exit /b 1
..\obj\harness.exe tone.psf tone.wav >nul || exit /b 1
python check_tone.py tone.wav || exit /b 1
..\obj\harness.exe tone_dma.psf tone_dma.wav >nul || exit /b 1
python check_tone.py tone_dma.wav || exit /b 1
..\obj\harness.exe tone_irq.psf tone_irq.wav >nul || exit /b 1
python check_irq.py tone_irq.wav || exit /b 1
del /q *.psf *.wav 2>nul
echo all engine checks passed
