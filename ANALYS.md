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
- Addon-lager: `C:\Sim\Addons\<kategorimappar>`.

Innehåll i Community (118 poster):

| Typ | Antal | Kommentar |
|---|---|---|
| **Junction** | 95 | Addons Linker använder junctions. Flertalet pekar in i `C:\Sim\Addons\…` |
| Junction utanför `C:\Sim\Addons` | 19 av de 95 | Orbx Library (`C:\Sim\Orbx Library\msfs\…\Community\…`), FSDT Addon Manager (GSX), Neofly |
| **SymbolicLink** | 3 | SayIntentions (skapade av SayIntentions egen installer). En är **trasig**: `SayIntentions-VR-Toolbar-Plugin` |
| Riktiga mappar | 19 | Navigraph, FlyByWire installer, FSUIPC/MobiFlight-moduler, Carenado m.fl. – installerade direkt av respektive installer |
| Fil | 1 | `UNINSTALL_CARENADO_PA28R_181_MSFS2020.exe` (skräp i Community) |

Observationer:
- **Svaret på frågan: det är junctions**, inte symlinks (förutom SayIntentions tre).
- Neofly-paket och `bagolu-c152-neoflybannertow` finns **dubbelt**: både i `C:\Sim\Addons\{Tools,Aircraft}` och i `C:\Sim\Neofly\addons`, och Community pekar på Neofly-kopian.
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
- **Snabb start är ett fokus.** All skanning av kataloger och BGL-filer sker på en bakgrundstråd. Gränssnittet ska gå att använda direkt.
- **Prioritering av addon-typer:** 1) **Scenery** (huvudfokus: flygplatser, karta, GSX), 2) **Aircraft**, 3) allt annat samlas under **"Övrigt"** (bibliotek, verktyg, moduler, AI-trafik osv.), med enkel av/på men ingen specialbehandling.
- **MSFS 2024: nice to have, out of scope för MVP.** (Designa så att sim-specifika delar kan bytas – sökvägar, content-format, inbyggda flygplatser.)
- **Begränsning:** MSFS 2020 läser in Community-paketen en gång vid uppstart, så av/på slår igenom först vid nästa start. Varken BOB eller Addons Linker kommer runt det. Appen bör upptäcka att simmen körs och visa "slår igenom vid nästa start" (som BOB gör). Den ska också undvika att ta bort länkar under körning, eftersom simmen kan ha filer öppna.

## Designbeslut (grilling 2026-10-25)

Termerna definieras i [GLOSSARY.md](GLOSSARY.md).

**Reviderad modell (2026-10-25, efter andra Fable-granskningen och förtydligande av Rickard):**
- **Package** är det sim:en laddar och det enda som har ett eget av/på-läge (användarens val, sparat).
- **Addon** är ett eller flera paket som hör ihop, t.ex. "EHAM Schiphol" med tre paket. Varje paket tillhör **exakt en** Addon. Addon är lövet i trädet, och paketen är egenskaper på Addonen, inte noder.
- **Catalog** är användarens egen gruppering i appen, t.ex. "Europe", och kan ligga i flera nivåer. Vid första starten speglar den Library-mapparna, men sedan är den fri från disken. Mappar med flera paket (t.ex. `SKIATHOS V1.2`) blir Catalogs.
- Av/på går på alla nivåer: Catalog (allt under), Addon (alla dess paket) och enskilt paket. Addons och Catalogs visar På, Av eller **Mixed**, uträknat från paketen. Begreppen "egen Addon", "grupperad Addon" och Partial är strukna.
- Från början blir paket som hör till samma flygplats **en Addon per flygplats**. Flygplan blir en Addon per flygplan, och allt annat ett paket per Addon. Användaren kan sedan slå ihop, dela upp och flytta fritt.
- **Uses:** en Addon kan använda en annan utan att innehålla den, t.ex. en flygplats som använder `library1v14-cbj`. Det används visas nedtonat och går inte att slå av eller på därifrån. Relationen hittas via `dependencies` eller sätts manuellt. Appen varnar när en Addon är på men något den använder är av.
- **Struktur:** Scenery (flikarna Airports, Landscapes och Other för Asset packs), Aircraft och Other. Varje flik har sitt eget katalogträd. Vyerna "Mappar/Flygplatser" är ersatta av detta.
- **Managed elsewhere** är en flagga per paket (kan sättas för ett helt Library) och skyddar länken mot alla ändringar. Vid första starten föreslås FSDT-, Orbx- och Neofly-Libraries, men inte förvalt.
- Vad ett klick gör var bestäms när gränssnittet designas, inte i specen.
- **Tröskel**: ett paket med airport-poster för fler än 10 flygplatser räknas inte som Airport scenery som standard (t.ex. World of Jetways med 897 och Nordics POI med 87), plus en undantagslista (2026-10-25).
- **Undersökningspunkt:** läser GSX Pro profiler som följer med scenery-paket? Tills vidare visas de som "medföljer scenery, ej installerad", och bara GSX-mappen räknas för Two profiles.
- Category härleds från `content_type` i manifestet och kan åsidosättas manuellt: **Scenery**, **Aircraft** eller **Other**.
- Scenery delas i flikarna **Airports**, **Landscapes** och **Other** (Asset packs), se ovan.
- Ett Package hör till en Airport om (1) en BGL-fil innehåller en airport-post för koden, (2) namnet eller titeln innehåller koden som ett eget ord, eller (3) användaren har kopplat det manuellt. Automatiska kopplingar kan tas bort. Ett paket kan höra till flera Airports. Geografisk närhet kan komma senare.
  - Exempel: `brickbybrickdesign-airport-eham-amsterdam`, `duijffmedia-airport-eham-amsterdam` och `duijffmedia-schiphol-amsterdam-mega-enhancement-lighting` hamnar alla under EHAM. Det är tre lager som kompletterar varandra, inte konkurrerar.
