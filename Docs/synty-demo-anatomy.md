# Anatomija Synty PolygonCity demo scene

> Izmereno 2026-08-25 parsiranjem originalne scene iz git-a
> (`e9dbc9d2:Assets/Synty/PolygonCity/Scenes/Demo.unity`, 5179 prefab-instanci). Ovo je
> građa za gradsko jezgro v2: blokovi u `Assets/Prefabs/CoreBlocks` su požnjeveni iz ove
> scene i ovde piše kako su ih Synty artisti složili i kakvi su im putevi. Plan koji na
> tome gradi: `Docs/core-district-plan.md`.

## 1. Skelet: ostrvo, jedan glavni put, tri poprečne, uličice

Demo je ostrvo ~265 × 320 m (`SM_Env_WaterEdge_*` oko x −112..152, z −160..160) sa dva
mosta (`SM_Env_Bridge_*`, x ≈ −150 i x ≈ 145) preko kojih glavni put ide dalje.

| put | položaj | širina | profil (pločice preko puta) |
|---|---|---|---|
| **glavni put** istok–zapad | z −10..0, x −155..195 | 10 m | `Road_02@90 \| Road_02@270` — dve trake, bez srednje linije |
| ulica S1 sever–jug | x −75..−65, z −110..−10 | 10 m | `Road_02@180 \| Road_02@0` |
| ulica S2 sever–jug (centralna) | x −5..5, z −70..−10; x −10..5, z 0..135 | 10 / **15** m | jug 2×`Road_02`; sever z 50..105: `Road_02 \| YellowLines_02 \| YellowLines_01` — tri trake, jedina žuta linija u sceni |
| ulica S3 sever–jug | x 90..100, z −70..−10; x 90..105, z 0..100; x 95..105, z 110..135 | 10 / 15 / 10 m | 2×`Road_02` |
| ulice istok–zapad | z −80..−70 (x −65..90); z −120..−110 (x −65..−5); z 95..105 (x 5..40); z 135..145 (x 5..95) | 10 m | 2×`Road_02` (jedan segment `Patch_01`) |
| **uličice**, jednosmerne | x −100..−95, x −75..−70, x −45..−40, x 60..65, x 135..140; z 65..70, z 100..110, z −50..−45, z −145..−140 | **5 m** | `Road_Bare_01` + `Road_Arrow_01/02` na ušću |

Pločice puta u sceni: `Road_02` 390, `Road_Bare_01` 190, `Road_Crossing_01` 43,
`Road_Lines_02` 31, `Road_Patch_01` 21, `Road_ParkingLines_01` 20, `Street_Divider_02` 16,
`YellowLines_01/02` po 12, `Road_Arrow_01/02` 11+10, `Street_Divider_01` 6, `Road_03` 4.
Svi pivoti na višekratnicima 5 m (faza 0), y = 0. Devet pločica ima scale x = −1 (Synty
mirroruje simetrične pločice; mi to ne radimo).

### Raskrsnice
- **Kutija golog asfalta** (`Road_Bare_01`, 2×2 ćelije na ukrštanju dve ulice od 10 m).
- **Zebre** samo na prilazima glavnim raskrsnicama (43 pločice, po dve preko 10 m ulice).
- `Road_Lines_02` uz raskrsnice = trake za skretanje; `Road_Patch_01` = dekorativne zakrpe.
- **Semafori** na četiri raskrsnice: (0,−5) i (95,−5) na glavnom putu, (0,−75), (97,107) —
  po četiri stuba `LightPole_Base + Arm + Lights + CrossLights + CrossButton + Box`.
- **Give-way** znaci (20) na ušćima uličica i sporednih ulica; bolardi (`SidewalkPoles`) na
  uglovima; rampe na ivičnjaku (`Sidewalk_Dip_Corner` 50, `Sidewalk_Dip` 15) na prelazima.
- `Street_Divider_01/02` (22): kratki fizički razdelnici na sredini 10 m ulice na tri mesta
  (x −70 z −52..−23; z −75 x −57..−14; z −5 x 23..52) — pešačka ostrva, ne bulevar.

