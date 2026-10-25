# Warn only for Duplicate packages, not for several Packages at one Airport

The only load conflict the app warns about is a Duplicate package: the same Package name in more than one place the sim loads from. Several Packages belonging to the same Airport are shown neutrally ("3 packages"), and a Package that replaces or patches official scenery is shown as "replaces built-in".

## Considered Options

- **Warn when two non-official Packages provide the same Airport.** Rejected: stacked layers are common and intended. EHAM has three Packages from two creators (`brickbybrickdesign-airport-eham-amsterdam`, `duijffmedia-airport-eham-amsterdam`, `duijffmedia-schiphol-amsterdam-mega-enhancement-lighting`) that complement each other. A warning would be a false alarm, and the app can't reliably tell layers from true competitors.
