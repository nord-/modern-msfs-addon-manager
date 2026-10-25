# Egen addon-hanterare för MSFS – analys

Sparad 2026-10-25. Underlag för att plocka upp bollen senare.

## Bakgrund

Rickard använder idag **MSFS Addons Linker** (bad2000, gratis, flightsim.to/addon/1572) och har tittat på **BOB Dispatcher** (bobdispatcher.com, kommersiell).

**Det Rickard gillar i Addons Linker**
- Slå av/på addons snabbt.
- Karta över installerade flygplatser **plus** MSFS inbyggda flygplatser.

**Det som saknas / stör**
- Gränssnittet är fult och krångligt.
- Ingen koppling till GSX-profiler.
- Ingen sökning efter scenery-addons man *inte* har installerat.

## Jämförelse: Addons Linker vs BOB

| | Addons Linker | BOB Dispatcher |
|---|---|---|
| Pris | Gratis (donation) | Engångsköp (SimMarket / flightsim.to) |
| MSFS 2020 | Ja (egen exe) | Ja – väljs i setup (enl. utvecklaren, se nedan) |
| MSFS 2024 | Ja (egen exe) | Ja – primärt fokus |
| Var filerna ligger | I en mapp *du* väljer och organiserar | Flyttas in i BOB:s eget bibliotek vid import |
| Av/på | Länk i Community | Länk i Community |
| Karta | Ja, inkl. teleport via SimConnect | Ja (World Map), med METAR, GSX-status, "Fly from/to here" |
| GSX-profiler | Valfri kolumn (senaste versionen) | Egen vy: With Profile / Missing / Two profiles |
| Per-flygning-aktivering | Nej (manuella presets) | Ja – SimBrief/rutt → aktiverar rätt scenery + flygplan |
| Starta sidoprogram | Nej | Ja (Launch Flow) |
| Mognad | Lång historik, ~576K nedladdningar, betyg 4,9 | Nyare |

**Lärdom från BOB-kommentar (Mingo01, okt 2026):** BOB *flyttar* addons till sitt bibliotek. Avinstallerade man BOB pekade länkarna i Community på ingenting och det såg ut som att filer var förstörda. Sedan 1.4.10 finns "Move Everything Back" (Settings → General, och erbjuds vid avinstallation). Utvecklaren bekräftar också MSFS 2020-stöd och att 1.5.7 fixade Marketplace-flygplatser för 2020.

→ **Designprincip för vår app:** flytta aldrig användarens filer implicit. Avinstallation/byte av verktyg ska aldrig kunna lämna Community trasig.

## Nuläge på Rickards maskin (inventerat 2026-10-25)

- Community: `C:\Users\ricka\AppData\Roaming\Microsoft Flight Simulator\Packages\Community` (Steam-installation av MSFS 2020).
- Addon-lager: `C:\Spel\Addons\{AIG, Aircraft, fsltl, Scenery\{Australia, Europe, North America, Objects}, Tools}`.

Innehåll i Community (118 poster):

| Typ | Antal | Kommentar |
|---|---|---|
| **Junction** | 95 | Addons Linker använder junctions. Flertalet pekar in i `C:\Spel\Addons\…` |
| Junction utanför `C:\Spel\Addons` | 19 av de 95 | Orbx Library (`C:\Spel\Orbx Library\msfs\…\Community\…`), FSDT Addon Manager (GSX), Neofly |
| **SymbolicLink** | 3 | SayIntentions (skapade av SayIntentions egen installer). En är **trasig**: `SayIntentions-VR-Toolbar-Plugin` |
| Riktiga mappar | 19 | Navigraph, FlyByWire installer, FSUIPC/MobiFlight-moduler, Carenado m.fl. – installerade direkt av respektive installer |
| Fil | 1 | `UNINSTALL_CARENADO_PA28R_181_MSFS2020.exe` (skräp i Community) |

Observationer:
- **Svaret på frågan: det är junctions**, inte symlinks (förutom SayIntentions tre).
- Neofly-paket och `bagolu-c152-neoflybannertow` finns **dubbelt**: både i `C:\Spel\Addons\{Tools,Aircraft}` och i `C:\Spel\Neofly\addons`, och Community pekar på Neofly-kopian.
- Appen måste alltså hantera en blandning: egna junctions, junctions skapade av andra verktyg (Orbx, FSDT), symlinks, riktiga mappar och trasiga länkar. Jämför BOB:s färgkodning: *Made outside Bob / Broken link / Bob's link / Always on / Plain folder*.
- GSX Pro-profiler: `%APPDATA%\Virtuali\GSX\MSFS\*.ini` – 33 st, namngivna `<icao>-<författare>.ini` (t.ex. `eddf-virtualFRA_GSX_VDGS.ini`, `EFHK-Epixeri.ini`). ICAO = prefix före första `-`, skiftlägesokänsligt.

