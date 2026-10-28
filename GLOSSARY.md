# Modern MSFS Addon Manager

The vocabulary for managing which content Microsoft Flight Simulator 2020 loads: where the packages live, how they get into the sim, and what they contain.

## Packages and Addons

**Package**:
A folder with a `manifest.json`. It is the smallest unit the sim loads, and the only thing that is itself on or off.
_Avoid_: Mod

**Addon**:
One or more Packages that belong together, such as all Packages for one Airport. Every Package belongs to exactly one Addon. The app shows each Addon as a card; its Packages are listed in the card's dialog.
_Avoid_: Bundle, group, product

**Card**:
How the app shows one Addon on the card wall: an image, the Airport code and name or the Addon's name, and a light for its state.
_Avoid_: Tile

**Anchor**:
The Airport or Container an Addon was built around, so that new Packages for the same Airport or aircraft join it after renames and merges.

**Tag**:
A label on a Package taken from the folders between its Library root and the Package, such as "Europe". An Addon has the tags of all its Packages.
_Avoid_: Catalog, folder, group

**Uses**:
A relation from one Addon to another it needs but doesn't contain, such as an Airport using an Asset pack. Not in the MVP.
_Avoid_: Contains, includes

**Mixed**:
The displayed state of an Addon when some of its Packages are on and others are off.
_Avoid_: Partial

**Managed elsewhere**:
A Package whose link another tool keeps recreating, so the app shows it locked and never changes its link.

**Asset pack**:
A Package of shared objects or models that other Addons use, such as an object library. It places nothing in the world itself.
_Avoid_: Library, model library, object library

## Where Packages live

**Library**:
A folder on disk the user registers with the app as a home for Packages, with one or more types (Scenery, Aircraft, Other) that decide which of its Packages are shown. Packages can sit at any depth inside it.
_Avoid_: Addon folder, repository, store

**Community**:
The sim's Community folder. A Package is loaded from here when an entry in Community points to it.

**Sim-managed package**:
A Package that the sim itself installs in its Official folder and always loads: the official content and Marketplace purchases. The app reads these but never changes them.
_Avoid_: Built-in package, official package (official is only one kind)

## Community entries

**Managed entry**:
A junction or symlink in Community that points to a Package in a registered Library. The app can create and remove it.

**External entry**:
A link in Community that points outside every registered Library.

**Fixed entry**:
A real folder in Community that is a Package. It is always on and the app never touches it.

**Ignored entry**:
A folder in Community, or the target of a link, that isn't a Package. The sim ignores it.

**Broken entry**:
A link in Community whose target no longer exists, outside any unreachable Library, and whose name matches no Library Package.

**Stale entry**:
A link in Community whose target is gone, but whose Package was found elsewhere in a registered Library.

**Offline entry**:
A link in Community into a registered Library that can't be reached right now.

**Stray entry**:
A loose file in Community that is not a Package.

## Content

**Category**:
The top-level kind of an Addon: Scenery, Aircraft or Other.

**Airport**:
A single airfield, identified by its ICAO code, or by its local ident when it has none (such as `OG20`). Several Packages can relate to the same Airport at once, including Sim-managed packages.
_Avoid_: Airfield, ICAO (as a name for the airport itself)

**Airport code**:
The 3–5 character code that identifies an Airport: its ICAO code, or its local ident when it has none.
_Avoid_: Ident, ICAO

**Generic airport**:
An Airport known only from the sim's generic airport or navigation data, with no Package providing scenery for it.

**Airport scenery**:
A Scenery Addon whose Packages belong to an Airport.

**Landscape**:
A Scenery Addon that belongs to no Airport and isn't an Asset pack, such as mesh, landmarks, regions or photo scenery.
_Avoid_: Scenery (as a type), area, regional scenery

**Container**:
The identity of an aircraft in the sim: the folder name under `SimObjects\Airplanes` that holds its `aircraft.cfg`.

**Livery**:
An Aircraft Package that repaints an aircraft provided by another Package.

**Mod**:
A Library Package that provides a Container that a Sim-managed package, a Fixed entry or an External entry also provides.

**Duplicate package**:
The same Package name both in Community and in the sim's Official folder.

**Duplicate copy**:
The same Package name present more than once across the Libraries, where at most one copy can be linked into Community.
