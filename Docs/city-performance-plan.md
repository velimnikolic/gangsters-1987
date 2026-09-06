# Plan: da grad u CoreDemo stane u budžet (perf, korak 1)

> Predlog 2026-08-27. **Sve brojke dole su izmerene**, ne procenjene: statika iz otvorenog
> editora (`Logs/citycount.txt`), Play iz `UnityStats` i konzole, seed 1987. Kod na koji se
> oslanja: `ScenePerf` (senke, cull slojevi, merge), `MergedChunk` (račun merge-a),
> `StreetCutaway` (blizak zum), `StandaloneDistrictHost` (perf prolaz za CoreDemo),
> `CoreDistrict` (šta ide pod koji koren).
>
> U jednoj rečenici: **grad radi, ali plaća sve odjednom** — 163 hiljade objekata, 99 miliona
> temena, 8,5 GB u Play-u, i kamera svakog frejma šalje 33 miliona temena jer se **zgrade crtaju
> do daljine od 2.500 m, bez ijednog LOD-a**. Najveći deo se dobija bez ijedne promene u izgledu.

> **L6 je implementiran 2026-08-28 za generisane residential blokove.** RecyclerView-style
> model, camera window, LRU/pool, 180 m handoff postojećoj 2D mapi i runtime invalidacija opisani
> su sa merenjima u `Docs/core-streaming.md`. Brojke ispod ostaju pre-streaming baseline.

---

## 0. Mera: šta grad danas košta

### Statika (crtež seed 1987 u editoru)

| gde | objekata | temena |
|---|---:|---:|
| **homes** (178 rezidencijalnih blokova) | **111.397** (68 %) | **84,1 M** (85 %) |
| roads (kolovoz + trotoar) | 29.817 (18 %) | 3,0 M |
| parks | 14.637 | 7,0 M |
| jezgro (19 pečenih blokova) | ~4.600 | 4,2 M |
| quays + reka | 2.916 | 1,0 M |
| **ukupno** | **163.272** | **99 M**, 120 materijala |

### Play (CoreDemo, ista seed, kamera na 320 m)

| mera | vrednost |
|---|---|
| merge | 153.603 komada, 90,6 M temena → **2.189 spojenih mreža**, 126 chunk-ova od 120 m |
| vertex baferi merge-a | **4.611 MB → 2.767 MB** (tangente i lightmap UV već izbačeni) |
| kanali | Position 1.037 MB, Normal 1.037 MB, TexCoord0 691 MB |
| ukupno zauzeće | **8,5 GB** (mono 989 MB) |
| po frejmu | **18,8 M trouglova, 33,3 M temena**, 2.496 draw call-ova, 134 setPass |
| ravnih koji ne bacaju senku | 68.914 (perf prolaz radi) |
| gašenje po daljini | 41.751 sitnica na 230 m, 49.548 drveća/bandera na 480 m |
| greške | 0 grešaka, 0 izuzetaka (sim harness 30 s) |

### Odakle temena

- **Katalog nije zavaren.** Jedna rezidencijalna jedinica je Synty kitbash od **113–328
  zasebnih mreža**: `residential-04` = 113 mreža / 120 k temena, `residential-03` = 328 / 272 k.
  Žetva ih je uzela kao gomilu zidnih modula, požarnih stepenica, klima-uređaja i saksija.
- **Dve zgrade nose grad.** Izvedeno iz brojanja mreža u sceni: `residential-04` stoji ~338 puta
  (41 M temena), `residential-01` ~136 puta (21 M) — zajedno **~88 % svih temena kuća**.
- **Nijedan LODGroup u celom katalogu.** Synty ne isporučuje LOD za zgrade (samo za rastinje), a
  žetva je i od palmi uzela `LOD0` i ostalo bacila.
- **Zgrade nemaju daljinu.** Sitnica se gasi na 230 m, drveće i banderi na 480 m — a zgrade su na
  sloju 0 i crtaju se **do far plane-a na 2.500 m**. Ceo grad, svaki frejm.
- **URP koristi samo kutijasti layer cull.** `layerCullDistances` ostaje aktivan, dok se
  `layerCullSpherical` sada postavlja samo u built-in pipeline-u, bez SRP warning-a.
- **156 vrsta mreža se uopšte ne spaja** jer im import zabranjuje čitanje — svaka ostaje sopstveni
  renderer (`Logs/unreadable-meshes.txt`).
