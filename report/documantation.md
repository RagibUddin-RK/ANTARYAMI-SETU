# 🌌 ANTARYAMI-SETU (अन्तर्यामी सेतु)
### *Omniscient Bridge - High-Tech Remote Administration & C2 Command Suite*

---

## 📖 Executive Summary
**ANTARYAMI-SETU** (meaning *"Omniscient Bridge"* or *"Inner Controller Bridge"* in Sanskrit) is an advanced, high-tech Remote Administration Tool (RAT) and Command & Control (C2) ecosystem. Engineered for high-fidelity endpoint monitoring and operational orchestration, the suite supports dual-mode communication paradigms: direct low-latency **TCP sockets** (for Local Area Networks/LAN) and RESTful **HTTP/S polling** (for Wide Area Networks/WAN). 

The ecosystem provides native administrative control over both Windows and macOS target platforms. Featuring a premium, sci-fi cyber-hacker aesthetic (HUD styling, glassmorphism, animated radar circles, and real-time visualization graphs), the suite provides administrators with an immersive operational dashboard to control target environments, exfiltrate strategic intelligence, and capture live telemetry.

---

## 🎯 Functional Feature Matrix
The following table outlines the feature availability and execution performance comparison across the three agent variants:

| Feature | 📡 WinOffline Agent (TCP LAN) | 🌐 WinOnline Agent (HTTP WAN) | 🍎 MacOnline Agent (HTTP WAN) |
| :--- | :--- | :--- | :--- |
| **Target Operating System** | Windows 10 / Windows 11 | Windows 10 / Windows 11 | macOS (Intel & Apple Silicon) |
| **Connection Topology** | Direct TCP Sockets (Port 9999) | RESTful HTTP Polling (`api_setu.php`) | RESTful HTTP Polling (`api_setu.php`) |
| **Communication Latency** | Ultra Low Latency (<10ms) | Dynamic Polling Delay (3s - 5s) | Dynamic Polling Delay (5s fetch, 10s telemetry) |
| **Required Privileges** | **Administrator** (`requireAdministrator`) | **Administrator** (`requireAdministrator`) | User (LaunchAgent Context) |
| **Remote Screen Capture** | Live Streaming Viewport (Type 8) | Static JPEG screenshots | Static PNG screenshots (via `screencapture`) |
| **File Explorer UI** | Interactive Tree/ListView (Type 9) | Command-line explorer | Command-line explorer (`cd`, `cat`, etc.) |
| **Clipboard read/write** | Full STA Thread Sync (Type 10) | Not Supported | Not Supported |
| **Keystroke Monitoring** | Real-time Keyboard hook buffer (Type 7)| Delayed interval HTTP updates | Not Supported (Requires Accessibility TCC) |
| **File Exfiltration (Pull)** | Direct Socket Transfer (Type 5) | Server File Vault Upload (`uploads/`) | Server File Vault Upload (`uploads/`) |
| **File Injection (Push)** | Direct Binary Injection (Type 6) | Remote Web Server File download | Remote Web Server File download |
| **System Info & Telemetry** | Automated 10-second TCP broadcast | Polling request registry updates | Polling request registry updates |
| **System commands (shell)** | Direct Cmd/PowerShell execution | Scheduled database job processing | Scheduled database job processing (zsh) |
| **Audio Alert & Beeps** | Host custom beep alerts | Basic beep executor | AppleScript audio beep trigger |
| **Text-To-Speech (TTS)** | Direct sound engine voice synthesis | Local text execution | Native macOS `say` voice synthesis |
| **Persistence Mechanism** | Windows Task Scheduler (`schtasks`) | Windows Task Scheduler (`schtasks`) & Registry Run Keys | macOS Startup LaunchAgent (`.plist`) |
| **Stealth Mode** | Completely windowless background process | Completely windowless background process | Windowless background process (`LSUIElement` plist) |
| **Event Log Forensics** | Parse Security (Logon/Clear) & System (Service) logs | Parse Security (Logon/Clear) & System (Service) logs | Not Supported |

