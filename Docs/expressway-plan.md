# Plan: autoput v4 — razgranata mreža (gradski autoput 1987, kako se zaista gradi)

> Predlog v2, 2026-08-25. Prva verzija je bila zatvoren prsten; korisnik: *„ne bih baš prsten,
> mislim da je bolje ako je razgranato"* — trasa je sada **stablo**: deblo i grane do mesta
> gde autoput ima razlog da se završi. Odlučeno u istom krugu: **bilbordi da, zid protiv
> buke da, ispod decka propalice i props.** Aritmetika: `python Docs/expressway.py` — **65 provera,
> 0 padova**.
> Prethodna tri pokušaja (šav-nadvožnjak + dijamant, belt sa slip-rampama, FreewayDemo sa
> naplatnom) ostaju u kodu isključeni; odeljak 12 kaže šta se od njih zadržava.

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
Expressway sa naplatnim rampama, SR 112 Airport Expressway koji se **završava na
aerodromu**, causeway-i preko zaliva, natrijumske lampe, zeleni putokazi sa
**sekvencijalnim brojevima izlaza** (Florida ih je imala do 2002). I **Honolulu H-1**:
interstate na ostrvu koji spaja aerodrom, centar i luku — tačno naš slučaj.

Mere (AASHTO Green Book 1984, zaokruženo; MUTCD 1978 sa dopunama do 1988):

| element | mera | napomena |
|---|---|---|
| traka | 3.6 m (12 ft) | |
| bankina spoljna / unutrašnja | 3.0 m / 1.2 m (10 / 4 ft) | 3.0 unutra tek za 3+ trake po smeru |
| razdelnik u gradu | betonska „Jersey" ograda, 0.6 m | travnati razdelnik je za van grada |
| projektna brzina / ograničenje | 55–70 mph / **55 mph** (24.6 m/s) | 55 je nacionalno ograničenje 1987; 65 samo van grada od aprila 1987 |
| min. poluprečnik pri e = 6 % | 60 mph ≈ 400 m; **50 mph ≈ 250 m**; 45 mph ≈ 200 m; 35 mph ≈ 120 m; 30 mph ≈ 90 m | ispod projektne brzine ide žuta tabla „advisory" |
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
| tipovi autoput–autoput | **truba** (grana/krak, naplata), direkcioni Y, stack | truba je standard za krak koji se negde završava |
| kraj autoputa | spust na nivo, signalisana raskrsnica sa arterijom, „END FREEWAY", 55 → 35 mph | nikad slepi kraj |
| osvetljenje | lampe na razdelniku ili parapetu svakih 40–60 m, visoki jarboli 30 m na priključcima | natrijum, narandžasto |
| signalizacija | zeleni putokazi: 1 milja, ½ milje, na nosu izlaza; tabla brzine posle svakog ulaza; miljokazi; „DO NOT ENTER / WRONG WAY" na izlaznim terminalima; ramp-meter semafori na ulazima (LA od 1968, uobičajeno sredinom 80-ih) | |

Šta se **ne** radi na pravom autoputu: ukrštanje u nivou, rampa koja izlazi pod 24°,
rampa kraća od 150 m, spajanje u kome glavni tok staje, servisne ulice kroz svaku
raskrsnicu grida, slepi kraj.

## 2. Trasa: stablo — deblo i grane

### 2.1 Šta je razmatrano

| trasa | za | protiv |
|---|---|---|
| kičma kroz grid (vijadukt nad jednim šavom) | dramatično | jede 140 m grada, već probano; blokovi jezgra su Syntyjevi |
| obalski put | pogled | 600–760 m od grida: ne služi nikome |
| zatvoren prsten (v1 ovog plana) | deterministički, bez krajeva | **korisnik: ne prsten, razgranato** |
| **stablo**: deblo uz grid + grane do listova | svaki krak vodi negde i tu se **završava u nešto** (aerodrom, luka, kopno, centar); forma se menja sa seedom; truba i terminus su najprepoznatljiviji delovi američkog autoputa | krajevi moraju biti projektovani (§4.3–4.4), ne ostavljeni |

### 2.2 Biblioteka trasa

Dve vrste linija, sve ostalo je izbor podskupa:

- **pojas** (`band`): pravougaonik na `D = 120 m` od spoljne ivice trotoara ivične ulice
  grida, uglovi **R = 260 m** (50 mph, e = 6 %; luk počinje 140 m pre ugla i prolazi 62 m
  od ugla). Ovo je geometrija prstena iz v1 — ali se gradi **samo deo** koji stablo traži.
- **radijale**: duž linije nekog kvarta, od pojasa napolje (prilaz aerodromu, kapija luke,
  ivica kopna) — ili duž bulevarske linije grida, od pojasa **unutra** (samo terminus u centru).

Zašto 120: unutrašnji terminal dijamanta pada na 62 m od ivičnjaka, tj. **76 m osa–osa** od
ivične ulice — jedan kratak gradski blok, koliko je i u Majamiju između rampe i prve
avenije; manje bi dve raskrsnice sedele jedna drugoj u krilu.

### 2.3 Listovi i stablo

**List** = mesto gde autoput ima razlog da se završi u nešto:

| list | linija | završetak |
|---|---|---|
| **A** aerodrom | prilaz aerodromu (sredina S/J obale) | krak u nivou se završava kao bulevar na servisnoj ulici aerodroma (SR 112 recept) |
| **H** luka | dalja od dve kapije luke | krak u nivou se završava na zadnjoj ulici luke (kamioni) |
| **M** kopno | `mainlandEdge` (50 % seedova) | causeway do ivice mape + naplatna rampa; portal za spawn/despawn |
| **D** centar | bulevarska linija grida | deblo se spušta na nivo i završava na signalisanoj raskrsnici sa ivičnom ulicom; bulevar nastavlja u grid |

**Stablo** (`FreewayPlan.Roll`, posle luke i aerodroma, pre sela):

1. Svaki list ima **port** — stanicu na pojasu gde ga seče njegova linija (D nema port, D je kraj).
2. **A je uvek kraj debla** (izlazni luk, §4.3): aerodrom stoji na 460 m od ivičnjaka, a
   truba traži 470 — ne staje; luk R=260 staje uvek jer je aerodrom na sredini duge strane.
3. **Deblo** = kraći luk pojasa od `port(A)` do `port(H)`, produžen **preko H** (i preko
   `port(M)` ako kopno postoji) do **D**: prve bulevarske linije grida ispred koje pojas
   ima **540 m bez ijedne pristupne ulice** (zona spusta, §4.4), tako da je deblo dugo
   **≥ 1.6 km** i da zona spusta počinje tek posle trake za ubrzanje poslednjeg
   priključka (+400). Deblo dakle uvek ima dva kraja: **A** i **D**.
4. **H visi o deblu trubom** (§4.2) ako je `port(H)` ≥ 850 m od `port(A)` duž pojasa;
   inače luka dobija **dijamant** na svojoj pristupnoj ulici (kamioni preko terminala —
   i to je uobičajeno za luku). **M** dobija trubu (deblo prolazi njegov port po
   konstrukciji).
5. Mreža je razgranata kad god geometrija to dozvoli: truba za H i/ili M. U seedu gde su
   aerodrom i luka bliži od 850 m i nema kopna, mreža je jedna linija A → D sa dijamantima
   — to je retko i log to kaže.
6. **Dijamanti** samo na uzdignutom delu debla (§2.6); na granama ih nema — grane su u
   nivou i kratke.