- Flygplats-Addonen innehåller alla paket som hör till flygplatsen (t.ex. EHAM:s tre lager).
- En Airport identifieras med sin ICAO-kod, eller med sin lokala ident när ICAO saknas (`OG20`, `U60`, `MA33`).
- **Aircraft:** en Addon per flygplan. Flygplanspaketet, dess liveries och mods ingår, och de kan slås av och på ett och ett i Addonens egenskaper. Appen varnar när en livery är på men flygplanet (ett Library-paket) är av.

**Libraries och Community**
- Ett **Library** är en rotmapp som användaren registrerar. Den skannas rekursivt, appen flyttar aldrig filer och mappstrukturen påverkar inte logiken.
- Varje post i Community klassas som Managed, External, Fixed, Ignored, Broken eller Stray.
- Sim-managed packages (i `Official`) läses men ändras aldrig. **Rättelse 2026-10-25:** mapparna direkt i Packages-roten är inga paket utan simmens WASM-cache (bara `work` och kompilerade filer, inget `manifest.json`), och de ignoreras. Från Sim-managed packages visas scenery. Flygplan därifrån går aldrig att slå av eller på, men visas som låst bas för liveries och mods.
- Av/på görs med directory junctions, som inte kräver admin.
- **Första starten:** appen hittar Community via `InstalledPackagesPath` i `UserCfg.opt` (både Steam och Store), analyserar vart befintliga junctions pekar och föreslår Libraries utifrån det. Användaren bekräftar och kan lägga till fler efter den första skanningen.
- **Duplicate copy**: samma Package i flera Libraries. Appen varnar och låter användaren välja vilken kopia som länkas. Förvalt är den som Community redan pekar på.

**State och avstämning**
- Hybridmodell: appen har eget state men stämmer av mot Community vid start. Vid avvikelse visas en synk-dialog där riktningen väljs per rad, och det finns knappar för att välja samma riktning för alla. "Behåll Community" är förvalt.
- Användardata (Libraries, Addons, kopplingar, dolda objekt, åsidosatta kategorier) sparas som JSON i `%APPDATA%`. Skanningscachen (paket, flygplatser, thumbnails) sparas i SQLite i `%LOCALAPPDATA%`.

**Varningar**
- Bara **Duplicate package** varnas, alltså samma paket både i Community och i `Official`. (ESGG/ESSA i Packages-roten var ett felaktigt exempel, se rättelsen ovan.) Plus Duplicate copy och livery utan flygplan, enligt ovan.
- Flera paket som hör till samma Airport visas neutralt ("3 paket"). Ett paket som ersätter eller patchar officiell scenery visas som "ersätter inbyggd", utan varning. Det känns igen på `dependencies` eller på filöverlapp i `layout.json`.

**Prestanda och bakgrundsarbete**
- Senast sparat läge visas direkt vid start. Avstämningen mot Community körs först i bakgrunden, därefter BGL-skanningen. Gränssnittet uppdateras efter hand.
- Skanningen är inkrementell och bygger på sökväg och tidsstämpel eller storlek på `manifest.json` och `layout.json`. Full omskanning görs med F5.
- Första skanningen sker i ordningen Community → Libraries → Sim-managed packages, med förloppsindikator.
- Av/på fungerar alltid, även under skanning.
- Community och alla Libraries bevakas, och berörda paket skannas om i bakgrunden.
- Medan MSFS körs köas alla ändringar och visas som "väntar". De verkställs automatiskt när sim:en stängs.

**Karta, GSX och sök**
- Kartan visar som standard flygplatser med riktig scenery, alltså Addons plus Asobos handgjorda. "Visa alla flygplatser" tar fram de generiska, med klustring.
- GSX: status per Airport (Installed / Missing / Two profiles) plus Profile only. Profiler läses från `%APPDATA%\Virtuali\GSX\MSFS` och från scenery-mappar med egna profiler. Profiler installeras med dra och släpp, och det finns en djuplänk till flightsim.to för profiler som saknas. Status visas bara för Airports med riktig scenery.
- Sök efter scenery som inte är installerad: djuplänkar till flightsim.to och SimMarket som konfigurerbara URL-mallar.
- Thumbnails tas från paketets egna filer, med platshållare per typ som reserv.

**Teknik och leverans**
- C# / .NET 10, WPF + WPF-UI, karta med WebView2 + Leaflet och tiles utan API-nyckel.
- Gränssnittet är **bara på engelska**, utan stöd för flera språk.
- **Bara mörkt tema.**
- Distribueras som **framework-dependent single-file exe** via GitHub Releases. .NET 10 Desktop Runtime följer inte med. Saknas den visar .NET:s egen startfil en dialog med länk till nedladdning från Microsoft. Appen visar en notis när det finns en ny release, men uppdaterar sig inte automatiskt.

**Utanför MVP:n**
- MSFS 2024 (men sim-specifika delar ska gå att byta).
- Presets.
- Aktivering per flygning (à la BOB och SimBrief).
- Installation från zip-arkiv.
- Daves profilanalysator på kartan.
- Geografisk koppling av scenery till Airport.
- Sök med fler butiker än flightsim.to och SimMarket.

## Beslut för plan 2, Content scan (grilling 2026-10-26)

