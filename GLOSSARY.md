# Modern MSFS Addon Manager

The vocabulary for managing which content Microsoft Flight Simulator 2020 loads: where the packages live, how they get into the sim, and what they contain.

## Packages and where they live

**Package**:
A folder with a `manifest.json`. It is the smallest unit the sim loads.
_Avoid_: Mod

**Addon**:
A named collection of one or more Packages that the user turns on or off as one unit. Every Package has its own Addon, and can also belong to any number of grouped Addons. A Package is on while at least one Addon containing it is on.
_Avoid_: Bundle, group, product

**Partial**:
The state of an Addon whose Packages in Community don't match the user's on/off choice for it.

**Managed elsewhere**:
An Addon whose links another tool keeps recreating, so the app shows it locked and leaves it alone.

**Asset pack**:
A Package of shared objects or models that other Packages use, such as an object library.
_Avoid_: Library, model library, object library

**Library**:
A folder the user registers with the app as a home for Packages. Packages can sit at any depth inside it, and its folders are shown as a browsing tree.
_Avoid_: Addon folder, repository, store

**Community**:
The sim's Community folder. A Package is loaded from here when an entry in Community points to it.

**Sim-managed package**:
A Package that the sim itself installs in its Official folder and always loads: the official content and Marketplace purchases. The app reads these but never changes them.
_Avoid_: Built-in package, official package (official is only one kind)

## Community entries

**Managed entry**:
A link in Community that points to a Package in a registered Library. The app can turn it on or off.

**External entry**:
A link in Community that points outside every registered Library.

**Fixed entry**:
A real folder in Community that is a Package. It is always on and the app never touches it.

**Ignored entry**:
A folder in Community, or the target of a link, that isn't a Package. The sim ignores it.

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

**Container**:
The identity of an aircraft in the sim: the folder name under `SimObjects\Airplanes` that holds its `aircraft.cfg`.

**Livery**:
An Aircraft Package that repaints an aircraft provided by another Package, and belongs under that aircraft.

**Duplicate package**:
The same Package name both in Community and in the sim's Official folder.

**Duplicate copy**:
The same Package name present in more than one Library, where at most one copy can be linked into Community.
