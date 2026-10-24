#!/usr/bin/env python3
"""GTK 4 native host. Uses the generated core/api JSON contract over local pipes."""
import json
import os
import pathlib
import subprocess
import sys
import threading
import time
import gi
gi.require_version("Gtk", "4.0")
from gi.repository import Gtk, GLib
ROOT = pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "ui/native/linux"))
from plugin_page import render

class App(Gtk.Application):
    def __init__(self):
        super().__init__(application_id="org.showdevice.app")
        self.process = None
        self.previous = None
        self.models = []
        self.selected_id = None
        self.last_refresh = 0
    def do_activate(self):
        self.window = Gtk.ApplicationWindow(application=self, title="ShowDevice")
        self.window.set_default_size(900, 600)
        root = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=20)
        for side in ("top", "bottom", "start", "end"):
            getattr(root, "set_margin_"+side)(24)
        header = Gtk.Box(spacing=16)
        title = Gtk.Label(label="Devices", xalign=0, hexpand=True)
        title.add_css_class("title-1")
        refresh = Gtk.Button(label="Refresh")
        refresh.connect("clicked", self.refresh)
        header.append(title); header.append(refresh); root.append(header)
        self.status = Gtk.Label(label="Loading…", xalign=0, wrap=True)
        root.append(self.status)
        body = Gtk.Box(spacing=24, vexpand=True)
        self.list = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=8)
        self.list.set_size_request(250, -1)
        self.detail = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=16)
        scroll = Gtk.ScrolledWindow(hexpand=True, vexpand=True); scroll.set_child(self.detail)
        body.append(self.list); body.append(scroll); root.append(body)
        self.window.set_child(root); self.window.present()
        threading.Thread(target=self.read_core, daemon=True).start()
    def refresh(self, _button):
        if time.monotonic()-self.last_refresh < 2:
            return
        self.last_refresh = time.monotonic()
        try:
            if self.process and self.process.poll() is None:
                self.process.stdin.write(b"refresh\n"); self.process.stdin.flush()
        except OSError as error:
            self.show_status("Unable to refresh: "+str(error))
    def show_status(self, text):
        self.status.set_text(text)
        self.status.set_visible(bool(text))
    def read_core(self):
        base = pathlib.Path(os.environ.get("SHOWDEVICE_HOME", ROOT)) / "dist"
        try:
            self.process = subprocess.Popen(
                [str(base/"showdevice"), "demo" if os.environ.get("SHOWDEVICE_DEMO")=="1" else "watch", str(base/"plugins")],
                stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL)
            while True:
                line = self.process.stdout.readline(2097153)
                if not line:
                    break
                if len(line)>2097152 or not line.endswith(b"\n"):
                    raise ValueError("Invalid core frame")
                snapshot = json.loads(line)
                GLib.idle_add(self.apply, snapshot)
            GLib.idle_add(self.show_status, "Local core stopped. Reopen the app to reconnect.")
        except (OSError, ValueError) as error:
            GLib.idle_add(self.show_status, "Unable to read devices: "+str(error))
    @staticmethod
    def clear(box):
        while box.get_first_child():
            box.remove(box.get_first_child())
    def apply(self, snapshot):
        signature = json.dumps([snapshot["devices"], snapshot["diagnostics"]], sort_keys=True)
        if signature == self.previous:
            return False
        self.previous = signature
        self.models = snapshot["devices"]
        self.clear(self.list)
        self.show_status("\n".join(snapshot["diagnostics"]))
        for model in self.models:
            button = Gtk.Button(label=model["device"]["name"])
            button.connect("clicked", lambda _b, m=model: self.select(m))
            self.list.append(button)
        selected = next((m for m in self.models if m["device"]["id"]==self.selected_id), None)
        if selected is None and self.models:
            selected = self.models[0]
        if selected:
            self.select(selected)
        else:
            self.clear(self.detail)
            self.detail.append(Gtk.Label(label="Connect a supported USB device or receiver, then refresh.", wrap=True, xalign=0))
        return False
    def select(self, model):
        self.selected_id=model["device"]["id"]
        self.clear(self.detail)
        render(self.detail, model)
    def do_shutdown(self):
        if self.process and self.process.poll() is None:
            self.process.stdin.close()
            self.process.terminate()
        Gtk.Application.do_shutdown(self)

if __name__ == "__main__":
    App().run(sys.argv)

