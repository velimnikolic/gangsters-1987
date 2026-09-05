# The Forge — generator rezidencijalnih blokova

Forge je deterministički generator jedne POLYGON City zgrade: dva reda radnji u
prizemlju, 3–5 spratova stanova, zatvoren krov i dekor preuzet iz izmerenih
rezidencijalnih prefaba. Čisti model pravi `ResidentialFacade.Sheet`, a isti list
`ResidentialBlocks.StandSheet` podiže i u editor showroomu i u runtime demo sceni.

Ovo nije novi gradski recept. Forge ne menja CoreDemo ili MiniCoreDemo, ne dodaje
zgrade u `ResidentialUnits.All`, ne menja `ResidentialHarvest.Sources` i ništa ne
bake-uje u `Assets/Prefabs/Residential`. Odluka gde će grad kasnije koristiti listove
ostaje zaseban posao.

## Izmereni izvori

`gangsters_module_atlas` meri module i piše
`Assets/RoadDemo/ResidentialModules.cs` i `Docs/residential-module-atlas.md`.
Konačni atlas ima 109 stavki: 34 strukturne i 75 dekorativnih, bez nerešenih
prefaba. Potvrdio je:

- ćeliju 5 m i sprat 3 m;
- sva tri `Apartment_Corner` modula kao spoljašnje uglove;
- roof parove `01→01`, `02→03`, `03→02` po istom izmerenom profilu;
- `FireEscape_03/02/01` kao bottom/middle/top;
- `Shop_03` kao dve ćelije, a sve ostale neuglaone radnje kao jednu fasadu.

`gangsters_decor_table` čita 14 požnjevenih zgrada i piše
`Assets/Resources/ResidentialDecor.json` i `Docs/residential-decor-table.md`.
`ResidentialDecor.cs` je mali tipski proveren loader, a CoreSim ugrađuje isti JSON.
Generator po importu invalidira katalog i izvedeni cache fasada. Od 1.272
direktna source propsa, 1.039 je vezano za potvrđeni host, 45 je imenovano kao
nevezano, a 188 je eksplicitno van cilja (među njima 60 požarnih stepenica na
ugaonim kolonama). Dobijena su 731 atomska template-a i 1.034 pravila, bez
`Unknown` hostova. Svako pravilo čuva stvarni host kind/style, lokalni XYZ/yaw i
potpisane column/row/floor pomake; grupni props se nikad ne svode na centroid.

Style težine 01/02/03 su `65/63/49`. Udeo požarnih stepenica, meren nad istim
unutrašnjim kolonama koje generator sme da koristi, jeste `0.25–1.0`. Svaki
family+anchor+relation bucket nosi zaseban izmereni min/mean/max; težina varijante
služi samo za izbor izgleda.

`RoofAccess` je nameran izuzetak između sirove tabele i generator policy-ja. Tabela
čuva izmerenih `0.125/0.165/0.25` nad svim krovnim ćelijama, dok ugovor epica traži
apsolutno `1–2` pristupa po zgradi. `Roll` i audit zato primenjuju `1–2`, ali i dalje
prijavljuju sirovi broj roof-cell kandidata umesto host-kompatibilnog imenitelja.

`WashingLine` je jedina nespremna porodica: nijedan od 26 primeraka nije prošao
strogi test da oba kraja izmerenog petometarskog mesha leže na attachment rootovima
dve susedne, podržane fire-escape kolone. Tabela ih sve imenuje, a Forge ih u ovoj
verziji izostavlja umesto da izmisli vezu.

## List i pravila

`ResidentialFacade.Roll(seed, length, floors, propsPercent)` je UnityEngine-free.
Prihvata `length=3..13`, `floors=3..5`, `propsPercent=0..200`, fiksnu dubinu 2 i
vraća:

- strukturne `Piece` zapise sa modulom, ćelijom, spratom i yaw-om;
- `Prop` zapise iz spremnih izmerenih template-a;
- sintetički `ResidentialUnit`, izveden iz istih komada;
- stabilan potpis `forge-L{L}-N{N}-{seed}` na podrazumevanih 100%, odnosno
  `forge-L{L}-N{N}-P{P}-{seed}` za ostale gustine, i presudu.

Stari overload bez procenta tačno je 100%. Procenat skaluje izmereni
`family min/mean/max` samo za opcioni dekor. `0%` zato uklanja opcione props-e, ali
namerno zadržava obavezne fire-escape lance i 1–2 krovna pristupa. Ti obavezni izbori
završavaju se pre bilo kog opcionog RNG izbora, pa isti seed ima istu konstrukciju i
isti obavezni skup na 0/100/200%. Višečlane varijante i dalje se biraju ili odbijaju
kao atomske grupe; slider nikad ne seče pojedinačne članove izabrane grupe. Ako
obavezni element zauzme anker, viša gustina se zasićuje na preostalom fizičkom
kapacitetu umesto da napravi sudar; rate audit i dalje odvojeno čuva sirovi izmereni
imenilac i traženi raspon.

