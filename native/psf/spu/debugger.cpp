// stripped: ares' debugger UI has no place in a playback DLL. load() kept as a no-op so
// spu.cpp compiles unmodified.
auto SPU::Debugger::load(Node::Object) -> void {}