### 📡 Windows Offline Agent (`WinOfflineAgent` Details)
*   **Live Stream Viewport:** Captures, encodes, and transmits active screen frames continuously utilizing GDI+ and JPEG quality scale parameters.
*   **Interactive File Manager:** Provides complete access to target drives including file download, file upload, rename, and directory deletes directly from a visual tree-list interface.
*   **STA Clipboard Synchronization:** Securely accesses and writes target clipboard text via Single-Threaded Apartment wrappers.
*   **Direct Payload Compiler:** Can compile payload executables target-coded to the server's local network interface configuration.

### 🌐 Windows Online Agent (`WinOnlineAgent` Details)
*   **Web Server Gate:** Routes commands and responses through a middleman PHP script (`api_setu.php`) storing jobs in MySQL tables. Allows easy traversal of host firewalls without port forwarding.
*   **File Vault Storage:** Downloads/Uploads exfiltrated binaries from the target computer directly into a secured, protected `/uploads/` subdirectory on the server.
*   **Heuristics Evasion:** Since communication uses standard web traffic ports (80/443), it is less likely to trigger network firewalls than non-standard TCP ports.
*   **Task Scheduler Elevation:** Automatically configures an elevated Scheduled Task (`schtasks.exe`) to restart automatically with Highest privileges upon system logon.

### 🍎 macOS Online Agent (`MacOnlineAgent` Details)
*   **macOS Target Support:** Implemented in C# targeting .NET 10.0, running natively on both Apple Silicon (ARM64) and Intel (x64) architectures.
*   **Native Shell Operations:** Executes system terminal commands via `/bin/zsh`, capturing standard output and error streams.
*   **LaunchAgent Persistence:** Integrates with macOS startup daemon structures by self-provisioning a LaunchAgent configuration file at `~/Library/LaunchAgents/com.antaryami.maconlineagent.plist`.
*   **Windowless Execution Profile:** Packed inside a native `.app` bundle, containing plist attributes (`LSUIElement = true`) that prevent the app from spawning a Dock icon or application window.

---

## 📐 System Architecture

The following diagram illustrates the hybrid architecture of **ANTARYAMI-SETU**, showcasing how the central **Admin Dashboard** simultaneously orchestrates LAN environments via direct TCP connections and WAN environments via the HTTP Web API gate.

```mermaid
graph TD
    %% Admin Suite Node
    subgraph Admin_Suite ["💻 Admin Command Center (Windows Forms)"]
        Dash[DashboardForm]
        Login[LoginForm - Secured Access]
        Term[TerminalForm - Action Center]
        Server[TCP Socket Server - Port 9999]
        Compiler[MSBuild Payload Compiler]
    end

    %% Web Infrastructure
    subgraph Web_Infrastructure ["🌐 C2 Web Gate (WAN)"]
        API[api_setu.php]
        DB[(MySQL Database)]
        VaultDir[Protected /uploads/ Vault]
    end

    %% Target Agents
    subgraph Targets ["🔒 Managed Targets"]
        OffAgent[WinOfflineAgent.exe - Windows LAN Target]
        OnAgentWin[WinOnlineAgent.exe - Windows WAN Target]
        OnAgentMac[MacOnlineAgent.app - macOS WAN Target]
    end

    %% LAN Flows
    Login -->|1. Authenticates| Dash
    Dash -->|2. Dynamic Build| Compiler
    Compiler -->|4. Generates| OffAgent
    Server <-->|Low-Latency TCP Socket (Packets 1-7)| OffAgent

    %% WAN Flows (Windows)
    Dash <-->|5a. HTTP Polling (5s)| API
    OnAgentWin <-->|6a. HTTP Polling & POSTs (10s)| API
    OnAgentWin -->|7a. Uploads Screenshots/Files| VaultDir

    %% WAN Flows (macOS)
    OnAgentMac <-->|6b. HTTP Polling & POSTs (10s)| API
    OnAgentMac -->|7b. Uploads Screenshots/Files| VaultDir
    
    %% API / Storage connection
    API <-->|SQL Queries| DB
    API -->|Saves Exfiltrated Binaries| VaultDir
    Term -->|8. Renders Data| VaultDir
```

---

## 🛠️ Detailed Technology Stack

The ANTARYAMI-SETU ecosystem is built on a modern, multi-tiered technology stack split between client-side administrative control, cross-platform web endpoints, and lightweight system background workers.