`FamilyRate.Capacity` je u ovoj verziji egzaktan post-required broj za
`WindowFloor/Single` (Aircon je jedina opciona porodica sa nenultim minimumom). Za
shop hostove i atomske roof grupe podrazumevana vrednost je i dalje sirovi eligible
broj, ne tvrdnja o maksimalnom zajedničkom packingu više grupa.

Svaki dugi red ima tačno jedna apartment-door vrata. Prizemlje je ostatkom popunjeno
radnjama; `Shop_03` ne prelazi ugao, a `Shop_05` dobija doored suseda. Stil 01/02/03
bira se ponderisano po koloni. Stack pokriva prva tri stambena sprata, singlovi
ostatak, dok ugao uvek koristi po jedan corner komad na svakom spratu. Roof edge i
corner biraju se samo iz atlasom potvrđenog para.

Dekor se postavlja kao cela izmerena varijanta. Fire escape ponavlja samo potvrđeni
middle član za N=4/5; billboard, vent i terasa rezervišu svoj puni 2D roof footprint.
Window-floor i roof rezervacije sprečavaju sukobe, a nekompatibilan host ili
nepotpuna grupa odbijaju se. Billboard dodatno mora da ima sva četiri transformisana
donja ugla svog renderer footprint-a nad stvarnim `Roof`/`RoofCorner` renderer
bounds-ima generisanog lista; varijanta koja bi visila pored zgrade se odbija pre
izbora cele grupe.

Isti resolver sada ima i opšti fizički support gate. Fasadni član mora da dodiruje
stvarni transformisani shell renderer, dok centar donjeg footprint-a krovnog člana
mora ležati nad stvarnim `Roof`/`RoofCorner` rendererom na njegovoj visini. Podignuti
član atomske grupe sme da nasledi potporu samo kroz dodir sa već podržanim članom;
najniži roof članovi moraju imati sopstveno uporište. Time se čuva nameran overhang
fire-escape-a, tendi i znakova umesto da se sve silom zatvara u pravougaonik bloka.
Ground-rooted props na trotoaru ostaju dozvoljeni unutar jednog cell-a oko zgrade.

Fire-escape role se već rešavaju sprat po sprat, zato se njihov source-host Y delta
ne primenjuje drugi put. Potrošač ih normalizuje na izabrani sprat i proverava mrežu
`bottom@1`, `middle@2..N-1`, `top@N`; generator tabele takođe ubuduće emituje nulti
lokalni Y za te role. I resolver i `Judge` koriste fizičku proveru, pa dozvoljeni
envelope više ne može sam da opravda loš measured placement.

Presuda ima 14 tipova: `Hole`, `Double`, `CornerOff`, `RoofPair`,
`ShopAcrossCorner`, `LoneGlass`, `NoDoor`, `NoEscape`, `EscapeOrder`, `LineLoose`,
`Clash`, `OutOfBox`, `Empty` i `UnitMismatch`. Ona ponovo gradi dozvoljene pozicije
iz nezavisno izmerene tabele, proverava renderer box svakog propsa, celovitost grupa,
2D zauzeće i sintetičku jedinicu. `Empty` se prijavljuje; ništa se ne dopunjava
nagađanjem.

## Podizanje

`ResidentialBlocks.StandSheet` pre podizanja ponavlja presudu. Svaki modul i prop
prolazi kroz zajednički `Composer.Raise`, zatim kroz postojeći colourway,
`StorefrontDressingSteps` i `BuildingCutaway.Prepare`. Neuspeh ruši ceo nepotpuni
root; jedinica ulazi u `ResidentialUnits.Generated` tek kada je zgrada završena.
`ResidentialUnits.Known` je samo pogled `All + Generated`, tako da gradski dealer i
požnjeveni katalog ostaju nepromenjeni.

Unit bounds koriste tačan sin/cos AABB za izmereni proizvoljni yaw dekora; samo
strukturni quarter-turn put ostaje celobrojan. Stand audit meri trenutno aktivne i
uključene renderere, pa unapred pripremljeni ali ugašeni `Boards`/damage prikazi ne
glume geometriju netaknute zgrade.

## ForgeDemo

`Assets/Scenes/ForgeDemo.unity` je odvojena scena. U Play modu njen panel daje:

