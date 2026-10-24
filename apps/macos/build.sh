#!/bin/sh
set -eu
cd "$(dirname "$0")/../.."
mkdir -p dist/ShowDevice.app/Contents/MacOS
swiftc -swift-version 5 -O -framework AppKit apps/macos/main.swift ui/native/macos/PluginPage.swift -o dist/ShowDevice.app/Contents/MacOS/ShowDevice
cp apps/macos/Info.plist dist/ShowDevice.app/Contents/Info.plist
cp dist/showdevice dist/ShowDevice.app/Contents/MacOS/showdevice
cp -R dist/plugins dist/ShowDevice.app/Contents/MacOS/plugins

