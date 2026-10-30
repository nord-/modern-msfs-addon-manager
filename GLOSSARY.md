# Modern MSFS Addon Manager

The vocabulary for managing which content Microsoft Flight Simulator 2020 loads: where the packages live, how they get into the sim, and what they contain.

## Packages and cards

**Package**:
A folder with a `manifest.json`, or a GSX profile file. A folder Package is the smallest unit the sim loads, and the only thing that is itself on or off.
_Avoid_: Addon, Mod

**Card**:
The app's group of Packages that belong together, such as all Packages for one Airport or one aircraft. A Package belongs to at most one card. On the card wall it shows an image, the ICAO code and Airport name or the card's name, and a light for its state: red Off, yellow Mixed, green On, or grey with a lock when Locked; its Packages are listed in the card's dialog. Scenery, Aircraft and Other cards are different kinds: only Scenery cards have ICAO codes, and cards are merged only within one menu section.
_Avoid_: Addon, bundle, group, product, tile

**Provisional card**:
The card of a Package that has no card yet, shown until a completed scan has read it. It offers on/off only and is never stored.

**Not shown**:
A Package that a completed scan has read but that has no card, because no Library registered for its menu section holds it. It is listed in Settings, where the user can give it a card.

**Not found**:
A Package whose Library answers but whose folder is gone. It keeps its card and all its user data until the user removes it.

**Built-in part**:
A part of a card that comes from the sim itself: a built-in Airport, or a Sim-managed aircraft base, livery or attached part of the card's aircraft. It is computed on every analysis, never stored, shown dimmed in the dialog and not counted towards the card's state.

**Tag**:
A label on a Package taken from the folders between its Library root and the Package, such as "Europe". A card has the tags of all its Packages; an Aircraft card also shows its Type designators as tags.
_Avoid_: Catalog, folder, group

**Uses**:
A relation from one card to another it needs but doesn't contain, such as an Airport using an Asset pack. Not in the MVP.
_Avoid_: Contains, includes

**Mixed**:
The displayed state of a card when some of its Packages are on and others are off.
_Avoid_: Partial

**Asset pack**:
A Package of shared objects or models that other cards use, such as an object library. It places nothing in the world itself.
_Avoid_: Library, model library, object library

## Where Packages live

**Library**:
A folder on disk the user registers for a menu section (Scenery, Aircraft or Other) as a home for Packages. Packages can sit at any depth inside it. The same folder may be registered for several menu sections; whether a Package gets a card automatically depends on whether a Library registered for its menu section holds it.
_Avoid_: Addon folder, repository, store, Library type

**Library not found**:
A registered Library whose root doesn't answer, such as an unplugged drive. Nothing changes while it is gone: its Packages keep their cards, choices and links.

**Community**:
The sim's Community folder. A Package is loaded from here when an entry in Community points to it.

**Sim-managed package**:
A Package that the sim itself installs in its Official folder and always loads: the official content and Marketplace purchases. The app reads these but never changes them.
_Avoid_: Built-in package, official package (official is only one kind)

## Community entries

**Managed entry**:
A junction or symlink in Community that points to a Package in a registered Library. The app can create and remove it.

**External entry**:
A link in Community that points outside every registered Library, or to a Library Package under another name.

**Fixed entry**:
A real folder in Community that is a Package. It is always on and the app never touches it.

**Ignored entry**:
A folder in Community, or the target of a link, that isn't a Package. The sim ignores it.

**Broken entry**:
A link in Community whose target no longer exists, outside any Library not found, and whose name matches no Library Package.

**Stale entry**:
A link in Community whose target is gone, but whose Package was found elsewhere in a registered Library.

**Offline entry**:
A link in Community into a registered Library that doesn't answer right now, or into a folder a scan could not read.

**Unreadable**:
A Package a scan could not read without errors: a read error inside the Package or in any folder below a Library root that answers. It is shown in the banner, never as Not found. An unreadable Package keeps its card if it has one; one without a card stays provisional, and its menu section is fixed and its card made only by a scan that reads it without errors.

**Stray entry**:
A loose file in Community that is not a Package.

## Content

**Category**:
The menu section a Package belongs to by its content: Scenery, Aircraft or Other. It is fixed once a completed scan has read the Package. A card's menu section is set when the card is made and changes only when the user moves it.

**Manifest section**:
The menu section a Package's manifest `content_type` alone gives it. Provisional cards use it until a completed scan has fixed the Package's Category.

**Airport**:
A single airfield, identified by its ICAO code. Several Packages can relate to the same Airport at once, including Sim-managed packages.
_Avoid_: Airfield, ICAO (as a name for the airport itself)

**ICAO code**:
The 3–5 character code that identifies an Airport: its ICAO code, or its local ident when it has none (such as `OG20`). Always written in full.
_Avoid_: code (alone), Airport code, ident

**Generic airport**:
An Airport known only from the sim's generic airport or navigation data, with no Package providing scenery for it.

**Airport scenery**:
A Scenery card whose Packages belong to an Airport.

**Landscape**:
A Scenery card that belongs to no Airport and isn't an Asset pack, such as mesh, landmarks, regions or photo scenery.
_Avoid_: Scenery (as a type), area, regional scenery

**Container**:
The identity of an aircraft in the sim: the folder name under `SimObjects\Airplanes` that holds its `aircraft.cfg`. Aircraft cards are grouped by Container.

**Type designator**:
The aircraft's ICAO type from `icao_type_designator` in its `aircraft.cfg`, such as `C152`. Shown as a tag on the Aircraft card; it never decides grouping.

**Livery**:
An Aircraft Package that repaints an aircraft provided by another Package.

**Mod**:
A Library Package that provides a Container that a Sim-managed package, a Fixed entry or an External entry also provides.

**Duplicate package**:
The same Package name both in Community and in the sim's Official folder.

**Duplicate copy**:
The same Package name present more than once across the Libraries, where at most one copy can be linked into Community.