### Osvetljenje i vozila
- 86 stubova lampi, razmak najčešće **12 m**, 16–20 m na sporednim; na 28 stubova kablovi
  (`Power_Cables`).
- 177 vozila (grupa `Road_Cars`): 86 stoje na `Road_02` (parkirana **u traci uz ivičnjak** —
  Synty nema parking-traku; igra ju je zato uvela, `RoadDemoBuilder.ParkLane`), 21 na golom
  asfaltu uličica, 13 na `ParkingLines`.

## 2. Blokovi

- Blok = **ivičnjak od jedne pločice (5 m)** `Sidewalk_Straight` oko cele kutije,
  `Sidewalk_Corner` na uglovima, `Dip` na prelazima; unutra `Sidewalk_01` kao plato/dvorište,
  trava (`Grass_01`) u dvorištu velikog stambenog bloka i u parkiću, **parkinzi**
  (`Road_ParkingLines_01`, 10×5 m, tri mesta u redu, + `Bare`) u usecima blokova i uz obalu.
- Put je samo kolovoz između ivičnjaka; trotoar pripada bloku. Tačno tako su blokovi i
  požnjeveni.
### 2.1 Trotoar — tačna pravila (izmereno na svih 16 blokova, 2026-08-25)

Prebrojano na požnjevenim prefabima: 410 ivičnjaka, 590 ravnih ploča, 74 ugla, 13 slivnika.
Ovo je recept koji `RoadDemo.CorePavement` sada izvodi.

| pitanje | odgovor iz merenja |
|---|---|
| širina trotoara | **jedna pločica, 5 m.** Od 350 ivičnjaka iza kojih uopšte ima zgrade, **275 ih ima pročelje tačno 5 m unutra** — fasada stoji na unutrašnjoj ivici ivičnjaka. Širi pojasevi (10, 25, 40 m) su dvorište, park ili parking, nikad trotoar |
| ivičnjak | `SM_Env_Sidewalk_Straight_01`, okrenut **NAPOLJE**: sever 0, istok 90, jug 180, zapad 270. Svih 410 se slaže |
| ugao | ugaona pločica obavija **jedan ugao svoje ćelije**, i koji je to ugao — to je ceo propis: **SI 0, JI 90, JZ 180, SZ 270**. Obični ugao (`Corner_01`, 15 kom.) i **unutrašnji** ugao (`Corner_02`, 11 kom.) dele istu tablicu; `Dip_Corner_01` (rampa za prelaz, 48 kom. = tri četvrtine svih uglova) je ista tablica **zaokrenuta za četvrt**: SI 90, JI 180, JZ 270, SZ 0. Svih 74 se slaže |
| unutrašnji ugao | ćelija kojoj su sve četiri strane u bloku, a **dijagonala fali** → `Corner_02`, ugao po dijagonali koja fali |
| pod unutar ivičnjaka | `SM_Env_Sidewalk_01`; u **11 od 16** blokova položen i **ispod zgrada** — zato se kroz blok nikad ne vidi |
| slivnik | `Sidewalk_Gutter_01` u nizu ivičnjaka, jedan na 13–40 pločica |

#### Props na trotoaru — DVE TRAKE i ništa između

Izmereno tako što je svaki prop koji stoji na nekoj od 511 ivičnjak-pločica preveden u
**okvir samog ivičnjaka**: „across" = metara unutra od ivice ka kolovozu, „turn" = zaokret u
odnosu na to kuda ivičnjak gleda.

| traka | across | šta stoji | okret |
|---|---|---|---|
| **uz ivičnjak** | **1.0 m** | `LightPole_Base_01` (64 od 70 na tačno 1 m), `Sign_GiveWay_03`, `ParkingMeter_01` | lampa gleda NAPOLJE — krak od 2.5 m visi nad kolovozom |
| | **1.5 m** | `SidewalkPoles_01` (bolardi, red od 3.2 m, centriran u ćeliji), `Trashbin_01`, `Hydrant_01`, `Mailbox_01` | kanta/hidrant bilo kako; bolardi paralelno sa ivičnjakom |
| **uz zid** | **4.0–4.5 m** | `ParkBench_01`, `Newspaper_02` | gledaju napolje |
| (na zidu) | 5.0 m | `PlanterWindow_01/02`, `SatDish_01`, `SecurityCamera`, `Poster_Frame`, `Sign_Attachment` | to je **zgrada, ne trotoar** — generator ih ne dira |

