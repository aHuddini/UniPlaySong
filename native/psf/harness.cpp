// Lab harness: PSF in, WAV out, through aopsf's glue over the ares SPU.
// Usage: harness <file.psf> <out.wav> [seconds]
#include <nall/nall.hpp>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>
#include <vector>
#include <map>
#include <filesystem>
#include <fstream>

extern "C" {
#include "psx_external.h"
}

namespace fs = std::filesystem;

struct Psf {
  std::vector<uint8_t> exe;               // decompressed PS-EXE
  std::map<std::string, std::string> tags;
};

static auto le32(const uint8_t* p) -> uint32_t { return p[0] | p[1] << 8 | p[2] << 16 | p[3] << 24; }

static auto readFile(const fs::path& p) -> std::vector<uint8_t> {
  std::ifstream f(p, std::ios::binary);
  if(!f) { fprintf(stderr, "cannot open %s\n", p.string().c_str()); exit(2); }
  return std::vector<uint8_t>(std::istreambuf_iterator<char>(f), {});
}

static auto parse(const fs::path& p) -> Psf {
  auto data = readFile(p);
  if(data.size() < 16 || memcmp(data.data(), "PSF\x01", 4)) { fprintf(stderr, "%s: not a PSF1\n", p.string().c_str()); exit(2); }
  uint32_t reserved = le32(&data[4]);
  uint32_t exeSize  = le32(&data[8]);
  uint32_t exeAt    = 16 + reserved;
  if(exeAt + exeSize > data.size()) { fprintf(stderr, "%s: truncated\n", p.string().c_str()); exit(2); }

  Psf psf;
  // zlib stream: 2-byte header, raw deflate, adler32 trailer. puff wants the raw deflate.
  psf.exe.resize(2 * 1024 * 1024 + 2048);
  uint32_t outLen = psf.exe.size(), inLen = exeSize - 2;
  int r = nall::Decode::puff::puff(psf.exe.data(), &outLen, &data[exeAt + 2], &inLen);
  if(r != 0) { fprintf(stderr, "%s: inflate failed (%d)\n", p.string().c_str(), r); exit(2); }
  psf.exe.resize(outLen);

  uint32_t tagAt = exeAt + exeSize;
  if(tagAt + 5 <= data.size() && !memcmp(&data[tagAt], "[TAG]", 5)) {
    std::string block((const char*)&data[tagAt + 5], data.size() - tagAt - 5);
    size_t pos = 0;
    while(pos < block.size()) {
      size_t nl = block.find('\n', pos);
      if(nl == std::string::npos) nl = block.size();
      std::string line = block.substr(pos, nl - pos);
      pos = nl + 1;
      size_t eq = line.find('=');
      if(eq == std::string::npos) continue;
      auto trim = [](std::string s) {
        while(!s.empty() && (s.back() == ' ' || s.back() == '\r' || s.back() == '\t')) s.pop_back();
        size_t i = 0; while(i < s.size() && (s[i] == ' ' || s[i] == '\t')) i++;
        return s.substr(i);
      };
      auto key = trim(line.substr(0, eq)), val = trim(line.substr(eq + 1));
      // multi-line values are repeated keys; join with newline
      if(psf.tags.count(key)) psf.tags[key] += "\n" + val; else psf.tags[key] = val;
    }
  }
  return psf;
}

// PSF load order: _lib first (its PC/SP win), then this file's EXE on top, then _lib2..N.
static auto load(PSX_STATE* psx, const fs::path& p, bool first, int depth = 0) -> Psf {
  if(depth > 10) { fprintf(stderr, "_lib nesting too deep\n"); exit(2); }
  auto psf = parse(p);
  auto dir = p.parent_path();
  if(auto it = psf.tags.find("_lib"); it != psf.tags.end()) {
    load(psx, dir / it->second, first, depth + 1);
    first = false;
  }
  if(psf_load_section(psx, psf.exe.data(), psf.exe.size(), first) != 0) {
    fprintf(stderr, "%s: %s", p.string().c_str(), psx_get_last_error(psx)); exit(2);
  }
  for(int n = 2; ; n++) {
    auto it = psf.tags.find("_lib" + std::to_string(n));
    if(it == psf.tags.end()) break;
    load(psx, dir / it->second, false, depth + 1);
  }
  return psf;
}

