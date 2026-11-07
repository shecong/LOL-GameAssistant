# Resource HID diagnostic prototype

Experimental source only; not referenced by the application build or installer.
It forwards Windows HID calls to a local OS copy and loads the user-supplied WAD
backend on the first HID API call, outside DllMain. It logs component loading,
which does **not** establish that resource replacement worked.

On 2026-11-07, the game launched with this prototype, but the Yasuo sample's HUD
icons and base model were unchanged after the assistant's memory engine was
stopped. Do not distribute this prototype as a working resource skin fix.

Additional temporary timing and backend-catalog probes are under `.artifacts`.
They must remain outside the production preparation pipeline.