- Trotoar je 19.000 pločica za 2 M temena — od svega **4 različite mreže**.

### Šta NIJE mereno

**Frejm.** Play harness (`Tools/play/run.ps1`, `gangsters_play`) namerno **gasi kameru, senke i
zvuk** — meri simulaciju, ne sliku. Nikad niko nije izmerio fps ovog grada; brojevi gore (draw
call-ovi, trouglovi, memorija) su ono što se pouzdano čita dok je editor u pozadini.

---

## 1. Poluge, redom po izmerenoj koristi

| # | poluga | šta danas košta | šta se dobija | menja izgled? |
|---|---|---|---|---|
| **L1** | **Zavariti katalog pri bake-u** — svaka `residential-*`, `park-*`, blok-prefab u jednu mrežu po materijalu | 113–328 objekata po jedinici | **163 k → ~30 k objekata**; merge dobija 4× manje komada; scena se gradi i editor se kreće znatno brže | **ne** |
| **L2** | **Daljina za zgrade** — zgrade dobijaju svoju cull distancu, chunk se gasi kad izađe iz kruga | 33,3 M temena po frejmu | **~8–12 M po frejmu**; draw call-ovi padaju sa 2.496 | **da** — traži odluku |
| **L3** | **LOD ili impostor za daleke blokove** — decimirani LOD1/LOD2 pri žetvi, ili pečeni „krovni otisak" po bloku | nema ničega, sve je LOD0 | još 2–3× na dalekoj polovini ekrana | **da** — traži odluku |
| **L4** | **Instancirati ponavljače** umesto spajanja: trotoar (19 k / 4 mreže), banderi 2.231, stolice 1.560, parking-satovi 849, palme 482 | ~25 k objekata, ~6 M spojenih temena | tih 25 k objekata i 6 M temena nestaju iz merge-a; po jedan draw call na vrstu | **ne** |
| **L5** | **Sabiti vertex format** merge-a: Normal → SNorm8, UV0 → Float16 | Normal 1.037 MB, UV0 691 MB | **2.767 MB → ~1.700 MB** | **ne** |
| **L6** | **Ploče i streaming** — gradi i drži samo ploče u krugu oko kamere | ceo grad je uvek u memoriji | jedino što skalira kad grad opet poraste; i vreme gradnje pada | **ne** |
| **L7** | **Granica senki** — shadow distance i kaskade po stvarnom zumu | senke se računaju daleko van onoga što se vidi | jeftino, meri se odmah | malo |
| **L8** | **156 nečitljivih mreža** — jedan prolaz kroz importere | ostaju sopstveni renderi, van merge-a | 156 vrsta ulazi u merge | **ne** |

---

## 2. Predloženi redosled

**Korak A — bez ijedne odluke i bez promene izgleda: L1 + L4 + L5 + L8.**
Očekivanje: objekata 163 k → ~30 k, memorija 8,5 GB → ~4 GB, merge nekoliko puta brži, vreme
crtanja scene sa ~40 s naniže. Ovo je čist dobitak i ne dira ni jedan piksel slike.

**Korak B — traži tvoju odluku: L2 + L3.** Daljina i daleki blok su pitanja izgleda, ne koda.

**Korak C — L6 streaming**, kad se zna koliko grad još raste (industrija, luka, aerodrom još nisu
u ovoj sceni).

**Nulti korak, pre svega**: harness dobija zastavicu koja ostavlja kameru upaljenu i piše frame
time u `summary.json`, pa svaka poluga ima presudu u brojkama, a ne u priči.

---

## 3. Kako se sudi

- statika: `Logs/citycount.txt` — objekti i temena po korenu (postoji, jedan eval)
- Play: draw call-ovi, trouglovi, temena, memorija iz `UnityStats` + konzolni izveštaj merge-a
- prag: **bez izmerenog fps-a nema praga** — zato nulti korak

---

## 4. Pitanja za korisnika

1. **Šta se vidi na ivici vidljivosti?** Izmaglica (grad bledi u daljini) ili grad prosto staje na
   ivici ekrana? Bez odluke, L2 se ne dira.
2. **Kako izgleda daleki blok?** Krovni otisak (jeftino, odozgo se skoro ne razlikuje) ili prava
   pojednostavljena kuća?
3. **Koji je cilj?** Koliko fps na tvojoj mašini, na kojoj rezoluciji — to je prag za sve gore.
4. **Da li da se pobere još 4–6 kuća?** Sada dve zgrade nose ~88 % grada; više kuća je i lepše i
   daje prostor da se jeftinije kuće stave tamo gde ih niko ne gleda izbliza.