```
+-------------------------------------------------------------------------------+
|                               TECHNOLOGY STACK                                |
+-----------------------+-----------------------+-------------------------------+
|       COMPONENT       |    PRIMARY LANGUAGE   |      FRAMEWORK & LIBS         |
+-----------------------+-----------------------+-------------------------------+
|  Admin Dashboard      | C# (.NET 10.0)        | Windows Forms, Win32 Native   |
|  WinOffline Agent     | C# (.NET 10.0 C#)     | Native Interop (P/Invoke)    |
|  WinOnline Agent      | C# (.NET 10.0 C#)     | HttpClient, Multipart Form   |
|  MacOnline Agent      | C# (.NET 10.0 macOS)  | HttpClient, macOS Plist, zsh  |
|  C2 Web Gateway       | PHP 8.0+              | PDO (PHP Data Objects)        |
|  Persistent Storage   | SQL / relational      | MySQL / MariaDB               |
+-----------------------+-----------------------+-------------------------------+
```

### Core Technologies
1.  **C# & .NET 10.0**: Used to build the high-performance Windows GUI and all three command execution agents. Leverages async/await multi-tasking engines for non-blocking network I/O.
2.  **Win32 Native APIs**: P/Invoke bindings connect directly to Windows kernel libraries (`user32.dll` and `gdi32.dll`) to allow screen capture and global keyboard/mouse hook captures.
3.  **macOS Native Commands**: Bridges System Process APIs to `/bin/zsh` for custom command routing, `/usr/sbin/screencapture` for zero-window screenshots, and native AppleScript commands via `osascript`.
4.  **RESTful HTTP/HTTPS & TCP**: Implements asynchronous TCP socket servers on port `9999` and utilizes `System.Net.Http.HttpClient` for standard outbound web polling.
5.  **PHP PDO Backend**: Integrates structured database queries with prepared statement execution to defend against SQL Injection vulnerabilities on the C2 web interface.

---

## 📂 Project File Structure

Below is the directory architecture of the **ANTARYAMI-SETU** repository, showcasing component division and resource placement:

```
ANTARYAMI-SETU/                     <-- Project Root Folder
│
├── Admin/                          <-- Master Command Center Suite (WinForms)
│   ├── bin/                        <-- Compiled Binaries (Debug/Release)
│   ├── obj/                        <-- Intermediate Compilation Objects
│   ├── AntaryamiSetuAdmin.csproj   <-- MSBuild Project File
│   ├── LoginForm.cs                <-- Security Authenticator Form
│   ├── login_bg.png                <-- Authenticator UI Design Graphic
│   ├── dashboard.cs                <-- Master Dashboard Logic Panel
│   ├── devices.cs                  <-- Standalone Connected Devices View Form
│   ├── terminal.cs                 <-- Node Controller Terminal UI
│   ├── KeystrokeForm.cs            <-- Keylogger Telemetry Interceptor UI Form
│   ├── ScreenShareForm.cs          <-- Remote Screen Viewport Form
│   ├── FileManagerForm.cs          <-- Remote Explorer File Manager Form
│   ├── ClipboardForm.cs            <-- Remote Clipboard Manager Form
│   ├── logo_bg.png                 <-- Application Embedded Branding
│   └── Program.cs                  <-- Core WinForms Application Entry Point
│
├── WinOfflineAgent/                <-- Local Network TCP Background Service
│   ├── bin/                        <-- Compiled Agent Executables
│   ├── obj/                        <-- Build Objects
│   ├── AntaryamiSetuAgent.csproj   <-- C# Project Setup file
│   ├── EncryptionUtil.cs           <-- Symmetrical telemetry crypt engine
│   └── Program.cs                  <-- Agent Entry and Native TCP Execution
│
├── WinOnlineAgent/                 <-- Windows Wide Network Web-polling Agent
│   ├── bin/                        <-- Executables
│   ├── obj/                        <-- Intermediate Files
│   ├── OnlineAgent.csproj          <-- C# Project File
│   ├── app.manifest                <-- App configuration for Admin elevation
│   ├── Program.cs                  <-- HTTP Polling and Payload Operations
│   ├── api_setu.php                <-- C2 Web Gateway PHP Script
│   ├── .user.ini                   <-- Hostinger PHP configuration for 2GB uploads
│   └── .htaccess                   <-- Apache server configuration for body limits
│
├── MacOnlineAgent/                 <-- macOS Wide Network Web-polling Agent
│   ├── bin/                        <-- macOS Binaries
│   ├── obj/                        <-- Intermediate Files
│   ├── MacOnlineAgent.csproj       <-- C# macOS Project Configuration
│   ├── Program.cs                  <-- HTTP Polling, Telemetry, and macOS Shell
│   └── build_app.sh                <-- Automated universal bundle packager script
│
├── report/                         <-- Documentation Directory
│   └── documantation.md            <-- This Systems Architecture Report
│
└── logo.png                        <-- Global Brand Asset
```

