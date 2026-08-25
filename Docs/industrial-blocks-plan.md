# Industrijski blokovi za jezgro (IndustrialBlockForge)

> Plan napisan i **implementiran 2026-08-25**. Alat radi; četiri kandidata stoje u
> `Assets/Scenes/IndustrialLab.unity`, probni bake je prošao i obrisan.
>
> U jednoj rečenici: editor alat digne **4 kandidata industrijskog bloka odjednom**, korisnik
> ih pogleda i kaže „ispeci 1 i 2, ignoriši 3 i 4", a alat izabrane snimi kao prefabe u
> `Assets/Prefabs/CoreBlocks/` **kroz postojeći bake jezgra** (`CoreBlockTray`), tako da su
> nerazlučivi od 16 blokova koje smo požnjeli iz Synty demoa.

| šta | gde |
|---|---|
| generator | `Assets/Scripts/Editor/IndustrialBlockForge.cs` |
| radna scena | `Assets/Scenes/IndustrialLab.unity` |
| meni | `Tools/City/Core/Industrial/…` |
| komanda | `unity command gangsters_industrial` (`Assets/Scripts/Editor/PipelineCommands.cs`) |
| izlaz | `Assets/Prefabs/CoreBlocks/ind-<recept>-NN.prefab` |

---

## 1. Radni tok

```bash
# 1. četiri kandidata (otvara/pravi lab scenu sam ako je tekuća scena čista)
unity command gangsters_industrial --seed 7 --recipe all
#    → [{index, recipe, seed, width, depth, pieces, gaps}, …]

# 2. pogledaj ih
unity command screenshot --view scene

# 3. ispeci izabrane, ostale baci
unity command gangsters_industrial --bake 1,2 --names ind-works-01,ind-depot-01
#    → [{index, name, path, wrote, trouble}]
```

- `--recipe works|depot|yard|strip` pravi 4 varijante istog recepta; `all` (podrazumevano) po
  jednog od svakog.
- bez `--names`, ime je `ind-<recept>-NN` (prvi slobodan broj u folderu). Postojeće ime se
  **odbija** (`trouble`), ne prepisuje.
- `--keepOthers true` ostavlja neizabrane kandidate da stoje.
- Meni ekvivalenti: `Open The Industrial Lab`, `Generate Four Candidates`,
  `Bake Candidate 1..4`, `Discard Candidates`.

Posle bake-a: tray se ukloni, scena se snimi, a ako u sceni stoji review red
(`Tools/City/Core/Show Baked Blocks`) osveži se — tu se novi blok vidi u istom redu sa 16
Synty blokova, što je i prava provera stila.

---

## 2. Šta „u stilu CoreHarvest blokova" znači (izmereno)

| pravilo | vrednost | izvor |
|---|---|---|
| Rešetka | **5 m**, svi pivoti na višekratnicima 5, y = 0 | `CoreLayout.Cell` |
| Blok nosi SVOJ trotoar | prsten od **jedne pločice 5 m** oko cele kutije | `Docs/synty-demo-anatomy.md:50` |
| Put nije deo bloka | kolovoz polaže `CoreRoads` po razmaku između blokova | `CoreRoads.cs:58` |
| Pod miran | jedna pločica po površini, bez rotacija; **najviše dve površine** | memorija `block-floor-calm` |
| Nema praznog prostora | svaki kutak je parking / radna površina / roba | anatomija §3 |
| Rotacija bloka | uvek 0° (`CoreLayout.Place` postavlja identitet) | `CoreLayout.cs:241` |
| Pivot prefaba | sredina ground kutije, snap na 5 m; X/Z centrirani, **Y netaknut** | `CoreBlockTray.Bake` |
| Hijerarhija | **ravna** — svaki komad direktno dete korena | `CoreLayout.Measure` |
| Koren nosi | samo `BlockLotTag` (bake ga sam dodaje) | `CoreBlockTray.cs:690` |

**Ne mešati sa grid-gradom**: tamo ulica sama polaže trotoar od 6.5 m (`StreetKit`) i blok je
samo unutrašnjost lota; izlaz ide u `Assets/CityKit/Blocks` i čita ga `RoadDemoBuilder`. Ovde
blok nosi 5 m ivičnjak u sebi, izlaz je `Assets/Prefabs/CoreBlocks`, čita ga `CoreLayout`.

### Izmerene mere pločica (2026-08-25)

