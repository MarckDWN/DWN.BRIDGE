# 🤝 Contributing to DWN.BRIDGE

Thank you for your interest in contributing to **DWN.BRIDGE**! We are actively looking for developers, testers, and co-maintainers to build a secure, local-first bridge for AI workflows. 

Whether you want to write C#, improve the sandboxing logic, add database drivers, or test on new environments, your help is highly appreciated.

---

## 🚀 How to Get Started

### 1. Prerequisite Setup
* Install [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
* Visual Studio 2025 (or Rider / VS Code with C# Dev Kit).
* Clone the repository:
  ```bash
  git clone https://github.com/MarckDWN/DWN.BRIDGE.git
  ```

### 2. Run in Debug Mode
1. Open the solution file `AIBridge.sln` in your IDE.
2. Ensure the startup project is set to `AIBridge` (the main WPF application).
3. Build and run in **Debug** mode.

---

## 🎯 Where We Need Help (Roadmap & Tasks)

We have opened several tasks on the GitHub issue tracker. Look for issues labeled:
* `good first issue`: Ideal tasks for getting familiar with the codebase.
* `help wanted`: Core features or fixes we need assistance with.

### Core Areas for Contribution:
1. **Database Providers**: We want to expand local schema parsing to support:
   * PostgreSQL (`Npgsql` integration).
   * MySQL / MariaDB.
   * Local folder CSV/JSON directories.
2. **UI & UX Polish**: 
   * Transitioning settings and popup dialogs to support dynamic Dark/Light theme switching.
   * Improving responsiveness of the main WebView2 wrapper grid.
3. **Local Sandboxing & CLI checks**:
   * Refining local execution timeouts and stdout/stderr parsing.
   * Implementing cleaner process cancellation triggers when a run is aborted.

---

## 📜 Coding Guidelines

* **Code Style**: We follow standard .NET/C# naming conventions (PascalCase for classes/methods, camelCase for local variables).
* **Privacy First**: Any contribution must adhere to the **Zero-Knowledge** model. No raw database rows, sensitive keys, or personal folder files should ever be logged or transmitted to the orchestration backend.
* **Consent Dialogs**: Any new tool or script execution capability must pass through the `CommandConsent` execution firewall.

---

## 📬 Join the Discussion

If you want to discuss architecture design, ask questions, or align on features:
* Join our [Discord Server](https://discord.gg/45W4KDue8a) and pop into the `#dev` channel.
* Open a GitHub Discussion or Issue.

Thank you for helping us make AI workflows safer and cost-free! 💻🔒
