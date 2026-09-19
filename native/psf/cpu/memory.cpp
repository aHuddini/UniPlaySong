// Replaces ares' memory.cpp. ares routes the CPU through its own bus (RAM, BIOS, MMIO map,
// instruction cache, access timing). Here every access goes to aopsf's psx_hw.c, which owns the
// RAM and the HLE register map, so the glue sees exactly the traffic it was written against.
//
// Kept from ares: alignment exceptions. Dropped: bus errors (the glue answers every address),
// the instruction cache, and per-access timing (one cycle per instruction, as the glue expects).
// A write while the cache is isolated goes nowhere, as on hardware.

inline auto CPU::fetch(u32 address) -> u32 {
  return program_read_dword_32le(bus, address);
}

inline auto CPU::peek(u32 address) -> u32 {
  return program_read_dword_32le(bus, address);
}

template<u32 Size>
inline auto CPU::read(u32 address) -> u32 {
  if constexpr(Accuracy::CPU::AddressErrors) {
    if constexpr(Size == Half) {
      if(unlikely(address & 1)) return exception.address<Read>(address), 0;
    }
    if constexpr(Size == Word) {
      if(unlikely(address & 3)) return exception.address<Read>(address), 0;
    }
  }
  if constexpr(Size == Byte) return program_read_byte_32le (bus, address);
  if constexpr(Size == Half) return program_read_word_32le (bus, address);
  if constexpr(Size == Word) return program_read_dword_32le(bus, address);
}

template<u32 Size>
inline auto CPU::write(u32 address, u32 data) -> void {
  if constexpr(Accuracy::CPU::AddressErrors) {
    if constexpr(Size == Half) {
      if(unlikely(address & 1)) return exception.address<Write>(address);
    }
    if constexpr(Size == Word) {
      if(unlikely(address & 3)) return exception.address<Write>(address);
    }
  }
  if(unlikely(scc.status.cache.isolate)) return;
  if constexpr(Size == Byte) program_write_byte_32le (bus, address, data);
  if constexpr(Size == Half) program_write_word_32le (bus, address, data);
  if constexpr(Size == Word) program_write_dword_32le(bus, address, data);
}