// s.ddd | m:ss.ddd | h:mm:ss.ddd, comma or dot
static auto seconds(const std::string& s) -> double {
  double total = 0;
  size_t pos = 0;
  while(pos <= s.size()) {
    size_t c = s.find(':', pos);
    std::string part = s.substr(pos, c == std::string::npos ? std::string::npos : c - pos);
    for(auto& ch : part) if(ch == ',') ch = '.';
    total = total * 60 + atof(part.c_str());
    if(c == std::string::npos) break;
    pos = c + 1;
  }
  return total;
}

static auto writeWav(const fs::path& p, const std::vector<int16_t>& pcm) -> void {
  std::ofstream f(p, std::ios::binary);
  auto w32 = [&](uint32_t v) { f.write((const char*)&v, 4); };
  auto w16 = [&](uint16_t v) { f.write((const char*)&v, 2); };
  uint32_t bytes = pcm.size() * 2;
  f.write("RIFF", 4); w32(36 + bytes); f.write("WAVE", 4);
  f.write("fmt ", 4); w32(16); w16(1); w16(2); w32(44100); w32(44100 * 4); w16(4); w16(16);
  f.write("data", 4); w32(bytes);
  f.write((const char*)pcm.data(), bytes);
}

// Spec: only the primary EXE's region marker (offset 0x4c) decides the refresh rate; _lib
// regions are ignored, and a _refresh tag overrides everything. The glue would take the region
// from whichever EXE loads first - the _lib - so the answer is fixed before any section loads.
static auto refreshRate(const Psf& primary) -> uint32_t {
  if(auto it = primary.tags.find("_refresh"); it != primary.tags.end()) return atoi(it->second.c_str());
  std::string marker((const char*)primary.exe.data() + 0x4c, std::min<size_t>(80, primary.exe.size() - 0x4c));
  return marker.find("Europe") != std::string::npos ? 50 : 60;
}

int main(int argc, char** argv) {
  if(argc < 3) { fprintf(stderr, "usage: harness <file.psf> <out.wav> [seconds]\n"); return 1; }
  fs::path in = argv[1], out = argv[2];

  std::vector<uint8_t> state(psx_get_state_size(1));
  auto* psx = (PSX_STATE*)state.data();

  // HLE_LOG=1 with a DEBUG_HLE_BIOS build of psx_hw.c prints every BIOS call the driver makes.
  if(getenv("HLE_LOG")) psx_register_console_callback(psx, [](void*, const char* m) { fputs(m, stderr); }, nullptr);

  psx_set_refresh(psx, refreshRate(parse(in)));
  auto psf = load(psx, in, true);
  for(auto& [k, v] : psf.tags) printf("  %-10s = %s\n", k.c_str(), v.c_str());
  printf("  refresh    = %u Hz\n", refreshRate(psf));

  double secs = argc > 3 ? atof(argv[3]) : 0;
  if(secs <= 0) {
    secs = psf.tags.count("length") ? seconds(psf.tags["length"]) : 0;
    if(psf.tags.count("fade")) secs += seconds(psf.tags["fade"]);
    if(secs <= 0) secs = 30;
  }

  if(psf_start(psx) != AO_SUCCESS) { fprintf(stderr, "psf_start: %s", psx_get_last_error(psx)); return 2; }

  const uint32_t chunk = 2048;
  uint32_t frames = uint32_t(secs * 44100);
  std::vector<int16_t> pcm(size_t(frames) * 2);
  for(uint32_t at = 0; at < frames; at += chunk) {
    uint32_t n = std::min(chunk, frames - at);
    if(psf_gen(psx, &pcm[size_t(at) * 2], n) != AO_SUCCESS) {
      fprintf(stderr, "psf_gen stopped at %u frames: %s\n", at, psx_get_last_error(psx));
      pcm.resize(size_t(at) * 2);
      break;
    }
  }
  psf_stop(psx);

  double sum = 0; int16_t peak = 0;
  for(auto s : pcm) { sum += double(s) * s; if(abs(s) > peak) peak = abs(s); }
  double rms = pcm.empty() ? 0 : sqrt(sum / pcm.size());
  printf("frames=%zu  rms=%.1f  peak=%d  (%.1fs)\n", pcm.size() / 2, rms, peak, pcm.size() / 2 / 44100.0);

  writeWav(out, pcm);
  return 0;
}
