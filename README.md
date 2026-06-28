# DWN.BRIDGE 
<!-- VERSION_BADGE --> *Sync based on Private Release: **v1.0.0.36***
> "Faceless. Nameless. I just build the simulation. 'I can only show you the door. You're the one that has to walk through it.'" 💊

**DWN.BRIDGE** is an open-source, hacker-friendly Thin Client that bridges the gap between powerful, free web-based LLMs (like Google Gemini) and your local environment. 

By leveraging stealth browser automation (Playwright), it bypasses the need for expensive API tokens, allowing you to run a fully capable Agentic AI directly on your machine. The AI can read your files, execute commands, and write code on your behalf, without you ever paying a cent in API costs.

---
**DWN.BRIDGE LATEST RELEASE INSTALLER  (WINDOWS CLICKONCE)**

[www.dwnbridge.org](https://www.dwnbridge.org/)
---

---
**DWN.BRIDGE YOUTUBE OFFICIAL DEMO VIDEO CHANNEL**

[www.youtube.com](https://www.youtube.com/@MarckDwn)

---


## ⚡ Architecture
This repository contains the **Open-Source Thin Client** (WPF / .NET 10). It is purposefully designed as a "dumb proxy":
- **Total Transparency**: It interacts directly with your local File System and Command Line. Being open-source means you can audit the code and compile it yourself. No backdoors.
- **Dynamic Tools**: Tools like `READ_FILE`, `WRITE_FILE`, and `RUN_COMMAND` are executed locally, safely within a sandboxed terminal.
- **The "Cloud Brain"**: The heavy lifting of Prompt Engineering, Agent orchestration, and Context Management is offloaded to a proprietary, private backend Server. The Client and Server communicate via heavily encrypted AES-GCM payloads.

## 🛠️ How to run (Hacker Mode)
If you prefer to compile the code from source instead of using the pre-compiled ClickOnce installer:
1. Clone this repository.
2. Ensure you have the .NET 10 SDK installed.
3. Open `AIBridge.sln` in Visual Studio or your favorite IDE.
4. Compile the `AIBridge` project in `Debug` or `Release` mode.

## 🤝 Community Agents
The community is encouraged to build and share custom `.md` Agent definitions.
Currently, this client supports local custom agents. Just create your markdown profile in the app, map the dynamic tools, and unleash the AI.

---
**DWN.BRIDGE JOIN OUR COMMUNITY ON DISCORD**

[dwn.bridge community](https://discord.gg/45W4KDue8a)

---
## ⚖️ License
Released under the MIT License. See `LICENSE` for details.
