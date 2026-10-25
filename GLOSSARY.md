# Modern MSFS Addon Manager

The vocabulary for managing which content Microsoft Flight Simulator 2020 loads: where the packages live, how they get into the sim, and what they contain.

## Packages and Addons

**Package**:
A folder with a `manifest.json`. It is the smallest unit the sim loads, and the only thing that is itself on or off.
_Avoid_: Mod

**Addon**:
One or more Packages that belong together, such as all Packages for one Airport. Every Package belongs to exactly one Addon. An Addon is a leaf in the app's tree; its Packages are properties of it, not nodes.
_Avoid_: Bundle, group, product

**Uses**:
A relation from one Addon to another it needs but doesn't contain, such as an Airport using an Asset pack.
_Avoid_: Contains, includes

**Catalog**:
The user's own grouping of Addons in the app, such as "Europe". Catalogs can nest. They start out mirroring a Library's folders but are independent of the disk afterwards.
_Avoid_: Folder, group, category

**Mixed**:
The displayed state of an Addon or Catalog when some of the Packages below it are on and others are off.
_Avoid_: Partial

**Managed elsewhere**:
A Package whose link another tool keeps recreating, so the app shows it locked and never changes its link.

**Asset pack**:
A Package of shared objects or models that other Addons use, such as an object library.
_Avoid_: Library, model library, object library

## Where Packages live

**Library**:
A folder on disk the user registers with the app as a home for Packages. Packages can sit at any depth inside it.
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
A link in Community whose target no longer exists.

**Stray entry**:
A loose file in Community that is not a Package.

## Content

**Category**:
The top-level kind of an Addon: Scenery, Aircraft or Other.

**Airport**:
A single airfield, identified by its ICAO code, or by its local ident when it has none (such as `OG20`). Several Packages can relate to the same Airport at once, including Sim-managed packages.
_Avoid_: Airfield, ICAO (as a name for the airport itself)

**Airport scenery**:
A Scenery Addon whose Packages belong to an Airport.

**Landscape**:
A Scenery Addon that belongs to no Airport and isn't an Asset pack, such as mesh, landmarks, regions or photo scenery.
_Avoid_: Scenery (as a type), area, regional scenery

**Container**:
The identity of an aircraft in the sim: the folder name under `SimObjects\Airplanes` that holds its `aircraft.cfg`.

**Livery**:
An Aircraft Package that repaints an aircraft provided by another Package.

**Duplicate package**:
The same Package name both in Community and in the sim's Official folder.

**Duplicate copy**:
The same Package name present more than once across the Libraries, where at most one copy can be linked into Community.
