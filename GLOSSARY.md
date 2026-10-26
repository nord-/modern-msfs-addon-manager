# Modern MSFS Addon Manager

The vocabulary for managing which content Microsoft Flight Simulator 2020 loads: where the packages live, how they get into the sim, and what they contain.

## Packages and where they live

**Package**:
A folder with a `manifest.json`. It is the smallest unit the sim loads.
_Avoid_: Mod

**Addon**:
A named collection of one or more Packages that the user turns on or off as one unit. A Package can belong to several Addons, and it is on while at least one of them is on.
_Avoid_: Bundle, group, product

**Partial**:
The state of an Addon where some of its Packages are on and others are off.

**Asset pack**:
A Package of shared objects or models that other Packages use, such as an object library.
_Avoid_: Library, model library, object library

**Library**:
A folder the user registers with the app as a home for Packages. Packages can sit at any depth inside it, and its folder structure has no meaning to the app.
_Avoid_: Addon folder, repository, store

**Community**:
The sim's Community folder. A Package is loaded from here when an entry in Community points to it.

**Sim-managed package**:
A Package that the sim itself installs and always loads: the official content and Marketplace purchases. The app reads these but never changes them.
_Avoid_: Built-in package, official package (official is only one kind)

## Community entries

**Managed entry**:
A link in Community that points into a registered Library. The app can turn it on or off.

**External entry**:
A link in Community that points outside every registered Library.

**Fixed entry**:
A real folder in Community, not a link. It is always on and the app never touches it.

**Broken entry**:
A link in Community whose target no longer exists.

**Stray entry**:
A loose file in Community that is not a Package.

## Content

**Category**:
The top-level kind of a Package: Scenery, Aircraft or Other.

**Airport**:
A single airfield, identified by its ICAO code, or by its local ident when it has none (such as `OG20`). Several Packages can relate to the same Airport at once, including Sim-managed packages.
_Avoid_: Airfield, ICAO (as a name for the airport itself)

**Airport scenery**:
A Scenery Package that belongs to at least one Airport.

**Landscape**:
A Scenery Package that belongs to no Airport, such as mesh, landmarks, regions or photo scenery.
_Avoid_: Scenery (as a type), area, regional scenery

**Duplicate package**:
The same Package name present in more than one place the sim loads from.

**Duplicate copy**:
The same Package name present in more than one Library, where only one copy is linked into Community.

**Livery**:
An Aircraft Package that repaints a specific aircraft and belongs under it.
