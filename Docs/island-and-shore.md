# Ostrvo, obala, reka i pod ispod puta (2026-08-19)

Četiri primedbe iz Play-a u `Game.unity`:

1. *„luka je nekako usečena u zemlju pa imamo oštre ivice oko nje koje idu pod 90
   stepeni u more"*
2. *„povećaj ostrvo 3x"*
3. *„obrati pažnju kuda ide reka jer ne možeš da roknes stvari preko reke ako nije
   most"*
4. *„neki delovi puta su ispod poda pa se vidi ono presecanje kad idem levo desno"*

(Peta — rampe na autoput — je u `Docs/freeway-interchange.md`, tačka 6.)

Provera: `python Docs/island.py -v` — **37 provera, 0 padova**.

---

## 1. Luka usečena u zemlju

Luka rezerviše **bazen** kao pravougaonik (`HarborDistrict.Reserve` → `into.Sea`):
voda koju brod traži, i nikakav oblik. Ostrvo ga je čitalo kao „unutra ili napolje":

```csharp
if (_reservations.InWater(x, z)) return SeabedY;   // -5 m
```

Trava na jednoj strani linije, dno mora na drugoj — dakle **vertikalni zid od 5+ m
duž celog pravougaonika**, a taj pravougaonik ide 900 m u pučinu i 260 m sa svake
strane luke. To su te ivice pod 90°.

Sada (`District.cs` `WaterAt`, `Island.cs` `IslandHeight`):

- težina bazena je 1 unutra i **pada na 0 preko `ShoreBlend = 80 m`** izvan
  pravougaonika → isti pravougaonik čita se kao **zaliv sa plažom** (nagib 1:12);
- **brda se sklanjaju od bazena** preko `BasinFade = 220 m` — luka stoji u podnožju
  kopna, a ne na vrhu litice od 26 m koja bi u zaliv pala preko jednog ramena;
- bazen **nikad ne diže** teren (`Min`), i **ne vuče pod vodu tuđe ravno tlo**:
  `DryHeight` vraća `held` (koliko čvrsto neki kvart/put drži teren na svom nivou), a
  težina bazena se njime prigušuje — betonski plato luke ostaje beton, plaža počinje
  tamo gde se plato završava.

Uz to, dve stvari koje su pravile **rupe** oko svakog kvarta:

- ćelija heightfield-a odbacivala se po **centru**, pa je ćelija koja upola viri iz
  popločanog pravougaonika odnosila i do 5 m terena sa sobom — venac praznog vazduha
  oko luke i predgrađa kroz koji se vidi more. Sada se odbacuje samo ćelija koja je
  **cela** unutra (`InPaved(x, z, GroundStep * 0.5f)`), pa teren radije prolazi malo
  *ispod* pločica kvarta.
- predgrađe je držalo ostrvo ravnim na `0f` — tačno u ravni sa svojim travnjacima.
  Sada na `RoadBed` (v. tačku 4).

## 2. Ostrvo 3x

`islandWest/East/North/South` 460/380/360/420 → **1380/1140/1080/1260 m**,
`coastWander` 90 → 240.

Šta je moralo uz to:

- **Teren u pločama.** Bio je jedan mesh preko celog ostrva: jedan renderer koji se
  uvek crta, uvek je u shadow pass-u i ne može se kuliti po ćošku. Sada
  `GroundTileSpan = 480 m` po ploči, svaka sa svojom pozicijom u svetu (temena su
  lokalna), pa merge i distance culling rade (`ScenePerf` seče na 120 m).
- `GroundStep` 8 → **10 m**: plaža i dalje ima pet redova kvadova, a heightfield je
  pola miliona temena umesto 800 hiljada.
- **Divljina se proređuje sa udaljenošću** (`NearWild 340 → FarWild 1250 m`, na
  `22%`). Ista gustina svuda bila bi ~80 000 stabala, od kojih 70 000 iza magle i iza
  cull distance.
- **Bazen se otvara na pučinu sam.** Luka rezerviše 1150 m vode jer ne zna koliko je
  ostrvo široko; na ovom ostrvu obala ide i do 1668 m. Zato `OpenBasinsToSea` (u
  `Island.cs`, jedino mesto koje zna gde mu je obala) gura morski kraj svakog bazena
  dok ne prođe obalu — inače je luka ribnjak nasred livade.
- `farClipPlane` 3200 → **8000** (poslednji klik mape diže kameru ~5.3 km), a
  `DemoMap.BuildLand` skalira svoju ćeliju sa površinom ostrva — na 20 m bi bilo 90
  000 uzoraka i daleka polovina zemlje prosto ne bi bila nacrtana.

## 3. Reka

Reka izlazi iz grida i ide kanalom do mora (`IslandHeight` je useca do `SeabedY + 1`,
sa obalama od `RiverBank = 14 m`). Preko tog kanala su se do sada polagale stvari
kao da ga nema:

- **Belt je bio brana.** Prsten ide oko celog ostrva, dakle preseca **svaki** izlaz
  reke, a celo svoje rame je držao ravnim (`Level(r, 0f)`) — nasip trave sa putem na
  vrhu, preko reke. Sada `BuildBeltSide` deli tu rezervaciju oko kanala
  (`ClearOfRivers`, `RiverClear = 26 m` čiste obale), pa kanal zadrži svoje korito, a
  belt ga prelazi kao **most**: deckovi su i pre išli pravo preko (`LayBeltDecks` ih
  lomi samo za pad), a sada ispod njih stoji i podgled, nosači i stubovi na obalama —
  iz istog PolygonCity kompleta kojim su dressovani gradski mostovi preko iste reke
  (`BeltRiverBridge`).
- **Kvartovi su sedali preko kanala.** Pravilo „ništa gde reka izlazi iz grida" bilo
  je provereno samo na **liniji za koju je kvart zakačen**. Predgrađe od osam blokova
  je 720 m obale, a njegovi pinovi su negde u sredini toga — pa je ležalo tačno preko
  kanala: kuće u vodi, ulice preko reke bez mosta, i tlo pod kvartom držano ravnim,
  što je reku uz to i zagatilo. Sada `CityLayout.Grid.Rivers` daje pojaseve reka po
  obali, a `Clashes` ih tretira kao zauzeto (`RiverGap = 45 m`).

Popravke koje ovo *ne* pokriva: nema mosta ako se ručno u inspektoru zakači kvart
preko reke — samo se izgubi (roll ga preskoči).

## 4. Put ispod poda

Ostrvo je držalo tlo ispod svakog puta koji nije samo popločalo **tačno na `0f`** —
a pločice puta se polažu na `y = 0`. Dve površine u istoj ravni se u depth bufferu
tuku, pa put probija kroz travu u trakama koje se pomeraju sa kamerom: tačno „vidi se
ono presecanje kad idem levo desno".

Sada `RoadDemoBuilder.RoadBed = -0.06 m` — šest centimetara ispod asfalta — svuda gde
je stajalo `Level(r, 0f)`:

| gde | šta stoji gore |
|---|---|
| `Belt.cs` | rame belta i njegovi padovi |
| `Districts.cs` `ReserveCorridor` | ulice do kvartova (najvidljivije) |
| `Seams.cs` | spust autoputa, repovi, terminalni pad, priključna cesta |
| `Wayside.cs` | plato pumpe |
| `SuburbDistrict.cs` | prsten oko predgrađa |

Šest centimetara: dovoljno da razdvoji ravni, a manje od 10 cm koliko je visok
ivičnjak trotoara, pa se stepenik na ramenu ne vidi.