| pločica | mere | y raspon |
|---|---|---|
| `SM_Env_Sidewalk_01` (pod) | 5.000 × 5.000 | 0 … **0.034** |
| `SM_Env_Sidewalk_Straight_01` (ivičnjak) | 5.000 × 5.000 | −0.230 … **0.070** |
| `SM_Env_Sidewalk_Corner_01` | 5.000 × 5.000 | −0.230 … 0.070 |
| `SM_Env_Sidewalk_Dip_01` | 5.000 × 5.000 | −0.230 … 0.070 |
| `SM_Env_Road_Bare_01` (asfalt) | 5.000 × 5.000 | 0 |
| `SM_Env_Road_ParkingLines_01` | **10.000 × 5.000** | 0 |

Sve pločice se polažu na **y = 0**, kao u demou. `Deck` (visina na koju staju zgrade i props)
= vrh pod-pločice + 2 cm = **0.054** (isti razmak koji luka daje halama).

### Okretanje ivičnjaka — PROVERENO, ne pogađati

`StreetKit` polaže južni trotoar ulice na yaw 0, što znači da je **ivičnjak-kamen na +Z
strani pločice** pri yaw 0. Odatle za blok (put je spolja):

| strana bloka | put je na | yaw |
|---|---|---|
| jug | −Z | **180** |
| sever | +Z | **0** |
| zapad | −X | **270** |
| istok | +X | **90** |

Ugaona pločica pri yaw 0 nosi kamen na **+X i +Z**, pa za blok: **NE = 0, SE = 90, SW = 180,
NW = 270**. (Izvedeno iz `StreetKit.LayCrossroads:370-373` i potvrđeno u sceni.)

---

## 3. Građa (sve osim `Assets/SimpleAirport`)

### Zgrade — gotove, pečene, pročelje na +Z, pod na y = 0
`Assets/CityKit/Buildings/` — mere izmerene 2026-08-25 (X × Y × Z):

| prefab | mere | uloga |
|---|---|---|
| `building-factory` | 24.39 × 11.66 × 15.39 | pročelje ulice (cigla, vodotoranj na krovu) |
| `building-factory-old` | 22.47 × 12.61 × 12.39 | pročelje/ćošak (cigla + dimnjak) |
| `building-factory-hall` | 21.36 × 8.01 × 9.97 | hala u zaleđu |
| `building-workshop` | 15.39 × 4.09 × 12.39 | radionica |
| `building-warehouse-large` | 32.73 × 13.18 × 40.00 | glavna hala depoa (**krov ima svetlarnike**, nije bez krova) |
| `building-warehouse-small` | 22.74 × 7.41 × 17.74 | red hala |
| `building-depot-garage` | 25.24 × 7.83 × 17.50 | garaža sa roller vratima |
| `building-yard-shed` | 6.89 × 3.93 × 6.14 | portirnica uz kapiju, **nikad u redu** |
| `building-warehouse` | **70.70 × 13.41 × 61.50** | ceo GangWarfare kompaund — prevelik za blok < 85×75, zasad se ne koristi |

### Dvorište
`SM_Bld_Fence_01` (3.00 × 2.92 panel) · `SM_Bld_Fence_Wire_01` (**nije ograda** — 0.775 m
bodljikava kruna, pivot na 3.164) · `SM_Bld_Fence_Gate_01` (6.03 × 1.03) ·
`SM_Bld_Fence_Pole_01` · `SM_Bld_LoadingDock_02` (žuta zaštitna ograda, 6.00 × 3.24) ·
palete/bačve/kolutovi/cevi/dumpster/bandera/znak iz `PolygonGangWarfare/Prefabs/Props` ·
viljuškar i kamion iz `…/Vehicles` · sanduk `PolygonGeneric` · bure-vatra i kupa `PolygonPalmCity` ·
betonska barijera `PolygonCity` · kontejneri `Assets/CityKit/Ships/container-20-*` (3.36 × 3.21 × 6.38).

**Zabranjeno:** `Assets/SimpleAirport`; iz `PolygonMapsMilitaryWarehouse` samo bodljikava žica
(ostalo je sivo, atlas fali); `SM_Env_Background_Crane_*` (kulisa); uspavani
`Assets/Scripts/Generation/Industrial*.cs` + `PortLayout.cs` (pišu za polyperfect katalog koji
ne postoji — **ne dirati**).

---

## 4. Četiri recepta

Blok je **maska ćelija 5 m**, ne pravougaonik — zato `strip` može da ima usek. Prsten ivičnjaka
= rubne ćelije maske; unutrašnjost = ostalo.

`--recipe all` deli prva četiri; `depot` se dobija samo imenom (`--recipe depot`) jer je
magacin, a stovarište već postoji u četvorci.

