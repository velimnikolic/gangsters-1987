# Gangsters 1987 — trenutno stanje projekta

> Snapshot: 2026-09-03  
> Unity: `6000.5.6f1`  
> Git osnova: `main` na `b0cb3398`, zajedno sa trenutnim lokalnim izmjenama  
> Provjera za ovaj dokument: statički pregled koda, scena, paketa, dokumentacije i Git stanja; Unity, Play Mode i testovi nijesu pokretani.

## Ukratko

`Gangsters 1987` je proceduralna crime/strategy igra sa taktičkim životom na ulici. Igrač vodi svoju kriminalnu porodicu kroz grad: organizuje ljude, oprema ekipe, zauzima teren, pritiska lokale, skuplja reket, ulazi u sukobe sa drugim porodicama i nosi se sa policijom, sudom i zatvorom.

Projekat je trenutno velika igriva razvojna vertikala, a ne spreman distributivni build. Najkompletniji zajednički runtime je Core/RoadDemo grad, dok niz manjih scena služi za razvoj i pregled pojedinačnih sistema. `EditorBuildSettings` još nema definisanu shipping scenu.

Glavne stvari koje već postoje:

- proceduralno sastavljen i streamovan grad sa različitim kvartovima;
- saobraćaj, pješaci, motocikli, policija, dan/noć, vrijeme, svjetla i ambijent;
- igračev outfit i 20 rivalskih porodica sa sopstvenim ljudima, novcem, nalozima i AI odlukama;
- organizacija Boss → lieutenants → crews/hoods, uz detalj, kolektore i pratnju;
- centralni biznisi, vlasnici, deeds, zaštita, strah, prisustvo i kontrola teritorije;
- fizičke ekipe, vozila, pucnjava, cover, drive-by, bombe, molotov i racketeering akcije;
- policijske prijave, svjedoci, wanted sistem, hapšenje, sud, advokat, kaucija i zatvor;
- veliki ledger, TurfMap, minimapa, ulični overlayi i novinski feed;
- kampanjski save/load koji regeneriše grad iz seed-a i vraća autoritativne knjige.

## Trenutni gameplay krug

1. Grad se generiše iz seed-a i kampanja počinje 1987. godine.
2. Igrač kroz Ledger i TurfMap pregleda ljude, porodice, blokove, lokale, novac i zakon.
3. Ljudi se regrutuju, raspoređuju pod lieutenante, opremaju i šalju na posao ili fizičku lokaciju.
4. Ekipe patroliraju, napadaju, prijete, razbijaju ili pale lokale i skupljaju dospjeli reket.
5. Prisustvo, strah, zaštita i stanje lokala utiču na kontrolu bloka i prihod.
6. Rivalne porodice koriste isti model ljudi, novca, naloga i teritorijalnih komandi, ali njihove odluke donosi `HouseMind`.
7. Svjedoci i vlasnici mogu prijaviti događaj; policija izlazi na teren, prepoznaje wanted ljude, hapsi ih i vodi kroz sudsko-zatvorski tok.
8. Dnevni tick obrađuje prihode, plate, porez/tribute, odnose, napredovanje, lojalnost, povrede, odsustva i druge dugoročne posljedice.
9. Kampanja se može sačuvati i ponovo učitati sa naslovne strane Ledgera.

## Runtime i arhitektura

### 1. Core/RoadDemo — glavni integracioni runtime

`Assets/RoadDemo` je danas najkompletniji fizički grad i ulični runtime. `CoreDemo` je najbliža glavnoj sceni igre, a `MiniCoreDemo` je manja integraciona scena za brže provjere.

Ovaj stack sadrži:

- proceduralni raspored grada i kvartova;
- ulice, raskrsnice, lane i pedestrian grafove;
- streamovanje i reciklažu blokova oko kamere;
- saobraćaj, pješake, ekipe, policiju i fizičku borbu;
- TurfMap/minimapu, lokalne overlaye i block film;
- `BusinessRuntime`, `TerritoryRuntime` i vezu sa zajedničkim outfit/personnel sistemima.

`GameplayBootstrap` automatski dodaje zajedničke `PersonnelDirector`, `OutfitDirector` i `PersonnelAlmanac` slojeve kada prepozna planirani RoadDemo/Core grad.