Ritam: **lampa svakih 20 m** po nizu ivičnjaka (21 od 38 njihovih razmaka je 20 m, još 9 je
25 m) i **prva ćeliju od ćoška, nikad na ćošku**; dve trećine lampi stoji na ŠAVU između
pločica (na višekratniku 5 m). Bolardi ćeliju od ćoška, ~1.9 reda po bloku. Zatim: kanta na
16 ivičnjak-pločica, hidrant i poštansko sanduče na 46, klupa na 85, novinski kiosk na 102.
Svi propovi na **y = 0**.

`LightPole_Base_01` je 6.5 m i JESTE cela ulična lampa (mast + krak); 19 od 70 nose i
semaforski pribor (`Arm + Lights_01/02 + CrossLights + CrossButton + Box`) — to su semafori,
ne lampe, i idu kroz `TrafficSignal`, ne kroz blok.


- Zgrade su **modularne**: `Base` + N × `Floor`/`Stack` + `Roof` kao deca base-a. Spratnost
  2–5 (Apartment, OfficeSquare, OfficeOctagon, Shop), 7–12 (OfficeOld_Large/Small =
  tornjevi). Tornjevi: 12 spr. (60,120), 10 spr. (128,118), (120,27), (43,−31), 8 spr.
  (−64,36) — gomilaju se oko centralne raskrsnice glavnog puta i S2, bez strogog gradijenta.
- Pročelja uz ivičnjak gledaju na ulicu; zadnja strana bloka je usek: parking, dvorište ili
  **manji blok sa uličicom**. Blok-07 (35×35) stoji u useku bloka-06 sa uličicom od 5 m
  između; blok-10 (55×35) stoji u useku bloka-12 sa parkingom između.
- **Prazan prostor ne postoji**: svaki usek je parking sa linijama, dvorište, ili blok + uličica.

### Naši prefabi = Synty blokovi (100 % poklapanje komada)

| prefab | pivot (x, z) | kutija u demou | šta je |
|---|---|---|---|
| block-01 | (−90, −55) | x −100..−75, z −100..−10 | 30×90, toranj |
| block-02 | (−85, 15) | x −90..−75, z 0..35 | 15×35, nisko |
| block-03 | (−55, −95) | x −65..−40, z −110..−80 | 25×30, gradska kuća |
| city-hall-block | (−51, −97) | isto mesto kao block-03 | duplikat sa tray-a, 21 komad — koristi se block-03 |
| block-04 | (−50, −120) | x −95..−5, z −140..−105 | 90×35 L, obalne radnje |
| block-05 | (−40, 35) | x −70..−5, z 0..65 | 65×65, kancelarije 8 spr. |
| block-06 | (−35, −40) | x −65..−5, z −70..−10 | 60×60 L, stambeni |
| block-07 | (−20, −30) | x −40..−5, z −45..−10 | 35×35, u useku bloka-06 |
| block-08 | (−20, −95) | x −35..−5, z −110..−80 | 30×30 park |
| block-09 | (35, −40) | x 5..60, z −70..−10 | 55×60, kancelarije 10 spr. |
| block-10 | (35, 15) | x 5..60, z 0..35 | 55×35 L, u useku bloka-12 |
| block-11 | (50, 120) | x 5..95, z 105..135 | 90×30 L, stanica + 12 spr. |
| block-12 | (50, 50) | x 5..95, z 0..100 | 90×100 stepenice, stambeni + dvorište |
| block-13 | (75, −40) | x 65..90, z −70..−10 | 25×60, toranj |
| block-14 | (115, −20) | x 100..130, z −30..−10 | 30×25 |
| block-15 | (120, 125) | x 105..135, z 110..140 | 30×30, 10 spr. |
| block-16 | (120, 55) | x 100..145, z 0..105 | 45×105 L, istočni platoi |

