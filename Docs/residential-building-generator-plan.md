# Plan: forge stambenih zgrada nad radnjama (ResidentialForge) — EPIC 36

> Predlog 2026-09-04, na korisnikovu ideju istog dana („radnje su uvek iste širine, na ćoškovima
> samo prozori s obe strane, stanovi iste širine — odredi dužinu/širinu, uvek dva reda radnji,
> gore visinu, poređaj stanove, dodaj dekoracije pokupljene sa residential zgrada"). Prošao
> contrarian istog dana (§9). **Epic u Linearu: GAN-332** (tiketi FORGE-001..006).
>
> **Obim (korisnik, 2026-09-04): epic je samo forge — kako se zgrada pravi.** Gde i kako će se
> zgrade koristiti u gradu (koji blokovi, katalog, prazan pojas u MiniCoreDemo) korisnik odlučuje
> kasnije. Merenja o upotrebi ostaju u §8 kao građa za tu odluku, ne kao posao.
>
> **Odluke uzete (korisnik, 2026-09-04):** 3 do 5 spratova nad radnjama; oba ugaona modula radnje;
> brownstone se ignoriše u potpunosti; zadnji red prizemlja = radnje („uvek dva reda radnji");
> ugaone radnje ka kratkim stranama na oba kraja; dve izmene recepta za redove uzete, ali čekaju
> odluku o upotrebi (nisu u epicu).
>
> U jednoj rečenici: **forge od Synty POLYGON City modula sklopi zgradu L × 2 ćelije — radnje u
> oba reda, 3–5 spratova stanova po kolonama, krov od ivičnih komada, dekor tamo gde ga Synty autori
> stavljaju — determinisano iz kockice, sa presudom koja meri svaku ćeliju i sa sintetičkom
> `ResidentialUnit` koju bilo koji budući domaćin čita kao požnjevenu kuću. Stoji u svojoj
> izložbenoj sceni. Ništa se ne peče, ništa se ne stavlja u grad.**

| šta | gde (novo) |
|---|---|
| atlas modula: mera, lica, porodica stila, spoljni uglovi, par ivica↔ugao krova, deo stuba — **izmeren** alatom | `Assets/Scripts/Editor/ResidentialModuleAtlas.cs` → generisan `Assets/RoadDemo/ResidentialModules.cs` + `Docs/residential-module-atlas.md`; komanda `gangsters_module_atlas` (FORGE-001) |
| tabela dekora: gde Synty autori stavljaju klime, stepenice, bilborde — **izmerena iz svih 14 kuća**, sa udelom nevezanih | `Assets/Scripts/Editor/ResidentialDecorTable.cs` → generisan `Assets/RoadDemo/ResidentialDecor.cs`; komanda `gangsters_decor_table` (FORGE-002) |
| list zgrade (čist C#: `(dice, L, N)` → moduli + dekor + **sintetička `ResidentialUnit`**) + presuda + sweep + testovi | `Assets/RoadDemo/ResidentialFacade.cs`, `Tools/CoreSim --facade`, `gangsters_forge_tests` (FORGE-003) |
| stajanje lista od modula: kolorvej, izlozi, cutaway; registar generisanih jedinica | `ResidentialBlocks.StandSheet`, `ResidentialUnits.Generated` (FORGE-004) |
| izložba za oko + komanda | `Assets/Scenes/ForgeDemo.unity`, `Tools/City/Residential/Forge Showroom`, `gangsters_forge` (FORGE-005) |
| doc, memorija, zatvaranje | `Docs/residential-forge.md` (FORGE-006) |

## Stanje

- [x] odluke uzete: spratovi, ugao, brownstone, zadnji red, krajevi, obim = samo forge (2026-09-04)
- [x] contrarian prošao, nalazi ugrađeni (§9)
- [x] epic GAN-332 sa šest tiketa
- [ ] D1–D6 (§7) — odgovori ili podrazumevano
- [ ] FORGE-001 atlas modula
- [ ] FORGE-002 tabela dekora
- [ ] FORGE-003 list, presuda, sweep, testovi
- [ ] FORGE-004 stajanje lista
- [ ] FORGE-005 izložba i komanda
- [ ] FORGE-006 doc i zatvaranje

---

## 0. Šta postoji (izmereno 2026-09-04)

### 0.1 Kuće su od istih ~30 modula

Prebrojano iz prefaba `residential-01..08` (`m_SourcePrefab` po instanci; `10..16` su od istog
kita — FORGE-002 čita svih 14). Nema nijednog komada zida koji nije Synty modul; „dekor" su obični
propovi istog paketa. Svi komadi su **ravna braća** u prefabu — vezivanje propa za modul je
pretraga po ćeliji i spratu, ne hijerarhija.

| sloj | moduli | u kućama 01–08 |
|---|---|---|
| prizemlje | `Shop_01/02/04/05/06`, `Shop_03` (dupli, 10 m), `Shop_Corner_01/02`, `Apartment_Door_01/02` | 1–15 radnji po zgradi; vrata stanova 0–10 |
| spratovi | `Apartment_01/02/03` (jedan sprat), `Apartment_Stack_01/02/03` (tri sprata u komadu), `Apartment_Corner_01/02/03` (ugao, po spratu — **ugaoni stack ne postoji**) | Stack 2–24, singl 3–16, ugao 8–21 |
| krov | `Apartment_Roof_01/02/03` (ivica), `Apartment_Roof_Corner_01/02/03`, `Roof_Access_01`, `Skylight_01` | krov 3–23, ugao 3–9, izlaz 1–4 |
| požarne stepenice | `FireEscape_01/02/03` — **uvek u jednakom broju** | 9–35 komada = 3–13 stubova |
| tende | `Shop_Cover_01..05` | 0–6 |
| zid | `Aircon_01`, `PlanterWindow_01/02`, `Window_01`, `Washingline_01/02/03`, `Sign_Attachment_02/03`, `LargeSign_*`, `Sign_Hotel/Cafe/Barber/XXX/Pub`, `SecurityCamera(_Arm)`, `ATM_01`, `Poster(_Frame)` | klime **10–32** po zgradi |
| krovni dekor | `Roof_Aircon_01/02/03`, `SatDish_01`, `Vents_Straight/End/Corner/Exhaust/Transition`, `Billboard_Roof_01` + `Billboard_Sign_0N`, terasa (`Couch`, `Table`, `Planter`, `PotPlant`, `PowerBox`) | ventilacija 5–22 komada, bilbordi 0–4 |
| tlo iza | `Skip_01/02`, `TrashBag`, `CardboardBox`, `Pipe_Small`, `PowerBox` | 1–6 |

Brownstone moduli (`Apartment_Stairs_*`, `Stairs_Planter`, `Door_Corner`, drveće) su van plana
po odluci.

### 0.2 Mere modula (živi editor, `gangsters_measure`, 2026-09-04)

Okvir modula: pivot je **SI ugao ćelije**; na yaw 0 modul puni x −5..0, z −5..0, lice na +Z
(žetva, 2026-08-26). Kutija ispod je u tom okviru; ono što viri preko +Z je venac/prepust.

| modul | x | y | z | napomena |
|---|---|---|---|---|
| `Apartment_01` | −5.00..0.00 | 0..3.00 | −5.00..0.20 | ravna fasada |
| `Apartment_02` | −5.09..0.09 | 0..3.00 | −5.09..0.13 | |
| `Apartment_03` | −5.14..0.14 | 0..3.00 | −5.15..0.35 | dublji venac |
| `Apartment_Corner_01` | −5.00..0.20 | 0..3.00 | −5.00..0.20 | ugao, dva lica |
| `Apartment_Corner_02` | −5.09..0.13 | 0..3.00 | −5.09..0.13 | po žetvi **unutrašnji** ugao (lice čita +X) |
| `Apartment_Corner_03` | −5.22..0.27 | 0..3.00 | −5.22..0.27 | |
| `Apartment_Door_01` | −5.05..0.05 | 0..3.00 | −5.05..0.53 | ulaz u stanove, nadstrešnica |
| `Apartment_Door_02` | −5.06..0.06 | 0..3.00 | −5.06..0.06 | |
| `Apartment_Stack_01/02/03` | kao singl istog broja | **0..9.00** | kao singl istog broja | tri sprata u komadu |
| `Apartment_Roof_01` | −5.15..0.15 | 0..0.50 | −5.17..0.75 | ivica krova, venac 0.75 |
| `Apartment_Roof_02` | −5.13..0.13 | 0..0.74 | −5.15..0.34 | |
| `Apartment_Roof_03` | −5.15..0.15 | 0..0.74 | −5.17..0.46 | |
| `Apartment_Roof_Corner_01` | −5.15..0.75 | 0..0.50 | −5.17..0.75 | par sa `Roof_01` (ista dubina venca) |
| `Apartment_Roof_Corner_02` | −5.16..0.46 | 0..0.74 | −5.17..0.46 | dubina kao `Roof_03` |
| `Apartment_Roof_Corner_03` | −5.13..0.34 | 0..0.74 | −5.15..0.34 | dubina kao `Roof_02` |
| `Roof_Access_01` | ±1.38 | 0..2.74 | ±1.43 | pivot u centru |
| `Shop_01` | −5.00..0.00 | 0..3.05 | −5.00..0.54 | 2 renderera (zid + `_Glass`) |
| `Shop_03` | −10.04..0.04 | 0..3.08 | −5.00..0.49 | **dva modula** |
| `Shop_Corner_01` | −5.19..0.19 | 0..3.00 | −5.12..0.28 | vrata na licu +Z |
| `Shop_Corner_02` | −5.17..0.17 | 0..3.00 | −5.17..0.17 | vrata na kosini 45° |
| `Shop_Cover_05` | −4.66..0.73 | −0.68..0 | −4.66..0.73 | krovna nadstrešnica 5×5, visi ispod y=3 |
| `Shop_Cover_01` | ±2.29 | ±0.15 | ±1.41 | platnena tenda, pivot u centru |
| `FireEscape_03` | −4.90..−0.65 | −0.53..3.27 | 0..1.76 | **donji** deo, sa merdevinama |
| `FireEscape_02` | −3.32..−0.07 | −0.53..3.08 | 0..1.64 | **srednji** |
| `FireEscape_01` | −3.32..−0.07 | −0.53..0.96 | 0..1.64 | **gornji** (samo podest) |

Šta se iz mera vidi i što forge sprovodi:

* **Stil ide u porodicama 01/02/03**: singl, stack i ugao istog broja imaju istu dubinu venca
  (01 = 0.20, 02 = 0.13, 03 = 0.35). Krovni ćoškovi su **numerisani drugačije** od krovnih ivica
  (po dubini: `Roof_01`↔`Roof_Corner_01`, `Roof_02`↔`Roof_Corner_03`, `Roof_03`↔`Roof_Corner_02`).
  Nagoveštaj iz kutija — **atlas ga potvrđuje merom lica** (FORGE-001); isto i koji ugaoni moduli
  su spoljni (stil 02 možda nema spoljni ugao).
* **Ugao je uvek po spratu** (nema `Corner_Stack`): kolona ugla ima N komada; kolona zida jedan
  stack + (N−3) singla, ili N singla.
* **Krov je samo ivica.** Svaki krovni komad nosi venac; u paketu nema ravnog ispuna. Zgrada
  dubine 2 ćelije je jedina koju kit zatvara bez rupe — otuda „uvek dva reda", i otuda su Synty
  krila svuda dubine 2 (`residential-05` je 2×9).
* **Požarne stepenice su stub od tri dela**: 03 dole (merdevine vise −0.53), 02 sredina, 01 gore;
  na pivotu modula (+0.08, +0.02), yaw modula, vire 1.64 m ispred lica.

### 0.3 Spratna mera i visine dekora (iz prefaba 01–08)

| šta | y (m) | pravilo koje se vidi |
|---|---|---|
| radnja / vrata stanova | 0 | prizemlje 3 m |
| spratovi | 3, 6, 9 (12) | sprat 3 m; stack na 3 pokriva 3..12 |
| krovna ivica | 12 (15 na podignutom delu) | krovna ploča = zadnji sprat + 3 |
| krovni dekor, izlaz na krov, bilbord, terasa | **12.5** (15.5) | krovna površina je +0.5 nad ivicom |
| klima na zidu | 3.5 / 6.5 / 9.5 (ređe 3.0 / 6.0 / 9.0) | pola metra nad linijom sprata |
| žardinjera na prozoru | 3.0–3.5 / 6.0–6.5 / 9.0–9.5 | ista linija |
| štrik za veš | 4.3–10 | **između dva stuba stepenica** (dužina 5.21 = jedna ćelija) |
| tenda `Shop_Cover` | 2.5–3.5 | vrh radnje |
| natpis nad vratima | 1.5–3.0 | nad vratima radnje |
| bočni natpis (`Sign_Attachment`) | 3.5 / 6 / 6.5 / 12 | na uglu kolone |
| veliki znak (`LargeSign_*`) | 5.0–5.5 na zidu, 12.0 na ivici krova | dva položaja |
| kamera | 5.0 | |
| ATM, poster | 1.5 / 1.0 | prizemlje, goli zid |

Učestalosti (izmerene; u tabelu dekora ulaze kao raspon): klima ≈ **1 na 3 prozorska sprata**
(res-06: 32 / 96, res-07: 10 / 30); stubova stepenica **28–86 %** kolona (res-06: 9 / 32, res-07:
4 / 10, res-08: 6 / 7); vrata stanova **0–10** po zgradi (res-08 nema nijedna); bilborda 0–4;
podignut deo krova na 4 od 8.

### 0.4 Šta nizvodno čita jedinicu, i po čemu

Kuća na bloku je `Spot` recepta sa **objektom** `ResidentialUnit`. Nizvodno čita taj objekat, ne
prefab: recept (`Face`, `CW/CD`, `Shops`), poslovi (`ResidentialBusinessSites` — `ShopBays`, id
`spot:{index}:{unit.Name}:shop:…`), stanovi (`ApartmentBuildings` — `MaxH`, `ShopBays`, `Doors`;
stanova je `ShopBays.Length × spratova`), izlozi (`ResidentialStorefrontDressing` — keš **po
`unit.Name`**, prolaz kroz `ResidentialUnits.All`), turf mapa (`TurfMapSurvey:1869` — `known =
ResidentialUnits.All`), kompozitor (`ResidentialBlocks.StandUnit` — **jedino mesto koje otvara
prefab**). Zato **sintetička jedinica** — objekat sa istim poljima, izračunatim iz lista i atlasa —
prolazi kroz sve osim tri mesta: `StandUnit` (grana `StandSheet`), keš izloga i `known` mape
(registar `ResidentialUnits.Generated`). Prefabi se dižu kroz `Composer.Raise` → `DemoAssetLoad`
(AssetDatabase u editoru) — isto kao park, pa i moduli iz `Assets/Synty/PolygonCity/Prefabs/Buildings/`.

### 0.5 Pravila iz memorije koja važe, i jedno koje se menja

- **Ako ne znaš — ostavi prazno i prijavi.** Ćelija fasade kojoj list nema šta da da je greška
  presude, ne pogađanje.
- **Fasade nisu uvek +Z** — atlas meri lice svakog modula (`FacadeFinder`), list čita atlas.
- **Ne izmišljaj pravila u planovima** — učestalosti iz tabele dekora; sve moje je u §7 pitanje.
- **U blokove samo katalog** — zgrada je od kataloških modula.
- **Menja se: „blokovi od klastera, nikad sečena zgrada"** (2026-08-17): važilo je za PalmCity
  katalog bez podatka o pročelju; POLYGON City modul ima izmereno lice. Pravilo ostaje za jezgro;
  za forge važi ovaj plan (memorija dopunjena 2026-09-04).

---

## 1. Gramatika zgrade (šta list radi)

Sve dole je **izmereno na požnjevenim kućama** osim onoga što je obeleženo *(D1–D6, §7)*.

### 1.1 Ulaz i otisak

`ResidentialFacade.Roll(dice, L, N)`: kockica, dužina **L** u ćelijama (3..13 u sweep-u),
spratova **N ∈ {3, 4, 5}** nad radnjama. Odluke fiksiraju ostalo: dubina **2** (krov je samo
ivica, §0.2), **radnje u oba reda**, **ugaone radnje ka kratkim stranama na oba kraja**.

```
 lice B (−Z)
 ┌───┬───┬───┬───┬───┬───┬───┬───┬───┬───┬───┐
 │ C │ S │ S │ D │ S │ S │ S │ S │ D │ S │ C │  zadnji red = radnje ka licu B
 ├───┼───┼───┼───┼───┼───┼───┼───┼───┼───┼───┤
 │ C │ S │ D │ S │ S │ S │ S │ D │ S │ S │ C │  prednji red = radnje ka licu A
 └───┴───┴───┴───┴───┴───┴───┴───┴───┴───┴───┘
 lice A (+Z)                 L = 11 (55 m)       C ugao (Shop_Corner ka kratkoj strani), S radnja, D vrata stanova
```

### 1.2 Kolone

Fasada je niz **kolona** po ćeliji obima: komad prizemlja, komadi spratova, komad krova, dekor.
Zgrada L × 2 ima 2L + 4 kolone, 4 ugaone (jedna ćelija, dva lica) i 2L − 4 zidne. Kratke strane
nemaju zidnih kolona — samo dva ugla, kao kraj `residential-05`.

### 1.3 Prizemlje

| kolona | komad |
|---|---|
| ugao | `Shop_Corner_01` (vrata na licu) ili `Shop_Corner_02` (vrata na kosini), okrenut ka kratkoj strani — **oba, odluka** |
| zid | `Shop_01/02/04/06` (jedna ćelija), `Shop_03` (dve, nikad preko ugla), `Shop_05` (bez vrata — **nikad sam**, FRONT-001 ga pripaja komšiji sa vratima) |
| vrata stanova | `Apartment_Door_01/02` — bar jedna kolona po redu *(D2)* |

Tenda (`Shop_Cover_0N`) po tabeli dekora; natpisi *(D4)*.

### 1.4 Spratovi i krov

Sprat s na y = 3s. Zidna kolona: stil S ∈ {01, 02, 03} *(D1)*, `Stack_0S` na y=3 + `Apartment_0S`
na 12 (i 15) za N > 3, ili N singla; ugaona kolona: `Apartment_Corner_0S` po spratu, samo spoljni
uglovi iz atlasa. Krov na y = 3 + 3N: `Roof_0S` po zidnoj koloni, venac napolje; `Roof_Corner` par
iz atlasa; `Roof_Access` 1–2; `Skylight` po tabeli. Podignut deo krova nije u v1 *(D5)*.

### 1.5 Požarne stepenice

Stub na koloni: `FireEscape_03` na y=3, `_02` na srednjim, `_01` na poslednjem spratu; pivot
modula + (0.08, 0.02), yaw modula. Udeo kolona: iz tabele dekora *(D3)*. Štrik samo između dva
susedna stuba.

### 1.6 Dekor

Tabela dekora (FORGE-002) za svaku porodicu daje: na kom modulu stoji, pomak u okviru modula,
učestalost (min/srednje/max). Propovi koji vežu **dve kolone** (štrik, lanac ventilacije,
nadstrešnica 5×5, stub preko spratova) su **relacije između kolona**. Stub stepenica zauzima
kolonu na svim spratovima (klima i žardinjera ne idu na nju); bilbord i izlaz na krov zauzimaju
krovne ćelije; ventilacija je lanac `End → Straight… → Corner → Exhaust`; terasa je grupa na
jednoj krovnoj ćeliji.

### 1.7 Sintetička jedinica

List uz module daje i `ResidentialUnit` **izračunatu po konstrukciji**: `CW = L, CD = 2`, `Plan`
sve `#`, `Face` = dve duge strane, `Doors`/`Shops` po strani iz modula, `ShopBays` iz kataloga
vrata modula (`StorefrontDoorCatalog`, isti koji žetva koristi), `MaxH = 3 + 3N + venac`, `Over`
iz atlasa (1.76 stepenice, 0.75 venac), `Kind = Through`, `Name = forge-L{L}-N{N}-{seed}`,
`Pieces`. Isti tip, ista polja — budući domaćin je čita kao požnjevenu kuću.

### 1.8 Determinizam

`Roll` je čista funkcija; sva bacanja unapred iz jednog `Random(dice)`; nijedan
`UnityEngine.Random`; ništa ne čita kockicu posle lista (zamka ShuffleBag). Ista kockica = ista
zgrada, na svakoj mašini i u CoreSim-u.

---

## 2. Presuda (meri ono što broji)

| nalaz | šta meri |
|---|---|
| `Hole` | ćelija obima bez komada na nekom spratu, ili krovna ćelija bez komada |
| `Double` | dva komada na istoj ćeliji i spratu |
| `CornerOff` | ugaoni komad van ugla, zidni na uglu |
| `RoofPair` | krovna ivica i ugao različite dubine venca |
| `ShopAcrossCorner` | `Shop_03` preko ugaone ćelije |
| `LoneGlass` | `Shop_05` bez susedne radnje sa vratima |
| `NoDoor` | red bez kolone vrata stanova *(ako D2 kaže da mora)* |
| `EscapeOrder` | stub bez 03 dole / 01 gore, srednji koji nije 02 |
| `LineLoose` | štrik čiji krajevi nisu na dva stuba |
| `Clash` | dva propa koje tabela ne dozvoljava zajedno |
| `OutOfBox` | prop van otiska + izmerenog prepusta |
| `Empty` | ćelija kojoj list nije imao šta da da — **prijavljena, ne popunjena** |
| `UnitMismatch` | sintetička jedinica ne odgovara modulima (izlozi, vrata, lica, visina) |

Sweep: `Tools/CoreSim --facade`, seedovi 1–30 × L 3..13 × N 3..5 = **990 listova, 0 kvarova**,
plus raspodela modula i dekora po zgradi uz izmereni raspon §0.3 (list koji sistematski beži iz
raspona je greška tabele, ne ukus). Headless suite `gangsters_forge_tests` (12 ugovora, u epicu).

Scena: izložba (§3.3) — svaki list stoji, 0 komada van otiska + prepusta, natpis (L, N, izlozi,
stanovi, komadi), pločica na svakim vratima, izlozi živi (panes = `ShopBays.Length`).

---

## 3. Kako list postaje zgrada

### 3.1 Stajanje od modula (FORGE-004)

`ResidentialBlocks.StandSheet(sheet, root, way)`: koren nazvan potpisom lista; svaki modul kroz
`Composer.Raise(path)` (isti put kao park i dvorište), na ćeliju/sprat/yaw po konvenciji §0.2;
dekor iz lista. Zatim isto što i za požnjevenu kuću, deljenim kodom sa `StandUnit`: `Colourway`,
`StorefrontDressingSteps` (po `unit.Name` = potpis), `BuildingCutaway.Prepare` odložen dok dressing
ne završi. `StandUnit` dobija jednu granu za spot koji nosi list — **nijedan spot ga u ovom epicu
ne nosi**.

### 3.2 Registar generisanih jedinica

Tri mesta čitaju jedinice **po imenu ili iz `ResidentialUnits.All`** (§0.4). Sintetička jedinica
se upisuje u `ResidentialUnits.Generated` (po imenu = potpisu, deterministički); `All` ostaje
žetvena tabela, `Known = All + Generated` gde se traži „poznata". Ništa drugo nizvodno se ne dira.

### 3.3 Izložba (FORGE-005)

`Tools/City/Residential/Forge Showroom` → `Assets/Scenes/ForgeDemo.unity`: red listova L 4, 8, 11,
13 × N 3, 4, 5, natpis (L, N, seed, izlozi, stanovi = izlozi × N, komadi, `Empty`), crvena
pločica na svakim izmerenim vratima, `ReviewSceneCamera`. `gangsters_forge --seed N --length L
--floors F [--rebuild]` baci jedan list, ispiše kvarove i jedinicu, sa `--rebuild` ga stane.
**Bez pečenja, bez žetve, bez grada.** Korisnik gleda sam.

---

## 4. Šta se koristi od postojećeg

- Konvencija modula (žetva, izmerena 2026-08-26); `gangsters_measure`; `FacadeFinder.FrontOf`.
- `ResidentialUnit` (tip i polja), `StorefrontDoorCatalog` (vrata modula).
- `Composer.Raise/Sit/Prop`, `DemoAssetLoad`; `Colourway`, `StorefrontDressingSteps`,
  `BuildingCutaway` iz `ResidentialBlocks`.
- `ShopShowroom` + `ReviewSceneCamera` (obrazac izložbe); `PipelineCommands` (`[CliCommand]`,
  glavna nit, bez dijaloga).
- `Tools/CoreSim` (`Stubs.cs`, eksplicitan spisak fajlova) — `--facade`.
- Žetva `ResidentialHarvest` se **ne dira**; katalog ostaje 14 kuća.

---

## 5. Redosled rada (tiketi GAN-332)

| tiket | šta | dan |
|---|---|---|
| FORGE-001 **Atlas modula** | komanda meri svaki modul: kutija, lica, porodica stila, par ivica↔ugao krova, spoljni uglovi, deo stuba; piše `ResidentialModules.cs` + `Docs/residential-module-atlas.md`; nagoveštaji iz §0.2 se potvrđuju ili obaraju | 0.5 |
| FORGE-002 **Tabela dekora** | alat prolazi svih 14 kuća: prop → modul (ćelija + sprat), pomak, učestalost; relacije za štrik/ventilaciju/stub; udeo nevezanih po porodici, > 20 % = „nije spremna" | 1 |
| FORGE-003 **List i presuda** | `ResidentialFacade.Roll` + sintetička jedinica + `Faults` + `CoreSim --facade` + `gangsters_forge_tests`; 990 listova, 0 kvarova | 1.5 |
| FORGE-004 **Stajanje** | `StandSheet` deljenim kodom sa `StandUnit`; kolorvej, izlozi, cutaway; `ResidentialUnits.Generated` i dva čitača | 1 |
| FORGE-005 **Izložba i komanda** | `ForgeDemo.unity`, meni, `gangsters_forge`; korisnik gleda | 1 |
| FORGE-006 **Doc i zatvaranje** | `Docs/residential-forge.md`, ovaj plan → „kako je urađeno", memorija, epic Done | 0.5 |

Ukupno **5.5 dana**. FORGE-001 ∥ FORGE-002; FORGE-003 čeka oba i D1–D6; petlja gledanja izložbe
nije u proceni.

---

## 6. Nije deo ovog epica

- **Upotreba u gradu**: koji blokovi, klasa `Strip` u receptu, pečenje u katalog, prazan pojas
  MiniCoreDemo — korisnik odlučuje kasnije; građa u §8.
- Dve izmene recepta za redove (uzete, ali idu uz odluku o upotrebi).
- L oblik, unutrašnji ugao, brownstone, podignut deo krova, zgrade dubine ≠ 2, deoba na više
  zgrada (D6 → v2).
- Unutrašnjost radnje; nova kamera; save podaci.

---

## 7. Odluke

**Uzete 2026-09-04 (korisnik):** spratova nad prizemljem 3 do 5; oba ugaona modula; brownstone se
ignoriše; zadnji red = radnje; ugaone radnje na oba kraja; obim = samo forge.

Otvoreno (D1–D6 u epicu) — podrazumevani odgovor je **moja pretpostavka, nije odobreno**; pita se,
ne kodira:

| # | pitanje | podrazumevano |
|---|---|---|
| D1 | Stil po koloni: mešano 01/02/03 po koloni (kao Synty, izmereno na svih 8 prebrojanih) ili jedan stil po zgradi? | mešano, po učestalosti iz tabele |
| D2 | Vrata stanova: bar jedna kolona po redu, najviše jedna na 5 kolona? (izmereno 0–10 po kući; `residential-08` nema nijedna) | bar jedna po redu |
| D3 | Udeo stubova stepenica: bacanje iz izmerenog raspona 28–86 %, ili fiksno? | bacanje iz raspona |
| D4 | Brendirani znakovi (`LargeSign_Pizza`, `Sign_Barber`, `Sign_XXX`…) govore koji je posao, a posao dodeljuje simulacija. Po tabeli, ili samo neutralni (`Sign_Attachment`, tende, bilbordi, `Sign_Hotel`)? | neutralni |
| D5 | „3 do 5 spratova" = nad radnjama (ploča 12 / 15 / 18 m)? Synty ima 3 nad radnjama + podignut četvrti na 2–3 ćelije | nad radnjama; bez podignutog dela |
| D6 | Jedna zgrada po listu, ili 2–3 zgrade (npr. 4+7) sa zajedničkim zidom, svaka sa svojim N i stilom? Kit nema slepi bočni zid za visinsku razliku | jedna zgrada; deoba v2 |

---

## 8. Građa za odluku o upotrebi (izmereno 2026-09-04, nije posao ovog epica)

Ovo je zapisano da ne bi moralo ponovo da se meri kad korisnik bude odlučivao gde zgrade idu.

### 8.1 Baseline recepta (`Tools/CoreSim --residential --seed 1 --count 30`)

| mera | vrednost |
|---|---|
| čisti blokovi | **106 / 120** (corner 30/30, row 30/30, block 20/30, court 26/30) |
| odbijeni ćoškovi | **84** („no unit faces both streets and fits") |
| kuća stalo | 430; `residential-07` 20.7 %, `residential-01` 18.8 %, `residential-16` 17.9 % (07 i 16 su ista šipka 2×5 dva puta) |
| kuće u redu | **0–1 po jedinici** — svaka kuća stoji na ćošku |

Redovi su zatvoreni receptom, ne katalogom: (1) red počinje ćeliju dalje od ugaone kuće
(`Units`, `ResidentialLot.cs:1256`), a `ClearBetween` (`:1064`) odbija razmak 1–2 ćelije; (2)
„nikad druga radnja pored prethodne" — korisnikovo pravilo 2026-08-28 (`:1305`); (3) `least`
(`:675`) samo iz `Kind == Corner`. Korisnik je uzeo izmene (1) i (3); pravilo (2) ostaje.

### 8.2 Trake u kvartovima (`--homes` nad celim gradom, seed 1987 = MiniCoreDemo)

| mera | seed 1987 | 30 seedova |
|---|---|---|
| rezidencijalnih blokova | 146 | 4 885 |
| od toga traka (unutrašnja dubina ≤ 4 ćelije) | **82 (56 %)** | 2 694 (55 %) |
| dubina / dužina traka | 2: 37 · 3: 25 · 4: 20 / 11–13 ćelija (55–65 m) | isto po udelu |
| traka izgrađena kućama | 53 % ćelija — 1–2 kućice na krajevima, sva 4 ćoška odbijena, sredina popločana | 58 % |
| izloga danas → sa šipkom L × 2 po traci | 3 913 → ≈ 4 728 (**+21 %**) | 130 076 → +19 % |
| široki blokovi, pokrivenost kućama | < 30 %: 5 · 30–50 %: 51 · ≥ 50 %: 8 | 126 · 1 860 · 205 |

### 8.3 Prazan pojas u MiniCoreDemo (korisnikova slika, 2026-09-04)

Pojas između Whitney Ave i Dunlap St preko bulevara: **pojas parka** između jezgra i kvarta iz
plana prstena (`CoreLayout.Ring.LandBelt`, 10–12 ćelija, `plan.BeltParks`). Rig MiniCoreDemo
(`quarterBudget = 2`: „Downtown + Landward") ga pri sečenju odbaci (`CoreDistrict.KeepQuarters`,
`Drop(_plan.Parks, keptGround)` — park nije unutar zadržanog tla nijednog kvarta), a putevi se
čitaju ponovo pa trotoar ostane. Živa scena: 0 objekata `park`. U punom gradu bi bio zelen. Šta tu
ide — park, zgrade, ništa — je korisnikova odluka.

---

## Provere

| provera | rezultat |
|---|---|
| atlas: svaki modul ima kutiju, lice, porodicu; spoljni uglovi i par ivica↔ugao potvrđeni merom | — |
| tabela dekora: svaki prop 14 kuća vezan ili prijavljen kao nevezan; udeo po porodici | — |
| `CoreSim --facade` 990 listova, `Faults == 0`, raspodela u rasponu, ponovljivost | — |
| `gangsters_forge_tests` zelen (12 ugovora) | — |
| izložba: 12 listova, 0 komada van otiska + prepusta, natpisi, pločice, izlozi živi | — |
| `recompile_status` čist; `StorefrontContractTests`, `BusinessFoundationTests`, `CoreVacancyTests` zeleni (nizvodno netaknuto) | — |

## Rizici koje plan vidi

- **Tabela dekora je jedini alat bez presedana** (FORGE-002): propovi su ravna braća, vezivanje je
  pretraga; porodica sa > 20 % nevezanih ostaje van lista i to se kaže.
- **Spoljni uglovi po stilu**: ako stil 02 nema spoljni ugao, list mora da zna da kolona stila 02
  ne može biti ugaona — atlas pre lista, ne posle.
- **Tri mesta po imenu** (§3.2): propušteno jedno = zgrada bez izloga u kešu ili na mapi.
- **Budžet komada**: L=13, N=5 ≈ 310 komada — koliko `residential-03`; meri se na izložbi.

## 9. Šta je contrarian rekao (v1, 2026-09-04) i šta od toga važi za forge

Presuda nad v1 („peci u katalog"): doraditi. Provereno i ugrađeno: vrsta jedinice po yaw-u (→
sintetička jedinica je `Through` po konstrukciji, ne po pretpostavci), stanovi = izlozi (→ §0.4,
§8.2), tabela dekora iz 14 sa relacijama i udelom nevezanih (FORGE-002), spoljni uglovi iz atlasa
(FORGE-001), merilo po obliku i diff (→ §8). Nalazi o imenima žetve, kandidatima i katalogu koji
menja svaki seed su **otpali sa pečenjem**: forge ništa ne peče. Najjeftiniji test koji je
predložio (`dotnet run -c Release -- --residential …`) je proširen u `--homes` meru §8.2.
