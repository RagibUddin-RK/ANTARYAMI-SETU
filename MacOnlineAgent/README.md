# macOS Online Agent (`MacOnlineAgent`)

This is a lightweight, cross-platform C# implementation of the **Antaryami-Setu Online Agent** specifically designed for macOS. 

Its sole responsibility is to run silently in the background, periodically pinging the central C2 server (PHP API) with telemetry so the administrator can see if the target macOS device is **online** or **offline** in the Admin Dashboard.

---

## 🛠️ Requirements & Setup

To build the macOS Agent, you will need:
* **macOS Operating System** (to bundle/test the `.app` package)
* **.NET 10.0 SDK** (Installed on the build machine)

---

## ⚙️ Building the Application

We have provided an automated build script `build_app.sh` that compiles the C# program and packages it into a native macOS App Bundle (`.app`).

### 1. Build and Package
Open your terminal inside the `MacOnlineAgent` folder on your Mac and run:

```bash
chmod +x build_app.sh
./build_app.sh
```

### 2. Output
* If running on macOS with Xcode command-line tools installed, the script will automatically create a **Universal Binary App** (`MacOnlineAgent.app`) that works natively on both Intel (`x64`) and Apple Silicon (`arm64`/M1/M2/M3) Macs.
* If `lipo` is not found, it will produce separate directories:
  * `MacOnlineAgent_osx-arm64.app` (Apple Silicon)
  * `MacOnlineAgent_osx-x64.app` (Intel)

### 📦 Manual Build Command (Single File Binary)
If you want to build a raw standalone single-file binary manually (without the `.app` bundle wrapper), you can run these commands in your terminal:

**For Apple Silicon (M1/M2/M3/M4 macOS Devices):**
```bash
dotnet publish -c Release -r osx-arm64 --self-contained true -p:PublishSingleFile=true -p:AssemblyName=CustomAgentName
```

**For Intel-based macOS Devices:**
```bash
dotnet publish -c Release -r osx-x64 --self-contained true -p:PublishSingleFile=true -p:AssemblyName=CustomAgentName
```

*These commands will output a single standalone executable file `CustomAgentName` inside `bin/Release/net10.0/osx-arm64/publish/` or `bin/Release/net10.0/osx-x64/publish/` respectively.*

---

## 🚀 Execution & Silent Background Operation

A standard app bundle shows a window or a Dock icon. However, this agent is configured using the plist key:
```xml
<key>LSUIElement</key>
<true/>
```
This tells macOS that the application is a **background agent app**. 

When launched:
1. **No Dock icon** will appear.
2. **No window** will open.
3. The process runs completely silently in the background.

### Running the App:
You can launch it by double-clicking `MacOnlineAgent.app` in Finder, or from the terminal using:
```bash
open MacOnlineAgent.app
```

### Stopping the App:
Since there is no UI, you can stop the agent from the terminal using `killall`:
```bash
killall MacOnlineAgent
```

---

## 🔒 Configuration

You can customize the API endpoint by opening [Program.cs](file:///c:/Users/ragib/Desktop/ANTARYAMI-SETU/MacOnlineAgent/Program.cs) and editing the `ApiUrl` variable:
```csharp
private static string ApiUrl = "https://yourdomain.com/api/api_setu.php";
```
Make sure this matches the `ApiUrl` in your Admin Dashboard and `OnlineAgent` config.

**API Key Security:**
To prevent unauthorized access, the Mac Agent registers with a secret Registration Key and generates dynamic session tokens. Open `Program.cs` and ensure the registration `X-API-KEY` header matches the `$AGENT_REG_KEY` configured on your `api_setu.php` server:
```csharp
_telemetryClient.DefaultRequestHeaders.Add("X-API-KEY", "SetuAgentReg@2026");
```

**Rate Limiting:**
Outbound API requests from the agent are bound by an IP-based rate limiter (maximum **300 requests per minute**) on the central gateway to prevent system database flooding.