- seed, dužinu i broj spratova sa `−/+` kontrolama;
- veliki **OPTIONAL PROPS** slider od 0% do 200%, sa resetom na 100%;
- **GENERATE BLOCK** za jedan novi list;
- **GENERATE 12-SHEET GALLERY** za 4×3 pregled dužina `4, 8, 11, 13` i spratova
  `3, 4, 5`.

Svaki klik ponovo poziva centralni `Roll → StandSheet`; scena nema demo-only
generator. Caption i status pokazuju traženi procenat i stvarni broj postavljenih
propsa, uz mere, seed, bay-eve, broj delova i faults, a crvene pločice koriste stvarni
`ShopBays[].Door`. Nulta vrednost znači bez opcionog dekora, ne bez obaveznih
fire-escape/roof-access elemenata. Kamera je zajednički
`RoadDemo.DemoCamera`: WASD/strelice pomeraju, Q/E ili right-drag rotira, wheel
zumira; hint je vidljiv u sceni.

Editor meni `Tools/City/Residential/Forge Showroom` ponovo gradi celu scenu. CLI:

```text
unity command gangsters_forge --seed 7 --length 11 --floors 4 --json
unity command gangsters_forge --seed 1987 --props 150 --showroom --json
```

Druga komanda u Edit modu čuva 12-sheet galeriju. Nije Play test.

## Provera

```text
unity command gangsters_module_atlas --json
unity command gangsters_decor_table --json
unity command recompile --json
unity command recompile_status --json
unity command gangsters_forge_tests --json
unity command gangsters_forge --seed 7 --length 11 --floors 4 --json
unity command gangsters_forge --seed 1987 --showroom --json
```

Merodavni offline sweep je 30 seedova × 11 dužina × 3 visine = 990 listova.
U trenutnom Unity-bundled .NET 8 okruženju `CoreSim.csproj` cilja noviji framework,
pa je sweep izgrađen eksplicitnim `msbuild -p:TargetFramework=net8.0
-p:TargetFrameworks=net8.0`, a zatim pokrenut iz dobijenog DLL-a.

Konačni edit-mode rezultati 2026-09-04, posle billboard/props follow-upa:

- Unity compile: `completed`, `failed=false`, bez grešaka;
- atlas: 109/109 modula, 34 strukturna + 75 dekorativnih, 0 nerešenih;
- decor tabela: 731 template, 1.034 pravila, 0 `Unknown`; `WashingLine` jedina
  nespremna porodica;
- Forge contracts: `14/14` faultova i `26/26` imenovanih ugovora;
- CoreSim na svakoj od gustina 0/50/100/200%: `990/990` čisto i `990/990`
  identično ponavljanje, sa svim rate/policy gate-ovima unutra;
- billboard census na 990 podrazumevanih listova: 1.442 baze, 0 footprint-a sa makar
  jednim od četiri donja ugla bez krovne potpore;
- prethodni pojedinačni golden L11/N4/seed7 ostao je 109 modula + 131 props, 0 faults;
- problematični L8/N3/seed1988 daje 23/78/97 propsa na 0/100/200%, svaki sa 0
  faults; edit-mode capture na 100% pokazuje bilbord nad krovom;
- showroom na 100%: 12/12 listova, 2.362/2.362 podignutih delova, 192/192 shop bay-a,
  svi renderer bounds unutar `Unit`, 0 faults;
- business foundation: prošao;
- storefront audit: prošao (8 profila, 19 assets, 114 harvested door bay-a i 17
  prefabova);
- core-vacancy i dalje pada na 21 blok zbog paralelnih promena u odvojenom
  `CoreDistrict`/fire-station radu; Forge ne ulazi u taj plan.

Slider nije klikan u Play-u; njegov konačni UI/vizuelni sud ostaje korisniku.

### Floating-elements follow-up

Pre ispravke, nezavisni pure census nad 3.960 listova (30 seedova × 11 dužina ×
3 visine × P0/50/100/200) našao je 2.958 detached/sumnjivih placementa u 26
geometrijskih obrazaca, plus 10.216 pojavljivanja donjeg fire-escape člana na
`Y=-3`. U isto vreme 189.198 namernih renderer overhang placementa imalo je stvarni
kontakt, što je razlog da se proverava potpora umesto nominalnog block AABB-a.

Posle ispravke isti CoreSim opseg je `3.960/3.960` čist i deterministički, sa svim
rate/policy gate-ovima unutra. Pure regresije su `14/14` faultova i `30/30`
imenovanih ugovora; novi ugovori pokrivaju fire-escape floor lattice, stvarnu
RoofAccess potporu, detached verdict i očuvane ShopCover/FireEscape overhang-e.
Unity Play prikaz nije prekinut radi ove provere; novi live kadar treba napraviti
tek posle izlaska iz trenutno pauziranog Play moda i recompilea.
