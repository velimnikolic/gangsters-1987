# Pending review findings — otvoreni nalazi iz nezavisne revizije

Izvor: `Tools/review/adversarial-claude/review.sh` nad zamrznutim delta-om od 41 fajla
(sesija `01a076af`, 2026-09-06 15:43 UTC). Verdikt: **NEEDS ATTENTION**.

Sesija koja je pokrenula reviziju prekinuta je pre nego što je videla rezultat, pa ova
dva nalaza **nisu zatvorena**. Naredna sesija koja radi na CoreDemo/ostrvu treba da ih
obradi. Bez Editora — offline/read-only provere su dovoljne za oba.

---

## 1. Forest batch bounds pokrivaju celo ostrvo — frustum culling, distance culling i shadow gate su svi no-op

- **Fajl:** `Assets/RoadDemo/IslandForest.cs`
- **Linije:** 65–96 (placement/batching), 110–124 (`LateUpdate`)
- **Confidence:** 0.87

Nalaz iz prve revizije ("hiljade retkih batcheva") zatvoren je punjenjem batcheva do 1023 —
ali je redosled punjenja *uniformno random preko `land.Bounds`* (linije 67–68), a AABB svakog
batcha raste `Encapsulate`-om preko sopstvenih članova (linija 95). Uzastopna postavljanja za
dati `Part` raspoređena su preko celog ostrva, pa `Bounds` svakog batcha konvergira na pun
pravougaonik ostrva (≈9 × 7 km, jer je `IslandLandform.Bounds` = region ±(radius+500)).

Posledice u `LateUpdate`:

- `batch.Bounds.SqrDistance(at)` vraća **0** za bilo koju kameru unutar ostrva → `> 3400²`
  reject nikad ne opali.
- `GeometryUtility.TestPlanesAABB` uvek prolazi.
- `distance < 650f * 650f` je zato uvek true → **svaki batch se šalje sa
  `ShadowCastingMode.On`, svaki frame**, u main passu i ponovo po shadow kaskadi.

Prefabi potvrđuju razmeru: `SM_Gen_Env_Tree_*` / `SM_Gen_Env_Rock_*` nose po jedan `MeshFilter`,
dakle ~16 partova × ~1023 = ~22.000 instanciranih mešova bezuslovno crtano i sa senkama, na
projektu čije sopstvene beleške evidentiraju izostanak LOD-a i postojeći problem od 99M vertexa.

Log linija na 106–107 meri `placed`, `submitted` i mean instances/batch — sve to izgleda
*zdravo* upravo zato što su batchevi veličine ostrva, pa je ova regresija nevidljiva iz
prikupljenih dokaza.

Dokumentacija (`Docs/core-region.md`) tvrdi "culled GPU instances" i "limits distant shadows".
Ne važi ni jedno.

**Predloženi fix:** ključ batcha na `(Part, spatialCell)` — bucketuj postavljanja u ćelije od
npr. 250 m i puni do 1023 *unutar ćelije* — tako batchevi ostaju veliki, a njihovi AABB-ovi
lokalni. Zatim loguj mean batch AABB extent pored broja instanci, da se sledeća pojava ovoga
može detektovati bez Editora.

---

## 2. Airport clearance oblikuje i proverava samo final/downwind/base — crosswind leg izostavljen

- **Fajlovi:** `Assets/RoadDemo/IslandAirfield.cs` (linije 22–38),
  `Tools/IslandSim/Program.cs` (linije 76–93)
- **Confidence:** 0.62

`FlightOps` leti četiri departure/circuit lega, ne tri. Clearance geometrija i offline sempling
pokrivaju final, downwind i base; **crosswind leg nije ni oblikovan ni sempliran**, pa teren i
vegetacija na toj deonici nisu pod kontrolom visine.

**Predloženi fix:** proširiti i oblikovanje koridora i regresionu proveru na crosswind leg,
istim pravilom rezerve koje već važi za ostala tri.
