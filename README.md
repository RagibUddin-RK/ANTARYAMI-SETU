# ANTARYAMI-SETU

**ANTARYAMI-SETU** is a comprehensive, dual-mode Remote Administration Tool (RAT) and remote management platform. It allows an administrator to monitor, manage, and control remote Windows devices seamlessly through a centralized dashboard. The system supports both **Offline** (Direct TCP) and **Online** (HTTP-based) connection modes to ensure persistence and control over varying network conditions.

---

## 🏗️ Architecture & Components

The project consists of three main components:

### 1. Admin Dashboard (`/Admin`)
A robust C# WinForms application targeting .NET 10.0 that acts as the Command & Control (C2) center.
- Acts as a **TCP Server** on port 9999 to accept direct connections from Offline Agents.
- Acts as an **HTTP Client** to fetch data and send commands to Online Agents via the PHP API.
- Features a futuristic dark-themed UI with real-time traffic monitoring, Geo-IP node mapping, auto-lock security, and a payload builder.

### 2. Offline Agent (`/OfflineAgent`)
A standalone C# .NET 10.0 agent designed for local or direct-network administration.
- Connects directly to the Admin Dashboard via raw TCP sockets.
- Requires administrator privileges to run.
- Self-installs into `ProgramData` and registers for startup persistence via Windows Task Scheduler.
- Runs completely silently in the background (Windowless).

### 3. Online Agent (`/OnlineAgent`) & PHP API
A C# .NET 10.0 agent designed for over-the-internet administration where direct TCP connections are not feasible (e.g., behind NAT/Firewalls).
- Polls a central web server (`api_setu.php`) for commands using standard HTTP POST/GET requests.
- Avoids firewall blocks by blending in with regular web traffic.
- **Dynamic Token Security**: The API is strictly secured via a dynamic session token architecture. Agents register with a low-privilege key and generate unique Session Tokens (`X-API-KEY`) per execution, preventing C2 hijacking even if an agent executable is reverse-engineered.
- **IP-Based Rate Limiting**: Features a database-backed rate limiter set to **300 requests/minute** per IP on agent endpoints to prevent DDoS and storage spam, while automatically whitelisting Admin actions to prevent C2 lockouts.
- **PHP API (`api_setu.php`)**: Acts as the middleman database/relay between the Admin Dashboard and the Online Agent.
- **Large File Support Configurations**: Features `.user.ini` and `.htaccess` profiles configured to override Hostinger/cPanel upload limits for updates up to 2GB.

---

## 🚀 Key Features

Both agents support a wide array of remote management capabilities:

- **Remote Shell & Execution**: Execute invisible background commands via `cmd.exe` or `powershell.exe`.
- **Live Screen Capture & Streaming**: Take silent screenshots or stream the live desktop feed to the Admin panel.
- **File Management System**: Browse, upload, download (exfiltrate), rename, and delete files on the target machine.
- **Global Keylogger**: Silently hook and record all keystrokes globally.
- **Audio Manipulation**: Trigger custom system beeps or use Text-to-Speech to speak messages directly on the target machine.
- **Clipboard Management**: Read from and write to the target's clipboard.
- **Visual Alerts**: Trigger unclosable fullscreen popup alerts.
- **Self-Installation & Persistence**: Automatic elevation, self-copying to hidden directories, and dual startup persistence mechanisms (Windows Startup registry injection and scheduled tasks via `schtasks.exe`).
- **Wi-Fi Profile Extraction**: Extract saved Wi-Fi SSIDs and cleartext passwords (Online Agent exclusive).
- **Windows Event Log Forensics**: Extract dynamic reports containing logon actions (ID 4624/4625), log clears (ID 1102), and persistent system services (ID 7045).
- **Streamed Self-Update (1GB+)**: Memory-safe chunked downloads and extended network timeout configurations to deploy large payload agent binaries securely.
- **Self-Destruct & Uninstall**: Cleanly remove all traces and terminate the agent remotely.

---

## 🛠️ System Setup & Requirements

### Software & SDK Requirements
* **Operating System**: Windows 10 / Windows 11
* **.NET SDK**: [.NET 10.0 SDK](https://dotnet.microsoft.com/download)
* **IDE**: Visual Studio 2022 (with ".NET Desktop Development" workload) or VS Code.
* **PHP Environment**: PHP 8.0+ web server (XAMPP/WAMP or Public Hosting like Hostinger/cPanel) for the Online Agent API.

---

## ⚙️ Setup & Deployment

### 1. Admin Dashboard
1. Open `Admin/AntaryamiSetuAdmin.csproj` in Visual Studio.
2. Build and run the project. The TCP server will automatically start listening on port `9999`.

### 2. Offline Agent (Local Network)
1. Open `OfflineAgent/Program.cs`.
2. Update the `ServerIp` variable to match the IPv4 address of the machine running the Admin Dashboard:
   ```csharp
   private static string ServerIp = "192.168.x.x"; // Your Admin IP
   ```
3. Build the executable.

### 3. Online Agent (Public Internet & Hostinger)
1. Upload the `OnlineAgent/api_setu.php` file to your public web hosting directory (e.g., `public_html/api/api_setu.php`).
2. **Crucial:** Upload the generated `OnlineAgent/.user.ini` and `OnlineAgent/.htaccess` files to the exact same folder on Hostinger to support updates up to 2GB.
3. **API Key Setup:** Open `api_setu.php` and set your Admin `$ADMIN_KEY` (default is `SetuSecret@2026`) and Agent `$AGENT_REG_KEY` (default is `SetuAgentReg@2026`). 
   * Update the Admin key in `Admin/dashboard.cs`, and `Admin/terminal.cs` if you change it.
   * Update the Agent key in `OnlineAgent/Program.cs` and `MacOnlineAgent/Program.cs` if you change it.
4. Open `OnlineAgent/Program.cs` and update the `ApiUrl`:
   ```csharp
   private static string ApiUrl = "https://yourdomain.com/api/api_setu.php";
   ```
5. Open `Admin/dashboard.cs` and update the `ApiUrl` variable to point to the exact same URL so the Admin panel can manage the online agents.
6. Build the executables.

---

## 📦 Building Silent Executables (`.exe`)

Both agents can be compiled into standalone, single-file executables that run completely silently in the background (no console window) with administrator privilege enforcement configurations.

### Build Command
Open your terminal (PowerShell/CMD) in either the `OnlineAgent` or `OfflineAgent` directory and run:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:AssemblyName=CustomAgentName
```

**Command Flags Explained:**
* `-c Release`: Optimizes code for production.
* `-r win-x64`: Targets 64-bit Windows environments.
* `--self-contained true`: Embeds the .NET 10.0 runtime directly into the `.exe`, ensuring it runs on target machines without requiring a pre-installed .NET SDK.
* `-p:PublishSingleFile=true`: Merges all DLL dependencies into one single standalone executable.
* `-p:AssemblyName=CustomAgentName`: Customizes the output file name.

**Output Location:**
`[AgentFolder]\bin\Release\net10.0-windows\win-x64\publish\CustomAgentName.exe`
