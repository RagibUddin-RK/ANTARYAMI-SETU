#!/bin/bash

# Exit immediately if a command exits with a non-zero status
set -e

echo "=================================================="
echo "      Building MacOnlineAgent App Bundle          "
echo "=================================================="

# 1. Clean up old builds
echo "[*] Cleaning old build directories..."
rm -rf ./publish
rm -rf ./MacOnlineAgent_osx-arm64.app
rm -rf ./MacOnlineAgent_osx-x64.app
rm -rf ./MacOnlineAgent.app

# 2. Publish self-contained single-file binaries
echo "[*] Publishing Apple Silicon (osx-arm64)..."
dotnet publish -c Release -r osx-arm64 --self-contained true -p:PublishSingleFile=true -o ./publish/osx-arm64

echo "[*] Publishing Intel x64 (osx-x64)..."
dotnet publish -c Release -r osx-x64 --self-contained true -p:PublishSingleFile=true -o ./publish/osx-x64

# Info.plist generator helper
create_info_plist() {
    local target_file="$1"
    cat <<EOF > "$target_file"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleExecutable</key>
    <string>MacOnlineAgent</string>
    <key>CFBundleIdentifier</key>
    <string>com.antaryami.maconlineagent</string>
    <key>CFBundleName</key>
    <string>MacOnlineAgent</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleShortVersionString</key>
    <string>1.0</string>
    <key>LSUIElement</key>
    <true/>
</dict>
</plist>
EOF
}

# 3. Create app package structure
# Check if lipo is available (typical on macOS with Xcode CLI tools) to make a Universal Binary
if command -v lipo &> /dev/null; then
    echo "[*] Creating Universal App (MacOnlineAgent.app) containing both architectures..."
    
    mkdir -p "MacOnlineAgent.app/Contents/MacOS"
    mkdir -p "MacOnlineAgent.app/Contents/Resources"
    
    # Merge both architectures into a single binary
    lipo -create "./publish/osx-arm64/MacOnlineAgent" "./publish/osx-x64/MacOnlineAgent" -output "MacOnlineAgent.app/Contents/MacOS/MacOnlineAgent"
    chmod +x "MacOnlineAgent.app/Contents/MacOS/MacOnlineAgent"
    
    create_info_plist "MacOnlineAgent.app/Contents/Info.plist"
    echo "[+] Successfully created Universal App: MacOnlineAgent.app"
else
    # Fallback to separate bundles
    echo "[!] lipo not found or not running on macOS. Creating separate Intel/Silicon apps..."
    
    # Apple Silicon App
    mkdir -p "MacOnlineAgent_osx-arm64.app/Contents/MacOS"
    mkdir -p "MacOnlineAgent_osx-arm64.app/Contents/Resources"
    cp "./publish/osx-arm64/MacOnlineAgent" "MacOnlineAgent_osx-arm64.app/Contents/MacOS/MacOnlineAgent"
    chmod +x "MacOnlineAgent_osx-arm64.app/Contents/MacOS/MacOnlineAgent"
    create_info_plist "MacOnlineAgent_osx-arm64.app/Contents/Info.plist"
    echo "[+] Created: MacOnlineAgent_osx-arm64.app"

    # Intel App
    mkdir -p "MacOnlineAgent_osx-x64.app/Contents/MacOS"
    mkdir -p "MacOnlineAgent_osx-x64.app/Contents/Resources"
    cp "./publish/osx-x64/MacOnlineAgent" "MacOnlineAgent_osx-x64.app/Contents/MacOS/MacOnlineAgent"
    chmod +x "MacOnlineAgent_osx-x64.app/Contents/MacOS/MacOnlineAgent"
    create_info_plist "MacOnlineAgent_osx-x64.app/Contents/Info.plist"
    echo "[+] Created: MacOnlineAgent_osx-x64.app"
fi

echo "=================================================="
echo " Build process completed successfully!"
echo "=================================================="