---

## 5. Izmereno 2026-09-06: frejm je CPU, ne GPU

Živi Play (korisnikova sesija, 3D pogled, Game view ~3000×1770, RTX 5080), Unity Profiler
očitan kroz `eval` bez diranja Play-a, prosek 150 frejmova:

| gde | ms/frejm |
|---|---:|
| **glavna nit ukupno** | **~20** (23 sa uključenim profilerom) |
| PlayerLoop | 16 |
| ├ BehaviourUpdate | 4,5 — `DemoCrews.Update` 2,9 (od toga `Crews.Combat` 2,3), `RoadDemoBuilder.Update` 1,1 |
| ├ LateBehaviourUpdate | 7,3 — **`DemoCrews.LateUpdate` 6,4**, od toga `ApplyWorldFog` **5,5** |
| ├ render loop (URP, glavna nit) | 1,7 |
| ├ WaitForRenderJobs + UpdateAllRenderers | 1,2 |
| EditorLoop (Game view blit itd.) | 3,6 — nema ga u buildu |
| render nit | 5,3 (čeka glavnu nit 17 ms) |

GPU nije usko grlo: render nit i GPU čekaju glavnu nit. Scena: 159 k renderera, 1.740
svetala, 542 animatora, 449 pešaka, 162 auta, 58 jedinica / 120 ljudi, 35–55 M trouglova.
GC alokacije skripti ~2 KB/frejm. Sekcije `districts` i `civilians` iz `TickTimer`-a su skupe
samo u prvim minutima (dizanje okruga), ne u stabilnom stanju.

**Fog of war (5,5 ms).** 2.825 aktera (svaki građanin, svako auto na putu i uz ivičnjak) sa
23.910 renderera; svakom skrivenom se `forceRenderingOff = true` upisivao SVAKI frejm, a
`CityBlocks.At` je za svakog prolazio kroz sva 193 bloka (4,5 ms samo to). Urađeno:
`CityBlocks.At` dobio mrežni indeks (48 m ćelije, isti redosled kandidata); akter se sudi
jedan u četiri frejma, a odmah čim se promeni skup otkrivenih blokova; skriveni renderer se
ponovo upisuje jednom u 64 frejma, ravnomerno.

**Sken neprijatelja (2,3 ms).** `EnemyWithin`/`EnemyComing` su za svaku ekipu bez mete
prolazile sve ljude svih drugih ekipa (~14 k parova) uz native pozive po paru
(`activeInHierarchy`, liste auta) i pogled na teritoriju (`MayEngage`) pre daljine. Urađeno:
snimak ulice jednom po frejmu (pozicija, prisutan, u autu) + krug oko ekipe koji preskače
parove ekipa predaleko da bi ijedan par ljudi bio u dometu; `MayEngage` tek posle daljine.
Isti odgovor kao ranije (prva ekipa po redu koja zadovolji sve uslove).

**Očekivano posle ovoga:** ~8 ms manje po frejmu → u editoru ~11–12 ms (85–90 fps), u
buildu ~8 ms (~120 fps). **120 fps u samom editoru nije dostižno** dok EditorLoop nosi 3,6 ms
i ne-skriptni PlayerLoop ~4,3 ms. Napomene za mašinu: Windows prijavljuje 6 logičkih jezgara
na 9900X3D (BIOS/SMT?), a monitor je 60 Hz uz `vSyncCount: 1` u PC kvalitetu — u buildu sa
vsync-om nikad preko 60.

### 5.1 Drugi prolaz istog dana: pokrenut grad, ne pauziran

Prvo merenje je bilo nad PAUZIRANIM gradom (korisnikov popup u 06:00 drži `timeScale 0`; skripte
i dalje rade, ali niko se ne kreće). Pokrenut grad (sat 7, 166 pešaka, 168 vozila) je skuplji;
mereno mojim Play-om, vsync isključen za merenje, isti 3D pogled sa ~58 M trouglova:

| šta | pre | posle |
|---|---:|---:|
| `RoadDemoBuilder.Update` | 9,7 ms | 4,2 ms |
| └ `districts` (aerodrom: 40 šetača × `WalkObstacles.Steer`) | 7,4 ms | 2,1 ms |
| └ `civilians` | 4,2 ms | 1,4 ms |
| `Crews.Walkers` (TickCrew ×120) | 3,1 ms | 0,6 ms |
| `DemoCrews.LateUpdate` (fog + nišan) | 6,4 ms | 0,8 ms |
| `Crews.Combat` | 2,3 ms | < 0,1 ms |
| `CityBlockRecycler.Update` (mirna kamera) | 1,4 ms + zastoji | 0,25 ms |
| **frejm, pokrenut grad, editor** | **33–36 ms** | **~24–26 ms** |
| frejm, pauziran grad, editor | ~20 ms | ~16 ms |

Tri uzroka iza prvog: (1) `WalkObstacles.OnGround` je za svaku probu pitao SVIH 216 planova
trotoara (5,5 od 6,8 µs po probi; `Run` 68 µs) — sada grubi indeks planova po 32 m ćeliji
(`WalkPlanIndex`), `Run` 7 µs; (2) brojač aktivnih pogleda u recycler-u je odlutao na 0 dok je
15 pogleda bilo aktivno, pa je svaki tek sagrađen prefetch blok odmah izbačen i ponovo građen
(194 sagrađena / 193 izbačena za tri minuta mirne kamere) — sada se broji iz samih pogleda; uz to
aktivan pogled pri izbacivanju prvo prolazi kroz Deactivate, pa `DemoStreetLamps` više ne dobija
uništeno svetlo; (3) fog i sken neprijatelja iz §5.

Šta ostaje u pokrenutom gradu (~22 ms pravog frejma u editoru): EditorLoop 4,4; render loop 2,9 +
čekanje render job-ova 2,3; animatori/Director 2,6 (čekanje job-ova na 6 jezgara); auti
`RoadCarSimulation` 2,3 (4–9 kad frejm pređe 33 ms i uđe drugi podkorak); okruzi 2,1; civili 1,4;
DemoCrews 1,0 + 0,8; `TurfMarks.Update` 0,5 (210 alokacija po frejmu, `GetComponent` na null);
stanice goriva/vatrogasci 0,7. Pod editorskog frejma bez ijedne skripte je ~12 ms.

### 5.2 Treći prolaz: šta je probano i šta nije pomoglo

- **`mergeVisibleBlocks: 1`** (spajanje mreža strimovanih blokova) — na istom pogledu render
  petlja 2,5 ms i čekanje render job-ova 2,0 ms, isto kao bez spajanja; a korak gradnje bloka
  skače do 196 ms. Vraćeno na 0. Nije vredno.
- **Aerodromski šetači** (40): proba prepreka jednom u dva frejma po šetaču, sa prošlom
  linijom i preostalim slobodnim metrima između — uštedа ~0,5 ms.
- **TurfMarks**: 1 ms po Sweep-u svakih 0,25 s (`DoorMenu.Read` za 119 radnji) — ostavljeno.
- **Pool prefab delova**: kapacitet 5.600 = tačno radni skup 18–20 aktivnih blokova na
  početnom kadru, pa je „spremno" 13 i svaki NOVI blok instancira (promašaji 4.311 : 246
  ponovnih upotreba, skoro svi pri dizanju grada jer prewarm ne postoji). Sa recycler
  popravkom iz §5.1 posle dizanja nema izbacivanja (2 sagrađena / 0 izbačena za 4 min mirne
  kamere). Ako smeta pri panovanju: `prewarmPartLimit` u CityViewConfig-u dići na ~9.000 —
  odluka o memoriji, nije doneta.
- Zašto 18–20 aktivnih blokova, a ne 5 kao u `core-streaming.md`: `Visible()` širi otisak
  kamere za `prefetch` 25 m plus „facade lead" do 60 m, pa pogled sa 165 m booma i 55° nagiba
  hvata ~20 recepata. Smanjenje tog vođenja je pitanje pop-in-a, ne koda.
- **Frejm sada** (pokrenut grad, editor, početni kadar): ~22–24 ms; od toga EditorLoop 4,4,
  render + job-ovi ~4,5, animatori 2,6, skripte ~9. Pauziran grad ~16 ms. Na mapi ~14 ms.

### 5.3 Četvrti prolaz (uveče 2026-09-06): render, spajanje i ritam

Metod: seed 1987 fiksiran (`newSeedEveryPlay` isključen samo za merenje), vsync isključen samo
u sesiji, isti početni kadar, dva pojasa sata: špic 7,6–8,7 i mirno 10,5–11,1.