### 2. LivingCity — stariji generisani stack

`Assets/Scripts/Generation`, `Assets/Scripts/City` i dio `Assets/Scripts/Entities` sadrže stariji `CityBuilder` grad. On ima sopstvenu generaciju, saobraćaj, pješake, `PropertyDirector`, `GangDirector`, `StrategicMapHud` i stariji interaction put.

Kod i dalje postoji i radi kao poseban runtime, ali nije ista autoritativna gradska reprezentacija kao Core/RoadDemo. Ne treba pretpostaviti da sistem prisutan u jednom stacku automatski postoji u drugom.

### 3. Zajednički simulacioni sloj

Centralni gameplay modeli nalaze se pod `Assets/Scripts` i nijesu vezani za streamovane GameObject prikaze:

| Domen | Autoritativni sloj | Prikaz / adapter |
|---|---|---|
| Porodice i kampanja | `Underworld`, po jedan `House` i `CampaignRunner` za svaku porodicu | `UnderworldHost`, `OutfitDirector` |
| Ljudi i organizacija | `Roster`, `Organization`, `RosterOps` | `PersonnelDirector`, `DemoCrews`, Ledger |
| Biznisi | `BusinessSiteCatalog`, `BusinessDirectory`, `BusinessDeeds` | `BusinessRuntime`, `BusinessMarker`, TurfMap, Ledger |
| Teritorija | `TerritoryState`, komande, događaji i scheduler | `TerritoryRuntime`, TurfMap, notice/diagnostics HUD |
| Novac i poslovi | `Accounts`, `DaySheet`, `OrderBook`, `CampaignRunner` | Finances/Orders stranice i street job adapteri |
| Policijski dosijei | `PrisonPipeline`, `CourtCase`, police roster | `PoliceForce`, `PoliceDispatch`, THE LAW |
| Save/load | `CampaignFile` i eksplicitni snapshot modeli | Ledger SAVE/LOAD i CLI komande |

Osnovno arhitektonsko pravilo je da plan/model čuva istinu, a scene, markeri, mape i UI je samo prikazuju ili šalju namjeru kroz centralnu komandu. Streamovani blok može nestati bez nestanka biznisa, vlasništva, roster zapisa ili teritorijalnog stanja.

## Grad i svijet

### Proceduralna geografija

Implementirani su:

- proceduralni `CityLayout` sa seed-om;
- Core centar sa različitim dimenzijama blokova i blok-katalogom;
- residential kvartovi iz stabilnih recipe/plan podataka;
- Harbor, Airport i Suburb distrikti;
- parkovi, wild pojasevi, rijeke, obala, kejevi i mostovi;
- industrial generator i posebne industrial scene;
- pumpe, parking, nightclub, skladišta, policijska stanica i drugi specijalni blokovi;
- expressway/freeway kod i demo scena, dok su autoputevi u glavnom RoadDemo gradu po defaultu isključeni;
- stabilni block/district/business ID-jevi za simulaciju i mapu.

### Streaming

`CityBlockRecycler` drži samo potreban prozor živih blokova oko kamere. Recepti, geografski ID-jevi, biznisi i teritorija ostaju van recikliranih view objekata. Postoje hold/release i readiness mehanizmi za UI fotografisanje bloka i druge potrošače kojima je potreban trenutno sastavljen prikaz.

### Život na ulici

Postoje zasebni, povezani sistemi za:

- automobile, parkiranje, semafore, raskrsnice i rutiranje;
- motocikle, vozače, saputnike, naginjanje i pad;
- civilne pješake, rasporede, sjedenje, razgovore i posjete lokalima;
- školski i forecourt život;
- luku, brodove, dock workere i industrijski ambijent;
- policijske automobile, foot patrol parove, smjene i dispatch;
- day/night sat, sunce, ulična svjetla, prozore, farove, oblake i vrijeme;
- zvuk grada, ljudi, policije, saobraćaja, oružja i UI-ja.

## Organizacija, ljudi i porodice

### Underworld

`Underworld` sadrži 21 kuću: igračev outfit (`gang 0`) i 20 rivalskih porodica. Svaka kuća koristi isti osnovni model:

- svoj `Roster`;
- svoj `CampaignRunner`;
- svoj sef i dnevne knjige;
- svoju opremu, poslove, plate i front;
- odnose i grievances prema drugim porodicama;
- stanje da li je porodica aktivna, extinct ili bez nasljednika.

Rivalne porodice nijesu više samo spawnovani protivnici. `HouseMind` periodično čita ograničeni pogled na svijet i emituje namjere kroz iste komandne ulaze koje koristi igrač. Može da organizuje ljude, raspoređuje teren, kupuje opremu, mijenja stav prema drugima i pokreće teritorijalne ili druge poslove.

### Personnel i chain of command

Model danas podržava:

- Boss rank i nasljeđivanje stolice;
- lieutenante i hoodove;
- crew membership, bench/line podjelu i ograničenja kapaciteta;
- Bossov bodyguard detail;
- collector duty i posebni bag node sa do dvije escort pozicije;
- individualne atribute, potencijal, praksu, XP i starenje;
- lojalnost, pohlepu, disciplinu, temperament, ambiciju i courage;
- plate, traženu platu, potplaćenost, skimming i defection;
- povrede, bolnicu, zatvor, odsustvo i smrt;
- lični dosije, rap sheet, wanted stanje i reason feed;
- ličnu i grupnu opremu: oružje, vozila, prsluke i eksploziv.

Player roster se iz centralnog modela projektuje u `DemoCrews` fizičke jedinice. Identitet i članstvo ostaju u rosteru, dok trenutnu poziciju, vozilo, borbu i street order drži fizički runtime.

## Biznisi, teritorija i reket

### Biznisi

`BusinessRuntime` jednom populira grad iz stabilnih plan podataka i zatim vezuje trenutno streamovane prikaze na već postojeće ID-jeve.

Trenutni model pokriva:

- fizičke residential storefront bay-eve, uključujući rear/inward strane;
- café, pizza/pub, diner, gym i nightclub lokacije;
- caryard, fuel station, warehouse, works i harbor compound biznise;
- quay lokale i fairground;
- determinističke vlasnike i business archetype;
- deeds/vlasništvo nad premises;
- privremeno zatvaranje i oporavak lokala nakon štete;
- jedna kanonska lista biznisa nezavisna od toga da li je view trenutno streamovan.

Poznato ograničenje: dio starih harvested downtown blokova još nema plan-level podatak koji pouzdano razdvaja pojedinačne shopfrontove, pa se vodi kao nepodržan/ineligible izvor umjesto da sistem izmisli pogrešne lokale.

### Teritorija

Centralni territory sloj koristi stabilne ID-jeve i sadrži:

- canonical geography blokova i susjedstva;
- prisustvo porodica na terenu;
- fear događaje i decay;
- derived control, contested stanje i takeover/loss tokove;
- responsibility dodijeljenu Bossu ili lieutenantima;
- typed komande, odbijanja i events;
- centralni scheduler umjesto jednog `Update()` poziva po lokalu ili bloku;
- snapshots za save/load.

### Reket i akcije na lokalu

Postoje centralna pravila i fizičke akcije za:

- approach/demand/threaten;
- smash i torch;
- block shakedown;
- protection state, dospjeli iznos i kolekcione runde;
- kolektora, pratnju, torbu i bankiranje prihoda;
- persistentnu torbu na zemlji i TAKE THE BAG tok;
- reakciju vlasnika, poziv policiji, svjedoke i complaint/case vezu;
- oštećen i zatvoren lokal, daske i fire/smoke posljedice.

Prihod od zaštite se knjiži kao `Protection`, dok plijen iz poslova i uzeta torba idu pod `Jobs`; oba ulaze kao dirty money.

## Taktička igra i borba

Fizičke crew jedinice trenutno podržavaju:

- selection, hover i map/street orders;
- hodanje po pedestrian grafu i držanje formacije;
- ulazak/izlazak iz premises i headquarters;
- ulazak u automobil, vožnju, parkiranje i iskrcavanje;
- napad na drugu ekipu i povlačenje;
- cover, nišanjenje, pucanje, muzzle flash, smoke i krv;
- pistol/rifle/tommy-gun varijante;
- drive-by i run-down;
- bombe, eksplozije i molotov;
- smash/torch radnje nad lokalom;
- smrt, flee, surrender, arrest i street outcome koji se vraća u knjigu posla.

