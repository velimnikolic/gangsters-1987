# Autoput: prilazne rampe i pomoćne saobraćajnice (2026-08-19)

Zahtev: *„autoputevi nemaju rampe da se ljudi uključe na njih, ni isključe. na
ukrštanjima autoput ima samo kvadrat prazan"* i pojašnjenje: *„ne rampe no one
pomoćne saobraćajnice koje se uključuju na autoput koje treba budu povezane na
lokalni"*.

Dakle dve stvari: (1) autoput je bio zatvoren objekat — nadvožnjak koji prelazi ceo
grid na stubovima i silazi tek na svoja dva kraja, 166 m izvan poslednje raskrsnice,
pa se iz samog grada na njega nije moglo ni ući ni izaći; (2) na raskrsnicama
mrežnog autoputa (belt) stajao je go kvadrat.

Sve novo je u `Assets/RoadDemo/RoadDemoBuilder.Interchange.cs` (nov fajl), plus
sitne kuke u `Seams.cs`, `Belt.cs`, `RoadDemoBuilder.cs`, `LaneNet.cs`, `RoadCar.cs`.

---

## 1. Koridor: 30 m → 140 m

Poprečni presek, od ose koridora na obe strane:

```
 0        11.4    17.1        30            45   50   55        70
 |  deck   | žica | rampa gore | podnožje rampe | zelenilo | servisna | zelenilo |
 |<- 22.8 m dupli deck ->|     |<-- 11.4 m široka rampa -->|  |<- 10 ->|
```

Autoput koji se može koristiti nije samo deck: to je deck, po jedna rampa sa svake
strane, po jedna servisna saobraćajnica sa svake strane **tih**, i dovoljno mesta da
raskrsnica servisne i raskrsnica granične ulice ne sede jedna drugoj u krilu. To je
oko 150 m grada — i uvek je bilo toliko. Šav je zato `width = 140f`.

## 2. Pomoćne (servisne) saobraćajnice — `BuildFrontageLanes`

Po jedna kolovozna traka niz svaku stranu koridora, u nivou, uz spoljnu ivicu šava
tako da trotoar granične ulice služi i njoj. **To su obične dvosmerne ulice iz lane
grafa** i seku **svaku** ulicu grida koja prolazi ispod decka, u pravoj raskrsnici.
Koridor time postaje deo lokalne mreže umesto da je deli.

Da bi to bilo moguće, `BuildGraph` više ne polaže jedan kolovoz po segmentu grida
nego lanac kroz čvorove koji stoje u sredini segmenta (`LaneSegment`,
`_midSegment`). Ulica kroz koridor ide: raskrsnica grida → servisna (zapad) →
podnožje rampe (zapad) → podnožje rampe (istok) → servisna (istok) → raskrsnica grida.

## 3. Dijamant — `BuildSlipLanes` + `BuildSlipGeometry`

Na **jednom** poprečnom bulevaru (`PickInterchange`: bulevar najbliži sredini trase
koji ima 160 m mesta sa obe strane) — četiri jednosmerne rampe:

| rampa | deck | gore (odvajanje) |
|---|---|---|
| istok, izlaz | +u | pre raskrsnice |
| istok, ulaz | +u | posle raskrsnice |
| zapad, izlaz | −u | posle raskrsnice (jer taj deck ide unazad) |
| zapad, ulaz | −u | pre raskrsnice |

Svaka se penje 8.9 m na ~145 m (**6 %**) i sužava 12.9 m poprečno, pa kod gore-a
stoji **pored** autoputa a ne u njemu. Rampa je istovremeno i geometrija (isti
`SM_Env_Road_Highway_01` komadi, nagnuti i zaokrenuti pod ugao trase) i kolovoz u
grafu; ispod nje su stubovi, ali **nikad u kolovozu** (`InAnyRoad`).

## 4. Deck u lane grafu — `BuildDeckLanes`

Deck je sada **dva jednosmerna kolovoza** (`LaneNet.AddOneWay`, novo), a ne jedan sa
četiri trake — to je ono što dozvoljava da se rampa priključi na jedan od njih bez
da auto preseca drugi. Svaki prati profil trase preko `Carriageway.SurfaceAt`
(novo: visina duž puta, ne konstanta — `RoadCar.SurfaceLift` je čita).

Terminali su sada **čvorovi belt-a** (`HighwayEnd.Node`): belt je i pre lomio svoje
deckove oko T-pada, sada tu stoji raskrsnica. Postoji dakle put: lokalna ulica →
servisna → rampa → deck → drugi kraj → belt → bilo koji kvart, bez prolaska kroz
centar.