- **Draw call-ovi**: 21.500 po frejmu, od toga 10.700 shadow caster-a — skoro sve iz strimovanih
  stambenih blokova (svaki ~1.000 renderera, 18–20 aktivnih). Spajanje (`mergeVisibleBlocks`)
  je bilo mrtvo slovo jer je `ResidentialConditionView` svaku fasadu obeležavao kao „dinamičnu"
  (menja joj materijal habanja), pa je `ScenePerf.Animated` sve preskakao. Sada: samo
  „dressing" koren je dinamičan; habanje ide kroz deljene materijale (`_NeglectAmount`), a
  promena praga habanja ili gustine dekoracija izbacuje spojeni pogled da se sagradi iznova.
  Grupe se seku na ≤ 60 k temena (korak spajanja 196 → ≤ 60 ms), skriveni (forceRenderingOff)
  se ne spajaju. Rezultat: **draw 21,5 k → 2,7–4,3 k, shadow 10,7 k → 1,5–2,5 k**, render petlja
  2,9 → 1,3 ms, čekanje render job-ova 2,3 → 0,7 ms.
- **Kamera**: `IslandLandform.Height` (~50 µs) zvan 15× po frejmu = 0,85 ms; visine temena
  mreže se keširaju po landform-u → ~0.
- **Građani u magli** tiču se svaki drugi frejm sa dugovanim vremenom (`CivilianCadence`);
  isto šetači pumpi. Aerodromski šetači probaju prepreke svaki drugi frejm.
- **Recycler**: prefetch blok se ne gradi kad je keš pun (klackanje 26 gradnji / 4 min).
- **Nije poluga**: animatori u magli (već culled — gašenje 512 animatora nije pomerilo
  Director vreme), GPU Resident Drawer uključen uživo (bez promene; traži novi pipeline, nije
  testiran iz asset fajla), spajanje pre popravke „dinamičnih" površina.
- **Rezultat** (editor, pokrenut grad, seed 1987, fiksna kamera): težak kadar sa 17 aktivnih
  blokova p50 ~19–20 ms; lakši kadar (5–8 blokova) p50 ~15–18; mapa ~13 ms. Igra sama vozi
  kameru na mapu oko 8:00 i vraća je oko 10:50 — ne mešati te prozore sa uličnim.
- **Šta je ostalo u frejmu** (~19 ms, teški kadar): EditorLoop 4,0; render 1,9 + čekanje render
  job-ova 1,3; čekanje job-ova (6 niti) 1,5; skripte ~7 (građani 0,5–0,8, patrole 0,35, DemoCrews
  0,9 + nišan 0,65, auti 1,0–2,7 po saobraćaju, TurfMarks 0,25, stanice 0,2); GC 0,7 (policija
  alocira ~15 KB/frejm u 4 alokacije — uzrok nije nađen); fizika 0,6 (0 rigidbody-ja, ali
  stakla/bombe/rasipanje prave Rigidbody kad zatreba, pa Script mod nije bezbedan);
  UpdateAllRenderers 0,5. Trzaji: gradnja + spajanje bloka pri pomeranju kamere, korak do
  ~80 ms (bilo 196).
- **Za 60 fps u editoru nedostaje ~3 ms** i to su: build (bez EditorLoop-a — odmah ispod
  16 ms), manji Game view (3007×1774), jedna kaskada senke umesto dve (vizuelno), ili
  Windows koji vidi svih 12 jezgara (job-ovi čekaju 1,5 ms po frejmu na 6 niti).

### 5.4 Noć 2026-09-07: dekor stoji u magli, pool i pojas strimovanja

Korisnikove odluke: `prefetchMetres` 25 → 45 i `cachedViews` 4 → 8 (blok se gradi pre nego što
uđe u kadar; povratak nazad ne gradi ponovo); `prewarmPartLimit` 5.600 → 12.000 (delovi se
zadržavaju u poolu umesto Instantiate na svaki nov blok); **dekor stoji dok ga niko ne vidi**
(`DecorFog`): luka (kranovi, brodovi, viljuškari, radnici) i aerodrom (letovi, rotori, apron,
ukrcavanje, ljudi) ne tiču dok je cela njihova zemlja u magli (9 tačaka, presuda na pola
sekunde); kamioni, kerb saobraćaj i parking kola ostaju na putevima; ekipe, policija, radnje i
događaji se ne pitaju. Harness (DriveTrace) i grad bez fog izvora vide sve da se kreće.