Oblici po seedu: A i H na istoj strani → deblo duž te strane + ugao + D na susednoj („L");
susedne strane → luk ugla + produžetak („L"/„U"); naspramne → „U" preko dva ugla.
Dužina debla **1.6–4.5 km**, grane 0.3–0.8 km.

### 2.4 Visine

- **Deblo je vijadukt na 7.0 m** (kolovoz) gde god ispod prolazi pristupna ulica sela —
  a to je ceo pojas osim zone spusta: pristupne ulice su duge 110 m i ne mogu preko, a rov
  na ostrvu sa dnom mora na −5 m bio bi pod vodom.
- **Grane su u nivou**: rampe trube (§4.2) ili izlazni luk (§4.3) spuštaju saobraćaj sa
  7.0 na 0.12, i dalje ide 2+2 kolovoz od 24.4 m na nasipu sa Jersey ogradom — grane su
  radijale, nijedna ulica ih ne seče (sela su na susednim linijama, `Clashes` ih drži ≥ 60 m).
- **Terminus D** u nivou (§4.4).
- **Nadvišenje** u lukovima 6 % (ceo presek kao jedna ravan; parapet ±0.73 m).
- **Reke**: deck ide dalje na 7.0, stubovi postaju mostovski jarmovi (§3.4); rezervacija
  korita ostaje (`ClearOfRivers`, `RiverClear 26`).
- **Brda**: pojas brda ostrva (danas 55–210 m od ivice grada) prelazi na `D + 45 .. D + 250`;
  reljef u koridoru `±60` se gasi (`HighwayFade`, treba mu polilinija, §11).

### 2.5 Šta stablo radi rasporedu kvartova (`CityLayout`)

Stablo se **kotrlja pre sela**: rezervacija sa kojom se sve ostalo sudara (`Clashes`).

| pravilo | vrednost | zašto |
|---|---|---|
| koridor debla i grana (polilinija + polu-širina) | osa ± 20 m (ROW ograda) | pod deckom samo stubovi i ono iz §7 |
| kvadrat dijamanta | 840 m duž × 140 m preko (osa ± 70) | rampe i terminali (§4.1) |
| kvadrat trube | 840 m duž × 350 m preko (−30 … +320 od ose, napolje) | iste pomoćne trake kao dijamant, četiri rampe, petlja, početak grane |
| zona spusta pred D | 540 m pojasa **bez pristupnih ulica** + luk unutra | deblo u nivou; ulica ga ne može preseći |
| `NearStrip` prvog prstena sela | 110 → **200 m** (na liniji dijamanta **280 m**) | telo sela van koridora; kapija ≥ 90 m od spoljnog terminala |
| obala | `islandWest/East/North/South` **+ 90 m** | tačno koliko su sela gurnuta; inače pada broj sela |
| pristupna ulica sela | seče deblo **denivelisano** (ispod, svetla 5.33 m) | nema ukrštanja u nivou, nikad |
| linija dijamanta | pristupna ulica = **bulevar** (35 m, 2+2, medijan) od ivične raskrsnice do kapije; linija grida se promoviše | terminali traže trake za levo |
| linija lista (A, H, M) | pristupna ulica kvarta **jeste grana** (krak u nivou zamenjuje ulicu od 15 m); luka na trubi mora stajati na stripu **≥ 470 m** (120 + 320 + 30) — sa obalom +90 uvek jeste (475–635) | truba se završava na 440 m od ivičnjaka |
| benzinska (`Wayside`) | nikad u koridoru; iza spoljnog terminala dijamanta (178–280 m) | „pumpa na izlazu" |

### 2.6 Izbor dijamanata

Kandidati su **linije koje nose pristupnu ulicu** i seku uzdignuti deo debla (arterija
koja se završava 50 m posle terminala je besmislica). Redosled: bulevarske linije grida,
pa ostale. Uslovi: razmak od susednog priključka (dijamant, truba, izlazni luk, zona
spusta) **≥ 850 m** (nos ulaza → nos sledećeg izlaza ≥ 400 m); stanica ≥ 100 m od
tangente luka. Očekivano **2–4 dijamanta** po seedu uz 1–2 trube. Selo bez dijamanta
stiže na autoput kroz grid do susedne arterije — kao u pravom gradu.

### 2.7 Kopno (M)

Na poluostrvu (`mainlandEdge`, `MainlandReach 700`) grana ka kopnu: truba na deblu,
krak u nivou do ivice mape, **naplatna rampa** na sredini (Rickenbacker/Venetian
causeway 1987 su naplaćivali; `TollPlaza` + `RoadNode.Toll` već rade), portal na ivici
mape gde se kola pojavljuju i nestaju kao brodovi. Faza 7.

## 3. Poprečni presek i profil

### 3.1 Deblo

```
   spolja                                                                     ka gridu
   ─┬──────────┬──┬──────────┬─
    │  deck A  │▌ │  deck B  │      deck: SM_Env_Road_Highway_01, 11.4 m, parapet spolja
    │ 11.4 m   │  │  11.4 m  │      razmak 1.6 m: Jersey ograda (SM_Prop_Barrier_03) + lampe
    ═╧══════════╧══╧══════════╧═     ukupno 24.4 m; ose deckova na ±6.5 m od ose
    ↑ 7.0 m    ▐▌ stub / deck / 20 m (SM_Env_Road_Highway_Pillar_01, T-glava 8.7 m)
   ─┴──────────────────────────┴─   tlo: ogoljen asfalt, propalice i props (§7), ograda ROW na ±20 m
```

- Kolovoz na **7.0 m**; ploča Synty decka je 1.55 m ispod površine → svetla visina
  **5.33 m** nad ulicom na 0.12 (standard 4.9–5.0).
- Trake u grafu: **2 po decku na ±2.85** (kao do sada) — **osim ako** `Highway_01` u meshu
  ima 3 trake (inventar procenjuje 3 × 3.8 bez bankine, kakav je bio Embarcadero); to
  se sredi u fazi 0 gledanjem mesha, i tada su trake na −3.8/0/+3.8.
- Deck je **dva jednosmerna kolovoza** (`AddOneWay`), samo što od sada kolovoz ume da bude
  polilinija (§5.1).
- Stubovi: po jedan pod svakim deckom svakih 20 m; nad ulicom stubovi na ±16 m od ose
  ulice (raspon 32 m), nikad u kolovozu (`PierFree` postoji).
- **Grana u nivou**: isti presek 24.4 m na `RoadBed`, bez stubova, Jersey u sredini, bankine
  3 m, ograda ROW; iste pločice decka polažu se na tlo (parapet ostaje kao ivična ograda).

### 3.2 Rampa

Jedna traka, kolovoz **7.3 m**, parapet spolja; osa rampe na **±18 m** od ose debla
(2 m vazduha do parapeta decka). Pomoćna traka (usporavanje/ubrzanje) je **proširenje
decka** na 15.2 m — deck se za dužinu pomoćne trake širi (procedura, §6), parapet ide sa
njim.

### 3.3 Profil rampe (izlaz; ulaz je ogledalo)

```
   7.0 ────────────────╮ K=5, A=6 → 30 m
                        ╲  6 %  ·  85 m
                         ╲
                          ╰──────── K=5, A=6 → 30 m ─── 0.12
      gore(nos)   |<——————— 145 m ———————>|  luk   terminal / grana
```

| deonica | dužina | napomena |
|---|---|---|
| traka za usporavanje (paralelna, na decku) | 120 m (sužavanje 50 + paralelno 70) | 24.6 → 13 m/s, a = 1.8 m/s² |
| gore: nos + razmicanje osa 14.1 → 18 m | 35 m | ovde rampa dobija svoj parapet |
| ispupčena vertikalna krivina | 30 m (pad 0.9 m) | K = 5 m/% (rampa 25 mph) |
| ravan pad 6 % | 85 m (pad 5.08 m) | |
| udubljena vertikalna krivina | 30 m (pad 0.9 m) | ukupno 6.88 m = 7.0 − 0.12 ✓ |
| horizontalni luk | dijamant **R = 40**, 90° (20 mph); truba **R = 120** (35 mph) ili petlja **R = 50** (20 mph) | |
| kraj | dijamant: T na arteriji na **±58 m**; truba: ulivanje u granu | |

Ulaz: kraj → luk → uspon (30 + 85 + 30) → nos (35) → **traka za ubrzanje 180 m**
(paralelno 120 + sužavanje 60). Nagib 6 % je gornja gradska granica AASHTO-a.

### 3.4 Mostovi i prelazi

- **Ulica ispod** (svaka pristupna ulica): deck ide dalje, stubovi na ±16 m od ose ulice.
- **Reka**: `SM_Env_Bridge_Support_01` (23.4 m — skoro tačno dvostruki deck 22.8 + 1.6)
  kao mostovski jaram u vodi svakih 15 m, obalni `Bridge_Wall_01` ×4 kao upornjak, ono što
  već radi `BeltRiverBridge`.
- **Luk R=260**: tetive od 20 m daju prelom 4.4° po spoju — vidljivo na parapetu; zato su
  lukovi (i sve rampe) **ekstrudirani** (§6), a ne slagani.
- **Spust debla** (zona D, ili izlazni luk ka listu): 4 %, krivine K 45 (ispupčenje) /
  30 (udubljenje) → 180 + 22 + 120 = **322 m** za 6.88 m; na tlu `RoadBed` sa `HighwayFade`.

## 4. Priključci i krajevi

### 4.1 Tight diamond (na uzdignutom deblu)

```
                 spoljni terminal (T, semafor)  ← arterija ide dalje ka kapiji kvarta
   ────────────────────┬──────────────────────────
     deck B (−u) ═══╗  │  ╔═══ ulaz B  ← nos B  → ...traka za ubrzanje... (−u)
   ...izlaz B ──────╝  │  ╚═══════════════════════════════════════════════════
     ═══════════════════╪═════════════════ deblo, ose deckova ±6.5 ═══════════
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
deck A sleće na unutrašnji terminal, deck B na spoljni. Kvadrat: **840 × 140 m**.

Terminal je pravi `RoadNode` sa **semaforom** (danas ih dobijaju samo čvorovi grida — ručni
čvor mora da ume da bude signalisan), sa trakom za levo na arteriji (medijan bulevara se
tu prekida), desno sa rampe slobodno uz ustupanje, levo na semaforu. Pešački prelaz
preko rampe samo na signalisanoj strani; niko ne hoda uz rampu ni po decku (nema
`PedLink`-ova; stubovi su `WalkObstacles.Blocked`).

### 4.2 Truba (grana ka listu A, H ili M)

```
                                  grana u nivou, 2+2, 24.4 m
                                  │ ↑ ka listu (aerodrom / luka / kopno)
                       ╭──────────┤├──────────╮   v = +320: grana počinje
        petlja R=50  ╭─╯          ││          ╰─╮   direktna R=120
        (20 mph)     │   ╭────────┤├─────╮      │
                     ╰───╯        ││     │      │
   ═══ deck B (−u) ══╪════════════╪╪═════╪══════╪═══════════ 7.0 m
   ═══ deck A (+u) ══╪════════════╪╪═════╪══════╪═══════════
        ╰─ poludirektna R=120     ╰╯     ╰─ direktna R=120 (ispod debla)
           (ispod debla)       u = 0 (osa grane)
   duž debla: −400 … +400 (pomoćne trake kao kod dijamanta);  preko: −30 … +320 (napolje)
```

Četiri rampe, sve jednotračne 7.3 m, sve se spuštaju sa 7.0 na 0.12 na ≤ 6 % (§3.3), sve
prolaze **ispod** uzdignutog debla gde ga seku (svetla 5.33 — nema drugog nivoa):

| kretanje | rampa | poluprečnik |
|---|---|---|
| deblo (+u) → grana | direktna, desno | R = 120 (35 mph) |
| grana → deblo (−u) | direktna, desno, ispod debla | R = 120 |
| deblo (−u) → grana | poludirektna, levo, ispod debla | R = 120 |
| grana → deblo (+u) | **petlja** 270° | R = 50 (20 mph) |

Truba je u nivou čim siđe s decka — zato grana počinje u nivou na `v = 320` (440 m od
ivičnjaka) i takva ostaje do lista. Direktna rampa ima 72 m prave + 188 m luka za 145 m
spusta — pad sme da ide kroz luk, kao i kod petlje. Kvadrat: **840 × 350 m**. Aerodrom (strip 460) trubu
**ne može** da primi (440 + 30 > 460) — zato je A uvek kraj debla (§4.3); luka mora imati
strip ≥ 470 (danas 415–545, sa obalom +90: 475–635).

### 4.3 Kraj debla ka listu (izlazni luk)

A nije truba nego **sam kraj debla**: deblo skreće napolje lukom **R = 260** (408 m) u
liniju prilaza aerodromu i na tom luku se **spušta** (4 %, 322 m ≤ 408) na nivo; grana u
nivou nastavlja do kvarta i završava se kao bulevar na njegovoj kapiji (T na servisnoj
ulici aerodroma — to je već čvor grafa preko `WeldRoads`). Luk počinje 260 m pre linije
i završava na 380 m od ivičnjaka; aerodrom je na 460 → 80 m grane u nivou. Ako bi luk
udario u ugao pojasa (aerodrom preblizu ugla), R = 200 (45 mph): luk 314 m, kraj na 320,
ostatak spusta (8 m) na pravoj grani.

### 4.4 Terminus u centru (D)

```
   ═══════════ deblo na 7.0 ═══╲__ 4 % · 322 m __ nivo · 60 m ─╮ R=90 (30 mph)
                                                                 │
        zona spusta: 540 m pojasa bez ijedne pristupne ulice     │  luk unutra do 30 m od ivičnjaka
                                                                 ▼
                                               ──┬── ivična ulica grida: signalisana raskrsnica
                                                 │   „END FREEWAY", 55 → 35 mph, 2+2 → bulevar
                                                 ▼   bulevar nastavlja u grid
```

Deblo se spušta duž pojasa (paralelno sa gridom) — to je jedini deo debla u nivou, zato
tu **ne sme biti ni jedna pristupna ulica** (540 m: 322 spust + 60 nivo + 90 luk + zazori;
`FreewayPlan` to rezerviše, `Clashes` ne dozvoli selo na tim linijama). Luk unutra R = 90
završava na 30 m od ivičnjaka, 44 m do ose ivične ulice; raskrsnica je četvorokraka,
signalisana, bulevarska linija grida (promovisana) nastavlja u centar. Nikakav slepi kraj:
lutalica sa debla prosto uđe u grid.

### 4.5 Parclo (bulevari, faza 7)

Gde arterija nosi pravi bulevar grida i ima mesta u dva kvadranta: petlje R = 50 umesto
dva leva skretanja na terminalu. Kvadrat 840 × 240 m. Dijamant radi sve što treba,
parclo je karakter.

### 4.6 Šta ovo popravlja od prijavljenog

| prigovor | ovde |
|---|---|
| „glupe uključne/isključne rampe" | rampa je 340 m od trake za usporavanje do terminala, nagib 6 %, luk R = 40 na dnu, pomoćne trake na decku, nos sa parapetom, terminal sa semaforom — mere iz priručnika, ne iz grafa |
| „loše se povezivao na grad" | dijamant samo na arteriji koja vodi negde (kvart ↔ grid), arterija je bulevar, terminal 76 m od ivične raskrsnice; grane se završavaju **u** aerodrom/luku/centar, ne u vazduhu; nema servisnih ulica ni praznih kvadrata |
| ukrštanja u nivou | nijedno |
| glavni tok staje na spajanju | nikad (§5.3 — invarijanta harnessa) |
| prsten | ne: stablo sa 2–3 grane po seedu |

## 5. Lane-graf i vozač: šta mora da se doda

Ovo je jezgro plana. Bez ovih šest stvari svaki novi autoput završi kao prethodna tri.
Truba i terminus ne traže ništa preko ovoga — truba je četiri rampe (isti `Fork`/`Merge`
kao dijamant), terminus je obična signalisana raskrsnica.

### 5.1 Kolovoz kao polilinija (`Carriageway.Path`)

`Carriageway` dobija niz stanica `(tačka, smer)` sa kumulativnim `s`; `Right(s)`,
`PointAt(s,d)`, `Project(p)` rade po segmentu; `RoadCar` već vozi u `(s,d)` — menja se
samo izvor okvira. Tetive ≤ 10 m za R < 100, ≤ 20 m inače; smer je kontinualan pa kola
ne cimaju. Isti `Path` koristi i polaganje (tetive za pločice, ekstruzija za krivine).
Kolovoz više **ne mora da se lomi u čvor na svakom prelomu**.

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
- Terminali na arteriji i terminus D ostaju obične `RoadNode` kutije sa semaforom.

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
  terminalu lutalica sa arterije uzima ulaznu rampu sa **0.5**; na kraju grane / terminusu
  prosto nastavlja u kvart ili grid — tako se mreža puni i prazni bez rutiranja.
- `RouteToward` postaje Dijkstra po **vremenu** (`dužina / min(limit, cruise profila)`):
  patrola, gangster i kamion biraju autoput kad je brži, ne kad je kraći.
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
R 120 → 15.5, R 90 → 13.4, R 50 → 10, R 40 → 8.9); profili sa većim `LateralG` idu brže.
`TimeGap` na autoputu 1.2 s (danas 0.9 na ulici). Ograničenje decka 24.6 m/s (55 mph),
luk 22.4 (50 mph), zona spusta 35 mph.

### 5.7 Spawn i tok

Kola na deck trakama svakih 40 m po traci (gradski autoput u podne): mreža od 2.5–5 km
= **250–500 kola**. Mreža je otvorena: kola ulaze i izlaze kroz listove i dijamante, ne
uklanjaju se; portal na kopnu (§2.7) spawnuje/uklanja `through` saobraćaj. Kamioni:
luka ↔ industrijski blokovi jezgra (`ind-*`) — rutirani po vremenu, dakle autoputem.

## 6. Geometrija i delovi (šta od Synty-ja, šta se ekstrudira)

Inventar (`gangsters_measure`, 380 prefaba, 2026-08-25) kaže: Synty ima **prav deck od
11.4 × 20 m, luk 90° R≈12.5, luk 45° R≈25, rampu od 5 m visine na 25 %/12.5 %, T-stub** — i
ništa drugo za autoput. Nema krivine R 40–260, nema kolovoza od 7.3 m, nema proširenja,
nema nosa. Zato:

| element | izvedba |
|---|---|
| prava deonica decka (uzdignuta ili u nivou) | `SM_Env_Road_Highway_01`, tetive od 20 m, `FreewayKit.LayDeck` (već ume razvlačenje i nagib) |
| lukovi R 40–260, proširenje 11.4 → 15.2, nos, rampa 7.3 m sa parapetom, petlja | **procedurna ekstruzija** profila decka (ploča 1.55, parapet 1.2, ista `PolygonPalmCity` atlas mapa sa UV-ima očitanim iz `Highway_01`) duž `Path`-a; spoj sa pločicom nevidljiv jer je materijal i UV traka ista |
| oznake na decku | zapečene u `Highway_01`; na ekstruziji iste UV trake; nos = `SM_Env_Road_Median_01` (šrafura) na +0.02 |
| stub | `Highway_Pillar_01`, `StandPillar` ga razvlači za 7 m deck (napravljen za 5) |
| razdelnik | `SM_Prop_Barrier_03` (2.5 m, pivot na kraju, lančanje) u razmaku 1.6 m |
| most preko reke | `SM_Env_Bridge_Support_01` (23.4 m) svakih 15 m, `Bridge_Wall_01` ×4 na obalama |
| ograda ROW | `SM_Env_Fence_01` (5 m žica) na ±20 m, sa rupama gde su propalice ušle (§7) |
| ~~zid protiv buke~~ | **IZBAČEN 2026-08-26** (korisnik, na slici iz Play-a: „ovaj zid nema neku poentu“). Ono što je stajalo u sceni bio je zidić od cigle položen duž **cele** uzdignute trase, metar iznad parapeta i sa njegove spoljašnje strane — a trasa ide 120 m od najbližeg prozora, pa nije zaštićivao ništa i nije stajao ni na čemu. Ako put jednom prođe pored kuća, dobija betonski, na parapetu, na dužini na kojoj prolazi. |
| lampe | `SM_Prop_LightPole_Base_01` (kobra, 6.5 m) skaliran ×1.6 na parapetima, naizmenično, svakih 50 m; na priključcima `airport-apron-mast` (15 m) ×2 = jarbol 30 m |
| putokazi | `SM_Prop_Sign_Pole_04` (konzola 6.5 m) + `Billboard_Sign` ploča sa novim zelenim materijalom + TMP tekst; bočni: `SM_Sign_Blank_01/02` zeleni; `SM_Sign_Speed_55/35` postoje; `No_Entry`, `One_Way`, `Give_Way` postoje |
| bilbordi | `SM_Prop_Billboard_01` + `_Pole_01` (6.9 m) van ROW-a, okrenuti saobraćaju |
| ramp-meter | `SM_Prop_Traffic_Light_02` na `Sign_Pole_02`, samo dekor u v1 |
| miljokaz | `SM_Sign_Blank_04` na `Sign_Pole_01` |
| naplatna (samo causeway) | `FreewayKit` booth + `Barrier_Gate_01`, već radi |

Napomena o memoriji „u blokove samo katalog": ovo nisu ukrasi na placu nego **kolovoz**
koji katalog fizički nema u traženim poluprečnicima; ekstruzija nosi Syntyjev materijal i
UV-e pa se u sceni ne razlikuje od pločice. Traži potvrdu (§13).

## 7. Opremanje, signalizacija, život ispod decka (period 1987)

- **Zeleni putokazi** (belo na zelenom, MUTCD): na 800 m i 400 m pre izlaza („EXIT 3 —
  Harbor Blvd — ½ MILE"), na nosu („EXIT 3 ↗"), posle svakog ulaza „SPEED LIMIT 55" +
  potvrdna tabla rute. **Sekvencijalni brojevi izlaza** od terminusa D ka listovima
  (Florida 1987); na trubi zeleni putokaz sa dva imena („AIRPORT ↗ · PORT →"). Pred D:
  „FREEWAY ENDS ½ MILE", „SPEED LIMIT 35", pa „END FREEWAY". Na izlaznim terminalima
  „DO NOT ENTER" / „WRONG WAY", na ulaznim „ONE WAY" i zeleni trailblazer „SR 9 EAST →".
- **Ime**: pul u `StreetNames` — deblo „<Grad> Expressway" / „Bayshore Expressway" /
  „STATE ROAD 9xx"; grane po listu: „Airport Expressway", „Port Connector", „<Kopno>
  Causeway". Mapa (`DemoMap`) dobija sloj: debela crveno-plava linija sa štitom, tanke
  rampe, broj izlaza uz svaki priključak, imena grana.
- **Bilbordi** (odlučeno: da): `SM_Prop_Billboard_01` na ~300 m, van ROW-a na strani sela,
  okrenuti ka dolazećem saobraćaju; sadržaj = period reklame (cigarete, pivo, Miami Vice
  paleta, lokalne firme iz ledgera — advokat za RICO, auto-plac, motel). Nikad u koridoru.
- **Zid protiv buke** (odlučeno: da): na parapetu decka uz sela; u nivou dva reda panela.
- **Ispod decka** (odlučeno: propalice i props): ogoljen asfalt i šljunak između stubova,
  a na njemu ono što se pod gradskim vijaduktom 1987. i nalazi — **buradi sa vatrom,
  dušeci, kolica iz samoposluge, palete, kartonske kutije, kese đubreta, gume, olupina
  bez točkova, grafiti na stubovima** (Synty: `Barrel`, `Mattress`, `Shopping_Cart`,
  `Pallet`, `Cardboard_Box`, `Trash_Bag`, `Tyre`, `Car_Wreck` iz GangWarfare/City/
  Nightclubs paketa — tačna imena se izmere u fazi 5) i **propalice**: 2–4 po stubu-grupi,
  civilni `CrowdLooks` sa „skitnica" kostimom, idle/sedeći/ležeći loop iz Synty animacija,
  ne hodaju po gridu (pešački graf ih ne vidi), reaguju na pucnjavu kao civili
  (`StreetAlarm`). Gušće na priključcima i uz terminus D, ređe na pravcu. Rupe u ROW
  ogradi tamo gde ih ima.
- Detalji koje ne radimo u v1: telefoni za pomoć, drenaža.

## 8. Saobraćaj i igra

- Gustina i perf: 250–500 kola na mreži; deck, stubovi i ograda u `MergedChunk`-ovima po
  100 m; kola na decku su obična `RoadCar`.
- Policija patrolira autoputem (BFS po vremenu); potera `Hot` profilom po decku; spikes
  (`SM_Prop_Road_Spikes`) na rampi kao blokada — faza 7.
- Drive-by na decku radi kao na ulici (isti `CrewCar`), gangster ide 27 m/s.
- Kamioni luka ↔ industrija, krađa kamiona na rampi, sastanci pod deckom (propalice kao
  svedoci) — kuke za kasnije.

## 9. Provera

**Bez Unity-ja — `python Docs/expressway.py`** (mora da prođe pre prve linije C#):
koridor i kvadrati u pojasu sela; luk R=260 ne dira ivični trotoar; rampa: zbir padova =
7.0 − 0.12, nagib ≤ 6 %, K ≥ 5, dužina ≥ 150; terminali dijamanta na ±58, ≥ 70 m od ivične
raskrsnice grida (76) i ≥ 90 m od kapije sela (102); truba: sve četiri rampe imaju mesta
za pad, petlja R=50 je duža od pada, grana počinje pre lista (luka ≥ 470, aerodrom ne može trubu);
izlazni luk: spust 4 % staje na luk od 408 m i završava pre lista; terminus D: zona spusta
540 m, luk R=90 ostavlja ≥ 40 m do ivične raskrsnice; brzine po krivinama; razmak
priključaka ≥ 850 sa nosevima ≥ 400; oblici debla za tipične gridove (L, U) sa dužinom
1.6–4.5 km i ≥ 1 trubom; svetla visina ≥ 4.9; obala +90 drži ono što je držala.

**Harness** (`gangsters_play` u lab sceni, pa `analyze.py --freeway`, presuda po tally-ju
od 30): novi redovi `merge` (id, razmak, čekanje), `lanechange` (id, obavezna/diskreciona),
postojeći `deck`. Kvarovi: `mainlinestop` (kolo na glavnoj traci < 3 m/s duže od 4 s bez
zastoja ispred), `mergestuck` (> 20 s na kraju pomoćne trake), `wrongway`, `carstuck`,
izuzeci — **svi 0**. Mere: medijana brzine na decku **≥ 20 m/s**; svaka rampa (dijamant i
truba) upotrebljena bar jednom u 5 min; bar jedno putovanje list → list (npr. aerodrom →
luka) i bar jedno deblo → D → grid; lutalice čine ≥ 30 % silazaka.

## 10. Faze (svaka ostavlja grad u ispravnom stanju)

| faza | šta | fajlovi | gotovo kad |
|---|---|---|---|
| **0** | plan + `expressway.py`; probe mesha `Highway_01` (trake, UV trake) — mali editor command | `Docs/`, `PipelineCommands.cs` | 0 padova; broj traka odlučen |
| **1** | `FreewayPlan` u `CityLayout.Roll`: portovi listova, deblo (luk + produžetak do D), trube, zona spusta, dijamanti; sve kao polilinijske rezervacije; NearStrip 200/280, obala +90, promocija arterija; `gangsters_layout` ispisuje stablo; `DemoMap` crta sloj | `CityLayout.cs`, `Districts.cs`, `Island.cs`, `DemoMap.cs`, `StreetNames.cs`, `PipelineCommands.cs` | 30 seed-ova: ≥ 6 sela, deblo A → D, truba za H u ≥ 80 % seedova, 2–4 dijamanta, 0 sudara, luka uvek sa stripom ≥ 470 |
| **2** | `LaneNet`: `Path` kolovozi, `Lane.s0/s1`, `Class` + `CruiseFor`, brzina u krivini, rutiranje po vremenu — **bez ijednog novog puta** | `LaneNet.cs`, `RoadCar.cs`, `DriverProfile.cs` | regresija: CrewDemo/CoreDemo/Game harness kao pre (tally 30) |
| **3** | `Fork`/`Merge`, `LaneChange`, izlazi lutalica; **lab scena** `ExpresswayLab.unity` (grid 4×3 + deblo sa jednim dijamantom, jednom trubom ka „aerodromu" od dva bloka i terminusom D — sva tri tipa čvora; tanki host kao `BlockLabBuilder`); `analyze.py --freeway` proširen | `LaneNet.cs`, `RoadCar.cs`, `Assets/ExpresswayLab/`, `Tools/play/analyze.py`, `DriveTrace` | invarijante iz §9 u labu, 30/30 |
| **4** | geometrija u gradu: `RoadDemoBuilder.Expressway.cs` (profil, ekstruder `DeckMesh`, pločice na pravim, stubovi, mostovi, rampe, truba, izlazni luk, terminus, bulevar-arterija, tlo, polilinijska rezervacija + `HighwayFade`, pojas brda) | `RoadDemoBuilder.Expressway.cs`, `FreewayKit.cs`, `District.cs`, `Island.cs`, `Seams.cs`, `Wayside.cs` | Game.unity harness 30/30; Play pregled |
| **5** | opremanje: ograde, razdelnik, lampe, putokazi, brojevi izlaza, zid protiv buke, bilbordi, život ispod decka (props + propalice), mapa, imena | `RoadDemoBuilder.ExpresswayDressing.cs`, `CityLife.cs`, `DemoMap.cs` | Play pregled sa §7 kao kontrolnom listom |
| **6** | čišćenje: belt, šav `Highway`, `FreewayRoute`, `HighwayTraffic`, `freeway.py`/`motorway.py` napolje; `freeways` prekidač → `expressway`; docs | v. §12 | kompajl čist, tally 30 |
| **7** | opciono: causeway + naplata (M), parclo, kamioni, potera, sastanci pod deckom | | |

Redosled 2 → 3 → 4 je namerno: graf pre geometrije, jer je graf ono što je tri puta
zakazalo, a lab scena proverava sva tri tipa čvora pre nego što se dotakne grad.

## 11. Šta u postojećim sistemima mora da se promeni (van autoputa)

- `DistrictReservations`: nova vrsta rezervacije **polilinija + polu-širina** (danas samo
  osni pravougaonici) — `FlatAt`, `InPaved`, `HighwayFade`, `UnderHighwayOrRiver`,
  `DressWilderness` (5 mesta).
- `CityLayout`: list A/H čija je pristupna ulica grana; zona spusta bez pinova; `Clashes`
  protiv polilinije.
- `BuildSignals`: semafor i na ručnim čvorovima (`RoadNode.Signalled`) — terminali, D.
- `PlanNext`: kandidat „izlaz" odvojen od „pravo".
- `RouteToward`: težina po vremenu.
- `ScaleLifeToCity`: budžet kola za mrežu; `CityLife`: propalice pod deckom.
- `Wayside`: koridor je zabranjen; pumpa iza spoljnog terminala.
- `Island`: pojas brda pomeren, obala +90.

## 12. Šta se zadržava od prethodnih pokušaja, šta ide napolje

**Ostaje** (dobro i već izmereno): `FreewayKit` (merenje, `LayDeck`, `StandPillar`, `Sit`),
`Carriageway.SurfaceY/SurfaceAt/Elevated` + `RoadCar.SurfaceLift`, `AddOneWay`,
`_midSegment/LaneSegment`, `RoadNode.Toll` + `TollPlaza`, `PaveFreewayJunction`,
`PierFree/InAnyRoad`, `ClearOfRivers/RiverCrossings/BeltRiverBridge`, `HighwayFade`,
DriveTrace `deck/toll` redovi i `analyze.py --freeway`.

**Napolje posle faze 4**: `RoadDemoBuilder.Belt.cs` (prsten u nivou), šav `SeamKind.Highway`
+ `Interchange.cs` (servisne, dijamant 6 %), `FreewayRoute` u `Freeway.cs` (osim toll
delova), `HighwayTraffic`, `Docs/freeway.py` i `motorway.py` (već ne odgovaraju kodu),
`Docs/freeway-interchange.md` → jedan pasus istorije u ovom dokumentu.

## 13. Odluke

Odlučeno 2026-08-25: **razgranato, ne prsten; bilbordi da; zid protiv buke da; ispod decka
propalice i props.**

Još čeka:

1. **Terminus D u centru** u prvoj verziji (predlog: da — bez njega mreža bez kopna ima
   samo jednu granu, a centar ostaje bez „kraja autoputa"; cena je zona od 540 m pojasa bez
   sela i promocija jedne linije u bulevar).
2. **Procedurna ekstruzija** decka za lukove/rampe/proširenja sa Synty materijalom (§6) —
   bez toga nema krivine R 40–260 ni rampe od 7.3 m; predlog: da.
3. **Obala +90 m** da prstenovi sela stanu (§2.5) — alternativa je manje sela.
4. **Causeway sa naplatom** na poluostrvu (§2.7) — predlog: faza 7, ne sad.
5. **Broj traka na decku** (2 ili 3) — odlučuje mesh u fazi 0, ne ukus.

## 14. Kako je izvedeno (2026-08-25)

Plan je implementiran i vrti se u `Assets/Scenes/ExpresswayDemo.unity` (grid 17×5 linija,
blok 85×70, pojas 120 m, luk 200 m, deck 7 m; dijamant na liniji 4, grana T na 12,
terminus na liniji 0). Ovo je razlika između papira i onoga što stoji u sceni.

### 14.1 Šta je napravljeno

| deo plana | gde je u kodu |
|---|---|
| linija koja se **savija** (§5.1) | `RoadLine.cs` (polilinija, Hermite poza, poluprečnik, `Offset/Sub/Reversed/Corner`) + `Carriageway.Path`/`DirAt`/`RightAt`/`RadiusAt` |
| **račvanje i ulivanje bez kutije** (§5.3) | `RoadNode.Seam` + `LaneNet.PrepareSeam`: svaka ulazna traka ide u jednu izlaznu koja se s njom poklapa i **ništa se ne sudara**; traka koja se završava dobija spoj u susednu sa ustupanjem (jedini konflikt na šavu) |
| **trake sa početkom i krajem** (§5.2) | pomoćna traka je traka deonice koja postoji samo između svojih šavova (`RoadEdge.Auxiliary/Exit`) |
| **promena trake** (§5.4) | `RoadCar.TickLaneChange/WantedLane/GapForLane/BeginLaneChange`, `Manoeuvre.LaneChange` |
| **brzine po klasi puta** (§5.6) | `RoadClass` + `DriverProfile.CruiseOn`; brzina u krivini `sqrt(a·R)` |
| **rutiranje po vremenu** (§5.5) | `LaneNet.RouteToward`: težina = dužina × (10 / ograničenje), i promena trake je **grana pretrage** (`shift` tabela) |
| **profil i geometrija** (§3, §6) | `DeckMesh` — presek ekstrudiran duž linije, nadvišen u krivini, materijal i UV **očitani sa Syntyjevog decka**; `DeckMesh.Paint` za linije |
| trasa, dijamant, grana, terminus (§2, §4) | `Expressway.cs` (rešavač) + `RoadDemoBuilder.Expressway.cs` |
| opremanje (§7) | `RoadDemoBuilder.ExpresswayDressing.cs`: lampe, jarboli, ROW ograda, zid protiv buke, zeleni putokazi, bilbordi, props i propalice pod deckom (`RoadsideFigure`) |
| naplata | `TollPlaza`/`TollGate` iz starog autoputa, **na grani** |

### 14.2 Gde se izvedeno razlikuje od plana

1. **Truba → semi-direkcioni T.** Petlje od 270° (R = 50) nema. Sve četiri rampe grane su
   desne, a dve koje moraju na drugu stranu **prolaze ispod decka** (plan je i tražio da
   prolaze ispod). Rampa koja ide ispod spusti se na tlo u prvih 38 % dužine (≈ 4 %).
2. **Rampa je jedna kriva, ne „prava + luk R=40".** Bezijeova kriva od nosa do terminala
   daje poluprečnik 150–200 m umesto 40 — blaže od plana. Nagib i dužina u granicama.
3. **Naplata je na grani, ne na kičmi.** Dva priključka na pola milje ostavljaju **4 m**
   trase između svojih pomoćnih traka; barijera traži 200. Grana ima 430 m praznog kolovoza.
4. **Arterija dijamanta je ulica od 10 m**, ne bulevar od 35 (linija grida jeste
   promovisana u bulevar; koridor van grida polaže `StreetKit` kolovoz). Terminali jesu
   signalisani.
5. **Zeleni putokazi nemaju slova** — tabla, nosač i mesto su ono što se čita sa 200 m.
   Brojevi izlaza postoje u podacima (`Exchange.Number`).
6. **Nema sloja na strateškoj mapi** ni imena rute.

### 14.3 Šta je popravljeno u zajedničkom kodu (van autoputa)

- **`RoadSpace.Overlap` je bio 2D.** Rampa ispod vijadukta delila je isti kvadratni metar
  sa deckom 7 m iznad, pa su se kola odbijala zauvek: **10.281 odbijenih koraka u jednom
  runu**. Dodat `RoadSpace.Storey = 3.2 m`. Posle: **208**.
- **`RoadCar.Cruise()` u raskrsnici.** Dok je auto u kutiji `Road` je null, pa je nova
  brzina po klasi padala na gradskih 10 m/s **usred autoputa** — kočenje na svakom šavu.
  Sada odgovara put na koji ulazi (`Via.To.Road`).
- **`SpawnCars`** je išao redom po `_edges`, pa je poslednje ožičenom putu ostajalo nula
  kola; sada se lista promeša seedom.
- `CorePavement.Lay`: brojač `open` zaklonjen lokalnom promenljivom istog imena — kompajl
  je stajao (tuđa nezavršena izmena).

### 14.4 Primedbe iz Play-a i šta je urađeno

*„rampa ne sme da ima zidove ka unutra"* → parapet decka se otvara na nosu, a rampa nema
zid ka autoputu uopšte.

*„ukljucenja ne treba da imaju ogradu u delu gde su autoput i obrnuto; fale linije; rampe
za naplatu"* → (a) otvor se **meri sa rampi** (`MeasureRampWindows`): parapet je dole
tačno dokle rampa stvarno ide uz deck **i na istoj visini** (rampa koja prolazi ISPOD
nije „pored"), a gore je svuda drugde; (b) `DeckMesh.Paint` crta ivične i isprekidane
linije po celoj mreži; (c) naplatna plaza sa dve kapije.

*„lampe i stubove po sred puta; nema ivicnjake na nekim mestima; na dosta mesta tone u
zemlju"* → (a) **lampe su na razdelniku** — parapet nije na istom mestu celom dužinom
(gde se deck širi za izlaz ivica se pomeri četiri metra, pa stub završi u traci);
putokazi i table brzine idu po **stvarnoj** ivici (`DeckEdgeAt`), stubovi se drže i van
grana; (b) ivičnjaci vraćeni svuda osim uz samu rampu; (c) **ceo koridor se drži ravnim**
— trasa, svaka rampa, obe grane, arterije i kapije (495 kvadrata, `Level` + `NoFlora`).
Prvo sam probao da teren **isečem** (`Pave`): put se video, ali je oko njega ostao rov
otvorenog mora, jer isečena ćelija je ćelija koju ostrvo uopšte ne crta. Ravnanje na
`RoadBed` je ispravno: asfalt stoji 18 cm iznad tla, bez rupa.

### 14.5 „Reši sve" (2026-08-25, četvrti krug)

1. **Tangenta na spoju deonica.** `RoadLine.Sub` je svoju prvu tangentu računao kao
   TETIVU, a tetiva nije tangenta: svaki isečak je izlazio iz roditeljske linije za
   stepen-dva, pa je na svakom šavu autoputa auto dobijao put koji pokazuje malo drugde.
   Sada `Through/Sub/Offset/Reversed` prenose **prave smerove na krajevima**.
2. **Krajevi trase.** Deck je prestajao 26 m pre terminalnog čvora, a sam čvor je stajao
   još 20 m dalje — konektor od skoro 40 m, tj. auto koji je u kutiji raskrsnice sekundu
   i po. Trasa se sada završava **na licu** gradske raskrsnice, a deonice staju 10 m
   pre nje. **Belt odbijanja: 1756 → 1.**
3. **Bez promene trake preko raskrsnice.** Auto koji je još prelazio kad stigne u kutiju
   unosio je bočni pomak u nju, a kutija ga je isterivala preko dužine konektora — na
   šavu autoputa to je nekoliko metara, dakle trzaj u stranu (`jump` u crnoj kutiji).
   Sada se prelazak ne započinje ako do kutije nema dužina prelaska + 15 m.
4. **Arterija dijamanta je bulevar**: dva kolovoza sa medijanom od 15 m između njih,
   trake na ±7.5 i ±12.5, `MedianHalf = 5` — terminali sada imaju gde da čekaju levo.
5. **Autoput na strateškoj mapi**: crta se **svojom bojom** (`Deck`) i **po liniji koju
   prati**, a ne kao tetiva od kraja do kraja (`DemoMap.BuildPlan`).
6. **Zelene table imaju slova**: TMP legenda („EXIT 2 / PORT / 1/2 MILE"), okrenuta ka
   saobraćaju, na **živom** korenu — tekst uvučen u merge izgubi svoj materijal.

### 14.6 Presuda

`unity command gangsters_play --scene Assets/Scenes/ExpresswayDemo.unity --seconds 180`:

| | prvi run | posle popravki |
|---|---|---|
| belt odbijanja | 10.281 | **1756, od 5 kola** |
| `speeding` | 395 | **0** |
| `stall` | 818 | 735 — **svi u gridu**, red na semaforima |
| promene trake | 67 | 62 |
| `deck` ulazak/izlazak | 383 | 357 |
| naplata | — | **11** |

Graf u sceni: **18 deonica autoputa, 10 rampi/grana, 18 šavova, 8 pomoćnih traka (4
izlazne), 2 naplatne kapije**. `python Docs/expressway.py` — 66 provera, 0 padova.

**Šta još nije dobro:** nekoliko zaglavljenih parova kola (kapija grane, spoj deonica);
`jump` 125 i `steer` 67 — prelomi na spojevima deonica, verovatno tangenta u
`RoadLine.Sub` (podlinija računa svoj prvi tangent kao tetivu, pa se smer za dlaku ne
poklopi sa roditeljem); **tally od 30 runova nije puštan**; nema sloja na mapi ni slova
na tablama.

## 15. Kotrljanje: ista mapa, drugi grad, drugi autoput (2026-08-25)

Korisnik: *„znas da ce mapa cela random da se generise svaki put i da autoput isto treba
random i da radi?"* — do tada je trasa bila **ručno zadata** u sceni i radila je za jedan
grad. Sada se kotrlja iz istog broja iz kog i grad.

### 15.1 Kako se kotrlja

`ExpresswayRoute.roll` (podrazumevano uključeno) → `ExpresswayLayout.Roll`:

- **linija sa koje trasa kreće** — u prvoj četvrtini grida, nikad ivična;
- **linija na kojoj umire** — u **donjoj trećini**, bulevar ako ga grad tu ima (što je
  južnije, to je duža istočna strana pojasa, a dužina te strane je ono što priključku
  treba);
- **priključci** — nudi se **svaka** linija grida, a postojeći filtar zadržava one koje
  stanu: pola milje razmaka, van uspona i van spusta. Koliko ih ima zavisi od grada.

Demo scena (`rollCity`) sada vrti `rollCityPlan`/`rollCityEachPlay`, pa je **svaki Play
drugi grad i drugi autoput**. `citySeed > 0` pribija grad, da bi se isti mogao odigrati
sa autoputem i bez njega.

### 15.2 Šta se moralo promeniti da bi radilo na svakom gradu

1. **Oba kraja trase su gradske raskrsnice.** Trasa je pre izlazila u polje i tamo joj je
   trebala izmišljena ulica da umre na njoj. Te ulice niko nikad nije vozio: kola su ih
   napunila, okretala se u njima i zaključavala u njima — **4565 odbijenih koraka** na
   jednom takvom batrljku. Sada trasa izlazi iz grida na jednoj liniji, obiđe grad po
   pojasu i vrati se u grid na drugoj. Nema izmišljenih ulica.
2. **Grana više nije truba sa četiri slobodne rampe.** Dve od te četiri moraju na drugu
   stranu autoputa i **ukrštale su se međusobno u nivou, bez raskrsnice** — tri kola
   zaključana, **11.273 odbijanja**. Petlja u pravoj trubi postoji baš zato; pošto je
   plan (§14.2 t. 1) nije imao, grana je sada **dijamant čija arterija nastavlja napolje**,
   sa naplatnom rampom na pola puta. To je i ono što grad zaista radi.
3. **Uspon 4 % → 4,5 %** (322 → 287 m) i **razmak priključaka 850 → 800 m** (pola milje,
   urbani minimum iz priručnika). Uspon i spust zajedno pojedu ~730 m trase pre nego što
   ijedan priključak može da stane; na gradovima koje ova igra zaista kotrlja to je bila
   razlika između jednog i dva priključka.

### 15.3 Presuda na nasumičnim gradovima

Isti grad odigran **sa autoputem i bez njega** (`Tools/play/run.ps1`, batch, editor
zatvoren; `-Set ExpresswayDemoBuilder.citySeed=N,expressway=on|off`):

| grad | belt bez puta | belt sa putem | od toga **na autoputu** | deck | promene trake | naplata |
|---|---|---|---|---|---|---|
| 4242 | 767 | 650 | **0** | 98 | 47 | 7 |
| 777 | 796 | 661 | **0** | 54 | 25 | 9 |
| 31337 | 737 | 796 | **26** | 109 | 49 | — |

**Autoput ne košta ništa.** Svih ~700 odbijanja postoji i kad je isključen: sva su na
jednom mestu u gridu (0, 0) i to je **gradski zastoj, ne put** — vidi §15.4.

### 15.4 Šta ostaje otvoreno

- **Zastoj u gradu na (0,0)** — ~700 odbijenih koraka po runu, **i sa autoputem i bez
  njega**. Nije od ovog posla, ali je najveći pojedinačni kvar koji harness sada vidi.
- **22.569 grešaka po runu** u konzoli, isto sa i bez autoputa — takođe zatečeno.
- **Jedan priključak po gradu** na ovim veličinama: trasa je ~2400–2700 m, uspon i spust
  uzmu ~730, a dva priključka traže 340 + 800 + 400 = 1540. Veći grad ih dobije dva.
- `jump` ~50 i `steer` ~55 po runu — prelomi na spojevima deonica.
- Tally od 30 runova nije puštan do kraja (17 runova pre nego što je prekinut, i to na
  starijoj verziji koda).
- **Batch harness piše van projekta**: `-Out` pod `Temp/` Unity obriše pri gašenju, pa
  runovi idu u `%LOCALAPPDATA%\gangsters-play`. Isto važi za `Tools/play/tally.sh`.

### 15.5 Šta je prvi pogled u Play pokazao (2026-08-26)

Četiri primedbe sa slika iz igre, sve četiri iz istog uzroka: **oprema je postavljana po
osi trase i po merama koje trasa *obično* ima, umesto po geometriji komada puta koji je tu
zaista stajao.**

| šta se videlo | zašto | šta je sada |
|---|---|---|
| lampe lebde u vazduhu | stajale su na osi **trupa**, a tamo je između dva kolovoza 1.6 m čiste praznine (`DeckOff` 6.5 − `DeckHalf` 5.7) — stub u toj pukotini drži se ni za šta sedam metara iznad zemlje | na **decku**, na pojasu između ivičnjaka razdelnika i unutrašnje trake, naizmenično levi pa desni kolovoz; kobra ruka okrenuta preko kolovoza |
| putokaz i njegov nosač lebde | konzola je stajala **1.2 m izvan** ivice decka — dakle pored vijadukta, u vazduhu | na **bankini unutar parapeta** (`OuterShoulder`, koja se pomera sa širenjem decka za rampu), tabla obešena o ruku konzole nad kolovozom |
| „EXIT“ piše naopačke | `LookRotation(-facing)`: text mesh se čita **s leđa svog forward-a**, pa tabla okrenuta *ka* vozaču daje ogledalo | `LookRotation(facing)`; uz to legenda od tri reda se prelivala preko table → TMP `enableAutoSizing` u pravougaonik same table |
| naplatna pokriva zemlju između kolovoza | ruke i kućice su rađene po merama **autoputa** (2 trake ± 5.7 m od jedne ose), a barijera stoji na **arteriji** — dva kolovoza sa 15 m razdelnika između | po trakama te arterije (`LaneOffsets`): stub na ostrvu uz traku, ruka preko trake, kućica na razdelniku levo od vozača, i **apron** (popločan razdelnik) pod celom naplatnom |

Uz to: `TollGate` je imao **jednu** ruku, pa je na plazi sa više traka svaka osim poslednje
ostajala zauvek dole dok kola prolaze kroz nju — sada drži listu (`Arms`).

**Peta primedba, isti uzrok: „rampe se ne poklapaju s putem."** Rampa napušta deck **u
traci decka**: osa joj je na `AuxOff` = 6.65 m od ose kolovoza, a deck je tu širok
`AuxHalf` = 9.5 m. Ploča od 7.3 m položena od nosa je zato **6.5 m druge kolovozne ploče
NA prvoj**, na istoj visini (izmereno: `across 6.62, edge 9.49, gap −6.52, dy 0.00`), sa
sopstvenom ivičnom gredom (`Fascia` 0.5 m) koja visi kroz kolovoz. Rešenje nije pomerati
rampu nego joj **odseći ploču na ivicu decka**: `MeasureRampClips` prošeta svaku rampu i
zapamti (na svaka 4 m) koliko daleko preko njene ose seže deckov asfalt, pa je unutrašnja
ivica ploče `max(−RampHalf, ivica_decka − d + 5 cm)`. Tako ploča **jeste nos**: iverak od
0.7 m na račvanju koji se otvara u put kako se rampa odvaja, bez preklopa i bez trougla
rupe između njih. Unutrašnja linija se crta samo tamo gde unutrašnja ivica postoji (inače
bi ležala na deckovoj). `DeckMesh.Build` je dobio `maxStep` (rampe 8 m): korak se računa iz
poluprečnika krivine, a ovde se menja **širina**, o kojoj krivina ne zna ništa.

**Šesta, sedma i osma primedba (isti krug).**

| šta se videlo | zašto | šta je sada |
|---|---|---|
| „ovo uključenje ima nerealan stepen" | rampa počinje da pada **na samom nosu**: `GradeFrom` je bio 0, pa je posle 30 m već 32 cm ispod decka, posle 60 m metar i kvart — a tu je još uvek na deckovoj ploči, pa se ivična greda decka vidi kao stepenik kroz kolovoz | `LevelRampAlongDeck`: rampa **drži visinu decka dok god je uz njega** (prozor odsecanja iz `MeasureRampClips`, +12 m za vertikalnu krivinu), pa tek onda pada/penje se na dužini koja ostane (4.3–4.6 %, ispod dozvoljenih 6) |
| „ograda fali na autoputu" | `MeasureRampWindows` je u listu „otvori parapet" ubacivao i **pojaseve pomoćne trake**, a pomoćna traka počinje 340 m pre nosa i traje 400 m posle ulaza: trećina milje vijadukta sa svake strane svakog priključka stajala je bez ograde nad padom od sedam metara, zato što se traka proširila | u listi su **samo rampe**. Traka nije rampa — parapet se prosto pomeri napolje sa ivicom na kojoj stoji |
| „i dalje vidim ovaj zid dole" | to više nije bio zid protiv buke nego **ROW ograda**: plan je tražio žicu, a `SM_Env_Fence_01` je žica **na betonskom soklu**, i na svakoj razdaljini u ovom svetlu čita se samo sokl — drugi zid, u polju, duž puta sa kog niko nikoga ne ograđuje | i ona je izbačena. Pored ovog puta ne stoji ništa. Ako se ikad ograđuje, žicom (`SM_Prop_Fence_Wire_01`, panel 2.6 m, bez sokla) |

**I mapa.** `BuildPaint` je crtao crte **po tetivi** `A→B` svakog puta, a ne po njegovoj
liniji: na svakom luku trase i na svakoj rampi isprekidane crte su bežale sa kolovoza i
išle preko trave. Uz to je svakom jednosmernom kolovozu decka crtala **žutu srednju liniju**
(sredina autoputskog kolovoza je granica traka, ne osa dvosmerne ulice) i merila razmak
crta uličnom aritmetikom sa **pojasom za parkiranje**, kog na autoputu nema. Sada:
`Chords()` seče svaki put na komade od 20 m po njegovoj sopstvenoj liniji (isti pomoćnik
koji crta i asfalt), ritam crta se **prenosi** iz komada u komad (`Dashes` vraća fazu, pa
se crte ne gomilaju u krivini), bela ide između **stvarnih traka** tog kolovoza
(`RoadEdge.Offset`), žute na autoputu nema, a deonice decka se produže za 3 m sa svake
strane da se zatvori šav (4 m raskrsnice koje ne nosi nijedan kolovoz, pa ih nije crtalo
ništa). Usput: `side` u `BuildPaint` je pokazivao **levo** umesto desno — niko nije
primetio dok su svi ofseti dolazili u simetričnom paru, a autoputski ne dolaze.

### 15.6 Priključak na ulicu: „kakav je to spoj za put" (2026-08-26)

Peta primedba iz istog kruga, uz sliku odozgo: *„ovo izgleda jako glupo"*, pa
*„putevi treba budu smooth a ne ovo nesto samo na cosak 4 trake samo pretvoris u 2"*.

Izmereno u otvorenom editoru nad PORT dijamantom (unutrašnji terminal na
`(1293, 792)`, arterija duž Z, rampe duž X):

| šta stoji | mera |
|---|---|
| arterija | ulica, `StreetKit.RoadHalf` 5 → **10 m** |
| rampa | `RampHalf` 3.65 → **7.3 m** |
| kutija raskrsnice (`TermHalf*`) | x[1284..1302] × z[781..803] = **18 × 22 m** |
| popločano (`PaveFreewayJunction`, ćelija 5 m) | **20 × 25 m** |
| gola kaldrma ispred grla rampe | **4 m** |
| gola kaldrma pored grla, duž arterije | **7.35 m sa svake strane** |
| uglovi | **četiri prava, bez ijednog zaobljenja** |
| visina asfalta grada (`SM_Env_Road_Bare_01`) | ravan komad na **y = 0.000** |
| visina rampe na kraju (`GradeY`) | **0.12** → rampa stiže **preko ivičnjaka od 12 cm** |

To je to: pravougaonik duplo širi od svega što ga koristi, sa dve svetlije ploče
zabodene u bokove pod 90°, i promena širine **na ivičnjaku** umesto na putu. Nos
(gore) je istim pogledom bio čist — dakle greška je bila samo na drugom kraju rampe.

Popravljeno:

- **Grlo.** `XwRamp.Flare/MouthRun`: rampa se na poslednjih 80 m (ili 45 % svoje
  dužine, šta je manje — nos meri svoje sa druge strane i dva ne smeju da se sretnu)
  otvara sa 7.30 na **10.95 m**, `smoothstep` pa u ivičnjaku nema prelom gde
  proširenje počinje. To je tačno **jedna dodata traka od 3.65 m**, obeležena punom
  linijom na `-RampHalf` — dodata traka raste levo od nje, kako i raste u prirodi.
  Najstrmije 1:15, što je za skretačku traku na semaforu blago.
- **Uglovi.** Nova `JunctionApron`: raskrsnica se **crta** — širina arterije, širina
  grla rampe, i **zaobljenje od 10 m u sva četiri ugla** (koliko kamionu treba da
  uđe sa autoputa u arteriju bez točka preko suprotnog ivičnjaka). Obris je proveren
  računski pre Play-a: prost poligon, CCW, zvezdast oko centra (lepeza je dovoljna),
  653 m² za ulicu / 1518 m² za bulevar. Asfalt je materijal i UV **očitan sa
  `SM_Env_Road_Bare_01`**, pa je crtana raskrsnica isti asfalt kao popločana ulica.
  Leži 1 cm ispod oba puta, da se ne otimaju o istu površ.
- **Ivičnjak od 12 cm.** `ExpresswayLayout.StreetY = 0` (novo, uz `GradeY` koji
  ostaje 0.12 za trasu u polju): rampa se spušta na **gradsku nulu**, ne na
  autoputsku. Doc kod `GradeY` je i pisao „the city's own road surface" — a nije bio.
- **Zid na rampi.** `RampWalls`: parapet dok je rampa u vazduhu, **ivičnjak od 12 cm
  kad je na zemlji** (prelaz između 0.8 i 2.4 m visine). Metar betona pored puta na
  nivou ulice je zid u polju, a na grlu bi bio zid preko raskrsnice. Nikad nula —
  `DeckMesh.Section` zid nulte visine i dalje računa kao ivičnjak, pa bi rampa dobila
  35 cm zareza u ivici kolovoza na stanici gde se ugasi.
- **Oznake.** Unutrašnja ivična linija sad prati `Inner(s)` (širi se sa grlom),
  dodata je linija između dve trake grla i **stop-linija** preko grla — samo na
  rampi koja propušta (`Falling`).

**A onda: „druga boja puta, kvadrat pa putevi izlaze iz njega."** Zaobljenje i grlo
nisu bili dovoljni, i uzrok je bio dublji od geometrije — **rampa je do same
raskrsnice ostajala u boji mosta**. Izmereno iz atlasa, ne pretpostavljeno:

| šta | materijal | asfalt |
|---|---|---|
| ulica, gola ćelija (`SM_Env_Road_Bare_01`) | `Road_01` (PolygonCity) | **rgb(70, 69, 70)** |
| ulica sa oznakama (`SM_Env_Road_YellowLines_02`) | `Road_01` | **rgb(70, 69, 70)** — isto |
| deck autoputa (`SM_Env_Road_Highway_01`) | `Road_Grey_01` (PolygonPalmCity) | **rgb(118, 113, 107)** |

Dva paketa, dva atlasa. Rampa je ulazila u gradsku raskrsnicu **69 % svetlija i
toplija** od ulice koju spaja, sa šavom preko puta — pa se raskrsnica čitala kao
zakrpa iz koje izlaze tuđi putevi, bez obzira na to koliko su uglovi zaobljeni.

Pravi put menja površinu **na upornjaku** i nigde drugde. Sada isto:

- `DeckMesh.Flat(prefab)` — nova čitanka: materijal i jedna tačka atlasa sa ravnog
  komada gradskog kolovoza, pa je crtani put isti asfalt kao popločana ulica.
- `XwRamp.Abutment(h)` — stanica na kojoj rampa prolazi visinu `h`. Iznad je most i
  crta se `_xwSkin` (Synty autoput), ispod je put u gradu i crta se `_xwGround`
  (`Road_01`). `LayExpresswayRamps` crta rampu u **dva poteza** oko te stanice.
- `RampAbutment = 1 m` je ista visina na kojoj `RampWalls` spušta parapet u
  ivičnjak — jedna konstanta vodi i materijal i zid, pa most prestaje da bude most na
  jednom mestu, a ne na dva.
- `JunctionApron` više ne čita sam sebi asfalt: dobija `DeckMesh.Skin`, isti onaj
  kojim se crta i rampa na zemlji. Raskrsnica, rampa i ulica su time **jedna
  površina jedne boje**.
- `PierWorth = 3.5 m` (bila gola trojka u petlji) sada ima ime, jer je čita i objašnjenje
  upornjaka.

Ostaje neprovereno u Play-u: editor je za vreme rada dvaput prešao na tuđu scenu, pa
je ovo jedini krug čije brojke stoje samo iz atlasa i računice (obris raskrsnice,
taper grla, visine, boje), a ne sa slike.

Provereno u otvorenom editoru (`open_scene` → `editor_play` → `capture_game_view`), ne
računicom: znak „EXIT 1 / PORT / 1/2 MILE“ se čita sa kolovoza, lampe i konzole stoje na
asfaltu, četiri ruke naplatne stoje nad četiri trake. Mere komada (`Sign_Pole_04` konzola
6.0 m / visina 6.03, `LightPole_Base_01` 6.48 m sa sopstvenom rukom do 2.31,
`Barrier_Gate_01` ruka 4.30 m, kućica 3.2 × 3.2) očitane su iz projekta, ne pretpostavljene.
