# Plan: autoput v4 — „Inner Loop" (gradski autoput 1987, kako se zaista gradi)

> Predlog 2026-08-25, još **neodobren**. Aritmetika plana: `python Docs/innerloop.py` — **39 provera, 0 padova**.
> Prethodna tri pokušaja (šav-nadvožnjak + dijamant, belt sa slip-rampama, FreewayDemo
> sa naplatnom) ostaju u kodu isključeni dok ovaj ne stane na noge; odeljak 12 kaže šta
> se od njih zadržava.

## 0. Zašto su dosadašnje rampe bile glupe (dijagnoza, bez ulepšavanja)

Tri puta je autoput napravljen, tri puta je ispalo „rampe koje ne liče na rampe i put
koji se loše kači na grad". Uzrok nije bio u pločicama nego u tome što je **geometrija
puta izvedena iz ograničenja lane-grafa**, a ne obrnuto:

| šta je graf umeo | šta je zato autoput dobio |
|---|---|
| kolovoz je samo prava `A→B`, krivine ne postoje | belt sa četiri **ćoška pod 90°** (svako kolo koči na 6 m/s), rampe kao prave kose daske |
| spajanje dva kolovoza = **raskrsnica sa kutijom** (`RoadNode`), kroz `CanEnter` | „uključenje" na kome glavni tok autoputa **staje** da propusti rampu; gore = raskrsnica |
| nema promene trake | nema trake za ubrzanje/usporavanje, nema preticanja, kolo na unutrašnjoj traci nikad ne može da siđe |
| `Prepare` odbacuje manevar tek za `dot < −0.5` | slip rampa belta pod **23.9°** (da par izlaz/ulaz ne bude „polukružno okretanje") — ugao izabran zbog grafa, ne zbog puta |
| lutalica bira jedan „pravo" kandidat po blizini | izlazna rampa pod 6° se računa kao „pravo" i odbacuje: **lutalice nikad ne silaze** s decka |
| rutiranje po **metrima** | zaobilaznica autoputem je uvek duža → niko je ne bira |
| `Traffic.Cruise = 10 m/s` | deck od 22–25 m/s vozi se 36 km/h |
| koridor mora u šav grida | 140 m koridor sa servisnim ulicama koje seku SVAKU ulicu grida u nesignalisanim raskrsnicama; dijamant staje samo na jednu ulicu |
| rampa mora da sleti na postojeći čvor | 6 % nagib na 145 m, sužavanje 12.9 m, „foot" 30 m od ose — rampa koja stoji uz deck kao stepenice |

Uz to, dva čisto geometrijska promašaja: belt u **nivou** (autoput sa ukrštanjima u nivou
nije autoput) i spust šava od **8.8 %/12.5 %** (Synty `Ramp_01` od 20 m polagan kao da je
40 m; pravi komad od 40 m je `Ramp_02`).

Zaključak koji nosi ceo plan: **prvo se projektuje put po pravilima po kojima se pravi put
(AASHTO 1984), pa se lane-graf proširi onim što takav put traži** — krivine, trake sa
početkom i krajem, račvanje bez kutije, ulivanje sa procenom razmaka, promena trake,
rutiranje po vremenu.

## 1. Šta je gradski autoput 1987. (referenca)

Uzor: **Majami 1987** — I-95 i I-395 kao betonski vijadukti nad gradom, SR 836 Dolphin
Expressway sa naplatnim rampama, causeway-i preko zaliva, natrijumske lampe, zeleni
putokazi sa **sekvencijalnim brojevima izlaza** (Florida ih je imala do 2002). I **Honolulu
H-1**: interstate na ostrvu koji spaja aerodrom, centar i luku — tačno naš slučaj.

Mere (AASHTO Green Book 1984, zaokruženo; MUTCD 1978 sa dopunama do 1988):

| element | mera | napomena |
|---|---|---|
| traka | 3.6 m (12 ft) | |
| bankina spoljna / unutrašnja | 3.0 m / 1.2 m (10 / 4 ft) | 3.0 unutra tek za 3+ trake po smeru |
| razdelnik u gradu | betonska „Jersey" ograda, 0.6 m | travnati razdelnik je za van grada |
| projektna brzina / ograničenje | 55–70 mph / **55 mph** (24.6 m/s) | 55 je nacionalno ograničenje 1987; 65 samo van grada od aprila 1987 |
| min. poluprečnik pri e = 6 % | 60 mph ≈ 400 m; **50 mph ≈ 250 m**; 45 mph ≈ 200 m | ispod projektne brzine ide žuta tabla „advisory" |
| max. uzdužni nagib glavne trase | 3–4 % (grad) | |
| vertikalne krivine | K ≈ 45 m/% (ispupčenje), 30 m/% (udubljenje) pri 55 mph | dužina krivine = K × razlika nagiba |
| svetla visina ispod nadvožnjaka | 4.9–5.0 m (16–16.5 ft) | |
| rampa: širina kolovoza | 1 traka 3.6 + bankine 1.2 i 2.4 → **7.3 m** | |
| rampa: projektna brzina | 25–40 mph (11–18 m/s); petlja 25 mph, R ≈ 45–60 m | |
| rampa: nagib | 5 % poželjno, **6 %** dozvoljeno u gradu (do 7–8 % kratko) | |
| traka za usporavanje | ≈ 120–150 m + sužavanje | |
| traka za ubrzanje | ≈ 180–300 m + sužavanje 60–90 m | duže za kamione |
| razmak priključaka u gradu | 1 milja poželjno, **½ milje** minimum u gustom centru | nos ulaza → nos sledećeg izlaza ≥ 300–600 m (weaving) |
| terminali dijamanta na arteriji | 90–150 m između | signalisani u gradu |
| tipovi priključaka na arteriju | **tight diamond** (podrazumevano u gradu), parclo (bulevari), puna detelina (van grada, traži 300 m kvadrat) | SPUI je prvi put 1974, retkost; diverging diamond ne postoji |
| tipovi autoput–autoput | stack, turbina, truba (za kraj/naplatu) | |
| osvetljenje | lampe na razdelniku ili parapetu svakih 40–60 m, visoki jarboli 30 m na priključcima | natrijum, narandžasto |
| signalizacija | zeleni putokazi: 1 milja, ½ milje, na nosu izlaza; tabla brzine posle svakog ulaza; miljokazi; „DO NOT ENTER / WRONG WAY" na izlaznim terminalima; ramp-meter semafori na ulazima (LA od 1968, uobičajeno sredinom 80-ih) | |

Šta se **ne** radi na pravom autoputu: ukrštanje u nivou, rampa koja izlazi pod 24°,
rampa kraća od 150 m, spajanje u kome glavni tok staje, servisne ulice kroz svaku
raskrsnicu grida.

## 2. Trasa: prsten oko centra („Inner Loop")

### 2.1 Razmatrane trase

| trasa | za | protiv |
|---|---|---|
| **Kičma** kroz grid (vijadukt nad jednim šavom) | dramatično, majamijski | jede 140 m grada, već probano; blokovi jezgra su Syntyjevi i ne trpe koridor kroz sredinu |
| **Obalski** autoput (uz plažu) | lep pogled | 600–760 m od grida: ne služi nikome osim spoljnog prstena sela |
| **Kičma od aerodroma do luke** oko grida | jedan put, jasan | dva slobodna kraja (terminus na arteriji) i seed-zavisna forma (I, L ili U); kad su luka i aerodrom na istoj obali kičma je 800 m |
| **Prsten** oko grida u pojasu prvog prstena sela | deterministički, nema slepih krajeva, svaka strana grada služena, svi priključci istog tipa; američki „downtown loop" (I-277 Charlotte, I-490 Rochester, Kansas City) | ~5–6 km vijadukta; monotonija se razbija priključcima, mostovima i lukovima |

**Odluka: prsten.** Sve što je bilo dobro u ideji belta (ostaje se van centra, svaki
kvart ima put do luke i aerodroma bez prolaska kroz grid) sa svim onim što belt nije bio:
denivelisan, sa pravim rampama, sa krivinama.

### 2.2 Geometrija prstena

```
            ┌──────────────── obala ─────────────────┐
            │      ring-1 sela        (aerodrom)     │
            │  ╭────────────────────────────────╮    │   D  = 120 m od ivičnjaka grida (spoljna
            │  │ ring-0 sela  200..460 m        │    │        ivica trotoara ivične ulice) do ose
            │  │  ══════════ prsten (D=120) ═══ │    │   R  = 260 m u sva četiri ugla
            │  │ ║  ┌────────────────────────┐ ║│    │   H  = 7.0 m visina kolovoza (vijadukt)
            │  │ ║  │        GRID            │ ║│    │
            │  │ ║  │  15×9 linija, 1.4–2 km │ ║│    │   luk R=260 seče dijagonalu ugla na 62 m
            │  │ ║  └────────────────────────┘ ║│    │   od ugla grida (ne dira ivični trotoar)
            │  │  ═════════════════════════════ │    │
            │  ╰────────────────────────────────╯    │
            │            (luka u ćošku obale)         │
            └────────────────────────────────────────┘
```

- **Osa** na `D = 120 m` od spoljne ivice trotoara ivične ulice grida, paralelno sa sve
  četiri strane; četiri **luka `R = 260 m`** (50 mph, e = 6 %) spajaju strane. Luk počinje
  140 m (R − D) pre ugla grida na svakoj strani i prolazi 62 m od samog ugla.
- Zašto 120: unutrašnji terminal dijamanta je na `D − 58 = 62 m` od ivičnjaka, tj. **76 m
  osa–osa** od ivične ulice grida — jedan kratak gradski blok, koliko je i u Majamiju
  između rampe i prve avenije; manje od toga bi dve raskrsnice sedele jedna drugoj u
  krilu (isti prigovor kao kod servisnih ulica).
- **Dužina**: za grid 1700 × 850 m prsten ima ~5.6 km (`innerloop.py` računa po gridu).
- **Visina**: ceo prsten je **vijadukt na 7.0 m** (kolovoz), kao I-95 nad Overtownom.
  Ne zato što je lepše nego zato što je nužno: pristupne ulice sela (jedini putevi van
  grida) duge su 110 m — ne mogu da se popnu preko autoputa, pa autoput mora preko njih;
  a rov (Čikago) na ostrvu sa dnom mora na −5 m bio bi pod vodom. Spust na nasip između
  dva ukrštanja traži ~800 m bez ijedne ulice ispod — na ovom gridu se to ne dešava (§3.3
  ostavlja pravilo za slučaj da se desi).
- **Nadvišenje** u lukovima 6 % (ceo presek se naginje kao jedna ravan oko ose; spoljni
  parapet +0.73 m, unutrašnji −0.73 m).
- **Reke**: prsten seče svaki izlaz reke iz grida (2–3 puta) — deck ide dalje na istoj
  visini, samo se stubovi menjaju za mostovske (§3.4). Rezervacija tla oko korita ostaje
  kakva je (`ClearOfRivers`, `RiverClear 26`).
- **Brda**: pojas brda ostrva (danas 55–210 m od ivice grada, do 26 m) prelazi na
  `D + 45 .. D + 250`; reljef u koridoru `D ± 60` se gasi (`HighwayFade`, već postoji za
  osne pravce — treba mu polilinija, v. §11).

### 2.3 Šta prsten radi rasporedu kvartova (`CityLayout`)

Prsten se **kotrlja pre sela**, kao aerodrom i luka: on je rezervacija sa kojom se
sve ostalo sudara (`Clashes`), a ne nešto što se ugura posle.

| pravilo | vrednost | zašto |
|---|---|---|
| koridor prstena (polilinija + polu-širina) | osa ± 20 m (ROW ograda) | pod deckom ništa ne stoji osim stubova |
| kvadrat priključka | 840 m duž ose × 140 m preko (osa ± 70) | rampe i terminali (§4) |
| `NearStrip` prvog prstena sela | 110 → **200 m** (na liniji priključka **280 m**) | telo sela van koridora; kapija ≥ 90 m od spoljnog terminala |
| `RingStep` | ostaje 330 (prstenovi 200 / 530 / 860) | |
| obala | `islandWest/East/North/South` **+ 90 m** | inače drugi prsten sela ne staje i pada ispod `MinSuburbs 6` |
| pristupna ulica svakog sela | seče prsten **denivelisano** (prolazi ispod, svetla visina 5.45 m) | nema ukrštanja u nivou, nikad |
| linija priključka | pristupna ulica dobija **profil bulevara** (35 m, 2+2, medijan) od ivične raskrsnice grida do kapije kvarta; linija grida se promoviše u bulevar | terminali dijamanta traže trake za levo; arterija sa rampama nije ulica od 15 m |
| benzinska (`Wayside`) | nikad u koridoru; na liniji priključka ide **iza spoljnog terminala** (178–280 m od ivičnjaka) | „pumpa na izlazu sa autoputa" |
| luka / aerodrom | ostaju gde su (strip 415+/460 m) — prsten ih ne dira | |

### 2.4 Izbor priključaka

Kandidati su **samo linije koje nose pristupnu ulicu** (arterija koja se završava 50 m
posle terminala je besmislica). Redosled: (1) prilaz aerodromu, (2) jedna od dve kapije
luke (ona dalja od ćoška), (3) pristupne ulice na bulevarskim linijama grida, (4) ostale
pristupne ulice. Uslovi:

- razmak osa dva priključka **≥ 850 m** (nos ulaza → nos sledećeg izlaza ≥ 400 m);
- stanica arterije ≥ 100 m od tangente luka (terminal traži prav deck oko sebe; same
  rampe smeju da prate luk);
- očekivano **4–6 priključaka** po seed-u: 2 na dugim stranama, 0–1 na kratkim.

Selo čija linija nije dobila priključak stiže na prsten kroz grid do susedne arterije —
i to je normalno: u pravom gradu isto tako.

### 2.5 Kopno (opciono, faza 7)

Ostrvo je u 50 % seed-ova poluostrvo (`mainlandEdge`, `MainlandReach 700`). Tada prsten
dobija **trubu** na toj strani i **causeway** do ivice mape sa **naplatnom rampom** na
sredini (Rickenbacker/Venetian causeway 1987 su naplaćivali) — kola se pojavljuju i
nestaju na ivici mape kao brodovi. `TollPlaza` i `RoadNode.Toll` već postoje i rade.
Bez kopna prsten je zatvoren i naplate nema (na downtown loop-u je ne bi ni bilo).

## 3. Poprečni presek i profil

### 3.1 Glavna trasa

```
   spolja                                                                     ka gridu
   ─┬──────────┬──┬──────────┬─
    │  deck A  │▌ │  deck B  │      deck: SM_Env_Road_Highway_01, 11.4 m, parapet spolja
    │ 11.4 m   │  │  11.4 m  │      razmak 1.6 m: Jersey ograda (SM_Prop_Barrier_03) + lampe
    ═╧══════════╧══╧══════════╧═     ukupno 24.4 m; ose deckova na ±6.5 m od ose prstena
    ↑ 7.0 m    ▐▌ stub / deck / 20 m (SM_Env_Road_Highway_Pillar_01, T-glava 8.7 m)
   ─┴──────────────────────────┴─   tlo: ogoljen asfalt + ograda ROW na ±20 m
```

- Kolovoz na **7.0 m**; ploča Synty decka je 1.55 m ispod površine → svetla visina
  **5.45 m** nad ulicom na 0.12 (standard 4.9–5.0).
- Trake u grafu: **2 po decku na ±2.85** (kao do sada) — **osim ako** `Highway_01` u meshu
  ima 3 trake (inventar procenjuje 3 × 3.8 bez bankine, kakav je bio Embarcadero); to
  se sredi u fazi 0 gledanjem mesha, i tada su trake na −3.8/0/+3.8. Odluka ne menja
  ništa drugo u planu.
- Deck je i dalje **dva jednosmerna kolovoza** (`AddOneWay`), samo što od sada kolovoz
  ume da bude polilinija (§5.1).
- Stubovi: po jedan pod svakim deckom svakih 20 m; nad ulicom stubovi na ±16 m od ose
  ulice (raspon 32 m), nikad u kolovozu (`PierFree` postoji).

### 3.2 Rampa

Jedna traka, kolovoz **7.3 m**, parapet spolja; osa rampe na **±18 m** od ose prstena
(2 m vazduha do parapeta decka). Pomoćna traka (usporavanje/ubrzanje) je **proširenje
decka** na 15.2 m — deck se za dužinu pomoćne trake širi (procedura, §6), parapet ide sa
njim.

### 3.3 Profil rampe (izlaz; ulaz je ogledalo)

```
   7.0 ────────────────╮ K=5, A=6 → 30 m
                        ╲  6 %  ·  85 m
                         ╲
                          ╰──────── K=5, A=6 → 30 m ─── 0.12
      gore(nos)   |<——————— 145 m ———————>|  luk R=40   terminal
```

| deonica | dužina | napomena |
|---|---|---|
| traka za usporavanje (paralelna, na decku) | 120 m (sužavanje 50 + paralelno 70) | 24.6 → 13 m/s, a = 1.8 m/s² |
| gore: nos + razmicanje osa 14.1 → 18 m | 35 m | ovde rampa dobija svoj parapet |
| ispupčena vertikalna krivina | 30 m (pad 0.9 m) | K = 5 m/% (rampa 25 mph) |
| ravan pad 6 % | 85 m (pad 5.08 m) | |
| udubljena vertikalna krivina | 30 m (pad 0.9 m) | ukupno 6.88 m = 7.0 − 0.12 ✓ |
| horizontalni luk **R = 40 m**, 90° | 63 m po luku, 40 m po osi prstena | 20 mph; iz pravca ose u pravac arterije |
| terminal | T na arteriji, na **±58 m** od ose prstena | signalisan, trake za levo |

Ulaz: terminal → luk R=40 → uspon (30 + 85 + 30) → nos (35) → **traka za ubrzanje 180 m**
(paralelno 120 + sužavanje 60). Nagib 6 % je gornja gradska granica AASHTO-a; sa 5 % rampa
raste za 20 m i priključak za 40 m — ostavljen kao konstanta.

### 3.4 Mostovi i prelazi

- **Ulica ispod** (svaka pristupna ulica, sve arterije): deck ide dalje, stubovi na ±16 m
  od ose ulice, ispod = ulica sa trotoarom kakva jeste.
- **Reka**: `SM_Env_Bridge_Support_01` (23.4 m — skoro tačno dvostruki deck 22.8 + 1.6)
  kao mostovski jaram u vodi svakih 15 m, obalni `Bridge_Wall_01` ×4 kao upornjak, ono što
  već radi `BeltRiverBridge`.
- **Luk R=260**: tetive od 20 m daju prelom 4.4° po spoju — vidljivo na parapetu; zato su
  lukovi (i sve rampe) **ekstrudirani** (§6), a ne slagani.
- **Spust na nasip** (pravilo za slučaj da negde ima ≥ 800 m bez ulice ispod): 3 %, krivine
  K 45/30, na tlu `RoadBed` sa `HighwayFade`; na ovom gridu se ne očekuje.

## 4. Priključci

### 4.1 Tight diamond (podrazumevani)

```
                 spoljni terminal (T, semafor)  ← arterija ide dalje ka kapiji kvarta
   ────────────────────┬──────────────────────────
     deck B (−u) ═══╗  │  ╔═══ ulaz B  ← nos B  → ...traka za ubrzanje... (−u)
   ...izlaz B ──────╝  │  ╚═══════════════════════════════════════════════════
     ═══════════════════╪═════════════════ prsten, ose deckova ±6.5 ══════════
     deck A (+u) ═══════╪═════════════════════════════════════════════════════
   ...izlaz A ─────╗   │   ╔═══ ulaz A → nos A → ...traka za ubrzanje (+u)
                   ╚───┼───╝
                 unutrašnji terminal (T, semafor)  ← arterija ide u grid (76 m do ivične raskrsnice)
                       │
   stanice duž ose:  −340 … −220 (nos izlaza) … 0 (arterija) … +220 (nos ulaza) … +400
```

| stanica (m od ose arterije) | šta |
|---|---|
| −340 | početak trake za usporavanje (deck se širi na 15.2) |
| −220 … −185 | nos izlaza; rampa dobija parapet, osa odlazi sa 14.1 na ±18 |
| −185 … −40 | pad 6 % sa krivinama (145 m) |
| −40 … 0 | luk R=40 do terminala na v = ±58 |
| 0 | arterija (bulevar 35 m) prolazi ispod decka; terminali na v = −58 (unutra) i +58 (spolja), 116 m između |
| 0 … +40 | luk R=40 sa terminala |
| +40 … +185 | uspon 6 % |
| +185 … +220 | nos ulaza |
| +220 … +400 | traka za ubrzanje 180 m (deck širi) |

Svaki deck ima izlaz **pre** arterije i ulaz **posle** nje, oba na svojoj desnoj strani;
deck A sleće na unutrašnji terminal, deck B na spoljni. Kvadrat priključka: **840 × 140 m**.

Terminal je pravi `RoadNode` sa **semaforom** (danas ih dobijaju samo čvorovi grida — ručni
čvor mora da ume da bude signalisan), sa trakom za levo na arteriji (medijan bulevara se
tu prekida), desno sa rampe slobodno uz ustupanje, levo na semaforu. Pešački prelaz
preko rampe na terminalu: zebra samo na signalisanoj strani; niko ne hoda uz rampu ni po
decku (nema `PedLink`-ova; stubovi su `WalkObstacles.Blocked`).

### 4.2 Parclo (bulevari ka centru, faza 7)

Gde arterija nosi pravi bulevar grida i ima mesta u dva kvadranta: petlje R = 50 m
(25 mph) umesto dva leva skretanja na terminalu. Kvadrat 840 × 240 m. Ne ulazi u prvu
verziju — dijamant radi sve što treba, parclo je karakter.

### 4.3 Truba (kopno) i terminus

Truba na causeway-u (§2.5): petlja R = 50 i direktna rampa R = 120. **Terminusa nema**:
prsten je zatvoren, causeway se završava na ivici mape (portal za spawn/despawn). Ako
se ikad poželi kičma umesto prstena, terminus je „autoput se spušta na 3 % i postaje
bulevar na signalisanoj raskrsnici" — jedini realan način da se autoput završi u gradu.

### 4.4 Šta ovo popravlja od prijavljenog

| prigovor | ovde |
|---|---|
| „glupe uključne/isključne rampe" | rampa je 340 m od trake za usporavanje do terminala, nagib 6 %, luk R = 40 na dnu, pomoćne trake na decku, nos sa parapetom, terminal sa semaforom — mere iz priručnika, ne iz grafa |
| „loše se povezivao na grad" | priključak samo na arteriju koja vodi negde (kvart ↔ grid), arterija je bulevar, terminal 76 m od ivične raskrsnice, nema servisnih ulica ni praznih kvadrata; sve ostale ulice prolaze ispod bez ikakvog čvora |
| ukrštanja u nivou | nijedno |
| glavni tok staje na spajanju | nikad (§5.3 — invarijanta harnessa) |

## 5. Lane-graf i vozač: šta mora da se doda

Ovo je jezgro plana. Bez ovih šest stvari svaki novi autoput završi kao prethodna tri.

### 5.1 Kolovoz kao polilinija (`Carriageway.Path`)

`Carriageway` dobija niz stanica `(tačka, smer)` sa kumulativnim `s`; `Right(s)`,
`PointAt(s,d)`, `Project(p)` rade po segmentu; `RoadCar` već vozi u `(s,d)` — menja se
samo izvor okvira. Tetive ≤ 10 m za R < 100, ≤ 20 m inače; smer je kontinualan pa kola
ne cimaju. Isti `Path` koristi i polaganje (tetive za pločice, ekstruzija za krivine).
Kolovoz više **ne mora da se lomi u čvor na svakom prelomu** — to je ono što je belt
pretvorilo u četiri raskrsnice.

### 5.2 Trake sa početkom i krajem (`Lane.s0, s1`)

Pomoćna traka za usporavanje postoji od −340 do −220, za ubrzanje od +220 do +400;
glavne trake celom dužinom. `Ahead/Behind` upiti po traci gledaju samo kola čiji `s` je u
opsegu trake.

### 5.3 Račvanje i ulivanje bez kutije (`Fork`, `Merge`)

- **Fork** (nos izlaza): veza traka→traka (spoljna traka → rampa); glavne trake ne znaju
  da čvor postoji — nema `CanEnter`, nema konflikta, nema `StopSetback`.
- **Merge** (kraj trake za ubrzanje): rampa se uliva u pomoćnu traku bez ikakvog
  ustupanja (pomoćna traka je njena); pomoćna traka se **završava** na +400 i kolo mora
  da pređe u glavnu traku (§5.4). **Glavna traka nikad ne staje zbog ulivanja** — to je
  invarijanta koju harness proverava (`mainlinestop = 0`).
- Terminali na arteriji ostaju obične `RoadNode` kutije sa semaforom.

### 5.4 Promena trake (`Manoeuvre.LaneChange`)

Bočni prelaz na susednu traku preko `L = max(40, 2.5·v)` m; za vreme prelaza kolo se
računa u obe trake. Dve vrste:

- **obavezna**: pomoćna traka se završava (od `s1 − 200`), ili je izlaz koji kolo hoće
  ≤ 400 m ispred a kolo nije u spoljnoj traci. Prihvatanje razmaka: iza ≥ `v_iza · 1.2 s + 5 m`,
  ispred ≥ `v · 0.9 s + 5 m`. Ako razmaka nema do `s1 − 30`, kolo **usporava na pomoćnoj
  traci** i čeka (pravo ulivanje); tek na samom kraju staje.
- **diskreciona** (v2 iste faze): vozilo ispred sporije ≥ 3 m/s i levo slobodno → pretekni;
  posle vrati desno („keep right"). Bez ovoga kamioni (20 m/s) prave kolonu iza sebe.

### 5.5 Odluke lutalica i rutiranje

- Na `Fork`-u lutalica u spoljnoj traci uzima izlaz sa verovatnoćom **0.35**; na
  terminalu lutalica sa arterije uzima ulaznu rampu sa **0.5** — tako se prsten puni bez
  da se ijedno kolo rutira.
- `RouteToward` postaje Dijkstra po **vremenu** (`dužina / min(limit, cruise profila)`):
  patrola, gangster i kamion biraju prsten kad je brži, ne kad je kraći.
- Rutirana kola na `Fork`-u prate rutu; ako nisu u spoljnoj traci, obavezna promena od
  400 m ranije (§5.4).

### 5.6 Brzine po klasi puta

`Carriageway.Class ∈ {Street, Boulevard, Ramp, Freeway}`; `DriverProfile.CruiseFor(class)`:

| profil | ulica (danas) | rampa | autoput |
|---|---|---|---|
| Traffic | 10 | 12 | **23** |
| Lorry | 8 | 10 | 20 |
| Gangster | 14 | 15 | 27 |
| Police | 14 | 15 | 29 |
| Hot / Getaway | 18 / 16 | 18 | 31 / 30 |

Brzina u krivini: `v ≤ sqrt(a_lat · R)` sa `a_lat = 2.0 m/s²` za Traffic (R 260 → 22.8,
R 40 → 8.9, petlja R 50 → 10); profili sa većim `LateralG` idu brže. `TimeGap` na autoputu
1.2 s (danas 0.9 na ulici). Ograničenje decka 24.6 m/s (55 mph), luk 22.4 (50 mph, tabla).

### 5.7 Spawn i tok

Kola na deck trakama svakih 40 m po traci (gradski autoput u podne), nikad se ne uklanjaju
(prsten je zatvoren). Sa causeway-em (§2.5) portal na ivici mape spawnuje/uklanja
`through` saobraćaj. Kamioni: luka ↔ industrijski blokovi jezgra (`ind-*`) preko prstena
— rutirani po vremenu, dakle prstenom.

## 6. Geometrija i delovi (šta od Synty-ja, šta se ekstrudira)

Inventar (`gangsters_measure`, 380 prefaba, 2026-08-25) kaže: Synty ima **prav deck od
11.4 × 20 m, luk 90° R≈12.5, luk 45° R≈25, rampu od 5 m visine na 25 %/12.5 %, T-stub** — i
ništa drugo za autoput. Nema krivine R 40–260, nema kolovoza od 7.3 m, nema proširenja,
nema nosa. Zato:

| element | izvedba |
|---|---|
| prava deonica decka | `SM_Env_Road_Highway_01`, tetive od 20 m, `FreewayKit.LayDeck` (već ume razvlačenje i nagib) |
| luk R=260, proširenje 11.4 → 15.2, nos, rampa 7.3 m sa parapetom, luk R=40 | **procedurna ekstruzija** profila decka (ploča 1.55, parapet 1.2, ista `PolygonPalmCity` atlas mapa sa UV-ima očitanim iz `Highway_01`) duž `Path`-a; spoj sa pločicom nevidljiv jer je materijal i UV traka ista |
| oznake na decku | zapečene u `Highway_01`; na ekstruziji iste UV trake; nos = `SM_Env_Road_Median_01` (šrafura) na +0.02 |
| stub | `Highway_Pillar_01`, `StandPillar` ga razvlači za 7 m deck (napravljen za 5) |
| razdelnik | `SM_Prop_Barrier_03` (2.5 m, pivot na kraju, lančanje) u razmaku 1.6 m |
| most preko reke | `SM_Env_Bridge_Support_01` (23.4 m) svakih 15 m, `Bridge_Wall_01` ×4 na obalama |
| ograda ROW | `SM_Env_Fence_01` (5 m žica) na ±20 m; ispod decka ogoljen asfalt (`_bare`) — **ništa drugo** dok se ne odluči (§13) |
| zid protiv buke | `GW SM_Bld_Fence_Brick_01` (3 × 3 m) na parapetu, 1 red, gde je telo sela ≤ 60 m od ose |
| lampe | `SM_Prop_LightPole_Base_01` (kobra, 6.5 m) skaliran ×1.6 na parapetima, naizmenično, svakih 50 m; na priključcima `airport-apron-mast` (15 m) ×2 = jarbol 30 m |
| putokazi | `SM_Prop_Sign_Pole_04` (konzola 6.5 m) + `Billboard_Sign` ploča sa novim zelenim materijalom + TMP tekst; bočni: `SM_Sign_Blank_01/02` zeleni; `SM_Sign_Speed_55` postoji; `No_Entry`, `One_Way`, `Give_Way` postoje |
| ramp-meter | `SM_Prop_Traffic_Light_02` na `Sign_Pole_02`, samo dekor u v1 |
| miljokaz | `SM_Sign_Blank_04` na `Sign_Pole_01` |
| naplatna (samo causeway) | `FreewayKit` booth + `Barrier_Gate_01`, već radi |

Napomena o memoriji „u blokove samo katalog": ovo nisu ukrasi na placu nego **kolovoz**
koji katalog fizički nema u traženim poluprečnicima; ekstruzija nosi Syntyjev materijal i
UV-e pa se u sceni ne razlikuje od pločice. Traži potvrdu (§13).

## 7. Opremanje i signalizacija (period 1987)

- **Zeleni putokazi** (belo na zelenom, MUTCD): na 800 m i 400 m pre izlaza („EXIT 3 —
  Harbor Blvd — ½ MILE"), na nosu („EXIT 3 ↗"), posle svakog ulaza „SPEED LIMIT 55" +
  potvrdna tabla rute. **Sekvencijalni brojevi izlaza** u smeru kazaljke od prilaza
  aerodromu (Florida 1987). Na izlaznim terminalima „DO NOT ENTER" / „WRONG WAY", na
  ulaznim „ONE WAY" i zeleni trailblazer „SR 9 EAST →".
- **Ime**: pul u `StreetNames` — „<Grad> Expressway", „Bayshore Expressway", „Inner
  Loop", broj rute „STATE ROAD 9xx"; izlazi nose ime arterije. Mapa (`DemoMap`) dobija
  sloj: debela crveno-plava linija sa štitom, tanke rampe, broj izlaza uz svaki priključak.
- **Bilbordi** ka autoputu (`SM_Prop_Billboard_01`, ~300 m razmak, van ROW-a) sa period
  reklamama — opciono, jer je sadržaj tla van grida (§13).
- Detalji koje ne radimo u v1: grafiti na upornjacima, telefoni za pomoć, drenaža.

## 8. Saobraćaj i igra

- Gustina: 2 trake × 2 decka × 5.6 km / 40 m ≈ **560 kola** na prstenu; `ScaleLifeToCity`
  dobija stavku za autoput (danas ~280 u gradu). Perf: deck, stubovi i ograda u
  `MergedChunk`-ovima po 100 m; kola na decku su obična `RoadCar`.
- Policija patrolira prstenom (BFS po vremenu); potera `Hot` profilom po decku; spikes
  (`SM_Prop_Road_Spikes`) na rampi kao blokada — faza 7.
- Drive-by na decku radi kao na ulici (isti `CrewCar`), gangster ide 27 m/s.
- Kamioni luka ↔ industrija, krađa kamiona na rampi — kuke za kasnije.

## 9. Provera

**Bez Unity-ja — `python Docs/innerloop.py`** (mora da prođe pre prve linije C#):
koridor i kvadrat priključka u pojasu sela; luk R=260 ne dira ivični trotoar; rampa:
zbir padova = 7.0 − 0.12, nagib ≤ 6 %, K ≥ 5, dužina ≥ 150; terminali na ±58, ≥ 70 m od
ivične raskrsnice grida (76) i ≥ 90 m od kapije sela (102); brzina po krivini; razmak priključaka ≥ 850 sa
nosevima ≥ 400; broj priključaka po tipičnom gridu ≥ 3; svetla visina ≥ 4.9; obala +90
drži tri prstena; dužina prstena.

**Harness** (`gangsters_play` u lab sceni, pa `analyze.py --loop`, presuda po tally-ju od
30): novi redovi `merge` (id, razmak, čekanje), `lanechange` (id, obavezna/diskreciona),
postojeći `deck`. Kvarovi: `mainlinestop` (kolo na glavnoj traci < 3 m/s duže od 4 s bez
zastoja ispred), `mergestuck` (> 20 s na kraju pomoćne trake), `wrongway`, `carstuck`,
izuzeci — **svi 0**. Mere: medijana brzine na decku **≥ 20 m/s**; svaka rampa upotrebljena
bar jednom u 5 min u oba smera; bar jedno putovanje ≥ četvrtine prstena; lutalice čine ≥
30 % silazaka.

## 10. Faze (svaka ostavlja grad u ispravnom stanju)

| faza | šta | fajlovi | gotovo kad |
|---|---|---|---|
| **0** | plan + `innerloop.py`; probe mesha `Highway_01` (trake, UV trake) — mali editor command | `Docs/`, `PipelineCommands.cs` | 0 padova; broj traka odlučen |
| **1** | `FreewayPlan` u `CityLayout.Roll` (prsten kao polilinijska rezervacija, izbor priključaka, kvadrati, NearStrip 200/280, obala +90, promocija arterija); `gangsters_layout` ispisuje prsten i izlaze; `DemoMap` crta sloj | `CityLayout.cs`, `Districts.cs`, `Island.cs`, `DemoMap.cs`, `StreetNames.cs`, `PipelineCommands.cs` | 30 seed-ova: ≥ 6 sela, 4–6 priključaka, 0 sudara |
| **2** | `LaneNet`: `Path` kolovozi, `Lane.s0/s1`, `Class` + `CruiseFor`, brzina u krivini, rutiranje po vremenu — **bez ijednog novog puta** | `LaneNet.cs`, `RoadCar.cs`, `DriverProfile.cs` | regresija: CrewDemo/CoreDemo/Game harness kao pre (tally 30) |
| **3** | `Fork`/`Merge`, `LaneChange`, izlazi lutalica; **lab scena** `LoopLab.unity` (grid 4×3 + prsten + 2 dijamanta, tanki host kao `BlockLabBuilder`); `analyze.py --loop` | `LaneNet.cs`, `RoadCar.cs`, `Assets/LoopLab/`, `Tools/play/analyze.py`, `DriveTrace` | invarijante iz §9 u labu, 30/30 |
| **4** | geometrija u gradu: `RoadDemoBuilder.Loop.cs` (profil, ekstruder `DeckMesh`, pločice na pravim, stubovi, mostovi, rampe, terminali sa semaforom, bulevar-arterija, tlo, polilinijska rezervacija + `HighwayFade`, pojas brda) | `RoadDemoBuilder.Loop.cs`, `FreewayKit.cs`, `District.cs`, `Island.cs`, `Seams.cs`, `Wayside.cs` | Game.unity harness 30/30; Play pregled |
| **5** | opremanje: ograde, razdelnik, lampe, putokazi, brojevi izlaza, zid protiv buke, mapa, imena | `RoadDemoBuilder.LoopDressing.cs`, `DemoMap.cs` | Play pregled sa §7 kao kontrolnom listom |
| **6** | čišćenje: belt, šav `Highway`, `FreewayRoute`, `HighwayTraffic`, `freeway.py`/`motorway.py` napolje; `freeways` prekidač → `innerLoop`; docs | v. §12 | kompajl čist, tally 30 |
| **7** | opciono: causeway + naplata, parclo, kamioni, potera, bilbordi | | |

Redosled 2 → 3 → 4 je namerno: graf pre geometrije, jer je graf ono što je tri puta
zakazalo, a lab scena proverava graf pre nego što se dotakne grad.

## 11. Šta u postojećim sistemima mora da se promeni (van autoputa)

- `DistrictReservations`: nova vrsta rezervacije **polilinija + polu-širina** (danas samo
  osni pravougaonici) — `FlatAt`, `InPaved`, `HighwayFade`, `UnderHighwayOrRiver`,
  `DressWilderness` (5 mesta).
- `BuildSignals`: semafor i na ručnim čvorovima (`RoadNode.Signalled`).
- `PlanNext`: kandidat „izlaz" odvojen od „pravo".
- `RouteToward`: težina po vremenu.
- `ScaleLifeToCity`: budžet kola za prsten.
- `Wayside`: koridor je zabranjen; pumpa iza spoljnog terminala.
- `Island`: pojas brda pomeren, obala +90.

## 12. Šta se zadržava od prethodnih pokušaja, šta ide napolje

**Ostaje** (dobro i već izmereno): `FreewayKit` (merenje, `LayDeck`, `StandPillar`, `Sit`),
`Carriageway.SurfaceY/SurfaceAt/Elevated` + `RoadCar.SurfaceLift`, `AddOneWay`,
`_midSegment/LaneSegment`, `RoadNode.Toll` + `TollPlaza`, `PaveFreewayJunction`,
`PierFree/InAnyRoad`, `ClearOfRivers/RiverCrossings/BeltRiverBridge`, `HighwayFade`,
DriveTrace `deck/toll` redovi i `analyze.py --freeway` kao osnova za `--loop`.

**Napolje posle faze 4**: `RoadDemoBuilder.Belt.cs` (prsten u nivou), šav `SeamKind.Highway`
+ `Interchange.cs` (servisne, dijamant 6 %), `FreewayRoute` u `Freeway.cs` (osim toll
delova), `HighwayTraffic`, `Docs/freeway.py` i `motorway.py` (već ne odgovaraju kodu),
`Docs/freeway-interchange.md` → jedan pasus istorije u ovom dokumentu.

## 13. Odluke koje čekaju korisnika

1. **Prsten** umesto kičme aerodrom–luka (§2.1) — predlog: prsten.
2. **Procedurna ekstruzija** decka za lukove/rampe/proširenja sa Synty materijalom (§6) —
   bez toga nema krivine R 40–260 i nema rampe od 7.3 m; predlog: da.
3. **Tlo ispod decka**: ogoljen asfalt + ograda (predlog, po pravilu „ako ne znaš — ostavi
   prazno"), ili parking pod deckom na priključcima (majamijski, ali je to pogađanje sadržaja).
4. **Obala +90 m** da tri prstena sela stanu (§2.3) — alternativa je manje sela.
5. **Causeway sa naplatom** na poluostrvu (§2.5) — predlog: faza 7, ne sad.
6. **Zid protiv buke** na parapetu uz sela — predlog: da, u fazi 5.
7. **Bilbordi** van ROW-a — sadržaj tla van grida; predlog: ne bez izričite želje.