---

## 🧮 Core Algorithms & Execution Mechanics

### 1. Custom Binary Packet Serialization Protocol (TCP Socket Mode)
For ultra-fast, low-bandwidth, low-overhead communication over TCP sockets, the ecosystem avoids heavy formats like XML or JSON. Instead, it relies on a raw binary sequence structure.

```
       HEADER FRAME (5 BYTES)                VARIABLE PAYLOAD
+------------------+---------------------+-------------------------------+
|   TYPE IDENTIFIER| PAYLOAD LEN (INT32) |         PAYLOAD DATA          |
|      1 Byte      |       4 Bytes       |            N Bytes            |
+------------------+---------------------+-------------------------------+
```
*   **Packet Reading Algorithm**:
    1.  The receiver reads exactly **5 bytes** from the network stream to parse the header.
    2.  Extract the first byte as the `Type Identifier`.
    3.  Convert the remaining 4 bytes into a 32-bit signed integer (`BitConverter.ToInt32`) to determine the exact `Payload Length` (N).
    4.  Initialize a buffer of size N, and read synchronously or asynchronously until N bytes are completely read.
    5.  Pass the payload buffer to the corresponding data deserializer based on the `Type Identifier`.

*   **Packet Type Identifiers**:
    *   `Type 1`: Client Telemetry (JSON formatted system details)
    *   `Type 2`: Command Execution Payload (Plaintext command string sent to target)
    *   `Type 3`: Console Execution Output (Plaintext response stream returned to controller)
    *   `Type 4`: Visual Intel Screenshot (Binary Jpeg stream)
    *   `Type 5`: Exfiltration Pull Transfer (4-byte filename length prefix + filename string + file payload bytes)
    *   `Type 6`: Injection Push Transfer (4-byte filename length prefix + filename string + file payload bytes)
    *   `Type 7`: Keystroke Streams (Plaintext raw keyboard input buffers)
    *   `Type 8`: Live Screen Sharing Frame (Compressed binary JPEG stream)
    *   `Type 9`: Directory Explorer List (JSON structured files and folders payload)
    *   `Type 10`: Clipboard Synchronization Data (Plaintext clipboard string)

### 2. Hardware-Bound Identifier (SHA-256 Derivation)
To prevent ID collisions and uniquely identify managed nodes without keeping static, vulnerable hardware logs, agents derive a hardware-bound key on startup.

$$\text{Identifier} = \text{SubString}\Big(\text{SHA256}(\text{MachineName} + \text{UserName}), \, 0, \, 12\Big) + \text{Suffix}$$

*   **Suffix Mappings**:
    *   Windows Offline: `_SETU`
    *   Windows Online: `_SETU_WEB`
    *   macOS Online: `_SETU_MAC`

### 3. macOS Windowless Stealth (`LSUIElement` Manipulation)
Standard macOS applications automatically claim a graphical canvas, displaying a Dock icon and a menu bar when launched. To bypass this visual notification and run invisibly:
*   The C# assembly output is packaged into a standard macOS application bundle directory structure:
    ```
    MacOnlineAgent.app/
    └── Contents/
        ├── Info.plist
        └── MacOS/
            └── MacOnlineAgent (Compiled Binary)
    ```
*   The `Info.plist` configuration inserts the Application UI element control key:
    ```xml
    <key>LSUIElement</key>
    <true/>
    ```
*   When the launcher loads the process, the macOS Window Server recognizes this key and flags the application as a background helper agent, suppressing the Dock icon and window layout.

