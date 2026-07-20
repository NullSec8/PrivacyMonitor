# Contributing to Privacy Monitor

Thank you for your interest in contributing! This document provides guidelines for contributing to the Privacy Monitor project.

## How to Contribute

### Reporting Bugs

1. Check if the bug has already been reported in [GitHub Issues](https://github.com/NullSec8/PrivacyMonitor/issues).
2. If not, create a new issue with:
   - A clear, descriptive title
   - Steps to reproduce the issue
   - Expected behavior vs actual behavior
   - Your environment (Windows version, .NET version, etc.)

### Suggesting Features

1. Check existing issues for similar suggestions.
2. Create a new issue with the `enhancement` label.
3. Describe the feature, why it's useful, and how it should work.

### Pull Requests

1. Fork the repository.
2. Create a branch for your feature or fix (`git checkout -b feature/my-feature`).
3. Make your changes following the coding standards below.
4. Test your changes thoroughly.
5. Commit with a clear, descriptive message.
6. Push to your fork and create a pull request.

## Development Setup

### Prerequisites

- Windows 10/11 (64-bit)
- .NET 9 SDK
- Node.js (for the update server)
- WebView2 Runtime

### Getting Started

```powershell
# Clone the repository
git clone https://github.com/NullSec8/PrivacyMonitor.git
cd PrivacyMonitor

# Run the update script to restore packages and build
.\update-all.ps1

# Run the app
dotnet run --project wpf-browser\PrivacyMonitor.csproj
```

### Project Structure

- `wpf-browser/` - Main WPF application
- `browser-update-server/` - Node.js update server
- `chrome-extension/` - Chrome/Edge extension
- `wpf-browser/website/` - Website files

## Coding Standards

### C# (WPF App)

- Follow standard C# naming conventions
- Use PascalCase for public members, camelCase for private fields
- Add XML documentation comments for public APIs
- Keep methods focused and reasonably sized
- Use meaningful variable and method names

### JavaScript (Update Server)

- Use consistent indentation (2 spaces)
- Use camelCase for variables and functions
- Add comments for complex logic
- Handle errors appropriately

### General

- Keep code clean and readable
- Don't add unnecessary comments
- Test changes before submitting
- Update documentation if needed

## Testing

### Manual Testing

1. Build the project: `.\update-all.ps1`
2. Run the app: `dotnet run --project wpf-browser\PrivacyMonitor.csproj`
3. Test the specific functionality you changed
4. Verify no regressions in existing features

### Automated Testing

- CI runs on push/PR (see `.github/workflows/ci.yml`)
- Ensure your changes pass all CI checks

## Documentation

- Update README.md if adding new features
- Update relevant documentation in `wpf-browser/docs/`
- Keep code comments up-to-date

## License

By contributing, you agree that your contributions will be licensed under the [MIT License](LICENSE).

## Questions?

If you have questions about contributing, feel free to open an issue or reach out to the maintainers.
