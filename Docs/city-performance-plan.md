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
- **URP jede pola kulinga.** Konzola: `Camera.layerCullSpherical` radi samo u built-in-u; ostaje
  `layerCullDistances` (kutijasto, ne sferno).
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