### 4. LaunchAgents Persistence on macOS
To survive user logouts and reboots, `MacOnlineAgent` implements persistence via the Launchd system:
*   On execution, the agent checks if its running directory is located outside local build paths.
*   It automatically creates or updates a configuration plist file at `~/Library/LaunchAgents/com.antaryami.maconlineagent.plist`.
*   **Properties Configured**:
    *   `Label`: Unique identifier string `com.antaryami.maconlineagent`.
    *   `ProgramArguments`: Path to the compiled `MacOnlineAgent` binary inside the `.app` bundle.
    *   `RunAtLoad`: Set to `true` to immediately launch the binary when the user logs in.
    *   `KeepAlive`: Set to `true` to automatically relaunch the binary if the process crashes or gets killed.

### 5. Stream-Based Large Payload Update Execution (1GB+)
To update target agents with large payload binaries (up to 2GB) without exhausting system memory (RAM) or triggering timeout exceptions:
*   **Infinite Timeouts**: Both the Admin panel and Online Agent override the default HttpClient timeout (100 seconds) to a maximum of **45 minutes** (`TimeSpan.FromMinutes(45)`), preventing premature connection loss.
*   **Disk-Buffered Streaming (RAM Evasion)**:
    - *Admin Upload*: Utilizes `StreamContent` to stream the update binary directly from the disk file stream into the multipart HTTP POST request without staging bytes in RAM.
    - *Agent Download*: Uses `HttpCompletionOption.ResponseHeadersRead` on the API response to retrieve data. Instead of loading the binary into memory using `GetByteArrayAsync`, it reads the download network stream sequentially and writes to a file stream buffer (`FileStream` chunked write) directly into a temporary file on disk (`update_temp.exe`), keeping memory consumption near 0MB.
*   **Hot-Swapping replacement execution**: Runs a background batch script (`updater.bat`) that waits 3 seconds for the main agent thread to exit, deletes the original executable, renames the new streamed update, launches the upgraded agent, and cleans up itself.

---

## 🛡️ Platform Security Postures & Vulnerability Analysis

### 1. Architectural Vulnerabilities of ANTARYAMI-SETU
As designed, this administrative framework contains several security vulnerabilities that would make it highly insecure for use outside of isolated, authorized laboratory conditions:

*   **Hybrid Transport Encryption Postures**:
    *   *Offline TCP Channel*: The Offline TCP Agent uses **AES-256 symmetric encryption (CBC mode, PKCS7 padding)** via `EncryptionUtil` for all command and data payloads, preventing standard network sniffers from intercepting logs in transit.
    *   **Online HTTP/S Channel**: Since the API endpoint uses an **HTTPS URL (`https://antaryami.uno/...`)**, all web requests between the Admin Panel, Online Agents, and the Web Gateway are dynamically encrypted using **TLS/SSL (Transport Layer Security)**. This prevents network eavesdroppers from intercepting WAN communication.
*   **Static Hardcoded Security Credentials**:
    *   *Vulnerability*: The Admin Panel relies on hardcoded strings (`ANTARYAMI` / `Ragib@00100`) stored directly in the compiled binary.
    *   *Impact*: Reverse engineering tools (like ILSpy or dnSpy) can decompile the assembly in seconds and retrieve the master administrative password.
*   **Dynamic Session Token Authentication (Patched)**:
    *   *Previously*: The system relied on a single hardcoded master key (`SetuSecret@2026`) across all agents, making the entire C2 infrastructure vulnerable if a single agent `.exe` was decompiled.
    *   *Current Defense (Dynamic Tokens)*: The architecture now employs a strict separation of privileges and dynamic session tracking.
        1.  **Admin Actions**: Require the hardcoded master `SetuSecret@2026`.
        2.  **Agent Registration (`telemetry`)**: Agents use a separate, low-privilege `SetuAgentReg@2026` key strictly to announce their presence. During this handshake, the agent generates and transmits a unique cryptographic UUID (`session_token`).
        3.  **Agent Operations**: All subsequent payload requests (`fetch`, file uploads, keystrokes) are locked to the specific device's dynamically generated `session_token`.
    *   *Impact*: Decompiling an agent executable now only yields the low-privilege registration key. Attackers cannot hijack the admin panel, view other devices, or extract data from other infected endpoints.
*   **Lightweight IP-Based Rate Limiting**:
    *   *Defense*: The `api_setu.php` C2 gateway features a built-in rate limiter utilizing a local database table `rate_limits` to track request volumes per IP.
    *   *Parameters*: Outgoing agent requests (telemetry, log submission, uploads) are restricted to a maximum of **300 requests per 1-minute window** to mitigate DDoS and database/storage exhaustion risks.
    *   *Admin Exemption*: To ensure seamless C2 operations (such as broadcasting commands to 1000+ devices simultaneously), admin-specific dashboard actions (`send_cmd`, `get_devices`, `get_results`, etc.) are whitelisted and bypass the rate limit checks.