Manje demo scene (`CrewDemo`, `CoverDemo`, `RifleDemo`, `RacketeeringAnimationDemo`, `MotoDemo`) služe kao tanki review/test harnessi za iste centralne ili zajedničke sisteme.

## Policija, sud i zatvor

Policijski/law tok je već širok i povezan sa ulicom:

- police roster po precinctu;
- day/night smjene, foot patrol i patrol cars;
- zamjena poginulih policajaca i uništenih vozila;
- dispatch na pucnjavu, smrt policajca i prijavu vlasnika;
- svjedoci, complainants i njihova spremnost da svjedoče;
- wanted nivoi, prepoznavanje i skrivanje u owned premises;
- surrender/refusal odluka i fizičko hapšenje;
- pritvor, transfer do suda, presuda, transfer u zatvor i release;
- bail, skip, lawyer/counsel i mogućnost pritiska na svjedoka;
- trajni docket, verdict istorija i rap-sheet posljedice.

THE LAW stranica u Ledgeru čita `LawSheet.Collect` i prikazuje docket, ljude unutra, wanted listu, counsel i završene presude. Save format verzije 2 čuva i kompletan docket; stariji v1 save se migrira.

## Ekonomija, headquarters i armory

Ekonomija trenutno ima:

- jedan sef po porodici;
- clean/dirty podjelu unutar istog iznosa;
- daily sheets, rolling istoriju i finansijski pregled;
- protection, jobs, sales i legitimate income kategorije;
- kupovine, druge troškove, mito, plate, porez i tribute;
- plate po ranku i posljedice prazne koverte;
- cijene opreme, vozila, premises i ključnih kriminalnih radnji;
- headquarter izvještaj o sefu, ljudima, zalihama i vozilima;
- armory katalog i pravilo da se oprema fizički mijenja samo na odgovarajućoj lokaciji.

Kompletan drug trade/proizvodnja i laundering loop još nijesu implementirani; postoje cijene i budući dizajn smjerovi, ali ne i završeni gameplay sistem.

## UI i prezentacija

### Personnel Ledger

`PersonnelAlmanac` je glavni komandni i informativni UI. Ima sljedeće stranice:

1. THE PAPER — proceduralne vijesti, classified ulazi i SAVE/LOAD;
2. PERSONNEL — roster, lični dosije, statistike, status i oprema;
3. ORGANIZATION — organizaciona struktura i upravljanje članovima;
4. CHAIN OF COMMAND — Boss, detail, lieutenant grane, bag grane i reserve;
5. BLOCKS — teritorija, odgovornost, biznisi, akcije i block file;
6. FINANCES — sef, income/cost kategorije i dnevne knjige;
7. ARMORY — kupovina i dodjela opreme;
8. FAMILIES — rivalne porodice, odnosi i poznati podaci;
9. THE LAW — cases, prisoners, wanted, counsel i verdicts;
10. ORDERS — interna stranica za kreiranje/vođenje naloga bez stalnog taba.

Ledger koristi zajednički `LedgerV2`/`LedgerKit` vizuelni jezik, IBM Plex Mono/Oswald/PT Serif porodice, portrete i 1987 paper/telex estetiku.

### Mape i overlayi

Core/RoadDemo koristi:

- `TurfMapHud` kao punu taktičku/teritorijalnu mapu;
- `TurfMinimap` u uglu;
- pripremljeni building layer nezavisan od live streamovanih mesh-eva;
- visibility/fog-of-war izveden iz friendly crew područja;
- crew, traffic, police, business, block i witness markere;
- map targeting za kretanje, naloge i skokove iz Ledgera.

Na ulici postoje crew bar, namjere i putanje, door/business meniji, front cards, territory notices, police/arrest HUD i block photograph/film prikaz. Stariji LivingCity stack koristi `StrategicMapHud` i `CityOverlayHud`.

## Save/load

`CampaignSave` piše čitljiv JSON u `Application.persistentDataPath/gangsters/autosave.json`.

