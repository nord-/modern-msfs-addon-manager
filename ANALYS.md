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
- **Redigerbart i dialogen, med direktsparning:** namn, bild (ett pakets thumbnail eller en egen fil; bara sökvägen sparas) och ICAO. Alla koder med giltigt format accepteras; en okänd kod ger ingen kartmarkör (position hamnar under Later). En kod som redan har ett kort slår ihop korten.
- **Kartan** är en vyväxling `Cards | Map` på Scenery → Airports.
- **GSX** visas på kort och i dialogen; GSX-tabellen utgår. Installation med dra och släpp och av/på för profiler hamnar under Later.
- **Community-vyn** ligger under Settings; trasiga länkar och lösa filer visas i en banner.
- **Uses**, inklusive varningen "Used Addon off", hamnar under Later. Hide/Show hidden och Delete from disk likaså.
- **Lagring:** användardatan stannar i JSON (`userdata.json`), skild från SQLite-cachen. Rickard utmanade beslutet; argumenten: sökningen körs ändå i minnet mot härledda data (C6), datamängden är liten, JSON går att läsa och rädda för hand, och cachen raderas vid formatbyte. Den enda verkliga vinsten med SQLite, låsning mellan två instanser, löses med en mutex: appen tillåter en instans åt gången.
- **Managed elsewhere utgår ur MVP:n** (spec-granskning runda 3). Rickard: "jag lägger aldrig till ett lib hos oss som är 'Managed elsewhere'? Vad skulle syftet vara för mig att lägga till `C:\Sim\Addons\Neofly` om jag inte har för avsikt att hantera de Addons:en i den här appen?" Mappar som ett annat verktyg äger (Orbx Central, FSDT, Neofly) registreras inte; deras länkar är External och visas ändå låsta på rätt kort. Allt i en registrerad Library går att slå av och på. Ersätter flaggan i designbesluten 2026-10-25.
- **Nästlade Libraries är tillåtna:** innehållet avgör menyn och typen bara synligheten, så ett paket får typerna från alla Libraries det ligger i.
- **Ny färdplan:** plan 3 = Addons (kort, Library-typer och synlighet, flikar, taggar, dina ändringar, trafikljus, varningar, mutex, CLI `addons` med ett ready-for-human-issue); plan 4 = GSX (läsa, status, profil-kort); plan 5 = WPF-appen enligt denna design; plan 6 = karta och release.

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