---

### 2. Windows 11 vs macOS Sequoia Native Defense Systems

#### Windows 11 Security Posture
*   **Windows Defender Heuristics**: Spawning command line processes (`cmd.exe /c` or `powershell.exe`) in rapid succession from background folders like `ProgramData` is flagged by behavioral heuristic engines.
*   **AMSI Scanning**: Script components running inside PowerShell run through the Antimalware Scan Interface to detect commands containing web-download strings.
*   **Startup Monitoring**: Writing to CurrentUser Run Registry keys without digital signatures causes immediate telemetry flags in Endpoint Detection and Response (EDR) solutions.
*   **UAC Consent Prompt**: Running with `requireAdministrator` triggers a User Account Control prompt. Administrative rights are required to bypass standard user sandboxing and register scheduled tasks.

#### macOS Security Posture (Sequoia / Sonoma)
*   **Gatekeeper & App Translocation**: Any unsigned `.app` bundle downloaded from external sources is sandboxed and executed from a random read-only virtual directory (translocation) to prevent DLL hijacking.
*   **TCC Permissions (Transparency, Consent, and Control)**:
    *   **Screen Capture**: `screencapture -x` requires explicit user permission inside "System Settings -> Privacy & Security -> Screen & System Audio Recording". If the permission is missing, the screenshot command fails or returns a black image.
    *   **Accessibility & Keylogging**: Reading global key events or registering hook events requires Accessibility configuration. `MacOnlineAgent` does not implement global hooks to avoid triggering early OS alerts.
*   **LaunchAgent Restrictions**: macOS notifies users via a persistent notification banner whenever a new item registers to run in the background (via `LaunchAgents` plist creations).

---

## 🛠️ Operational Guide & Troubleshooting

### 1. Agent Uninstallation & Clean Deletion

#### A. Windows Agents (WinOffline / WinOnline)
Execute the following commands via the Admin Console Terminal to cleanly uninstall the Windows agents:
```cmd
:: Delete registry startup key
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v "AntaryamiSetuAgent" /f
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v "AntaryamiSetuOnlineAgent" /f

:: Delete task scheduler entry
schtasks /delete /tn "OnlineAgentTask" /f
schtasks /delete /tn "AntaryamiSetuAgentTask" /f

:: Force process deletion after exit delay
cmd /c "timeout /t 3 & del /f /q %CD%\AntaryamiSetuAgent.exe"
```

#### B. macOS Online Agent (MacOnlineAgent)
Execute the following command sequence inside the target Mac terminal to completely erase all agent dependencies:
```bash
:: Stop LaunchAgent daemon
launchctl unload ~/Library/LaunchAgents/com.antaryami.maconlineagent.plist

:: Delete LaunchAgent plist file
rm -f ~/Library/LaunchAgents/com.antaryami.maconlineagent.plist

:: Terminate running process and delete application bundle
killall MacOnlineAgent
rm -rf /Applications/MacOnlineAgent.app
```

---

### 2. Compilation & Payload Packaging Guides

#### Windows Agents Compilation
Open a terminal in the folder of the target agent (`WinOnlineAgent` or `WinOfflineAgent`) and run:
```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:AssemblyName=CustomWinAgent
```

#### macOS Agent Compilation (`build_app.sh`)
The macOS agent requires compilation and packaging into a `.app` bundle.
1.  Copy the `MacOnlineAgent` directory to a macOS machine with the .NET 10.0 SDK.
2.  Open the terminal, grant execute permissions, and run the packager script:
    ```bash
    chmod +x build_app.sh
    ./build_app.sh
    ```
3.  The script performs the following operations:
    - Compiles binaries for Apple Silicon (`osx-arm64`) and Intel (`osx-x64`).
    - Uses `lipo` (if Xcode CLI tools are installed) to merge them into a single **Universal Binary**.
    - Provisions `Contents/Info.plist` with `LSUIElement` parameters.
    - Outputs `MacOnlineAgent.app` ready for deployment.

---
*Developed under the ANTARYAMI command architecture. All systems fully monitored.*