Čuva se kampanjsko stanje:

- seed grada, dan i sat;
- svih 21 kuća, rosters, organization, oprema, novac i poslovi;
- odnosi i grievances;
- territory snapshot, deeds, shutdowns i map knowledge;
- prisoners, escapes, docket, witnesses i verdicts.

Namjerno se ne čuvaju frame-level pozicije tijela niti izvedeni fear/presence/control cache. Load ponovo učitava scenu, regeneriše isti grad iz seed-a, vraća knjige i ponovo materijalizuje fizičke jedinice.

## Scene

`Assets/Scenes` trenutno sadrži više razvojnih scena, grupisanih ovako:

| Uloga | Scene |
|---|---|
| Glavna integracija | `CoreDemo`, `MiniCoreDemo` |
| Grad i kvartovi | `ResidentialDemo`, `IndustrialDemo`, `HarborDemo`, `AirportDemo`, `SuburbDemo`, `ExpresswayDemo` |
| Blokovi i sadržaj | `BlockDemo`, `CoreHarvest`, `ParkingDemo`, `PumpDemo`, `ShopDemo` |
| Crew/combat | `CrewDemo`, `CoverDemo`, `RifleDemo`, `MotoDemo`, `RacketeeringAnimationDemo` |
| UI i vizuelni pregled | `Ledger`, `OcclusionDemo`, `FireSmokeFxDemo` |
| Ostalo | `HoodDemo`, `BikeDemo`, `PushUpDemo`, `PalmCityDemo`, `IndustrialLab` |

Tu su i `BuildingCatalog.unity` i `CastCatalog.unity`, kao i third-party sample scene. Nijedna scena trenutno nije unesena u `EditorBuildSettings`, pa projekat nema definisan finalni start/build tok.

## Tehnička osnova i sadržaj

- Unity `6000.5.6f1`;
- URP `17.5.0` i Shader Graph;
- Input System `1.20.0`;
- AI Navigation `2.0.14`;
- uGUI + TextMesh Pro stilizovani runtime UI;
- NUnit/Unity Test Framework i performance test paket;
- `com.unity.pipeline` za komande prema već otvorenom editoru;
- Synty POLYGON paketi za grad, likove, vozila, policiju, zatvor, klubove i druge setove;
- Mixamo i lokalne animacije;
- Unity Particle Pack, custom fire/smoke/bomb/muzzle FX i shaderi;
- poseban gun/weapon asset set;
- audio biblioteka za ambience, ljude, saobraćaj, policiju, oružje i UI.

Detalji licenci i izvora su u [Docs/credits.md](Docs/credits.md) i [Docs/audio-map.md](Docs/audio-map.md).

## Alati i provjera

Projekat ima mnogo headless/model ugovora i Unity CLI komandi u `Assets/Scripts/Editor/PipelineCommands.cs`, uključujući:

- geography, vacancy i city-layout provjere;
- organization, command, personnel, wage i personality testove;
- business, economy, territory, control, presence, fear i racket audite;
- underworld, houses, relations, rounds i simulacioni tally;
- police, law-sheet i save/load testove;
- scene play harness i capture/audit scenarije.

`Tools/play` sadrži run/soak/analyze alate za ponovljene Play sesije. `Tools/CoreSim` i `Tools/RoadSim` omogućavaju dio čistih simulacija van Unity prikaza. Precizan rad sa aktivnim editorom opisan je u [Docs/unity-cli.md](Docs/unity-cli.md), a soak/harness tok u [Docs/play-harness.md](Docs/play-harness.md).

Važno: uspješan compile ili headless contract nije dokaz da je vizuelni ili fizički tok dobar u Play Mode-u. Za street, streaming i UI promjene potreban je poseban live pregled.

## Gdje šta živi