(kutija = raspon pivota komada; rotacija svih 0°.)

## 3. Pravila koja iz ovoga slede

1. Jedan **glavni put** kroz ceo kvart; ostale ulice se kače na njega. Sve pravo, sve pod
   pravim uglom; T-raskrsnice su normalne (S1 i ulice z −75 / z −115 se završavaju na drugoj).
2. **Dve širine**: ulica (10 m, dvosmerna) i uličica (5 m, jednosmerna, goli asfalt + strelica).
3. **Blok nosi svoj trotoar**; put je samo kolovoz između ivičnjaka.
4. **Nema praznog prostora**: usek = parking / dvorište / manji blok + uličica.
5. Raskrsnica = gola kutija; zebre i semafori samo na glavnim, give-way na sporednim,
   bolardi i rampe na uglovima.
6. Visoko uz glavnu raskrsnicu, nisko ka obali.
7. Parkiranje uz ivičnjak u traci — u igri to rešava parking-traka ulice od 15 m.

## Dodatak — ASCII mapa originala (sever gore, 5 m po znaku, x od −155 na levom kraju)

```
legend: r road lane  Y/y yellow line  l lane line  a arrow  x zebra  . bare asphalt  P parking
        S pavement  k kerb tile  c kerb corner  s other pavement  G grass  W water  ~ bridge
        B building base  # building >= 4 floors   (blank inside a block = ground under a building)
  160                                   WWW WW WW             WW
  155                            WWWWWsWWWWWWWWWsWW  WWW WW WWWWWW
  150                           WWSSSsS kkkkkkkk SW WWWWWWWWWWWWWWW
  145                           WWSckkk  P P P P kppkkkkkkkkkskkkcW
  140                          WWWSk..rrrrrrrrrrrrpprrla.. l.....kW
  135                          WWWSk..rrrrrrrrrrrrrrrrrr..akkkkc.kW
  130                           WWSsxxckkkkkkkkskkkkkkkcrrkSSSSk.kWW
  125                           WWSpprkBSSSSSS#SBSBSBSSkrrkSSSSs.kWW
  120                           WWspprkSSSSSSSSSS#SSSSSkrrkSSSSk.kW
  115                           WW  rrkSSSSSSSSSSSSSSSSBrrkSSS#k.kW
  110                            WkrrrkBBSSSSSSS SSckkacxxckkppc.kW
  105                            WkrrrckkkkBskkkkskc.l x..al..p..kW
  100                           WWkrYyalrrrrxrrrrrr..lax..ckkkkkkcWWW
   95                           WWkrYy lrrrrxrrrrrr..ckcxxsS  SSSSWWW
   90                           WWkrYyakkBkkc......ppk krrkSSBSSSSW
   85                            WkrYyk B BskP.....pPk  rrkSSSSSSSW
   80                WWW WW WWW   krYykBB  Bkp.......kSsrrkSSSBSSBWW
   75               WWWWWWWWWWWWWWkrYykBB BBk.P P P .kSkrrkSSSSSSSWW
   70              WWckkkskkkkkkkkcrYykBBBBBBsBBBBBBBBBkrrkSSSSsSSWWW
   65              WWk...........larYykBB BBB     B  B  ..kSSSSSSSWWW
   60              WWk.ckkkkkk#skkBrYysBB BBBBBBBBBBB B..  SSSSSSSWW
   55              WWk.kSSSSSSSSSSprYykB BBBSSSSSSSSB B..kBSSSSSSSWW
   50               Wk.kSSSSSSSSSSprYyk B BS  G GG SB BrrkSSSSSWWWW
   45               Wk.kBSSSSSSSSSkrYykB BBSGGGGGGGSB BrrkSSSSSWWW
   40                k.kSSSSSSSSSSkrrrckkskcGG GGGGSB BrrkSSSSSWW
   35             WWWk.B#SSSSSSSSSkrrr l..ppkskkskcSB BrrkSSSSSW
   30            WWSSk.kSSSSsSSBSSs rraBkBpp P P PkS  BrrkSSSSSWW
   25            WWSBk.kSSSSSSSSSS krrkB B..pp....ksBBBrrkSSS#SWW
   20         ~~  WSSk.kSSSSSSSSSSSkrrkBBBP ppP .pppSSkrrkSSSSSWW
   15         ~~  WSBk.kSSSSSSSSSSSkrrkB BBBBBBBBppp SkrrkSSSSSWW
   10         ~~ WWSBk.kSSSSSS SSSSkllk BBB B   k.kS skrrkS#SSSWWW
    5         ~~WWWSSklkSBSSSs SSSSBllkBBBBBBBBBBlksSSkrrkSSSSSWWW
    0 kkkkkkkkkkkkkkkc akkkppkkkkkkcxxckBkBskBkkcackppcxxckkkkkkkkkkkkkkkkkk
   -5 rrrrrrrrrrrrrrrr..rrrrprrrrllx..xllrrrrrrrx.rrrrx..rrrrrrrrrrrrrrrrrrr
  -10 rrrrrrrrrrrrrrrr..rrrrrrrrrllx..xalrrrrrrrx.rrrrx..rrrrrrrrrrrrrrrrrrr
  -15 kkkkkkkkkkkkkkkcxxckka cBBBBkBxxckkkBkkkkka ckkkBxxckkkBkkkkkkkkkkkkkk
  -20        WWWSSSSSkrrkBsBlkBBBBBkalkSSSSSSSSBklkSSSkrrkSSSSS~~
  -25        WWWSSSSSkrrkBBk.k BBB BllkSSSSSSSSSk.kSSSkrrkSSSSS~~
  -30         WWSSBSS rrkB B.kBBB BBrrkSSSSSSSSBk.kSSSkrrkSSSSS~~
  -35         WWSSBSSsprkB B.cscB BBrrkSSBSSS#SSk.k#SSpprkSSSSS~~
  -40          WSSSSSkrrkB B...kBBBBprkSSSSSSSSSk.kSSSprrkWWWWWW
  -45          WSSSS#krrkB B...ckkkpprkSSSSsSSS k.kBSSBrrkWWW
  -50          WS#SSSkrrkBBBP ....larrkSSSSSSSSSk.kSSSkrrkWW
  -55          WSSSSSkrrkB BBBBBBBBBrrkSSSSSSBSSk.kSSSkrrkW
  -60         WWSSSSSBrrs B   B    krrkSSSSSSSSSk.kSSSkrrkW
  -65         WWSSSSSkrrkBBBBBBBBB BrrkBSBSSSSSSklkBSSkrrsWW
  -70         WWSSSSSkrrckskkkkkkskcxxckkkkkkkkkcackkkcrrkWW
  -75          WSSBSSk..xrrrrrrrrrrx..xrrrrrrrrrrrrrrrr..kWW
  -80          WSSSSSk..xrrrrrrrrrrx..xrrrrrrrrrrrrrppr..kWW
  -85         WWSSSSSkrrckska ckkkpppxckkkkkkkkkkkkkkkkkkcW
  -90         WWSS#SSkrrkSSSk.kGGGppprkWWWWWWWWWWWWWWWWWWWW
  -95        WWWSSSSSkrrkSBSkxk  GGkrrs WWWWWW    WWWWWWW
 -100        WWWckkkkarrkSSSk.kG G krrkW  WW
 -105         WWk...l rrkSSSp.kGGG krrkW
 -110         WWk.cBkBrrckkkcackskkcrrkWW
 -115          Wk.kB B..xrrrrrrrrrrx..kWW
 -120          Wk.kBBB..xrrrrrrrrrrx..kW
 -125          Wk.kB BBBBBBB skkkkkcap s
 -130          Wk.k BB SB  S SBSBBSklkSWWW
 -135         WWk.kBBBBBBBBBSSSSSSSk.kSWWW
 -140         WWk.ckkkkkkppkkkkkkkkc.kSWW
 -145         WWk........p...........kSWW
 -150          Wckkk P P P P  kkkkkkkcSW
 -155          WSSSS kkkkkkkk SSSSSsSSSW
 -160          WWWWWsWWWWWWWWWsWWWWWWWWW
```
