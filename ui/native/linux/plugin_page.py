"""Native GTK renderer; consumes data only, never imports plugin code."""
from gi.repository import Gtk
from datetime import datetime

def condition(c, state):
    if c is None:
        return True
    if c["op"]=="host_writes_enabled":
        return False
    r=state.get(c.get("binding"), {})
    if r.get("availability")!="available":
        return False
    return c["op"]=="available" or (c["op"]=="equals" and r.get("value")==c.get("value"))

def value(state, key):
    r=state.get(key)
    if not r:
        return "Waiting for device"
    availability=r["availability"]
    if availability in ("unknown","offline","unsupported"):
        return {"unknown":"Unknown","offline":"Device offline","unsupported":"Not supported"}[availability]
    text=str(r["value"])
    if availability=="stale":
        text="Last known: "+text
    if r.get("updated_at_ms") is not None:
        text+=" · "+datetime.fromtimestamp(r["updated_at_ms"]/1000).strftime("%H:%M:%S")
    return text

def render(parent, model):
    if model.get("error"):
        parent.append(Gtk.Label(label="Reading unavailable: "+model["error"],wrap=True,xalign=0))
    state=model["state"]
    caps={c["id"]:c for c in model["capabilities"]}
    def add(node, box):
        if not condition(node.get("visible_when"),state):
            return
        label=node["label"]; component=node["component"]; binding=node.get("binding")
        if component!="button":
            box.append(Gtk.Label(label=label,xalign=0,wrap=True))
        if component=="group":
            group=Gtk.Box(orientation=Gtk.Orientation.VERTICAL,spacing=8)
            for child in node.get("children",[]):
                add(child,group)
            box.append(group)
            return
        t=caps.get(binding,{}).get("value_type",{})
        if component=="toggle":
            control=Gtk.Switch()
        elif component=="select":
            control=Gtk.DropDown.new_from_strings(t.get("values",[]))
        elif component=="number":
            control=Gtk.SpinButton.new_with_range(t.get("min",0),t.get("max",100),t.get("step") or 1)
        elif component=="slider":
            control=Gtk.Scale.new_with_range(Gtk.Orientation.HORIZONTAL,t.get("min",0),t.get("max",100),t.get("step") or 1)
        elif component=="button":
            control=Gtk.Button(label=label)
        else:
            control=Gtk.Label(label=value(state,binding),xalign=0,wrap=True,selectable=True)
        if component not in ("status","result"):
            control.set_sensitive(False)
            control.set_tooltip_text(label+" — read-only in this release")
        box.append(control)
    for page in model["pages"]:
        heading=Gtk.Label(label=page["title"],xalign=0);heading.add_css_class("title-2");parent.append(heading)
        for node in page["nodes"]:
            add(node,parent)

