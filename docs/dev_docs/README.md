# UniPlaySong - Developer Documentation

Welcome to the UniPlaySong developer documentation. This directory contains comprehensive documentation for developers working on or extending the UniPlaySong extension.

## Documentation Index

### Core Documentation

1. **[ARCHITECTURE.md](ARCHITECTURE.md)** - System architecture, design patterns, and service organization
   - Project structure and organization
   - Core architecture components
   - Design patterns used
   - Data flow diagrams
   - Extension points

2. **[DEPENDENCIES.md](DEPENDENCIES.md)** - Complete dependency reference
   - NuGet packages and versions
   - Native DLLs (SDL2)
   - External tools (yt-dlp, FFmpeg)
   - Dependency management
   - Troubleshooting guide

3. **[TECHNICAL_REFERENCE.md](TECHNICAL_REFERENCE.md)** - Technical implementation details
   - Key variables and state management
   - Feature implementation details
   - Logic flow explanations
   - Debugging tips
   - Constants reference

4. **[CODE_COMMENTS_REFACTORING.md](CODE_COMMENTS_REFACTORING.md)** - Code comment cleanup plan
   - Refactoring strategy
   - Comment style guide
   - File-by-file plan
   - Quality checklist

### Additional Resources

- **[BUILD_INSTRUCTIONS.md](BUILD_INSTRUCTIONS.md)** - Build and packaging instructions
- **[../GITHUB_RELEASE_CHECKLIST.md](../GITHUB_RELEASE_CHECKLIST.md)** - Release checklist

## Quick Start for Developers

### Understanding the Codebase

1. **Start with [ARCHITECTURE.md](ARCHITECTURE.md)**
   - Understand the overall system design
   - Learn about service organization
   - Review data flow diagrams

2. **Review [DEPENDENCIES.md](DEPENDENCIES.md)**
   - Understand what dependencies are required
   - Learn about native DLLs and external tools
   - Set up your development environment

3. **Reference [TECHNICAL_REFERENCE.md](TECHNICAL_REFERENCE.md)**
   - Look up specific implementation details
   - Understand key variables and logic
   - Debug issues with technical details

### Common Tasks

**Adding a New Feature:**
1. Review [ARCHITECTURE.md](ARCHITECTURE.md) to understand extension points
2. Check [DEPENDENCIES.md](DEPENDENCIES.md) for required dependencies
3. Reference [TECHNICAL_REFERENCE.md](TECHNICAL_REFERENCE.md) for similar implementations

**Debugging an Issue:**
1. Check [TECHNICAL_REFERENCE.md](TECHNICAL_REFERENCE.md) for relevant state variables
2. Review [ARCHITECTURE.md](ARCHITECTURE.md) to understand data flow
3. Use debugging tips in [TECHNICAL_REFERENCE.md](TECHNICAL_REFERENCE.md)

**Refactoring Code:**
1. Follow [CODE_COMMENTS_REFACTORING.md](CODE_COMMENTS_REFACTORING.md) guidelines
2. Maintain consistency with existing patterns
3. Update documentation as needed

## Documentation Standards

### Keeping Documentation Current

- **Update documentation when code changes**
- **Add documentation for new features**
- **Remove outdated information**
- **Keep examples current**

### Documentation Style

- **Clear and concise**: Get to the point quickly
- **Code examples**: Include relevant code snippets
- **Cross-references**: Link to related documentation
- **Practical focus**: Emphasize how things work, not just what they are

## Contributing to Documentation

### When to Update Documentation

- Adding new features or services
- Changing architecture or patterns
- Updating dependencies
- Fixing bugs that reveal documentation gaps
- Refactoring code that changes behavior

### How to Update Documentation

1. **Edit the relevant markdown file**
2. **Follow existing formatting and style**
3. **Update the table of contents if needed**
4. **Test code examples still work**
5. **Review for clarity and completeness**

## Additional Resources

### External Documentation

- [Playnite SDK Documentation](https://playnite.link/docs/)
- [SDL2 Documentation](https://wiki.libsdl.org/)
- [Material Design for WPF](https://github.com/MaterialDesignInXAML/MaterialDesignInXamlToolkit)

### Related Files

- `README.md` - User-facing documentation
- `CHANGELOG.md` - Version history
- `LICENSE` - License information

## Questions?

If you have questions about the codebase or need clarification on documentation:

1. Review the relevant documentation file
2. Check the code comments (see [CODE_COMMENTS_REFACTORING.md](CODE_COMMENTS_REFACTORING.md))
3. Search the codebase for similar implementations
4. Review git history for context

---

**Last Updated**: 2025-01-15  
**Documentation Version**: 1.0

