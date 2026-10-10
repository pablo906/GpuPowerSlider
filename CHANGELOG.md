# Changelog

## 1.0.0

- Publish the working GA403WW / RTX 5080 Laptop runtime power GUI.
- Slider and integer wattage entry with live enforced limit and maximum readback.
- Guarded Apply disables Dynamic Boost, requests CURRENT through the installed
  NvpwrControl backend, then verifies the resulting limit.
- Queue requests while GPU utilization is above 30%; cancel pending requests.
- Restore 25 W Dynamic Boost and report external policy changes.
- Follow Windows light/dark preferences and size text for display scaling.
- Include 20 hardware-free controller and GUI checks, CI, portable release
  packaging and SHA-256 checksums.