**Sopstveni saobraćaj decka (`HighwayTraffic`) je ukinut** — voze ga gradska kola iz
grafa. Ostaje samo kao rezerva kad nema belt-a (`!BeltOn`), jer tada trasa završava
na okretnici koja nije ni u kakvom grafu i auto bi tu zauvek stajao.

## 5. Prazni kvadrati — `PaveFreewayJunction`

Raskrsnice belt-a, sva četiri ćoška belt-a i oba terminalna pada autoputa polagani su
`BuildBlockFloor`-om, tj. **podom placa**: pločice pod nasumičnim uglom, pa ispucale,
zakrpljene i sa šahtovima, jer plac je ono za šta služi. Na kolovozu to izgleda tačno
onako kako je i prijavljeno — prazan kvadrat nasred puta.

Sada su to kolovozne pločice (`_bare`, ugao 0), zebra preko svakog kraka na koji
zaista stiže ulica, i ivičnjaci u ćoškovima gde se hoda.

## 6. Belt: rampe na svakom ukrštanju (2026-08-19, drugi krug)

Ponovljena primedba: *„autoputevi nemaju pomoćne putiće da se uključiš na njih"*.
Nadvožnjak ih je dobio (tačke 2–3), ali **belt** — prsten oko celog ostrva, jedini
autoput kojim se stvarno vozi između kvartova — imao je samo četiri gola ukrštanja u
nivou: staješ na autoputu da bi skrenuo s njega.

Sada svako ukrštanje belta ima **četiri jednosmerne rampe**, po jednu u svaki kvadrant,
koje nose sva četiri desna skretanja mimo raskrsnice (`RoadDemoBuilder.Belt.cs`,
`BuildBeltSlips`). Levo skretanje i pravo i dalje idu kroz kutiju; desna je ne diraju.

```
                        gore (na beltu, 80 m od raskrsnice)
                       /
   ulica ----o--------+---------- belt
           foot      /|
        (46 m)      / | raskrsnica
```

- **gore** — čvor na beltu, 80 m gore i dole od raskrsnice, širok koliko i kolovoz;
  rampa se odvaja iz **spoljašnje trake** (`BeltOuterLane = 8.3`).
- **foot** — čvor na poprečnoj ulici, 46 m sa svake strane belta; rampa sleće u
  sopstvenu traku ulice (`BeltStreetLane = 2.5`).
- Ulica kvarta više nije dva segmenta oko belta nego **lanac**: lice grida → bliže
  stopalo → raskrsnica belta → dalje stopalo → kapija kvarta (`Districts.cs`,
  `WeldRoads`).
- Rampa je od istih Highway deck ploča od kojih je i belt, u nivou (`LayRampDeck`).

Geometrija nije proizvoljna: rampa leži na **23.9°** od ose belta, i to je namerno
ispod 30°. `LaneNet.Prepare` odbacuje manevar kada je `dot(a.Dir, b.Dir) < -0.5`, a za
par izlaz/ulaz istog kvadranta taj dot je `-cos(2θ)`; iznad 30° bi graf dozvolio da
auto siđe sa belta i odmah se vrati na njega kao da je to polukružno okretanje.

Rampe dobija ukrštanje koje ima mesta **i na beltu** (94 m do najbližeg suseda, ćoška
ili terminala autoputa) **i na ulici** (kvart mora stajati bar 218 m od grida, pa je
`CityLayout` podigao `strip` sa 190–200 na 250–330 m). Gde nema mesta, to ukrštanje
zadržava običnu raskrsnicu i to kaže u logu.

---

## Provera

`python Docs/freeway.py` — **28 provera, 0 padova**: da li presek staje u koridor,
da li se kutije čvorova preklapaju, nagib i sužavanje rampe, da li `PickInterchange`
uopšte nađe ulicu i da li joj gore padaju na ravan deo decka, i da li je negde ostao
stari pod placa.

**U Play-u još neviđeno.**

## Šta NIJE urađeno

- Pešački graf ne zna za servisne saobraćajnice (niko ne hoda uz koridor).
- Semafori se prave samo za čvorove grida, pa raskrsnice u koridoru rade na
  „propusti onoga ko je već u kutiji" (`CanEnter` bez signala) — što je za priključenje
  na rampu ispravno, ali za bulevar ispod decka nije idealno.
- Dijamant je na jednoj ulici po koridoru. Na ovom gridu više i ne može: koridor je
  dug ~640 m, rampi treba 160 m sa svake strane ulice, pa su dozvoljeni centri samo u
  pojasu od 300 m — a dva dijamanta bi tražila 320 m razmaka. Petlja u
  `PlanInterchange` ima smisla tek na dužem koridoru.
- Rampe belta nisu u pešačkom grafu (niko ih ne prelazi peške), i nemaju trougao
  ostrva između rampe i raskrsnice.