| Putanja | Namjena |
|---|---|
| `Assets/Scripts/Outfit` | kampanja, porodice, ekonomija, poslovi, AI namjere |
| `Assets/Scripts/Personnel` | ljudi, roster, organizacija, razvoj, lojalnost i dužnosti |
| `Assets/Scripts/Territory` | geografija, prisustvo, strah, kontrola, reket, komande i snapshots |
| `Assets/Scripts/Business` | business sites, vlasnici, direktorij, deeds i shutdowns |
| `Assets/Scripts/Police` | čisti police/law modeli, sud i prison pipeline |
| `Assets/Scripts/Save` | kampanjski JSON format i restore tok |
| `Assets/Scripts/UI` | Ledger, map/overlay adapteri i vizuelni sistemi |
| `Assets/Scripts/Gameplay` | runtime directors, bootstrap i zajednički gameplay adapteri |
| `Assets/Scripts/Generation` | stariji LivingCity generator |
| `Assets/RoadDemo` | glavni proceduralni grad, streaming, ulica, ekipe, policija i TurfMap |
| `Assets/Scenes` | integracione, demo i review scene |
| `Docs` | dizajn, implementacioni zapisi, pravila i operativna dokumentacija |
| `Tools` | CLI, simulacije, soak, audio i UI design-system alati |

## Trenutno u radu u ovom checkoutu

Radno stablo nije čisto. Lokalno se trenutno dorađuju:

- fizički izlazak policajca sa foot patrola do lokala radi uzimanja izjave;
- zajednički doorway prolaz za crew i police posjetioce;
- complaint/witness podaci po pojedinačnoj prijavi;
- bag-detail odbrana headquarters bloka i povratak kolektora/pratnje unutra;
- collector i racket dokumentacija/acceptance detalji;
- apartment blueprint UI brief i HTML prototip.

Ove lokalne promjene su dio trenutnog projekta, ali nijesu isto što i potvrđen stabilni baseline. Posebno su apartment gameplay i blueprint trenutno dizajn/prototip, ne implementiran centralni runtime.

## Poznate granice i otvorene stvari

- Nema konfigurisanog shipping builda ni glavne scene u `EditorBuildSettings`.
- Core/RoadDemo i stariji LivingCity još nijesu jedan objedinjeni city/navigation/runtime model.
- Neki harvested downtown blokovi nemaju dovoljno plan podataka za pouzdano izdvajanje svakog lokala.
- Apartments/owned flats sistem je u fazi briefa i UI prototipa.
- Full drug economy, production i laundering nijesu završeni gameplay sistemi.
- Credits dokument postoji, ali nema završene credits stranice u igri.
- Nekoliko street/streaming ponašanja ima jače statičke i contract dokaze nego ponovljenu live vizuelnu potvrdu.
- Collector/bag detalj ima implementirane model, UI i street puteve, ali acceptance brief i dalje traži ručni Play pregled kompletnog izlazak–runda–povratak i death/pickup toka.

## Ključna dokumentacija

- [CLAUDE.md](CLAUDE.md) — pravila rada u projektu i Unity CLI tok;
- [PROJECT_ARCHITECTURE_SNAPSHOT.md](PROJECT_ARCHITECTURE_SNAPSHOT.md) — detaljan istorijski audit od 2026-08-30; koristan za staru strukturu, ali su tvrdnje da nema AI-ja, save/load-a i centralnog territory lifecycle-a danas zastarjele;
- [Docs/core-streaming.md](Docs/core-streaming.md) — block streaming;
- [Docs/tactical-map.md](Docs/tactical-map.md) — TurfMap i minimapa;
- [Docs/business-inventory.md](Docs/business-inventory.md) — izvori biznisa;
- [Docs/racket-collections.md](Docs/racket-collections.md) — zaštita i kolekcione runde;
- [Docs/headquarters.md](Docs/headquarters.md) — sef, HQ i armory autoriteti;
- [Docs/ledger-law-sheet.md](Docs/ledger-law-sheet.md) — sudsko-policijska Ledger stranica;
- [Docs/economy-prices.md](Docs/economy-prices.md) — cijene u dolarima iz 1987;
- [Docs/1987-period-reference.md](Docs/1987-period-reference.md) — istorijski i tonalni okvir;
- [Docs/credits.md](Docs/credits.md) — licence i attribution.

---

Ovo je opis onoga što postoji u checkoutu 2026-09-03, ne obećanje budućeg scope-a. Kada se promijeni autoritativni sistem, glavna scena ili status apartments rada, treba ažurirati ovaj dokument zajedno sa tim promjenama.