| recept | mere | zid | šta je |
|---|---|---|---|
| `works` | 75/90 × 60/75 | cigla | dve ciglane + radionica **popune celu ulicu**, kapija u procepu, hala u dnu, **zidani dimnjak** iza kotlarnice, vodotoranj, ostava, roba u redovima |
| `plant` | 90/105 × 60/75 | cigla | dve procesne hale u dnu sa **dimnjakom u procepu između njih**, fabrika i radionica na ulici, dug zid sa kapijom između njih, 2 utovarne rampe, cevni riseri |
| `yard` | 90/105 × 60/75 | **žica** | red hala u zaleđu, do 3 reda kontejnera (visina 1–3, 35 % „jedan brodar = jedna boja"), viljuškar |
| `strip` | 60/75 × 45/60 | cigla | radionica + hala na ulici, prolaz između njih, **usek 20 × 10 m u SI ćošku** koji ulica preuzima kao parking |
| `depot` | 75/90 × 75 | žica | velika hala uz zadnju liniju, 2 rampe, manevarski plato, portirnica, parking trake |

Zajednička pravila: pročelje = južna ivica; **jedan prolaz na jug**, oba krila kapije otvorena
u dvorište; props se odbijaju ako nema mesta (`Room`) i **nikad ne staju na prsten**.

### Površine: dvorište je asfalt

Jedno pravilo za sve blokove (`Block.Surfaces`), ne po receptu:

- **unutrašnjost = asfalt**;
- **pločnik samo na dva mesta**: pojas uz unutrašnju stranu ograde (ono po čemu se hoda), i
  apron od jedne ćelije oko svake zgrade;
- **apron pobeđuje prilaz**: zgrada koja stoji pola na pločniku a pola na putu nije ni jedno ni
  drugo. Prilaz seče samo pojas uz ogradu — iza njega je dvorište ionako asfalt, pa nema šta da
  se seče.

Zgrade se povlače `Setback` od ivice; **svaka jedna** mora da koristi `block.In/Out/Far`, ne
`Cell`. Jedna promašena linija (`building-factory`) ostavila je zgradu tačno na zidu i osam
panela je prolazilo kroz nju. Provera je u probi: presek otiska zgrade sa panelima zida mora
biti **0**.

Bilo je obrnuto — pločnik svuda, pa po jedan pravougaonik asfalta po receptu — i svaki blok je
imao ploču čistog pločnika nasred radnog dvorišta, a odluka koja je ista za sve stajala je u pet
recepata.

### Zid, a ne žica

Perimetar je **zid od cigle** (`SM_Bld_Fence_Brick_01` 3.00 × 3.00 × 0.51 +
`SM_Bld_Fence_Brick_Pillar_01`), kao svaka druga ograda u ovom gradu. Bodljikava žica
(`SM_Bld_Fence_01` + kruna `SM_Bld_Fence_Wire_01`) ostaje **samo za stovarište i depo** — to su
jedina dva mesta gde je i u stvarnosti žica.

**Zid je zatvoren prsten. Jedini prekid je kapija.** Prva verzija ga je sekla svuda gde zgrada
stoji uz liniju („zgrada je ionako zid"), i rezultat je bio perimetar sa rupama: svuda gde se
dve zgrade sretnu pod uglom ili gde jedna stane pola metra kratko, ostane procep. Umesto toga
zgrade se **povlače 1.6 m unutra** (`Setback`, `Block.In/Out/Far`) i zid prolazi ispred njih.

**Ćoškovi se ne preklapaju i ne zjape.** Kraj poteza gde blok PRESTAJE je spoljni ugao: potez
staje jednu ćeliju pre njega, pa se dve strane sretnu umesto da se ukrste. Kraj gde se blok
nastavlja na drugoj dubini je **unutrašnji ugao useka** — tamo se dva poteza uopšte ne sreću,
nego se mimoiđu za pet metara — pa se potez tu produžava za ćeliju. Bez prvog svaki blok nosi
dva panela jedan kroz drugi na sva četiri ugla; bez drugog svaki usek ima rupu na stepenici.
Oba su u `Block.Corner`.

Kapija ostavlja **koridor**: u stovarištu nijedan red kontejnera ne zatvara traku na koju kapija
izlazi, inače je dvorište dvorište u koje se ne može ući.

**Ulaz je samo jedan, prekida trotoar, i put od njega unutra je NEPREKINUT.** Ćelije prstena na
koje kapija izlazi gube pločicu ivičnjaka i dobijaju **`SM_Env_Road_Bare_01`**; odatle prilaz
ide pravo unutra sve dok na njega nešto ne stane (zgrada, obeležen parking) ili dok ne stigne do
suprotnog ivičnjaka — izmereno 40–55 m po bloku.

Zato `Way` nije polje nego **svojstvo**: postavljanje odmah polaže prilaz (`Block.Corridor`) i
**rezerviše to tlo**, pa se posle na njega ne spušta ni props ni kontejner. Prilaz se uz to
**nikad ne prepokriva** — `Pave` preskače njegove ćelije, inače depo koji posle asfalta popločava
svoj apron prebaci ploču preko puta.

Odbačene verzije: `SM_Env_Sidewalk_Dip_01` (spuštena ivica nije prekid pločnika) i „dve ćelije
asfalta iza kapije" (dalo je *malo put, malo pločnik, malo put* — prilaz koji se prekine na pola
nije prilaz).

**Krila kapije stoje UNUTAR otvora.** Zabačena na drugu stranu, svako je ulazilo pola metra u
zgradu pored — a kapija se po pravilu i seče između dve zgrade. To se odozgo ne vidi, pa se i to
sada broji: `wallInBuilding` = koliko komada ograde (panel, stub, krilo) stoji unutar otisка
zgrade. Mora biti **0**.

**Rupe se mere protiv TLA, ne protiv onoga što je zid tražio.** `Block.WallGap` računa obim
zemljišta koje zid opasuje (ćelije unutar prstena) minus kapija, pa oduzima položene metre.
Prva verzija je poredila položeno sa *traženim* — što nije provera: strana koju graditelj poteza
nikad nije izračunao je strana koju nikad nije ni tražio, pa je rupa na unutrašnjem uglu useka
prošla kao „0". Uz to, potez kraći od jednog
modula se sada **razvuče na modul i pusti da prepusti**, umesto da ostane nepokriven — to je bio
poslednji izvor rupa koje se u screenshot-u ne vide dok se ne traže.

### Dimnjak

`Block.Chimney(x, z, visina)`: **visok okrugli dimnjak koji se sužava** — bubnjevi (Unity valjak)
od poluprečnika 2.3 m u dnu do 1.15 m na vrhu, po ~2.2 m, plus kruna koja malo prepušta.
`works` 22–27 m, `plant` 26–32 m. Boja: ravna cigla-crvena, `Assets/Materials/IndustrialStack.mat`.

Ravna boja bez teksture ovde **nije kompromis**: svaki Synty komad u bloku je ravno polje sa
atlasa, pa se ravna cigla-crvena među njima uklapa tačno — a okrugло okno ionako nema UV-ove
kojima bi pogodilo ciglu na tom atlasu.

Dva odbačena pokušaja: naslagana villa-kapa (`SM_Bld_Villa_Chimney_01` × 6) — metar široko okno
sa kapom na svaka 2.5 m čita se kao stub sanduka; i kvadratno okno od `SM_Bld_Wall_Brick_01`
panela — kutija, koja se ne može suziti bez skaliranja panela ispod originala, što ovaj projekat
ne radi.

Ako dimnjaku nema mesta, `Chimney` to **kaže u konzoli** umesto da blok tiho ostane ravan (jednom
je `plant` ostao bez dimnjaka jer mu je prilaz prolazio kroz to mesto).

Na vrhu dimnjaka stoji **dim**: `FX_Smoke_Black_Small_01` (PolygonParticleFX, isti koji luka
koristi za brodske dimnjake), skaliran ×2.2, `playOnAwake`. Postavlja se **ručno na koordinatu**,
ne kroz `Sit` — particle renderer prijavljuje kakve god granice drži dok ne svira, pa bi ga
sedanje na njih odnelo bilo gde. U Scene view-u se ne vidi bez `ps.Simulate(...)`; u igri svira sam.

Ostala „fabrička" oprema: `SM_Prop_Pipe_Preset_01` (4.46 × 7.05, cevni riser uz zid hale),
`_02` (4.07 × 6.91), `SM_Prop_Water_Tower_01` (2.42 × 5.51), `SM_Bld_LoadingDock_02`.

### Roba u redovima, ne samo rasuta

`Ranks(...)` slaže bačve/palete u redove uz zid (12 % šanse da jedno mesto fali), pored
`Scatter` koji ih rasipa. Dvorište u kome je sve rasuto izgleda kao smetlište; dvorište u kome
je sve u redu izgleda kao render. Treba oboje.

---

## 5. Kako je napravljeno (i zamke koje su plaćene)

- **Postavljanje se MERI, ne izvodi iz pivota.** `Lay()` instancira, okrene, skalira, pa pomeri
  po izmerenim `Renderer.bounds` tako da otisak padne tačno na traženi pravougaonik. Pločice
  pivotiraju u ćošku, zidovi na kraju, i koji je to ćošak menja se sa okretanjem — merenje je
  jedini odgovor tačan za sve.
- **`PrefabUtility.InstantiatePrefab`, uvek.** Bake replay-uje override-e na svežu instancu
  izvora; goli `Object.Instantiate` bi dao dubok primerak. Provereno: 300/300 dece pečenog
  prefaba je povezano na izvor.
- **Ništa se ne skalira ispod originala.** Ograda deli razmak na **cele module i razvlači**
  (`floor`, ne `round`); stretch kraći od jednog modula se preskoči. Faktor u granici 0.5 %
  se zaokruži na tačno 1 (`Whole`), da prefab ne nosi scale 0.9999996.
- **Seed:** `System.Random(seed*97 + k*31 + recept*7)` po kandidatu. Nigde `UnityEngine.Random`.
- **NIKAD ne zovi `CoreBlockTray.BakeNow()` iz komande.** Uvek završi
  `EditorUtility.DisplayDialog` — modalni prozor koji zaustavi main thread editora dok neko ne
  klikne OK; svaki sledeći pipeline poziv onda puca na „Main thread operation timed out".
  Ovo se desilo 2026-08-25 i editor je morao da se odblokira spolja. Zato postoji
  `CoreBlockTray.BakeQuietly(scene, out said)` — isti bake bez dijaloga. Bake bez dijaloga
  traje **~2 s**.
- **Lab je svoja scena**, ne `CoreHarvest.unity`: harvest scena nosi ceo Synty demo, snima se
  sekundama, i bake koji na kraju snimi scenu tamo probije pipeline tajmaut.
- **Lab se učitava ADITIVNO, pored onoga što je otvoreno** (`IndustrialBlockForge.Lab()`), i
  aktivan je samo dok posao traje (`InTheLab`), pa se vraća prethodna aktivna scena. Sve što
  editor alat digne — privremena instanca za merenje prefaba, tray — sleti u AKTIVNU scenu, pa
  je to jedini način da ništa ne završi u tuđoj. `Generate` odmah snimi lab.
  Razlog: 2026-08-25 je druga sesija radila u `CoreDemo.unity` sa nesnimljenim izmenama i alat
  je dvaput ostao blokiran (a jedan set kandidata koji je živeo samo u memoriji je nestao kad
  je scena zamenjena). Prva verzija je odbijala da radi dok je tuđa scena prljava; aditivna
  ne mora ništa da odbija.
- Play mode i dalje blokira: `gangsters_industrial` odbija dok editor svira.
- Izmene u postojećem kodu, minimalne: `CoreBlockTray.MakeTray` (tray pod zadatim imenom),
  `CoreBlockTray.BakeQuietly`, izuzimanje korena kandidata iz `Sweep` (samo tamo — merenje
  ivica scene ih i dalje vidi, da review red i novi tray ne slete na njih), i
  `CoreLayout.Measure` sada broji i `building-` prefiks kao zgradu.

---

## 6. Stanje i provere (2026-08-25)

| provera | rezultat |
|---|---|
| kompajl | čist |
| četiri kandidata, seed 7 | works 75×60 (254 komada), plant 90×60 (321), yard 90×60 (399), strip 60×45 (138) |
| rupe u podu | **0** u sva četiri |
| nedostajući prefabi | nijedan |
| pečen prefab | 300 dece, **300 povezano na izvor**, 0 skalirano ispod 1, koren = `Transform + BlockLotTag` |
| kutija pečenog | 75.14 × 60.14, pivot u sredini, dno na −0.23 (kamen ivičnjaka) |
| bake dva odjednom + odbacivanje ostalih | radi; tray uklonjen, scena snimljena |
| review red sa 16 Synty blokova | novi blokovi se čitaju kao ista porodica |
| trajanje bake-a | ~2 s po bloku |

**Poznato i ostavljeno:** `BlockLotTag` pečenog bloka kaže 80 × 65 za blok 75 × 60, jer
`CoreBlockTray.Bake` zaokružuje ground kutiju naviše na peticu a prsten prelazi 7 cm preko.
Svi požnjeveni blokovi imaju istu osobinu (isti kod), a `CoreLayout` ionako meri sam — pa se
zajednički bake ne dira.

**Nije deo ovog zadatka:** gde industrijski blokovi stoje u jezgru/gradu (`CoreLayout.Blocks`
je fiksna tabela; grid grad bira bake po veličini lota, ne po zoni), život u bloku (blok je
statičan bake, kao i ostalih 16), benzinska/silos/rezervoar (nema modela — Town ima samo
`SM_Prop_Gaspump_*`).
