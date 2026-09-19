// stripped: ares' debugger UI has no place in a playback DLL. Kept as no-ops so exceptions.cpp
// compiles unmodified.
auto CPU::Debugger::load(Node::Object) -> void {}
auto CPU::Debugger::instruction() -> void {}
auto CPU::Debugger::exception(u8) -> void {}
auto CPU::Debugger::interrupt(u8) -> void {}
auto CPU::Debugger::message() -> void {}
auto CPU::Debugger::function() -> void {}