- **SQLite:** `Microsoft.Data.Sqlite` med rå SQL, utan EF Core eller Dapper. Schemat har ett versionsnummer. Stämmer det inte raderas cachen och allt skannas om. Single-file-verifieringen hör till plan 6.
- **BGL-fixtures:** syntetiska BGL-filer som byggs i kod (`BglBuilder`), med **riktiga filer som förlaga** (Rickard). Ett CLI-kommando `bgl-dump` skriver ut strukturen hos riktiga filer som JSON, utan råa bytes. Dumparna checkas in, och ett test kontrollerar att byggaren ger samma struktur. Förlagor: en BGL ur `fs-base-genericairports`, `brickbybrickdesign-airport-eham-amsterdam`, sltcreations `YALR1`, en läsbar `asobo-airport-*` och `asobo-airport-lfcs-saucats`. Riktiga filer testas dessutom i ett opt-in-integrationstest.
- **Asset pack-testet** bestäms i plan 3, eftersom det hänger på Uses. Plan 2 sparar vilka BGL-sektionstyper varje Package har.
- **Collision report:** en ren lista med generic Airport codes exporteras från Rickards installation och checkas in. Koder är fakta, inga sim-filer, så testet kan köras i CI.
- **Fixture-utökning:** fakta per Package (airport records, om BGL finns, containers, `base_container`, `isAirTraffic`, `SimObjects`-sökvägar ur `layout.json`) för Library, Community och Sim-managed packages. Exporten görs med CLI-kommandot `export-content`, som använder plan 2:s egna läsare.
- **Två manuella steg i planen**, eftersom simmen inte finns på utvecklingsdatorn: tidigt `bgl-dump`, sent `export-content` plus listan med generic codes.
- **Category:** discovery sätter en preliminär Category från `content_type` (BGL-förekomst läses ur `layout.json`). Content-passet ger den slutliga, med `isAirTraffic`.
- **Användardata:** plan 2 äger det som rör enskilda Packages: manuella och borttagna Airport-länkar, opt-in-paket, tröskel, exclusion list och stop-list. Category-override per Addon hör till plan 3.
- **Thumbnails:** plan 2 sparar bara sökvägen. GSX-profiler läses i plan 4.
- **Glossary:** nya termer **Airport code**, **Generic airport** och **Mod**.
- **Prestanda:** en inkrementell skanning utan ändringar ska ta under 2 sekunder på Rickards maskin, mätt i opt-in-testet. Första fullskanningen har inget mål, bara synlig progress.
- **Antaganden i plan 2 (godkända 2026-10-26):**
  - Härledda resultat (länkar, Airports, aircraft-grupper, antal records) räknas om från cachade fakta och cachas inte (C6); spec 11 uppdateras i Task 14.
  - Regel 2 (token i namn/titel) gäller bara Packages som inte är Sim-managed. Sim-managed Packages utan records använder package-name-regeln (Task 10).
  - `layout.json`-överlappet som signal för Replaces built-in utgår. Dependency-signalen behålls (Task 11).
  - De generiska koderna från beslutet "Collision report" ligger i content-fixturen (en kompakt `codes`-array per exkluderat Package), inte i en separat fil (Task 19).
  - Records från Packages som exkluderas av ett mönster (voloport, neofly, points of interest) ignoreras överallt: inga Airports på kartan och inga kända koder för token-regeln. Records som bara exkluderas av tröskeln ger fortfarande koder, namn och positioner (C4).
  - `OptedIn`, manuella länkar och borttagna länkar gäller även Sim-managed Packages (C5, spec 6.5). Rickard beslutade 2026-10-26: ett Marketplace-flygplats vars BGL:er är krypterade och vars namn saknar `-airport-<kod>` kan länkas för hand, så kartan visar den som inbyggd och ett Library-Package på samma Airport flaggas Replaces built-in.
  - Inaktiva Duplicate-kopior analyseras som aktiva. Plan 3 visar bara den aktiva kopian (C8).
  - Ett krypterat flygplan (`.fsarchive`, ingen `aircraft.cfg`) ger sin container i alla källor, inte bara Sim-managed (Task 12, spec 6.6).
  - Exclusion list och stop-list lagras som användarens tillägg och borttagningar ovanpå inbyggda standardvärden, så nya standardvärden i en senare release når befintliga användare (Task 9).
  - En anropares avbrott stoppar bara dess egen väntan. En delad skanning avbryts först när tjänsten avvecklas (Task 17).
  - Ett Library på en onåbar disk behåller sitt cachade innehåll, så Airport-länkarna försvinner inte medan det är offline (Task 15).
  - En låst cache (andra instansen) gör att cache-instansen kör i minnet resten av sessionen i stället för att misslyckas (Task 14).
  - `isAirTraffic=1` gör ett Package till Other oavsett `content_type` (spec 6.4, Task 2).
  - En External-post skannas under postens namn, det namn simmen laddar den under. En som pekar in i en registrerad Library-kopia skannas en gång, som den Library-kopian (Task 16).
  - `ContentWatcher` får en separat `CommunityChanged`-signal nu. Att reagera på den (spec 7.4 körtidsändringar, egna ändringar undertryckta enligt spec 12) hör till plan 5 (Task 18).
  - Ett Library som saknas när watchern startar bevakas inte förrän plan 5 återskapar watchern efter nästa skanning (Task 18).
  - Package-name-regeln för krypterade officiella flygplatser länkar bara kända koder, så ett Sim-managed namn skapar aldrig en Airport utan namn (Task 10).
  - Real scenery räknar länkar från Packages som inte är exkluderade, plus manuella länkar (Task 11, spec 6.5).
  - Airport-namn som är lokaliseringsnycklar (`TT:…`) räknas som inget namn, så ett vanligt namn från en annan record vinner och en Airport med bara nyckel visar sin kod. Rickard beslutade 2026-10-26 (alternativ C): visar Task 4:s dumpar `TT:`-namn läggs en extra task till som löser dem via `en-US.locPak` före Task 6 (Task 4 och 11).
  - **Lokaliseringsnycklar löses (Task 6a, Rickard 2026-10-26, alternativ C, issue #8):** `TT:`-namn löses via `<Official>\fs-base-genericairports\en-US.locPak` (exakt nyckel, annars koden i nyckeln); en olöst nyckel räknas som inget namn. Fakta från Rickards maskin: `TT:`-namn finns bara i Official Asobo/fs-paket (42 av 106 flygplatspaket), aldrig i Microsoft-paket eller Library; nyckeln skrivs utan `TT:` i locPak-filen; alla 42 observerade nycklar finns i filen (41 exakt, `TT:AIRPORT.CYTZ.name` saknar regiondel och löses via koden mot `AIRPORT0D.CYTZ.name` = `Toronto/City Centre`); filen är 5,7 MB, UTF-8 utan BOM, JSON `{ "LocalisationPackage": { "Language", "Strings": { nyckel: värde } } }` med `\/`-escapes och råa icke-ASCII-tecken; nycklarna har formen `AIRPORT<rr>.<KOD>.<fält>` (`rr` = `0D`, `EK`, `LR`, `SZ`; fält = `name`, `city`, `state`), 43 387 flygplatser, ingen kod under två regioner, inga tomma namn, en nyckel med gemener (`WIEb`). Bara `.name` läses; filen läses lazy en gång per instans.
  - Cachade resultat vid start kommer från själva skanningen (`ScanCallbacks.OnCached`, före discovery), så cachen läses en gång per körning och av en anropare (Task 17).
  - `scan` listar Sim-managed Scenery-Packages utan länk som saknar BGL eller har en oläsbar (krypterade flygplatser som package-name-regeln missade). Task 21 noterar dem i PR:en så de kan länkas för hand när plan 5 har UI:t.
  - Liveries vars flygplan inte är installerat får ändå en grupp, så plan 3 kan varna "Livery without aircraft" (Task 12).
  - Ett Package som ger flera containers ligger i varje grupp. Plan 3 väljer den alfabetiskt första (spec 6.6).
  - Plan 3 äger Asset pack-testet. Plan 2 tillhandahåller `BglSectionTypes` (beslutet "Asset pack-testet").
  - **FSDT:s mapp föreslås aldrig som Library (Rickard 2026-10-27, issue #9):** `…\FSDT Addon Manager\MSFS` innehåller ett backup-träd med 15 000 mappar utan `manifest.json` (`fsdreamteam-gsx-world-of-jetways-2020_backup`) som gjorde varje skanning 2+ s, och FSDT:s Addon Manager skapar och äger sina egna Community-junctions. En junction vars mål ligger under den mappen ger därför inget Library-förslag alls (varken verktygsrot eller steg 2); posterna förblir External. Orbx och Neofly är fortsatt verktygsrötter. `LibraryScanner.Walk` är oförändrad.
  - **Fast storlek för airport-records: bara `0x44` (Rickard 2026-10-27, PR #10):** `BglFile.FindAirportFixedSize` provar inte längre `0x38`, `0x3C`, `0x48` och `0x4C`. Mätt på sim-maskinen över 10 551 BGL-filer (4 oläsbara) och 81 521 airport-records: alla har en ren väg från `0x44` (80 491 bara `0x44`, resten även en eller flera av de andra storlekarna), och barnet vid `0x44` är alltid en riktig record (`0x19` i 80 361 fall, `0x33` i 1 160). De andra storlekarna går bara rent av en slump (1 030 records; "första barnet" där är skräp). "Välj den minsta" hade valt `0x38`/`0x3C` för 1 009 records och tappat deras namn, bland annat `asobo-airport-vnlk-lukla` och `asobo-airport-lflj-courchevel`. En hypotetisk fil med annan storlek ger nu ingen record-layout i stället för ett felaktigt val; `AirportRecordReader.ReadName` ger då inget namn, men kod och position påverkas inte.
  - **Samma-namn-regeln i aircraft-grupper gäller bara Community-kopior (Rickard 2026-10-27, PR #10):** en Community-kopia (External-junction) med samma namn som en Library-leverantör är samma Package och ingen låst bas (10481a6, Neofly). En Sim-managed leverantör är alltid en låst bas, även när ett Library-Package har samma namn; Library-kopian visas då som Mod av den, och hur det visas överlåts till plan 3. På Rickards maskin delar inget Library- eller Community-Package namn med ett Sim-managed (mätt i `rickard-content.json`), så fallet är hypotetiskt här; Neofly-dubbletterna i `C:\Spel\Addons` är borttagna.

## Beslut för gränssnitt och Addons (designsession 2026-10-28)

Rickard stoppade skrivandet av plan 3: gränssnittet styr modellen, och specen hade skjutit interaktionsdesignen till plan 5. Besluten nedan ersätter Catalog-modellen och Library-förslaget. Specen är uppdaterad (6.1, 6.3–6.7, 7.1, 8, 10, 11, 17).

- **Princip:** automatiken föreslår, användaren bestämmer. Allt automatiskt går att överstyra på kortet, och överstyrningen gäller alltid.
- **Navigation:** vänstermeny Scenery, Aircraft och Other, med Settings fäst längst ner. Flikar: Scenery → Airports | Landscapes | Assets; Aircraft → Aircraft | Instruments | Other; Other har inga flikar. Menyval i kategorinamn, flikar i plural ("Aircraft" är redan plural på engelska).
- **Kort:** en Addon visas som ett stort kort med bilden överst, kod och namn (flygplatser) eller Addonens namn. Ingen switch på väggen; ett trafikljus i högra hörnet (rött av, gult blandat, grönt på, grått med lås när inget går att slå om). Väntande ändringar visar "Next sim start". Klick öppnar en dialog (Esc, ✕ eller klick utanför stänger) med en knapp för allt och en switch per paket. Möjlighet att växla kortlayout hamnar under Later.
- **Catalogs utgår.** Mapparna mellan Library-roten och paketet blir **taggar** (en mapp med ett enda paket och verktygslayouter som `…\<produkt>\Community\` ger ingen tagg). Filter, sök och redigering av taggar hamnar under Later.
- **Library-typer:** ett Library har en eller flera typer (Scenery, Aircraft, Other). Appen skannar alltid allt; innehållet avgör menyvalet och Library-typen avgör om paketet visas där. Rickard: "Typen av addon styr alltid om den syns eller inte." Paket vars Library saknar typen listas under Settings → Not shown med en genväg för att lägga till typen.
- **Första start:** användaren lägger till Libraries själv med typ; inga förslag. Junction-analysen visar bara täckningen ("87 of 95 links covered"). Rickard organiserar själv per typ (`C:\Sim\Addons\Scenery`, `C:\Sim\Addons\Aircraft`), men förslag fungerar dåligt för den som har allt i en mapp.
- **Inbyggt visas som kort bara när användaren har ett tillägg för det:** ett paket som ersätter flygplatsen, en livery eller mod, eller en GSX-profil ("En GSX-profil är också en sorts addon"). Annars syns inbyggda flygplatser bara på kartan.
- **Kort per flygplats** (EHAM med tre paket = ett kort). Ett paket med flera flygplatskoder går till den kod som står som eget ord i namnet; annars eget kort med "(several)". Exempel: `sltcreations-airport-yq05-mount-mulligan` har YQ05 och YMUZ (Mount Mulligan Lodge) och blir YQ05; `asf-mission-demo` (FEFA, FEGK) blir "(several)". `tag21-airport-ybas-alice-springs` har två YBAS-poster men bara en kod.
- **Landscapes och Assets:** ett kort per paket; manuell sammanslagning.
- **Aircraft:** ett kort per flygplan med liveries och mods inuti. `content_type: INSTRUMENTS` → fliken Instruments (`pms50-instrument-gtn750-pro`, `sky4sim-os`); Navigraph-avioniken är `MISC` och innehåller navdata, inte instrument, så den hamnar under Other. Aircraft-paket utan flygplan stannar under Aircraft → Other.
- **Asset pack-testet:** inga BGL-sektioner med placerade objekt (`0x25`) eller terräng (`0x65`). Ger fem paket på Rickards maskin; `human-library-animated` flyttas för hand till Assets.
- **`isAirTraffic=1` → Other**, även `apronlife-jetway-helper` (jetbryggor placeras som trafik i MSFS). Rickard: hjälparen passar bäst under Other.
- **Dra och släpp:** kort på kort slår ihop (målet vinner: namn, bild, kod, flik); kort på flik eller menyval flyttar; "Move out" i dialogen delar upp. Alla kort kan slås ihop, även två flygplatskort och inbyggda kort.
- **Redigerbart i dialogen, med direktsparning:** namn, bild (ett pakets thumbnail eller en egen fil; bara sökvägen sparas) och ICAO. Alla koder med giltigt format accepteras; en okänd kod ger ingen kartmarkör (position hamnar under Later). ~~En kod som redan har ett kort slår ihop korten.~~ Ersatt 2026-10-29: att skriva en ICAO-kod slår aldrig ihop kort (se kortmodellen nedan).
- **Kartan** är en vyväxling `Cards | Map` på Scenery → Airports.
- **GSX** visas på kort och i dialogen; GSX-tabellen utgår. Installation med dra och släpp och av/på för profiler hamnar under Later.
- **Community-vyn** ligger under Settings; trasiga länkar och lösa filer visas i en banner.
- **Uses**, inklusive varningen "Used Addon off", hamnar under Later. Hide/Show hidden och Delete from disk likaså.
- **Lagring (ersatt 2026-10-29 av en SQLite-fil, se kortmodellen nedan):** användardatan stannar i JSON (`userdata.json`), skild från SQLite-cachen. Rickard utmanade beslutet; argumenten: sökningen körs ändå i minnet mot härledda data (C6), datamängden är liten, JSON går att läsa och rädda för hand, och cachen raderas vid formatbyte. Den enda verkliga vinsten med SQLite, låsning mellan två instanser, löses med en mutex: appen tillåter en instans åt gången.
- **Managed elsewhere utgår ur MVP:n** (spec-granskning runda 3). Rickard: "jag lägger aldrig till ett lib hos oss som är 'Managed elsewhere'? Vad skulle syftet vara för mig att lägga till `C:\Sim\Addons\Neofly` om jag inte har för avsikt att hantera de Addons:en i den här appen?" Allt i en registrerad Library går att slå av och på, även `C:\Sim\Orbx` där Orbx Central laddar ner och skapar egna junctions. Rickard: "Vill jag stänga av en flygplats från `C:\Sim\Orbx`, t.ex. 'ESGG', så kan jag göra det från antingen vår app eller från OrbX." Orbx ändringar fångas av avstämningen mot Community. Mappar som inte registreras (FSDT, Neofly) är External och visas låsta. Ersätter flaggan i designbesluten 2026-10-25.
- **Nästlade Libraries är tillåtna:** innehållet avgör menyn och typen bara synligheten, så ett paket får typerna från alla Libraries det ligger i.
- **Ny färdplan:** plan 3 = Addons (kort, Library-typer och synlighet, flikar, taggar, dina ändringar, trafikljus, varningar, mutex, CLI `addons` med ett ready-for-human-issue); plan 4 = GSX (läsa, status, profil-kort); plan 5 = WPF-appen enligt denna design; plan 6 = karta och release.

## Beslut för plan 3 (2026-10-28)

Val som gjordes när plan 3 (Addons) skrevs och implementerades. Specen är uppdaterad där valen ändrar den (avsnitt 6.1, 6.4, 7.1, 7.4 och 16).

- **Library-typer (ersatt 2026-10-29 av en SQLite-fil och sektioner per Library, se kortmodellen nedan)** lagras på `LibraryRoot` som en lista av `Category`. En gammal `userdata.json` laddas med alla tre typerna och Managed elsewhere-medlemmarna ignoreras, så ingen migrering och ingen schemaändring behövs. Det är säkert eftersom inget släppt hittills skriver `userdata.json` (CLI:t sparar aldrig och appen finns inte än).
- **En sammanslagning fäster målets utseende men är inte en flytt:** synligheten följer fortfarande Libraries typer om inte användaren flyttat kortet (`Overrides.Moved`, som sätts av Move, flytten från Not shown och när en kod skrivs in).
- **Ett Package med någon oläsbar BGL** faller på Asset pack-testet och startar i Landscapes (krypterad scenery kan placera objekt).
- **Ett Library-Package som länkas till en annan kopia** än den lagrade aktiva kopian ger en `LinkRetargeted`-rad i synkdialogen vid registrering, i stället för att tyst behålla eller skriva över.
- **Namn som hålls av en Fixed- eller External-post** förblir `RejectReason.NameTaken` i `LinkPlanner`; kortet kallar det låst. `RejectReason.Locked` är borttagen.
- **En Library-länk som ett annat verktyg ändrar** i ett registrerat Library ger nu en vanlig synkrad (Managed elsewhere följde den tyst förut).
- **`LinkCoverage`** räknar FSDT:s junctions som "inte räknade" tillsammans med symlänkar och trasiga länkar. På Rickards maskin: 87 av 91 täckta med `C:\Sim\Addons` och `C:\Sim\Orbx Library\msfs`, 7 inte räknade.
- **`RegisterLibrary`** adopterar via "har en kopia under Librarys sökväg" och hoppar över väntande Packages; `UnregisterLibrary` ändrar bara listan.
- **Synkdialogens självkontroll** hoppar över ett Package vars namn en Fixed- eller External-post håller (det är låst och räknas som av), så avregistrering av ett Library vars Package har en kopia någon annanstans ger ingen rad.
- **Att skriva eller rensa en kod** markerar kortet `CodeEdited`, vilket räknas som rört, så en omskanning förankrar aldrig ett kort vars kod användaren redigerat. En bildöverstyrning ensam rör inte kortet (spec 11 listar flyttat, sammanslaget, omdöpt, fått kod).
- **Två orörda kort som skulle få samma kod** får ingen av dem; användaren slår ihop dem.
- **Ett Package vars lagrade kopia är offline** visas som Offline även när en annan kopia är nåbar, i linje med apply-vägen.
- **Ett försvunnet Fixed- eller External-Package** blir kvar som Missing i sin Addons dialog; ett som inte låg i någon Addon försvinner.
- **En länk in i ett onåbart Library** vars Package ingen slutförd skanning har sett (disken försvann under första skanningen) får inget kort. Settings → Community listar den som Offline, och den får sitt kort när disken är tillbaka och en skanning slutförts (Rickard, 2026-10-28).
- **"Grouping cards…"** räknar provisoriska kort bara tills en skanning slutförts; Packages som en slutförd skanning inte kunde läsa nämns i bannern i stället och förblir provisoriska.
- **En synkrad för ett namn som en Fixed- eller External-post nu håller** erbjuder bara Keep Community.
- **Aktiv kopia kan inte ändras** medan en Fixed- eller External-post håller Packagets namn (apply-vägen avvisar det som NameTaken).
- **Minnet av ett Packages senaste fakta** (`UserState.LastSeen`) skrivs efter varje slutförd skanning, för varje hittat Package, även Community-Packages.
- **Aircraft-gruppering:** Providers, Mods och låsta baser går samman via delade containrar. En Livery eller ett Attached Package vars container hålls av en Provider-, Mod- eller låst-bas-grupp går med i gruppen för den första sådana containern (efter namn) och bidrar inte med egna containrar; först när ingen av dess containrar hålls bildar de en egen grupp. Ett liverypaket som täcker två flygplan slår alltså aldrig ihop dem, och en livery vars container ingen levererar förankras inte förrän en leverantör dyker upp. En grupps anchor är alla dess containrar sorterade efter namn.
- **Minnet av ett Packages senaste fakta** sparar också namnen på dess flygplatskoder, så ett kort vars Airport-Package är Missing behåller sitt namn ("EHAM Schiphol").
- **Ett nytt Package som går med i en befintlig Addon** läggs sist bland dess Packages; den lagrade ordningen ordnas aldrig om.
- **"(several)" och länkade koder** beräknas, de lagras inte.
- **Ett kort vars Airport är okänd** får namn efter sitt första Package, inte efter sin kod.
- **Ett provisoriskt kort** är kortet för ett Package som ligger i ingen Addon, inte ett eget begrepp i lagringen. Det följer samma synlighetsregel som lagrade kort (det kan vara Not shown) och visar länkade koder och "(several)" som andra kort.
- **Inbyggda flygplatsdelar** fäster bara vid ett befintligt kort; beräknade (GSX) kort hör till plan 4.
- **`addons` (heter `cards` sedan 2026-10-30, se plan 3b nedan)** adopterar Community i minnet när ingen snapshot finns, så det visar på/av på en maskin där appen aldrig körts, och sparar ingenting.
- **Inget CLI-testprojekt:** `--library`-parsern bor i Infrastructure (`LibraryArgument`) och enhetstestas där; Task 20 kör den också med typer.
- **Brädets analys** byggs om med de aktuella länkinställningarna efter en kodändring (`BoardInput.Reanalyze`) i stället för att vänta på nästa skanning.
- **Spec 6.4 kallar `navigraph-navdata` och `navigraph-navdata-base` External-poster;** på Rickards maskin är de riktiga mappar i Community (Fixed). Båda är låsta på samma sätt, så kortväggen påverkas inte.
- **Implementationen körs på branchen `feat/addons`** (avgrenad från planbranchen), så plancommitsen följer med i implementations-PR:en.
- **Kort med samma titel (2026-10-29, sim-körningen i #15):** två synliga kort kan få samma namn (två "My Company Aerial", två "Windy Things Library") och skiljs då bara åt av taggar. Rickard valde att plan 5 löser det i kortväggen: när titlar krockar visar kortet även paketnamnet (t.ex. som undertitel). Core ändras inte i plan 3.

## Beslut för kortmodellen (2026-10-29)

Granskningen av PR #16 visade att sammanslagning byggde på ankarlistor som växer och matchar värden (en ICAO-kod som skrevs in slog ihop korten). Rickard: "vi kan inte bygga ett stabilt hus på en skakig grund." Modellen byggs om i samma PR. Specen är uppdaterad (6.1–6.7, 7.1, 8, 10, 11, 13, 16, 17) och GLOSSARY.md likaså.

- **Card och Package:** Card är gruppen och lagras med id, sektion, flik, ICAO-koder (bara Scenery) och användarens namn och bild. Package är mappen (eller en GSX-profil) och har en FK till sitt kort. Termen Addon utgår. Rickard: "Varför inte ha en Card-entitet som har en typ … Varje paket … måste ha en Card-entitet som förälder för att synas."
- **Scenery-, Aircraft- och Other-kort är olika sorter.** Bara Scenery-kort har ICAO-kod. Rickard: "Other-kort har inte samma metadata som Scenery-kort eller Aircraft-kort har, så ICAO ska inte gå att sätta på varken ett Other- eller Aircraft-kort."
- **Sammanslagning sker bara med dra och släpp,** inom samma sektion och flik. Målet behåller namn, bild och flik; det dragna kortets paket flyttas och det tomma kortet tas bort. En ICAO-kod som ett annat kort redan har avvisas i ICAO-kod-fältet, den slår aldrig ihop. ~~Efter att EHRD slagits ihop med EHAM får ett senare EHRD-paket ett eget kort.~~ Ersatt i spec-granskning runda 2: målet får även det dragna kortets ICAO-koder, så ett senare EHRD-paket går till det sammanslagna kortet. Rickard: "beslutet från avsnitt 1 togs innan vi bestämde att ett kort kan ha flera koder." Ersätter "En kod som redan har ett kort slår ihop korten" i designbesluten 2026-10-28.
- **Flytt till en annan sektion** gör kortet till den sektionens sort, och det hamnar på fliken som reglerna ger där. Ett kort som lämnar Scenery förlorar sina ICAO-koder. Paketen behåller sin egen sektion.
- **Ett pakets sektion låses** när en fullständig skanning har läst det. Kort skapas aldrig från den snabba skanningen. Rickard: "Vi ska aldrig lägga till nya paket baserat på den snabba skanningen."
- **Libraries hör till en sektion,** inte tvärtom. Rickard: "ett Library kan aldrig ha en typ, det är åt fel håll. Scenery t.ex. kan ha flera Libraries (rot-sökvägar)." En sektions Libraries avgör bara om ett paket får ett kort automatiskt; övriga listas under Not shown. Ett befintligt kort syns alltid. En varning när ett Library tas bort från en sektion hamnar under Later.
- **Scenery:** paket med samma enda ICAO-kod delar kort. Ett paket med flera ICAO-koder får alltid ett "(several)"-kort som listar alla, och namnet tas från manifestets titel (`sltcreations-airport-yq05-mount-mulligan` → "YQ05 Mount Mulligan" med YQ05 och YMUZ). Token-regeln som valde en ICAO-kod och linked codes utgår. Ett nytt paket går till kortet med exakt samma ICAO-koder.
- **Aircraft grupperas på Container.** `icao_type_designator` läses ur `aircraft.cfg` och visas som tagg på kortet. Rickard: "vi ska fortfarande läsa ur icao_type_designator, men flygplanen grupperas på container." Liveries följer `base_container`.
- **Other** grupperas aldrig automatiskt.
- **En GSX-profil är ett paket.** Rickard: "GSX-profil är som vilket annat paket som helst … På samma sätt ska vi göra för GSX, och skapa ett Airport-kort." Beräknade kort utgår.
- **Kartan** tar markörerna från paketens flygplatsposter, oavsett vilken sektion kortet ligger i. Rickard: "är det en flygplats enligt paketets data så ska den visas upp på kartan."
- **Not found (alternativ B):** användaren avgör vad som tas bort. Ett paket vars Library svarar men vars mapp är borta stannar på kortet med all sin data och får varningen Not found; knappen Remove glömmer det. Rickard: "I fallet med 'EHAM 1.3' -> 'EHAM 1.5' skulle jag tappa all customization för EHAM med A. Med B ligger kortet kvar och skanningen upptäcker 'EHAM 1.5' … Avinstallerar jag 'EHAM 1.3' så får jag ett kort med ett paket som har en sökväg som inte finns längre --> varning." Massborttagning hamnar under Later.
- **Library not found:** svarar inte roten alls ändras ingenting, och bannern säger det. Ett läsfel under skanningen ger ett oläsbart paket, aldrig Not found. Offline- och Missing-maskineriet per paket från plan 3 (som Kjell lade till på eget initiativ i spec-granskningen) ersätts av detta.
- **Lagring i en SQLite-fil:** `%LOCALAPPDATA%\ModernMsfsAddonManager\userdata.db` med användartabeller och cachetabeller, var för sig versionerade. Rickard: "Nu börjar vi bygga relationer mellan entiteter, med FK:s i databasen" och "Allt i samma db-fil." En ny cacheversion bygger bara om cachetabellerna. Ersätter lagringsbeslutet i designbesluten 2026-10-28 och spec 11:s "User data never lives in the cache file". Ingen flytt från `userdata.json` behövs, eftersom ingen sådan fil finns.
- **CLI:t är inte en del av produkten,** utan ett utvecklarverktyg för fixturexport och kontroll mot den riktiga maskinen.
- **Ett paket utan exakt träff** går till det äldsta kortet vars ICAO-koder innehåller alla paketets ICAO-koder (spec-granskning runda 1 för en ICAO-kod, utvidgat i runda 2 till flera; Rickard valde A båda gångerna). En uppdatering med en ICAO-kod som inget kort har får ett nytt kort, och dubbletten försvinner för gott när användaren drar ihop den eftersom sammanslagningen tar med ICAO-koderna. Annars skulle "OrbX EHAM 1.5" få ett nytt kort så fort användaren lagt till EHRD på EHAM-kortet. Följd: ett paket med bara YQ05 går till "(several)"-kortet när inget YQ05-kort finns.
- **Ett paket som håller på att installeras lämnas i fred** (spec-granskning runda 1, Rickard valde A): kort skapas, och sektionen låses, först när paketets mapp varit orörd några sekunder efter den senaste ändringen bevakarna såg. En uppackning kan skriva `manifest.json` före BGL-filerna, och en skanning däremellan skulle låsa ett Scenery-paket som Other.

## Beslut för plan 3b (2026-10-30)

Plan 3b bygger kortmodellen. Besluten nedan ska Rickard kontrollera.

- **Tystnadstid:** 10 sekunder (`ContentScanner.QuietPeriod`). Ett paket som skrevs medan mappen inte var orörd cachas inte och får varken sektion eller kort förrän en senare körning läser det i lugn och ro.
- **Låst fil under en skanning** gör att körningen misslyckas med `UserDataLockedException`. Nästa bevakarhändelse, schemalagda omskanning eller F5 återupptar inkrementellt, och det är så skanningen "pausar".
- **Migreringskopior** heter `userdata.v<från>-<yyyyMMddHHmmss>.db.bak`, så en omförsökt migrering skriver aldrig över en tidigare kopia.
- **`SqliteUserStateStore.Save`** skriver om alla användarrader i en transaktion (några hundra rader), utan diffning per rad.
- **Utvecklar-CLI:t:** kommandot `addons` heter nu `cards` och skriver ut kortväggen. Det öppnar databasen med `CacheOnly`, sparar ingenting och lagrar aldrig sektioner, så paket utan lagrad sektion läses om vid varje körning av `scan` och `cards`.
- **GSX-profiler** placeras och lagras som paket här, från en `GsxProfile`-lista som läsaren i plan 4 levererar. Nya profiler i en skanning grupperas med nya mappaket från samma skanning på mängden ICAO-koder (ett nytt EHAM-scenery och en ny `eham.ini` ger ett kort).
- **Provisoriska kort** listas efter flikens lagrade kort, sorterade på paketnamn.
- **Ett paket under en mapp som upptäckten inte kunde läsa** visas som Unreadable på sitt kort (reglaget avstängt, går inte att ta bort, nämns i bannern), aldrig som Not found.
- **"Kort som har ICAO-koderna först, kort som täcker dem genom borttagningar sedan"** (spec 6.5, Rickard 2026-10-29) gäller även exakt träff, inte bara inklusionsmatchningen: ett EHAM-paket går till kortet som har EHAM före ett äldre kort där användaren tagit bort EHAM.
- **Aircraft-kort:** ett paket går först till det äldsta kortet där ett paket tillhandahåller eller är låst bas för en delad Container, och först därefter till ett kort som bara målar om eller kopplas till den. Spec 6.6: en livery för flera flygplan slår aldrig ihop kort.
- **Inbyggda flygplansdelar** visas på kort vars paket tillhandahåller, målar om eller kopplas till Containern (spec 6.4).
- **Behörighet för en menysektion** är unionen av alla kopiors Libraries.
- **En GSX-profil som redan ligger på ett kort** förblir ett paket även när dess ICAO-kod på tre tecken inte längre är känd.
- **Memo-enum lagras som namn.** Ett okänt namn eller ett tal räknas som skada.
- **Plan 4 måste sätta `GsxFolderFound` till true** när GSX inte är installerat, annars visas bannern "GSX folder missing" för evigt. (Ersatt 2026-10-30 av D3 nedan.)

## Beslut för plan 4, GSX (2026-10-30)

Plan 4 matar GSX-profiler in i kortmodellen. Besluten D1–D10 står i `docs/superpowers/plans/2026-10-30-04-gsx.md`; de som ändrar spec eller en tidigare anteckning:

- **D2:** GSX-mappen cachas inte. Den läses vid varje skanningskörning och vid start (cirka 40 småfiler). Medföljande profiler cachas som ett faktum om sitt paket (`PackageContent.GsxFiles`, cacheversion 4). Varje board, även startboardet i `ScanCallbacks.OnCached`, måste få `WithGsx(...)`, annars visas alla lagrade profiler som Not found tills första körningen är klar (plan 5 kopplar det).
- **D3:** Bannern "GSX folder missing" visas bara när mappen saknas och en profil är lagrad på ett kort. Ersätter anteckningen att plan 4 ska sätta `GsxFolderFound` till true.
- **D4:** Medföljande profiler listas bara under kortets egna ICAO-koder, så ett flygplanspakets `A320.ini` eller en lös `test.ini` visas aldrig.
- **D5:** Kopiering skriver aldrig över; den vägras om `<namn>.ini` eller `<namn>.off` finns och frågar innan Two profiles uppstår.
- **D6:** Söklänken är `https://flightsim.to/others/gsx-pro/search/{kod}` med små bokstäver. **Obekräftad:** en hämtning av `/search/essa` gav en ofiltrerad lista över alla 2 720 profiler (filtret kan ligga i sidans JavaScript). Rickard kontrollerar länken i en webbläsare.
- **D10:** En Two profiles-varning per Airport på kortet, även när profilerna ligger på ett annat kort med samma ICAO-kod.
- **Tillägg efter granskning (2026-10-30):** när GSX-mappen inte kan läsas visar korten ingen GSX-markering, inga söklänkar och ingen Two profiles-varning (spec 8: "ändrar ingenting"); bannern säger varför.
- **Fynd på den riktiga maskinen:** flygplanspaket levererar `.ini`-filer som `cold_and_dark.ini` och `apu_running.ini`, vars namn ger ICAO-liknande koder (COLD, APU). De lagras som medföljande filer men visas aldrig, eftersom flygplanskort saknar ICAO-koder. ESSA, ESGG, EDDF och EPKK levererar alla en profil. Orbx listar EDDF:s (`GSX-PROFILE\EDDF-7g8nwm.ini`) och EPKK:s (`GSX-Profile-EPKK\epkk-papadelta.ini` och `.py`) inte i `layout.json`, så medföljande GSX-filer hämtas både från `layout.json` och från en genomgång av disken (kontrollerat på maskinen 2026-10-30).

## Komponenter och bedömd svårighet

| Del | Svårighet | Anteckning |
|---|---|---|
| Libraries + Addons + av/på | Medel | Läsa `manifest.json`, junctions, Addons med delade Packages och Partial-läge. Ta över befintliga junctions från Addons Linker. |
| Community-inventering och avstämning | Medel | Klassa poster, jämföra med sparat state, synk-dialog. |
| Bakgrundsskanning och cache | Medel | Inkrementell skanning, SQLite-cache, filbevakning, prioritetsordning. |
| BGL-tolkning (flygplatser) | Medel–hög | Airport-poster ur Community-, Library- och Official-paket. Formatet är kartlagt (bl.a. Little Navmap, open source). |
| Hierarki: Airport → Addons, flygplan → liveries | Medel | Kopplingsregler, manuella kopplingar. |
| Karta | Medel | WebView2 + Leaflet, klustring av cirka 40 000 flygplatser. |
| GSX-profiler | Låg–medel | `.ini` och `.py`, profiler i scenery-mappar, dra och släpp. |
| Djuplänkar för sök | Låg | URL-mallar. |
| Snyggt gränssnitt | Medel | Tar mest tid i praktiken. |

**Tidsbedömning:** Med Addons, delade Packages och avstämning i första versionen blir MVP:n snarare 5–8 veckors fokuserat kvällsarbete än de 3–6 som först bedömdes.

## Nästa steg

1. ADR:er för de beslut som är svåra att ändra (se nedan).
2. Spec och plan, sedan kod på en egen branch.
