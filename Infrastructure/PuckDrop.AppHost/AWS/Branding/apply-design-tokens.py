"""Applies app.css's design tokens to the managed login branding settings document.

Re-run after refreshing branding-settings.json from
`aws cognito-idp describe-managed-login-branding-by-client` - the describe returns Cognito's
defaults for any key you haven't overridden, and this puts the PuckDrop values back. Idempotent.

    python3 Infrastructure/PuckDrop.AppHost/AWS/Branding/apply-design-tokens.py
"""
import json
import pathlib

SETTINGS = pathlib.Path(__file__).resolve().parent / "branding-settings.json"

# app.css tokens as Cognito writes colours: RRGGBBAA, no leading '#'.
ICE         = "f4f7fbff"  # --pd-ice
SURFACE     = "ffffffff"  # --pd-surface
INK         = "0b1928ff"  # --pd-ink
SLATE       = "495766ff"  # --pd-slate
STEEL       = "677380ff"  # --pd-steel
LINE        = "d5dbe3ff"  # --pd-line
BOARDS      = "0e2743ff"  # --pd-boards
FROST       = "c9daebff"  # --pd-frost
BLUE        = "2460b7ff"  # --pd-blue
BLUE_DEEP   = "104a97ff"  # --pd-blue-deep
BLUE_TEXT   = "1f55a6ff"  # --pd-blue-text
BLUE_TINT   = "e4efffff"  # --pd-blue-tint
WIN         = "1b6c3aff"  # --pd-win
WIN_TINT    = "e0f7e5ff"  # --pd-win-tint
MISS        = "b02b27ff"  # --pd-miss
MISS_TINT   = "ffeae7ff"  # --pd-miss-tint
AMBER       = "804d0cff"  # --pd-amber
AMBER_TINT  = "ffeecbff"  # --pd-amber-tint
RADIUS      = 8.0         # --pd-radius

d = json.loads(SETTINGS.read_text())
c, cc = d["components"], d["componentClasses"]

# Only light mode is themed: categories.global.colorSchemeMode stays LIGHT because app.css has no
# dark theme, so Cognito's dark values are left untouched and never render.
d["categories"]["global"]["colorSchemeMode"] = "LIGHT"
# The header is what carries the --pd-boards band and the frost lockup; the footer stays off.
d["categories"]["global"]["pageHeader"]["enabled"] = True

c["pageBackground"]["lightMode"]["color"] = ICE
c["pageBackground"]["image"]["enabled"] = False  # no PAGE_BACKGROUND asset is uploaded

c["form"]["lightMode"]["backgroundColor"] = SURFACE
c["form"]["lightMode"]["borderColor"] = LINE
c["form"]["borderRadius"] = RADIUS
c["form"]["logo"].update({"enabled": True, "location": "CENTER", "position": "TOP", "formInclusion": "IN"})

c["pageHeader"]["lightMode"]["background"]["color"] = BOARDS
c["pageHeader"]["lightMode"]["borderColor"] = BOARDS
c["pageHeader"]["logo"].update({"enabled": True, "location": "START"})

c["pageText"]["lightMode"].update({"headingColor": INK, "bodyColor": SLATE, "descriptionColor": SLATE})

c["primaryButton"]["lightMode"]["defaults"].update({"backgroundColor": BLUE, "textColor": SURFACE})
for state in ("hover", "active"):
    c["primaryButton"]["lightMode"][state].update({"backgroundColor": BLUE_DEEP, "textColor": SURFACE})

sb = c["secondaryButton"]["lightMode"]
sb["defaults"].update({"backgroundColor": SURFACE, "borderColor": BLUE, "textColor": BLUE_TEXT})
sb["hover"].update({"backgroundColor": BLUE_TINT, "borderColor": BLUE_DEEP, "textColor": BLUE_DEEP})
sb["active"].update({"backgroundColor": BLUE_TINT, "borderColor": BLUE_DEEP, "textColor": BLUE_DEEP})

c["alert"]["lightMode"]["error"].update({"backgroundColor": MISS_TINT, "borderColor": MISS})

cc["buttons"]["borderRadius"] = RADIUS
cc["input"]["borderRadius"] = RADIUS
cc["input"]["lightMode"]["defaults"].update({"backgroundColor": SURFACE, "borderColor": STEEL})
cc["input"]["lightMode"]["placeholderColor"] = SLATE  # steel is border-only contrast, slate reads as text
cc["inputLabel"]["lightMode"]["textColor"] = INK
cc["inputDescription"]["lightMode"]["textColor"] = SLATE
cc["divider"]["lightMode"]["borderColor"] = LINE
# Managed login exposes only a focus ring colour, not app.css's 3px ring plus white gap.
cc["focusState"]["lightMode"]["borderColor"] = INK
cc["link"]["lightMode"]["defaults"]["textColor"] = BLUE_TEXT
cc["link"]["lightMode"]["hover"]["textColor"] = BLUE_DEEP
cc["optionControls"]["lightMode"]["defaults"].update({"backgroundColor": SURFACE, "borderColor": STEEL})
cc["optionControls"]["lightMode"]["selected"].update({"backgroundColor": BLUE, "foregroundColor": SURFACE})

si = cc["statusIndicator"]["lightMode"]
si["success"].update({"backgroundColor": WIN_TINT, "borderColor": WIN, "indicatorColor": WIN})
si["warning"].update({"backgroundColor": AMBER_TINT, "borderColor": AMBER, "indicatorColor": AMBER})
si["error"].update({"backgroundColor": MISS_TINT, "borderColor": MISS, "indicatorColor": MISS})

cc["dropDown"]["borderRadius"] = RADIUS
dd = cc["dropDown"]["lightMode"]
dd["defaults"]["itemBackgroundColor"] = SURFACE
dd["hover"].update({"itemBackgroundColor": ICE, "itemBorderColor": STEEL, "itemTextColor": INK})
dd["match"].update({"itemBackgroundColor": SLATE, "itemTextColor": BLUE})

SETTINGS.write_text(json.dumps(d, indent=1) + "\n")
print(f"applied design tokens to {SETTINGS.name} ({SETTINGS.stat().st_size} bytes)")
