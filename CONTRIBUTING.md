# Contributing to KodyOrderSync

First off, thank you for considering contributing! We welcome contributions from everyone, whether you're fixing a bug, improving documentation, adding support for a new POS system, or suggesting a new feature.

Please take a moment to review this document to understand how you can contribute effectively.

## Code of Conduct

This project and everyone participating in it is governed by the [KodyOrderSync Code of Conduct](CODE_OF_CONDUCT.md). By participating, you are expected to uphold this code. Please report unacceptable behavior as outlined in the Code of Conduct.

## How Can I Contribute?

### Reporting Bugs

*   **Check Existing Issues:** Before submitting a bug report, please search the [GitHub Issues](https://github.com/kody-com/[your-repo-name]/issues) to see if the bug has already been reported.
*   **Provide Details:** If the bug hasn't been reported, please open a new issue. Include as much detail as possible:
    *   A clear and descriptive title.
    *   Steps to reproduce the bug.
    *   What you expected to happen.
    *   What actually happened.
    *   Relevant logs or error messages (please remove sensitive information).
    *   Your environment details (e.g., .NET version, OS version, POS system if relevant, MySQL version if relevant).

### Suggesting Enhancements

*   **Check Existing Issues:** Search the [GitHub Issues](https://github.com/kody-com/[your-repo-name]/issues) to see if your enhancement suggestion already exists.
*   **Provide Context:** Open a new issue describing your enhancement. Explain:
    *   What the enhancement is.
    *   Why it would be useful (what problem does it solve?).
    *   Any potential implementation ideas (optional).

### Pull Requests

We welcome pull requests for bug fixes and feature enhancements!

1.  **Fork the Repository:** Create your own fork of the repository on GitHub.
2.  **Clone Your Fork:** Clone your fork locally: `git clone https://github.com/YOUR_USERNAME/KodyOrderSync.git`
3.  **Create a Branch:** Create a descriptive branch for your changes: `git checkout -b feature/your-feature-name` or `git checkout -b fix/issue-number`.
4.  **Make Changes:** Implement your fix or feature.
    *   Adhere to the existing coding style (generally standard .NET conventions).
    *   Ensure your changes work correctly. Add tests if applicable (we aim to improve test coverage!).
5.  **Commit Changes:** Commit your changes with a clear commit message: `git commit -m "feat: Add support for XYZ POS"` or `git commit -m "fix: Correctly handle order status update logic"`.
6.  **Push to Your Fork:** Push your branch to your fork on GitHub: `git push origin feature/your-feature-name`.
7.  **Open a Pull Request:** Go to the original `KodyOrderSync` repository on GitHub and open a pull request from your branch to the `main` branch (or the relevant development branch).
    *   Provide a clear title and description for your pull request.
    *   Link to any relevant issues (e.g., "Closes #123").
    *   Explain the changes you've made and why.

## Development Setup

*   You'll need the .NET SDK specified in the project file.
*   Ensure you have access to necessary external systems for testing (like a test KodyOrder API endpoint or a local MySQL instance).
*   Configure your `appsettings.Development.json` or use user secrets for local development credentials.

## Questions?

Feel free to open an issue on GitHub if you have questions about contributing or the project itself.

Thank you for your contribution!