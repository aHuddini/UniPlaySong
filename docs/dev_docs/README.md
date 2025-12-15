# Developer Documentation

This folder contains technical documentation for developers working on or studying the UniPlaySong extension.

## Quick Start

1. **[DEVELOPER_GUIDE.md](DEVELOPER_GUIDE.md)** - Start here for architecture overview and codebase structure
2. **[BUILD_INSTRUCTIONS.md](../BUILD_INSTRUCTIONS.md)** - How to build the extension

## Documentation Index

### Core Documentation
- **DEVELOPER_GUIDE.md** - Main developer guide with architecture overview
- **VERSIONING.md** - Version management system and release process
- **TESTING_GUIDE.md** - Testing guidelines and best practices

### Architecture & Services
- **ERROR_HANDLER_SERVICE.md** - Centralized error handling service
- **LOGGING_STANDARD.md** - Logging standards and patterns
- **CONSTANTS_AND_LOGGING_UPDATE.md** - Constants module and logging infrastructure

### Feature Implementations
- **AUDIO_NORMALIZATION_IMPLEMENTATION.md** - Audio normalization feature technical details

## Code Structure

The extension follows a service-based architecture with clear separation of concerns:

- **Services/** - Business logic (download, playback, normalization, etc.)
- **Models/** - Data models and DTOs
- **Views/** - UI components and dialogs
- **Menus/** - Menu handlers
- **Players/** - Audio playback implementations
- **Common/** - Shared utilities, constants, converters

For detailed architecture information, see [DEVELOPER_GUIDE.md](DEVELOPER_GUIDE.md).

---