## Inspiration: BOB:s gränssnitt

Skärmdumpar: `C:\Users\ricka\OneDrive\Bilder\bob\` (5 st, .webp).

1. **Flight Preparation** – rutt på mörk karta, kort per avgång/ankomst/flygplan med status (Found / Turns on / "Two copies load" / "GSX not found – Find a profile").
2. **My Content (grid)** – kort med bild, ICAO + namn, skapare, region/land, toggle. Filterflikar per typ (Airports, Scenery, Aircraft, Liveries, Utilities, Model Libraries), grupper/taggar, Enabled/Disabled, Creator/Region/Country, sök, Bulk Editor.
3. **My Content (delad vy)** – vänster: bibliotek som träd med kryssrutor; höger: faktiskt innehåll i Community med typ, version och statusfärg.
4. **World Map** – mörk karta (Mapbox), installerade flygplatser markerade, sidopanel med bild, METAR, GSX-status, toggle, "Fly From/To Here". Filter: All / On / Off / Streamed by MSFS / Not in Bob.
5. **GSX Profiles** – tabell ICAO, namn, land, skapare, parking, installerad, status (Installed / Off / Two profiles / Two sceneries / Missing) + åtgärder (sök profil).

Det som gör BOB tilltalande: mörkt, lugnt tema, generöst luftigt, tydlig statusfärg, ett vänstermenyträd, kort *eller* tabell efter behov.

### GSX-inspiration: Dave's GSX Profile Manager

flightsim.to/addon/105454 (DaveGaming, v1.0.4, gratis, källkod på GitHub, betyg 4,8). Fristående verktyg som jämför installerad flygplats-scenery med GSX Pro-profiler.

**Värt att låna**
- Hitta sim-installationen via `InstalledPackagesPath` i `UserCfg.opt`, och klara Steam-, Store-, Official-, OneStore- och StreamedPackages-layouterna.
- Läsa ICAO för Asobos inbyggda flygplatser ur paketnamnet.
- Tolka **både `.ini` och `.py`** i profilerna (stands, pushback, fordons- och passagerarvägar). Positioner som finns i båda listas en gång.
- En flygplats i flera mappar blir **en rad med "+N"**, vilket ger dubblettvarning på köpet.
- **"Profile only"-rader**, alltså profiler utan matchande scenery.
- **Dra och släpp** en profilfil för att installera den, med fråga innan något skrivs över.
- Status Installed / Missing / Ignored med filter för kontinent, land och typ, plus sök.
- Djuplänk till flightsim.to med ICAO förifylld under GSX Profiles. Det bekräftar att det **inte finns något API för nedladdning**.
- En profil-analysator på kartan som visar stands och pushback-vägar. Det är nice to have.
- Kortkommandon: F5 för att skanna om, `/` för att söka och Esc för att rensa.

**Fallgropar att undvika** (från kommentarerna)
- Scenery som inte är flygplatser (trädborttagning, statiska objekt) klassas som flygplatser. Klassa i stället utifrån **airport-poster i BGL**, inte namnet.
- Flygplatser försvinner mellan versionerna. Det kräver robust skanning och tester mot en riktig Community-mapp.
- Profiler som följer med i en scenery-mapp skannas inte. Vi ska skanna dem.
- Dolda flygplatser går inte att återställa i gränssnittet. En "Visa dolda" behövs.
- En karta som kräver en API-nyckel för att visa något. Välj kart-tiles som fungerar utan nyckel.

**Källkod och licens:** https://github.com/DaveGaming01/davesgsxprofilemanagersource. Licensen är **GPL-3.0**. Koden är skriven i TypeScript med Electron, Vite och Tailwind, plus en `worker`-del.
- Idéer, filformat och sökvägar är inte upphovsrättsskyddade och får användas fritt.
- Att kopiera eller **översätta** kod till C# räknas som ett härlett verk. Om appen sedan distribueras måste den då också vara GPL-3.0, med öppen källkod.
- Två vägar: (a) läs koden som referens och skriv egen kod, så att vi kan välja licens fritt, eller (b) licensiera vår app som GPL-3.0 och låna fritt. **Beslutat 2026-10-25: MIT, väg (a).** Daves kod används bara som referens.
- Särskilt intressant att läsa: tolkningen av GSX-profilernas `.ini` och `.py`, skanningen av `UserCfg.opt` och mapplayouterna, och regeln för ICAO i Asobos paketnamn.

## Krav (beslutade)

- **Plattform:** .NET 10 som minimum (.NET 11 släpps snart – sikta på att kunna lyfta).
- **Repo:** https://github.com/nord-/modern-msfs-addon-manager (publikt, MIT).
- **MSFS 2020: must have.**
- **MSFS 2024: nice to have, out of scope för MVP.** (Designa så att sim-specifika delar kan bytas – sökvägar, content-format, inbyggda flygplatser.)
- **Begränsning:** MSFS 2020 läser in Community-paketen en gång vid uppstart, så av/på slår igenom först vid nästa start. Varken BOB eller Addons Linker kommer runt det. Appen bör upptäcka att simmen körs och visa "slår igenom vid nästa start" (som BOB gör). Den ska också undvika att ta bort länkar under körning, eftersom simmen kan ha filer öppna.

## Komponenter och bedömd svårighet

| Del | Svårighet | Anteckning |
|---|---|---|
| Bibliotek + av/på | Låg | Läs `manifest.json` (title, creator, content_type, package_version). Av/på = skapa/ta bort **directory junction** (kräver inte admin, till skillnad från symlinks). Måste kunna läsa och respektera befintliga junctions från Addons Linker så att migrering blir smärtfri. |
| Presets | Låg | Namngivna uppsättningar aktiva addons. |
| Community-inventering | Låg–medel | Klassa varje post: vår länk / främmande länk / symlink / riktig mapp / trasig / skräpfil. |
| Karta – installerade flygplatser | Medel | ICAO + koordinater ur addonens BGL-filer. Formatet är kartlagt (bl.a. Little Navmap, open source). |
| Karta – inbyggda flygplatser (2020) | Medel–hög | Bas-BGL:er ligger på disk i 2020 (Official/OneStore resp. Steam) och kan skannas. Alternativ/komplement: OurAirports-data för koordinater. (2024 strömmar → SimConnect facilities, men det är utanför MVP.) |
| GSX-profiler | Låg | Matcha ICAO från filnamn i `%APPDATA%\Virtuali\GSX\MSFS`. Visa Installed / Missing / Two profiles. |
| Sök oinstallerad scenery | Oklar | Inget officiellt API hos flightsim.to / SimMarket / Marketplace; scraping är skört och sannolikt mot villkoren. **MVP: djuplänkar** – klick på flygplats utan scenery öppnar sökning på flightsim.to/SimMarket för ICAO. |
| Snyggt gränssnitt | Medel | Tar mest tid i praktiken. |

**Tidsbedömning:** MVP (bibliotek, av/på, presets, karta installerade + inbyggda, GSX-markering, djuplänkar) ≈ 3–6 veckors fokuserat kvällsarbete. Polerad version (bilder, dubblettkontroll, liveries, robust hantering av trasiga länkar) ungefär lika mycket till.

## Teknikval (förslag)

- **C# / .NET 10**, **WPF** + **WPF-UI** (Fluent, mörkt tema). Alternativ att väga: WinUI 3 eller Avalonia.
- **Karta:** WebView2 + Leaflet med OSM-tiles (mörk stil, t.ex. CartoDB Dark Matter) – klarar tusentals markörer.
- **SimConnect:** officiell managed wrapper (behövs först för t.ex. teleport/inbyggda flygplatser i 2024).
- Avfärdat: Tauri (SimConnect från Rust är krångligt), Electron (tungt).

## Öppna frågor till nästa session

1. Ska vi ta över befintligt lager `C:\Spel\Addons` som det är (mappstrukturen = kategorier), eller ha egen metadata/taggar ovanpå?
2. Hur behandla främmande junctions (Orbx, FSDT, Neofly) – visa och tillåta av/på, eller bara visa?
3. Riktiga mappar i Community (Navigraph m.fl.) – "always on", eller erbjuda att flytta ut?
4. Ska per-flygning-aktivering (à la BOB/SimBrief) med i MVP eller senare?
5. Bilder/thumbnails: från addonens `ContentInfo`/thumbnail-filer, eller egen katalog?
6. Börja med `superpowers:brainstorming` → spec → plan innan kod.
